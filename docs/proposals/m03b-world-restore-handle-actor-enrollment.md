# M03-B World restore handle-actor enrollment

**Status: tentative rectification for later review.** Proceed under the user's tentative-unblocker instruction, but do not infer implementation qualification or permission to bypass another producer gate. This proposal changes no maintained contract, implementation, test, roadmap, public API, schema, or acceptance identifier.

## 1. Finding and decision

The restore lifecycle is substantially specified and a Standard Branch implementation exists. **Its candidate Entity handle-resolution actor is not contract-closed.** Chapter 30 conditions candidate handle materialization on enrollment of the restore actor; Chapter 29's exact helper-caller inventory omits the existing `World.ResolveRestoreEntity(Guid)`. Internal accessibility and existing NUnit execution paths cannot supply that missing semantic permission.

Select the existing candidate-owned resolver, not a new `World.IssueEntity` API. Enroll its one direct handle-helper edge and the exact existing resolver callers listed below. Preserve the already specified candidate-construction, capture, replacement, ownership, and publication contracts. This is a narrow actor-inventory correction, not approval of the entire current load implementation.

### Confirmed evidence

Paths are repository-relative; line numbers describe the frozen inputs in section 7.

| Boundary | Current evidence and consequence |
|---|---|
| Required enrollment | `docs\design\30-public-api-review-surface.md:3208,3229-3237` requires the candidate's own handle helper **after its restore actor is enrolled**, exact signatures/caller edges, and no generated-helper or type-wide exemption. |
| Missing exact caller | `docs\design\29-project-setup-build-and-deployment.md:236-259,276-286` lists the permitted `CreateEntityHandle` callers and limits permission to declared exact actors. `ResolveRestoreEntity` is absent. |
| Unclosed name, not a missing public feature | `docs\design\27-state-serialization-replay-and-recovery.md:223` says to construct the candidate map using `World.IssueEntity`, but Chapter 30 supplies no corresponding declaration and current C# symbol lookup finds no such member. Treat that phrase as requiring reconciliation with the existing admitted-map/materialization mechanism, not authorization to invent another issuer. |
| Existing implementation | `sources\TerraFX.Runtime\Entities\World.PersistenceRestore.cs:15-38` validates a nonempty stable ID, candidate state, owner thread, live Entity locator and generation, then calls the receiver's private `CreateEntityHandle`. It does not allocate an identity or call the `Entity` constructor directly. |
| Map construction precedes resolution | `sources\TerraFX.Runtime\Entities\World.cs:206-243,447-489` consumes the admitted restore state, slot table and identity ledger before initializing the candidate. The resolver is used during restoration, not as a postpublication live-World lookup route. |
| Real higher orchestration exists | `sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs:349-399` constructs/initializes the candidate, prepares Scene restoration, restores ECS and Scene state, then clock/controls. `sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.cs:71-114` performs fresh validation, remap/playback/marker preparation and replacement. This shows implementation, not enrollment. |
| Current supported selection is narrower than the design | `sources\TerraFX.Runtime\Hosting\WorldPersistence.cs:709-757` admits only Standard Branch preparation. The resolver's generation-1 assumption is not StrictLocal reconstruction evidence. |

## 2. Existing decisions retained, not duplicated

- [The runtime-port proposal](m05-owner-contract-closure-runtime-ports.md), section 3, already specifies the M05 restored join, ordering, prepared assets and prohibition on hidden ticks or canonical repair.
- [The typed restore-source addendum](m05-owner-contract-closure-runtime-ports-addendum.md), section 2, already specifies the completed-tick/source-owner handoff and six-argument `RestoreEcsRows` entry. It does **not** enroll `ResolveRestoreEntity` as a handle-helper caller.
- [The Scene workspace proposal](m03b-scene-preparation-workspace-closure.md), section 7, and [the resource-bundle closure proposal](m03b-resource-bundle-preconstruction-owner-and-return-closure.md), section 5, already identify the original Persistence seed/root needed before Scene measurement. Their funding and actual-return obligations remain open; this document assigns no substitute amount or owner.
- The full existing proposal set was searched for the resolver, `IssueEntity`, handle-helper permissions and restore-actor enrollment. No existing proposal closes this exact missing edge. Section 7 freezes that set.

