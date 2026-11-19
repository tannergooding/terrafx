# M03-B World restore operation owner alignment

**Status: tentative rectification direction; the execution handoff is not contract-closed.** This addendum identifies the smallest ownership split and the precise decision needed before implementing it. It does not authorize an invented phase API, a reverse dependency, or a change to the authoritative Chapters 27/29/30. The current task changes only this proposal.

## 1. Finding

The [handle-actor enrollment proposal](m03b-world-restore-handle-actor-enrollment.md) correctly separates resolver enrollment from replacement-operation alignment. Commit `26b2b2465a25350617fdbb88ffa3f5d513d8a145` supplies the narrow enrollment; it does not make the current higher-owned replacement actor conform to the existing lower-owned contract.

**A namespace move is not a safe decomposition.** The current operation owns Hosting objects and invokes candidate-dependent Scene restoration, whole-World capture, receipt construction, host adoption and service completion. Moving that implementation into Entities would move higher orchestration downward. Keeping those calls through an interface, delegate, partial-method hook, generic dispatcher, ambient lookup or opaque executable object would retain the same forbidden dependency.

Conversely, no existing port provides the required non-callback handoff between candidate creation by the captured Entities operation and higher-owned restoration/capture. Chapter 30 specifies one parameterless execute operation that restores, compares and publishes or refuses; it does not specify intermediate results, reentry or an earlier candidate-creation phase. Implementing such a protocol requires an owner decision, not a mechanical refactor.

### Exact current evidence

Paths below are relative to `sources\TerraFX.Runtime`; line numbers refer to the frozen inputs.

| Input | Observation |
|---|---|
| Chapter 29, lines 49-64, 207-234; Chapter 30, lines 3222-3226, 3244-3256 | Entities owns `WorldReplacementOperation`; only the exact captured operation may call the restored-candidate factory. Hosting captures synchronously and drives a typed owner-boundary request. These are semantic restrictions despite the shared Runtime assembly. |
| `Hosting\WorldPersistence.cs:594-671` | Public commit constructs a Hosting operation, calls `operation.TryCapture`, then calls its execute method directly or queues it. There is no `CommitLoadAtOwnerBoundary` member. |
| `Entities\WorldPreparedReplacement.cs:30-39`; `Entities\WorldConstructionPlanControl.cs:73-111` | Current capture returns credits/handoff and two refusal carriers, not the contract's complete issued operation and one bounded refusal carrier. |
| `Hosting\WorldLoadPlan.cs:138-178`; `Hosting\WorldLoadPreparedState.cs:548-599` | Actual capture passes through the public plan's gate and higher prepared state before reaching the lower capability. C# references identify `WorldLoadPreparedState.TryCaptureCommit` as the sole production direct caller of the lower capture member. |
| `Hosting\WorldReplacementOperation.cs:11-38`; its `.Capture.cs` partial | The operation implements the lower retention marker but stores `WorldPersistence`, `WorldLoadPlan` and `WorldLoadPreparedState`. Its execute signature returns `WorldLoadReceipt` and takes optional `EngineHost`, unlike the declared contract. |
| `Hosting\WorldLoadPreparedState.cs:345-453` | Its `CreateRestoredCandidate` is the sole production direct caller of `World.CreateRestoreCandidate`; it then initializes the candidate, prepares Scene restoration, restores ECS/Scene/clock/control state and installs playback ownership. Friend-test factory calls are not production permissions. |
| `Hosting\WorldLoadPreparedState.cs:686-739`; `Integration\WorldPersistenceCaptureCoordinator.cs:15-56` | Fresh comparison captures the actual restored Scene through the higher Integration coordinator, then captures the remaining World projection. Supplying the saved Scene record is not an observational capture of candidate Scene state. |
| `Scenes\ScenePersistenceRestorer.cs:164-378,606-649` | Scene preparation and installation need the actual candidate, its new registry/identity map, the retained package and Scene-owned expansion/instance operations. The work cannot all move before candidate construction. |
| `Hosting\EngineHost.WorldReplacement.cs:24-108,140-186`; `Hosting\EngineHost.cs:337-341` | The current operation prepares host adoption, then invokes it itself. Current adoption takes a prepared `WorldReplacementAdoptionPlan`, not Chapter 30's exact three-argument member called by the typed request. Preserve prepublication preparation, not the current caller/signature mismatch. |

