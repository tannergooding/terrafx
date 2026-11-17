# M05 aggregate attachment projected-membership addendum

## 1. Disposition and observed gap

**Tentative implementation recommendation, pending owner review; not implementation or qualification acceptance.** This corrects only the membership-source and sequencing requirements in sections 5.5–5.6 of [the aggregate attachment proposal](m05-animation-aggregate-attachment-closure.md), reviewed at SHA-256 `96B7692FDB3CA6C4F8ECFA9668E7B50943493EB7E519B5F181DDA2C0D54C5BD4`. Keep its schemas, field tree, budgets, source/restore leases, and other owner boundaries unchanged.

The proposed signature `IWorldAttachmentValidationContext.ValidateEntityId(Guid)` is sufficient for a field reader, but its proposed **issuer and membership source are not yet sufficient**:

| Current evidence under `sources\TerraFX.Runtime\Entities` | Consequence |
|---|---|
| `World.TryGetEntity(Guid)` calls `EnsureWorldReadAccess`; that guard rejects attachment callbacks and ordinary reads during Structural. | Calling it from the validation context is misuse, not an implementation of private projected access. Keep the guard. |
| `World.Attachments.cs`, `TryResolveAttachmentEntityReference`, avoids that public guard but reads only the installed identity ledger, live slot, and storage location. `ValidationContext.ValidateEntityId` then calls the existing live-handle validator. | A same-transaction creation is absent from both installed maps. Even a new GUID-to-handle overlay would still fail if followed by the old unconditional `GetLiveArchetype` check. |
| `World.Commands.cs:1434–1437` calls final reference preflight before identity preparation at lines 1449–1526. The call is currently conditional on destruction. | Moving Animation preparation before that call alone cannot supply new stable IDs; addition-only and component-only changes also need the selected membership pass. |
| `StableIdentityLedger.PreparedChange.GetAssignedId/GetAssignedOrdinal`, lines 774–787, already expose the real prepared assignments before Commit. `Prepare` rejects duplicate/reused identities. | Reuse these exact assignments. No second UUID computation, early identity commit, guessed slot generation, or caller-provided identity set is necessary. |

The `_activeCommandProjectedEntityHandles`/`_activeCommandExcludedEntityHandles` sets support handle-valued component checks; they are not a complete stable-ID membership source. No existing proposal defines the missing sealed GUID overlay.

## 2. One lower-owned validation source

Add one internal Entities-owned capability; private root/control bookkeeping is omitted:

```csharp
internal sealed class WorldAttachmentMembershipSource
{
    private WorldAttachmentMembershipSource();
    internal static WorldAttachmentMembershipSource CaptureCurrent(World world);
    internal static WorldAttachmentMembershipSource CaptureProjected(World world);
    internal void RequireWorld(World world);
    internal void ValidateEntityId(Guid entityId, Entity? excludedEntity,
        IReadOnlySet<Entity>? excludedEntities);
    internal void ValidateEntity(Entity entity, Entity? excludedEntity,
        IReadOnlySet<Entity>? excludedEntities);
    internal void ValidateForCommit();
    internal void Revoke();
}
```

Only the actual World dispatcher issues a source, **before entering participant/provider callbacks**, under its already-admitted operation. Capture is not permitted merely because a World is idle or its GUID matches. The dispatcher must have registered its exact protected validation operation or sealed command-preparation root; captures during callbacks, from an unfinished projection, or outside that issuance window reject.

The source captures exact World object/construction ownership, owner thread, containing-operation and validation-subphase identity, protected base membership, and, for Projected, the same sealed command root described below. Two subphases of one Step do not share issuance authority. It owns no World, asset, or publication authority. No constructor/factory accepts a dictionary, GUID list, EntityToken list, guessed epoch, or caller-selected exclusion policy. Null/default is unissued. There is no public source getter or general membership service.

Current means the actual installed membership protected by the containing operation, **not necessarily a published frame**. It supports ordinary attachment installation/validation and the real initialized restore candidate after its complete Entity map exists. It uses private ledger/slot checks, not `World.TryGetEntity`. CaptureCurrent rejects when that operation requires projected membership; it cannot be a fallback for an unavailable projection.

Projected resolves exactly:

```text
all protected live Entity assignments at preparation entry
minus this complete batch's retired Entity assignments
plus this complete batch's surviving prepared Entity creations
```