There is also **implementation divergence from already explicit contracts**, not another missing decision to resolve here: Chapters 29-30 assign `WorldReplacementOperation` to Entities and restrict the candidate factory's caller, whereas the current class is in Hosting and `WorldLoadPreparedState.CreateRestoredCandidate` directly calls that factory. The current capture/execute signatures also differ from Chapter 30's declared chain (`WorldPreparedReplacement.TryCaptureCommit`, `WorldPersistence.CommitLoadAtOwnerBoundary`, and `WorldReplacementOperation.ExecuteAtOwnerBoundary`). Preserve those authoritative requirements and align implementation with them in its own bounded change. Do not silently authorize the current higher-layer factory caller, relocate Scene/Assets orchestration into Entities, or add a reverse dependency through this enrollment.

## 3. Exact proposed authorization

All actors below belong to `TerraFX.Runtime`. Names identify existing members, not new declarations or a serialized policy grammar. Parameter and return types are by value unless shown otherwise; all methods are nongeneric.

### Candidate-owned materialization

Enroll this existing **instance** member:

```csharp
// TerraFX.Entities.World; existing internal member
Entity ResolveRestoreEntity(Guid stableId);
```

Its sole new sensitive-call permission is:

```text
TerraFX.Entities.World.ResolveRestoreEntity(Guid): Entity
    -> the same candidate receiver's
       TerraFX.Entities.World.CreateEntityHandle(uint, uint): Entity
```

It receives no direct constructor, owner allocator, stable-identity issuance, callable-acquisition, or public lookup permission. `CreateEntityHandle` remains the sole `Entity(uint,uint,uint)` constructor caller.

The receiver must be the exact initialized, unpublished restore candidate produced from the accepted construction plan. Its complete admitted live Entity map must already exist, although component/system/Scene restoration need not yet be complete. Validate the existing owner-thread, candidate, liveness and locator rules before materialization. A missing/retired/wrong-kind identity cannot mint a slot or fall back to another World, even if GUID bits match. Bind every returned handle to the candidate's owner and admitted slot/generation.

This enrollment initially covers only the existing Standard Branch selection. It does not authorize applying the current generation-1 implementation to StrictLocal, RecordedContinuation or IndependentFork.

### Exact direct callers of the resolver

Permit only these existing production members to call `ResolveRestoreEntity` on that candidate:

| Exact existing caller | Narrow purpose |
|---|---|
| Instance `void TerraFX.Entities.World.RestoreEcsRows(ReadOnlyMemory<byte>, WorldPersistenceEcsContents, long, ImmutableArray<IWorldPreparedResourceSource>, ImmutableArray<IWorldPreparedAttachmentSource>, CancellationToken)` | Materialize receiving Entities while restoring the already validated ECS projection. |
| Instance `Entity TerraFX.Entities.World.WorldComponentRestoreContext.ResolveEntity(ComponentFieldDescriptor, Guid)` | Resolve an exact declared component reference within the active candidate callback scope. |
| Instance `Entity? TerraFX.Entities.World.WorldComponentRestoreContext.ResolveOptionalEntity(ComponentFieldDescriptor, Guid)` | Materialize an existing optional target after the declared optional-reference check; its absent-target branch does not call the resolver. |
| Instance `Entity TerraFX.Entities.World.WorldSystemRestoreContext.ResolveEntity(WorldSystemFieldDescriptor, Guid)` | Resolve an exact declared system-state reference within that system's active candidate scope. |
| Instance `Entity TerraFX.Entities.World.WorldAttachmentRestoreContext.ResolveEntity(WorldAttachmentFieldDescriptor, Guid)` | Resolve an exact declared attachment reference within that attachment's active candidate scope. |
| Static `ImmutableArray<SceneExternalBinding> TerraFX.Scenes.ScenePersistenceRestorer.ReadExternalBindings(World, EbinRecordReadView)` | Rebind validated saved external targets to the candidate before restored Scene preparation consumes them. |
| Static `void TerraFX.Scenes.ScenePersistenceRestorer.RestoreHierarchy(World, SceneWorldRegistry?, EbinRecordReadView, int, CancellationToken)` | Rebind validated hierarchy nodes/parents before the candidate's existing hierarchy transaction. |