These are implementation divergences, except for the unclosed execution handoff and supporting declarations identified in section 4. None authorizes changing the contracts to describe the current source.

## 2. Required ownership partition and exact known chain

The smallest dependency-correct partition has the following responsibilities. This is an ownership boundary, **not an assertion that the missing protocol already exists**.

| Owner | Responsibilities retained there |
|---|---|
| `TerraFX.Entities` | `WorldConstructionPlanControl`, `WorldPreparedReplacement`, the issued `WorldReplacementOperation`, exact target/domain/root/grant validation, candidate factory authority, lower World restoration operations, binding/default-domain transfer and restored-marker publication. No Hosting or Scenes orchestration reference or new callback. |
| `TerraFX.Hosting` | `WorldPersistence`, `WorldLoadPlan`, `WorldLoadPreparedState`, source/checkpoint/package ownership, higher restoration sequencing, source-bound remap and public receipt construction, persistence-service accounting and separately reported old-World retirement. These responsibilities confer no World factory permission. |
| `TerraFX.Scenes` | `ScenePersistenceRestorer`, package-backed Scene preparation, restored instances/registry/hierarchy and their cleanup. |
| `TerraFX.Integration` | Composition of actual Scene and Entities capture for fresh whole-World observations. |
| `TerraFX.Hosting.EngineHost` and `TerraFX.Integration.WorldInputTimelineAdapter` | Prepublication schedule/input-adoption preparation and allocation-free adoption by the typed owner request after publication, before another host request or tick. |

The exact signatures already required by Chapter 30 are:

| Declaring owner | Required signature / exact caller |
|---|---|
| Hosting `WorldPersistence` | `Task<WorldLoadReceipt> CommitLoadAsync(WorldLoadPlan plan, CancellationToken cancellationToken)`; public entry, retaining its optional token default. Directly calls the lower capture member after entry checks. |
| Entities `WorldPreparedReplacement` | `bool TryCaptureCommit(out WorldReplacementOperation? operation, out WorldGuidRefusal refusal)`; called only by that public entry and forwarding to the sealed construction control's all-or-none transaction. |
| Hosting `WorldPersistence` | `Task<WorldLoadReceipt> CommitLoadAtOwnerBoundary(WorldReplacementOperation operation)`; called by standalone commit on the exact World owner, or the typed host request on the host owner. |
| Entities `WorldReplacementOperation` | `WorldReplacementOutcome ExecuteAtOwnerBoundary()`; called only by the preceding owner-boundary member after successful capture. Its specified complete restore/compare/publication semantics remain authoritative. The outcome declaration is unresolved, not supplied by this table. |
| Entities `World` | `static World CreateRestoreCandidate(WorldConstructionPlan constructionPlan)`; called only by the exact captured operation, before candidate factories/Restore. |
| Entities `WorldReplacementOperation` | `TransferSourceToPlayback(WorldPlaybackControl playback)`; the specified one-use transfer is invoked by its owner-boundary execution after source-reader/callback quiescence. Chapter 30 does not close this member's complete declaration or the playback carrier's ownership projection; see section 4. |
| Hosting `EngineHost` | `bool TryEnqueueWorldReplacement(WorldReplacementOperation operation, out Task<WorldLoadReceipt>? completion)`; called only by host-owned public commit after capture. |
| Hosting `EngineHost` | `void AdoptWorldReplacement(World previousWorld, World replacementWorld, long hostTimestamp)`; called only by the typed request after publication, not by the Entities operation. |
| Integration `WorldInputTimelineAdapter` | `void AdoptWorld(World previousWorld, World replacementWorld, long hostTimestamp, bool isPaused)`; called only by that host adoption member. |

Thus both routes must converge on `WorldPersistence.CommitLoadAtOwnerBoundary(operation)`, which alone calls the lower execute member. The hosted route first passes through the typed bounded queue; the existing `EngineHost+HostWorldReplacementRequest.Execute(EngineHost)` is the relevant current request actor, not a generic host callback.

The existing higher-owned operations that need a legal place inside that owner-boundary lifecycle are:

