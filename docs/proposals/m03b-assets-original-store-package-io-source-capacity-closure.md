# M03-B ordinary original-Store package-I/O source-capacity closure

**Disposition: tentatively selected for implementation progress under the user's unblock instruction; later owner review remains required. This is a source-priced, lifecycle-incomplete candidate, not accepted API/design text or an installed native/runtime profile. Native-owning worker materialization/start remains gated on the unresolved finalizer/termination mechanism. Keep `PreparedScene.QualifiedFootprint` null.**

## 1. Decision

Continue the [ordinary file-I/O selection](m03b-assets-file-io-port-implementation-closure.md): one dedicated package-I/O worker per original Store issuer, shared across its generations; Windows overlapped/event I/O and Linux offset `pread`. Apply the narrower accounting subject in the [original B0 rectification](m03b-assets-original-b0-source-capacity-rectification.md), not the earlier complete transitive memory interpretation.

**Batch B needs separately visible additional terms inside the same original B0.** Transfer A's unspent native scratch/OVERLAPPED provision into those exact blocks, but do not treat its already materialized operation carrier, protected messages, or metadata reservation as spare worker funding. The catalogue below derives the added declarations and request membership independently. It does not increase B0, add a purse, or permit replacement options to refinance earlier debt.

Select one-shot lower request records, including an embedded completion core, rather than source recycling whose actual caller-return boundary is presently unproved. One logical exact-extent request performs its own short-read loop. One hash request streams the entire package. Neither allocates another source record per native chunk, short read, polling interval, or `EINTR` retry. Opening reserves its complete finite request-record peak before acquisition; ordinary later requests need positive additional preclaims.

This selection is **only for the ordinary original-Store lane**. The existing [bounded-v1 Batch B proposal](m03b-assets-batch-b-worker-envelope-rectification.md) selects caller-executed offline-tool I/O and remains unchanged. Its measured shells, narrow paths, scalar delivery, two-entry membership and allocation allowances are not prices or authority for this worker.

Public signatures, defaults, General registrations, None/Brotli support, ordinary cancellation/fault delivery and package semantics remain unchanged. The changed budget subject and proposed recovery/return rules still require Chapters [02](../design/02-dotnet-api-memory-and-ownership.md), [04](../design/04-capacity-and-workload-profiles.md), [11](../design/11-assets-import-streaming-and-packages.md) and [30](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) disposition. Chapter 02's linked Chapter 11 recovery-delivery definition is still missing. Prose supplies neither that acceptance nor any Consumer, Scene, native, NativeAOT or M05 gate.

## 2. Current evidence and its consequence

Evidence includes the initially dirty A implementation and its current bytes at `9a6715cf51e5ee75487a760d947ae6a5c249a4bc`.

| Current source | Consequence |
|---|---|
| [`AssetStore.OriginalOwnership.cs`](../../sources/TerraFX.Assets/AssetStore.OriginalOwnership.cs), [`AssetStore.cs`](../../sources/TerraFX.Assets/AssetStore.cs) | A charges S = 66,472, K(n) = 1,080 + 32n and Open = 4,334,016. `_ioControl` is a real `OriginalOperation`; the 65,536 + 32 native extents remain unmaterialized. A has no worker, event, native acquisition or source request queue. |
| [`AssetPackageReader.cs`](../../sources/TerraFX.Assets/AssetPackageReader.cs) | The installed opening still constructs FileStream, hashes through it, and uses `Memory<byte>`/RandomAccess. A's source preclaim does not qualify these constructor, native-close or async-return paths. All generation acquisition/hash/read/close sites must change together before a native generation is selected. |
| [`AssetPackageBacking.cs`](../../sources/TerraFX.Assets/AssetPackageBacking.cs) | The existing reference count, retirement operation, failure root and generation identity are useful carriers. Managed stream-release return is not a native disposition. Sticky token/read-limit interests are real retained source debt, not a reason to keep an otherwise quiescent worker running indefinitely. |
| Store disposal and `EndOriginalServiceProvision` | A returns its unused service provision at logical disposal. Once it materializes a worker, this transition is illegal while old generations, queued/returning work or retirement callers still require that worker. The service lifetime must be corrected before materialization. |
| [`AssetStore.PackageLease.cs`](../../sources/TerraFX.Assets/AssetStore.PackageLease.cs), [`AssetStore.ReadDescriptor.cs`](../../sources/TerraFX.Assets/AssetStore.ReadDescriptor.cs), [`AssetPayloadReader.cs`](../../sources/TerraFX.Assets/AssetPayloadReader.cs) | Inspections and temporary-reference refusal are not bounded by issued lease credits. Current payload arrays, pooled Brotli input and higher source controls still need originating grants. A wire debit alone is not a buffer grant. |
| [`AssetStoreOptions.cs`](../../sources/TerraFX.Assets/AssetStoreOptions.cs) | Validate checks nonnegativity only. This derivation uses enforced package/directory/profile ceilings, not Chapter 04's unenforced hard option ceilings as actual worker/request bounds. |
| [`AssetStoreAllocationTests.cs`](../../tests/TerraFX.Assets.Tests/AssetStoreAllocationTests.cs), [`AssetStoreOriginalOwnershipTests.cs`](../../tests/TerraFX.Assets.Tests/AssetStoreOriginalOwnershipTests.cs) | Current assertions cover A's field/stride expressions and original-generation accounting, not new worker declarations or native/caller-return observations. Preserve the exact-fit and held-generation regressions while updating the subject of their budgets. |

Use Assets' existing owner area and private bindings; retain Assets → Core only under [29](../design/29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag). Do not reuse the bounded native port's source-size constants or assume its compile-only error paths close this worker's ownership.

## 3. Closed candidate storage catalogue

These are **enumerated strong-state candidate declarations**, not measurements of code already implemented or a closed native-finalizer/termination catalogue. The numerical lane is Windows x64 CoreCLR `11.0.0-rc.3.26522.148`, reference revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. Verify the compiled candidate's complete fields/base chain, value widths and allocation sites before installing its price. Any different or additional field, holder, array, string, boxed product or block requires rederivation before materialization. Section 5's unresolved execution-root lifetime prevents native-owning worker materialization even if these source expressions fit.

Use A's conservative source-field model:

```text
Align8(n) = checked((n + 7) & ~7)
Slots(T) = sum(Align8(W(f)), f in all instance fields of T/base)
Q(T) = max(24, Align8(16 + Slots(T)))
A<T>(c) = Align8(24 + checked(c * managed stride(T)))
Native(c) = c
```

