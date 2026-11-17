# M03-B Scene preparation owner-aware async handback

**Status: tentative blocker assessment and proposed owner disposition, not an accepted contract, implemented handback, J/K catalogue, qualification, or permission to execute Scene preparation. `QualifiedFootprint` remains unavailable.**

Scope: Windows x64, managed `net11.0`, Debug and Release, with the compiler-generated async machinery identified below. Other async/runtime modes and targets require separate proof.

## 1. Decision

There is **no safe completion-observer repair for the current suppliers**. Their Tasks expose an outcome before the supplying state-machine invocations and Scene-created continuation/context invocations necessarily finish. The missing fact cannot be recovered by another Task, a supplier `finally`, a counter decremented inside those invocations, or a method-builder decorator that delegates resumption to the stock builder.

There is an important limit to that conclusion: **“no implementation is possible using public .NET APIs” would be too strong.** Public compiler hooks permit a replacement builder that owns its own state-machine storage and invokers. Such a replacement need not obtain a CLR notification about the stock builder's return; it would replace that invocation boundary. It is not, however, a small adapter around the existing producers. It requires a different, transitive async-lifetime implementation, including the root's ordinary completion Actions. Chapter 02 currently expressly prohibits engine-specific async abstractions. No complete implementation, preserved-context proof, or finite allocation catalogue for that replacement exists in the inspected inputs.

Consequently, do not select a speculative direct builder as an available unblocker. Select the **prerequisite-only owner disposition in section 7**: supported Scene preparation requires a fulfilled, funded supplier/invoker handback capability, and the current lane lacks it. Unrelated dependency-ready M03-B/M05 implementation can already continue under Chapter 35; it does not require making this guard pass.

This assessment neither establishes an unconditional impossibility theorem for arbitrary replacement async implementations nor suggests waiting for a new CLR API as the only possible future solution. The precise impossibility result below concerns the **existing Task/ValueTask producers and their public outcome interfaces**.

## 2. Current in-repository evidence

Source paths in this table are relative to `sources\TerraFX.Runtime`. They identify working-tree bytes, not just HEAD.

| Boundary | Fact and consequence |
|---|---|
| `Scenes\PreparedScene.cs:80-115` | `QualifiedFootprint` is null, and `CreatePreparation` throws before `world.AcquireGuidUse`. The reported 143 Runtime Debug failures at this guard do not establish 143 independent producer defects. They are supplied run evidence, not a suite rerun by this assessment. |
| `Scenes\PreparedScene.Preparation.cs:70-89` | The ordinary entry invokes `PreparationEntry` through `AsyncTaskMethodBuilder.Start` without requesting its Task, then ends the entry part after `Start` returns. This really encloses initial synchronous context restoration. It does not own the suppliers' later resumptions. |
| `Scenes\PreparedScene.Preparation.cs:205-234,691-718` | `_supplierProducersUnjoined` is set before invoking the planner and has no clearing assignment. The permanent hold is deliberate; readiness cannot safely replace its missing return fact. |
| `Scenes\SceneLoader.cs:231-278` | Ordinary planner/resource invokers construct captured funding delegates and return supplier Tasks. There is no separate return channel. Their call-envelope allocation must be funded before those constructions, not by the storage receiver returned later. |
| `Scenes\ScenePrefabExpansionPlanner.cs:142-237,769-928,1318-1348` | The ordinary route includes `PrepareCoreAsync`, `PrepareTakingLeaseCoreAsync`, `BuildAsync`, and `CountAndMeasureAsync`. Resource cleanup and phase-slot acknowledgments do not acknowledge generated producer return. |
| `Scenes\ScenePrefabExpansionPlanner.cs:1350-2200` | The nested traversal is **not Task-only**: counting, overrides, expansion, root-count filling, Prefab lookup, and selector traversal include `async ValueTask`/`ValueTask<T>` methods. Their potentially suspended implementations belong to the same closure. Decorating only the outer Task methods misses them. |
| `Scenes\ISceneExpansionResourcePreparer.cs:10-21`; `Rendering\Renderer.CpuExtractionSupport.cs:174-183` | The actual built-in resource adapter is itself async and awaits the async bundle producer. An adapter returning a Task is not an enclosing invoker-return notification. |
| `Rendering\RenderSceneAssetBundle.cs:119-382`; `RenderAtlasPageLoader.cs:25-92`; `RenderMaterialTextureLoader.cs:26-155`; `RenderMeshAssetBinding.cs:15-32` | Bundle preparation awaits nested Atlas/page, Mesh/Skeleton, and Material/texture helpers. Even the no-render-data return at bundle lines 132-134 passes through the invoked adapter/bundle methods. Only absence of the participant skips the resource role; a selected participant returning a null result has still run. |
| `Scenes\PreparedScene.Preparation.cs:393-483,605-663` | Both root callbacks obtain a Task result and end their ordinary part inside the callback. Both registrations use `ConfiguredTaskAwaiter.OnCompleted`, not `UnsafeOnCompleted`. Registration return is covered by its registering part; callback-return/context suffixes are not covered by the callback's own decrement. |
| `Scenes\PreparedScene.Preparation.cs:502-584` | The explicit publisher can observe the selected `TrySet*` returning and clear its promise/fault references. This is a useful real boundary, but does not join other producers or enclose the callback's runtime invoker. |
| `Scenes\PreparedScene.cs:675-719`; `Entities\WorldGuidUseControl.cs:272-321` | Root state is cleared before the successful lower use-release call. The control still accesses its own fields after process/domain release calls. A new producer protocol would not automatically qualify this separate release suffix or its partial-release failure ownership. |

