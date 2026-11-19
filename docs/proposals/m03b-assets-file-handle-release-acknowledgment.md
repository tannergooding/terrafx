# M03-B Assets file-handle release acknowledgment

**Status: tentatively approved as rectification guidance per user direction; proposal only, not implementation. The lower capability specified here does not exist in Assets. Implementation and qualification remain blocked by the gates below; keep `PreparedScene.QualifiedFootprint` null.**

## Decision and scope

**The selected supported BCL surface is insufficient for the combined requirement.** Assets can retain a backing and could fund its actual read/cache graph under the proposed original Store grant. It cannot obtain the final native-close result from `FileStream`/`SafeFileHandle`, control their private cache-return boundary, or recover native ownership after an uncertain destructive close. Another await, `DangerousAddRef`, or a disposal wrapper does not supply those missing facts.

Select one minimum capability change: **extend the already proposed preowned `AssetPackageBacking` opening carrier with a private, platform-specific package-file I/O port**. That carrier owns acquisition, actual read machinery and cache, and a one-shot native-release disposition. Keep it in Assets, with the existing Assets-to-Core dependency. This is not a public filesystem abstraction, new recovery owner, runtime fork, general scheduler, or Core/Platform-to-Runtime dependency.

The port must replace the opaque lower file acquisition/read/recycle/close edge for its entire backing generation, not merely wrap `Dispose`. Reuse package validation, digest semantics, codecs and higher progress owners. Opening/hash reads, ordinary payload reads and inspections sharing that generation cannot secretly introduce a second BCL cache. An internal digest adapter, if needed, borrows this same port and has no independent file-close authority.

This narrowly amends the proposed Scene lower edge from stock `RandomAccess.ReadAsync` to the port's actual single-consumption read completion. The [Scene actual-invoker proposal](m03b-scene-actual-invoker-return-closure.md) still owns the Runtime invoker, context, registration, lease handoff and outer return. The [Assets bootstrap proposal](m03b-assets-prelease-bootstrap-and-close-closure.md) still owns original Store issuance, same-envelope compatibility and opening/failure retention. Their broad ledgers are not replaced here.

No existing BCL owner is detached, rewrapped or migrated into this port after acquisition. A future capable backing must start under it. A lower implementation that cannot meet the port remains unavailable; ordinary Assets behavior is not thereby qualified or silently rerouted.

## Verified boundary and source evidence