References, including nullable/null holders, are eight-byte fields. Inline values include their complete managed padding; do not align each constituent of an embedded value independently. Object/array prefixes and alignment are part of this selected source envelope, not a complete physical-memory guarantee.

### Existing carriers after B's changes

| Carrier | Complete membership/change | Raw fields | Q |
|---|---|---:|---:|
| Store | Eleven reference-width fields: A's ten plus `_originalPackageIo`; three Int64, three Int32, one Boolean | 125 | 160 |
| Original issuer | One gate reference, four Int64, one Int32; no Store/backing/request references | 44 | 64 |
| Backing | Sixteen reference-width fields, including its two inline ImmutableArray holders and reported-path holder; two Boolean, thirteen Int32, four Int64, ProfileId 16, Settings 8, one native pointer 8 | 246 | 328 |
| Original operation | A's eight references plus `_ioRequest`; CancellationToken 8, three Int64, four Int32 | 120 | 152 |
| Opening storage | Ten reference-width fields, including its inline entry array; one Int32 | 84 | 104 |
| Read limits / backing token / shared anchor | Unchanged declarations, independently retained | 44 / 0 / 28 | 80 / 24 / 32 static slot bytes |

For the backing, **replace** the `_stream` reference with the shared-worker reference; do not keep a dormant BCL file owner beneath a capable generation. Add a Windows HANDLE field and six Int32 fields: Linux descriptor, native API, acquisition state, one-shot close state, disposition and close error. Add `_originalReportedPath`, an eight-byte string holder for the ordinary normalized exception-path context through later reads/close. Reuse `PackageByteLength`, generation identity and the existing reference count. Native-call/disposition state does not become a competing reference count. The existing source-interest bit field records whether the reported string is borrowed or owns the separate F(g) claim below.

For opening storage, replace its `_stream` holder with the prepared `char[]` path holder. Add the IncrementalHash holder and its constructor/disposal state Int32. The remaining header/profile/directory/padding/digest/options/Builder/entry holders retain their current widths. Transient path arrays still receive their own capacity claims even when held only in a synchronous helper.

O retains the two existing source-message provisions:

```text
O_B = 152 + 544 = 696
```

The 544 belongs only to A's unchanged, closed producer grammar. The native worker transports raw result/error/disposition fields and retained original failures; it creates no new formatted source messages. A new native/path diagnostic formatter or payload must derive and claim its own bound, not inherit the old 123-character assumption. Exception/EDI/Task internals remain opaque interests, not a price of 544 or permission to truncate original faults.

### New worker and request declarations

`OriginalPackageIo` has the following complete field set:

| Storage | Fields | Raw / aligned slots |
|---|---|---:|
| Nine references | issuer, existing service-return operation, object queue gate, Thread, queue head, queue tail, active request, returning head, precreated retirement callback | 72 / 72 |
| Three native pointers | scratch, OVERLAPPED block, I/O event | 24 / 24 |
| Three Int64 | source claim, next request sequence, live generation interests | 24 / 24 |
| Twelve Int32 | lifecycle, startup permit, queued count, native use, publication use, stop request, exit observation, retirement queued, join state, event-close state, event-close error, scratch-release state | 48 / 96 |
| **Worker shell** | `Q = 16 + 72 + 24 + 24 + 96` | **168 / Q 232** |

Its service-return operation is A's `_ioControl`, counted once despite aliases. Construct that service operation without capturing an application SynchronizationContext; it has no Store/backing/input alias while parked. This is private worker-context selection, not suppression of public callers' context semantics. Static worker/retirement callbacks take the subordinate control as state and capture no public Store. Active/queued/returning requests supply the required strong roots.

The worker executes package-native calls, synchronous owned copies and whole-package BCL hash work only. It never executes a codec body, Runtime work or public Task completion.

`OriginalIoRequest` has:

| Storage | Fields | Raw / aligned slots |
|---|---|---:|
| Seven references | issuer, worker, operation, backing, queue-next, return-next, typed byte-array destination | 56 / 56 |
| Inline token/core | CancellationToken 8; ManualResetValueTaskSourceCore&lt;int&gt; 40 | 48 / 48 |
| Four Int64 | request identity, source claim, checked file offset, native-attempt sequence | 32 / 32 |
| Thirteen Int32 | kind, destination offset, length, result, error, wait/auxiliary error, cancel/auxiliary error, lifecycle, native use, publication use, registration use, consumption use, return use | 52 / 104 |
| **One-shot request** | `R = 16 + 56 + 48 + 32 + 104` | **188 / Q 256** |

The two added auxiliary slots retain independently failing wait/cancel calls without overwriting the terminal read error. Existing kind/state bits tag their API and precedence; an open request may use an otherwise unused auxiliary slot for open-prefix restoration failure. Cancel-attempted/observed-cancellation flags use existing state/native-use bits, keyed to the checked native-attempt sequence. Adding a different representation requires a new field-derived price.

The pinned [completion-core source][completion-core] contains four reference fields, Int32 result, Int16 version and Boolean asynchronous-continuation flag: 39 raw bytes rounded to a 40-byte inline value in this model. Its referenced EDI/context/delegate machinery is not another source-owned object price. Verify the actual inline width in the candidate; the test helper currently correctly refuses unknown value types.

Do not Reset/recycle this core in B. A fresh logical request has one completion and one consumer; its checked issuer-local identity is not the core's short version token. Use asynchronous continuations and raw facts rather than `SetException` recapture in the lower source. Registration/consumption/publication interests survive readiness until their actual enclosing callers return. Extra engine-defined completion/queue wrappers would be additional Q terms. This is a tentative private ticket, not a public ValueTask API or measured performance improvement; Chapter 02's selection/return requirements remain.

Register only the installed control wake on the lower source, without FlowExecutionContext/UseSchedulingContext flags. Preserve application context in the ordinary wrapper/actual invoker above it. Otherwise the pinned core can call an arbitrary SynchronizationContext.Post or scheduler during publication even with asynchronous continuations enabled. The worker must not execute that user code. Dispatch/publication failure after native completion still retains its actual return/failure interest; do not reset or complete the source again.

### Native blocks, paths and metadata