```csharp
// Existing TerraFX.Scenes.ScenePersistenceRestorer static members:
internal static Task<ScenePersistenceRestorer.PreparedRestore> PrepareAsync(
    World world, AssetStore assets, AssetPackageLease packageLease,
    ReadOnlyMemory<byte> scenesRecord, WorldPersistenceScenesContents contents,
    CancellationToken cancellationToken);
internal static void RestorePrepared(World world,
    ScenePersistenceRestorer.PreparedRestore prepared,
    CancellationToken cancellationToken);

// Existing TerraFX.Hosting.WorldLoadPreparedState instance member:
internal ValidatedWorldIdentityHistory ValidateRestoredCandidate(World candidate);
```

They are existing internal operations, not proposed lower signatures. `WorldLoadPreparedState.CreateRestoredCandidate(CancellationToken)` cannot remain their factory-owning wrapper: its direct call to the sensitive factory is precisely what must be removed after the handoff is specified.

## 3. Ordering and ownership that any implementation must preserve

1. **Preparation precedes issuance.** Complete source-dependent identity/profile/schema/package/reference checks and prepay the actual candidate/coexistence/cleanup allowance. The lower plan owns the exact already-admitted target identity and binding; no reconstructed plan, fresh lineage draw, second history registration or GUID-based retarget is allowed.
2. **Capture is the acceptance point.** Allocate/fund the preowned operation before the gated transaction. Public usage and pre-admission cancellation checks precede its direct capture call. Capture transfers the complete bundle or none under process-registry, exact-domain, then binding gates; no allocation, callback, diagnostic formatting, result delivery or Task completion occurs inside them. Preserve expected domain-closed versus stale-target refusal facts without retaining the source's extra out parameters as an undeclared overload exemption.
3. **Closure does not revoke accepted work.** Disposal-first captures nothing; capture-first has independent source/target/domain interests and survives plan/service wrapper closure. Capture after external-domain closure rejects; an earlier accepted operation may finish under its prepaid grants if its exact target remains valid. Coordinator P remains while either associated coordinator or commit role is live. Accepted work uses `CancellationToken.None`.
4. **Create and restore only at the exact owner boundary.** Revalidate before candidate owner issuance, then let only the captured Entities actor call the factory. Complete its admitted Entity map before Restore callbacks/reference materialization and use the existing candidate initialization path. Preserve the global Persistent-before-Derived barrier, registered resource/system/attachment restoration, higher-owned Scene restoration and clock/control ordering. The existing M05 proposal's derived-only restored join remains after those restores and before fresh comparison when that composition is implemented; this addendum enables none of its missing producers.
5. **Keep source use and transfer distinct.** After actual commit-source readers/callbacks join, transfer existing `S_c` plus the exact source/recording data and pin ownership once to candidate playback before publication. Do not acquire another commit interest, refund a transferred credit or revoke installed playback with the old binding/wrapper. `S_o` follows actual source-data release, `T_c` covers actual target comparison and `D_c` covers accepted work and cleanup.
6. **Prepare every fallible publication dependency first.** Fresh observational Validate/Count/Write must cover the real complete candidate and compare with validated target records. Prepare the marker, source-bound independently owned remap, receipt/retirement storage and host schedule/input-adoption storage before publication. Do not replace fresh Scene output with saved bytes, repair canonical state after comparison, run a hidden tick or replay notifications.
7. **Revalidate and publish once.** Check the exact old binding again. Staleness before issuance consumes no candidate owner; staleness afterward burns that owner and unwinds the unpublished candidate without retargeting. The allocation/callback-free visibility transaction transfers binding/history/WorldGuid/reservation state and, for an engine-default domain, its single close-owner credit/generation, while installing the coherent restored marker. No ownerless/double-owner interval or close/reopen substitution is allowed.
8. **Adopt before further host work.** After successful publication, the typed host request adopts only the exact old schedule entry and rebinds the existing input adapter/router/event-sequence lease, retaining pause and physical-input-reconciliation state and starting the new clock segment at replacement `NextTick` without old debt. Adoption performs no postpublication allocation. Keep host preparation/adoption outside Entities and outside admission gates.
9. **Deliver and retire through their real channels.** Expected prepublication refusals publish no replacement/remap/loaded identity. Unexpected callback, I/O, allocation, comparison or invariant failures fault the operation. Successful publication remains `Loaded`; old retirement has its own completion/failure. Old-World retirement cannot close the successor's default domain. Charges end only after actual uses/storage release, with unreleased cleanup retained by its existing owner/quarantine.

