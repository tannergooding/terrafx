# M03-B Assets Batch B: rectify the worker-envelope blocker

**Status: tentatively approved as implementation guidance under the user's blocker-proposal policy; review pending, not an accepted maintained contract or qualified allocation profile. No acquisition or CLI enablement is authorized by the measurements below.**

## 1. Decision and evidence

**Do not attach an unpriced BCL `Thread` to Batch A. Select caller-executed native package I/O for the already synchronous, internal `bounded-v1` Static-Mesh lane.** Keep Windows overlapped I/O with an explicitly owned event/`OVERLAPPED`, Linux offset `pread`, direct native ownership, scalar failures and the original Store budget. Remove only this lane's dedicated thread, interthread queue and request/reply signaling. This is an explicit revision of the tentative worker selection, not an automatic fallback after worker admission fails.

The worker cannot **currently** be funded and fully retired under the selected proof obligations without unsupported assumptions. This is not evidence that the default 536,870,912-byte budget is numerically too small, nor that BCL threads are generally unusable. Section 2 identifies the missing evidence and a concrete reconsideration procedure. The narrow alternative lets Batch B implementation proceed without making that unresolved execution service a prerequisite for a synchronous tool.

| Inspected evidence | Consequence |
|---|---|
| [`AssetStore.Bounded.cs`][preclaim], [`AssetStoreBoundedProvision.cs`][provision] | The 544-byte control (32 bytes of added owner references), 131,391-byte opening subtotal, 80-byte per-generation native-file shell, and 144-byte native-port shell are preclaimed before acquisition. The opening subtotal includes 65,536-byte profile and 128-byte directory arrays plus measured 24-byte allocation overhead for each. On the 44-character Windows path, cold/warm reservations are 1,197,287/148,711 bytes. `_reservedBytes` remains readonly; native port initialization and file acquisition are still unavailable. |
| [`AssetStoreBoundedBootstrap.cs`][bootstrap], [`BoundedStaticMeshAssetValidation.cs`][validation] and its [factory][factory] | The one-MiB attributed shared partition and 16-KiB per-Store provision cover the acquisition-free registration prefix. The factory closes precisely two codecs over 25 + 8 + 4 + 18 + 26 schema members; Rendering also initializes nine M05 descriptors outside that membership. None of these counts bounds thread/runtime/native storage. |
| Existing [preclaim tests][preclaim-tests], [`AssetStoreAllocationTests`](../../tests/TerraFX.Assets.Tests/AssetStoreAllocationTests.cs), and the TRX inputs in section 8 | Runtime preclaim tests execute 6/6 cases and the complete Assets suite executes 85/85 in Debug and Release. Cold/warm preclaim allocation is 936,888/76,840 managed bytes against 1,197,287/148,711 reserved bytes on the 44-character Windows path. The current AssetPackageReader selection passes 15/15 in Debug/Release, including four bounded payload-range preflight cases; the Runtime typed-shell selection passes 1/1 in each configuration and measures 176/240 bytes. These runs cover managed declarations, carrier sizes, pure metadata/range checks, shell sizes, and acquisition-free preclaim only; they execute no native file open/read/close or actual-return path. |
| [`AssetPackageReader`][reader], [`AssetPackageBacking`][backing], [`AssetPayloadReader`][payload] | Ordinary opening/hashing still uses `FileStream`; reads reach BCL `RandomAccess`. The ordinary backing constructor leaves its added bounded-file reference null. `OpenBoundedMetadata` now returns the two None entries with the exact backing token after padding validation. `ReadBoundedPayload` requires that token and a caller span, then validates range/codec/destination before reading through the same bounded file. Neither bounded method has a production caller; the pre-opening backing still refuses ordinary reference acquisition. |
| [`AssetBoundedNativePort`](../../sources/TerraFX.Assets/AssetBoundedNativePort.cs) and its [ABI declarations](../../sources/TerraFX.Assets/AssetBoundedLinuxStatX64.cs) | The compile-only candidate contains caller-thread open/read/hash/close/control operations, Linux `flock(LOCK_SH)` retry on `EINTR`, and a 65,536-byte native scratch range. A C11 translation unit compiled against the installed Windows SDK 10.0.28000.0 headers asserts `OVERLAPPED` size/offsets 32/16/24 and `FILE_STANDARD_INFO` size/offsets 24/8/21; its source hash is listed below. No native program or I/O ran. Linux x64 headers and all Arm64 profiles remain unverified; native-close and actual-return evidence are absent, and no production caller initializes the port. |
| Pinned CoreCLR [`Thread.CoreCLR.cs`][thread-core], [`Thread.Unix.cs`][thread-unix], [native finalization][thread-finalize] and [PAL creation][thread-pal] | `OnThreadExited` explicitly runs while the unmanaged thread is still alive and signals the Unix join event. Unix `JoinInternal` waits on that event; PAL creates a detached pthread. `Thread` also has native finalization work. Managed join is therefore not a receipt for destruction of the entire native thread/control suffix. |