The three contexts are existing private nested World types; their CLR metadata names use `World+WorldComponentRestoreContext`, `World+WorldSystemRestoreContext` and `World+WorldAttachmentRestoreContext`. Their public interface implementations do not make those types or permissions public APIs.

This set is independently derived from current C# references: ten production invocation sites across seven exact members. The forwarding four-argument `RestoreEcsRows` overload is not a direct resolver caller. Friend-test calls are not production actors. Neither another overload nor another partial/generated method inherits permission.

Application providers continue through their existing scoped bindings; they receive no World/resolver/delegate capability. Preserve field/family validation, callback affinity, sticky failures and `finally` revocation. This proposal adds no authority to read component columns from system/attachment Restore.

## 4. Owner changes and distinguishing behavior

The subsequent contract change should make these decisions explicit together:

1. **Persistence owner, Chapter 27:** replace the undeclared `World.IssueEntity` wording with consumption of the admitted complete candidate map and candidate-owned handle materialization. Do not reset history, rerun fresh construction, allocate replacement semantic IDs, or change the Persistent-before-Derived barrier.
2. **Entities/layering owner, Chapter 29:** add the exact resolver-to-helper edge and the seven-member caller set. Retain the existing factory/capture/owner-issuer requirements; their current implementation divergence remains implementation work.
3. **Declaration/actor owner, Chapter 30:** enroll the existing resolver and cross-reference the complete caller set. Preserve exact-signature/direct-call versus callable-acquisition distinctions. Introduce no public member or general `World.*` authorization.
4. **Hosting and Scenes implementers:** use the exact captured candidate flow and the existing higher-owned restoration operations. They cannot reconstruct permissions from plan GUID getters or grant a source/provider direct handle construction.

For example, a checkpoint contains live Entities A and B, a persistent A-to-B reference, a Scene external target B and a hierarchy edge to B. The old World and candidate can have the **same WorldGuid and stable GUIDs but different process owners**. Every restored reference must resolve to B's candidate-owned handle; reusing the old B handle is invalid. The proposed edge permits that operation. Merely granting the whole World type or the provider access to `CreateEntityHandle` would additionally permit unrelated handle fabrication and is rejected.

No example depends on new schema fields, acceptance IDs, particular numeric owner IDs, or a fabricated snapshot.

## 5. Implementation order and required evidence