The planner's relevant nested ValueTask members are `CountEntitySubtreeAsync`, `CountOccurrenceAsync`, `CountOverrideLayerAsync`, `CountPlacementAsync`, `ExpandEntityAsync`, `ExpandEntityChildrenAsync`, `ExpandOccurrenceAsync`, `ExpandPlacementAsync`, `FillEntitySubtreeRootCountsAsync`, `FillOccurrenceRootCountsAsync`, `FillPlacementRootCountsAsync`, `GetPrefabAsync`, and `GetSelectorTargetSourceEntityAsync`. This is a source-derived member set, **not** a bound on simultaneously live invocations. Recursion, registration/dispatch overlap, and suspended ancestors require an independent peak derivation.

The package/restore and measurement routes remain distinct consumers of shared helpers. They must not acquire an ordinary-preparation grant implicitly or become unfunded when `preparationOwner` is null.

## 3. Runtime evidence and its scope

The inspected local CoreLib has product version `11.0.0-rc.3.26522.148+5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. The public runtime source links below are pinned to that VMR commit, not `main`. Paths in this table are beneath its `src/runtime/src/libraries/System.Private.CoreLib/src` directory.

| Pinned source | Relevant ordering |
|---|---|
| [AsyncTaskMethodBuilderT.cs:643-735][builder-outcome] | `SetResult`/`SetException` complete the Task. They do not enclose the invoking state machine's subsequent return and the box's cleanup. |
| [AsyncTaskMethodBuilderT.cs:510-579][builder-invoker] | The actual box invokes `StateMachine.MoveNext` directly for its flow-suppressed sentinel, or through an ExecutionContext runner. Only afterward does it test completion and clear `StateMachine` and `Context`. These are real post-outcome accesses, not inferred object sizes. |
| [AsyncValueTaskMethodBuilderT.cs:45-105][value-builder] | Suspended generic ValueTask methods use the Task builder's await/box machinery; fault propagation also delegates to it. A synchronous inline ValueTask result does not establish safety for the suspended branch. |
| [AsyncMethodBuilderCore.cs:20-55][builder-start] | `Start` restores changed SynchronizationContext and ExecutionContext after `MoveNext`. A signal from inside `MoveNext` precedes that restoration. |
| [TaskAwaiter.cs:438-450,520-532][awaiter] | Configured awaiters' `OnCompleted` requests ExecutionContext flow even when `ContinueOnCapturedContext` is false. Changing it to `UnsafeOnCompleted` drops that request; it is not an equivalent ownership fix. |
| [Task.cs:2638-2698][task-registration] | Flowing an ordinary Action continuation creates an `AwaitTaskContinuation`. The registration/completion race can dispatch it before the registering call returns. |
| [TaskContinuation.cs:634-728][action-invoker] | Inline invocation can restore context after the Action returns. Queued invocation with a captured nondefault context uses `RunForThreadPoolUnsafe`; its comment explicitly leaves context reset to the dispatch loop. |
| [ExecutionContext.cs:309-363][context-tail]; [ThreadPoolWorkQueue.cs:994-1018][pool-tail] | ThreadPool context reset occurs after work-item execution and can call AsyncLocal change handlers. A `finally` in the root Action is inside this boundary. |
| [ExecutionContext.cs:54-98,152-221][context-run] | Public `Capture` returns null for suppressed flow; the stock builder instead uses internal `CaptureForSuspension` and a special suppressed sentinel. Public `Run` rejects null. A replacement must specify suppressed-flow behavior rather than treating null as an ordinary captured context. |
| [IAsyncStateMachineBox.cs:9][internal-box]; [AsyncMethodBuilderAttribute.cs:11-20][builder-attribute] | The stock box interface is internal. Conversely, method-level builder selection is public, so replacing invocation ownership is possible in principle; it is not a public hook into the stock box's final return. |

There is also a useful positive boundary: the ordinary, no-context raw-Action path in [TaskContinuation.cs:741-771][raw-action] restores `Task.t_currentTask` after the Action; it does not perform ExecutionContext restoration there. This is a possible *input* to a future direct-invoker suffix proof, not a reason to consider the current flowing registrations joined. Queued, tracing, failure, enclosing-producer, and native-code suffixes still need coverage.

Read-only PE metadata inspection of the current Runtime DLLs, without loading or executing TerraFX code, confirmed stock `AsyncTaskMethodBuilder<T>` fields in the planner core/build producers and bundle producer, and stock `AsyncValueTaskMethodBuilder<T>` fields in `CountOccurrenceAsync` and `GetPrefabAsync`. The inspected planner machines are classes in Debug and value types in Release. These observations prevent assuming that Release's inline state layout also covers Debug's separately constructed machine.

| Inspected artifact | SHA-256 |
|---|---|
| `artifacts\bin\sources\TerraFX.Runtime\Debug\net11.0\TerraFX.Runtime.dll` | `83FCC7A4ECFC407084B67DB6F6D20E4E2A1A6A2AA424E97D99041D105C894FAD` |
| `artifacts\bin\sources\TerraFX.Runtime\Release\net11.0\TerraFX.Runtime.dll` | `6D0C8F84E1C473CBBDFAEF3E986A9AA697D651D4D509620106C23AD2EE2D5B7B` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Private.CoreLib.dll` | `DF53885902E896A2B40988237CBE9CBB9F1BB0743200F05FE9257C56A29C5732` |

