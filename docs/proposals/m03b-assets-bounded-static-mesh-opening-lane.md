# M03-B Assets: select a bounded static-Mesh package lane

**Status: tentative implementation guidance under the user's blocker-proposal policy, not an accepted design contract. No complete allocation profile, implemented native worker, or Scene capability is established. Keep `PreparedScene.QualifiedFootprint` null.**

## 1. Decision

Implement a **separate, internal, synchronous, closed-input Assets lane**, first consumed by the existing `cook-static-mesh` production command through a new explicit option:

```text
--package-validation bounded-v1
```

The lane opens the command's actual EPKG2 output and supports its actual Mesh/Material typed validation. It uses exactly two factory-created codecs, default cancellation only, no ambient-context capture, value-only failure receipts, and the original Store's CPU envelope. It is not a qualification of ordinary `AssetStore.CreateAsync` or `LoadAsync`.

Select these dispositions now:

| Blocker | Selected rectification |
|---|---|
| Arbitrary registration/codec graphs | Do not admit them. A whole-registration seal covers exactly the two codecs and the complete fixed metadata set in section 4. No unused application codec or opaque application origin is permitted. |
| Caller token/source/context | Admit only a source-free token; never capture the caller's execution or synchronization context. Reject a sourced token or unsupported control-thread context before worker/file acquisition. The CLI's cancellation boundary is explicit below. |
| Original and cleanup exceptions | The new lane returns fixed scalar facts, **not original exceptions**. It never exposes an exception that a retained engine alias still references. This is an explicit new error contract, not an implementation-equivalent replacement for the existing public Assets contract. |
| Bootstrap/opening | Extend the existing unpublished `AssetStore` issuer and `AssetPackageBacking` opening carrier; preclaim their complete provision within the original `CpuAssetBudgetBytes` before acquisition. |
| Native I/O | Retain the selected one-worker-per-original-Store mechanism, with direct Windows overlapped file ownership/Linux `pread` and one-shot native close receipts. Ordinary bounded-lane operations use one nonwaiting Store slot. |
| Final return/unwind | A preclaimed, sealed delivery owner transfers the terminal return partition to the actual synchronous recipient. That recipient observes driver return before ending delivery. No ready Task, self-written latch, or observer Task attests actual return. |
| Late backing/lease lifetime | The original issuer, registration, worker and charges survive logical Store close through every exact retained generation/handle. Last ordinary release performs the required external worker join; failed/uncertain retirement keeps its original debt. |

This is a useful first lane because the producer and consumer already exist. It requires neither a Scene, World, Renderer instance, native graphics device nor fabricated asset producer. `RenderMeshAsset` and `RenderMaterialAsset` are currently **internal** Runtime types; do not promote them or invent a public typed-Mesh API to implement this proposal. Content.Tools already has the permitted Runtime friend relationship for these owner codecs.

The [retained-input assessment](m03b-assets-retained-input-budget-closure.md) correctly establishes that the arbitrary-input/original-error contract has no finite same-envelope closure. This proposal selects the narrower behavior instead of asking the same three ownership questions again. It adopts the early original issuer from the [bootstrap proposal](m03b-assets-prelease-bootstrap-and-close-closure.md) and the per-Store native port from the [file-I/O proposal](m03b-assets-file-io-port-implementation-closure.md), but **does not select that proposal's general None/Brotli or arbitrary public-call execution scope**.

## 2. Current evidence and why the general route cannot be the bounded lane

The snapshot in section 10 includes the current bytes, not the older proposals' review hashes.

| Verified source | Consequence |
|---|---|
| `AssetPackageReader.OpenAsync` | Validates options/pre-cancellation and refuses zero CPU budget **before** constructing `FileStream`. It then hashes, constructs bounded header/profile/directory state and entries, and constructs the backing. Its catch awaits stream disposal before rethrowing; cleanup can replace the primary. |
| `AssetPackageBacking` | After `7e76914c`, retains the digest, registration and effective budget scalars, **not** `AssetStoreOptions` or its path. This removes one retention edge, not the registration's codec graph. |
| `AssetStoreRegistrationBuilder` / `AssetStoreRegistration` | Public adapters retain exact caller codec objects; both immutable maps and the exact schema registry remain reachable. Membership immutability does not constrain codec state or mutation. |
| `AssetOwnedReadDescriptor`, package/asset leases and their lifetimes | Retain the exact registration/backing or Store. A descriptor's lack of a package-reference credit does not make its managed references weak. Issued leases deliberately survive replacement and Store disposal. |
| `AssetStore` / backing / lease release | Exception fields, the cached disposal completion and the failed-release root retain originals. These are not bounded scalar outcome storage. |
| `StaticMeshCookCommand.ValidatePackageAsync` | Writes the real cooked package, constructs the canonical registry, registers Rendering codecs, opens a real Store and loads `RenderMeshAsset` and `RenderMaterialAsset` before publication. It checks one material slot, one group, no Skeleton, PBR rather than Sprite, and no texture bindings. |
| `ContentPackageWriter.CreatePackageCore` | Produces canonical EPKG2 with None payloads: initialized directory entries leave the codec byte zero and stored/decoded envelope lengths equal. |
| `Renderer.RegisterAssetCodecs` | Currently registers **four** codecs: Mesh, Texture, Atlas and Material. It is not the selected two-codec registration and must not silently acquire a whole-registration seal. |
| `RenderingSchemaCatalogue` | Initializing its base `Schemas` also initializes the nine-entry `M05Schemas` static companion. The latter is not registered in this lane, but its actual cold/static storage cannot be omitted from funding. |
| `PreparedScene.QualifiedFootprint` | Still returns null. Neither a bounded static package validator nor commit `607a4e3d`'s standalone LOD kernel changes that gate. |