Local metadata identifies CoreLib `11.0.0-rc.3.26522.148+5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; its read-only file SHA-256 is `df53885902e896a2b40988237cbe9cbb9f1bb0743200f05fe9257c56a29c5732`. All runtime links below select that unified-source revision, including its native PAL. This identifies the inspected reference lane, not a loaded consumer runtime, native binary, filesystem or ThreadPool mode.

| Current repository boundary | Consequence |
|---|---|
| [`AssetPackageReader.OpenAsync`](../../sources/TerraFX.Assets/AssetPackageReader.cs#L23), [`AssetStore.CreateAsync`](../../sources/TerraFX.Assets/AssetStore.cs#L41) | FileStream construction precedes the reader's cleanup `try`; backing and Store construction follow validation. A constructor failure can return neither owner. Moving a `try` or using `File.OpenHandle` does not install ownership inside the lower acquisition prefix. |
| [`ReadExactlyAtAsync`](../../sources/TerraFX.Assets/AssetPackageReader.cs#L219), [`AssetPayloadReader`](../../sources/TerraFX.Assets/AssetPayloadReader.cs#L16) | None still allocates a destination and loops over potentially short `RandomAccess.ReadAsync` operations using `stream.SafeFileHandle`. Opening also reads through the stream for whole-package hashing. |
| [`AssetPackageBacking.ReleaseReference`](../../sources/TerraFX.Assets/AssetPackageBacking.cs#L171) | The last reference calls synchronous stream disposal; a caught exception roots the backing and suppresses implicit retry. Normal disposal clears the stream/reference. Neither path observes a native-close result. |
| [`AssetStore` replacement/disposal](../../sources/TerraFX.Assets/AssetStore.cs), [`AssetLeaseLifetime`](../../sources/TerraFX.Assets/AssetLeaseLifetime.cs), [`anchored read`](../../sources/TerraFX.Assets/AssetStore.ReadDescriptor.cs#L59) | Store close/replacement can leave old backings alive; temporary anchored references can become final even when admission fails. Final file release is not exclusively a successful Scene-lease disposal event. Store body completion does not attest lower return. |
| [`AcquireLoad`](../../sources/TerraFX.Assets/AssetStore.cs#L360), [Chapter 30 section 6](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) | Current CPU/decode/lease counters do not grant the proposed native/cache/control provision. An options value, package extent or retained managed reference is not that grant. |

| Exact runtime source | Established fact and limitation |
|---|---|
| [Windows RandomAccess][win-read], [overlapped source][win-source], [preallocated storage][preallocated] | Async-handle reads check out a source, pin storage and check out native-overlapped state. `Complete` calls `ReleaseResources` before publishing the outcome: cancellation-registration disposal, unpinning and returning the operation's overlapped use precede result visibility. This does not destroy its reusable preallocated storage. A pending read can exist before cancellation registration succeeds. |
| Same [overlapped source][win-source] | `GetResult` runs `TryToReuse` in `finally`, including on a fault/cancellation. It resets the value-task core, then parks the source in the handle's single slot or disposes it. The source/preallocated-overlapped graph can outlive the read. Reset is not complete containing-object alias clearing; callback/registration/invoker suffixes are distinct. |
| [Unix RandomAccess][unix-read], [thread-pool source][unix-source], [native `PRead`][pal-pread] | The selected Unix path queues synchronous offset I/O to the ThreadPool. It checks cancellation before entering the read, clears operation/context/token/buffer fields before outcome publication, then `GetResult` resets and stores the source in the handle's cache. That return uses `Volatile.Write`, not Windows' compare/exchange/disposal/`IsClosed` protocol. Native `pread` retries `EINTR`; a running read is not forcibly canceled. |
| [SafeHandle][safe-release] | The ref-count/closed/disposed transition commits before `ReleaseHandle`; its Boolean is ignored. Extra native-use refs can defer release beyond a Dispose call. `InternalRelease` saves/restores the prior P/Invoke error, so reading last-error after Dispose is not a close-result channel. |
| [Windows release/cache code][win-source], [Unix release][unix-handle], [native `Close`][pal-close] | Windows calls `CloseHandle`, disposes parked cache/event state, then returns its Boolean. Unix returns whether PAL close was zero, after best-effort lock cleanup. **This PAL maps native `close` returning `EINTR` to zero without retrying.** Thus even the raw Unix return/error pair is not preserved through these layers. |
| [Unbuffered stream close][stream-close], [file-handle surface][file-handle], [`File.OpenHandle`][file-open] | Stream close disposes binding then handle and returns no native receipt. SafeFileHandle is sealed; its cache operations are private/internal. `OpenHandle` delegates to the same lower opener. No supported cache-drain or native-release observer is exposed by these paths. |
| [Windows open/bind][win-open], [Unix open][unix-handle], [invalid-handle helper][win-invalid] | Windows acquires before binding, whose local catch covers `ArgumentException`, not every partial-initialization failure. Unix has a different catch/dispose path. Windows read helpers can themselves dispose a handle after `ERROR_INVALID_HANDLE`. Observing only final stream disposal misses other release sites. |
| [Bound handle][bound-handle], [portable implementation][portable-binding], [Windows-pool implementation][windows-binding] | Binding explicitly does not take ownership of the supplied file SafeHandle. Native-overlapped and deferred binding lifetimes differ by mode; the Windows-pool callback has bookkeeping/wrapper exit after invoking completion. A public binding Dispose is not a universal callback-return acknowledgment. |

These are information/lifetime boundaries, not evidence that a valid admitted production file close fails or leaks.

**Distinguishing source-derived schedule, not an experiment:** a Scene None read finishes and its `GetResult` returns; the payload pin has ended but a source remains cached in the open backing. Refunding all read-control bytes would refund surviving storage. Later final stream disposal can return normally without communicating native close status. On Linux, an actual close `EINTR` would additionally have been normalized by the PAL. Neither read-result consumption nor normal Dispose establishes the requested combined receipt.

Consequently, a custom SafeHandle subclass alone is insufficient: it cannot be substituted for sealed SafeFileHandle in RandomAccess, and its own ordinary `ReleaseHandle` still runs after the SafeHandle transition. `ownsHandle: false` around a BCL RandomAccess handle does not add cache control. Duplicating the native handle leaves the BCL-owned duplicate's final disposition unanswered. `SetHandleAsInvalid`, reflection into CoreLib, GC/finalization, a second Dispose, or probing a recycled handle value are not repairs.

## Required private port

The following are operation responsibilities, not proposed public declarations or completed private layouts:

| Operation | Inputs, owned progress and required result |
|---|---|
| **Open into carrier** | The acquisition-free unpublished Store issuer and backing are admitted and independently rooted first. Preinstall path/native-marshalling storage, invalid handle fields, cleanup/outcome provision and any boundary guard. Install each acquired file/binding/native control into that exact carrier before another fallible operation. Record `NeverAcquired` versus acquired prefix explicitly; no returned owner is required for unwind to retain it. |
| **Issue read** | Use the installed Assets progress owner, exact backing identity, checked file offset and owned typed destination/range, plus the selected cancellation token. Acquire access/native-use and submission interests atomically against close before reading the raw handle. Persist issuing/accepted/no-callback progress before returning or unwinding; absence of a returned ticket does not erase possibly submitted work. Return one read ticket: exact file/read-record identity and checked operation sequence, never a raw handle or a new funding capability. |
| **Consume read** | One consumer obtains the actual count or original fault/cancellation, after native buffer use ends. Submission, cancellation callbacks, delivery and read-source return remain separately tracked. Short-read progress stays in the same higher read owner. No second consumer, premature reset or Task-result-only acknowledgment. |
| **Return read storage** | After the old read's actual accesses end, return its record either to this backing's already funded cache or to completed retirement. Record which occurred. Clear old buffer/token/context/continuation/outcome aliases where they are no longer required; otherwise retain and charge the complete surviving graph. Do not overwrite an occupied cache or lose a returning source's owner. |
| **Retire generation once** | Close new admission, then require all actual native uses, submission/registration calls, result consumers, returning records and dependent callbacks to end or transfer. Drain the owned cache/binding in dependency order. Elect one native-close attempt and preserve its result/error in the carrier before any later teardown, exception construction or accounting. Repeated close observes this progress; it does not issue another native close. |

No span crosses suspension. Retained destination access is an explicit Assets-owned buffer/range interest, not a new `Memory<T>` bridge exception to [Chapter 02](../design/02-dotnet-api-memory-and-ownership.md#3-leases-and-asynchronous-operations). A read ticket's readiness is not its final return. The Runtime driver still owns the actual ordinary invocation/registration/consumption boundary; native callbacks only record/wake owned progress, never publish an engine Task directly or execute cleanup under a gate.

### Native disposition is separate from cleanup success

The preinstalled receipt identifies the backing generation and single attempt; it preserves native result, error only where defined, and these monotonic facts:

- **Never acquired / owned, not attempted:** distinguish an unsuccessful native open from later initialization failure or retirement refused before close.
- **Issued, not returned:** the native call still owns an execution interest. Cancellation/timeout does not manufacture a terminal disposition or release its inputs.
- **Consumed:** the selected native contract establishes that this exact process handle/descriptor reference was consumed. Record separately whether native diagnostics or later control cleanup failed.
- **Retained, known:** a proved native failure leaves the original reference owned. Revoke normal use, retain that original owner/authority and grant; do not dispose it away or retry implicitly.
- **Indeterminate:** no supported conclusion about consumption. Retain the original carrier, progress and possible debt; prohibit native use, rewrapping, probing and retry. Rooting this record cannot resurrect a reference already consumed. This outcome never qualifies as successful release.

File-reference consumption, cache/control retirement and final caller/accounting return have independent progress. A consumed reference followed by a cleanup/accounting failure is never closed again. A retained or indeterminate disposition is not represented merely by an exception attached to a now-empty backing.

### Windows and Linux are different implementations

| Platform | Required acquisition/read/release semantics |
|---|---|
| **Windows** | Acquire the read-only, read-shared, overlapped/random-access file into the carrier before binding initialization. Own the real `ReadFile` OVERLAPPED, destination access/pin, cancellation registration and completion dispatch. Preserve immediate failure/no-callback versus accepted completion, including completion racing submission return. `CancelIoEx` requests cancellation; even success or `ERROR_NOT_FOUND` does not join the read. Await actual completion before unpin/reuse. After full quiescence, call `CloseHandle` once and capture its Boolean/error immediately. Nonzero establishes consumption of this HANDLE reference. Zero is not imported Linux-style “consumed despite error”: classify only from a proved Windows contract; otherwise retain an indeterminate failure. An I/O `ERROR_INVALID_HANDLE` alone does not establish local handle consumption. |
| **Linux** | Acquire the descriptor into the carrier with the selected read-only/close-on-exec behavior; descriptor zero is valid, `-1` is the invalid sentinel. Preserve actual same-descriptor length/type checks, best-effort shared `flock` and random-access policy where applicable; Windows mandatory sharing is not a Linux guarantee. Select completion-owned queued `pread` work, matching the current running-read cancellation boundary, with an explicitly owned package-I/O worker invocation. After quiescence, capture the real `close` result/errno once, not the private PAL's normalized result. For a reached Linux close on a valid owned descriptor, success and documented post-consumption errors, including `EINTR` and ordinary `EIO`/`ENOSPC`/`EDQUOT`, consume it; never retry the number. `EBADF` is an ownership/invariant failure, not proof this owner successfully released its reference. Interposed/denied/otherwise unclassified calls require their own disposition proof, not the assumption that every non-EBADF errno consumed the descriptor. |

The Windows rules follow [CloseHandle][closehandle] and [CancelIoEx][cancelioex]. The Linux rules follow [Linux close(2)][linux-close], not a generic POSIX EINTR rule; other Unix systems are not covered. Preserve raw diagnostics separately from the reviewed public error mapping: today's PAL normalizes Linux close EINTR, whereas other close errors are subsequently hidden by SafeHandle. This proposal does not silently select new public exception behavior for either case.

For both implementations, preserve checked short-read accumulation and EOF-before-declared-extent failure. Cancellation before issue acquires no native read; cancellation after issue cannot release the destination, file use or source before actual completion. Windows cancellation may lose to successful completion; aborted-operation byte counts are not accepted as a decoded result. Linux cancellation is checked before work enters `pread`; a running syscall/EINTR retry retains ownership until return. Registration/setup failure after issue is a possibly live read, not rollback evidence.

The Linux worker is an explicit missing lower capability, **not** an already authorized Runtime/World worker or a hidden `Task.Run`/unbounded queue. Chapter 02's dedicated-I/O execution, original payer, queue/return/shutdown and partial-construction rules must be closed for this package-only port. This is a requested exception/prerequisite to the earlier no-new-worker selection, not permission to build a general worker service. No worker implementation or numerical capacity is approved here. The Windows binding/dispatch implementation has the same acquire-into/actual-return obligation; merely choosing public ThreadPoolBoundHandle does not prove its constructor prefix or deferred release.

### SafeHandle and caller/unwind roots

The backing's direct native ownership is established at acquisition; it is not stolen from a previously owning BCL SafeFileHandle. If a reviewed binding requires a SafeHandle, use a preinstalled **non-owning boundary guard**, backed by this independently rooted owner. It adds no second native-close authority and never enters RandomAccess's hidden cache.

Every marshaled or explicit SafeHandle reference must be accounted for. A marshaling AddRef protects its call, not the whole later asynchronous kernel operation. An explicit successful `DangerousAddRef` has one matching release and an independent strong owner root through actual use/return; it is not itself a funding grant. Final native close requires proof that all such borrowers have returned. SafeHandle provides no public ref-count query, so this follows from closed call membership and joins, not polling `IsClosed`.

Do not invoke owning SafeHandle disposal as the acknowledged close primitive. On proved consumption, invalidate/disarm the non-owning guard and native field only after recording the receipt; on a known retaining failure preserve the original native field and owner, with use closed. Indeterminate values remain quarantined diagnostics/possible debt, never fresh capabilities. The direct owner needs Chapter 02's legal nonthrowing finalizer fallback or established nonblocking retirement handoff, including partial initialization and disposal races; the installed root remains responsible throughout ordinary native ownership. Finalizers may not wait/join or silently retry; neither a normal finalizer nor critical-finalizer fallback supplies deterministic release evidence.

Before any external acquisition the existing Assets anchor must root the unpublished backing, original issuer, claims and unwind progress. After successful publication, real Store/lease/read/caller interests replace that root only by acknowledged transfer. Failed standalone creation still has a root even when no Store/Task is retained by its caller. After native consumption, keep the remaining return/fault/accounting suffix rooted and funded until its own return.

At every handback, an **ordinary enclosing caller** observes the lower call returning/unwinding before dropping the lower invocation interest. Include submission and registration returns, cancellation-callback joins, source return, context restoration, gate exits and the actual worker/native callback wrapper. A flag inside completion is not acknowledgment of its own enclosing return. Any surviving suffix needs an independently funded, already installed owner or a proved no-owned-access tail. Do not make a worker wait for itself, park the sole cleanup executor, retain every successful read forever, or release a synchronous lease successfully while hiding an unowned final-close continuation.

## Cost ownership and relation to tentative authorization

Use the existing proposal terms; introduce no purse or numerical allowance:

| Lower cost/lifetime | Original paying owner and end |
|---|---|
| File field/guard, binding and parked read-source/cache capacity, retained aliases, native controls and final-close provision | `Keep(g)` / its `I_cache`, under the bootstrap proposal's original Store envelope, through current, old leased, opening and failed generations. Cache capacity must be admitted before acquisition/materialization. |
| Checked-out read records, destination access/pins, native-overlapped/worker controls, cancellation, submission/return and delivery | `Open(o,p)` for opening/hash reads; the admitted read operation's `C_read` for the selected Scene read. Ordinary Assets reads sharing the generation need their own original admitted Assets operation provision, not an inference from decoded-byte counters. Attribute independently funded backing-owned capacity explicitly. Active checkout plus parked capacity and concurrent returning invocations are not bounded by the cache slot count. |
| Runtime invoker/context/registration/publication suffix | The Scene proposal's original operation/use interests and `J/K`, not the backing's file debit or another Scene use. Lower worker/dispatch costs likewise need a named original grant; “runtime-owned” is not an exemption. |
| Cold shared anchor/dispatcher, failed-open/retained/indeterminate graphs and remaining fault/return provision | The bootstrap proposal's expressly admitted original provision. Static reachability, a small node and a fixed count of exception references do not bound or fund the retained graph. |

Returning a checked-out source into the backing cache is an acknowledged lifetime transfer into **already reserved** `Keep(g).I_cache`, not a refund for destruction. Count shared physical storage once, but separately reserve simultaneous vacant cache capacity and additional checked-out sources. At final cache drain, end all old aliases/uses before returning its grant; “retired” does not mean synchronous GC collection or falling process RSS.

The user's tentative proposal authorization permits using the unpublished-Store and actual-invoker designs as implementation guidance and selecting this bounded rectification. It does **not** establish the missing issuer/budget-scope/compatibility, recovery-delivery or caller-context contracts, qualify source-to-binary/native behavior, or make this new lower port available. The earlier conditional earmark in `CpuAssetBudgetBytes` remains conditional on its stated owner approvals. Spare decoded-wire capacity is not that authority. Continue implementation only as the cited contracts and the NO-GO gates permit; do not treat tentative approval as qualification or authorization to publish remotely.

## Specific NO-GO gates

| Gate | Exact condition for reconsideration |
|---|---|
| **Original authority** | Close the bootstrap proposal's acquisition-free issuer, same-envelope debit/replacement compatibility and cold/failed-root funding decisions, plus the missing Chapter 11 primary/cleanup delivery definition. No successor Store/Scene purse or new process quota. |
| **Acquisition into ownership** | The actual Windows and Linux file/binding/worker acquisition prefixes, including native-success-to-field-installation and partial managed/marshaling initialization, have complete installed-root/unwind ownership. An outer `try` or returned-handle adapter is insufficient. |
| **Executable lower port** | Close the private input/result/state layouts, checked ticket identity/membership, Linux dedicated-I/O execution and Windows dispatch/ref ownership for named OS/ABI/libc/filesystem/runtime/ThreadPool modes. No stock hidden cache mixed into the capable generation, reflection/private-runtime import, new generic scheduler or synchronous-I/O substitution on the Scene invoker. |
| **Actual return and useful funding** | Derive the complete simultaneous lower graph/cache/worker/native/fault/caller-tail charge and transfer/return sequence against the original grants before work. Unknown native/shared/capture costs are not zero; an always-refusing profile is not a useful lane. Close the Scene proposal's separate context/fault/dispatch gates as well. |
| **One-shot native disposition** | Capture each actual release site's native result before destructive cleanup. Establish the exact consumed/retained interpretation; any unclassified or indeterminate branch remains failed-retained and unqualified. Consumed-with-error or later accounting failure can never trigger a second close. Native calls still executing keep live interests and nonterminal operations. |
| **Observation and acceptance** | When separately authorized, validate the real implementation through ordinary owning-project Debug then Release checks and independently required native/AOT/public-consumer observations under [Chapter 35](../design/35-implementation-roadmap.md#2-implementation-prerequisites-and-handoff). A throwing stream subclass, raw-handle corruption, artificial allocator failure, warm-cache sample or unreached selection is not a real constructor/close/return witness. If no safe genuine failure/return stimulus is available, retain that exact observation gap and retry only when the capability/stimulus changes. |

The receipt concerns one process ownership reference. It is not proof of kernel-object destruction, absence of other handles/mappings, filesystem durability, physical device close or recovery from arbitrary OS failure. Even a future accepted lower port would not close the planner/receiver/publication gates, install a qualified footprint, enable nonempty Scene/restore/rendering, add an acceptance ID, or complete M03-B.

No build, test, allocation probe, native failure injection or runtime experiment was performed. No source, maintained contract, test, other proposal or roadmap is changed by this task.

## Bounded analysis-input freeze

Snapshot UTC `2026-11-19T10:18:22.0492925Z`, HEAD `63cada028b29e41fad3e499aa20395b42c2835dc`. The two prerequisite proposals are committed at `4b48e017` and `a3628a5d`; the later HEAD changes roadmap gate wording, not their source boundaries. Dirty contracts are context, not accepted amendments.

The exact local sets below use SHA-256 of ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines, LF-joined, UTF-8 without BOM/trailing newline. Exclude this output from its own inputs. The two schema catalogues are identity-only preservation inputs; this proposal changes no schema or fixture.

```text
A: sources\TerraFX.Assets\
AssetLease.cs, AssetLeaseLifetime.cs, AssetPackageBacking.cs,
AssetPackageReader.cs, AssetPayloadReader.cs, AssetStore.cs,
AssetStore.PackageLease.cs, AssetStore.ReadDescriptor.cs,
AssetStoreOptions.cs, TerraFX.Assets.csproj, NativeAssetSchemaCatalogue.cs;
sources\TerraFX.Runtime\Scenes\PreparedScene.cs, SceneSchemaCatalogue.cs