These Runtime DLL hashes differ from the earlier stable-root compile record. That record must not be presented as compilation/qualification of these DLLs. Metadata inspection establishes selected fields, not complete source-to-binary provenance, IL/native suffixes, allocation bounds, or executed behavior.

`global.json` selects the local RC SDK; the present `dotnet --info` reports that SDK but a **GA 11.0.0 muxer host**. The test runtimeconfig requests the RC framework. Neither fact proves which CoreCLR a future testhost actually loads. Qualification must identify its loaded CoreLib/coreclr/clrjit and code-generation settings. This document's runtime-source ordering analysis is scoped to the pinned RC source; it is not GA, Runtime Async, diagnostic-mode, other-platform, or NativeAOT qualification.

## 4. Precise impossibility result for the existing boundary

Let a supplier's original claim cover its Scene-owned generated frame/box and required invocation/context suffix. Consider a genuinely suspended successful planner:

1. Its stock builder makes the supplier Task terminal.
2. Before the enclosing ExecutionContext runner returns or the box clears its state, another thread runs the root's registered completion Action.
3. The Action obtains the plan, prepares any next stage, publishes the public outcome, and ends its own part. A public consumer can finish body disposal concurrently.
4. The supplier remains stopped before the box's writes at pinned builder lines 578-579.

Compare this with a schedule in which the supplier has already passed those writes. All outcome facts exposed by the supplier Task can be identical: result, status, exception/cancellation information, and any observer Task's completion. No public Task completion operation exposes whether those later box/context instructions have happened.