## 4. Precise unresolved decision

**The missing decision is the candidate-to-higher-orchestrator-to-publication handoff, including how it satisfies the existing execute contract.** The owner must close all of the following together before source alignment:

- The exact non-callback protocol by which the captured Entities actor alone creates the unpublished candidate, Hosting performs the candidate-dependent Scene/Assets composition and actual fresh capture, and the same lower authority completes comparison/source transfer/publication. Chapter 30 currently exposes no such phase transition. In particular, treating `ExecuteAtOwnerBoundary()` as publish-only, returning an intermediate candidate from it, or calling it repeatedly would change its specified complete terminal semantics; none is silently authorized here.
- The owning declaration and complete closed result/lifetime contract of `WorldReplacementOutcome`. Repository-wide C# symbol lookup finds no declaration; the only design occurrence is Chapter 30's execute signature. Do not alias it to Hosting's `WorldLoadReceipt`, infer intermediate variants, invent fields, or let its existence imply permission to call higher orchestration.
- The lower-only ownership projection for the specified playback transfer. Current `WorldPlaybackControl` and `WorldRetainedHistoryControl` are Hosting types; the former stores `AssetPackageLease` and the latter a Hosting checkpoint envelope. Moving those graphs wholesale would not close the boundary. The owner must specify how the exact `S_c`/source-data/pin transfer reaches candidate playback without an Entities-to-Hosting dependency or executable restoration callback, and who retains actual cleanup obligations.

The existing ports do not answer that decision:

| Existing port | Why it is not the missing authority |
|---|---|
| `IWorldRestoreSourceOwner` / `WorldRestoreSourceLease` | Expose exact source registration/history/tick/limits and scoped source reads. They do not execute candidate Scene restoration, fresh whole-World capture or publication. Do not repurpose the source control's existing cleanup callback into restoration orchestration. |
| `IWorldSceneParticipant` | Provides the existing hierarchy/camera/capture/retirement operations, not package-backed candidate restoration or a complete target comparison. Extending it for this purpose would be a new protocol, not reuse. |
| `World.AdoptRestoreLifetimeLease(IDisposable)` | Retains a candidate lifetime obligation; it does not perform the specified `S_c` transfer or establish restore/comparison completion. |
| `WorldPersistenceWorldRecordCaptureCoordinator.Capture(...)` | Can combine already captured Scene bytes with lower World state. It cannot itself observe the higher Scene registry, and calling it with saved bytes would evade the fresh-comparison requirement. |

The recommended direction is to keep the partition in section 2 and obtain that explicit protocol completion, rather than relocate authority or introduce a reverse callback. This is a genuine design/API stop under this task's restrictions, not a proposal to rewrite Chapters 29/30 merely to match the current Hosting implementation. Tentative approval of this analysis does not manufacture the missing members or relax the current execute semantics.

### Distinguishing example

A Standard Branch checkpoint has a saved Scene instance with live Entities A and B and an edited saved hierarchy connecting them. Its replacement keeps the same WorldGuid/stable IDs but receives a fresh process owner. Scene restoration must install the saved instance/hierarchy into the **new candidate's** registry and resolve candidate-owned handles, not old handles or a rerun of authored state.

Those operations require a candidate already created by the lower actor. Fresh whole-World capture must then observe that installed Scene state; using saved Scene bytes would let an empty or incorrectly restored registry escape the comparison. Performing all higher work before candidate construction, giving Hosting factory permission, or moving Scene calls into Entities each fails a different existing requirement. The currently missing handoff must distinguish this real composition from an empty-Scene load. This is a required future witness, not an executed example; existing producer guards remain in force.

## 5. Conditional implementation batches and evidence

All implementation batches below depend on the protocol decision; no source change is authorized by this proposal task.

