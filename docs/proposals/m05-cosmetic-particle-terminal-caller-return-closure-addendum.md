# M05 terminal caller-return closure

## Recommendation

**NO-GO for live terminal integration or cosmetic standing-owner construction.** Keep the prepublication anchor candidate. Tentatively select one fixed, synchronous **process-registry-owned enclosing invocation** that accepts the complete World close/unpublished-unwind body return, then the complete domain-retirement body return, before ending the original domain control charge and original process roles. Do not merely remove the anchored `Dispose`/`TryRetire` refusals.

This closes the proposed caller boundary, not its funding qualification or issuer permission. The smallest next owner decision is permission to escrow the exact original retirement authority in the already retained binding generation, independently of the spent lifetime permit, with the original domain charge surviving until that enclosing caller accepts return. No current issuer contract supplies that transition.

This supplements, rather than replaces, the tentative [anchor disposition](m05-cosmetic-particle-terminal-anchor-disposition-addendum.md), [domain authority](m05-cosmetic-particle-domain-terminal-authority-addendum.md), [synchronous invocation](m05-cosmetic-particle-synchronous-release-invocation-addendum.md), [terminal ledger](m05-cosmetic-particle-terminal-return-ledger-addendum.md), and [protected coverage](m05-cosmetic-particle-protected-close-coverage-addendum.md). Their historical source observations are not current-source proof.

## Current-source corrections

`E` below means `sources\TerraFX.Runtime\Entities`.

| Current evidence | Consequence |
|---|---|
| `E\WorldGuidRegistry.TerminalAnchor.cs` roots the exact domain in the original reserved binding; attachment requires unbound owner/no independent uses. Its detach/failure operations require a **Reserved** binding. | Preparation/cancellation exist. Failure retention after Active/Revoked publication does not. The reference does not fund its domain graph. |
| `E\WorldGuidRegistry.cs`, `TryRetire`, refuses an anchored binding **before** consuming its permit. Unanchored retirement consumes the permit and then calls `TryReleaseFresh`. | Wiring the current anchor into normal retirement would refuse closure. Detaching first destroys the required failure root; ordinary retirement supplies no replacement terminal authority. |
| `E\WorldAdmissionDomain.cs:3141`, `ReleaseRegistry`, leaves `RegistrarControlBytes`; `PublishRetirement` at 3118 calls `TrySetResult` and only then exchanges `_registryCharge` to zero. | The original control charge now spans synchronous publication. The old “registry charge is zero before publication” observation is stale. It still ends before the enclosing domain-call return. |
| The seven calls in `WorldAdmissionDomain.cs` leave their growth/membership scopes before publication. | The old outer-growth-gate suffix observation is stale for these bytes. Gate disposal remains an owner access wherever a proposed final debit is moved. |
| `World.Dispose` retains the enrolled reservation through its long nested `finally`. Its last handback is `CompleteWorldClose` at `World.cs:1523`, after restore-lease, quarantine-capacity and structural-operation cleanup. | The original World claim now covers that cleanup. A body boundary before this `finally` is incomplete. |
| `CompleteWorldClose -> EndWorldReturnPart -> FinishWorldRetirement` can retire the lifetime, debit the original World reservation and publish at `WorldAdmissionDomain.SceneStorage.cs:791`. | **There are seven legacy publication entries plus this eighth call site**, all calling one publisher/one TCS setter. A seven-only projection misses the actual enrolled-World terminal route. |

`WorldGuidUseControl.Release` also now retains a parent return part, and `CompleteDomainRelease` performs its control writes before the domain-use debit. Do not repeat the older post-debit field-write diagnosis as current fact. Its final parent handback can still reach the eighth publication route.

The source contains the explicit binding-cell padding and tests asserting the 128-byte managed stride. This assessment did not measure that stride or rerun those tests.

## Selected caller and charge progression

The first candidate lane is a real fresh standalone World with an engine-private default domain and empty registration: no Scene/extraction participant, resources, systems, transferred attachments, independent uses/history/operation consumers, restore/replacement or hosted/external domain. These exclusions must be enforced **before their acquisition/allocation**, not discovered during cleanup. They do not authorize a fabricated World or completion producer.

| Actual provision | Candidate coverage and end |
|---|---|
| Original `WorldDomainReservation` | Complete World construction/close body, including every World-reading cleanup `finally`, then lifetime fields/locks. Keep its actual interest/bytes until those uses end; do not copy, refund/reacquire or silently transfer it. |
| Actual domain's original registrar/control provision | Domain completion, publication, failure retention and all domain-owned cleanup through acceptance of the complete domain-body return. Retire only backing whose actual use ended; retain the original remaining control/protected charge, not newly reacquired credit. |
| Selected process registry's existing control/backing | Fixed enclosing invocation, original binding/owner-use/key retention and final process-only commit/gate suffix. This backing genuinely survives cell release at high water; its new invocation coverage is **not yet derived**. |

