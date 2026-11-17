# M03-B Scene preparation finite-envelope and return addendum

**Status: tentative rectification proposal for later review, not a qualified footprint, implemented producer, maintained-contract amendment, or permission to execute the fail-closed candidate.**

## Disposition

The existing proposals do **not** close the concrete preparation blocker. The [owner-aware handback assessment](m03b-scene-preparation-owner-aware-async-handback.md), sections 6-8, deliberately selects an unavailable-capability prerequisite, not an implementation of it. The [supplier-return proposal](m03b-scene-preparation-supplier-return-closure.md) likewise supplies neither finite J/K nor a fulfilled return channel. Their safety conclusions remain valid.

This addendum supplies three missing decisions/inputs rather than proposing the stable root again:

- Record the verified preparation-versus-activation cap mismatch explicitly.
- Resolve a previously unexamined **input-proportional native fault-capture path**; a fixed exception count does not bound its bytes or work.
- Select an operation-specific, owned-invocation implementation route and give its funding, cleanup and release proof order. Do not select a decorator over the current stock builders, another completion observer, or a hypothetical CLR return notification.

The route below can advance prerequisite implementation under the user's tentative-unblocker policy. It cannot turn an unresolved native allocation or invocation suffix into a qualified producer. Keep `QualifiedFootprint` null until both obligations actually close. Chapter 35 already permits unrelated dependency-ready M03-B/M05 work; this proposal creates no new milestone gate or API/acceptance identifier.

## Current source findings

Source paths in this section are beneath `sources\TerraFX.Runtime`.

| Boundary | Verified fact and consequence |
|---|---|
| `Scenes\PreparedScene.CreatePreparation` | `QualifiedFootprint` is null; the throw precedes `world.AcquireGuidUse`. The current ordinary preparation candidate is not executable. Its fields do not establish an allowance. |
| `Scenes\PreparedScene.Preparation.cs`, `StartPlanPreparationCore` / `EndPreparationProducer` | `_supplierProducersUnjoined` is set before invoking the planner and has no clearing assignment. Removing the guard alone would not produce a draining implementation. Task readiness is not a valid replacement signal. |
| `Scenes\SceneLoader.StartPlanPreparation` / `StartResourcePreparation` | Captured funding delegates are constructed before the supplier calls. A returned storage reservation cannot retroactively fund those closures, the call envelope, or continuation registration. |
| `Scenes\ScenePrefabExpansionPlanner` | The closure includes `PrepareCoreAsync`, `PrepareTakingLeaseCoreAsync`, `BuildAsync`, `CountAndMeasureAsync` and the nested Task/ValueTask traversal identified by the existing handback assessment. Two top-level suppliers and three driver parts are not bounds on these invocations. |
| `Scenes\ISceneExpansionResourcePreparer`; `Rendering\Renderer.CpuExtractionSupport.cs`; `Rendering\RenderSceneAssetBundle.PrepareAsync` | The actual adapter awaits the bundle; the bundle awaits Atlas, Material/texture and Mesh/Skeleton helpers. A selected participant returning no render data has still invoked its producer. Only absence of the participant skips that role. |
| `Scenes\PreparedScene.Preparation.cs`, completion registration/callbacks | Both registrations use flowing `OnCompleted`. Callback-local part completion is inside the runtime's invocation/context boundary. The explicit `TrySet*` publisher supplies one useful returned-call boundary, not the missing supplier or callback-invoker join. |
| `Scenes\PreparedAssetsLifetime.Dispose` / `ReleaseLease` | The last reference clears `_value` before fallible physical disposal. A retained wrapper then need not retain the failed value. A wrapper/root reference alone is not a complete cleanup graph. |
| `Rendering\RenderSceneAssetBundle.Dispose` | Nested `finally` blocks return `_storageReservation` even when an entry release throws. A failed resource graph can therefore lose its funding. Construction's catch/finally ownership needs the same correction. |
| `Scenes\SceneExpansionPlan.Dispose` | Logical disposal is latched before child disposal. It preserves some failed child fields, but a second no-op Dispose is not an acknowledgment that their physical release succeeded. |
| `Entities\WorldSceneStorageReservation`; `World.ReleaseSceneStorageReservation` | The reservation contains bytes/World/workspace-control fields, not the retained-receiver pin required by workspace closure. World-close tolerance in the release helper is not proof that surviving R remains charged. |
| `Scenes\PreparedScene.TryReleasePreparationUse`; `Entities\WorldGuidUseControl.Release`; domain/process release helpers | The root clears its own state before successful lower release. The lower control still accesses its anchor/state after process/domain release, and domain release can publish its retirement Task after removing the operation interest. These suffixes and partial-release ownership require separate proof. Do not restore the superseded claim that the root itself writes fields after successful `use.Dispose()`. |

