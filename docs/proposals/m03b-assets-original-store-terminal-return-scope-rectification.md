# M03-B original-Store terminal-return scope rectification

**Disposition: tentative contract-scope guidance under the user's unblock instruction. No universal native-thread/outer-dispatch return receipt is established. Select the narrow terminal-substrate disposition below for further source implementation, subject to owner acceptance and the actual closed call-site proof. Native-owning worker materialization and native/Scene/Consumer/AOT activation remain fail-closed.**

## 1. Decision

The [retirement-root proposal](m03b-assets-original-store-worker-retirement-root-closure.md) separates finalizable I/O lifetime L from execution state E and has one worker, one retirement mailbox and one terminal callback. Its remaining problem is not another byte amount: public Thread/Task/ThreadPool APIs supply no receipt for **every enclosing native/runtime/dispatch return**.

Select an explicit, terminal-only **source-dead substrate transfer** instead of inventing that receipt:

1. Assets must observe its actual native-use, cleanup, publication and accounting helper returns, preserve one-shot native dispositions and finish every retained source/data interest or exact owned transfer.
2. A terminal bridge may hand a **proven inert** engine carrier to the framework epilogue after its ordinary cleanup helper actually returns. The remaining framework references are retirement retention pending CLR collection, not a new engine-use owner or an unbounded paying purse.
3. CLR Thread/runtime owns the remaining thread/TLS/stack/join-control/finalization tail; ThreadPool owns its enqueue wrapper, context/thread reset and dispatch tail. Assets does not report those physical resources destroyed, their callbacks returned, or their bytes measured.
4. This changes only the completion subject of these two terminal epilogues. It does not exempt live E/L/source fields, buffers, faults, arbitrary callbacks or any native operation from original B0/source ownership.