| Batch / owner | Dependency and acceptance |
|---|---|
| Close the protocol — Entities/Persistence/Scenes contract owners | Resolve section 4 with exact declarations, callers, result alternatives, transfer/cleanup rules and the example above. Preserve the existing lower factory/issuance authority and higher algorithms. Until then, operation alignment is not implementation-ready. |
| Align capture and routing — Entities + Hosting | Preown/issue the lower operation through the exact two-out capture signature; public commit calls it directly. Route standalone and typed host execution through the exact Hosting owner-boundary member. Remove Hosting's operation-retention actor rather than maintaining competing operation types or reconstructing authority from plan metadata. |
| Integrate restored state — Hosting + Scenes + Integration + Entities | Use only the reviewed handoff to invoke the existing higher restore/capture operations on the exact candidate. Keep cached registered authors and retained package identity; remove the unauthorized higher factory call. Implement no generic callback, new issuer or test-only execution route. |
| Complete publication/adoption/delivery — Entities + Hosting | Keep lower atomic replacement and source transfer with the operation; keep host preflight, exact typed adoption, public receipt/service completion and old retirement above it. All fallible/allocation-bearing adoption preparation precedes publication. |
| Validate the integrated unit — Runtime test/acceptance owner | Review actual C# binding edges and execute the focused production owner paths below against the final implementation. Managed behavior, compiled roots, public consumers, native/AOT qualification and milestone acceptance remain separate. |

Required focused evidence:

- **Caller membership:** exactly one authorized production factory caller in the captured Entities operation; the public commit is the direct lower-capture caller; only the Hosting owner-boundary member calls execute; only the typed request calls host adoption. Include all partial declarations and both execution routes. No new architecture analyzer or policy schema is required.
- **Capture/closure/refusal:** disposal-first versus capture-first, single-use/competing capture, pre-capture cancellation versus accepted noncancelable work, queue-full cleanup, service disposal joining accepted work, and two plans against one old binding. After one publishes, the other cannot replace the same-GUID successor.
- **Freshness/domain/ownership:** stale before issuance and after candidate creation; external closure before/after capture; default-domain close ownership transfers once and survives old retirement. Preserve independent source/cache/playback/remap lifetimes and actual cleanup/quarantine funding.
- **Real restoration:** production Save → PrepareLoad → CommitLoad, candidate-owned component/system/attachment/Scene references, complete Persistent-before-Derived restoration, actual Scene registry/hierarchy capture, fresh canonical comparison, unchanged `NextTick`/publication semantics and no hidden execution. Late callback/comparison faults cannot publish an incomplete candidate.
- **Host publication:** same exact schedule/input ownership, retained pause/reconciliation state, no old simulation debt and no intervening host request/tick between publication/adoption. A postpublication old cleanup failure keeps `Loaded` with failing `OldWorldRetirement`.

The existing class in `tests\TerraFX.Runtime.Tests\Hosting\EngineHostPersistenceCommitTests.cs` is **`TerraFX.Runtime.Tests.Hosting.WorldPersistenceCommitTests`**, not a class named after the file. Its standalone/hosted commit, bounded-queue refusal, stale-target, external-domain, retirement-failure and default-domain tests are relevant starting points. `WorldPreparedRestoreSourceTests` covers registered-source identity/read/retention behavior; `WorldLoadPlanTests`, `WorldIdentityRemapTests` and the existing Entities restore tests cover adjacent contracts. These bodies/symbols were inspected; no tests were run for this documentation-only task.

For later implementation, use the SDK from `global.json` and ordinary owning Runtime build/test commands from [Building](../project/Building.md), Debug before applicable Release. Add only missing focused regressions, combine applicable selectors, and confirm fresh nonzero selected/executed results. Do not count unexecuted or producer-blocked Scene variants as passes.

## 6. Non-goals and retained gates

- No source, maintained-contract, test, roadmap or other proposal edit; no commit, push or PR. This document proposes no public API, State2/schema change, new acceptance ID, domain/profile allowance or generic qualification framework.
- No extra factory caller, broad `World.*` permission, Hosting/Scenes reference from Entities, new orchestration callback, service locator, second allocator, speculative outcome carrier or fabricated restore capability.
- No enablement of StrictLocal, RecordedContinuation, IndependentFork, migration, live M05 animation/particles or another unsupported composition.
- Preserve `PreparedScene.QualifiedFootprint == null` and `ScenePersistenceRestorer.MeasureRestoredScenePlanStorageBytesAsync`'s refusal for nonempty Scene instances with a resource preparer. This addendum closes no Scene root/workspace/receiver/producer-return or Assets release gate. The [workspace proposal](m03b-scene-preparation-workspace-closure.md) and [resource-bundle closure proposal](m03b-resource-bundle-preconstruction-owner-and-return-closure.md) retain those owners.
- The [runtime-port proposal](m05-owner-contract-closure-runtime-ports.md) and its [typed-source addendum](m05-owner-contract-closure-runtime-ports-addendum.md) retain the M05 join, cached-author and asset-reference contracts; neither declares the missing operation handoff/outcome.
- Chapter 35 permits unrelated dependency-ready M03-B/M05 work to continue. This bounded alignment stop is not a blanket milestone prerequisite or acceptance claim.