- Transfer exactly `Native(65,536)` into one worker scratch allocation and `Native(32)` into one separate, initialized native OVERLAPPED allocation. Existing [`AssetBoundedWindowsOverlapped`](../../sources/TerraFX.Assets/AssetBoundedWindowsOverlapped.cs) and the [Windows declaration][overlapped] establish two pointer-width status fields, an eight-byte offset union and an event pointer: 32 bytes on x64.
- The OVERLAPPED address must remain stable through actual native completion. Do not embed it in a movable managed worker and retain a pointer after a scoped pin ends. A managed scratch alternative costs `A<byte>(65,536) = 65,560`, not 65,536; it is not selected here.
- `P(o) = sum A<char>(c_i)` over the opening's prepaid path capacities. Each `c_i` includes its selected NUL/prefix/suffix extent; char stride is two. Obtain checked required capacities through acquisition-free native path queries before allocating each buffer. During growth, charge both old and new capacities until old use/helper return ends. Complete path preparation and its claims before Thread construction/start or package acquisition.
- Install the reported path in the backing before native acquisition. It is the ordinary normalized path, not an automatically added native device prefix. Borrow `options.AbsolutePackagePath` only when the reporting path is exactly that prebuilt string; otherwise preclaim `F(g) = Align8(16 + 4 + 2*(f + 1))` for its f-character string before copying. This one per-generation source product survives opening-buffer retirement and is shared by that generation's exception constructors; aliases do not debit another string. On a normalization failure, the reporting input remains the borrowed original path.
- Preserve the pinned [normalization/short-name behavior][path-helper] and [device-prefix rules][path-internal]; fixed 511-character bounded-v1 input is not the ordinary contract. [GetFullPathNameW][full-path] distinguishes success length excluding NUL from required capacity including NUL. Short-name expansion, a partially existing suffix and a later size change require their actual capacity arithmetic. A query is neither validation of file identity nor authority to allocate an uncharged result. Do not call BCL `Path.GetFullPath` first and price its returned string afterward. If a source-owned string/copy is selected instead, derive its complete pre-allocation envelope; no unexplained path constant is supplied here.

Unchanged metadata arrays remain header 88, profile 65,560, directory 4,194,392, padding 40 and actual digest 56 source bytes. Entry stride 32 follows the current AssetId/codec/padding/three-UInt64 declaration, not merely its wire width. One exact-capacity Builder array transfers into K once.

Reusing the unchanged Core profile path gives the independently enumerated source subtotal:

```text
Profile = 88 + 48 + 3*A<byte>(32) + A<reference>(1)
        + 64*56 + 3*A<reference>(64)
        + 65,536 + 64*(24 + 7) + 56
        = 73,104
```

These are binding/context/three digests/profile-selection/field/stage-sort-immutable/payload/record capacities from FoundationCodec, FoundationProfileBinding, FoundationCodecContext, EbinCodec.Stage, EbinField and EbinRecord. Field count 64 and disjoint payload bytes 65,536 bound the copies; 24 + at most seven alignment bytes explains each payload-array overhead. Changing whole-package hashing does not remove these per-profile products or the separate settings-hash interest.

## 4. Exact additional B0 terms and distinguishing fit

The conservative opening reservation counts one open request, one whole-package hash request, three metadata extent requests and at most n nonempty padding reads. Directory offset and `64 + 32*n` length are both 16-aligned, so directory-to-first-payload and the first entry gap are zero. Only the n−1 inter-payload gaps and final gap can be nonempty. For n = 0 all those gaps are zero. No request is constructed for a zero gap.

```text
N_open(n) <= n + 5
N_open_max = 131,077
R * N_open_max = 33,555,712
```

Reserve this peak because earlier records may still be returning while later logical reads proceed. It is not a fixed queue array, a requirement to allocate every reserved record, or permission to retain all successful requests permanently. Native chunking/short-read retries stay inside the existing request. Reclamation requires the real return join; a smaller price needs a proved smaller simultaneous membership.

```text
S_B = 160 + 64 + O_B + 232 + Native(65,536) + Native(32)
    = 66,720

K_B(n,g) = 328 + 80 + 24 + O_B + R + A<AssetPackageEntry>(n) + F(g)
         = 1,408 + 32*n + F(g)

OpenBase_B = O_B + 104 + 88 + 65,560 + 4,194,392 + 40 + 56 + 73,104
           = 4,334,040
Open_B(o) = OpenBase_B + R*N_open_max + P(o)
          = 37,889,752 + P(o)

ColdCreate_B(o,g) = 32 + S_B + K_B(131,072,g) + Open_B(o)
                 = 42,152,216 + P(o) + F(g)
```

K includes the generation's preconstructed final-close request and retirement operation; never debit that close request again as ordinary queue membership. S includes the actual worker shell plus its aliased service operation, not another issuer or anchor.

For an explicit capacity example, two concurrently prepaid 512-code-unit path arrays cost `P = 2*Align8(24 + 2*512) = 2,096`; a fresh 511-character reported path costs `F = 1,048`. The cold source preclaim is **42,155,360**, versus A's **8,595,904**: an additional **33,559,456** from the *same* original budget. These are declared capacities, not observed path lengths or initialized native-memory totals. The default 536,870,912 can finance this example; an A-exact-fit B0 cannot. Merely starting a Thread because A's scratch is reserved would violate admission or the separately closed lifecycle gate.

Expose the delta, rather than folding it into unexplained padding:

```text
Additional_B =
    248 fixed service bytes
  + 328 per complete generation base claim
  + 24 per complete opening base claim
  + 256 per reserved opening/ordinary request not already in K
  + source-owned path buffers and reported-path strings
```

The 248 is Store holder 8 + service-operation holder 8 + worker Q 232. The generation-base 328 is backing change 64 + retirement-operation change 8 + close request 256; F(g) is additional only for an owned reported string. The opening-base 24 is operation change 8 + opening-holder change 16. A's 65,568 native reserve transfers without a second debit. These transfers apply to new fully preclaimed construction, not retrofitting native ownership around an existing FileStream/SafeFileHandle.

Maintain disjoint live-wire, service/anchor, unique generation, opening, ordinary request, retained source-subset and unspent-provision membership under original B0. Candidate admission adds `K_B(max,g) + Open_B` to **all** existing debt, including its preclaimed reported-path capacity before any native acquisition. Positive unreserved request charges bound outstanding requests by remaining B0; lease-count limits do not. Existing backing-sensitive wire/decode/lease limits remain separate constraints. Refunded wire bytes do not refund a returning request or enlarge the original issuer.

## 5. Opaque interests and actual ownership

