# Owned buffers and internal collections

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

[02](02-dotnet-api-memory-and-ownership.md) owns general lifetime/failure rules. This chapter owns large buffer APIs and internal collection invariants.

## 1. Storage categories

Managed reference-bearing storage and unmanaged element storage have different contracts. The garbage collector (GC) must see and update managed references. Such elements stay in ordinary managed arrays/collections or typed pools; they are never hidden in native bytes, bitcast across reference-containing layouts, or copied with raw native memory operations.

Unmanaged storage admits `T : unmanaged`, checked count/stride/byte arithmetic, explicit allocation ownership, and no GC references. This does not make every bit pattern a valid `T`, establish native ABI compatibility, or prove a pointer's lifetime. Native resources/handles embedded in `T` still have their own semantic ownership; bulk storage disposal does not automatically dispose arbitrary elements.

Storage follows [02](02-dotnet-api-memory-and-ownership.md#2-reuse-ledger)'s memory-type exclusion, including private implementation and tools. A framework call accepting a prohibited type is not an exception to that policy; choose a span/typed-storage/native-I/O path with the ownership rules below.

Ordinary synchronous callers use `Span<T>`/`ReadOnlySpan<T>` in scoped methods. Retained native/GPU/I/O work takes an explicit owned buffer lease and range. Contiguous CPU storage uses `nint` lengths/offsets; aggregate logical chains use `long`. A span window still has an `int` length and is never a view of more than `int.MaxValue` elements. [02's integer policy](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries) owns the signed default and representation exceptions.

Synchronous `EbinPayloadWriter` helpers borrow their input spans and caller-provided output span only for that call; they do not retain, allocate, reserve or account for either storage. The caller keeps every input backing unchanged and the destination backing live and exclusively borrowed through completion. `scoped` prevents safe ref escape but does not establish backing stability or prevent aliases; retained/native work separately requires the owned-buffer lease and admission rules below.

## 2. Safe large unmanaged ownership

`TerraFX.Buffers.UnmanagedBuffer<T>` is a sealed `IDisposable` owner. It has no public pointer, escaping span, implicit conversion, copyable value-owner struct, or `MemoryManager<T>` inheritance. `Length` is an element count; `ByteLength` is the checked product, not interchangeable with it.

Allocation validates nonnegative element count, positive power-of-two alignment, type alignment/stride, configured byte budget, and native-address representability before reserving/allocating. `BufferAllocationOptions.MaximumAllocationBytes` and `Alignment` are `nint`; the budget is nonnegative and zero permits only an empty allocation. The checked count-times-stride and any allocator/alignment-rounded extent must fit `nint.MaxValue`, not just the element count. Native `nuint` allocator arguments are checked conversions of those admitted values. This deliberately rejects a contiguous byte extent above the signed native maximum, including above 2 GiB minus one on a 32-bit process; it is an API bound, not a claim that every OS allocation API has that limit. Actual address bits may have their high bit set. Zero length is legal, has no native allocation, and still has an ordinary disposable owner. Invalid arguments throw `ArgumentOutOfRangeException`/`ArgumentException`; extent arithmetic overflow throws `OverflowException`. Allocation failure throws after releasing reservations; there is no zero/default-success owner. Release uses the matching allocator and occurs exactly once.

| Member shape | Contract |
|---|---|
| `static UnmanagedBuffer<T> Allocate(nint length, BufferAllocationOptions options)` | Owns zero-initialized native storage; allocation/accounting completed before return |
| `nint Length`, `nint ByteLength` | Immutable validated extents |
| `BufferReadLease<T> AcquireRead(nint offset, nint length)` | Pins the named range and forbids conflicting write admissions |
| `BufferWriteLease<T> AcquireWrite(nint offset, nint length)` | Exclusive mutation of the named range under the selected overlap policy |
| `void CopyTo(nint sourceOffset, Span<T> destination)` | Synchronous copied read; no escaping native view |
| `void CopyFrom(ReadOnlySpan<T> source, nint destinationOffset)` | Synchronous copied write with conflict validation |
| `void Dispose()` | Closes new admissions, releases the owner's credit, and defers actual free until active uses/leases complete |

Both lease types are sealed disposable reference owners, not freely copied owning structs. A lease records allocation identity/generation, offset/count, access mode, and its independent lifetime credit. Calls through a disposed lease throw. `Dispose` is idempotent; disposing the buffer does not invalidate an already admitted operation's memory beneath it. A new lease after close throws `ObjectDisposedException`.

`BufferReadLease<T>.Retain()` and `BufferChain<T>.Retain()` return distinct independent read owners of the same exact range or canonical ordered segments. Retain and close linearize at the receiver admission gate; close-first throws before acquisition, retain-first survives later parent/owner close. Retention does not copy/flatten payloads, create writable aliases or transfer caller credits. Partial construction releases every acquired valid credit/reservation and preserves the primary exception. Valid built-in buffer/lease/chain/owner release is nonthrowing and idempotent, with no callback, allocation or wait in the release path; physical free still follows actual last use. New quota machinery requires its own qualification. Domain-bound retention cannot shed accounting through an ordinary Retain call; [11 M03 4.5](11-assets-import-streaming-and-packages.md#m03-45-native-node-graph-request-and-operation-credits) and this chapter's Core bridge specify exact child/root interests.

A read lease supplies `UseWindow<TAction>(nint relativeOffset, int length, scoped ref TAction action)` with `TAction : struct, IAction<ReadOnlySpan<T>>, allows ref struct`; a write lease uses `IAction<Span<T>>`. Their `Invoke` members receive `scoped ReadOnlySpan<T>`/`Span<T>`. Negative offsets/counts reject; after validating the offset, require the requested count to fit the remaining extent without overflowing an addition. The empty range at the end remains valid. Bounds/conflicts validate before invocation. The callable is borrowed by reference, invoked synchronously through its concrete generic type, and never copied, boxed, retained or wrapped in a delegate. Caller state changes remain observable; a scoped span cannot be retained or awaited through safe C#.

Each `UseWindow` takes a short-lived operation credit before inspecting the lease and releases it in `finally`. Concurrent or reentrant lease disposal cannot free memory while an admitted callback is running. Callback exceptions propagate after credit cleanup. A disposed owner cannot make a valid active lease refer to a new allocation. Unsafe caller code that deliberately exports an address is outside the safe facade, not something the API claims to sandbox.

Range conflicts are explicit: simultaneous reads may overlap; a write conflicts with overlapping reads/writes. A write lease also admits at most one active window/native operation, refusing concurrent or reentrant use even through copied references to that same lease. A sublease inherits its parent access reservation rather than conflicting with itself, but acquires that exclusive active-operation admission; the parent cannot mutate while its async consumer runs. Independent disjoint write leases may run concurrently. Conflicting synchronous admission throws `InvalidOperationException`, never blocks a callback. Ordinary async requests may queue outside that boundary. Empty ranges retain identity/lifetime but have no byte conflict.

## 3. Asynchronous and native consumption

`TerraFX.Buffers.OwnedBuffer<T>` is the sealed read-only owner for finalized results such as CPU readback, not another allocator or collection family. Its constructor is internal; public members are immutable `nint Length`/`ByteLength`, `AcquireReadLease(nint offset, nint length)` and `Dispose`. It shares `UnmanagedBuffer<T>`'s checked allocation, accounting, lease and scoped-window machinery.

Finalization joins all writes, transfers the private storage/accounting credit exactly once, and leaves no caller-accessible mutable owner or write lease. The engine performs no further writes after publication. Merely wrapping an arbitrary aliased writable owner does not establish this invariant. Independent read leases can overlap and remain valid after owner disposal; disposal closes new acquisitions and drops the owner credit, but callbacks/native consumers still delay actual reclamation. No public pointer, writable window, `Memory<T>`, or underlying storage property is added.

An asynchronous operation takes its own sublease before acceptance; the caller may then dispose its lease without revoking the operation. The operation task becomes terminal only after the underlying consumer has actually stopped accessing storage and the sublease is reclaimed. Canceling an observation or requesting OS cancellation is not completion.

Native bindings obtain an address only through an internal checked operation token. It includes exact byte extent, access mode, allocation identity, active-use credit, and required alignment. It cannot be cached beyond the operation or used after actual native completion. GPU upload staging, readbacks, file reads, decoder output, and interop calls retain their own resource/version/fence protocol in addition to this storage lease.

Shutdown closes admissions and awaits owned consumers under [02](02-dotnet-api-memory-and-ownership.md)'s timeout rules. A stuck native consumer leaves its memory allocated and reports an ownership-retaining shutdown fault; it must not be freed to make disposal appear successful. Diagnostic leaked/stuck bytes remain counted.

Safe public members establish these invariants internally. Small audited allocation/span/native-call blocks may use `unsafe` without propagating a caller obligation. `TerraFX.Core` is safe-facing, not unsafe-free. Raw geometry bitcasts follow [06](06-owned-geometry-and-numerics-interchange.md)'s scoped implementation unsafe-block policy while retaining safe public calls. Audio/native real-time callbacks do not call public allocation/acquisition/window/disposal methods: control setup admits and retains internal bounded access tokens for the full processing epoch. Last-credit disposal/free/accounting occurs on a non-real-time owner after quiescence, never at a sample callback's decrement. Retirement capacity is reserved before accepting consumers.

## 4. Noncontiguous data and managed overlap

A large contiguous owner is not required to represent every stream. `BufferChain<T>` is a sealed immutable descriptor over ordered retained read leases, with checked total `long` length, bounded segment count, and no gaps/overlap ambiguity in its logical sequence. Its `CopyTo` takes a `long sourceOffset`; logical chain offsets are not native addresses. Locate the segment in the checked logical domain before converting its segment-relative offset to `nint`. Construction owns independent lease credits; disposing a caller's original leases cannot invalidate the chain.

The raw constructor array admits at most 65,536 entries, including empty entries, checked after `IsDefault` and before descriptor allocation, enumeration or cloning. One over throws `ArgumentOutOfRangeException(nameof(segments))` without acquisition. Canonical zero-length entries remain omitted; immutable `SegmentCount` reports the resulting count even after close. Graph/request/domain source-chain admission is independently 4,096 by default and at most 65,536 under [04](04-capacity-and-workload-profiles.md). These are count guards, not measured sustaining capacity or permission to flatten fragmented input.

`BufferChain<T>.CopyTo` and `VisitWindows<TVisitor>(scoped ref TVisitor visitor)` expose windows in logical order. `TVisitor : struct, IFunc<ReadOnlySpan<T>, bool>, allows ref struct`; `bool Invoke(scoped ReadOnlySpan<T> window)` returns true to continue and false to stop. `VisitWindows` returns true only when traversal completes, including an empty chain; an explicit stop returns false even on the last window. Split each nonempty segment into maximal windows of at most `int.MaxValue` elements without crossing segment boundaries. Validate all descriptor extents before the first invocation and retain a chain operation credit protecting every private segment through traversal, including concurrent/reentrant chain disposal. Stop or exception releases that credit in `finally`; exceptions propagate and earlier caller-state changes are not rolled back. The visitor cannot evade validation or change chain ownership. Asynchronous consumers retain the chain or their own range subleases. Zero-length segments are omitted canonically. No public segmented cursor exposes BCL sequence positions or silently flattens an arbitrary chain into an array.

Managed-reference collections do not implement these unmanaged leases. Where a subsystem really needs retained managed `T` data, it uses an explicit immutable copied snapshot or a separately documented managed owner; it does not pretend that a native buffer can own object references.

### M03 4.6.4 Required Core bridge and public operational closure

Core alone knows the actual rounded allocation and private registration lifetime. The following safe-facing public primitives complete ordinary Retain; implementation and qualification remain prerequisites:

```csharp
public readonly struct BufferStorageIdentity : IEquatable<BufferStorageIdentity>
{
    public bool IsValid { get; }
    public bool Equals(BufferStorageIdentity other);
    public override bool Equals(object? obj);
    public override int GetHashCode();
}

public readonly struct BufferRetentionInfo
{
    public BufferStorageIdentity StorageIdentity { get; }
    public long AllocationByteCount { get; }
    public long ByteOffset { get; }
    public long ByteCount { get; }
    public long ReadCreditMetadataReserveBytes { get; }
}

public sealed class BufferRetentionDomain : IDisposable
{
    public BufferRetentionDomain(long maximumRetainedBackingBytes,
        long maximumMetadataBytes, int maximumReadCredits);
    public bool TryReserve(ImmutableArray<BufferRetentionInfo> reads,
        long additionalMetadataBytes, out BufferRetentionReservation? reservation);
    public Task WhenDrained { get; }
    public void Dispose();
}

public sealed class BufferRetentionReservation : IDisposable
{
    public long ReservedMetadataBytes { get; }
    public Task WhenReleased { get; }
    public void Dispose();
}

// Additional members on existing Core lease/chain types.
public BufferRetentionInfo GetRetentionInfo();
public BufferReadLease<T> Retain(BufferRetentionReservation reservation, int inputIndex);
public int SegmentCount { get; }
public bool VisitRetentionInfo<TVisitor>(scoped ref TVisitor visitor)
    where TVisitor : struct, IFunc<BufferRetentionInfo, bool>, allows ref struct;
public BufferChain<T> Retain(BufferRetentionReservation reservation, int firstInputIndex);
```

The first additional methods belong to BufferReadLease<T>; SegmentCount/VisitRetentionInfo/chain Retain belong to BufferChain<T>, retaining `where T : unmanaged`. VisitRetentionInfo admits/captures one charged immutable descriptor interest under the chain gate, then invokes every visitor outside all chain/storage/quota/Assets gates as [M03 4.6.8](05-owned-buffers-and-internal-collections.md#m03-468-gate-free-immutable-retention-info-visitation) specifies. It returns true for complete visitation (including empty), false for explicit stop even at the last descriptor; it visits metadata, not payload windows. Snapshot info has an internal constructor and opaque allocation-token identity, not a Guid/address/owner object/pointer or authority. Default identity/info is invalid. Rounded AllocationByteCount is frozen in Core at Allocate; ByteOffset/ByteCount are checked byte conversions of the exact live typed range. Opaque identity is unique to that allocation lifetime even if an address is reused. Inspecting info neither takes a payload credit nor makes later closed-source Retain succeed.

TryReserve validates the initialized infos/nonnegative metadata and checked demand, reserves the complete unique-backing/reference/metadata plan atomically, then creates the reservation from already booked storage. false/null is only expected capacity; disposed root domain/invalid inputs throw. No read credit is acquired by planning. Each actual Retain consumes exactly its planned slot/range/identity under the storage gate; duplicate slot use/wrong source/index fails before acquisition. Partial acquisition unwinds held slots and reservation interest, preserving the original input and other plans. Empty chains use no read slots but their wrapper/metadata still reserve. Inherited Retain/acquire/traversal of budgeted credits cannot call the unbudgeted internal Clone path; they reserve same-domain child slots and metadata before allocation or use. An explicit different-domain reservation is another full charge, never a detach/transfer.

Budgeted Core registration/descriptor storage is reservation-owned, bounded and reclaimable when its actual users leave. It may not simply grow the current caller-wide `_registrations` List unaccounted or strand engine-owned capacity after releasing its charge. Use separately tracked bounded registration storage or an equivalently charged/reclaimable implementation, preserving read/write conflicts and the same lifetime gates. Core charges every slot/container/anchor growth before allocation. The public bridge supplies no executable callbacks to a quota/release path, no custom allocator or friendship. Assets' domain books its own metadata through the same Core metadata ledger via additionalMetadataBytes, so there are not two independent64-MiB pools secretly totaling128 MiB.

UseWindow/VisitWindows/native operation tokens through a budgeted credit also reserve their active-use/temporary-reference count and charged metadata before starting, even when the implementation needs only a counter and preallocated slot. A read callback or chain traversal cannot evade the aggregate limit by avoiding a fresh wrapper; expected synchronous capacity refusal is InvalidOperationException with no callback invocation. Existing hot-path zero-allocation qualification is retained separately: accounting can use already reserved fixed slots/counters, not a new callback/delegate/heap owner per window.

Lock order is Assets admission gate -> Core quota gate -> storage gate for acquisition. Release detaches under storage gate and updates quota/completion outside it; never call back into Assets or acquire a domain gate while holding storage. Admission captures immutable plans under the graph/node/request gate without holding it across a domain callback; actual later acquisition verifies open state/ranges again. No wait/callback/task completion runs under these gates. Valid releases update fixed preallocated records/counters and complete asynchronous-continuation signals outside locks.

Reservation Dispose closes further planned acquisitions and closes only the root captures/allocations explicitly issued by that reservation, including captured chains and their descriptor owners, not just individual lease slots. It tracks those roots in already reserved Core storage, so failed Assets/Tools construction can retire them without recovering a lost generic helper return. Each independently returned child and active use retains its own interest; its metadata/backing stays charged until actual release/WhenReleased. A duplicate root-lease Dispose is idempotent. Domain Dispose closes new root work reservations; already admitted open read families can make bounded same-domain read children until their parent closes, so an active lease's lifetime is not revoked. WhenDrained completes only after every reservation/child/use/retirement charge ends. These completion tasks do not permit buffer reuse early and are not observer-cancellation effects.

AssetRetentionDomain(limits,maximumRetainedBackingBytes) owns that Core domain plus its higher-level count/queue/recovery ledger. The observational members remain `AssetRetentionLimits Limits`, `long MaximumRetainedBackingBytes`, `AssetRetentionSnapshot GetSnapshot()`, `Task RetryCleanupAsync(CancellationToken cancellationToken=default)` and `ValueTask DisposeAsync()`, but are not its complete callable API. [M03 4.6.6](11-assets-import-streaming-and-packages.md#m03-466-callable-assets-admission-participation-and-retirement-protocol) supplies the mandatory public opaque-ticket protocol, registered outside-gate resource drivers and actual domain-owned cleanup; [M03 4.6.7](05-owned-buffers-and-internal-collections.md#m03-467-prospective-backing-reservation-and-atomic-allocation-identity-handoff) supplies prospective allocation claims. Snapshot has get-only int GraphSnapshots/RequestSnapshots/OutstandingOperations/PinnedAssetLeases/ReadCredits/QueuedOperations/RunningOperations/QuarantinedOperations/LiveHandles/LiveProcesses/NamedStagingEntries; long RetainedBackingBytes/MetadataBytes/NamedStagingBytes/ConcurrentDecodeBytes/CapturedProcessBytes; bool IsClosed/IsDrained; and immutable exact limit/demand/diagnostic observations. These are bounded copied counters, not hidden drains/histograms. Tools owns worker/process orchestration and implements trusted resource drivers; Assets owns transitions/cleanup-pass selection/dispatch and Core owns buffer quota. Store and cooker expose their actual domain; no process service locator or implicit domain sharing.

### M03 4.6.7 Prospective backing reservation and atomic allocation identity handoff

Existing-read TryReserve cannot account for new output/decoded/scratch buffers. Core's public bridge includes these `TerraFX.Buffers` members; existing unbudgeted Allocate remains caller-owned:

```csharp
public readonly struct BufferAllocationInfo
{
    public bool IsValid { get; }
    public long ElementCount { get; }
    public int ElementStride { get; }
    public int RequiredTypeAlignment { get; }
    public long Alignment { get; }
    public long LogicalByteCount { get; }
    public long AllocationByteCount { get; }
    public long OwnerMetadataReserveBytes { get; }
}

// Additional members on UnmanagedBuffer<T>, retaining T : unmanaged.
public static BufferAllocationInfo GetAllocationInfo(
    nint length, BufferAllocationOptions options);
public static UnmanagedBuffer<T> Allocate(BufferAllocationTicket allocation);

// Additional overload on BufferRetentionDomain.
public bool TryReserve(ImmutableArray<BufferRetentionInfo> reads,
    ImmutableArray<BufferAllocationInfo> allocations, long additionalMetadataBytes,
    int additionalInterestSlots,
    out BufferRetentionReservation? reservation);
public BufferRetentionSnapshot GetSnapshot();

// Additional member on BufferRetentionReservation.
public BufferAllocationTicket GetAllocationTicket(int allocationIndex);

public sealed class BufferAllocationTicket : IDisposable
{
    public BufferAllocationInfo Info { get; }
    public Task WhenReleased { get; }
    public void Dispose();
}
```

BufferAllocationInfo has an internal constructor/private closed-T layout token; default invalid. GetAllocationInfo performs current Allocate's length/options/type-stride/alignment, checked nint/nuint/native-size and per-allocation-limit checks, allocating nothing. ElementCount is validated native length converted to long; stride is Unsafe.SizeOf<T>() and required alignment the existing layout value. Logical bytes=count*stride checked; rounded bytes follow the checked allocator/alignment extent in section 2. OwnerMetadataReserveBytes is the qualified Core plan including identity/storage/owner/registration/actual-use/ticket/retirement/peak. No forged amount/alignment or reflective T activation. Zero length charges0 backing but owner/identity/slot metadata.

BufferRetentionSnapshot is a sealed immutable domain-produced copied observation: long RetainedBackingBytes/ProspectiveBackingBytes/MetadataBytes; int ReadCredits/ActiveAllocationClaims/ActiveMetadataVisits; bool IsClosed/IsDrained. RetainedBackingBytes includes prospective plus bound identity backing. Add the same ProspectiveBackingBytes/ActiveAllocationClaims/ActiveMetadataVisits and int ProvisionalAdmissions to AssetRetentionSnapshot; existing total/count fields remain. These are cheap bounded observations, not exposing registration maps or draining events.

New TryReserve books existing unique backing, *each separate prospective allocation*, complete metadata and1 initial owner/reference slot per allocation, plus additionalInterestSlots, atomically. Extra slots grant no byte access and are released only with their own reserved interest; metadata must include the qualified per-slot floor. They are used for Assets operation consumers/shared descriptor visitors, not uncharged counters. Equal bytes do not share a prospective allocation. Old overload means initialized empty allocations/zero additional slots. Default arrays/invalid info/negative/overflow throw before reservation; false/null only capacity and no booking. Assets calls the combined vector inside its admission/growth transaction, so graph/request/work/decode and Core read/prospective/metadata claims cannot partly succeed. It exposes allocation tickets through growth, never its private Core quota domain.

Retained backing=sum of unique live identity charges plus still-prospective rounded claims. GetAllocationTicket consumes its planned ordinal once, no second claim; wrong T/domain or reuse throws. States Reserved -> Allocating -> Bound -> Retiring -> Released; abort before allocation Reserved -> Released. Facade/identity-map/owner/registration cells allocate from booked metadata before invoking native Allocate. Returned budgeted owner and all public read/write/window/child paths inherit domain; no detached Clone. Legal read-only finalization transfers exact identity/domain/charge and pre-reserved owner bookkeeping, never relabels aliased writable storage.

Allocate validates ticket/T/open state and atomically claims Reserved -> Allocating under Core quota, then invokes allocator/initialization outside quota/storage/Assets gates. Prospective charge remains through allocation/zeroing/failure cleanup and covers actual memory from allocator success before identity is public. Identity/token/map slot is pre-created, not pointer/digest or another claim. After initialization, one quota/storage transaction binds its exact identity/root interest, subtracts this prospective amount and adds the same frozen identity amount. Total backing is unchanged at that linearization: no gap/double charge. Owner exposes only afterward. Same-domain reads share identity plus separate reference/metadata charges; equal bytes in another allocation have another identity. Reused pointer cannot merge.

OOM/null/clear failure retires any private allocation with valid Core release, then removes prospective charge only after actual cleanup and preserves primary stack. Close before Allocating means no native call; close during Allocating marks pending retirement without refund. Initialization/bind finishes privately, then owner retires without exposure; conventional closed-ticket failure follows real release unless a primary allocation fault must propagate. Stuck allocator/user retains claim/operation; timeout refunds nothing. Dispose is idempotent and after binding closes only issued owner root; independent captures/children/actual uses keep their own interests.

A validated decoder header reserves logical decoded/concurrent bytes plus exact output/scratch allocation descriptors *before* decode. Count/Write/package/index/publication/capture buffers use this same bridge. Source/output/scratch coexist. Decode activity retirement can release logical concurrent demand after real decoding stops, not ready backing; ready snapshot interest is installed before old operation retirement. Evidence distinguishes logical, rounded reserved and actually allocated bytes. No guess/post-allocation booking.

### M03 4.6.8 Gate-free immutable retention-info visitation

VisitRetentionInfo does not clone payload registrations or acquire byte windows. Immutable chain descriptor storage contains only frozen per-nonempty-segment BufferRetentionInfo and logical order at construction/capture, not BufferReadLease roots. Its actual array/capacity/padding/identity metadata is already included in local chain limits and, when bound, a shared metadata-only reservation. Byte-root arrays/registrations are separately charged/owned. Dispose detaches those roots and the chain's descriptor-block reference before releasing them; an admitted visitor holds its independent local block interest, not an uncharged disposed chain's large arrays. SegmentCount/Length remain cheap cached immutable observations. No array/span/lease/pointer into the block is exposed; info is inert copied scalar/opaque identity, not access. This requires new Core storage/accounting machinery, not an assertion the current readonly Segment[] already implements it.

Under chain gate only: check open, reserve1 metadata-visit slot/interest with qualified fixed header/counter storage, attach to immutable block, leave gate. The slot counts MaximumReadCredits despite no payload credit, paying qualified ReadReference/interest metadata. It holds shared full-block metadata until last root/visit interest ends. Local/aggregate refusal throws established contextual InvalidOperationException *before callbacks*. Qualified fixed slots/counters avoid per-visit allocations; no uncharged array/deferred copy/closure. Detached visits use local chain preflight/peak, not an engine domain.

For a bound chain this is a same-family child-interest reservation, not a new public root TryReserve after domain close. An already admitted open chain/read family may make its bounded metadata/window children until that parent closes, preserving4.6.4's close semantics. A new engine snapshot/work admission still refuses. Parent close versus this child-interest capture is one chain-gate linearization; reserved-child failure invokes no visitor.

Every visitor.Invoke(info) uses concrete scoped-ref constrained dispatch outside *all* chain/storage/quota/Assets gates. User code may reenter, coordinate Dispose, block or visit another chain; no two-chain gate order spans a callback. Snapshot/close/nested visits of still-open chains are allowed. Info never substitutes for live-source Retain/read. Admitted-before-close visitation continues against the immutable block; a new visit after close throws. Frozen identity/range values survive byte-root disposal/physical free without exposing address or access authority.

Dispose closes under chain gate, then releases root interests outside; never waits for visitor/invokes one. Visit-first protects descriptor block after root metadata retirement; close-first is ObjectDisposedException/0 callbacks. This metadata-only retention does not release actual payload users; VisitWindows keeps its real byte-operation credits and remains gate-free.

Traverse captured block in logical order; release visit interest in finally on complete/stop/original exception. True means all descriptors (empty true), false explicit stop even last. Propagate original visitor exception/stack after valid nonthrowing slot/metadata release; preserve earlier caller state. Each concurrent visit pays1 interest/slot/header and shares full-block charge. No full copy/double block charge/early refund. Caller copies of inert info are ordinary detached values, not engine-retained private arrays.

Lock discipline: Assets gate may enter Core quota for inert reservation; chain/source gate may enter quota for bounded interest/capture; these outer gates are never held together. Quota may enter storage for acquisition/bind, never chain/source/Assets. Release detaches local state before updating outer quota/completion; no storage->quota gate inversion. Source gates never span participant work. All driver/visitor/user callbacks, completion/continuation, wait and cleanup dispatch occur outside every gate. This permits no exception for allegedly internal visitors.

## 5. Internal collections

Public engine service fields/properties do not expose mutable internal storage/view containers. Public observations use copied values, immutable snapshots, typed handles, and the lease/window operations above.

Internals may use specialized contiguous lists, sparse maps, bit sets, heaps, ring buffers, and structure-of-arrays (SoA) stores. Storage selection is driven by workload, reference content, alignment, lifetime, and measured generated code:

| Internal category | Required invariant |
|---|---|
| Managed value view | Typed array/span backing keeps references visible to the GC; bounded synchronous view cannot escape mutation/structural epoch |
| Unmanaged value view | Checked native extent/alignment plus a live allocation/access token; no hidden ownership transfer on ordinary struct copy |
| Owning collection storage | One reference owner controls allocation/pool return; copied value views are not disposable owners |
| Sparse handle store | Checked owner/index/generation; slots retire before generation wrap |
| Ring/heap/bit set | Frozen or explicitly reserved capacity; growth outside critical callbacks; overflow is observable refusal |
| Pooled scratch | One borrower/return protocol, clearing where needed, no return before jobs/native use ends |

A value container that mutates a length/cursor by copy must have explicitly local, nonshared use. Ref-struct/scoped views and private owner tokens prevent accidental persistence; code review/analyzers cover prohibited copying or mutation across structural epochs. No collection claims thread safety from being a struct, and no unchecked pointer container becomes a public convenience property.

The first implementation uses BCL collections privately where they meet these contracts. A custom internal container is justified by measured allocation, indirection, cache locality, size, or latency behavior. Public type identity is a separate compatibility decision under [02's boundary policy](02-dotnet-api-memory-and-ownership.md#public-type-boundary), not conditional on proving a custom container faster: an engine-owned public contract may initially use BCL backing. No internal container is exposed merely to avoid designing that contract. Tests cover empty/full/growth, bounds/overflow, copy semantics, pool ownership, cancellation/disposal, concurrency where admitted, and strict-order iteration.

## 6. Shared callable protocols

`TerraFX.Core` defines root-namespace `IAction<TArgument>`, `IFunc<TArgument,TResult>` and `IReadOnlyRefFunc<TArgument,TResult>` as declared in [30](30-public-api-review-surface.md#3-core-geometry-spatial-values-and-buffers). Arguments admit `allows ref struct`; result types do not. The first two receive `scoped` by-value arguments; the third receives `scoped in` and preserves the physics readonly-reference calling convention. No unused writable-ref/zero-argument interface family is required. Buffer windows, chains, ECS chunks and physics hit visitors select the actual argument/result convention, not subsystem-specific interface names.

Every hot-path invocation retains the caller's concrete struct/ref-struct type and `scoped ref` state through constrained generic dispatch. Generated adapters register the actual closed callable/argument/result instantiations for NativeAOT, never erase them into an interface field or synthesize a closure. Bounds, owner and admission checks remain with the invoking subsystem; an interface does not establish lifetime or authorize reentrancy. Exception cleanup, partial state and stop semantics are unchanged. `scoped` does not revoke an ordinary managed reference captured from an argument. BF12's code-generation/allocation qualification still applies; generic spelling alone is not a performance result.

The generic interface argument carries scoped with its anti-constraint. A concrete implementation for an ordinary class/value argument spells `Invoke(T argument)` without a by-value scoped modifier, which C# permits only for ref-struct values; a span implementation spells `Invoke(scoped Span<T> argument)`. Readonly-reference implementations retain `scoped in` for either kind. This closed-argument syntax difference does not change constrained dispatch or create managed-reference revocation.

## 7. First-class bounded object pools

`TerraFX.Pooling.ObjectPool<T>` in `TerraFX.Core` is a sealed reference owner for managed reusable `T : class`, not a native allocator, global singleton or concurrent collection. One owner thread is captured at construction. Every operation/property, including cleanup and observations, checks that thread; a wrong-thread call throws `InvalidOperationException` before mutation or policy invocation. Marshal delayed-job returns to that owner; no reset/destruction occurs on audio callbacks.

Construction takes positive `int maximumCapacity` and nonnull `IPoolPolicy<T>`, allocates fixed managed bookkeeping, and creates no payloads. Policy `Create` returns a distinct nonnull fully initialized object and retains responsibility for cleanup of failed creation; `Reset` restores the documented reusable state, clearing references/events as required; `Destroy` releases terminal resources. Destroy must be retry-safe after partial failure. Resource-free policies may intentionally do nothing in Destroy. Managed references remain GC-visible.

`Prewarm(int availableCount)` grows the available set to the requested nonnegative target, outside critical paths; it never trims. Validate the target against capacity remaining after rented/returning/quarantined/retired slots before calling policy. Successful prior creations remain available if a later Create fails; this is explicitly incremental, not atomic rollback. A null/duplicate currently owned object is a policy invariant failure, never another slot alias or permission to destroy an existing rental. `Rent` never calls Create: empty availability throws `InvalidOperationException`; `TryRent` returns false and a default unusable token only for that expected condition. Closed pools throw `ObjectDisposedException`, even for TryRent. A saturating nonnegative `long` miss counter records both exhaustion paths.

`PoolRental<T>` is a copyable, non-disposable readonly token with a private strong reference to its exact pool, signed slot index and opaque `uint` generation starting at 1. Copying creates no ownership credit. It has no public payload, pointer, generation arithmetic or raw native ownership. Pool.Use(rental, scoped ref action) invokes `IAction<T>` synchronously and preserves caller state. Default/wrong-owner/stale/double-return tokens throw before invocation/mutation. A slot already in Use refuses another Use. Generation advances on each new rental; after the maximum generation's return the slot is destroyed and permanently retired rather than wrapping. Retired slots reduce usable capacity and remain observable.

Slot states are Empty, Available, Rented, Returning, Quarantined and Retired. Return changes Rented to Returning immediately, invalidating every token copy for later operations. Reentrant Return during that slot's active Use defers Reset/Destroy until the callback exits; it does not free/reuse the payload beneath the callback. Use's finally releases the active credit and performs pending cleanup. An ordinary callback exception leaves an unreturned rental Rented; it does not secretly return it. If callback and deferred cleanup both fail, throw an AggregateException preserving both; a single failure propagates with its original stack.

Reset succeeds before Returning becomes Available. Failure quarantines the object and attempts terminal destruction, never republishes it; the reset exception remains observable. Successful destruction empties the slot; failed destruction retains ownership in Quarantined and reports both failures. `RetryCleanup` attempts destruction of all quarantined objects, not another Reset; it retains failed objects and aggregates failures after attempting the selected set. No policy may reenter pool operations, including reads, during Create/Reset/Destroy; reject before changing state. Return/Trim/Close attempts all selected independent cleanup work and aggregates failures without losing the per-slot ownership state.

`Trim(int availableCount)` retains at most that nonnegative number of available objects, destroying only the excess; rented/returning objects are unaffected. Close is permanent, refuses Rent/Use/Prewarm/Trim, and destroys available objects. Existing rentals still require Return; an active callback and its pending Return finish safely. Dispose aliases Close, not forced return or blocking drain. Repeated Close does not silently retry failed destruction; RetryCleanup does. `IsDrained` is true only after close, no owned payload/active callback remains, and all destruction succeeded. The caller/host retains and services a non-drained pool; there is no finalizer running arbitrary user policy or success-shaped timeout reclamation.

`PoolSnapshot` reports maximum capacity, Available/Rented/Returning/Quarantined/Retired counts, created payload count, rental high-water, misses and close/drain state as copied signed observations. Returning includes a callback-deferred return; quarantined objects remain CreatedCount until destruction succeeds. Every slot contributes exactly once to its state; CreatedCount = Available + Rented + Returning + Quarantined. Rental high-water measures Rented + Returning and is bounded by maximum capacity.

The pool machinery performs no managed allocation in warmed successful Rent/Use/Return cycles; policies and exceptional diagnostics are separately measured. This is bounded reuse, not Unity-style create-on-empty or an idle-retention maximum disguised as a total cap. Escaped managed references cannot be revoked: consumers must not use/retain payload aliases beyond their rental, and Return requires actual job/native completion. The token alone cannot infer that completion. Do not recycle publicly retained immutable snapshots, terminally disposed native owner facades or aliased completion objects; pool private builders/scratch and publish copied/independently retained results.

## 8. Acceptance obligations

| ID | Required observation |
|---|---|
| BF01 | Zero, ordinary, beyond-managed-array, overflowed, misaligned, and over-budget allocations have exact extents/accounting and conventional failures |
| BF02 | Buffer close/dispose, copied lease references, concurrent/reentrant callback disposal, and double disposal never free beneath active use |
| BF03 | Scoped windows and ordinary/ref-struct callables compile in separate consumers; retaining a borrowed span is rejected. `ref` state updates survive invocation/exception without copying, boxing or delegate adaptation; invalid bounds/conflicts reject before invocation. Finalized `OwnedBuffer<T>` publication leaves no mutable alias/write lease, and independent read leases survive owner close |
| BF04 | Async cancellation, observer cancellation, native completion, GPU fences, and stuck shutdown preserve memory until real consumption ends |
| BF05 | Allocation generation cannot alias a recycled region; diagnostic outstanding credits/bytes remain accurate |
| BF06 | Empty, multi-segment and beyond-`int.MaxValue` chained data validates before invocation, visits the specified windows in order, reports completion/explicit stop, preserves partial caller state on exception and protects all segments during reentrant/concurrent chain disposal; no implicit flattening allocation |
| BF07 | Managed references stay GC-visible; unmanaged raw copies do not fabricate element/resource ownership |
| BF08 | Internal value/native containers remain absent from public fields/properties; copy/growth/pool/epoch invariants are qualified |
| BF09 | Runtime/tooling source and generated output contain no use of the prohibited memory/sequence types; span/typed-storage/native I/O preserves checked extents, retained credits and actual completion without a private bridge exception |
| BF10 | Core safe-facing wrappers establish all unsafe allocation/view invariants; no caller pointer or global unsafe API is required |
| BF11 | Real-time paths use preallocated storage and admitted access; no blocking/growth/refcount reclamation callback occurs there |
| BF12 | AOT layout/allocation/cache and constrained-callable generated-code measurements use matched delegate/interface/value-callable workloads; steady callback dispatch adds no managed allocation, and inlining, copies, code size and actual gains/regressions are reported rather than assumed |
| BF13 | Signed 32/64-bit extent witnesses cover negative counts/offsets/window lengths/budgets/alignment, zero/end-empty ranges, count-times-stride and alignment-rounding overflow at `nint.MaxValue`, and `long` chain-total overflow. Reject before reservation/allocation/callback with unchanged state; high-bit native addresses are not rejected merely by signed count policy. Ordinary/ref-struct external consumers pass `int` span lengths to `nint` APIs without unsigned casts; windows remain `int`-bounded and narrowing is checked |
| BF14 | A separate safe consumer uses all three shared protocols with ordinary/ref-struct mutable state; buffers/chains/ECS/physics retain actual by-value/readonly-ref signatures, validation, partial exception state and last-hit stop behavior. Borrowed-span escape fails compilation; no obsolete subsystem-specific callable interface remains |
| BF15 | Bounded pool state and CLR witnesses cover prewarm/partial creation, no create-on-rent, exact counts, zero warm capacity, wrong thread/owner/default/stale/copied/double returns, reentrant/deferred return and close, callback/reset/destruction failures, retained quarantine/retry, trim, generation retirement and escaped-alias limitations. Warm machinery has no rental-wrapper churn; actual implementation measures allocations/retention/latency against unpooled work and qualifies AOT roots |