Thus a decision based only on those facts cannot distinguish the two schedules. If it returns the original claim in the completed schedule, it can return it in the stopped schedule while funded accesses remain. If it never returns it without additional information, the preparation use cannot drain. A timeout, scheduler preference, additional yield, or finite chain of observer Tasks adds no required happens-before edge.

This counterexample needs only one supported suspended success path to disprove a universal repair. Fault/cancellation completion uses the same problematic enclosing invocation. All-synchronous success may have a useful ordinary caller-return boundary, but testing `IsCompleted` cannot prove that a terminal Task took only that branch.

Specific purported repairs fail for distinct reasons:

| Proposed signal | Missing boundary |
|---|---|
| Supplier source `finally`, custom awaiter's `GetResult`, or root `GetResult` | Source cleanup/outcome consumption is inside or concurrent with the generated producer and its invoker. |
| Decorator builder's `SetResult`/`SetException`, even **after** delegating the completion call | The generated `MoveNext` and stock box/context invoker have not necessarily returned. |
| Proxy state machine's `MoveNext` `finally` passed to the stock builder | The proxy is still inside the stock box's context runner and subsequent state clearing. |
| Public `AsyncTaskMethodBuilder.Start` around initial invocation | It encloses that initial call, not subsequent invocations registered through the stock builder. The current ordinary entry already uses the valid initial boundary. |
| `ContinueWith`, an awaited wrapper, `WhenAll`, asynchronous-continuation options, or `TaskCompletionSource.TrySetFromTask` | They observe/propagate outcomes, not return of the antecedent's producer. [TrySetFromTask is available on this framework][outcome-copy]; its richer outcome propagation does not make it a join. |
| Root callback's final decrement or a callback-local `finally` | The enclosing `AwaitTaskContinuation` and possibly ThreadPool context reset remain outside it. |
| Polling GC roots, weak-reference disappearance, profiler method-leave events, or another heap sample | None is a production acknowledgment from the actual invoker. They neither introduce the missing ownership edge nor safely authorize claim return. |

The proof does **not** demand physical stack return when an independently proved suffix neither accesses grant-owned state nor allocates Scene-owned state. It identifies positive remaining accesses in the stock supplier path and unjoined context work in the current root callback path. Nor does it rule out replacing these invokers, a separately funded ownership transfer, or a different approved execution protocol.

## 5. Direct builder/invoker assessment

A genuine direct builder is materially different from a decorator. Its `AwaitOnCompleted`/`AwaitUnsafeOnCompleted` would register an Action targeting **its own** invocation object; that object would directly invoke the generated machine. It would not pass the machine to a stock builder's await methods and then claim to enclose the resulting stock box.

The abstract valid sequence would be:

```text
actual caller preclaims and retains the original invocation grant
  -> compiled async entry, including any Debug machine allocation
  -> owned initial/resumption invocation
  -> generated outcome recorded without returning the grant
  -> MoveNext has returned/unwound
  -> every relevant publication call and context scope has returned/unwound
  -> generated state, context, awaiter and result aliases are ended/transferred
  -> entry/registration/overlapping invocation/child obligations are joined
  -> exact original grant handback is the invoker's final owned operation
  -> independently proved no-grant-access/no-Scene-allocation suffix
```

This is a proof obligation, not executable candidate code or a selected implementation. In particular, completing a private/public promise introduces its own publication interval; moving it below `MoveNext` does not eliminate that interval or permit restoring context too early.