**Distinguishing source-derived schedule, not a local experiment:** the Linux worker returns from its managed body, signals the managed join event, and its ordinary retirement caller returns from `Thread.Join`; native cleanup can still be running. Refunding all worker/TLS/stack/control provision then is unjustified. A larger requested stack or another worker-written exit flag does not repair this return boundary.

This follows the [file-I/O proposal's missing-bounds and staged-work sections][io-proposal], not its older source snapshot. It preserves the [file-handle proposal's native-disposition rules][handle-proposal]. The [bounded-lane proposal][lane-proposal] remains guidance except for the explicitly replaced execution selection below.

## 2. What would establish the actual worker envelope

A future worker consumer must close **the actual implementation**, not fill in `q(FixedWorker)` from a typical allocation sample:

1. Compile the complete private candidate without activating acquisition: static worker entry started with `Thread.UnsafeStart(object?)`, startup permit, exact operation storage, wake/wait implementation, cancellation policy, stop path, partial-start unwind and external retirement caller. Enumerate all reachable allocation sites and their simultaneous capacities, including failure and contended paths. Account for queued, executing, ready, consuming and returning records; a public lease count does not bound reference-inspection requests. Do not construct a dummy worker or mark a placeholder profile ready.
2. Derive managed layouts/headers/strides and native ABI layouts from that compiled target. Close runtime constructor/attach/start/wait/exit/finalizer membership, TLS, allocator metadata/caches, OS controls, stack mappings and cold/shared state from selected implementation/API evidence. Every term needs a supported bound and payer before startup. A source-inferred byte-array capacity is different from a bound on the allocator or native provider owning it.
3. Separate native access completion, managed body return, actual native termination, runtime-control retirement and enclosing caller return. Storage may survive a join only through an explicit, already funded lifetime transfer whose membership and release are themselves closed. Do not move per-Store tails into Batch A's shared partition, wait for GC to certify release, or retain successful workers forever as purported closure.

| Target | Required proof and corroboration |
|---|---|
| Windows 11, separately x64 and arm64 | Inspect the chosen CoreCLR **or NativeAOT** start/exit path, PE stack defaults, OS rounding, reserve/commit/guard behavior, TEB/TLS, CLR attachment and native thread/join-handle controls. [Windows stack documentation][windows-stack] distinguishes reserved and committed storage. Charge the complete attributable reservation once; report commit separately rather than adding the same pages twice. The selected [Windows join implementation][thread-windows] waits on a native thread handle, unlike Unix, but that does not destroy the managed `Thread` or prove native finalizer/cache retirement. Use actual stack-map, native heap and handle observations to check the derivation. |
| Ubuntu 24.04 glibc, separately x64 and arm64 | Inspect the selected runtime, PAL, libc stack/guard/TCB/TLS allocation and cache/destructor paths. Attribute overlapping stack/TLS storage once. The inspected CoreCLR pthread is detached: libc [`pthread_join`][pthread-join] cannot legally be retrofitted onto it. Establish a supported native-tail/control acknowledgment or separately review a genuinely joinable execution capability, including managed attachment; neither exists in Assets today. `/proc` disappearance, recycled thread IDs and managed join-event readiness are not that capability. |

Only after a complete source/API-derived provision fits the original grant may the real candidate start for focused tests. Reconcile cold and warm managed allocations on **both** initiating and worker threads with native heap, mapping, handle and retained-state observations, across startup, real reads, stop/join and repeated creation/retirement. Use ordinary test outputs and OS/debugger tooling, not a replacement qualification runner. An observed high-water mark, RSS delta or safety multiplier is not a maximum. Unexplained growth or an unobservable suffix leaves the worker profile unavailable.