For any proposed finite allowance `F`, a legal current registration can contain an unused codec retaining an application graph larger than `F`, with identical package bytes, schema tuples and budgets. The backing retains that registration. A caller-owned cancellation source and a custom codec's original exception can independently retain arbitrarily large, mutable graphs. After original-error delivery, mutation of `Exception.Data` can also enlarge an engine-cached original. A fixed number of roots/exception slots does not bound any of these graphs.

Consequently, do not retrofit this selection into existing public calls by pruning registrations, weakening references, substituting tokens, suppressing context flow, summarizing original exceptions or falling back after a failed preclaim. Those change admitted behavior. The ordinary route remains general and **unqualified**, with its existing signatures, identities, token/error channels, None/Brotli support and replacement behavior.

## 3. Observable opt-in contract

Add the option only when the implementation and applicable profile are ready. Omission, or `--package-validation standard`, retains the existing command path. `bounded-v1` selects the new lane without fallback. Other values and duplicate options follow the command's existing argument-error handling. No public .NET Assets declaration is added by this selection.

| Condition in `bounded-v1` | Required observation |
|---|---|
| Useful profile, valid produced package and successful complete retirement | Existing success line and exit `0`; publish the same package bytes through `ContentPackageWriter.WriteAtomically`. |
| Unavailable target profile, unsupported closed input/context, insufficient original budget or busy bounded slot | Exit `1`, one `Content.Tools: bounded package validation refused (<reason>)` line, and no output replacement. Refusal is not success and does not retry through FileStream. |
| Digest/layout/profile/typed/reference/I/O failure, or independent cleanup/retirement failure | Exit `1`; print the scalar primary and every nonempty direct cleanup field in the fixed order in section 5. Preserve the previous output. Do not print an original exception's message, stack, `Data`, or inner exceptions. |
| CLI cancellation before entering bounded validation | Existing canceled-command channel, exit `130`; no Assets worker/package acquisition for this validation. |
| CLI cancellation during bounded validation | The interval is noncancelable. Drain actual work and retirement first. If validation/cleanup failed, exit `1`; otherwise recheck the **original command token** before atomic publication and use exit `130` if canceled. Never report cancellation over a cleanup failure. |
| Cancellation after atomic publication | Does not undo publication or claim rollback. Preserve the existing publication boundary. |

`Program.Main` currently creates a sourced CTS and passes its token to the command. **It cannot simply pass that token into the bounded lane.** In the new mode, the command explicitly checks that original token at the two documented boundaries and invokes the synchronous Assets interval with `CancellationToken.None`. This is the opt-in CLI's new cancellation behavior, not a purported identity-preserving token proxy. Standard mode and public Assets cancellation remain unchanged.

The command's source reading, adapter cooking, staging-file creation/deletion, console output and final atomic publication remain Content.Tools work outside the claimed Assets interval. Its original Task/CTS/arguments and source/package arrays are not stored in the lower issuer, worker, registration, delivery object or receipts. This proposal does **not** qualify the command's total memory use or its staging/publication cleanup. A staging/publication failure remains a command failure; Assets success is not whole-command success.

The smallest owner obligations are therefore concrete:

1. Runtime constructs the exact sealed family and performs the existing static-Mesh binding checks.
2. The Content.Tools caller performs the two cancellation checks, receives the synchronous delivery owner, and ends delivery after actual return on every reached path.
3. The lower issuer pays all retained Assets inputs, codec work, controls and cleanup from its original envelope. No external graph grant, application callback or caller-supplied size estimate is accepted.
4. The caller interprets scalar failure/refusal explicitly and never publishes on unresolved retirement.

The new internal value-outcome protocol needs an explicit exception to Chapter 02's original-exception/cleanup-sample delivery rule for this opt-in boundary. That is the selected contract amendment, not an assertion that the current rule already allows it. Later owner documentation belongs in Chapters 02/04/11 and the Content.Tools guide; Chapter 30's ordinary public Assets declarations do not change. This task changes none of them.

## 4. Exact closed membership and later typed loads

### Registration

Use a new private Runtime factory, not the public builder supplied by an application. It runs **after** the acquisition-free original-Store preclaim and constructs:

| Member | Exact membership |
|---|---|
| Typed adapters | One `RenderMeshAssetCodec` for `RenderMeshAsset`, `native/mesh` **1.0**, and one `RenderMaterialAssetCodec` for `RenderMaterialAsset`, `native/material` **1.0**. Both actual codec instances come from that factory and have no application-supplied instance state. Include their real nested actions, diagnostics and transitive static state in the profile. |
| Schema registry | Exactly the canonical union used by the current static cook validator: `CoreContentSchemaCatalogue.PrimitiveSchemas` (25), `.FoundationSchemas` (8), `NativeAssetSchemaCatalogue.Schemas` (4), `SceneSchemaCatalogue.Schemas` (18), and `RenderingSchemaCatalogue.Schemas` (26): **81 exact tuples**, with empty profile compositions. Preserve canonical descriptor references and the resulting registry identity. |
| Codecs not present | Texture, Atlas, Scene, Prefab, Skeleton, animation, Mesh/Material 2.0, particles and every application codec. Metadata presence is not executable codec registration. |
| Origin/seal | A value-only, versioned internal family identity, bound once to the exact original issuer, registry and the two exact adapters through the factory's internal construction path. No caller `object` origin, delegate, closure, Type-name/reflection match, or public builder action can issue it. |
| Mutation/sharing | No builder escapes. The registration remains unchanged across that issuer's generations. A replacement must use that exact registration, not a newly supplied or schema-equivalent one. |