## 7. Frozen review inputs

Entry HEAD: `26b2b2465a25350617fdbb88ffa3f5d513d8a145`. During investigation, HEAD advanced to `5a30bd848baf8101883f1a495bd4e0520ab2005d` through a Roadmap-only commit; all frozen working-tree input hashes below still matched at recheck. This task created no commit. The input freeze uses working-tree bytes, not just HEAD. Relevant preexisting dirty inputs are Chapters 04/12/16/30 and the Runtime/test project files, whose individual hashes are included below. Other preexisting Rendering and Scene-test edits are outside this decision; no all-worktree preservation or unchanged-HEAD claim is made.

The complete review/search input sets are frozen by the following manifest digests. For each set, enumerate exactly the specified files; emit `repository-relative Windows path|uppercase file SHA-256` rows, sort the rows with `StringComparer.Ordinal`, join with LF including one final LF, encode UTF-8 without BOM and SHA-256 the bytes. Sets contain no generated build outputs. The new proposal is excluded from its own input set.

| Set | Exact membership | Files | Manifest SHA-256 |
|---|---|---:|---|
| Authority | All immediate `docs\design\*.md`, plus the fourteen paths listed below | 52 | `7E66D6089468E46D5997646D0AD52CE25B9539A15B8A09CA51DBA273D510E03E` |
| Runtime | All `*.cs` recursively under `sources\TerraFX.Runtime\Entities`, `Hosting`, `Scenes`, `Integration` and `Animation` | 643 | `598C494404A67EC2DCFC6D69175695727A5B6A7F6447AE07631ABCF78DBAA25F` |
| Tests | All `*.cs` recursively under `tests\TerraFX.Runtime.Tests\Entities` and `Hosting`, plus `Scenes\SceneAssetStoreTests.Persistence.cs` and `Scenes\SceneRestorePreparationGateTests.cs` under that test project | 122 | `69D18043B1CAA57CECEA9F58F825562E425913DE683DCD1286C3174BB45A502C` |
| Related proposals/search set | All immediate `docs\proposals\*.md` except this file | 78 | `8383AF4AC51425C8B395E57704F7708F04DFAC83E094F232982FE2831D4BB649` |

The fourteen additional Authority paths are:

```text
docs\project\Roadmap.md
docs\project\Building.md
.github\copilot-instructions.md
.editorconfig
global.json
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
sources\Directory.Build.props
tests\Directory.Build.props
tests\Directory.Build.targets
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
.github\workflows\ci.yml
```

Individually addressable principal inputs follow. The final row is an additional input outside the four sets: C# factory-reference discovery also reaches that existing friend-test helper. Chapter 10's owning schema text is in the Authority set; no separate fixture/schema payload is being submitted or changed.