This is a **proposed amendment**, not an interpretation already accepted by Chapters [02](../design/02-dotnet-api-memory-and-ownership.md), [11](../design/11-assets-import-streaming-and-packages.md) or [30](../design/30-public-api-review-surface.md). It also refines the [B0 proposal's](m03b-assets-original-b0-source-capacity-rectification.md) distinction between retained source ownership and CLR-retired objects awaiting collection. If owners require full native-thread/control destruction and every ThreadPool outer return before Assets shutdown, this disposition cannot satisfy them; section 6 names the missing capability.

Keep the original [package-I/O](m03b-assets-original-store-package-io-source-capacity-closure.md) mechanism, fault types/identity, native close/cancellation rules, issuer and grants. No additional worker/restart, permanently registered wait, process-global root, public receipt API, Task observer chain or Runtime dependency is selected.

## 2. What the pinned runtime actually establishes

Reference identity is **CoreCLR 11.0.0-rc.3.26522.148**, unified revision **`5254d8bcf59917edc1e1abc33bdf2442b77b1c70`**. These are static source observations, not loaded native/binary qualification.

| Actual source | Supported observation; unsupported inference |
|---|---|
| [Thread.StartHelper.RunWorker][thread], parameterized branch | Clears `_startArg` into a local and invokes the parameterized entry. It does not keep an owning engine argument in that field afterward. This does not prove the native thread has exited when the entry returns. |
| [Thread.CoreCLR.StartCallback / OnThreadExited][thread-core] | Clears `_startHelper`, invokes it, then calls OnThreadExited. The latter explicitly says the unmanaged thread is still alive while managed state is marked dead. |
| [Windows JoinInternal][thread-windows] | Normally waits on the OS thread handle, with additional IsClosed/invalid-handle/finalizer-race paths and native SafeWaitHandle references. Join alone does not attest all CLR/native-control/finalizer retirement. |
| [Unix JoinInternal / SetJoinHandle][thread-unix] | Waits on the managed join event signaled by OnThreadExited. Managed entry completion is not destruction of the native-thread tail. Retrofitting pthread_join or a second native thread owner is not selected. |
| [QueueUserWorkItemCallbackDefaultContext&lt;TState&gt;.Execute][queue] lines 1271–1296 | Clears the callback field, invokes it with readonly `_state`, then returns. The wrapper can still retain E through `_state`; the callback does not observe its own wrapper return. |
| [ThreadPoolWorkQueue.Dispatch][queue] lines 984–1025 | Calls the dispatch helper; afterward clears `workItem`, resets ExecutionContext/SynchronizationContext and thread state, and performs runtime completion bookkeeping. Those actions can follow an engine callback's local finally/Task signal. |
| [Task.ExecuteWithThreadLocal / InnerInvoke][task] lines 2455–2558 | Invokes the delegate, calls Finish(true), and then has its own finally restoring task TLS/activity state. A known task-body completion can establish delegate completion only within this narrower source path, not all caller/dispatch returns. |
| [Task.TrySetResult][task] lines 3496–3513; [TCS.TrySetResult][tcs] | The promise task sets terminal flags before finishing continuations; TCS invokes that transition. An independently set terminal Task is not evidence that a producer, callback or outer dispatcher returned. |

RunContinuationsAsynchronously controls continuation execution, not the lifetime of every enclosing frame. WaitAsync cancellation, a second awaiter, a callback-written flag, a local finally, IsAlive and a ready Task cannot manufacture the missing native/outer receipt. No such inference is used here.

## 3. Exact terminal-substrate rule

The rule applies **only** to the package worker's final entry tail and its elected terminal callback's inert epilogue. It is unavailable until all of the following are proved for the actual compiled path:

- Ordinary native cleanup has ended every package-native read/copy/use and captured file/event/block consumption separately. An uncertain wait, close, start, callback registration or other still-consuming operation is not source-dead.
- E has no queue/active/returning request, native-use/publication use, delegated source consumer or future native/admission action. Closing state is monotonic and all reentry refuses.
- Every remaining engine alias that could use E is detached, including L's E link when legal. The terminal carrier's request/mailbox/thread/callback references are cleared at its last engine-use boundary. Null fields still have their charged declaration widths before that boundary.
- A retained L, service operation, Store, generation, source message/path or fault/publication interest remains under its own original claim. Its surviving state is not relabeled as framework storage. If L still needs E, E cannot be handed back.
- After the terminal cleanup helper returns, the bridge performs **only scalar original-issuer accounting**, with no E/L access, application callbacks, Task/TCS publication, context installation/restoration, codec work or new queue registration.
- Pinned/compiled caller analysis shows the remaining Thread/ThreadPool frames neither access engine data nor regain an engine-use capability. Framework-only retention of an inert carrier must not expose a callable source action. The source-dead rule cannot hide another retained source node in an opaque wrapper.

The meaningful amendment is: **inert framework/automatic-frame references after this closed terminal handback do not extend retained engine source-capacity ownership**. Physical GC retention may still exist. This is not a declaration that those references, native resources or framework epilogues have returned or been destroyed. Source bytes remain positively charged until the verified handback; the framework exclusion alone never refunds them.

Scope examples:

| Tail | Proposed owner/disposition |
|---|---|
| Thread entry has returned all engine/native helpers; only CLR exit/attachment/join-control work remains | Transfer that opaque implementation tail to the constructed BCL Thread/CLR lifetime. Preserve its counted protocol interest; claim only engine-use retirement, not native-thread destruction. |
| Terminal callback has returned its engine cleanup helper; wrapper readonly state still points to inert E | E can become CLR-retired source storage under this rule only after legal engine detach and actual helper return. The framework wrapper/dispatch owns its remaining retention/reset tail. |
| Callback still publishes the disposal Task, restores application context or carries a source fault | Not inert. Keep its real source/caller/publication grant and prove that separate returned-call boundary. |
| L still retains generation/source/fault state or an engine Store still accesses its service controls | Keep L/service/source claims. Do not refund 112/696 merely because Thread.Join returned. |

If a context reset, runtime event, provider or callback can still reach a live engine control/payload, the tail does not meet this rule. It remains an unclosed funded interest, not an allowed “opaque” fallback.

## 4. Returned-call handback, not a terminal Task receipt

Retain the root proposal's allocation-free finalizer mailbox/stop publication and worker-side single terminal enqueue. The independent callback first acknowledges **caller entered**, then joins outside gates; the worker never joins itself or waits for that callback's completion. Entry/callback notifications remain eligibility facts, not native/outer-return receipts.

For the proposed disposition, use one ordinary terminal cleanup helper called by that bridge. Its result contains only the existing **OriginalIssuer reference and checked scalar ended-byte amount**; no E/L/native pointer or new heap receipt escapes. For example:

```text
terminal bridge calls CloseTerminalEngineUses(E)
    helper validates actual cleanup/use facts and one-shot handback
    helper detaches only ended engine aliases/source portions
    helper returns (original issuer, ended source bytes)
bridge observes that ordinary return
bridge returns the ended bytes through the same original issuer
bridge performs no further E/L operation
framework epilogue continues under the proposed terminal-substrate rule
```

The helper does not attest its own enclosing return or refund its carrier from its own finally. The bridge observes the helper's real returned/unwound call. The original 64-byte scalar issuer stays paid through the subsequent accounting call/return and any first-paying anchor lifetime; it does not contain E/L/Store references and is not another purse.

A failure before source-dead handback keeps the exact owner, primary/cleanup faults and claims. A thrown accounting/validation failure is not success and cannot authorize another handback. Establish matched, nonwrapping, single-consumption accounting before detach; existing source-state bits can represent the one-shot disposition only after their final declaration check.

Public disposal/error delivery remains the existing owner channel. It must have its own actual publication/consumption/context source interests, and occur before or outside this inert bridge under the appropriate retained owner. Do not move public Task completion into the final epilogue to make the latter appear free. A native/control failure before handback is still Assets failure; runtime/application work **after a valid proposed transfer** is not retrospectively reported as an Assets rollback or proof of successful framework shutdown.

This is not an executable receipt implemented today. It is a precise narrower completion-subject decision permitting a non-permanent handback without an infinite sequence of “observe the observer” Tasks.

## 5. Original source claims and numerical effect

No new source class, pointer block, event, callback or public Task is selected. The scalar returned tuple is automatic call storage; any later retained receipt/wrapper/field changes its preclaim before construction. The root proposal's candidate remains:

```text
E Q = 160; L Q = 112; service O = 696
Store Q = 152; original issuer Q = 64
native scratch/OVERLAPPED = 65,536 + 32
S_root = 66,752
cold source candidate = 42,152,248 + P(opening) + F(generation)
additional source provision for this scope disposition = 0
```

These are declaration-derived candidate values, not an admission profile installed by this document. The dirty worktree now contains E/L/request declarations, a mailbox publication/finalizer candidate and capacity expressions, plus backing-native/reporting fields and their source charge, while retaining the BCL reader. It does not start a worker, acquire these native blocks or implement the terminal bridge/no-further-access handback. The current mailbox-only finalizer is not evidence of the complete closing/admission/resurrection protocol.

| Portion | When a return is eligible / what remains |
|---|---|
| Native 65,568-byte extents | Return only after actual last native use, one-shot free and actual cleanup-helper return. File/event opaque disposition and generation debt are independent. |
| E 160 | Return only at verified terminal source-dead handback and legal engine detach. Without scope acceptance/proof, all 160 remains charged despite Join/callback readiness. |
| L 112 / service 696 | Return only portions whose source/control/fault/response uses and aliases ended or transferred exactly. They may remain after E and native extents retire. |
| Store 152 / issuer 64 / shared anchor 32 | Their existing ownership/lifetime remains; do not include still-used accounting or repeated-disposal storage in a terminal-worker refund. |
| Every generation K, request R=256, opening/path/message/metadata claim | Continue existing disjoint source accounting through real consumers/cleanup/returns. This terminal-only rule does not retire them from Task terminality. |

For example, native-block return alone leaves **1,184** fixed source bytes (`152 + 64 + 696 + 160 + 112`), before any generation/request/fault debt. If only E then meets the accepted handback, **1,024** remains. This is not permission to free those remaining portions or an all-in runtime-memory measurement.

Opaque Thread, Task/TCS, queue wrapper, native wait/attachment/finalizer/context and dispatch interests remain counted and owned by their actual framework/invocation protocols. Unknown bytes are explicitly outside the selected subject; no unknown source-owned storage receives a zero allowance.

## 6. Exact owner decision and stop

Two owner dispositions are possible; **the first is tentatively selected here**, not accepted:

| Disposition | Materialization condition / remaining claims |
|---|---|
| Accept terminal source-dead substrate transfer | Owners must explicitly accept the narrower terminal completion/error/retention subject in Chapters 02/11/30. Then close the actual E/L detach, no-further-access, native-use/native-close, start/dispatch failure and scalar-accounting call-site proof for the chosen target. Until then no native-owning worker materialization/start/acquisition; retain E/L/service and every live data/caller claim. |
| Require full native-thread/control destruction and every outer dispatch return | Supply a reviewed lower execution capability that acknowledges actual native-thread/control retirement **and** enclosing callback/dispatch return with a closed original-grant lifetime. Current public Thread/Task/ThreadPool APIs do not expose that complete receipt. Keep all affected E/L/service/source/caller claims; no fabricated Task, extra worker, global root or padding closes the missing capability. |

The source-only decision does not cure unreturned constructors/start, an uncertain terminal queue attempt, a callback that never enters, active native I/O, partial-finalization/resurrection races, or retained original fault/context delivery. Those are genuine owning branches. Native activation also remains blocked by the unqualified target/profile and required observations under [35](../design/35-implementation-roadmap.md#4-completion-gates). Stable .NET 11 PATH children, Linux ABI, NativeAOT, Runtime Async and all Scene/Consumer gates remain as previously recorded.

Next bounded work is compile-only implementation of the actual terminal bridge and one-shot detach/accounting membership, with existing Assets regressions for retained portions, repeated handback refusal, source fault identity and the no-E/L tail. Do not add another observer to report outer return. When actual code exists, use the owning ordinary Debug then Release test selection; managed tests qualify only their exercised source/returned-call behavior. No native test is authorized merely to discover the still-missing proof.

## 7. Current input freeze and preservation

HEAD **`b961cc5e393ca7815920fedb21a6d764c6bda64c`**. Concurrent Assets source-capacity edits are inspected input, not edits made by this proposal. Refresh the snapshot rather than reuse the older clean-source union: the exact set is the package proposal's 61-member section 8 set, plus the package and retirement-root proposals, `sources\TerraFX.Assets\AssetStore.Bounded.cs`, `tests\TerraFX.Assets.Tests\AssetStoreTests.cs`, and the four new Assets files `OriginalPackageIo.cs`, `OriginalIoLifetime.cs`, `OriginalIoRequest.cs`, `OriginalPackageIoSourceCapacity.cs` (**69 inputs**). Its current union SHA-256 is **`8e8bb5a2db3534ac5cee7c8a186705167cceca947087ce50e29fe51154e37729`**, with the existing ordinal Windows-path/TAB/raw-hash, LF/UTF-8 digest definition.

Prior proposal hashes remain **`a06d30e336cdd3745e2cd93a5a577dd07a4956bc85d7bc95f0e013312c0a1112`** (package-I/O) and **`05697cddea6cede33a09657016060950d9bc9122795e02db31055057dd22ec7e`** (retirement root). Protected dirty Runtime test and schema bytes are unchanged. All runtime links select the pinned revision in section 2. Changed input bytes require scoped reassessment before applying any later disposition.

Only this new proposal is created. Existing proposals, source/tests/design chapters/plan artifacts and remote state are preserved. No build/test/native/AOT/allocation experiment or commit/publication is performed. Source arithmetic and input hashes are checked.

[thread]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.cs
[thread-core]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs
[thread-windows]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Windows.cs
[thread-unix]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Unix.cs
[queue]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs
[task]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs
[tcs]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskCompletionSource.cs