The count above is independently derived from the catalogue declarations: 25 + 8 + 4 + 18 + 26. It is **not** a byte charge. In particular, include the nine separately initialized Rendering M05 descriptors and underlying Foundation/static construction state in cold/shared accounting even though they are not members of the 81-entry registry.

Assets stores the ordinary typed adapters and lower-owned immutable profile facts. Runtime supplies reviewed, closed-code allocation bounds as data during construction; Assets performs no upward sizing/funding callback and references no Runtime/Graphics type. This is the existing permitted direction of higher-owner codec registration, not a Runtime service registered below itself. Do not introduce a general graph-size interface or executable profile expression language.

An unsealed registration, an ordinary four-codec Rendering registration, an additional **unused** codec, a supplied origin object, a different registry, or a reconstructed registration loses admission. Refuse before worker startup or package acquisition. Unlike the earlier per-read Scene proposal, unrelated unused codecs are **not** allowed in this whole-retained-input family.

### Token, context, package and options

- Token membership is exactly `CanBeCanceled == false`, equivalent here to a source-free token. A sourced token, including an already-canceled or derived source, returns `UnsupportedToken` before acquisition; it is not retained or replaced inside Assets. There are no port token registrations, linked sources or canceled Tasks.
- The new synchronous control entry requires no current `SynchronizationContext`; on Windows use an ordinary MTA control thread, not an STA/pumping join. Unsupported control context refuses before acquisition. The static package worker starts with `Thread.UnsafeStart(object?)`, whose documented contract does not flow the caller's `ExecutionContext`. Neither request nor completion contains an execution context, continuation or caller callback. This is a new-lane policy, not permission to change public async flow.
- The expected digest is exactly 32 copied bytes. Copy only the needed path/digest/scalars/IDs into installed provision; never retain an incoming options object. The first path profile accepts portable ASCII components matching `[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*`. On Windows require an ASCII drive letter, colon, backslash, and one or more such backslash-separated components, with at most 259 UTF-16 code units excluding the terminator. Reject a component whose part before its first dot is `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9` or `LPT1`–`LPT9`, case-insensitively. On Linux require a leading POSIX root separator and one or more such components, with at most 4,095 bytes excluding the terminator. Empty/trailing components, dot/dot-dot traversal, NUL, spaces/non-ASCII, UNC/extended/device paths and alternate streams are outside this first profile. Charge the actual copied/native path capacity before allocation. These are explicit opt-in restrictions, not changes to normal FileStream path acceptance; selected paths remain trusted package selections. Same-handle regular-file/type checks still follow acquisition.
- The package contains exactly the selected two distinct positive AssetIds, with None payloads. Validate this restriction from the checked directory after whole-file checksum validation. Content-dependent refusal necessarily follows acquisition and must complete its cleanup; do not claim that directory membership was known before opening.
- Typed admission selects the same single-group, single-material, unskinned, textureless PBR shape already checked by `StaticMeshCookCommand`. A valid other EPKG2 package can be unsupported by this lane. These are family restrictions, not changes to EPKG2 or ordinary Assets.
- Keep existing option defaults and hard ceilings: CPU `536,870,912` / `8,589,934,592` bytes; decoded asset and concurrent decoding `268,435,456` bytes; live leases `4,096` / `65,536`. Validate nonnegativity and the hard ceilings in this lane; current `AssetStoreOptions.Validate` alone checks only nonnegativity. Preserve the 8-GiB file, 131,072-entry and 4,194,368-byte format ceilings; the selected two-entry subset does not change them.

Use one nonwaiting ordinary operation slot per bounded issuer, including reference inspections, ready/consuming states and actual return. Busy refuses before new acquisition. This intentional new-lane restriction is not a new public quota and does not change general Assets concurrency. Final-close nodes already belong to retained backing/handle provision; ordinary saturation cannot prevent retirement.

### Typed behavior and lifetime

Internal bounded read/lease entrypoints support **both** registered types, with the exact adapters above, not a one-off decoder that discards registration after the first Mesh read. Each load preserves canonical envelope validation, full-record consumption, same-content-set reference ID/schema/major checks, exact context registry/read limits and package profile/settings. Apply the static-family shape checks before publishing a value. Include unsupported/malformed paths in the bound, including work done before a shape refusal.

Do not expose a bounded Store or handle through the ordinary Task/throwing-disposal entrypoints. Use internal receipt-returning handles over the real backing/lifetime controls; the public `AssetLease<T>` exception caches are not the bounded outcome store. No synthetic lease, snapshot, grant or return acknowledgment is introduced.

Later loads of either registered type remain legal while admission is open. An anchored load uses the exact original backing/registration after replacement; it never consults the new package for IDs. `Retain` shares the decoded value without another wire-byte charge and consumes another ordinary live-handle credit. Store close refuses new loads/retains but does not revoke issued values. An escaping descriptor is not admitted in this first lane: descriptors are synchronous borrows within its installed operation, not uncharged independent roots. General descriptors and public typed loading retain their existing behavior outside it.