Preinstall the root and startup interest before `Thread` construction/native initialization. A startup permit prevents package opening until the initiating ordinary caller has observed successful start return. Failure after possible startup retains that exact worker and its claim, even without a returned thread/session handle; join only a legally started thread, outside gates and never from itself. The actual caller must observe join/helper return and the separately established native/runtime suffix. Timeout, interruption or uncertain startup is not successful retirement. Unsupported partial-start/exit observations remain [Chapter 35 section 4][gates] gates, not permission to probe without funding.

This procedure is a reopening condition for a worker-based consumer, **not additional work required before implementing the no-worker lane selected here**. A native thread shim, process-wide worker, runtime fork and `Task.Run` substitute are not selected.

## 3. Exact narrow replacement and contract scope

Execute the whole bounded interval directly on its existing ordinary Content.Tools control-thread call stack. There is no new dispatch to the ThreadPool, synchronous blocking on an Assets Task, IOCP, worker callback, captured context or hidden background work. Continue refusing a sourced token, ambient `SynchronizationContext`, unsupported path/options and, on Windows, a non-MTA caller before native acquisition. Do not expose this entry or its handles to frame/render/audio/Scene invokers.

| Platform | Selected native port |
|---|---|
| Windows | Preowned read-only/read-shared `CreateFileW`, `OPEN_EXISTING`, overlapped/random-access flags; immediate installation of the returned HANDLE and same-handle type/length checks. One 65,536-byte native scratch range, one explicitly initialized `OVERLAPPED` and one manual-reset I/O event per original issuer. Issue checked-offset `ReadFile`, distinguish nonaccepted failure/immediate completion/`ERROR_IO_PENDING`, and establish actual completion with `GetOverlappedResult` before copying or reusing storage. No request/reply events, bound handle or continuation source. |
| Linux | Private blittable imports into the admitted glibc ABI: bounded NUL-terminated ASCII/UTF-8 path, read-only/close-on-exec/nonblocking `open`, immediate descriptor installation, same-descriptor `fstat` regular-file/length validation and offset `pread` on this caller. Review `off_t`, `ssize_t`, `size_t`, `stat` size/offsets/alignment separately for x64 and arm64 against their admitted headers; do not share an assumed `stat` layout. Preserve the selected advisory shared-lock/random-access policy without claiming Windows mandatory sharing. Reuse the scratch and operation record for short reads and `EINTR` retries. |

Hash the complete same file through that port using BCL incremental SHA-256; retain checksum-before-layout-error precedence. Reuse canonical profile/directory/envelope/reference validation and the sealed Mesh/Material 1.0 codecs. Both selected AssetIds must be distinct and positive, both package entries None. Reject unsupported content after the required checksum/checked-header observations without first allocating a general-size directory to discover that it exceeds this two-entry family. Keep typed array/range ownership explicit; add no `Memory<T>` bridge.

The tentative revision is deliberately narrow:

- Replace the worker/start/queue/join portions of the bounded-lane proposal's sections 1, 5–7 and Batch B in section 9 **only for `bounded-v1`**. Request-event, reply-event and worker-join receipt fields are inapplicable/empty, not evidence that those operations ran successfully. Keep the I/O-event receipt on Windows.
- [Chapter 02 section 2][memory] currently permits synchronous I/O on dedicated I/O workers, not this caller-executed Linux path. Select an explicit exception for this closed, noncancelable **offline-tool validation interval**, including deterministic ordinary handle release. It is not an interpretation that the existing wording already allows it. Chapters 02/04/11 and the Content.Tools guide must later record this selection and the previously proposed scalar-failure/same-envelope scope before activation; Chapter 30's ordinary public Assets contract remains unchanged.
- The general worker-based port and future asynchronous Scene invoker remain unavailable under their existing gates. Do not route ordinary `CreateAsync`/`LoadAsync`/replacement through this interval, narrow their inputs, change original-error delivery or silently preserve a BCL read/cache beneath a capable generation.