Use the existing Entities actor, not a delegate, Task observer, new worker, global root list or synthetic lifetime. The process registry is currently **not** the enclosing caller of the complete World/domain bodies; that is the required call-chain change.

The tentative progression is:

1. **Prepared.** Bind the exact original fresh grant/domain while the grant is reserved. Existing cancellation is prepublication abandonment only. Entering protected construction/ownership must irrevocably exclude that cancellation; current reserved-only checking alone is insufficient during owner-bound unpublished construction.
2. **Body running.** The fixed process caller directly invokes the actual World close or unpublished-construction/unwind body. Preserve its entire nested cleanup and exception precedence. Withhold the final `CompleteWorldClose` handback from that child; otherwise `EndWorldReturnPart` can retire it before the enclosing caller observes return. A public forwarding wrapper must have no later World-owned access/allocation.
3. **World body returned.** The real caller has observed ordinary return, not a flag set inside the child's `finally`. It directly invokes the domain completion body, including `CompleteWorldClose`, `EndWorldReturnPart`, `FinishWorldRetirement`, lifetime completion and publication. Original World funding remains until the lifetime/control accesses finish.
4. **Terminal authority escrowed.** While that lifetime is still funded, validate and consume its exact available permit once into an explicit terminal obligation in its original revoked binding. Keep the same O, owner U and necessary K roles/root charged. Do **not** call legacy `TryRetire` and reinterpret success as retention, or consult the spent permit/lifetime after its funding ends. An unpublished, never-owner-bound failure instead uses its still-valid original fresh grant; its P/coordinator U end only after their actual work returns.
5. **Publishing.** The sole selected terminal body retires actual interests/backing, exits domain scopes and invokes the real TCS setter directly, retaining the original control charge and process anchor. It must not execute the selected equivalent of the current publisher's final zeroing.
6. **Domain body returned.** Only after the complete domain call chain and publisher have returned to the process caller may the caller accept this phase. Task terminality, a returned `TrySetResult` Boolean alone, or a marker written inside `FinishWorldRetirement` does not establish it.
7. **Prepared final commit.** While claims remain, validate every remaining generation, role/count/index effect and terminal phase. Finish domain fields/reciprocal-anchor cleanup and domain gate exits before the final domain-charge debit. That debit must be the last domain-owned access. The surviving process caller then clears its anchor and releases the exact remaining original process roles, without a domain/lifetime callback. Its process gate exit stays process-funded.

The commit must have no valid-path allocation, publication, refusal or throwing cleanup after the domain debit. A check or `TryReleaseFresh` that could fail after destroying the last authority is not such a commit. Until that is established, retain the claim rather than debit and attempt recovery.

This selects the existing [harmless-suffix alternative](m03b-scene-preparation-owner-aware-async-handback.md), not literal physical return of every public caller. The remaining wrapper/IL/native suffix must be independently shown to perform no ended-owner access/allocation. If physical public-caller return is mandatory instead, the current void interfaces provide no independently funded outer acknowledgment; this lane remains unsupported.

## Complete publication membership

Preserve all unanchored behavior. Selected handling must distinguish nonterminal releases from the final publication election **before mutation**:

| Existing entry / current publication | Selected disposition |
|---|---|
| `Dispose` / main file:289 | Keep ordinary anchored disposal refused. Exact default-owner closure needs a private logical-close path retaining the anchor and deferring publication to the enclosing invocation. |
| `ReleaseHistoryInterest` / 412 | No independent history consumer is issued in this lane; refuse that acquisition before issuing its credit. No last-history publisher is silently admitted. |
| `ReleaseOperationCredit` / 442 | No independent operation credit is issued; refuse before acquisition. In particular, its caller's `_isChargeReleased` write and lock exit are not covered by publisher return. |
| `ReleaseWorld` / 507 | Enrolled World release remains the final-return route, not reservation `Dispose`. Unenrolled reservation/allocation rollback must run inside the actual construction/unwind invocation and defer selected terminal publication to its returned-body handback. It cannot be refused only after allocating the reservation. |
| `RollbackUnissuedWorldGuidUse` / 596 | Independent-use admission is excluded before raw/control allocation; this rollback is not an admitted selected publisher. |
| `RollbackWorldGuidUseRaw` / 662 | Same exclusion, before issuing the raw ticket. |
| `TryReleaseIndependentUse` / 1220 | No selected independent U is issued. Supporting a last-use publisher later requires enclosing that real release caller too; the World-close invoker is not its substitute. |
| `FinishWorldRetirement` / SceneStorage file:791 | Supported terminal body for normal enrolled close and enrolled unpublished unwind, but only through the fixed enclosing caller and explicit escrow above. |