The source terms above exclude opaque shells/internals, not their holder slots or lifetimes. Do not supply prices for Thread stacks/TLS/CLR attachment, monitor contention, SafeHandle/native controls, hashing providers, dispatch/context machinery, allocator overhead or OS/filesystem bookkeeping.

| Resource interest | Source-boundary multiplicity and owner |
|---|---|
| Thread construction/start/stop/join | One Thread construction attempt and at most one worker per original issuer; no restart worker. Preinstall the source control/start interest before construction, including an unreturned prefix. The Thread's StartHelper, join SafeWaitHandle and native/runtime descendants remain under distinct construction/start/termination protocol interests, not a claim that one call allocates one object. |
| Synchronization | Existing Store/issuer/backing/shared-anchor Lock interests remain distinct. Add one worker object/monitor gate; Q pays its reference, not its opaque object or contention state. Do not use `Monitor.Wait` on the unrelated gate entered through `Lock.EnterScope`. |
| Completion and dispatch | One core registration/consumption/publication protocol per issued request, with its actual context/scheduler/dispatch interests. One Store disposal TCS/associated-Task pair; one replacement barrier pair per elected candidate; one public response per invocation and one internal opening-result protocol per opening. Returning pairs remain distinct after candidate body election ends. Their producer/fault/context/return owners survive readiness; ValueTask values do not imply another Task allocation. |
| Terminal retirement dispatch | One precreated static callback and at most one queued terminal retirement work-item protocol per issuer, elected atomically. This supplies the independent non-worker join candidate, including finalizer handoff; it is not another package-I/O scheduler or proof of work-item/outer dispatch return. |
| Package file | One direct acquisition attempt per exact generation, immediate sentinel-aware installation, same-handle type/length validation, at most one issued native close. No BCL file/SafeFileHandle owner or IOCP binding remains beneath a selected native generation. |
| Event/native I/O | One manual-reset completion event per Windows worker; zero Windows events on Linux. One active native read using the fixed blocks. Event close is independent of file close; uncertain read completion retains blocks, event, file use and a nonterminal request. |
| Hash | One incremental whole-package provider protocol per opening, plus the existing settings-hash call per binding. Include constructor, append/finalize, disposal, failure and enclosing-return interests. Returned/copied digest storage is positively charged separately. |
| Path/import substrate | Each selected normalization/expansion/query/import call owns its framework/native prefix/return interest. Explicit path buffers remain source charges; the borrowed original options/path/token/registration/context graphs do not become new unbounded engine owners. |

Use the [bootstrap/close ownership](m03b-assets-prelease-bootstrap-and-close-closure.md) and [file-handle disposition rules](m03b-assets-file-handle-release-acknowledgment.md): native consumption precedes later cleanup/accounting. A failed or indeterminate close does not authorize another close against a possibly recycled handle/descriptor. Linux-specific consumed-with-error rules are not copied to Windows; Linux `close` is not retried after reached valid-descriptor `EINTR`. Independent safe cleanup continues after a sibling fails, preserving primary failure and original cleanup samples.

If the lifecycle gate is later closed, use [`Thread.UnsafeStart(state)`][thread-start] with the static parameterized entry and a startup permit opened only after the initiating enclosing caller observes start return. A possibly started/unreturned prefix retains its original interest and provision. Initialize Windows file HANDLE to −1, Linux descriptor to −1, event/block pointers to null and native OVERLAPPED/status storage before use. Source products use SkipLocalsInit; copy/hash only bytes the completed native call actually wrote.

Preserve the selected cancellation policy: checks before enqueue/dequeue/issue; Windows finite 50-ms event waits with one CancelIoEx request on observed cancellation; Linux a running `pread` retains use through actual return/EINTR handling. No port-owned cancellation registration, thread interruption or close-to-cancel. Polling is not a deadline or hard latency guarantee.

Native use completion, helper return, publication return, source registration/consumption return, thread termination and retirement-caller return are separate facts. The [actual-invoker proposal](m03b-scene-actual-invoker-return-closure.md) remains the higher boundary; B supplies no Scene acknowledgment. Neither a source-local finally nor a completed Task observes its own enclosing caller.

The pinned [Thread start/CoreCLR source][thread-core] calls OnThreadExited after the delegate; it explicitly says the native thread is still alive. [Unix Join][thread-unix] waits on that managed exit event, whereas [Windows Join][thread-windows] normally waits on an OS thread handle and has closed/invalid-handle/finalizer paths. **Do not demand an all-in byte bound on these excluded tails, but do not infer their disposition from Join either.** A proposed transfer of an opaque framework-retired tail must prove that no engine/source-data access survives and identify its remaining owner/protocol. It is not supplied by this proposal. Source blocks/control claims remain paid wherever actual use/return is unproved.

Store disposal closes new ordinary admission, not prepaid close/retirement progress. Stop/join only after every generation requiring I/O and every actual request/return use ends. Metadata-only token/read-limit debt can detach its worker interest after genuine native/return closure while retaining its own source subset. Join outside gates and never from the worker. The finalizer/abandoned-worker mechanism is **not derived**, as specified below; the precreated callback is provision for a candidate handoff, not an established handoff.

### Ordinary native-error mapping and fault owner

The port replaces ownership/I/O, **not the ordinary FileStream failure channel**. The committed `AssetStoreOriginalOwnershipTests.MissingFileConstructorPrefixRetainsItsOriginalCreateIssuer` requires FileNotFoundException and the original issuer; `UnreturnedCandidateFilePrefixKeepsOriginalDebtAndBlocksAnotherReplacement` also asserts primary-object identity through later disposal. Preserve these type/issuer/identity assertions while rederiving source totals for the new backend. Do not change them to generic IOException, scalar bounded-v1 refusal or a BCL retry.

Capture each failing API's raw code immediately with `Marshal.GetLastPInvokeError()` through a `SetLastError = true` binding, before another native/managed call. Retain domain, phase/API, original code and one-shot disposition separately from the eventual exception. The **same installed OriginalOperation** owns mapping, primary/cleanup samples and source claims even if Create returns no Store. Its ordinary control driver constructs the canonical exception outside the package worker and owner gates, installs it once as primary, and preserves that instance through propagation/delivery. An already thrown managed binding/path/hash exception is retained and rethrown unchanged, not remapped. Exception constructors/EDI/response machinery are counted opaque interests; they do not erase F(g), holder slots or raw facts.