Initial candidate targets are Windows 11 x64 and Ubuntu 24.04 glibc x64. Arm64 requires independent layouts and observations; 32-bit, musl, other Unix, arbitrary runtime/OS/provider builds and unqualified filesystems are unavailable. An OS/64-bit check alone cannot admit a complete profile. This session inspected Windows files and pinned public source; it executed no Linux native code, worker, AOT artifact or new platform experiment. When a target cannot run locally, obtain the required evidence on that target's runner; cross-compilation is not an execution pass. WSL evidence, if later collected, identifies that actual kernel/libc/filesystem profile rather than certifying an unrun native Ubuntu target.

## 4. Original-budget profile and root-before-acquisition

`B0` remains the original Store's `CpuAssetBudgetBytes`. The alternative removes a thread from actual membership; it does **not** make file/event/hash/runtime/caller-stack storage free.

```text
Debit =
    original issuer, registration and terminal-delivery provision
  + attributed cold/shared state
  + fixed native port/control storage
  + every current/candidate/old/failed/returning generation's Keep
  + simultaneous opening/read/codec/result storage not already charged
  + unspent protected cleanup and actual-return provision
Debit <= B0
```

`Keep` covers the exact backing/entries, native file, retained generation inputs and final-close/receipt controls; fixed scratch/event storage belongs only to the port term. Spending a reservation does not add another entitlement. Preserve existing wire/decode/live-handle restrictions and count each physical allocation once; a decoded-wire debit is only a minimum, not proof it covers codec arrays, scanners or semantic-validation workspace.

Before any materialization, derive the complete selected startup/opening/failure/return preclaim with checked arithmetic. `_reservedBytes` may remain immutable only as that complete construction commitment; later live phase/generation debits belong to the same original issuer's checked ledger. **An already issued Batch A delivery can only end its unstarted preclaim; it is not an execution permit that can discover and charge missing costs after startup.**

The 131,343-byte reservation is a raw-capacity subtotal, not an all-in charge. Add actual allocation headers/alignment, native path staging, entries, port/receipt/root storage, synchronization under contention, native allocator/control overhead, hash-provider construction/workspace/disposal, interop initialization and transient exception/unwind peaks. Include additional caller stack commitment/retention caused by the interval and the terminal driver suffix: removing a new thread is not a blanket “caller-owned stack” exemption. Qualify those costs for the closed tool/runtime/provider input set; retain `ProfileUnavailable` for any unbounded or unproved term.

The existing one-MiB shared allowance does not prove that new provider/import/static state fits. Derive any full-profile shared partition before its first initialization and attribute it to that first original grant for its real lifetime. A process whose bootstrap is already ready under an incompatible preclaim-only provision must refuse full-profile initialization, not silently enlarge/refinance it. No new process quota, larger default budget or successor purse is introduced.

Install the original unpublished Store, pre-opening backing, exact claim, protected scalar outcome and intrusive acquisition/unwind root **before** scratch/event/file acquisition. Prepare bounded native path/import storage first. Initialize all fields to their actual sentinels, and install each successful result before the next fallible action; no owning SafeHandle constructor or string marshaler may hide an acquired intermediate. A failed factory still has this original owner even if the caller receives no usable Store.

Replacement admits its candidate peak against all existing debt and at most the lesser of `B0` and the candidate CPU limit. Lower replacement limits refuse new work; they do not erase older obligations. Every retained old backing, handle, temporary anchored reference, failed close and returning operation keeps its exact original provision. Store logical close releases only its owner reference. No bound on old generations is inferred from “two entries” or one ordinary slot.

## 5. Operation, unwind and return protocol