## 5. Bounded failure representation

Use these proposed **internal** data shapes; none contains a managed reference:

```text
Status       = Succeeded | Refused | Failed
Refusal      = None | UnsupportedRegistration | UnsupportedToken |
               UnsupportedContext | ProfileUnavailable | InvalidOptions |
               Capacity | Busy | Closed
FailureKind  = None | Native | Digest | Layout | Profile | TypedRecord |
               Reference | UnsupportedContent | ManagedExecution | Invariant
Disposition  = NotAttempted | NotFinal | Consumed | ConsumedWithError |
               KnownRetained | Indeterminate

FailureFact  = { stage: fixed enum, kind: FailureKind,
                 nativeApi: fixed enum, nativeError: int,
                 schemaId: Guid, fieldTag: uint,
                 fileDisposition: Disposition }
Reply        = { status, refusal, published: bool,
                 ownerReleased: bool, consumersRemain: bool,
                 retainedGenerationDebtCount: int,
                 primary: FailureFact,
                 candidateFile: FailureFact, displacedOrFinalFile: FailureFact,
                 hashProvider: FailureFact, ioEvent: FailureFact,
                 requestEvent: FailureFact, replyEvent: FailureFact,
                 workerJoin: FailureFact }
```

Initialize every field; zero/None means that site produced no failure. Stage/API enums name only the compiled bootstrap/open/hash/header/profile/directory/read/decode/reference/publication/release/join sites. They do not carry strings or runtime Type objects.

The **seven direct cleanup fields** are derived from this mechanism: at most two generation-file dispositions directly handled by one opening/replacement/Store-close operation; one opening hash provider; three Windows events (I/O completion, request and reply); and one worker join. Native scratch is a single valid allocation with nonthrowing free after use ends, not another throwable cleanup sample. Linux leaves inapplicable event fields empty. Every generation separately retains its own one-shot file receipt. Older failed generations are not squeezed into two universal samples: their count and exact per-generation records remain charged; subsequent operations never retry them implicitly.

Ordering is primary execution failure first, then candidate file, displaced/final file, hash provider, I/O event, request event, reply event, worker join. Attempt independently safe cleanup siblings even after failure. Record consumption before later cleanup/accounting. A committed replacement remains `published=true` if old-package retirement fails; never report rollback. Repeated close returns the stored scalar disposition without another native attempt.

Known internal exceptions may exist transiently while BCL hashing, decoding or construction unwinds. Catch/map them inside the closed execution boundary, finish actual helper unwind, and discard every exception alias **before outcome delivery**. Do not call virtual `Message`, `Data`, `ToString`, capture EDI, construct aggregate wrappers, retain faulted Tasks, or save originals for diagnostics/repeated close. Unknown managed failures map to `ManagedExecution`; they are not success.

This removes post-delivery mutable-original retention. It does **not** declare transient exception construction free: qualify the actual selected runtime/provider throw and unwind envelope, including bootstrap and allocation failure. If its reachable graph cannot be bounded, the target remains unavailable. A host that exposes these internal faults to arbitrary mutating first-chance/profiler callbacks is not covered by the closed-program qualification; do not claim a general host guarantee from the Content.Tools executable.

Original codec/token/exception identity remains required on still-admitted ordinary public Assets calls. The bounded lane does not accept such a call and then deliver a different original. Its receipt contract is deliberately different and visible at the opt-in boundary.

## 6. Concrete ownership and construction order

### Original issuer and shared bootstrap

Use the **existing `AssetStore`**, extended to be constructible without an acquired package, as the sole original issuer. The bounded session is a facade over it, not another recovery authority. Extend the existing Assets intrusive root to opening, retained-release and quarantine states.

1. The acquisition-free prefix validates usage, token/context membership, scalar limits/IDs/path lengths and availability of the selected target profile. Its scalar preclaim names `B0`, the original CPU budget. No registration graph, worker, file or completion source is materialized to discover whether this claim fits.
2. Reserve complete cold/bootstrap, issuer, delivery, worker-start, candidate/opening and protected unwind provision. Construct/install the empty issuer/backing/delivery carriers before external acquisition. Every successful acquisition result is installed before the next fallible step.
3. Runtime constructs the closed registry/adapters under that provision and binds the seal once. Public/unsealed input checks do not invoke codec code. Assets never calls Runtime to obtain a grant.
4. Initialize worker controls/native paths, scratch and events; start the worker with a protected startup interest. Package acquisition cannot begin until the initiating ordinary caller has observed successful start return. Partial startup retains any possibly started worker and requires its real join.
5. Execute acquisition, hash and validation through the installed carrier. Failed opening returns no usable backing, but its original issuer/carrier already owns all remaining cleanup and debt.

Make the root's cold prefix explicit: zero-initialized primitive/root fields and an allocation-free admission gate precede materialization, rather than an uncharged implicit `new Lock()` initializer. The first successful bootstrap preclaim pays a **process-lifetime shared partition** for the actual anchor and canonical static catalogue/provider state. That partition is retained under the first original grant; it is not refunded at that Store's logical close. Later issuers borrow the same funded immutable state. Their own maps, registry arrays, adapters, workers and generations remain individually charged.