The active-workspace-to-storage transaction now exists in `WorldAdmissionDomain.TryTransferActiveWorldGuidUseWorkspace`. Do not repeat the workspace proposal's historical assertion that only free-capacity transfer exists. Its presence does not establish receiver survival, complete allocation funding, or final-return safety.

## Verified cap contract/implementation mismatch

The pending bytes implement **two separately admitted envelopes selected by one option**, not just the whole-preparation cap described by the maintained diff:

1. `SceneLoadOptions.MaximumPreparationBytes` has XML documentation saying “Scene preparation and per-activation byte budget”; its default is `67_108_864`, valid range `[1, 2_147_483_648]`.
2. Freeze copies the selected value into `SceneLoadOptionsSnapshot`; `PreparedScene` stores it independently of remaining workspace.
3. `CaptureActivationHeader` returns that captured P without borrowing the body. `SceneLoader.ActivateAsync` passes it directly to `_world.AcquireGuidUse(activationWorkspaceLimit)`, before queue admission. Each activation attempt therefore independently requests `256 + P`, while preparation/retained storage may still be charged.
4. The **current diff** in Chapter 16, section 2, calls the option an “explicit per-operation whole-preparation cap.” Chapter 30's added declaration and prose at the Scene-loading description/completeness table describe `PrepareAsync` selecting W; they do not specify that this option also selects every activation attempt's W. The surrounding statement that activation has a separate use does not define its byte selection.