`AdmitWorld` has the fresh-unwind `CompleteWorldClose` call at `World.cs:853`; `WorldConstructionPlan.ReleaseUntransferredRestoreResources` is another caller but is excluded with restore. Scene receiver/construction returns, use-return completion and resource-operation returns can also reach `EndWorldReturnPart`; their producers must remain unavailable in this initial lane. Excluding only the seven direct setters would not exclude those ingresses.

Election must cover this entire membership, preserve one public Task and existing outcome/continuation policy, and exclude another setter before publication. The historical CoreLib ledger's false-result/spin warning is not requalified here: no selected already-completed/competing setter is permitted. A false result is not successful handback or permission to retry.

## Failure, missing permission and first batch

Capture the actual escaping exception **after all protected child cleanup has unwound**, retain it by reference in the preowned exact anchor and bare-rethrow it. Do not capture an earlier exception that a later existing `finally` replaces. Before close starts, preserve the existing no-close/usage-refusal behavior. Once terminal work starts:

- Retain every remaining original charge and O/U/K root; retain precise confirmed subreleases instead of recreating consumed roles or lineage membership.
- A publication failure retains the original domain control charge. If the Task already became terminal, preserve its outcome; do not publish again or report readiness as settled ownership.
- Extend explicit retention authority to the selected Active/Revoked/escrow phases; today's Reserved-only helper cannot retain those failures. Repeated close/cancellation cannot discharge retained debt. No automatic retry/recovery is selected.
- Prove failure recording/final bookkeeping valid-path nonthrowing before use. A fixed process reference does not bound or pay an arbitrary exception graph; its actual retained/protected domain coverage remains required.

Chapters 12/29 currently restrict binding retirement/quarantine to the exact lifetime/permit and defined roles; they do not grant the surviving post-permit terminal escrow or the new process invocation. Chapter 04's original control rows and numeric headroom authorize neither undeclared work nor a protected allowance guessed from padding.

**Smallest next decision:** accept or reject that narrowly scoped escrow/enclosing-caller permission, conditional on complete original-domain and process coverage. Also close construction bootstrap: the present attach helper takes an already constructed domain, while its constructor creates TCS/backing before setting `_registryCharge`. A failed constructor cannot be retroactively anchored. Establish prospective original-domain funding/retention before selected construction can escape an obligation, or demonstrate an explicitly owned preselection boundary; do not silently fall back.

After those permissions and finite coverage are established, the first coherent implementation/test batch is:

1. Implement the issuer phases, preserved charge and prepared end-or-retain commit together in `WorldAdmissionDomain`/its terminal partial, `WorldGuidRegistry`/its terminal partial and the lifetime/permit/default-owner participants. Keep legacy Boolean success meanings and all ceilings unchanged; price changed common declarations before installation.
2. Integrate the **complete** real World close and fresh-construction unwind callers with the final-return partial in the same batch. Cover all eight publication entries/refusals above. Do not install an isolated “returned” token or another inert wrapper as caller closure.
3. Add focused existing-project tests using real issuance and factory paths: preparation/cancellation, wrong/stale/replayed authority, Active/Revoked escrow/retention, WorldGuid retention until final commit, normal empty-World close and actual unpublished refusal/unwind. Preserve ordinary unanchored registry/domain/receiver behavior. Use real available failure stimuli; do not manufacture a completion producer, manually stamp body return or add test-only callbacks. Unavailable failure/native stimuli remain explicitly unqualified.

Use ordinary owning-project Debug then Release validation with nonempty executed selections in that later batch. Source/managed behavior tests do not establish cold/failure allocation bounds, compiled/native harmless suffixes, physical return, Scene supplier closure or public/native/AOT consumer qualification. Those remain open alongside prospective construction coverage; `WorldCosmeticCreditOwner.HasQualifiedStandingOwner` remains false. No numeric allowance, public API or acceptance ID is added.

## Review input boundary

Verified HEAD: `2ec881048444963925d92c4a7f8d566e12addbd6`. This is a working-byte assessment, **not** a historical freeze or a HEAD-only review. The inspected Chapter 04/12 files are dirty. A concurrent registry edit adds clearing of terminal fields in `ReleaseBindingCell`; it was inspected and is included in the hash below, not authored here. Symbol-only discovery is not additional content evidence.

These are raw SHA-256 hashes of all 35 repository files whose content was read, including build/workflow context. Revalidate them before applying any disposition. Runtime binaries and historical IL artifacts were not read or requalified. This change adds only this proposal; no source/tests, maintained documents or other proposals were edited, and no builds/tests, commit, push or PR were performed.