1. **One nonwaiting ordinary slot, no queue.** Opening, replacement, typed loads and reference inspections share the existing selected single-operation policy. The slot includes ready/consuming/returning interests, with checked identity/sequence and no wrap. Nested private reads use their enclosing admitted operation; they do not create another waiter. Concurrent/reentrant ordinary admission returns `Busy` before new acquisition. Preclaimed per-generation final-close progress cannot be starved by this slot.
2. **No port cancellation registration.** Accept only source-free tokens; the internal interval is noncancelable. Later CLI integration checks its original command token before entry and, after complete successful validation/retirement, before atomic publication. In-interval failure wins over cancellation; cancellation after publication does not undo it. Standard mode is unchanged. No timeout or abandoned observation can release accepted I/O storage.
3. **Own every acquired prefix.** Unwind scratch, event, file and hash-provider prefixes in safe dependency order, attempting independent safe siblings after failure. Never close/free an event, `OVERLAPPED`, scratch or file while accepted I/O may still use it. A failed wait or `CancelIoEx` result is not completion. An unreturned native call keeps the interval nonterminal, rooted and funded; this selection makes no hard latency/progress promise.
4. **Record one-shot native disposition before later cleanup.** After actual use quiesces, elect one close, immediately capture its result and error only where defined, and retain the immutable per-generation receipt. On Windows, nonzero `CloseHandle` establishes consumption; a zero result without a supported retaining/consumption proof remains retained/indeterminate. On Linux, a reached `close` on a valid owned descriptor consumes it on success and documented post-consumption errors, including `EINTR`; **never retry that descriptor number**. `EBADF`, interposed/denied and unclassified cases are not successful release. Descriptor zero is valid; invalid is `-1`. Initialize Windows file fields to `INVALID_HANDLE_VALUE` and event fields to null.
5. **Observe actual ordinary return.** The native-port caller observes helper return/unwind after native completion. The Runtime validation driver observes read/hash/decode/cleanup helper return; the actual Content.Tools recipient observes driver return before `EndDelivery`. Readiness, a self-written flag and Task terminality are not these observations. No wait, native cleanup, callback or join runs under owner gates.
6. **Keep the terminal transfer finite.** Preserve the bounded-lane proposal's delivery partition as a real output transfer under `B0`, not a refund at readiness. `EndDelivery` and final ordinary handle release retain their own prepaid return suffix. After acknowledged detachment the delivered carrier contains only scalar receipt/grant facts, with no issuer/backing/codec/exception/context aliases; its transferred partition is not reused as Store capacity. This avoids an infinite chain of observers.
7. **Retire the actual last generation.** The last supported ordinary internal handle recipient performs that generation's native close; the final issuer-quiescent ordinary caller retires shared scratch/event controls. Late closes need no worker join because none was created. Unsupported-thread release or abandonment cannot silently execute blocking cleanup elsewhere: preserve a preinstalled nonblocking retirement/quarantine root and original debt. Finalizers never allocate, wait/join or invoke Runtime/user cleanup; quarantine is failure retention, not successful shutdown or a reason to root normal successes forever.

Use the existing internal [`AssetBoundedReply`][reply]/failure family. Preserve primary execution failure and every applicable direct cleanup fact in its existing order: candidate file, displaced/final file, hash provider, I/O event, then inapplicable request event/reply event/worker join fields. Each older failed generation retains its own receipt and contributes to retained debt; do not squeeze all history into the two directly handled file slots. A consumed file followed by event/accounting failure is never closed again. A published replacement stays published when displaced retirement fails.

Map transient closed-code exceptions to scalar facts only after actual helper unwind; retain no EDI, original exception, faulted Task or diagnostic string graph. Transient throw/provider/runtime costs still require funding. Arbitrary first-chance/profiler/application hooks are not part of this closed-program profile. Missing disposition or return evidence yields failure/nonterminal retained ownership, never a successful retirement receipt.

## 6. Staged implementation and focused evidence

| Stage | Work and exit boundary |
|---|---|
| **B1 — complete compile-only port** | The current candidate preclaims and roots the port, per-generation file state, and fixed profile/directory buffers; it includes caller-executed native open/read/hash/close/control primitives, a pure two-entry None metadata parser, and a token-bound caller-span payload read with pure range preflight. There is no production caller, and the payload helper has not read a native file. Typed Mesh/Material validation, scalar terminal receipt, and last-generation delivery/return chain remain absent. Complete those owners while keeping initialization and acquisition unavailable. Windows x64 headers have compile-only static-layout assertions; this is not native I/O, close, or actual-return evidence. Preserve ordinary Assets behavior and dependency direction: Assets references Core, never Platform/Runtime. |
| **B2 — closed profile, then real native opening** | Derive the complete simultaneous provision before execution. Use exact-fit/one-byte-short admission, then real missing/inaccessible/regular package files for accepted prefixes. Reconcile cold/warm managed/native/caller-stack evidence with the derivation and actual one-shot release. Default-budget useful fit is required, not an always-refusing “profile.” An unsupported native term keeps only that target unavailable. |
| **C — both actual typed roots and late generations** | Route all bounded hashing, metadata, None reads, envelope/reference inspection and both sealed codecs through this same generation's port. Derive malformed/rejected-input and retained-result peaks. Validate the existing independently authored GLB/OBJ triangle packages: three vertices/indices, one group/material slot, no Skeleton/textures. Exercise retained/anchored handles across replacement and Store close under unchanged `B0`. |
| **D/E — actual recipient, then explicit CLI** | Integrate the synchronous Runtime driver → actual Content.Tools recipient → `EndDelivery` chain. Only after full applicable ownership/profile/native/return gates and owner-contract reconciliation add `--package-validation bounded-v1`, phased cancellation and receipt printing. Execute the actual published NativeAOT tool on each admitted target; verify standard/bounded output bytes match and refusal/failure/cancellation never replaces prior output. No automatic standard-mode retry. |