The Windows decision table follows pinned [SafeFileHandle.CreateFile][file-open-windows] and [Win32Marshal][win32-errors]:

| Captured Win32 failure | Ordinary exception/type rule |
|---|---|
| `ERROR_FILE_NOT_FOUND (2)` | `new FileNotFoundException(null, reportedPath)`; preserve FileName and its standard file-not-found HResult. A missing leaf in an existing directory must take this route. |
| `ERROR_PATH_NOT_FOUND (3)` | `new DirectoryNotFoundException(null, reportedPath)` with .NET 11 DirectoryPath, **except** CreateFile's exact-root-path case, which the reference remaps to access denied. Test root length on the ordinary reported path, not an added native prefix. |
| `ERROR_ACCESS_DENIED (5)`; exact-root case above | UnauthorizedAccessException, with its standard HResult. |
| `ERROR_FILENAME_EXCED_RANGE (206)` | PathTooLongException. |
| `ERROR_SHARING_VIOLATION (32)`, `ERROR_FILE_EXISTS (80)`, `ERROR_ALREADY_EXISTS (183)` | IOException with `HR(error)`, not a missing-file or admission-refusal result. |
| `ERROR_OPERATION_ABORTED (995)` | The reference mapper produces OperationCanceledException. Preserve native-abort cancellation semantics; the selected read-cancellation branch additionally carries the actual selected token. Do not attach a newly fabricated canceled token to an open/path failure. |
| Other nonzero codes, including invalid parameter/lock violation | IOException with `HR(error)`. `HR(e) = e` when its high 16 bits are nonzero; otherwise `unchecked(0x80070000 | e)`, exactly the reference rule. |

Use public BCL constructors with null/default messages for these type/HResult/path semantics, not private CoreLib entrypoints or a fresh engine formatter. [FileNotFoundException][file-not-found] and [DirectoryNotFoundException][directory-not-found] support the specified path properties; use the parameterless/default constructor when there is no associated path. Localized FileStream message text is not given a new byte-identical formatting contract here; any separately required engine-authored path/error message needs its own reviewed source bound, not the old 544. Preserve actual caught original exceptions in all cases.

Path processing keeps its reference phase semantics: embedded NUL/effectively empty input is ArgumentException at the ordinary opening phase, not a newly synchronous Create usage exception. A failing GetFullPathNameW is mapped as the reference does; zero result with zero error uses `ERROR_BAD_PATHNAME (161)`. GetLongPathNameW short-name expansion failure is **not automatically an opening failure**: retain the reference's shorter-existing-prefix search for file/path-not-found and its normalized-path result when expansion cannot be performed. Required-capacity returns are not failures. No phase reacquires a file through FileStream/RandomAccess to obtain an exception.

Linux follows pinned [SafeFileHandle.Open/Init][file-open-unix] and [Interop.GetExceptionForIoErrno][unix-io-errors]:

| Captured libc errno / phase | Ordinary exception/type rule |
|---|---|
| `ENOENT` on file open | FileNotFoundException with the reported path **when its parent directory exists**; otherwise DirectoryNotFoundException with DirectoryPath. |
| `ENOTDIR` | DirectoryNotFoundException with DirectoryPath. |
| `EACCES`, `EPERM`, `EBADF` | UnauthorizedAccessException with a distinct inner IOException carrying the raw errno, as in the reference mapper. |
| `EISDIR` on open, or same-descriptor fstat identifies a directory | The reference maps this to access denied, including the inner IOException with the platform EACCES value. Keep the original errno/type fact separately; install/close any already returned descriptor once. Do not report malformed package bytes instead. |
| `ENAMETOOLONG` | PathTooLongException. |
| `EAGAIN/EWOULDBLOCK`, `EEXIST` | IOException whose HResult retains the raw errno. |
| `ECANCELED` | OperationCanceledException; selected caller cancellation uses the actual selected token, not an inferred token source. |
| `EFBIG` | ArgumentOutOfRangeException with parameter `"value"`, as in the reference, within the existing async execution channel. |
| Other errno values | IOException retaining raw errno. Retry EINTR only at the selected open/read helper's actual-return boundary; do not route the one-shot close EINTR disposition back into that retry loop. |

These are **libc errno symbols**, not the private PAL integers in [Interop.Error][unix-error-domain]. A direct libc binding must obtain their actual admitted Linux ABI values from its reviewed headers; casting a PAL enum to raw errno is invalid. Linux ENOENT alone cannot distinguish a missing leaf from a missing parent: reproduce the reference's `TrimEndingDirectorySeparator`/parent-directory-existence decision with a prepaid private stat/fstatat predicate over the prepared parent path. No BCL Directory.Exists, owning secondary file open or private System.Native dependency is selected. A parent-query failure is classification context, not replacement of the saved primary errno. Without its path-storage, predicate/error behavior and ABI closure, the Linux row remains unavailable. This table grants no Linux admission.

An actual native/path/read/hash execution failure is primary. Cancel-request, file/event release, hash disposal and open-prefix restoration failures are cleanup samples and never replace an existing primary. A failed wait is an execution/observation failure; preserve its first-primary ordering and any later independent completion/cleanup facts. Construct a cleanup exception only in its owning cleanup control; do not promote it into a new primary merely because mapping ran later. With no earlier primary, a genuine cleanup failure faults the ordinary operation under its existing failure channel. A selected cancellation becomes terminal only after actual native consumption/cleanup; native failure is not discarded because the token is also canceled. Unproved consumption remains nonterminal and funded. Chapter 11's still-unaccepted exact bounded delivery definition remains a gate; do not introduce an AggregateException, replacement error or truncated original sample to bypass it.

### Windows operation and cancellation/event closure

The exact existing binding shapes are the private `AssetBoundedNativePort.WindowsNative` declarations at lines 1055–1106. They provide CloseHandle, CreateEventW, CreateFileW, GetFileType, GetOverlappedResult, GetStandardInfo (`GetFileInformationByHandleEx`), ReadFile and ResetEvent. **They provide neither WaitForSingleObject nor CancelIoEx.** `ReadOneCore` currently uses `GetOverlappedResult(wait: 1)`; that unbounded blocking call is not the selected 50-ms cancellation protocol. The ordinary port needs its own reviewed pointer bindings to the persistent native OVERLAPPED, not an invocation of the bounded helper.

All selected imports use `kernel32.dll`, System32-only search, blittable HANDLE/pointer/DWORD and 32-bit BOOL representations, and SetLastError where failure uses the cached error. Prepare imports before acquisition under their counted prefix interests.