| Owner / dependency | Next action and evidence |
|---|---|
| Entities and Persistence contract owners | First reconcile Chapters 27, 29 and 30 as one exact actor/caller decision using section 3. The current task does not edit them. This is the next dependency-ready step under tentative approval, not a request for new API design. |
| Existing construction/replacement owners | Align the real captured-operation/factory/candidate call chain with the already specified lower-owned authority before claiming full restore-actor closure. Keep higher Assets/Scenes/Animation work behind its legitimate existing owner ports. No broad exemption or new callback route closes this work. |
| Runtime tests | Retain and extend the existing restore tests only where behavior/coverage is missing. `WorldPersistenceIdentityHistoryReaderTests.CreatesReplacementConstructionPlanFromAdmittedBranchHistory` already checks candidate-owner/index/generation and missing/empty ID resolution. `WorldPersistenceRegistrationCaptureTests` covers heterogeneous ECS/Derived/registered owner state, attachment Entity references and empty-candidate system Restore. These test bodies were inspected, not rerun here. |
| Public replacement witness | Exercise production Save → PrepareLoad → CommitLoad, not only friend-created candidates. Existing `EngineHostPersistenceCommitTests.CommitsPreparedBranchForStandaloneWorld` and `CommitsPreparedBranchAndAdoptsReplacementAtHostBoundaryAsync` supply the owning entry paths. Verify same-GUID/fresh-owner references, no extra tick, fresh canonical comparison, source-bound remap and separately reported old retirement. Exercise the selected component/system/attachment and Scene reference routes through actual registered owners. |
| Refusal and scoped use | Cover absent/retired references according to required versus optional field contracts, wrong kind/foreign scope, expired or caught callback misuse, stale target before/after issuance, and capture-first versus wrapper/domain closure. Expected preflight refusal, late callback faults and postpublication retirement failures retain their distinct existing channels. Do not infer semantic actor enforcement from same-assembly accessibility or friend tests. |
| Validation / acceptance owner | For a later implementation, run the smallest owning Runtime Debug selection, then Release where applicable; record nonzero selected/executed counts. Separately review the exact call inventory and absence of additional constructor/helper callers. No new architecture-policy schema/analyzer, test-only issuer, or generic qualification runner is required. Native/AOT/public-consumer and milestone acceptance retain Chapter 35's separate gates. |

## 6. Non-goals and remaining gates

- No source change, maintained-contract edit, test edit, commit, push or PR is part of this proposal task. No build/test execution or passing-result claim is made.
- Do not enable StrictLocal, continuation, fork, migration, live M05 animation/particles, new Scene producers or a public `IssueEntity` API.
- Do not change identity history, State2 bytes/schema versions, canonical ordering, callback semantics, cancellation, owner issuance, admission ceilings, interest transfers, cleanup/quarantine, or publication/retirement boundaries.
- Do not treat actor enrollment as allocation/lifetime qualification. Existing Persistence seed/root, Scene planner/bundle funding and producer/invoker-return work remains owned by the proposals in section 2.
- Preserve the current fail-closed paths: `ScenePersistenceRestorer.MeasureRestoredScenePlanStorageBytesAsync` returns null for a nonempty Scene composition with a CPU resource preparer (`ScenePersistenceRestorer.cs:74-77`); `PreparedScene.QualifiedFootprint` remains null. `SceneAssetStoreTests.RestorePlanWithCpuExtractionRefusesBeforePackageExpansion`, in `tests\TerraFX.Runtime.Tests\Scenes\SceneRestorePreparationGateTests.cs`, covers the separate restore-plan refusal. Do not remove either guard to execute the proposed witnesses.
- Chapter 35 schedules exact producer capabilities, not a blanket M03 exit prerequisite for all M05 work. Continue unrelated contract-complete work, but keep Scene-dependent renderer/public-consumer observations and M03/M05/native/AOT acceptance incomplete until their real prerequisites and observations close.

## 7. Frozen evidence

Baseline commit: `9299e49a3e6697eeef8b15518ce4d97aad84013c`. The working tree already contained unrelated changes. Of the restore-specific files below, Chapter 30 was dirty at inspection; its **working-tree bytes**, not HEAD, are the cited input. No reviewed input was modified by this task. No schema change is proposed.

SHA-256 values below identify the restore-specific authority, implementation and test input set:

```text
docs\design\27-state-serialization-replay-and-recovery.md|B9B6324C5B1B1543CA1D2287ECE579597A7E7AA62D885031C4FF1BB629F08777
docs\design\29-project-setup-build-and-deployment.md|3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B
docs\design\30-public-api-review-surface.md|15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B
docs\design\35-implementation-roadmap.md|24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87
docs\project\Roadmap.md|3086D68823CC53E67519ADBE3BA71EAC41A5B85054CB9FFC07432E387B33AFFE
sources\TerraFX.Runtime\Entities\World.cs|00FC348DBA47AA7E8B166B28FC7231CE411168FCF5E872A3E76781648F54D63A
sources\TerraFX.Runtime\Entities\World.PersistenceRestore.cs|86463430C1F582CFD6F7412DD8BC46110B8EF89F5E4F705AD5A7A7CD6238049E
sources\TerraFX.Runtime\Entities\WorldPreparedReplacement.cs|E14363D4707B8F3D3F92AB8241EC4D29B256DD2B9DB4F0D746DCD530BDC3FC23
sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs|B60C36D55EDD691A6C8442DB22F9A7995470F4FCE006D7578B24088A6496B508
sources\TerraFX.Runtime\Scenes\PreparedScene.cs|8A822C991792D18BBCF3D58718C7C420833C45A261F406487C7A6DD2E2EF3DF0
sources\TerraFX.Runtime\Hosting\WorldPersistence.cs|465614C0943947AD6FDFAD38C5920A0B4736310EB0F6E8118F5518CD76B3264B
sources\TerraFX.Runtime\Hosting\WorldPersistenceLoadWorkspace.cs|E9739302D10C567802C87BBFBFA8DC6D01095DFA0AB9544EF3E1E625D217457D
sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs|533D03EF8FA2292AEE2B0CE9F48A8C5EA19CFF16B4ADA1A8422C2DFC97EDB25A
sources\TerraFX.Runtime\Hosting\WorldLoadPlan.cs|67CA5C8BA0C9666C71657EBAD1184DFD93924D30AD88FA35A5C6E99877801B9A
sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.cs|C6DFA0EE44438C005E115FE9A15FE51643CF49176FB266A59AE5CFE67250EFF5
sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.Capture.cs|0B65EFA4D22D383BD93080FFC58622F5E4318D3296A147F8D997172BC9EF5C2D
tests\TerraFX.Runtime.Tests\Entities\WorldPersistenceIdentityHistoryReaderTests.cs|4CB6ACAECB6F19BCF36BF42EC4A82E6D0F3749FC2CCEBBACD80E2BAE2B5EA1FF
tests\TerraFX.Runtime.Tests\Entities\WorldPersistenceRegistrationCaptureTests.cs|5AABC2B1EF0D79B525E30A0D91828E35865E9FDC5946FDB622E12507259BE392
tests\TerraFX.Runtime.Tests\Entities\WorldStateRestoreBindingTests.cs|61644C7CE3A1032CEB4402836FA7A98322BA74D0855F7A8EE7617AB8AD67E751
tests\TerraFX.Runtime.Tests\Hosting\WorldLoadPlanTests.cs|86E49671F66CA215D38CEB6D51497C2DF95C185CB8DBE7F9418158633AC99606
tests\TerraFX.Runtime.Tests\Hosting\EngineHostPersistenceCommitTests.cs|6117160727FCC4CD3C88355064D889C9294D667A71BC19F8B74B38B33EA8CDA5
tests\TerraFX.Runtime.Tests\Hosting\WorldPreparedRestoreSourceTests.cs|19FDC9E71662E1BC92047869D40554B9CEB4A412ABB3F8F88D797B5C640C7F43
tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.Persistence.cs|B547C59BD829CFF197AFFDACDB5DACCD86386093DB9B5E9D0202230551ED07B4
tests\TerraFX.Runtime.Tests\Scenes\SceneRestorePreparationGateTests.cs|01395F056DDB8E73EB24EC1F0BD48554BCC2A7DC82FD3526B7D4E10C05355929
```

The prior proposal set contains **77** `docs\proposals\*.md` files, excluding this new file. Its manifest SHA-256 is `4DC358B6D2796D6C9A49E907C6A6A83B5B5BB0C7C92CF9F79B70A005CFECF31D`. To reproduce that digest, sort those files by name, emit one `repository-relative Windows path|uppercase file SHA-256` row per file, join with LF including a final LF, encode UTF-8 without a BOM, then hash the resulting bytes. This freezes both the existing decisions cited above and the searched set used to check for duplication.