| Obligation | Why a small direct-builder patch is not closed |
|---|---|
| **Binding and construction order** | Compiler-called `Create()` receives no root/grant parameter. Caller funding must precede the compiled entry, including Debug machine construction. An explicit binding protocol would have to connect the exact root, role, child invocation and original claim before any escape. An ambient AsyncLocal/TLS carrier is neither automatically funded nor automatically correct across nesting, reentrancy, suspension and thread changes. A private first-binding awaiter is possible language machinery, not something already installed. |
| **Every actual invocation** | Replacing only the outer planner/resource builder leaves nested Task and ValueTask boxes unjoined. Shared restore/measurement helpers need their own real caller authority, not a null/unlimited branch. Independently funded Assets producers can retain their own epilogues, but that ownership must be established at the boundary; Assets must not acquire a Runtime callback/dependency. |
| **Generated state clearing** | The direct invoker must own the complete machine storage, correctly handle Debug reference machines versus Release value machines, clear the actual retained storage rather than a copy, and cover generated kickoff/Task-getter and caller-return aliases. `SetResult` is not authority to clear a currently executing machine. |
| **ExecutionContext and SynchronizationContext** | Capture, entry isolation, resume behavior, suppression, changes inside the supplier, notification reentrancy, and restoration all need an explicit preserved semantic. Stock `CaptureForSuspension` is not public; null public capture is not interchangeable with the stock sentinel. Public `Run` does provide an enclosing return for a nonnull captured context, but merely nesting it inside the current flowing Action leaves the outer restoration unjoined. |
| **Root completion Actions** | They are ordinary methods, so supplier builder attributes cannot change their invokers. They would also require an owned invocation boundary. Raw unsafe registration plus explicit context management is a candidate direction only after equivalent behavior and every enclosing suffix are proved; a direct substitution is not acceptable. |
| **Registration and resumption races** | Reserve before registration; keep the registering invocation funded through registration return. A callback can start before that return. Prevent concurrent execution/clearing of one machine without blocking the only completion path. An exception after a possibly successful registration retains an uncertain obligation; it is not permission to abandon/refund it. |
| **Success, synchronous fault, suspension, fault and cancellation** | Preserve existing usage-error timing, original execution/cleanup exceptions, selected cancellation token, and asynchronous public continuation dispatch. A manual stock builder used only as a promise does not itself create a state-machine box, but it does not expose an option for asynchronous continuations. A second asynchronous TCS/outcome-transfer layer would add funded objects and fault/cancellation work. Public `TrySetFromTask` can preserve richer outcomes, but cannot supply return information for an opaque producer. |
| **Final handback and lower release** | After the handback, no invoker field access, context restore, fallible publication or new Scene-owned allocation may remain against the retired claim. Its caller and runtime dispatch suffix must be covered or independently shown harmless. The existing use-control release suffix remains a separate obligation. Moving a final decrement into another unjoined Action only moves the defect. |

The direct approach is therefore **not disproved as a possible new implementation**, but it fails the requested “smallest currently implementable, fully accounted mechanism” gate. Adopting it would require an explicit, narrowly reviewed exception to Chapter 02's async-abstraction rule and a complete implementation/proof across the table—not just a new builder struct. No such exception is inferred here.

## 6. Allocation accounting cannot be deferred to the handback

Retain the existing disjoint `J_shared`, `J_producer`, `K_body`, `K_publication`, supplier X, and retained-receiver R ownership. No new numeric allowance follows from this assessment. A direct replacement's variable frame graph cannot be charged as “two suppliers” or hidden in a fixed root constant.

| Row a replacement would have to close before execution | Required derivation and end |
|---|---|
| Whole stable root, synchronization state, existing promise, fault pair, and bound root Actions | Re-derive the final layout and all supported lazy/cold capacity after any fields change. Retain the whole shared carrier until the joined producer/body/release boundary; clearing fields does not shrink its charge. |
| Per-invocation generated machine and directly owned typed storage | Include Debug's separate machine, Release's inline payload, any reference box/interface boxing, result/exception slot, and original-grant link. Preclaim before the actual async call. Derive maximum coexisting invocations from actual control flow and admitted inputs, or refuse before the next invocation allocation. |
| Delegates, context callbacks, explicit binding and invocation bookkeeping | Charge every separately constructed Action/closure/holder and generic/cold callback state exactly once. Specify real retention, nesting and release. A value-type builder field is not itself another heap object; its referenced storage is not free. |
| Await registration, continuation and dispatch capacity | Include Scene-created continuation wrappers, Task continuation backing/growth overlap, queued work holders, registration-return overlap and supported runtime branches. No new dedicated worker or global owner is assumed. The tentative external-observer provenance rider does not exempt Scene-created registrations. |
| Context handling | Distinguish borrowed immutable input context from newly created holders/clones/capacity. Account supported suppression/restoration helpers and their invocation lifetime. AsyncLocal callbacks cannot be assumed absent or allocation-free merely because the supplier did not declare an AsyncLocal. Do not copy an arbitrary caller graph into a fixed allowance without a bound/owner. |
| Original and replacement outcome machinery | Include every plain promise Task, TCS/Task pair, defensive exception-enumerable copy, contingent holder, exception-dispatch capture, cancellation outcome and coexistence introduced by the selected design. `TrySetFromTask` does not make its destination bookkeeping free. Preserve original exceptions/tokens. |
| Fault, uncertain registration, cleanup and claim-return failure | Protected K must exist before optional work and before its own allocation. Preserve remaining numeric debt, exact root and still-owned graph. Ordinary unknown epilogues are live work, not stopped-but-unreleased quarantine. |
| Final invoker/control suffix | Identify the last allocating/accessing instruction, original funding, and actual end/transfer of the handback/release bookkeeping itself. Do not assume the control's 256-byte charge pays for a new retirement mechanism. |