| Operation / ABI shape | Issuance/use/ownership rule |
|---|---|
| [`GetFullPathNameW(char*, uint, char*, char**)`][full-path]; [`GetLongPathNameW(char*, char*, uint)`][long-path] | Acquisition-free size/fill/expansion calls over prepaid path capacities, with null file-part output where unneeded. Follow the separate length/error rules and preserve ordinary normalization. They acquire no engine-owned package handle. |
| [`SetThreadErrorMode(uint, out uint)`][thread-error-mode] | Match the pinned [DisableMediaInsertionPrompt][media-prompt] scope: attempt SEM_FAILCRITICALERRORS before CreateFile, treat failure to enter as the reference's best-effort/nonfatal result, and restore the saved mode once only when entry succeeded. Retain a restoration failure as proposed cleanup, never replacement of a saved open failure; the reference itself ignores that restoration result, so this extra cleanup observation is not asserted to be existing FileStream behavior. Old mode is an initialized synchronous local, not a new retained source owner. |
| `CreateFileW(char*, uint, uint, nint, uint, uint, nint)` → nint | One read-only, FileShare.Read, OPEN_EXISTING attempt per generation; overlapped/random-access and the reference's security-QoS flags. Invalid file is −1. Immediately install a successful HANDLE before type/length checks; do not use a SafeHandle-returning constructor. |
| `GetFileType(nint)` → uint; `GetFileInformationByHandleEx(nint, FileStandardInfo, void*, uint)` → int BOOL | Same-handle type/length checks, with fully initialized status storage. Distinguish FILE_TYPE_UNKNOWN/no-error from a real failed query. No second file owner. |
| `CreateEventW(nint, int, int, nint)` → nint | One unnamed manual-reset, initially nonsignaled event per worker, null security/name. Null is acquisition failure. Immediately install it; worker owns its one final CloseHandle attempt. |
| `ResetEvent(nint)` → int BOOL | Reset before each native issue only after prior actual use/return ends. Zero is a pre-issue failure; issue no read against an unreset/uncertain event. |
| `ReadFile(nint, void*, uint, uint*, nint)` → int BOOL | One native issue at a time, at most 65,536 bytes into scratch, exact checked offset in the native OVERLAPPED. TRUE is immediate completion; FALSE/997 is accepted pending work; another FALSE code is a nonaccepted failure or the defined EOF case. Do not copy incidental count/error bytes. |
| [`WaitForSingleObject(nint, uint)`][wait-one] → uint | Non-alertable event wait in **50-ms** quanta. 0 is signaled, 258 is a nonterminal polling timeout, 0xFFFFFFFF is failure with cached error. WAIT_ABANDONED or another value is an invariant failure for this event, not success. Each wait retains the event/read interest through actual return; never close the event during a wait. |
| [`CancelIoEx(nint, nint)`][cancel-io] → int BOOL | On observed selected cancellation, issue **once for the exact accepted native-attempt sequence**, with the same file and non-null native OVERLAPPED pointer. Mark attempted before the call; no null-pointer cancel-all, close-to-cancel or second cancellation call. TRUE and FALSE/1168 (`ERROR_NOT_FOUND`) are not completion receipts. Another FALSE code is retained separately as cancellation/cleanup failure while read ownership continues. |
| [`GetOverlappedResult(nint, nint, out uint, int)`][overlapped-result] → int BOOL | Use `wait = 0` for the 50-ms protocol. TRUE obtains the actual count; FALSE/996 (`ERROR_IO_INCOMPLETE`) remains pending. A valid conclusive completed-abort 995 has no decoded bytes; the actual canceled read uses its selected token. Other conclusive completion failures use the ordinary mapper. A failed/uncertain observation is not conclusive completion. |
| `CloseHandle(nint)` → int BOOL | One separately elected final call for the generation file and one for the worker event, after their actual uses end. Capture each result/error before other cleanup. Nonzero establishes consumption; zero retains the recorded disposition/debt and never authorizes a second implicit close. |

The missing wait/cancel declarations can use these private signatures; this is declaration guidance, not source added or an executing backend:

```csharp
[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
[LibraryImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
private static partial uint WaitForSingleObject(nint handle, uint milliseconds);

[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
[LibraryImport("kernel32.dll", EntryPoint = "CancelIoEx", SetLastError = true)]
private static unsafe partial int CancelIoEx(nint file, nint overlapped);
```

After every timeout, recheck the selected token without a cancellation registration. After signal/immediate completion, obtain the count/status for the same issued OVERLAPPED and verify native use ended before copying, reset or reuse. `ERROR_HANDLE_EOF (38)` from an admitted regular-file read/completion means zero-byte completed EOF; the existing exact-extent owner then raises EndOfStreamException if bytes remain. That is not an open-error mapping or a successful short extent. Cancellation can lose to successful completion; keep existing higher cancellation/commit ordering rather than inventing rollback.

A failed wait, cancellation or completion observation leaves native completion to be proved independently. Keep the file, event, scratch, OVERLAPPED and original operation live; preserve auxiliary raw errors and stop the unproved branch. Do not replace the finite wait with `GetOverlappedResult(wait: 1)`, complete the public Task from a cancel result, or free/reset blocks on ERROR_NOT_FOUND. No imports allocate a new engine event, source node or buffer per poll/cancel. Their opaque native/provider/runtime call interests and actual enclosing returns remain distinct from source bytes.

### Strong execution-state root: unresolved finalizer/termination gate

`UnsafeStart(state)` with `state = OriginalPackageIo` keeps that object strongly reachable through the executing loop. It also holds its Thread/control and native scratch/event ownership. Clearing public Store/backing references prevents those public roots; **it does not make the worker's own native-owner finalizer eligible while parked**. The current strong-state catalogue does not derive a weak-state/root-transfer protocol, abandoned-start trigger, last-generation finalizer-to-stop path or actual independent termination handoff. The precreated retirement callback and live-generation count alone prove none of those.

Leave this closure **unresolved**. Compile and inspect the source candidate, but do not materialize its executing/native-owning worker, construct/start its Thread or acquire its native blocks/event/files until a concrete non-permanent lifecycle is derived. That later derivation must name who detects abandonment with no public Store left, who strongly owns native resources through queued/active/returning use, the nonblocking finalizer transfer, who requests stop and observes actual worker/retirement-caller return, and partial-start/shutdown-versus-handoff races. Preserve one worker/no restart; price every new state carrier, weak holder or termination control before construction.