[Budget accounting, section 9](m03b-scene-preparation-budget-accounting.md#9-concrete-tentative-rectification-captured-cap-and-funded-delivery-tail) explicitly proposes `W_a = P`. That explains the code, but a tentative proposal and source XML do not silently amend Chapters 16/30. This is a verified contract/documentation gap, not evidence that the option is currently preparation-only in code.

**Recommended later reconciliation:** retain that existing tentative selection, with separate preparation and per-attempt activation admission, unchanged P/default/range, and unchanged activation delivery-tail ownership. Before claiming contract conformance, explicitly reconcile the option's dual role in its maintained owner/public descriptions under a separate authorized change. This task changes none of those descriptions. Do not “repair” the mismatch with zero-cap activation, shared release rights, a smaller adaptive P, or a new option.

**Distinguishing static example:** with fully covered `C = R` transfers and no intervening release, preparation plus one activation reserves `2*P + 512` above the earlier baseline, counting transferred R once. At the default this is **134,218,240 bytes**. A cheap activation can refuse solely because its independently selected P will not fit. Preparation success does not promise activation headroom. This is derived accounting, not an executed observation.

## Additional finite-K evidence: native capture is not just an EDI holder

The inspected local reference CoreLib is RC `11.0.0-rc.3.26522.148`, product commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. The public source below was fetched at that commit and inspected read-only. This identifies source semantics for that reference lane, **not the CLR loaded by a future testhost**.

| Pinned source under `src/runtime` | Relevant allocation/work |
|---|---|
| [`src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs:145-187`][holder] | Single/enumerated exceptions are captured into new EDI objects; an existing EDI can instead be stored directly. These are different paths. |
| [`src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs:239-249`][exception] | `CaptureDispatchState` calls `ExceptionNative_GetFrozenStackTrace`. Managed fields alone do not describe that operation's allocation. |
| [`src/coreclr/vm/comutilnative.cpp:83-118`][native] | Capture first calls `ExceptionObject::GetStackTrace`, then marks the returned stack frozen. Looking only at the subsequent freeze misses preceding work. |
| [`src/coreclr/vm/object.cpp:1629-1738`][object] | A nonnull stack whose recorded thread differs from the current thread enters `GetStackTraceClone`. It allocates a new stack at the **old capacity**, copies/scans frames, and can allocate a keep-alive array at the **old array capacity**. |
| [`src/coreclr/vm/object.h:1875-1908`][object-header] | `GetObjectThread` returns the recorded thread; freezing stores the sentinel `(Thread*)1`. “Already frozen” is not a source-level exemption from that mismatch test. |

Consequently, a fixed number of original exceptions does not establish a useful fixed all-path K. The new clone bytes and copy/scan work depend on preexisting stack/keep-alive capacities. Retaining the foreign exception graph is distinct from paying for newly created copies. This source proves an input-dependent allocation path; it does **not** establish mathematical unboundedness of CLR objects, a numerical maximum for this product lane, or that a particular Scene execution took it.

The first finite-K work item is therefore exact and bounded: follow those two allocation sites and their callers, establish their admissible capacities/multiplicities/coexistence, and identify a real **before-allocation** grant. Include the current child `GetResult` fault/cancel rethrows and the two-original-exception public publication path. Do not run live preparation to discover the amount.

If existing admitted inputs cannot yield a useful bound fitting P, a larger fixed K is not the remedy. A fulfilled runtime-owner solution is required: either budgeted capture with a legal preserved-fault disposition, or a non-copying relay of already captured dispatch state with explicit lifetime ownership. The latter must preserve the original exception identities/order and selected token; it still owes the destination's finite holder/list/publication provision. Current public `TaskCompletionSource<PreparedScene>.TrySetFromTask` requires a `Task<PreparedScene>`: the planner's `Task<SceneExpansionPlan>` and resource `Task<IDisposable?>` do not supply that capability. Fault extraction followed by `TrySetException` is not a no-copy relay.

Neither capability is asserted to be installed, and this document introduces no API declaration for it. In particular, denial of a dynamic capture claim must not silently replace the required original fault, recursively attempt the same unfunded capture, or report a successfully completed Task. If a legal funding/relay implementation cannot be specified, this precise publication boundary remains blocked. The external-observer dispatch rider does not cover it.

## Selected return-ownership implementation route

Do not extend the current candidate by clearing the supplier latch in a completion callback. Replace the unowned Scene invocation boundary rather than trying to observe it afterward.

**Tentative selection:** use operation-specific, explicit non-async progress state in the existing planner/builder and Rendering construction owners, driven by owned ordinary invocations. Retain the current public Task surface and real Assets I/O. This is an extension of the existing staged Scene driver, not a reusable task-like type, method-builder framework, scheduler, worker, ambient grant scope, or second planner algorithm. Chapter 02's async-abstraction restriction must be assessed against this concrete scope before implementation; it is not silently waived.

The required construction/invocation sequence is:

1. **Preclaim the call, then construct it.** From the exact existing issuer, claim the whole cursor/callback/context/registration peak before creating it or invoking a helper. Replace self-funding captured delegates with the workspace proposal's explicitly prepaid funding port. Keep one stable root and its original preparation use. A port alias conveys no use-disposal right.
2. **Make the actual Scene-owned supplier closure explicit.** Convert the ordinary planner's counting, selector/cache, root-count-fill and expansion suspension points, not only its outer Task methods. Do the same for the real resource adapter, bundle, Atlas/page, Material/texture and Mesh/Skeleton chain. Reuse shared synchronous semantic steps with restore/measurement; do not duplicate traversal or allow null-as-unlimited funding. Restore/measurement retain their own actual Persistence issuer, not public preparation P.
3. **Separate lower readiness from owned invocation return.** Stage returned values and partial leases in the existing rooted owner before fallible validation/attachment. A pending lower Task may supply readiness only. Any Assets machinery excluded from Scene X must have a named, independently funded Assets lifetime and no remaining Scene-grant access; decoded-byte/lease-count checks alone do not establish this. Otherwise that call envelope remains an unresolved prerequisite.
4. **Own every Scene resumption, including the root's two Actions.** An ordinary outer thunk must enclose the complete semantic step and its context scope, then clear the step's context/awaiter/task/input aliases, and only then hand back that invocation. Initial `AsyncTaskMethodBuilder.Start` already provides a usable initial context-return boundary; it is not a resumption solution. For resumed work, a non-flowing wake plus explicitly owned context invocation is a candidate implementation, **not** a mechanical `OnCompleted`→`UnsafeOnCompleted` substitution.
5. **Specify context before choosing registration.** Prove initial isolation, nondefault/default/suppressed ExecutionContext, AsyncLocal notifications/reentrancy, changes made inside a step, restoration, and lack of arbitrary caller SynchronizationContext capture. Public `ExecutionContext.Capture()==null` under suppression is not the stock builder's internal sentinel. Every supported branch needs a concrete public mechanism and funded holder; no unowned default-context bootstrap or late ambient lookup is assumed.
6. **Join registration and invocation separately.** Reserve the exact pending registration/role before exposure. Retain the registering invocation through registration return even if its callback runs first. Serialize cursor mutation and prevent clearing/reusing a slot while an older invocation can access it. Derive the number of coexisting invocations from the actual control flow; “one pending asset read” does not bound registration-return overlap. Uncertain registration failure retains the original obligation.
7. **Hand back only as the final owned operation.** After all context/publication scopes return and owned aliases end, the invoker transfers the exact original claim/role once to the root, outside gates. Its remaining managed/native suffix must neither access the retired grant's state nor allocate Scene-owned storage, including exception/tracing paths. Prove the enclosing raw Task/ThreadPool suffix as well; moving the signal into another Action does not close it. A branch without that proof stays unsupported, not normally quarantined.

This route removes Scene-owned compiler boxes rather than decorating their still-opaque invokers. It is not yet a qualified cursor implementation. A final compiled graph retaining any such Scene-owned async box must either supply its own actual owned-invoker proof or fail this route's closure. Standard Task terminality, source `finally`, GC, elapsed time, `WhenAll`, another observer Task and profiler method-leave events remain insufficient.

## Cleanup and final-return sequence

Close these lower ownership prerequisites before integrating the producer; they can be implemented and checked through the real lower owners without executing the unavailable Scene candidate:

1. **Keep failure roots before fallible work.** Each plan/bundle/lifetime retains its value, loaded-entry counts, current cleanup item and exact grants before acquisition/release. Clear only a successfully released/transferred item. Independently safe siblings may clean; retain failed items and bounded debt. Logical disposal prevents reuse/retry but does not acknowledge physical cleanup. Correct the currently early `_value` clearing and reservation-returning `finally` paths.
2. **Keep R independent of preparation.** Implement the existing workspace proposal's exact-reservation receiver link/pin, prepaid control and post-World-close segment accounting before X→R transfer. Preserve active-claim transfer and exact rollback provenance. A retained bundle or unused prepared result surviving World disposal must retain its own R/root; preparation U cannot be extended or reacquired as a substitute.
3. **Make lower release an owned terminal transaction.** Before removing the final operation funding/root, finish or validly transfer all control-field clearing, deferred retirement, domain-notification publication and failure bookkeeping. Use existing process/domain/retirement owners in their established lock order; no callback, completion or allocation runs under their gates. If another existing owner assumes a suffix, identify its original grant, actual transfer transaction and final end. The control's 256-byte charge is not automatically funding for a new retirement mechanism.
4. **Record irreversible progress, not an imagined atomic drain.** Process release can succeed before domain release fails. Preserve the still-live domain anchor and exact remaining interests/debt; do not recreate a process U, retarget a successor, or refund remaining claims. A failure after *both* roots were removed cannot be repaired merely by calling `Quarantine`; its safe retention must be arranged before that point.
5. **Release the preparation once.** Require returned public publication, ended ordinary parts, every invoked supplier/invoker returned or validly transferred, and actual body cleanup/installed transfer. An absent resource role is unstarted, not returned; a selected null-result role still owes return. Quarantined retained storage is not body Ended. Only then elect one final releaser. Preserve the current root's successful no-root-access-after-lower-release ordering and qualify the lower transaction's own suffix.

Body disposal/admitted-body return and supplier return remain separate facts. Queue refusal ends neither preparation body ownership nor its producer claim. The activation request returns its delegated body through the exact root; its separately funded activation publication tail is not a source of preparation funding.

## Finite catalogue, work and enablement obligations

Keep the four ledgers from [footprint closure](m03b-scene-preparation-footprint-closure.md) and supplier X/receiver R from [workspace closure](m03b-scene-preparation-workspace-closure.md). No numerical J/K is established here.

| Partition | Required proof before use |
|---|---|
| `J_shared` | Whole final root/control carrier and supported synchronization/lazy state, retained through the producer/body/release join. Old constructor-only measurements do not identify a changed layout. |
| `J_producer` | Final fixed driver, TCS/Task, callbacks and loader-owned invocation/registration/context storage. Distinguish borrowed immutable input references from newly created holders/copies. Include cold and overlap paths. |
| `K_body` | Protected cleanup/return capacity, bounded original-cleanup sampling and exact failure debt; actual retained failed graphs remain on their original X/R grants. Include the cleanup invoker's final work. |
| `K_publication` | Exact success/fault/cancel/refusal overload graphs, source-array/enumeration/copy/list/EDI/native capacity, cleanup overlap and publication/release failure. The native clone row above must have a real disposition, not a guessed constant. |
| Variable supplier X | Every cursor/traversal/cache/helper/call/registration capacity before construction/growth, using admitted input dimensions and actual simultaneous invocation count. Do not bury this graph in fixed J or “two suppliers.” |
| Retained R | Only identified receiver-owned capacity plus its funded retirement control; not counting scratch, publication work or an ended preparation use twice. |

Every row records creating site/type, checked capacity/multiplicity, independently derived bytes, copy/scan/cleanup work, coexistence, original issuer/grant, rooted path, last access/transfer and the handback's own suffix. Enforce `P = F + X + T`; remaining preparation operation charge is `256 + F + X`, with T cumulatively transferred capacity. Each newly materialized capacity is claimed before allocation. Returning a live claim to free F and reacquiring it as R is not a transfer.

J/K are byte provisions, not a newly invented timeout or CPU-work cap. Prove finite engine-created iteration/registration/cleanup demand from admitted counts and claimed capacities, including native input-dependent copying. Do not infer this from finite sampled allocation totals. External scheduler/notification code can block indefinitely; that supplies no refund or finite-time guarantee. Any ownership exclusion for that code must be explicit and must not exclude Scene-created invocation/restoration holders.

**Ordered enablement:** resolve the cap owner selection and native-capture funding/relay disposition; implement lower cleanup/receiver/terminal-return ownership; compile the actual owned-progress candidate with the guard still closed; freeze its complete source/compiler/runtime/native closure; derive and install its real claims; only then execute funded integration. A missing finite native row, lower owner or post-last-access edge is a hard stop, not authorization to install a provisional constant.

### Required future observations

Use ordinary owning-project builds and focused NUnit runs, Debug before Release, as described in `docs\project\Building.md`; confirm fresh nonzero selected/executed TRX results. The existing `PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` source contains an **activation** scheduler hold, nonempty binding and pixel-17 retention witness. It is not an executed preparation/invoker-return test in this assessment.

- Hold a real supplier after outcome readiness but before its owned return, and separately hold a root completion thunk before context/registration return. Body disposal or completed activation must not refund their original grants. Release the stops and observe one joined refund.
- Hold public `TrySet*` dispatch after terminal visibility. End/transfer the body while shared/producer/publication provision stays charged. Keep registration-before-return and public-promise-escape cases distinct.
- Reach catalogue-derived exact/one-byte-short claims with earlier setup funded. Cover cold/warm, actual synchronous/suspended, no participant/selected null-result/real-resource, original fault plus cleanup fault, selected-token cancellation, uncertain registration and post-close cleanup. A bootstrap refusal is not a later native/cache/resource claim witness.
- Demonstrate retained R surviving World close, final-lease release, and actual cleanup/return failure retention where safe real stimuli exist. Unsupported stimuli remain open; do not add a throwing fake supplier, synthetic OOM, test-only yield or fabricated Scene.
- Verify the selected dual-cap behavior separately: each attempt admits its own unchanged `256 + P`, queue refusal preserves the preparation, and held preparation/activation tails remain independently charged.

Allocation-complete corroboration remains subject to the existing [evidence-capability proposal](m03b-scene-preparation-allocation-evidence-capability.md); sampled GC ticks are not a substitute. Bind the actual loaded CLR and final native code versions, not just SDK/muxer versions. Other runtime/instrumentation modes, actual public consumers, NativeAOT and Chapter 35 acceptance remain separate, mandatory where applicable.

## Scoped input identity and limits

Snapshot: `2026-11-17T20:35:11Z`, HEAD `22d9e48f9cf028d4b52033b8411f91c80d930691`. The 59 direct local inputs are the following exact set; brace lists expand only the listed names. SHA-256 is over sorted repository-relative Git-style paths, each followed by NUL, lowercase raw-file SHA-256 and LF, UTF-8 encoded. Set digest: **`05fe8c05f0ea0f36b9060a8eedc30b650511bf47f3041f6a106e24a453249f10`**.

```text
.github\{copilot-instructions.md,workflows\ci.yml}
global.json
{Directory.Build.props,Directory.Build.targets}
sources\{Directory.Build.props,Directory.Build.targets}
tests\{Directory.Build.props,Directory.Build.targets}
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
docs\project\{Building.md,Roadmap.md}
docs\design\{02-dotnet-api-memory-and-ownership,04-capacity-and-workload-profiles,11-assets-import-streaming-and-packages,12-entities-components-and-queries,13-jobs-and-frame-pipeline,16-scenes-transforms-and-cameras,29-project-setup-build-and-deployment,30-public-api-review-surface,34-engine-acceptance-cases,35-implementation-roadmap}.md
docs\proposals\m03b-scene-preparation-{allocation-evidence-capability,budget-accounting,compiled-producer-evidence-addendum,footprint-closure,handoff-closure,owner-aware-async-handback,supplier-return-closure,workspace-closure}.md
docs\proposals\m05-m03b-owner-contract-closure.md
sources\TerraFX.Runtime\Scenes\{PreparedScene,PreparedScene.Preparation,SceneLoader,SceneLoadOptions,SceneLoadOptionsSnapshot,ScenePrefabExpansionPlanner,ISceneExpansionResourcePreparer,PreparedAssetsLifetime,SceneExpansionPlan,SceneActivationRequest}.cs
sources\TerraFX.Runtime\Rendering\{Renderer.CpuExtractionSupport,RenderSceneAssetBundle,RenderAtlasPageLoader,RenderMaterialTextureLoader,RenderMeshAssetBinding}.cs
sources\TerraFX.Runtime\Entities\{WorldGuidUseControl,WorldAdmissionDomain,WorldGuidRegistry,WorldSceneStorageReservation,WorldDomainReservation,World}.cs
sources\TerraFX.Assets\AssetStore.PackageLease.cs
tests\TerraFX.Runtime.Tests\Scenes\{PreparedSceneAssetRetentionTests,SceneLoadOptionsTests,SceneSupportRegistrationTests}.cs
.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\{.version,System.Private.CoreLib.dll}
```

Pinned upstream raw-byte SHA-256 values for the additional native/fault conclusions:

| Linked input | SHA-256 |
|---|---|
| `TaskExceptionHolder.cs` | `bc4dda204de133879e4e334b37f74fc6c57690c05a179ae4648fbfa653a38604` |
| `Exception.CoreCLR.cs` | `44ac1884bcfb3857679bd0865cd9f2eb49192e0196f14c837f2aa1d043f985b7` |
| `comutilnative.cpp` | `13efef9576d111d7f11134055b3745e528f5b072127bb1ff0237eae7874b73da` |
| `object.cpp` | `2f2dc344290dd316a704eede537dfde34288fbc71cab04006ae379796c9f77c7` |
| `object.h` | `a345711a7d7010b4bab264a10f8c1fdd4fde19544d3ecc923ee4f4a19ab380aa` |
| `TaskCompletionSource_T.cs` | `069571daddeffd14ed006b8e552b3541e61b7c79ba7e6a142037e59eadae1369` |

This is a scoped evidence identity, not a repository inventory, transitive runtime proof or acceptance receipt. No new schema or fixture bytes are inputs to this proposal. Current dirty-file bytes, including dirty maintained contracts, are covered by the scoped digest where listed; their inclusion does not accept those changes.

The initial 79 dirty/untracked paths had aggregate digest `cf6d32dbadabe05af2dc591466ea87f8264d8b1e3623c2bbd4b6d05d16b22293` using the same path/NUL/hash/LF algorithm over `git ls-files -m -o --exclude-standard -z`. Before this file was authored, concurrent work had increased that set to 82 paths with digest `bdb45a10252bb5cab7f9b440ffc6f8bb2288c486e610baf083ab0a79bbb04c78`. Those changes were left untouched; no full-worktree equality with the initial baseline is claimed. Recheck the scoped digest before applying these source-specific dispositions; a mismatch requires reassessing the affected input, not replacing the expected digest.

Only this new proposal is authored. No source, test, maintained-contract, roadmap, existing-proposal or schema edit, build/test run, Scene execution, allocation experiment, native/AOT observation, commit, push or remote mutation is part of this task. Earlier proposals' run claims are not imported as current evidence.

[holder]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs#L145-L187
[exception]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs#L239-L249
[native]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/comutilnative.cpp#L83-L118
[object]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.cpp#L1629-L1738
[object-header]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.h#L1875-L1908