The shared partition cannot retain the first Store facade, paths, files, operation buffers or outcomes. A failed partial cold initialization retains its exact partial graph/provision and refuses further bounded initialization; it does not repeatedly charge/refund an incomplete singleton. This is attribution of real process-lifetime state, **not** a new process quota or permission to root normal successful operations forever. No process-wide aggregate bound for arbitrarily many independent Stores is claimed.

### Native worker and reader controls

- One original issuer owns one BCL Thread and its full runtime/native provision. Request a 1-MiB worker stack as an implementation choice; qualify actual reservation/commit/guards/TLS/attach/start/join costs, not the request as an all-in charge.
- The static worker entry owns a prepaid subordinate control, not a delegate rooted to the public Store or caller. While parked it has no strong Store-facade/backing/operation-buffer alias. Actual queued/active work retains its exact generation and issuer interests.
- The worker has one 65,536-byte native scratch range. Windows adds one initialized `OVERLAPPED` and one I/O event, plus request/reply signaling events. Linux uses the selected 64-bit libc ABI, `pread`, and owned scalar wake state. No ThreadPool/IOCP completion dispatcher or `Memory<T>` bridge is selected.
- Windows opens read-only/read-shared/overlapped/random-access, immediately installs the HANDLE, and queries that handle's type/length. Linux opens read-only/close-on-exec/nonblocking, immediately installs the descriptor, then checks the same descriptor is the selected regular-file shape. Preserve checked short-read accumulation and EOF failure. No running syscall/native read is abandoned because an observer stopped waiting.
- Hash the complete same file through this port using BCL incremental SHA-256 and bounded chunks. Reuse the current Foundation and EIDX validation rules and checksum-before-layout-error precedence. Do not add a cryptographic implementation or second BCL file owner.
- The request, retained typed byte-array range, phase controls, result slot and close node are already admitted. No per-waiter Task, callback, context, queue node or exception holder is allocated. The one ordinary slot includes inspection work; its capacity is not inferred from `MaximumLiveAssetLeases + 1`.