```text
docs\design\04-capacity-and-workload-profiles.md|A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245
docs\design\10-binary-data-and-authoring-formats.md|56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F
docs\design\12-entities-components-and-queries.md|174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B
docs\design\16-scenes-transforms-and-cameras.md|B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A
docs\design\27-state-serialization-replay-and-recovery.md|11DB22E4E1DFCB8798A64CBDD8B036DBBE99BD27D01A975E34C323215E855AD0
docs\design\29-project-setup-build-and-deployment.md|A39A8D7C67F311AC86E9CD604DAF85DF5AD283957A10E0719939C2420A907FAA
docs\design\30-public-api-review-surface.md|6668E34C24AA09C4DA1197C6A6166712B42E102EFB6EFC4407B09B080C943211
docs\design\35-implementation-roadmap.md|24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87
docs\project\Roadmap.md|99136F7EDE8E0CF5BB94F575A936D64FE7F2E29B622A9B47EB984C82E65821D7
docs\proposals\m03b-world-restore-handle-actor-enrollment.md|B35FC754A7AA0FB235A37682131923DF7E3FF62DF95CD3FAC5A35E592336ED55
docs\proposals\m05-owner-contract-closure-runtime-ports.md|605CF1D92DC362A8B73EA9BDA3A5467757942C268712F484AF53D9A9159D53E3
docs\proposals\m05-owner-contract-closure-runtime-ports-addendum.md|5D641BAA686BDF9BF6E77C559C2AE0AFE1306966FA392AC0E642F3C569690DED
sources\TerraFX.Runtime\TerraFX.Runtime.csproj|2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj|B8DFBC8438BD32A93ACB54DD420AD09A7BF259C2DB5D366D2100229D066CF583
sources\TerraFX.Runtime\Entities\World.cs|00FC348DBA47AA7E8B166B28FC7231CE411168FCF5E872A3E76781648F54D63A
sources\TerraFX.Runtime\Entities\WorldConstructionPlanControl.cs|7EBD30AD74D1BF889BD342CD395FB58217CDBF059309C153803EA5D2943D4744
sources\TerraFX.Runtime\Entities\WorldPreparedReplacement.cs|E14363D4707B8F3D3F92AB8241EC4D29B256DD2B9DB4F0D746DCD530BDC3FC23
sources\TerraFX.Runtime\Entities\World.PersistenceReplacement.cs|42F902FCD6D3FB719FAEDF462BC4522CE3F7639EC7D168AA95A2C27629AC9F3A
sources\TerraFX.Runtime\Hosting\WorldPersistence.cs|465614C0943947AD6FDFAD38C5920A0B4736310EB0F6E8118F5518CD76B3264B
sources\TerraFX.Runtime\Hosting\WorldLoadPlan.cs|67CA5C8BA0C9666C71657EBAD1184DFD93924D30AD88FA35A5C6E99877801B9A
sources\TerraFX.Runtime\Hosting\WorldLoadPreparedState.cs|533D03EF8FA2292AEE2B0CE9F48A8C5EA19CFF16B4ADA1A8422C2DFC97EDB25A
sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.cs|C6DFA0EE44438C005E115FE9A15FE51643CF49176FB266A59AE5CFE67250EFF5
sources\TerraFX.Runtime\Hosting\WorldReplacementOperation.Capture.cs|0B65EFA4D22D383BD93080FFC58622F5E4318D3296A147F8D997172BC9EF5C2D
sources\TerraFX.Runtime\Hosting\EngineHost.WorldReplacement.cs|2698F54CB5ABB8E90110E84A7D93C6B4FEFB32285C67403D30DB9D4A95C05647
sources\TerraFX.Runtime\Hosting\EngineHost.cs|A3FB2415E31CB7B717F2AD382E1FACF874B6AC392A469B6E71BA198DF6A11743
sources\TerraFX.Runtime\Hosting\WorldPlaybackControl.cs|B863CAE1E03EE1B54CD1FBC9A665237FD4435409B1BDE552793F8041E73EA413
sources\TerraFX.Runtime\Hosting\WorldRetainedHistoryControl.cs|67FA48F8D29AC05C39D2C974F7BB9206EB94B3E8DDC205B0684D5AF320852FC4
sources\TerraFX.Runtime\Integration\WorldPersistenceCaptureCoordinator.cs|4608EA6CB3D8EFE939683048D73D249F4F8FD9FEE57EE37B3AA044894531940C
sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs|B60C36D55EDD691A6C8442DB22F9A7995470F4FCE006D7578B24088A6496B508
tests\TerraFX.Runtime.Tests\Hosting\EngineHostPersistenceCommitTests.cs|6117160727FCC4CD3C88355064D889C9294D667A71BC19F8B74B38B33EA8CDA5
tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs|1784651E891E29D88E84508DC7C78AD105F42F85ACF1AE125772F170FD22A587
```

All four manifest memberships/digests and all 31 individual hashes were reverified unchanged after proposal creation. Review applies only to these exact bytes and this bounded scope. Reverify the observed HEAD, all four manifests and individual hashes before later disposition; changed inputs require reassessment. Input-hash inspection is not build/test, allocation/lifetime qualification or milestone acceptance.