Managed fields alone do not close native dispatch-state capture, Lock capacity, runtime queue growth, or other reached native/lazy paths. Those are unresolved derivations, **not evidence that each is unbounded**. Sampled allocation events, old aggregate totals, and the prior root-construction observation cannot supply them. The existing workspace/receiver proposal remains independently necessary.

## 7. Smallest proposed owner-contract disposition

The following is proposed wording for later owner review, not an edit to maintained chapters:

> Scene preparation is supported only in a lane whose actual planner, selected resource supplier, transitive Scene-owned producers, and Scene-created completion invokers have funded, production-valid last-access/end-or-transfer boundaries. Task terminality establishes outcome availability, not discharge of their invocation grants. An unavailable lane refuses before preparation allocation or supplier invocation. This prerequisite applies to consumers that actually require Scene preparation; it is not a blanket prerequisite for unrelated M03-B or M05 implementation.

Owner placement:

- **Chapter 16 and Chapter 30:** record this supported-producer prerequisite and unavailable-profile behavior for `SceneLoader.PrepareAsync`; do not introduce a caller acknowledgment, new public Task-like API, fabricated result, or permissive fallback.
- **Chapters 02 and 04:** retain actual-consumption and complete preallocation accounting. Clarify that no particular CLR physical-return hook is mandated: an owned invoker with a proved harmless suffix could satisfy the requirement. Do not grant a blanket framework-allocation exemption or silently authorize a private async framework.
- **Chapter 35:** **no new blanket gate or sequencing change is needed.** Lines 28-55 already separate dependency-ready implementation from actual public-consumer producer closure. Its CPU-kernel/layout, source-adapter and bounded producer work can proceed when their own prerequisites exist. World extraction, real Scene consumers, hosted observations, and dependent qualification still require their actual producer closure.

The smallest change for **unrelated** work is therefore a clarification of gate ownership, not a Scene implementation or budget relaxation. A future decision to pursue a true private owner-aware builder/invoker is a separate, larger Chapter 02 exception with the full section 5 input set. It is not the selected resolution here.

The 143 reported guard failures remain a material unavailable-capability result. Do not fix them by ignoring/skipping tests, installing guessed J/K, clearing the latch on Task completion, raising P/world/domain ceilings, changing fixtures, or claiming the full Runtime suite is qualified.

## 8. Bounded continuation and exact stops

The current task ends with this document. The sequence below is for separately authorized subsequent work; none of its builds or Scene executions was performed here.

1. **Apply the prerequisite-only disposition to work selection.** Continue only units whose actual input/owner producers are already available under Chapter 35. Keep Scene preparation and dependent consumer observations blocked. **Stop** any attempt to treat the tentative proposal itself as a fulfilled capability or to rerun unchanged blocked preparation to discover funding.
2. **If a direct replacement is later selected, review its owner exception first.** Close explicit per-call grant binding, the complete Task/ValueTask member set, root Action invocation, context semantics and supported runtime modes. **Stop** if the design still delegates machine resumption to an opaque stock builder, leaves any Scene-owned child opaque, or uses an unowned ambient carrier/worker.
3. **Compile a real candidate with the guard still closed.** Use the SDK from `global.json` and ordinary owning-project commands, Debug before Release:

   ```powershell
   dotnet build .\sources\TerraFX.Runtime\TerraFX.Runtime.csproj --configuration Debug
   dotnet build .\sources\TerraFX.Runtime\TerraFX.Runtime.csproj --configuration Release
   ```

   Freeze its scoped dirty sources, generated machine/awaiter/builder membership, assembly hashes/MVIDs, IL/exception regions, runtime/compiler identities and executable native code versions. **Stop** on missing source/binary correspondence or an unaccounted tier/ReadyToRun/instrumentation branch. Do not reuse the hashes in this document as that candidate's proof.