Do not add a process-global permanent worker root, rely on GC/IsAlive/an exit flag to attest termination, or claim that a self-rooted worker can schedule its own finalizer to break the root. Existing explicit Store shutdown is not abandonment/finalizer closure. Until the missing mechanism is supplied, no target—including the source-layout RC lane—has materialization or native activation authority for this worker.

## 6. Ordered implementation/test boundary

**One coherent B boundary comprises source admission, the complete private worker/port/request/path implementation, canonical opening integration and reserved final retirement.** It is not the entire M03-B/M05 program.

| Order | Work and acceptance boundary |
|---|---|
| 1 | Reconcile A's service lifetime and original membership before worker construction. Preserve the first-paying anchor, nonwrapping IDs, failed prefixes, held generations and smaller backing limits. Derive the final changed carriers, including any shared backing effects on the separate bounded-v1 preclaim; do not leave that materialization using an invalid old shell price. |
| 2 | Implement and compile the private declaration/binding/control candidate without native-owning worker materialization, Thread construction/start or native acquisition. Reconcile raw auxiliary errors, reported-path ownership and all final fields/storage with this catalogue; production uses closed expressions, not reflection or GC counters. The weak-state/root-transfer/finalizer-to-termination mechanism remains unresolved and must be derived before that materialization/activation gate opens. |
| 3 | Integrate hash/header/profile/directory/padding/open failure and close through the same native generation. Reuse canonical validators and exact-capacity entry adoption; preserve checksum-before-layout-error precedence. Install actual buffer grants and controls before every reached acquisition/reference. Do not select a generation while any public/anchored/inspection/None/Brotli route can fall through to FileStream, RandomAccess or an uncharged buffer/control. Remaining typed-read/codec/receiver provision is its own subsequent boundary, not inferred from this lower price. |
| 4 | Extend existing Assets ownership/allocation/entry/reader/Store/package-lease tests as one batch: real-issuer exact fit/one byte short, path growth/reported-name peak, missing leaf versus parent, access/sharing/path failures and exact exception type/HResult/path/primary identity, held old generations and enlarged replacement options, ready-but-unconsumed/returning requests, independent close provision, late lease close after Store disposal, wait/cancel races and auxiliary failure precedence, checksum/layout ordering and unchanged None/Brotli/anchored behavior. Preserve the committed missing-file issuer/type assertion. Use existing literal fixtures. Managed mapping/delayed-consumer tests establish only exercised managed behavior; they cannot open the unresolved materialization/native-return gate. |
| 5 | Only after lower acquisition/actual-use/return gates are closed, execute the real selected native implementation on its qualified target for open/range/hash/cancellation/repeated-close/worker-retirement cases. Otherwise retain compilation/source-only evidence and stop before native activation. Missing safe native failure/partial-start/return stimuli remain named observation gaps, never fabricated handles, disabled tests or success-shaped fallbacks. |