D: docs\project\Building.md, Roadmap.md;
docs\design\02-dotnet-api-memory-and-ownership.md,
04-capacity-and-workload-profiles.md, 11-assets-import-streaming-and-packages.md,
29-project-setup-build-and-deployment.md, 30-public-api-review-surface.md,
35-implementation-roadmap.md;
docs\proposals\m03b-assets-prelease-bootstrap-and-close-closure.md,
m03b-scene-actual-invoker-return-closure.md

C: .github\copilot-instructions.md, .github\workflows\ci.yml,
Directory.Build.props, Directory.Build.targets, global.json
R: .dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\
.version, System.Private.CoreLib.dll
```

| Set / individual input | Files | SHA-256 |
|---|---:|---|
| A | 13 | `a35ae0956d1f6d9cb2bf013f1084c3dff327f207b36c5eea3f1173267cae46d8` |
| D | 10 | `f02ae80236df4ec387196549285681626897f492ad13872d7e40cf579a58fb47` |
| C | 5 | `dae084263f610f43a0d4b34730bfeffd8883fb9e5296ca806efddb1498e7f06b` |
| R | 2 | `4dee9cc9c928da2a1584ef40d5c7da50d0b5b2b801f0b45e2c41b9e19acb026c` |
| ALL | 30 | `c9dad43544d50db30a31faad73682484e0f2bc881cef2434de698423a3a55d83` |
| Dirty Chapter 04 | 1 | `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245` |
| Dirty Chapter 30 | 1 | `d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed` |
| `NativeAssetSchemaCatalogue.cs` | 1 | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `SceneSchemaCatalogue.cs` | 1 | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |

Preservation-only baseline: the 22 pre-existing dirty/untracked paths captured by `git status --porcelain=v1 -uall` at `2026-11-19T10:14:58.6774702Z` have set digest `d0143bceffb9bf8e0a5a02a4cb85b2c193f022288bb59c64ab820af96bd38ed6` using the same recipe. They are not a reviewed Rendering/test implementation set. Recheck scoped hashes, HEAD and dirty preservation before disposition; changed analysis inputs require reassessment. The pinned external sources and platform documents below are separate static inputs, not binary/native attestation.

[win-read]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/RandomAccess.Windows.cs#L254-L349
[win-source]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.OverlappedValueTaskSource.Windows.cs
[preallocated]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/PreAllocatedOverlapped.Windows.cs
[unix-read]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/RandomAccess.Unix.cs#L25-L103
[unix-source]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.ThreadPoolValueTaskSource.cs#L16-L184
[safe-release]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/SafeHandle.cs#L189-L264
[unix-handle]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs
[pal-close]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/native/libs/System.Native/pal_io.c#L395-L400
[pal-pread]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/native/libs/System.Native/pal_io.c#L2010-L2020
[stream-close]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/OSFileStreamStrategy.cs#L111-L130
[file-handle]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.cs
[file-open]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/File.cs#L165-L171
[win-open]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Windows.cs#L119-L278
[win-invalid]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/FileStreamHelpers.Windows.cs#L78-L105
[bound-handle]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolBoundHandle.Windows.cs
[portable-binding]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolBoundHandle.Portable.cs
[windows-binding]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolBoundHandle.WindowsThreadPool.cs#L145-L210
[closehandle]: https://learn.microsoft.com/en-us/windows/win32/api/handleapi/nf-handleapi-closehandle
[cancelioex]: https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex
[linux-close]: https://man7.org/linux/man-pages/man2/close.2.html