```text
1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C  global.json
AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105  Directory.Build.targets
03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58  .github\workflows\ci.yml
F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F  docs\project\Building.md
3086D68823CC53E67519ADBE3BA71EAC41A5B85054CB9FFC07432E387B33AFFE  docs\project\Roadmap.md
A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245  docs\design\04-capacity-and-workload-profiles.md
174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B  docs\design\12-entities-components-and-queries.md
3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B  docs\design\29-project-setup-build-and-deployment.md
24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87  docs\design\35-implementation-roadmap.md
FC1DED883EA5CF6A37214B595169457D6A92610AEC0908F4DE0ADE2304508C1D  docs\proposals\m05-cosmetic-particle-terminal-anchor-disposition-addendum.md
1C3FA0C41D8EF5272DEAA0A16A575FD83B7742F3752F0F4D92D93A9980A9AF9A  docs\proposals\m05-cosmetic-particle-domain-terminal-authority-addendum.md
1E97D990D2AFF1E2A92061919C4E105EB00E7CFC09C05FAC82F6B3979D0D0386  docs\proposals\m05-cosmetic-particle-synchronous-release-invocation-addendum.md
0A317BE624225E5A6873F82D491FDD93B2C01A91DCBAF1CF96153ADFE4659DB8  docs\proposals\m05-cosmetic-particle-terminal-return-ledger-addendum.md
098DA320E61AA3171B5B09D1C11DAA0CD03B67AFB5B1CA5DA5C75801987744FB  docs\proposals\m05-cosmetic-particle-protected-close-coverage-addendum.md
0A9AE81EB42D886168D9E05970C7395B33C9EF70FCCADA9ED311CF2E1DC7AD89  docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md
EC2C6286B389AFC7A0BB9DE39D71D45BA1E20CBBFAE0AE3C8F433FF28A886A5E  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
0C70A331E7FE558CB3756DC5D71B1810362388DA52C86BD10693B136739B301F  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.TerminalAnchor.cs
53111972C5B480E8357C93D471EDCB4E08C7BBE65BD341BFC7260FDC356BA5C3  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
CEAC98719116305BA2727B892CE540A3B6B26D1DDF72AFE06E21504909747FB0  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.ScenePreclaimedWorkspace.cs
16A8126C8A4076E664742430890308CAF180ABFF4173A660005456F9501265F8  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
0434DBDD840C9FDA69DF5714134E96C6408BD464B49D9F8A63595EBD24FBFBAB  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.TerminalAnchor.cs
C7DCBA4DFEF58346C6C851F77A8E130B1A4B776248E94663438A5D9E449245A8  sources\TerraFX.Runtime\Entities\World.cs
3E2594104DD57A2C85C84C5FC2F1A7C9895B2BC6590F17B57C063698A3C1A300  sources\TerraFX.Runtime\Entities\World.ResourceState.cs
671390B19880E4EC007A88D588B4DA318FA4C67B1D9B03A9CF00636AB051CFF2  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4  sources\TerraFX.Runtime\Entities\WorldDefaultDomainOwner.cs
D6CDD6105850B8076D40891B0ED1F583D9FCDE6EE190444A7D570879964403DC  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
F00B54B0E8F305F90DF0D63FFDF6C3419FEB8F3D2542E72D76A79AD2D0990946  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
D3B20BEE7FBD4CA540EB9E082DFECA7ACB533179BF20AE9EE13B54E5560845A5  sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs
456587893972E153B872DBBD4D849B67260A297DB4D6DEA2FBDE7CFE6E7EC0FC  sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
1F005480FE39ACE70B469848F943A5A72433C00A1F9E099075DCCB604B2A0002  sources\TerraFX.Runtime\Entities\WorldDomainOperationCredit.cs
1094A7B16FA8D78FB0A8095CB3E3C77AD94417DBB85B34CE7E46AB58BD67BAEA  sources\TerraFX.Runtime\Entities\WorldHistoryInterest.cs
7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6  sources\TerraFX.Runtime\Entities\WorldCosmeticCreditOwner.cs
8F0EF274C39C3E07E691951011AC86C574311912D7F78348EB8D6780555F0C71  sources\TerraFX.Core\ReentrantGate.cs
B2F20EC7E06DE398DA333BB4513C59FD09F7BAA95CAE5B8F3D6E51FFEC49C0BD  tests\TerraFX.Runtime.Tests\Entities\WorldGuidRegistryTests.cs
5580DA24D81D110DCFDAA35035406A0C6464A054B3A7489B8488398497B7BEBD  tests\TerraFX.Runtime.Tests\Entities\WorldSceneReceiverTests.cs
```