At that coherent boundary use [Building](../project/Building.md), the SDK selected by `global.json`, current analyzers/central packages and ordinary NUnit/VSTest:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetStoreAllocationTests|FullyQualifiedName~AssetStoreOriginalOwnershipTests|FullyQualifiedName~AssetStoreEntryBoundaryTests|FullyQualifiedName~AssetPackageReaderTests|FullyQualifiedName~AssetStoreTests|FullyQualifiedName~AssetPackageLeaseTests"
```

Run the same selection in Release after Debug. Confirm the added cases actually execute; retain fresh TRX/exits under `artifacts\tst\<Configuration>`. Identify which cases reach real native code and which remain unexecuted. Do not run broad Runtime/pixel/consumer filters merely to hit closed guards.

## 7. Target profiles and exact stop conditions

The user reports that a broad SceneAssetStore Runtime selection included 224 tests behind closed gates; its PATH-launched public consumer children ran stable `.NET 11.0.0`, while the Assets testhost ran `.NET 11.0.0-rc.3.26522.148`. The child failure at `PreclaimOriginalOpening` before acquisition is the correct profile refusal. **It is not a consumer observation/pass, native observation or qualification run.** It is not repeated here.

The launch boundary is present in `SceneAssetStoreTests.cs`, `SceneAssetStoreTests.D3D12Aot.cs` and `RenderPublicConsumerTests.cs`: managed children use `ProcessStartInfo("dotnet")`. `global.json` selects a developer SDK; it does not make every PATH child load the testhost runtime.

| Target/gate | Current disposition and retry condition |
|---|---|
| Windows x64 CoreCLR RC reference lane | A's source-layout lane only. Local CoreLib hash is `df53885902e896a2b40988237cbe9cbb9f1bb0743200f05fe9257c56a29c5732`. B's final declarations, ordinary-error parity and native/start/return/finalizer implementation are still unqualified. The strong execution-state root leaves worker materialization/start closed; derive its non-permanent lifecycle before any native observations or admission row. A framework-description string alone does not attest the binary or execution mode. |
| Stable `.NET 11.0.0` PATH children | **Unavailable; no stable-runtime admission is derived here.** Obtain the actual stable CoreLib/runtime/source identity, verify final headers/strides/completion-core width, generated/interop paths and thread/context/return ownership, then derive and review an explicit row and execute its applicable observations. Do not broaden the version predicate, change roll-forward to disguise the mismatch, or fall back to BCL I/O. Serviced GA qualification remains Chapter 35's separate obligation. |
| Linux x64/arm64, Windows arm64, NativeAOT, Runtime Async | No admission from the Windows-x64 RC table. Require each applicable managed/native ABI, compiler/generated-root and actual-return profile. NativeAOT is unavailable even if it reports the same framework-description text. |
| Unclosed source member/path/array/block | Stop before that materialization. Claim its exact capacity against the original B0 or refuse. Neither an opaque-resource exclusion nor remaining default headroom is a price. |
| Unclosed ordinary-error or wait/cancel branch | Complete the stage/domain mapping, reported-path grant, Linux parent predicate and real pointer imports before selecting the route. Do not map all errors to IOException, change native abort into success, suppress cleanup failure, or reacquire via BCL I/O to obtain an exception. |
| Unclosed construction/start/native use | Stop before native activation. An unreturned constructor/start prefix, uncertain completion, or unsafe handoff retains original ownership/debt; no timeout, exit flag or task readiness ends it. |
| Unclosed caller/thread/finalizer retirement | Native-owning worker materialization, Thread construction/start and native acquisition remain closed while the strong start-state finalizer/termination mechanism is unresolved. Price and derive a non-permanent root/transfer/termination path and its actual enclosing returns. Keep any already owned claims/resources live; normal-success permanent rooting or omission of the public Store reference is not closure. |
| Required native branch lacks a safe real stimulus | Record Chapter 35 sections 2.3/4, the exact unexecuted acquisition/close/cancellation/partial-start/return branch, missing capability/stimulus and retry condition in the owning run evidence. Do not repeat an unchanged blocked probe or replace it with synthetic native success. |
| Higher ordinary typed-read and Scene consumers | Their control/buffer/codec/context/receiver/planner/publication grants and actual returns remain separate gates. Source fit, a native file receipt or a successful Assets test cannot clear the Scene supplier latch/footprint or authorize CLI, nonempty restore, native diagnostics, rendering or public/AOT acceptance. |

**Resolved:** a finite, separately visible B0 source expression for the enumerated strong-state worker/open/read/close candidate, including reported paths and independent wait/cancel errors, with transfer of only the existing native extents. **Still unresolved:** final implemented membership, the non-permanent finalizer/termination lifecycle, complete target-specific native/caller-return closure and higher real-consumer qualification. Continue compile-only/source reasoning under tentative guidance; no native-owning worker materialization or activation is authorized.

Amendment scope: the original section 8 input membership/freeze is retained verbatim and remains the same 61-input workspace set. The pre-amendment proposal SHA-256 is `18fb1e1cf692ad637c408cbec6f416f934cc5ab6dc4adb00d2f7fc890148ef49`. The added evidence is only the linked Win32/Unix error, file-handle, path-exception and media-prompt source at the existing pinned reference revision, plus the canonical wait/cancel/completion/path/mode API references below. No source/test/design/plan inputs or protected dirty bytes are changed; these proposed contract details are not accepted merely by amending this document.

## 8. Scoped input freeze and preservation

Freeze: HEAD above; exact 61-input union hash **`618fd3954f64ccedfba921f4603642f95edef03f1a73d9835b4682f7d506cbc5`**. Set hashes use ordinal-sorted `relative-Windows-path<TAB>lowercase raw SHA-256`, LF-joined, UTF-8 without BOM/trailing newline. External reference sources are pinned by the revision in section 3. Recheck the input bytes before applying a later disposition; changed bytes require scoped reassessment.

| Set | Exact membership | SHA-256 |
|---|---|---|
| A, 8 | Project Roadmap/Building; design 02/04/11/29/30/35 | `c6064513387eb52fe3638d13db30563f3ce224566041a580a36d734d65ee83e3` |
| P, 6 | Proposals linked here: original B0, file-I/O, bounded-v1 Batch B, bootstrap/close, file-handle, actual-invoker | `7b50688f51066ad2a49020c8facc4eabb24e8798c2452477c2290198ffbe8123` |
| B, 10 | `.github\copilot-instructions.md`, `.github\workflows\ci.yml`, global.json, root props/targets/packages, sources/tests props, Assets/Assets.Tests projects | `f89740d2d09f8c2612e1cf04e78b0e45a154b14e88953346205c8da8dd90df11` |
| C, 25 | Assets: Store, Store.OriginalOwnership, PackageReader, PackageBacking, PackageEntry, StoreOptions, Store.PackageLease, Store.ReadDescriptor, PayloadReader, LeaseLifetime, PackageLease, OwnedReadDescriptor, BoundedNativePort, BoundedNativeFile, BoundedWindowsOverlapped, BoundedNativeApi; Core: FoundationCodec, FoundationProfileBinding, FoundationCodecContext, EbinCodec, EbinField, EbinRecord, EbinReadLimits; Runtime Scenes: PreparedScene, PreparedScene.Preparation. Each Assets basename has the `Asset` prefix; all are `.cs`. | `a7685af093d8f7a71706b648ddfdeec322e60bd3fd4a7b35bceed7d9b9b626b1` |
| T, 5 | Assets tests: AssetStoreOriginalOwnershipTests, AssetStoreAllocationTests; Runtime Scenes tests: SceneAssetStoreTests, SceneAssetStoreTests.D3D12Aot, RenderPublicConsumerTests (`.cs`) | `edf6a77aa6f371b35e3dfa21931e526fef0c53ebf1fb09adb322d2c2758d9a0b` |
| S, 2 | NativeAssetSchemaCatalogue / SceneSchemaCatalogue | `e2b69ad23a59f5ad02e1d25f1714f9c90b2595f20b797bd5a6a19eec8f309f1f` |
| D, 5 | Protected dirty Runtime Scenes tests: PreparedSceneAssetRetentionTests, RenderSpriteCapacityTests, RenderSpriteComponentTests, RendererExtractionTests, SceneAssetStoreTests.EngineHost (`.cs`) | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |

Schema raw hashes remain `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` (Assets) and `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` (Scene). Only this proposal is created. The protected dirty files and all source/tests/design chapters/other proposals are preserved. Declaration arithmetic and input hashes were checked; no build, test, native/AOT probe, plan artifact, commit or remote action is performed for this document.

[completion-core]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs
[thread-start]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.cs
[thread-core]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Threading/Thread.CoreCLR.cs
[thread-unix]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Unix.cs
[thread-windows]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Thread.Windows.cs
[overlapped]: https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-overlapped
[full-path]: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfullpathnamew
[path-helper]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/PathHelper.Windows.cs
[path-internal]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/Common/src/System/IO/PathInternal.Windows.cs
[win32-errors]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/Common/src/System/IO/Win32Marshal.cs
[file-open-windows]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Windows.cs
[file-open-unix]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs
[unix-io-errors]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/Common/src/Interop/Unix/Interop.IOErrors.cs
[unix-error-domain]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/Common/src/Interop/Unix/Interop.Errors.cs
[file-not-found]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/FileNotFoundException.cs
[directory-not-found]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/DirectoryNotFoundException.cs
[media-prompt]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/DisableMediaInsertionPrompt.cs
[thread-error-mode]: https://learn.microsoft.com/en-us/windows/win32/api/errhandlingapi/nf-errhandlingapi-setthreaderrormode
[wait-one]: https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-waitforsingleobject
[cancel-io]: https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex
[overlapped-result]: https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-getoverlappedresult
[long-path]: https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getlongpathnamew