Before native use, distinguish Windows null event from `INVALID_HANDLE_VALUE`, and Linux `-1` from valid descriptor zero. After actual I/O quiescence, capture the native close result/error exactly once. Windows failure without a supported consumption proof remains retained/indeterminate. A reached Linux close on a valid owned descriptor can consume it with documented errors, including `EINTR`; never retry the number. `EBADF`, interposed/denied calls and unclassified outcomes are not successful release. Use the [selected native-disposition rules](m03b-assets-file-handle-release-acknowledgment.md#native-disposition-is-separate-from-cleanup-success), not SafeHandle `IsClosed` or normal managed Dispose as the receipt.

### Actual return, delivery and late release

The final return owner is a necessary part of the selection:

| Boundary | Actual owner/observer |
|---|---|
| Native helper/read/hash/decode → worker loop | The loop calls the ordinary helper and observes its return/unwind. Only then can that helper's temporary provision end. The loop clears old payload/exception aliases before readiness. Its remaining loop/wake tail is independently prepaid issuer worker provision. |
| Bounded request/result helper → Runtime validation driver | The synchronous driver consumes the exact result and observes ordinary return/unwind. A new read cannot reuse operation storage while consumption/access interests remain. Typed result storage transfers only to its real internal lease. |
| Runtime driver → Content.Tools caller | Before acquisition, construct one sealed `AssetBoundedDelivery` owner under `B0`. It owns the terminal return partition. Return it synchronously, not through an independently faultable Assets Task; no out-parameter or callback exposes it early. The actual Content.Tools caller invokes `EndDelivery` only **after that ordinary driver call has returned/unwound**, including on recorded failure. |
| `EndDelivery` / last ordinary handle release → worker termination | Close admission, end actual uses, release exact generation ownership, request stop, and join the worker outside gates and never from that worker. The enclosing ordinary caller observes join/helper return and consumes raw cleanup facts. Prove the selected runtime's remaining thread-control retirement; `IsAlive`, an exit flag and ready receipt are not that proof. |

The delivery object's fixed terminal storage is an **output transfer**, not a refund at readiness. Its original partition stays with that exact caller-owned carrier through `EndDelivery` and its return suffix. After successful detachment it contains only scalar receipt/grant facts, with no issuer/backing/worker/codec/token/context/exception references. Do not refinance it from another Store or reuse its grant as operation capacity. The tiny delivered carrier may then be collected as ordinary caller-owned output; no Assets success root keeps it alive. This closes the suffix without an infinite chain of completion observers or a new caller graph grant.

For a live session, logical Store close releases only its owner reference. A typed or package handle can still be the last owner of a generation after Store close/replacement. That generation keeps the original issuer/registration/worker provision until final release. Closing a newer generation cannot refund an older one. The final ordinary handle that makes the entire issuer quiescent performs the external termination join; it does not wait on itself.

Preinstall nonblocking finalizer handoff/retirement links. A finalizer must not join, allocate, invoke Runtime/user code or traverse/dispose managed owners. If no ordinary caller remains able to prove worker retirement, retain/quarantine the original worker/control provision and report the scalar debt; do **not** claim a successful join or refund it. This is abandonment/failure retention, not the normal success path. The production validator must end all issued handles and delivery deterministically.

## 7. Same-envelope ledger and numerical work

`B0` is issued once from the original options. Replacement does not create another purse. Candidate CPU admission uses at most `min(B0, candidate.CpuAssetBudgetBytes)`; current, candidate, displaced, lease-retained, returning and failed generations all remain in the original total. Existing decode/live-handle counters still apply. Lower replacement limits do not invalidate old handles or erase their debt; they refuse new work that no longer fits.

For the selected two-entry opening, conservative simultaneous **raw capacities only** are:

```text
package header                   64
Foundation profile           65,536
two-entry directory             128  = 64 + 2 * 32
reusable alignment padding       15
worker I/O scratch           65,536
actual SHA-256 result            32
retained expected digest         32
                            -------
                            131,343 bytes
```

This is not an allocation profile or useful-fit proof. Add actual CLR headers/strides/alignment, the two-entry array, native path storage, metadata/maps/adapters, BCL hash/provider construction and disposal, worker/runtime/stack/signaling storage, opening controls and all protected error/unwind/return provision. Include rejection paths, cold peaks and actual capacities, not only successful small inputs. Do not copy World's `ArrayCharge` formula into Assets without qualification.

Use disjoint ownership:

```text
Debit =
    OriginalIssuerAndDelivery
  + AttributedSharedPartition
  + WorkerAndFixedRequestStorage
  + sum GenerationKeep(current/candidate/old/failed/returning)
  + CurrentOpeningOrReadPeak
  + RetainedTypedResultPartitions
  + UnspentProtectedRetirementAndReturn
Debit <= B0
```

Preserve the existing decoded-wire debit as a minimum, counted once per shared decoded value until final retained-handle release. For each closed read/result, derive the actual complete peak and charge the excess over storage already represented by that minimum; do not charge the same byte array twice or pretend the wire debit covers unknown codec allocations. Bound current codec arrays/copies, encoded-record retention, scanner List/HashSet capacities and semantic validation from pre-read entry lengths and fixed schema ceilings. Conservative bounds may refuse; use no application callback to measure a graph after allocating it.

No numerical worker/hash/fault/codec profile is asserted here. Unlike arbitrary application inputs, these are now a fixed, inspectable implementation set. The next batch can implement it and derive its actual coefficients/capacities. If the complete selected peak does not fit the default envelope for the real triangle example, **do not enable the lane**; reduce attributable implementation storage or report that measured failure, rather than raising budgets, hiding terms or declaring “caller-owned” exemptions.

## 8. Distinguishing example and acceptance checks

Use the existing independently authored GLB and OBJ triangle pair in `SceneAssetStoreTests.GlbAndWavefrontObjAdaptersCookEquivalentNativeMeshPackages`, and the actual `cook-static-mesh` command. The source-derived expected witness is three vertices, three indices, one group, one selected Material slot, no Skeleton and no texture bindings.

```powershell
dotnet .\artifacts\bin\sources\TerraFX.Content.Tools\Debug\net11.0\TerraFX.Content.Tools.dll cook-static-mesh obj --source Assets\triangle.obj --profile-record Assets\profile.ebin --material-record Assets\material.ebin --mesh-id 1 --material-id 2 --output artifacts\content\triangle.epkg --obj-convention rh-y-up-z-forward-meters --package-validation bounded-v1
```

This is a **future** invocation, not a command run by this proposal. The standard and bounded modes must publish byte-identical packages from those same inputs. That makes the lane useful for the existing pre-publication validation step, rather than merely opening an empty test package.

Distinguish admission from semantic rejection:

| Real test/input | Required result |
|---|---|
| Exact factory registration, default token, supported context, complete fit | Open and both actual typed loads succeed; verify the literal geometry/binding facts, then complete retirement. |
| Same package/budgets with a legal unused stateful application codec, unsealed two-codec reconstruction or ordinary four-codec Rendering registration | `UnsupportedRegistration` before worker/file acquisition. A missing-path variant must still produce that refusal rather than a file-not-found failure. The codec body is never invoked. |
| Ordinary/derived caller CTS, linked token, pre-canceled token; unsupported synchronization context/STA | Corresponding pre-acquisition refusal. No source/callback/context storage appears in the lower graph. Standard public Assets behavior is unchanged. |
| Correct registration but checksum mismatch; correctly checksummed malformed directory; Brotli entry; third entry | Distinct receipt facts and complete cleanup. Checksum failure precedes layout/content-family failure. No hidden BCL reopening. |
| Exact/one-byte-short complete preclaim; held current/old leases during replacement; returning operation or quarantine debt | Genuine fit/refusal under the unchanged original total. No double return, new purse, eviction, stale rebinding or live-storage refund. |
| Store closed before old asset/package handle; final handle closed on an ordinary thread | Issued value/provenance survives; new admission refuses; final file consumption and worker join happen once under the original grant. |
| Failure primary plus independent safe cleanup; consumed-with-error; uncertain disposition; repeated close | Every direct scalar fact has its defined slot/order; committed identity survives post-switch failure; no retry or false successful release. |

Use actual production inputs/owners for tests. An application codec used to demonstrate refusal is an input-contract witness, not a fake producer/grant. Do not use the existing throwing-FileStream test as native-close evidence. Managed dual-failure logic, static valid-path proofs and genuinely observed native failures are separate.

## 9. Implementation batches and fail-closed enablement

The choices above are sufficient tentative guidance to begin the next authorized implementation task. Do not insert another open-ended ownership decision ahead of the compile-only work. This document itself authorizes no source changes.

| Batch | Concrete work | Gate before enabling the capability |
|---|---|---|
| **A — acquisition-free owner and outcome contract** | Extend existing issuer/backing/lifetime controls with bounded mode, scalar preclaim, intrusive opening retention, fixed receipts and delivery partition. Add the exact closed factory construction seam. No public Assets API or Scene change. | Demonstrate unsupported registration/token/context refusal before acquisition; original general-route tests remain valid. No external acquisition while any fixed/protected term is unknown. |
| **B — real worker and native port** | Implement Windows and Linux acquisition/read/one-shot release, startup handshake, exact request storage, partial construction, finalizer handoff and external join. Compile the real implementation, not a fake ready profile. | Close the complete startup/acquisition/close/return ownership proof and derive actual fixed layout/native/runtime charges. A raw subtotal or a requested stack size is not permission to start it. |
| **C — opening and both typed roots** | Route checksum, metadata, None reads, envelope/reference inspection and both built-in codecs through that generation's port. Implement exact-context/registration checks, real bounded handles and replacement/late release. Close all driver/consumer aliases. | Derive the closed cold/peak/failure/result profile from final code. Admit a useful real package under default budgets, with exact/one-short and accumulated-generation checks. No generic/public fallback or Scene enablement. |
| **D — terminal delivery and qualification** | Integrate the actual synchronous Runtime driver → Content.Tools recipient → `EndDelivery` chain. Verify native/thread return, error mapping and scalar-only terminal output. | Complete ordinary, failure, cancellation-boundary and late-owner tests. Retain the original charge for any unsupported close/join outcome. No success while native consumption or actual return is unknown. |
| **E — opt-in production command** | Add `--package-validation standard|bounded-v1`, phased command cancellation and deterministic receipt printing. Reuse actual GLB/OBJ cooking and atomic publication. Update only its owning guide in that later task. | Enable only the qualified runtime/RID first. Execute the real published NativeAOT Content.Tools path, verify bytes/typed facts/no publication on refusal, and prove no arbitrary runtime exception/context hook enters its closed execution. Other targets stay `ProfileUnavailable`. |

Run ordinary owning-project Debug, then Release tests using the SDK selected by `global.json`. Preserve current-run TRX/logs under `artifacts`; confirm nonzero selected **and executed** tests. Relevant existing anchors are the Assets registration/reader/Store/package-lease fixtures and `SceneAssetStoreTests`' static-adapter/cook tests. Combine focused selectors; do not run the whole solution merely to validate this document.

For the implementation profile, collect cold and warm managed allocation/capacity observations, native heap/provider/handle/thread/stack reservation and commit, and enqueue→read/decode→close/join timing on the actual supported Windows/Linux targets. Compare against standard mode on the same packages. Derive a conservative source/runtime bound; measurements check that derivation and expose missed storage, rather than promoting observed high-water marks to mathematical maxima. Report serialization/copying cost or regression honestly; no performance win is claimed.

Chapter 35 section 4 still requires distinct native/AOT/consumer observations. When a required valid close failure, partial-startup failure or uncertain native-return branch lacks a safe real stimulus, record that branch, missing capability and retry condition. Keep its applicable qualification incomplete; do not corrupt handles, inject throwing allocators/streams, fabricate a producer or rerun an unchanged blocked probe to turn it green. If that missing observation is required for activation, the corresponding target stays unavailable.

**All Scene preparation, nonempty restore, Scene-dependent rendering, broader Assets profiles and milestone acceptance remain gated independently.** The lane establishes none of the planner/cache/receiver/publication return obligations, and does not change `PreparedScene.QualifiedFootprint`.

## 10. Review-input identity and verification scope

Source snapshot UTC `2026-11-19T20:16:08Z`, HEAD `e63b07aa2c9bd01f12414070484eec534de4c6b2`; `607a4e3d` and `7e76914c` are ancestors, not claimed HEAD identities. The scope below includes the final inspected Material codec partial. Listed tracked inputs were clean. Concurrent Runtime Scene-test edits and other new M05 proposals are outside this review and were not edited.

Each set digest is SHA-256 of ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines, LF-joined, UTF-8 without BOM/trailing newline. `ALL` is the union, excluding this output. This is an analysis snapshot, **not** a whole-program allocation/native qualification or acceptance of any older proposal.

```text
A: sources\TerraFX.Assets\
AssetCodecContext.cs, AssetCodecRegistrationReceipt.cs, AssetEnvelopeDecoder.cs,
AssetLease.cs, AssetLeaseLifetime.cs, AssetOwnedReadDescriptor.cs,
AssetPackageBacking.cs, AssetPackageEntry.cs, AssetPackageLease.cs,
AssetPackageReader.cs, AssetPayloadReader.cs, AssetReferenceScanner.cs,
AssetStore.cs, AssetStore.PackageLease.cs, AssetStore.PackagePin.cs,
AssetStore.ReadDescriptor.cs, AssetStoreOptions.cs, AssetStoreRegistration.cs,
AssetStoreRegistrationBuilder.cs, IBinarySchemaCodec.cs,
IContextualBinarySchemaCodec.cs

K: sources\TerraFX.Core\
CompiledContentSchemaRegistry.cs, ContentFieldDefaultDescriptor.cs,
ContentFieldDescriptor.cs, ContentProfileDescriptor.cs,
ContentSchemaIdentityAssignment.cs, DataSchema.cs, EbinReadLimits.cs

P: sources\TerraFX.Runtime\Rendering\
Renderer.CpuExtractionSupport.cs, RenderMaterialAsset.cs,
RenderMaterialAssetCodec.cs, RenderMaterialAssetCodec.Shared.cs,
RenderMeshAsset.cs, RenderMeshAssetCodec.cs, RenderMeshAssetCodec.Shared.cs;
sources\TerraFX.Runtime\Scenes\PreparedScene.cs, SceneSupport.cs;
sources\TerraFX.Content.Tools\
ContentPackageWriter.cs, Program.cs, StaticMeshCookCommand.cs

S: sources\TerraFX.Core\CoreContentSchemaCatalogue.cs;
sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs;
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs;
sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs

D: docs\design\
02-dotnet-api-memory-and-ownership.md, 04-capacity-and-workload-profiles.md,
11-assets-import-streaming-and-packages.md, 29-project-setup-build-and-deployment.md,
30-public-api-review-surface.md, 35-implementation-roadmap.md;
docs\project\Building.md, Roadmap.md;
docs\proposals\
m03b-assets-retained-input-budget-closure.md,
m03b-assets-file-io-port-implementation-closure.md,
m03b-assets-prelease-bootstrap-and-close-closure.md,
m03b-assets-file-handle-release-acknowledgment.md,
m03b-scene-built-in-codec-profile-admission.md

T: tests\TerraFX.Assets.Tests\
AssetPackageReaderTests.cs, AssetPackageLeaseTests.cs, AssetStoreTests.cs,
AssetStoreRegistrationTests.cs;
tests\TerraFX.Runtime.Tests\Scenes\StaticMeshSourceAdapterTests.cs

C: .editorconfig, .github\copilot-instructions.md, .github\workflows\ci.yml,
global.json, Directory.Build.props, Directory.Build.targets,
Directory.Packages.props, sources\Directory.Build.props,
sources\Directory.Build.targets, tests\Directory.Build.props,
tests\Directory.Build.targets,
sources\TerraFX.Assets\TerraFX.Assets.csproj,
tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj

R: .dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\.version
```

| Set | Files | SHA-256 |
|---|---:|---|
| A | 21 | `5af73ea6f0f0d269ddbe08ccb8c24bc878b3973d52a35bbba1d10c1e104e2a2b` |
| K | 7 | `5ca290644fee2ca411c143c67f09e79c1d3e1c6f066df376e9e96b44e5838cc2` |
| P | 12 | `1c1640a5ca73fc379dcde28ea74719373f2d6ac3658f1134007e6aca91735a19` |
| S | 4 | `e29908e3df557008195f0f7b088bdfe6f241102590c2587bcc092d57da274e01` |
| D | 13 | `d862f66c833887443c93e83545d23b16d9d14124b2c7034a3b8220ec8627800b` |
| T | 5 | `008e438cf69ed44be54964e38a2c991c21b145a539c0cd381b488f7d1022ecd7` |
| C | 13 | `7cbc6ebba1d4d6fa5a4636f6e722feda150641dc31e00471f229671f5d09582f` |
| R | 1 | `aab4f62773986811741aea2bbe2f631c7fbaeeefd485eca380deba5622b1ba8e` |
| ALL | 76 | `bde4c55beef3a6144a2b82e62b6c298274a68fd9f359af084669b146ec7cdd11` |

Critical raw source identities:

| File | SHA-256 |
|---|---|
| `AssetPackageReader.cs` | `4cb1c1797c4213285381f9defd8a06e6a15fd51b227e9ba189c7ca975621bd47` |
| `AssetPackageBacking.cs` | `5d1bd989ec81270124d5060bfc85299d74ddb74c3eb20b4280e47a7777545585` |
| `AssetStore.cs` | `16da003e227c380b904825036823a98c6060309c63ec76ad869eccfddfe7964e` |
| `AssetStoreRegistrationBuilder.cs` | `d1789c3cd71d8356e76d264c043c78d5938fa9e738cd5a686111e587b5e51b07` |
| `PreparedScene.cs` | `8a822c991792d18bbcf3d58718c7c420833c45a261f406487c7a6dd2e2ef3df0` |
| `CoreContentSchemaCatalogue.cs` | `91cf1aa2ff4feaba898d83e587d4be403de6cfea97cfbb59fb15d5b67edf2853` |
| `NativeAssetSchemaCatalogue.cs` | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `SceneSchemaCatalogue.cs` | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |
| `RenderingSchemaCatalogue.cs` | `de25a8c0e249ff9ab5b592fe848c85429c004fe629694c64ef1afef3ba731614` |

The local runtime identity file names `11.0.0-rc.3.26522.148`, unified-source revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; this is not a testhost/native-binary attestation. [Thread.UnsafeStart documentation](https://learn.microsoft.com/en-us/dotnet/api/system.threading.thread.unsafestart?view=net-11.0) verifies the selected no-ExecutionContext-flow API; its native/runtime costs still require qualification.

Recheck these bytes before disposition or implementation. Changed inputs require scoped reassessment, not reuse of an old proposal's hash table.

**Document-only work:** source paths/symbols, catalogue membership and input hashes were checked. No build/test/native/allocation experiment was run and no 50/50 test result is claimed as new evidence here. Source, tests, current proposals, design chapters and Roadmap are unchanged by this task.