Extend the existing owning fixtures, not a generic gate framework or test-only producer:

- Keep all six preclaim regressions; add complete-profile exact/one-short and mismatched-target refusal before native access. A missing-path variant distinguishes pre-acquisition refusal from an attempted open.
- In Assets reader/Store/package-lease fixtures, cover actual native ranges, chunk-boundary short-read accumulation, early EOF, checksum mismatch versus correctly checksummed malformed input, unsupported third/Brotli entries, and cleanup after a genuine later-prefix failure.
- Cover simultaneous ordinary admission, nested reference inspection, checked identity exhaustion, retained old generations, failed candidate/displaced retirement, Store-close-before-last-handle and repeated close. Verify debt persists through actual return and no second native close occurs.
- Cover primary plus independently safe cleanup facts, consumed-with-error and uncertain disposition logic without presenting modeled outcomes as real native-failure observations. Do not corrupt handles or use throwing streams/allocators to certify native close or thread startup.
- Exercise the real typed producer and final tool recipient, including cancellation during the noncancelable interval, no publication on failure and scalar-only terminal output. Count framework tests separately from native/AOT/consumer observations.

Use ordinary owning-project `dotnet build`/`dotnet test`, Debug before Release, with focused Assets/Runtime filters and Content.Tools tests when integrated; follow [Building][building], retain fresh TRX/logs under `artifacts`, and confirm nonzero selected/executed tests. Do not run these future tests against absent implementation or treat existing Batch A results as their pass.

If a required genuine native close failure, partial-acquisition failure, blocked native return or worker-start/exit stimulus cannot safely be obtained, record the exact affected [Chapter 35 sections 2.3/4][gates] obligation, missing capability/stimulus and retry condition. Static proofs and managed logic tests remain useful but do not replace required native observations. Do not repeat an unchanged blocked probe; keep activation unavailable where that observation is required. Caller-executed membership legitimately contains no thread startup/join test; that is not a worker qualification pass.

## 7. Unblocked scope and remaining boundary

This choice unblocks **implementation** of a real bounded native opening port and follow-on Mesh/Material validation/CLI work without inventing a worker allowance. It can supply M05's actual static-adapter/package-validation capability once its own gates pass. Other dependency-ready M05 work continues under Chapter 35's per-capability sequencing. It neither closes Scene planner/cache/receiver/publication ownership nor qualifies a Scene invoker, nonempty restore, extraction/submission or public renderer consumer. Keep `PreparedScene.QualifiedFootprint` null. The diagnostics return-path correction at `2f4f6a41` is outside this blocker and remains resolved.

Current boundary: B1 now has rooted port/file/profile/directory owners, a compile-only same-file metadata reader, and a token-bound caller-span payload-read/preflight helper. The 15/15 reader selection exercises only pure range validation; no production caller invokes the bounded path. Native initialization/acquisition, typed Mesh/Material validation, scalar terminal delivery, and actual-return closure remain unavailable. This remains tentative guidance, not a maintained-contract amendment or qualification; no public API, schema/diagnostic/acceptance ID, budget increase, or remote publication is selected.

## 8. Review-input identity