4. **Prove the actual invocation boundaries before integration.** Inspect the final suppliers and root Action invokers, including initial caller return, registration return, all resumptions, state clearing, context restoration, outcome publication and the final handback suffix. Derive every section 6 row and supplier X/receiver R obligation before the first corresponding allocation. **Stop** on any unknown funded access/allocation after a proposed handback, absent finite row, or unproved preservation of context/error/cancellation semantics. No root prototype or aggregate heap probe closes this step.
5. **Close the independent workspace/receiver and lower-release gates.** Preserve the root through partial release failure and exact remaining debts; qualify the use control's own suffix. **Stop** if a live claim is released/reacquired to manufacture transfer capacity or if release failure loses its anchor. A producer join alone is not permission to install `QualifiedFootprint`.
6. **Only after complete funding/ownership proofs may the derived footprint enable real Scene observations.** Use focused existing real-package consumers, Debug then Release, with nonzero selected/executed tests and complete allocation/native coverage required by the existing evidence proposal. Distinguish no participant, selected no-render-data participant, and actual retained resources; synchronous/suspended, success/fault/selected-token cancellation, registration races, and cleanup/publication overlap. Other targets/AOT and milestone acceptance remain separate.

For that future qualification, locate stops in the **final compiled actual invokers**, not a fabricated supplier or stale IL offsets:

| Stop/interleaving | Required observation; failure condition |
|---|---|
| Supplier outcome terminal, before enclosing context return/state clearing | Root/public outcome or body completion must not return that supplier's original claim. This distinguishes the missing join from ordinary readiness. |
| Root Action body ended, before its enclosing dispatch/context suffix ends | Its producer provision remains charged. An Action-local decrement is not the release witness. |
| Callback starts while the registering caller is held before registration return | Both obligations remain covered; clearing a machine or refunding on the callback alone fails. |
| Successful body disposal/admitted-body completion while either preceding stop is held | Only genuinely ended/transferred body ownership changes. Producer/shared provision remains owned; no new use or activation borrowing supplies it. |
| Final owned invoker after its last context/publication scope and state clearing | Original grant handback occurs exactly once, outside gates, and remains legal after admission closes. Inspect the remaining caller/native suffix; merely seeing the handback callback run is insufficient. |

These are specified distinguishing observations, **not executed races or authorization to execute the candidate**. Fault/cancellation and real cleanup-failure observations still require their own reachable, safely funded stimuli; do not inject artificial OOM or a fake preparer to label them covered.

## 9. Scoped input identity and verification limits

Assessment base: HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`, with the existing dirty/untracked working tree preserved. The following SHA-256 values identify the directly inspected local source/contract/proposal inputs, captured at `2026-11-17T03:22:52Z`. They are a scoped review-input set, not a repository inventory or build/acceptance receipt. No schema bytes or fixture membership are changed or qualified.

```text
global.json
  1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C
Directory.Build.props
  770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A
sources\Directory.Build.props
  D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
  2F1C81E54F0FBF12D01E34D0D840885F5EB0E94A8B8890865EF721B8C6A3C65D
sources\TerraFX.Runtime\Scenes\PreparedScene.cs
  B73DD4314B63084132AFC2F9325ED98EECCCE65A174F02B16F2DED7EC65F688C
sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
  5D210AE9266EFFE8C074DD0CADB8E6C8027A0155424A17F2E3CAB47DA479AA13
sources\TerraFX.Runtime\Scenes\SceneLoader.cs
  D53823C6C673E881AAD32A3E94508EC5B252CC39D366A99458A6C487E0FE4886
sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
  EC26B6CC6DA73AD054583CCC653CF58E45C3630662F642D8C79EF70BD7A21EA4
sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs
  67AE936843F6329D95D12B784A4686A8B75B2CCEB3932159E56BDD24F21CA423
sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs
  EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55
sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
  4FD3F20576AD328BD3E9958E40EF8D475451BDCBFC5122C3BA7E599A0EA8361A
sources\TerraFX.Runtime\Rendering\RenderAtlasPageLoader.cs
  C8A80F5A957BA0E1DB6A56D390CCB04A8D807399AD18F4FB233297A10C8411BE
sources\TerraFX.Runtime\Rendering\RenderMaterialTextureLoader.cs
  1F69743F5F4C300DA9A0C20E174D21748EBD7119496D957855B9C098D72584B9
sources\TerraFX.Runtime\Rendering\RenderMeshAssetBinding.cs
  E4B7EDBBC746CF29568D683C8ED0A8E0987FDE6DC10AD4E390370BB8D12865D5
sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
  68FE3AC1048423917A1EB64327457CB2D2A3EB95C8792C9536F30AC948192581
sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
  EBF8A3F7C1115538CF06379FA65AE339B783117E39F76D1DE61C46C0B5C40466
docs\project\Roadmap.md
  649182860D65DC902A406BCAC482AA1D286465BAFB0B34143452C8B09E51EBDE
docs\project\Building.md
  F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F
docs\design\02-dotnet-api-memory-and-ownership.md
  40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291
docs\design\04-capacity-and-workload-profiles.md
  A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245
docs\design\16-scenes-transforms-and-cameras.md
  B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A
docs\design\35-implementation-roadmap.md
  24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87
docs\proposals\m03b-scene-preparation-supplier-return-closure.md
  199158C2357AC6B3FE551F568C0DA69B739A0A30AFDFC2372113DD4C6597F8FD
docs\proposals\m03b-scene-preparation-workspace-closure.md
  9D7280A89C5BB75890ADAF65E26F8C80766B8687EE0608623D0F97D519BE32C3
docs\proposals\m03b-scene-preparation-handoff-closure.md
  628B09039114AACAEA65116DD584B2F94B554011306AB600414C5A2A809A7FBA
docs\proposals\m03b-scene-preparation-budget-accounting.md
  3F8E4F0315C6CC2933FC982F64D5E94B9070EB1876A74B0688118BB17AFF5A62
docs\proposals\m03b-scene-preparation-compiled-producer-evidence-addendum.md
  ECCC174D5DF86E195731EB3BE74FBE5A349D0214C8F94DE35769503D4418595C
docs\proposals\m03b-scene-preparation-allocation-evidence-capability.md
  D130242EEAB65921B9D6FF10D15E117AA0FC551503A0781D705E9856BABA08B9
```

The [supplier-return proposal](m03b-scene-preparation-supplier-return-closure.md) remains supporting tentative guidance; this assessment makes the Task-only limitation precise and supplies the actual runtime ordering and nested-ValueTask evidence. The [workspace](m03b-scene-preparation-workspace-closure.md), [handoff](m03b-scene-preparation-handoff-closure.md), and [allocation-evidence](m03b-scene-preparation-allocation-evidence-capability.md) obligations are not discharged.

Only this new proposal is authored. No maintained design, roadmap, code, tests, limits, fixtures, or existing proposal is changed. No build/test, Scene preparation, heap/root probe, native/AOT observation, commit, push, or publication is claimed. Source/metadata analysis explains the blocker; it does not qualify a replacement.

[builder-outcome]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs#L643-L735
[builder-invoker]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs#L510-L579
[value-builder]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncValueTaskMethodBuilderT.cs#L45-L105
[builder-start]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderCore.cs#L20-L55
[awaiter]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs#L438-L532
[task-registration]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L2638-L2698
[action-invoker]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs#L634-L728
[context-tail]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs#L309-L363
[pool-tail]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L994-L1018
[context-run]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs#L54-L221
[internal-box]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/IAsyncStateMachineBox.cs#L9
[builder-attribute]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderAttribute.cs#L11-L20
[raw-action]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs#L741-L771
[outcome-copy]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskCompletionSource_T.cs#L324-L345