The issuer builds a private new-GUID-to-planned-Entity overlay and retirement set from the **actual** command/identity preparations. Unchanged survivors may remain borrowed from the protected base ledger/slots; a second whole-World GUID index is unnecessary. The source covers all Entities, including nonplayers used as motion actors/targets, not just the Animation row set.

| Queried target | Required source behavior |
|---|---|
| Surviving prepared creation | Match its actual prepared GUID/ordinal, exact World owner, and sealed predicted slot/generation. Do **not** require an allocated slot, live storage location, or committed ledger entry yet. |
| Untouched or modified surviving existing Entity | Match the protected base assignment and actual slot/generation; apply the complete batch's removals and any additional existing exclusion arguments. |
| Destroyed Entity, canceled token creation, unknown/retired ID, or SceneInstance ID | Reject as an inadmissible live Entity reference. A SceneInstance creation is not an Entity. |
| Foreign/default/stale-generation Entity handle | Reject, even if its slot index equals a planned creation's index. |

Exclusion arguments come from the existing lower dispatch and can only further restrict membership. ValidateEntityId resolves privately and applies the same exclusions as ValidateEntity; neither method returns a handle. Both use the selected source's predicate. Do not resolve a planned GUID and then call the old installed-only attachment validator. Historical root-request identities are not routed through this live predicate.

## 3. Bind every context and membership read to that source

Replace the four internal `IWorldAttachmentRegistration` validation entries with:

```csharp
ImmutableArray<Diagnostic> Validate(World world, object value,
    WorldAttachmentMembershipSource membership);
void ValidateEntityReferences(World world, object value,
    WorldAttachmentMembershipSource membership);
void ValidateEntityReferences(World world, object value, Entity excludedEntity,
    WorldAttachmentMembershipSource membership);
void ValidateEntityReferences(World world, object value,
    IReadOnlySet<Entity> excludedEntities, WorldAttachmentMembershipSource membership);
```

The existing table/World validation wrappers forward the containing operation's mandatory source. `WorldAttachmentRegistration<T,TAuthor>.ValidationContext` captures it alongside its exact binding, operation value, and exclusions, and checks RequireWorld before field/provider dispatch. Its ValidateEntityId and ValidateEntity delegate to that source and preserve failures through the context's existing RecordFailure path, including when a provider catches them. The public field/provider/binding signatures do not change; a provider cannot replace the source.

The World dispatcher also binds the **same sealed preparation root** into its already-proposed AnimationMembership phase grant. Its component count/read methods enumerate final projected player membership, not live queries:

- Include untouched existing players, surviving touched players, and surviving creations with the selected player component; omit destroyed Entities and final player-component removals.
- Read the final shadow initialization for changed/new values; otherwise copy the protected existing column value through its typed registration. Include final same-batch component changes, not their earlier values.
- Return copied values and qualified identities in unsigned RFC EntityId order. Derive creation facts from each actual assignment's immutable branch-directory entry, not the current branch guessed for every row.

The projection contains the component rows and membership overlay together; independent enumerations over different epochs are not interchangeable. No borrowed column, mutable projection, source object, or EntityToken resolver escapes to Animation. Existing phase allowlists still forbid Scene reads/writes, I/O, and resource/control/event/result mutation during membership preparation. Membership validity alone grants no Scene-target/channel ownership.

All source operations check active operation/thread/World/root ownership before operands. Contexts retain their existing exact-family/value checks and first-failure ledger. Caught active misuse remains sticky. Wrong or absent projected source is a protocol failure, never an empty set or a current-World retry. Revoke callback bindings in `finally`; the owning dispatcher likewise revokes Current/Projected sources in `finally` after dependent callbacks unwind, on success or failure. Revoked use only throws and cannot fault a successor.

## 4. Corrected preparation and commit order

For an Animation-selected Structural batch:

1. **Finish the command shadow.** Seal the accepted commands, token cancellation, hierarchy destruction/detachment, complete component/tag edits, and prepared content prerequisites. Early shape checks remain; do not run the aggregate's authoritative reference pass against a partial command prefix.
2. **Prepare identities without publishing them.** Reserve and prepare final slot capacity and the actual `StableIdentityLedger.PreparedChange`. Validate the candidate. For each surviving creation at zero-based created-projection index `i`, use `GetAssignedId(i + sceneAssignmentOffset)` and `GetAssignedOrdinal(i + sceneAssignmentOffset)`, where `sceneAssignmentOffset` is one exactly when this actual batch prepends a SceneInstance assignment. Capture its branch index from that exact assignment and its final predicted slot/generation. Never treat the optional Scene assignment as the first Entity.
3. **Seal the read source.** Fund and build the overlay, retirement set, and complete selected player rows/copies. Reconcile handle sets used by ordinary component reference validation with these same final predictions. If predictions changed, rebuild their dependent shadow references before sealing or reject; do not silently retarget them. After sealing, no command, assignment, cancellation, row, or component value may change.
4. **Prepare the replacement.** Issue CaptureProjected and the actual membership grant; prepare the complete replacement Animation attachment/derived ownership from this source and already-prepared assets. Do not first validate the old Animation aggregate against removals: removing its player rows is the purpose of this preparation. Other surviving attachments are still validated; only actual destroyed Entity owners are omitted under existing rules.
5. **Validate the complete proposal.** Move required final reference preflight here, after identity preparation and replacement preparation. Attachment checks use this source; component checks use its matching handle projection. Always validate the prepared aggregate, including addition-only and player-component-only deltas, not only destruction. Preserve existing required destruction/surviving-reference coverage; a projected source alone does not require an additional whole-World scan of unaffected columns. Callback application code cannot mutate the frozen plan.
6. **Close preflight before the first write.** Finish all capacity/release/coexistence preparation and ordinary cancellation checks. After every callback unwinds, ValidateForCommit rechecks the exact protected source/plan, the existing prepared-ledger check, and final slot predictions. Revoke callbacks/source before the first hierarchy/slot/column/attachment/identity write—not merely before the later identity-ledger Commit.
7. **Install or abort.** Install the already-prepared ECS/Scene/Animation changes under the existing callback/allocation-free commit. Failure before commit preserves prior structural storage/attachment; abort prepared ownership and identity changes under their actual credits. After commit, ordinary post-Structural validation uses a **fresh Current source** and actual installed maps. Earlier tick writes retain existing nonrollback semantics.

A component-only delta still has a sealed projected source even without a new ledger candidate. A selected empty-delta membership invocation likewise receives a stamped source; do not take the existing empty-command fast return before satisfying that selected invocation. Neither case needs a fake assignment, receipt, mutation, or hidden tick. An unselected empty batch keeps its old fast path.

The existing Scene/owner-control `CommitCommandBatch` routes use the same source when validating their already-authorized prepared Animation edit. This does not invent a simulation Step/phase for owner control or authorize an unimplemented Scene/player producer. A route needing projected validation cannot substitute Current until that producer/dispatch is complete.

Source controls, overlay/retirement backing, copied player rows, and retained preparation roots stay within existing command/Animation/World/domain peak reservations, alongside old/new graphs and cleanup. Reserve before allocation/callback; count maxima promise no fit. Revocation ends read authority, not ownership of still-used preparation storage. No new independent-use credit or budget is created.

## 5. Distinguishing example and focused evidence

In one genuine transaction, create player P with explicit ID `11111111-1111-4111-8111-111111111111` and nonplayer actor A with ID `22222222-2222-4222-8222-222222222222`; prepare P's valid attachment row referencing both. Neither exists in the installed ledger before commit. The sealed source must validate both and report P in projected player rows. Public `TryGetEntity` remains forbidden inside the callback. After commit, ordinary lookup resolves the same actual assignments. Removing A in that same final shadow while leaving P's reference must reject before any structural installation.

Required observations when implemented, not executed results:

- Exercise that positive addition-only transaction and its removal-refusal variant through the real selected producer. Include a reference appearing only in a later player/array element.
- Repeat with actual automatic assignments and a prepended SceneInstance assignment; verify the exact identity-fixture ordinal/UUID expectations and Entity-versus-Scene kind, not a second local UUID generator.
- Change/add/remove a player component on a surviving Entity and verify final copied values, complete row counts/order, untouched players, and valid nonplayer targets.
- Cancel a token, retire a target, and attempt a stale-generation or foreign-source reference. No missing projected entry may fall back to installed membership. Wrong source/caught misuse rejects the containing operation.
- Keep ordinary idle attachment and genuine candidate-restore validation working through Current; retain callback nonreentrancy, revoked-binding behavior, and historical request restoration after player destruction.

Use ordinary owning-project Debug then Release tests with nonzero selections when implementing. No test-only source issuer or fabricated future handle is an observation.

**Excluded:** schema/public API changes, another identity allocator, global GUID membership service, early publication, relaxed callback guards, mutable canonical state, Scene/provenance or asset-lease redesign, replay/Physics feedback, native/AOT qualification, and milestone acceptance. Only this addendum is written.