Snapshot UTC `2026-11-20T18:51:24.6065631Z`; HEAD `d962a294809a84049eba29298e59cf3c19c11cf7`, branch `tannergooding-m05-implementation-plan`. The listed technical inputs dirty against HEAD are `sources\TerraFX.Assets\AssetPackageReader.cs` in A and `tests\TerraFX.Assets.Tests\AssetPackageReaderTests.cs` plus `tests\TerraFX.Runtime.Tests\Scenes\BoundedStaticMeshAssetValidationTests.cs` in T; their current bytes are hashed below. The five unrelated dirty tests in P are preservation-only, not reviewed implementation. Exclude this output.

Set digests are SHA-256 of ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` rows, LF-joined, UTF-8 without BOM/trailing newline. ALL unions A/R/S/T/D/C/V/E. External runtime links select unified-source revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; the local V marker identifies `11.0.0-rc.3.26522.148`, not a loaded runtime/native binary or NativeAOT qualification. The consulted Windows stack page identifies documentation revision `3aee48ac3f48e187d17bdfd56951741737331749`; Linux `pthread_join(3)` is man-pages 6.19, dated 2025-05-17. Neither supplies an all-in allocation bound.

```text
A: sources\TerraFX.Assets\
AssetStore.Bounded.cs, AssetStoreBoundedProvision.cs, AssetStoreBoundedBootstrap.cs,
AssetStore.cs, AssetStore.PackageLease.cs, AssetPackageBacking.cs,
AssetPackageReader.cs, AssetPayloadReader.cs, AssetBoundedReply.cs,
AssetBoundedFileDisposition.cs, AssetBoundedFailureStage.cs,
AssetBoundedNativeApi.cs, AssetBoundedNativePort.cs, AssetBoundedNativeFile.cs,
AssetBoundedLinuxStatX64.cs, AssetBoundedWindowsOverlapped.cs,
AssetBoundedWindowsStandardInfo.cs, AssetStore.Bounded.Native.cs, TerraFX.Assets.csproj
R: sources\TerraFX.Runtime\Rendering\
BoundedStaticMeshAssetValidation.cs, BoundedStaticMeshAssetRegistrationFactory.cs,
BoundedStaticMeshAssetDelivery.cs;
sources\TerraFX.Runtime\Scenes\PreparedScene.cs;
sources\TerraFX.Content.Tools\StaticMeshCookCommand.cs
S: sources\TerraFX.Core\CoreContentSchemaCatalogue.cs;
sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs;
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs;
sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs
T: tests\TerraFX.Runtime.Tests\Scenes\BoundedStaticMeshAssetValidationTests.cs;
tests\TerraFX.Assets.Tests\AssetPackageReaderTests.cs,
tests\TerraFX.Assets.Tests\AssetStoreAllocationTests.cs
D: docs\design\
02-dotnet-api-memory-and-ownership.md, 04-capacity-and-workload-profiles.md,
11-assets-import-streaming-and-packages.md, 29-project-setup-build-and-deployment.md,
30-public-api-review-surface.md, 35-implementation-roadmap.md;
docs\project\Building.md, docs\project\Roadmap.md;
docs\proposals\
m03b-assets-file-io-port-implementation-closure.md,
m03b-assets-file-handle-release-acknowledgment.md,
m03b-assets-bounded-static-mesh-opening-lane.md
C: .editorconfig, .github\workflows\ci.yml, Directory.Build.props,
Directory.Build.targets, sources\Directory.Build.props, global.json
V: .dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\.version
E: artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_09_56_12_net11.0.trx;
artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_09_57_01_net11.0.trx;
artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_09_55_08_net11.0.trx;
artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_09_55_17_net11.0.trx;
artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_10_47_32_net11.0.trx;
artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_10_47_44_net11.0.trx;
artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_10_49_45_net11.0.trx;
artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_10_50_24_net11.0.trx
P: tests\TerraFX.Runtime.Tests\Scenes\
PreparedSceneAssetRetentionTests.cs, RenderSpriteCapacityTests.cs,
RenderSpriteComponentTests.cs, RendererExtractionTests.cs,
SceneAssetStoreTests.EngineHost.cs
```

| Set | Files | SHA-256 |
|---|---:|---|
| A | 19 | `a5da893966db84ca82fafccfc91a7356ccc032d2b670c0855cd70751629d6e1f` |
| R | 5 | `3e78dbeb89f9c6156e7ba361fcdaf3f21b4e6848b0b9722bd4b30a526c2d4064` |
| S | 4 | `e29908e3df557008195f0f7b088bdfe6f241102590c2587bcc092d57da274e01` |
| T | 3 | `117b4d5d712835026aca42d1540a7d1c6cd9432d8e89f6be23925bbab4dbbff1` |
| D | 11 | `6320fad2da177942725d80eafbf4a5e91e169ea6f6a3c9e77bd930b327b14cce` |
| C | 6 | `c79d188b6f611c5f6fdce6759b15be4d4fc0b8af61b2fcabbfe761d66ed0dc17` |
| V | 1 | `aab4f62773986811741aea2bbe2f631c7fbaeeefd485eca380deba5622b1ba8e` |
| E | 8 | `b60e511863b7ddfadae572c664758f71d0afc5187d85515945a29e5233d81f62` |
| ALL | 57 | `21cee9d589699d7fd2bfc5c01029392d89f64e345bcf82a8a23a2d72f0e76d83` |
| P, preservation only | 5 | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |

External Windows SDK headers inspected for x64 field layout (not included in ALL):

| SDK header | SHA-256 |
|---|---|
| `10.0.28000.0\um\minwinbase.h` | `7d1408f4b8eeba96ae45892209132258cde80cab6dab192b4cceea591972c78b` |
| `10.0.28000.0\um\WinBase.h` | `08d0e56ea3801bba0980d83fdc7d68041b014352b01910a779eff2de7969973d` |
| Compile-only x64 static-assert source | `e3e963369a5e008c1adcb7cd3b78285e070b2e62a5af5bbba6afd5a0d7783e8d` |

Preserved schema hashes, in S order: Core `91cf1aa2ff4feaba898d83e587d4be403de6cfea97cfbb59fb15d5b67edf2853`; Assets `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a`; Scenes `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a`; Rendering `de25a8c0e249ff9ab5b592fe848c85429c004fe629694c64ef1afef3ba731614`.

Recheck these exact inputs before review disposition or implementation; changed bytes require scoped reassessment. Tentative selection does not turn these hashes, earlier test results or source analysis into native/profile acceptance.

[preclaim]: ../../sources/TerraFX.Assets/AssetStore.Bounded.cs
[provision]: ../../sources/TerraFX.Assets/AssetStoreBoundedProvision.cs
[bootstrap]: ../../sources/TerraFX.Assets/AssetStoreBoundedBootstrap.cs
[validation]: ../../sources/TerraFX.Runtime/Rendering/BoundedStaticMeshAssetValidation.cs
[factory]: ../../sources/TerraFX.Runtime/Rendering/BoundedStaticMeshAssetRegistrationFactory.cs
[preclaim-tests]: ../../tests/TerraFX.Runtime.Tests/Scenes/BoundedStaticMeshAssetValidationTests.cs
[reader]: ../../sources/TerraFX.Assets/AssetPackageReader.cs
[backing]: ../../sources/TerraFX.Assets/AssetPackageBacking.cs
[payload]: ../../sources/TerraFX.Assets/AssetPayloadReader.cs
[reply]: ../../sources/TerraFX.Assets/AssetBoundedReply.cs
[io-proposal]: m03b-assets-file-io-port-implementation-closure.md#4-same-envelope-derivation-and-the-exact-missing-bounds
[handle-proposal]: m03b-assets-file-handle-release-acknowledgment.md#native-disposition-is-separate-from-cleanup-success
[lane-proposal]: m03b-assets-bounded-static-mesh-opening-lane.md
[memory]: ../design/02-dotnet-api-memory-and-ownership.md#2-reuse-ledger
[gates]: ../design/35-implementation-roadmap.md#4-completion-gates
[building]: ../project/Building.md
[thread-core]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs#L582-L595
[thread-unix]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Unix.cs
[thread-windows]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Windows.cs
[thread-finalize]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/comsynchronizable.cpp#L611-L635
[thread-pal]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/pal/src/thread/thread.cpp#L564-L589
[windows-stack]: https://learn.microsoft.com/en-us/windows/win32/procthread/thread-stack-size
[pthread-join]: https://man7.org/linux/man-pages/man3/pthread_join.3.html
