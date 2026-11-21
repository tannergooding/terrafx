# M03-B original Assets B0 source-capacity rectification

**Disposition: option 2, tentatively selected for subsequent implementation batch A under the user's unblock instruction. This changes the proposed accounting subject; it is not an accepted contract amendment or an ordinary Store runtime/native allocation profile. No implementation is supplied here. Keep `PreparedScene.QualifiedFootprint` null and Scene, native, CLI, and consumer gates closed.**

## 1. Selected decision

Implement one original-Store ledger whose byte debit covers **explicit Store-owned storage capacity**, with independently counted interests in opaque CLR/framework/OS resources. Do not describe `CpuAssetBudgetBytes` as a bound on all memory attributable to those resources.

This is a deliberately narrower guarantee than the complete transitive service-memory envelope requested by sections 3.1 and 5 of [the original-Store execution proposal](m03b-scene-original-store-execution-closure.md). Under that earlier interpretation, batch A still has no complete derivable charge. The remedy is an explicit owner decision, not a larger estimate:

1. Original **B0** remains the original Create options' `CpuAssetBudgetBytes`. Existing wire debits plus admitted source-capacity claims must fit B0. Replacement never issues or enlarges it.
2. Source-capacity claims cover conservative allocation envelopes for source-defined engine classes, explicitly selected engine data arrays/strings, explicitly requested native blocks, and reserved future materialization of those same capacities. A selected data array remains charged when a BCL builder, copy, or hash call materializes it. Include known managed headers/alignment rather than discarding them.
3. Generated execution machinery, opaque framework object shells **including source-constructed `Lock`, `object` synchronization gates, Task/TCS, FileStream, and ImmutableArray Builder wrappers**, internal worker/hash/I/O state, native allocator overhead, thread stack/TLS/runtime state, and OS/kernel/provider bookkeeping are **outside this byte metric**. Their source holder slots remain charged; section 5 assigns their resource interests, issuance multiplicities, and lifetimes. This is an explicit exclusion, not a zero-byte price.
4. Ownership, actual consumption, one-shot disposition, cleanup, and actual caller-return requirements are unchanged. A cardinality permit is not a native completion receipt or permission to refund storage still being used.

There is **no new byte purse or unbounded paying owner**. Native/resource failure remains a failure, regardless of source-capacity fit. Independent Stores still have independent B0 values; no process aggregate is promised. A Store can refuse creation, a candidate, or an operation while earlier grants remain live.

The additional decision is the **capacity subject and its boundary**, not a new public options type, allocator callback, Runtime dependency, cryptographic implementation, or worker service. If owners require the earlier all-in memory guarantee instead, this selection cannot satisfy it: they must supply a separately reviewed metered/bounded lower execution capability. The current options and supported BCL calls supply none.

## 2. Why this decision is necessary

Inspection is at HEAD **`2aa27a9c36c21a05df5e178234dd05e17ea50272`**.

| Evidence | Consequence for A |
|---|---|
| `fd205498` and `AssetStoreEntryBoundaryTests.cs` | Usage checks precede pre-cancellation; valid pre-cancellation produces the original-token canceled Task without package acquisition. Preserve this repair. |
| `2aa27a9c` and `AssetStore.cs` | `_originalCpuAssetBudgetBytes` is immutable; `AcquireLoad` uses the lesser of original and backing CPU limits. This fixes wire-budget refinancing, not service/generation/opening charges. |
| `AssetPackageReader.cs:23-220` | Zero CPU budget already refuses opening. **Positive tiny** budgets still pass that check and can allocate a FileStream, hash state, metadata, backing, and Store. The older proposals' zero-budget-success example is stale. |
| `AssetStoreOptions.Validate` | Checks nonnegativity only. Chapter 04's hard option ceilings are not currently enforced here; do not use them as observed bounds on ordinary Store control membership. Fixed package/directory ceilings are enforced separately. |
| `AssetStore.cs` replacement and backing release | One candidate body is elected. Old package leases consume no wire bytes; repeated successful replacements can retain multiple old generations. Current/old/candidate membership needs its own debit. |
| `AssetPackageBacking.cs` | The failed-release root preserves reachability after backing construction. It does not issue B0, observe native close, or cover an unreturned FileStream constructor prefix. |
| `AssetStoreAllocationTests.cs` and Store/backing initializers | The Store shell is 104 bytes; its separately constructed `Lock` is not inside that measurement. The backing constructor's 296 bytes reconcile as backing shell 168 + token 24 + instance Lock 40 + packed read-limits object 64. Its warmed measurement excludes the separate static `s_failedReleaseRootGate` initialization. These measured allocation sizes are not Q's independently aligned source-field envelopes; none establishes FileStream/hash/thread/native or all-outcome return storage. |
| `AssetPackageReader.cs:122-215` | One `ImmutableArray<AssetPackageEntry>.Builder` wrapper owns the exact-capacity entry array before `MoveToImmutable`. The wrapper and array are distinct objects. Transfer adopts that array once; it does not allocate or charge a second entry array. |
| `FoundationCodec.ReadPackageProfile` and its actual callees | Opening also constructs a profile binding with three 32-byte digests, a one-element profile selection/context, and a staged EbinRecord with copied fields/payloads. Dropping `out value` does not prevent `EbinCodec.Stage`. These costs are absent from the simple header/profile/directory subtotal. |

The [bootstrap](m03b-assets-prelease-bootstrap-and-close-closure.md), [pre-lease capacity](m03b-assets-prelease-operation-profile-capacity-addendum.md), and [file-handle](m03b-assets-file-handle-release-acknowledgment.md) proposals correctly identify missing lower authority and receipts. The [file-I/O implementation proposal](m03b-assets-file-io-port-implementation-closure.md) selects one worker and a scratch capacity, but explicitly leaves complete worker/hash/native costs unbounded by the inspected evidence.

The impossibility here is **derivation from the existing contract**, not a claim that every conceivable implementation uses infinite memory:

- The same option tuple fixes neither initialized CLR thread/dispatch storage nor CNG/OpenSSL provider storage. `Thread` stack arguments, a 32-byte digest, package extent, and source shell sizes do not establish upper bounds for those complete graphs.
- An owned HANDLE/descriptor is a capability, not a byte-valued kernel-resource receipt. Existing options contain no bound or accounting channel for its provider/filesystem/kernel backing.
- Even B0 = 0 is a valid option tuple at the public usage boundary. The required pre-canceled or capacity-refused Task can exist with no admitted Store work. Under the earlier rule charging every pre-transfer response shell to B0, that boundary cannot deliver its required response.
- Applying section 3.2's tentative application-protocol exclusion removes the arbitrary caller-graph counterexample. It does **not** turn engine-created thread/hash/Task machinery into positively bounded B0 storage.

Thus no finite function of the existing inputs establishes the earlier complete envelope for this selected ordinary BCL-worker/hash path. More shell tests cannot prove it. Neither a borrowed Scene P, bounded-v1 provision parameters, all remaining B0, nor a guessed native padding closes the gap.

Chapters 02/04/11/30 must review the changed capacity subject. Chapter 02's missing Chapter 11 recovery-delivery definition remains a separate contract gate. Chapter 29 keeps Assets dependent only on Core; Chapter 35 keeps code, managed observations, native/AOT observations, and consumer acceptance distinct.

## 3. Application, response, and substrate boundaries

Apply only the following application-borrow and delivery portions of the existing execution proposal's **tentative section 3.2 decision**, without claiming them accepted:

- Caller-owned token sources, supplied context values/notification bodies, prebuilt registration/schema/codec graphs, caller Task continuations, and application mutation/history of exposed exceptions remain borrowed application graphs.
- Holder fields in source-defined engine owners remain positively charged, even when their target is an excluded framework object or borrowed graph. Engine-created semantic/cache/diagnostic data is not reclassified as application input.
- Public response ownership transfers only at the actual return/delivery boundary; producer interests can survive that transfer.

The present proposal **supersedes**, rather than imports, section 3.2's requirement that engine-created Task/TCS shells before transfer receive a complete positive byte provision. Framework shells are outside this byte subject **both before and after delivery**, regardless of who constructs them. An admitted operation's positive O pays its source control/message capacities, not those shells. Engine-authored source classes, selected data arrays/strings, and explicit native extents still require their own charges. In particular:

| Portion | Proposed A treatment |
|---|---|
| Source-defined engine owner/operation fields, input-holder fields, private fault sample references, explicitly constructed result strings | Source-capacity debit before construction; retained while still owned/usable, including return and failed-cleanup states. |
| Admitted public Task/TCS, CLR async frames, ExecutionContext capture/restoration machinery, EDI/exception implementation storage | Section 5's named framework interests attach to the exact issuing owner/invocation. No Q or other byte provision is asserted for their shells or internals before transfer. Additional source-defined wrappers/queues/holders and selected payloads remain charged. |
| Valid pre-canceled or unadmitted refusal response | The **same framework-shell exclusion** applies, not a separate pre-transfer byte exception. At most one public response is issued by that invocation, with no admitted worker/file/buffer/Store operation or Store-funded waiting queue. Use existing fixed refusal text; this does not authorize fresh source payloads without a claim. Aggregate caller observations remain application protocol. |
| Fresh engine diagnostic/string/array payload | Charge its selected capacity. The substrate exception cannot hide an engine buffer inside a Task, exception, callback, or `Data`. |
| Core process-wide catalogue/spatial registry | Keep existing Core ownership and limits, including the 4,096-profile bound. Do not attribute the entire shared Core registry to each Store or claim its cold/physical allocation qualification from A. Per-opening binding/context/record products below are charged to the opening. |
| Native thread, file/event, hash provider, OS/kernel state | Count exact issued resource interests; no byte amount is asserted for their opaque internals. Original owner, actual-use and final disposition remain mandatory. |

The boundary follows **storage kind and the selected ownership subject**, not whether an allocation expression appears in engine source. A framework Builder shell is excluded; its explicitly selected entry array is not. A framework exception shell is excluded; a fresh engine-authored message is not. A `Lock` object is excluded; its reference slot inside the Store or backing is not. Core's per-opening binding, context, record, fields, and copied digests are source-defined/data products and remain charged below.

This is a capacity-scope amendment, **not** a transfer of arbitrary engine resources into an “application owner.” It does not satisfy the old complete-memory guarantee. It does not permit suppression of ordinary cancellation/context semantics, pruning the General registration, truncating faults, scalar bounded-v1 fault projection, or refund based on Task readiness.

Automatic call-stack storage and CLR-retired objects awaiting collection are not retained source-capacity ownership. Explicit stack/native windows still obey actual-use safety. A surviving engine alias/consumer cannot be declared CLR retirement merely to end its claim.

## 4. Exact source-capacity mechanism for A

Use checked arithmetic and the following accounting unit. **This is a conservative declaration/requested-capacity envelope for the selected managed layout, not a complete physical resource-memory estimate.**

```text
InstanceFields(owner) =
    all storage-bearing instance fields in the source class/base chain,
    including auto-property backing fields, excluding computed properties/statics

W(field) = pointer width for a reference;
           complete managed storage width, including internal padding,
           for an inline value field
FieldCapacity(owner) = sum W(field), field in InstanceFields(owner)

Align8(n) = checked((n + 7) & ~7)
SlotCapacity(owner) = sum Align8(W(field)), field in InstanceFields(owner)
Q(owner) = max(24, Align8(16 + SlotCapacity(owner)))
A<T>(n) = Align8(24 + checked(n * managed element stride of T))
A_reference(n) = Align8(24 + checked(n * pointer width))
Q_string(n) = Align8(16 + 4 + checked(2 * (n + 1))) // length + UTF-16 terminator
Q_native(n) = n                                  // explicitly requested extent
```

The selected x64 layout model uses an object prefix of 16, array prefix of 24, minimum object allocation of 24, and alignment of 8. Q aligns each **top-level instance field**, not each constituent of an embedded struct. Null fields still occupy their declared slots. This conservatively covers inter-field packing rather than guessing the final CLR field order; it is not the sum of measured constructor allocations. Static storage is excluded from Q and receives only the separately enumerated anchor-field claim. Final changed declarations still need their own checks.

For this lane, `Guid`/`SpatialProfileId` are 16-byte inline values; `SpatialSettings` is an 8-byte pair of Int32 values; `CancellationToken` and each `ImmutableArray<T>` are 8-byte inline holders. These are not additional heap owners. `AssetPackageEntry` contains a 4-byte `AssetId`, one byte of codec, three bytes of alignment, and three UInt64 values: its managed array stride is 32. Value-only `EbinDiagnostic`/`EbinReadDiagnostic`, scan state, and record/span views are not separate class allocations. Automatic locals/awaiters remain under section 3's stack/generated-machinery boundary; an explicitly added source holder field must still be counted.

Count each distinct owned array/block once, even if multiple holders alias it. Holder fields themselves still occupy capacity. Empty storage can have zero element capacity but its selected allocation envelope/owner is not zero. **Only the explicitly classified opaque resource terms are outside the new metric; no unknown cost is represented by a zero-byte value.**

Production uses the closed declaration/stride expressions, not reflective allocation probes, GC allocation counters, or caller-provided costs. Any added retained field/storage site changes its claim before materialization. No unused padding or “safety factor” is permitted.

### Proposed A carrier catalogue

The following closes the small A declaration set. Counts are for pointer width 8; the raw field column is not a measured shell size. Q uses the conservative field-slot rule above.

| Owner | Exact storage membership | Raw fields | Q |
|---|---|---:|---:|
| Ordinary Store | Existing six reference fields (including the Lock and disposal/replacement TCS slots), three Int64, three Int32, one Boolean = 85; add issuer, generation-head, operation-head, I/O-control references | 117 | 152 |
| Original issuer | Lock reference; original B0, total charged capacity, next-generation and next-operation Int64; lifecycle Int32. **No Store/backing/operation references.** | 44 | 64 |
| Generation backing | Existing nine references, two inline ImmutableArray holders, two Boolean, five Int32, two Int64, inline ProfileId/Settings = 150; add issuer, next-generation, opening-control and retirement-control references; generation/Keep-claim Int64; selection/return-interest Int32 | 206 | 264 |
| Per-generation `EbinReadLimits` | Five Int32 limits and three Int64 aggregate limits | 44 | 80 |
| Backing token | Source-defined empty reference type; minimum object allocation, not an opaque native resource | 0 | 24 |
| Operation/control | Eight references: issuer, backing, next, input owner, public response, captured context, primary, cleanup; token storage; operation ID, claim, additional-failure count Int64; state, native-use, caller, return-interest Int32 | 112 | 144 |
| Opening storage holder | Header, profile, directory, padding, actual digest, entry-array (not Builder shell), temporary profile binding/context/record references | 72 | 88 |
| Shared anchor fields | Existing head and gate references; first paying issuer reference; initialization Int32; static aligned field slots, no new heap owner | 28 | 32 |

Use the backing's existing reference/release counters; do not introduce a second authoritative reference count. Selection and return/native state are independent. The issuer deliberately cannot root generations through a parked worker; list heads belong to the ordinary Store/active acquisition roots. The final worker implementation must preserve that separation.

The existing Store's field-slot Q is `16 + 6*8 + 3*8 + 3*8 + 8 = 120`; the four added references make 152. The existing backing's Q is `16 + 11*8 + 2*8 + 5*8 + 2*8 + 16 + 8 = 200`; its proposed additions contribute 64, making 264. The two ImmutableArray values account for two of those eleven reference-width fields, not two wrapper classes. Per-generation read limits remain a separate source class: Q is `16 + 5*8 + 3*8 = 80`, even though the packed object measured in the constructor test is 64. The separate token still costs 24. None of these terms includes the separately allocated instance or static `Lock` object.

An operation reserves two original sample slots and source message contents. In the current opening producer, entry-index substitution has at most six digits. The profile-message literal has 48 characters, EbinStatus names at most 19, and the closed Core category-producing source literal set is bounded by 56 characters. Thus the conservative source-message bound is 123 UTF-16 characters. Two simultaneously retained source-produced messages reserve **544 = 2 * Q_string(123)** bytes, including their headers/terminators/alignment. The operation provision is **O = 688 = 144 + 544**, not an exception/EDI/Task graph estimate. New formatted messages require a revised source bound; opaque runtime-produced failure messages remain explicitly outside this byte metric.

The measured 40-byte `Lock` and 24-byte `object` gate shells are **deliberately outside** this selected byte metric, not omissions priced at zero. Section 5 counts each separately from its source holder. `AssetBoundedNativeFile` and `AssetBoundedNativePort`, in contrast, are source-defined classes, not opaque wrappers. Ordinary A constructs neither; a later actual port must derive and claim its final source fields/storage rather than import their current bounded-v1 shell measurements.

### Opening storage closure

Preserve ordinary EPKG2 2.0, native envelope 2.0, General registration, None/Brotli admission, and checksum-before-layout-error precedence. Do not use the two-entry bounded-v1 parser or its path/token/context restrictions.

Before external acquisition reserve the maximum source-owned opening peak:

| Storage | Source-derived capacity |
|---|---:|
| Header | `A<byte>(64) = 88` |
| Profile bytes | `A<byte>(65,536) = 65,560`; payload maximum from `EbinReadLimits.Foundation` |
| Directory bytes | `A<byte>(64 + 32 * 131,072) = 4,194,392` |
| Entry array | `A<AssetPackageEntry>(n)`, `n <= 131,072`, charged in K, not again in Open. One exact-capacity array is funded before Builder construction and adopted once on transfer; the framework Builder shell is a separate opaque interest. |
| Reusable padding array | `A<byte>(15) = 40`; replace per-gap array construction with one opening-owned array |
| Actual whole-package digest | `A<byte>(32) = 56`; expected digest/options remain borrowed caller inputs |
| Profile binding/context/staging peak | 73,104 allocation-envelope bytes, derived below |

The 8-GiB package bound is a file extent, never an 8-GiB allocation. No ordinary option hard ceiling is silently added in A.

The **73,104** profile term is:

```text
Q(FoundationProfileBinding)          88  // three refs, Guid, Settings, three UInt32
Q(FoundationCodecContext)            48  // three refs and one Int32
three copied digests                 3 * A<byte>(32) = 168
one profile-reference array          A_reference(1) = 32
EbinField objects                    64 * Q(EbinField) = 64 * 56
Stage/sort/immutable reference arrays 3 * A_reference(64) = 1,608
copied payload arrays                <= 65,536 + 64*(24 + 7) = 67,520
Q(EbinRecord)                        56  // Guid, two UInt32, immutable-array ref
```

The canonical scan bounds field count at 64 and disjoint payload extents by the profile record size. Each payload array adds its 24-byte prefix and at most seven bytes of allocation alignment; the 67,520 bound is not unexplained padding. One binding and context are constructed; validation can reach at most one staged record, 64 field objects/payload arrays, and three stage/sort/immutable reference arrays. The context's persistence/package-profile fields are null here: it does not construct a `FoundationPersistenceBinding` or `FoundationProfileTuple`. `Stage` allocates fields/payloads even though `ReadPackageProfile` discards `out value`. These selected copies remain charged when `ToArray`/`ImmutableArray.Create` performs them. Hash/sort/formatting machinery has the separate interests below. Do not change protected M02 bytes or rewrite Core codecs for A.

For a concrete Windows-x64 **source-capacity bill**:

```text
O = 688
S = 152 + 64 + O + 65,536 + 32 = 66,472
K(n) = 264 + 80 + 24 + O + A<AssetPackageEntry>(n)
     = 1,080 + 32*n
Open = O + 88 + A<byte>(64) + A<byte>(65,536)
     + A<byte>(4,194,368) + A<byte>(15) + A<byte>(32) + 73,104
     = 4,334,016

cold Create source preclaim = 32 + S + K(131,072) + Open
                            = 8,595,904
```

These formulas remain exact **expressions for the enumerated conservative source-capacity subject**, not exact physical allocation totals. The framework objects excluded above add resource interests, not a Q term or guessed padding. In particular, 8,595,904 is A's cold source preclaim, not the total cold CLR/framework/OS allocation cost.

S earmarks an unstarted I/O/return control, a **65,536-byte explicitly requested native scratch block** (`Q_native`, not a managed byte array), and 32 bytes of OVERLAPPED declaration capacity. These are **unspent source provisions** in A, not an initialized worker. OVERLAPPED is a value/native storage extent, not another object shell or event; if embedded in a later source owner, convert its earmark into that owner's inline-field claim rather than charging both. A managed scratch alternative would need `A<byte>(65,536)`, not the unchanged native-extent term. A later actual worker/port, path copy, or added holder/storage site must receive its own declaration/extent-derived claim from the same B0 **before** materialization. The final source-declaration gate remains closed until those memberships and transfers are derived; this example is not the port's final profile.

This bill intentionally changes admission: B0 = 1 cannot open even an empty package. The default has room for this source subtotal; no conclusion follows about initialized runtime/native memory or successful resource acquisition.

## 5. Resource membership, not resource-to-byte conversion

The selected ordinary lower mechanism remains one package-I/O worker per original Store, shared across its generations. A does not initialize it. Counts below distinguish known object wrappers from **issued call/protocol interests**: one BCL call interest does not mean that call allocates only one object or native handle. Opaque descendants, provider internals, synchronization wait state, and runtime caches remain owned through that interest; their complete physical cardinality/bytes are not qualified. Reserve the following exact source-boundary membership in the issuing controls, including construction prefixes and returning/failed uses:

| Resource | Issuance bound and owning lifetime |
|---|---|
| Store `_gate` | One separately allocated `Lock` per materialized Store, including a failed constructor prefix. The original Store/source-construction owner retains it through every gate use and actual enclosing return. Q includes only its reference slot. A `Lock.Scope` value is not another gate object. |
| Original issuer gate | One separately allocated `Lock` per issuer, not the Store's gate. The scalar bootstrap owns construction until issuer installation; the issuer retains the interest through its last use. The first anchor-paying issuer remains retained for the anchor lifetime. |
| Backing `_gate` | One separately allocated `Lock` per materialized generation backing, including unreturned/failed construction. Opening owns the prefix, then the generation; displaced, leased, closing, failed, and returning generations retain their own distinct gate interests. |
| `s_failedReleaseRootGate` | Exactly one shared `Lock` per loaded backing-type/static-anchor instance (one in this lane), not one per backing or a portion of the 32-byte static field claim. Elect its construction interest before allocating type initialization; the installed anchor/first original issuer retains it for the selected anchor's process lifetime. Later issuers borrow that established interest; independently loaded Assets copies do not share the anchor or an aggregate budget. |
| Store disposal TCS/Task | One TCS object **and one distinct associated Task** per ordinary Store. All `_disposeCompletion.Task` observations alias that Task. Store disposal/control owns publication, fault, and return; the pair remains retained while the Store still holds it or a producer/caller uses it, not merely until it becomes terminal. |
| Replacement barrier TCS/Task | One TCS/associated-Task pair per elected candidate operation. Its own candidate/return control retains the pair through `TrySetResult` and actual enclosing return. Single-candidate election bounds the active body, not the number of earlier returning pairs; each remains represented in original debt. |
| Public response and opening result | At most one public response interest per invocation: current Create/Replace return their private async method's Task, not another facade Task; each `OpenAsync` issues one separate internal result Task. `DisposeAsync` issues a ValueTask value per invocation, not necessarily a new Task object; its shared completion Task is the pair above. Issuing invocation/operation owns each producer suffix. Delivery transfers application observation only. Unadmitted invocation responses have no Store-work membership; there is no asserted Store-wide bound on arbitrary caller observations. |
| Shared framework borrows | Each `Task.CompletedTask` access supplies one borrowed Task interest to its invocation (at most one such access per current pre-canceled helper or Dispose invocation). Do not assume every access returns one process-wide object. Framework async, enum, comparer, hashing, and I/O caches remain runtime-owned; their borrows stay attached to the counted source call sites. Cold cache initialization is excluded substrate, not a zero-cost source allocation or a newly funded Store service. |
| Entry Builder | One `ImmutableArray<AssetPackageEntry>.Builder` wrapper per opening. Opening owns it from construction through transfer/failure and actual call return. Its exact-capacity entry array is already funded in K; `MoveToImmutable` transfers that array without duplicating it. A replacement opening has its own wrapper, even for identical entries. |
| FileStream/file acquisition and close | One FileStream construction attempt per exact generation/opening. Opening owns constructor prefixes, then generation owns the returned stream through all reads and final close/return. At most one opening-failure `DisposeAsync` call or one final backing `Dispose` call is issued for that stream. Its strategy, SafeFileHandle/finalization and OS/filesystem descendants remain opaque; an unreturned constructor or a completed Dispose call is not a proved native disposition. |
| Hash providers | One whole-package `SHA256.HashDataAsync` call per opening, plus one settings `SHA256.HashData` call per constructed profile binding. These are **two distinct interests**, not one inferred from digest size. Their owners are opening and its binding-construction invocation; provider prefix, failure, retirement, and actual return remain covered. The returned/copied digest arrays are separately charged in Open/profile storage. The later serialized worker allows one active whole-package hash, but readiness alone cannot retire an earlier hash return interest. |
| Profile sort/format substrate | At most one `Array.Sort` call per staged record; its comparison-call-site/cache interest is shared after initialization and retained by the runtime for its cache lifetime. Each fresh source-formatted failure message has one formatting-call interest owned by its producing operation; the two reserved source messages still cost 544. Comparer/formatter implementation workspaces and generated cache/closure shells are excluded substrate, not omitted selected data arrays. |
| Read/padding async substrate | For entry count n, Open issues three fixed `ReadExactlyAtAsync` calls and at most `n + 2` padding-helper invocations. If p of those gaps are nonzero, there are `3 + p` extent-read invocations. Each nonempty L-byte extent issues at most L `RandomAccess.ReadAsync` attempts: positive reads advance, and zero terminates with EOF. Each helper/read/capture/continuation interest belongs to that exact opening/extent invocation through actual return. ValueTask/awaiter values are not themselves heap shells; any generated frame, Task/source or I/O state is excluded substrate. The later port must reuse its funded extent control only after all prior use/return interests end, not create a wrapper per short read. |
| Context and fault substrate | Each async/context application is attached to its exact counted invocation, including capture/restoration/notification and dispatch return. Caller context values remain borrowed. Each O reserves two source sample-reference slots; original exception/EDI storage and framework-added descendants remain separate fault-protocol interests. Current explicit EDI captures occur at most once per Replace, owning Dispose, or release-failure rethrow invocation; repeated invocations are not one indefinitely reusable return interest. Extra source fault arrays/holders require their own positive claims. |
| Worker execution/startup/termination | Reserve one worker/start/stop/termination protocol and at most one BCL Thread wrapper per original issuer; materialized worker count in A is zero. Its source control and unspent native scratch are in S. Thread/delegate/dispatch/runtime/native descendants remain with that protocol through partial start, idle use, stop and actual termination/return. No `Task.Run`, pool, or extra restart worker. |
| Native event and read use | The later Windows worker issues one event and at most one active native read using its single scratch/OVERLAPPED storage, never one event per generation or short read. Linux has no Windows-event interest. Event close, uncertain read completion, allocator/provider/OS state and finalizer handoff retain their original worker/generation interests until actual disposition and return. A installs none of these resources. |

For a finite B0, positive K/O claims bound generations and outstanding source controls by remaining capacity. Count queued, running, ready-unconsumed, stopped-failed, and returning membership. `MaximumLiveAssetLeases + 1` is not that bound: inspections and temporary-reference refusal can have no issued lease credit.

These interests cannot be collapsed into a generic “one operation” entry: in a normal first Create there are the distinct Store/backing/shared-anchor gates, the proposed issuer gate, the disposal TCS/Task pair, public/internal result protocols, Builder, file and two hash protocols. Their byte exclusion does not remove their owners or life cycles. A finite source bill is neither an exact descendant-object inventory nor proof of kernel handle count/disposition. The current BCL path remains unqualified on those axes. Direct native cardinality, partial startup, hash/provider retirement, finalizer handoff, and actual resource/caller return still need the later port's closed implementation and target evidence.

## 6. One original ledger across all generations

```text
live wire debit + S + shared-anchor attribution
    + sum unique K(g)
    + sum live/unspent opening and operation source claims
    <= original B0
```

Keep the current backing-sensitive wire, concurrent-decode, and lease checks independently, including `min(backing.CpuAssetBudgetBytes, originalB0)`. Source claims reduce original wire headroom; smaller applicable replacement limits remain constraints. Do not debit a P-funded physical allocation again as an Assets allocation. Existing wire counters remain a quota proxy, not measurement of arbitrary codec values.

| Transition | Required A accounting |
|---|---|
| Create | Usage/pre-cancellation first; scalar source preclaim before owner materialization. Construct unpublished Store/backing and install the exact acquisition root before file/worker/hash work. Constructor failure has the same original issuer, not a new emergency owner. |
| Candidate admission | Under the existing single-candidate election, claim `K(max entries) + Open` in addition to **all** existing claims before constructing candidate/completion storage. Refusal leaves current selection intact. |
| Successful publication | Atomic current switch changes selection only. Former current becomes displaced, then lease/temporary-use retained if applicable. It is not refunded. Equal package bytes/digests do not merge generation identity. |
| Failed opening/candidate | Preserve original issuer, acquired prefix, source storage, faults, and return interests even if no backing/Store is returned. Unresolved pending-release refusal remains. |
| Native/file consumption | Record its actual disposition separately. Consumption does not discharge metadata, cleanup, fault, accounting, or return claims. Unknown native disposition is never an `Ended` fact. |
| Returning generation/operation | Leave its charge unavailable until actual owned uses and enclosing calls end or an exact funded subset transfers. Task readiness, replacement-barrier completion, and a body-local flag are insufficient. |
| Ended storage | Return only the capacity whose access/ownership actually ended. Keep or transfer read-limit/token/metadata subsets retained by descriptors or semantic values. No normal-success permanent quarantine. |
| Store disposal | Close new admission and release only Store ownership; old leases/operations retain original issuer and their grants. Disposal never stops their required worker early. |

Exact generation IDs and operation IDs are checked nonwrapping issuer-local counters. Intrusive links are inside charged owners; no growing recovery list allocation. Reserved-to-materialized transitions do not create entitlement.

For the shared failed/acquisition anchor, prevent allocating backing-type initialization before scalar preclaim/resource-interest election; do not remove or silently merge its separate `Lock`. Cold activation charges **only its 32 aligned source field bytes** to the first original issuer and separately installs the single opaque gate-construction interest. Both persist for the anchor's process lifetime; that issuer's own 64-byte source envelope remains paid too. This is one finite standing attribution, not retention of every successful Store. Other Stores borrow the same established anchor fields/gate but pay their own linked owners. The anchor keeps no public Store reference and never pays generation debt. Before independent first-issuer transfer succeeds, scalar bootstrap owns the claim and construction interest; failed construction cannot silently donate an unpaid source anchor or assert an opaque prefix consumed.

The code must expose separate source-storage, actual-resource, and caller-return facts. It must not manufacture native completion or dispatcher return to make the accounting look reclaimed. A's model can be implemented while those execution gates remain unavailable.

## 7. Exact implementation boundary and ordered plan

**A implements the original issuer, source-capacity provision, and unique-generation ledger. It does not implement or activate the worker/native port, rewrite Scene/planner execution, or qualify typed codecs.** The earlier ordinary path is existing unqualified behavior, not a fallback from a refused selected native/profiled generation.

| Order | Owner files | Deliverable and focused tests |
|---|---|---|
| 1 | New `sources\TerraFX.Assets\AssetStore.OriginalOwnership.cs`; `AssetStore.cs` | Implement the closed source-capacity expressions, issuer-only scalar account, nonwrapping IDs, reserved/materialized/returning/debt claims, and intrusive membership. Keep the original budget field and entry fronts. Extend `AssetStoreAllocationTests.cs` to distinguish source capacity from actual shell/array allocation and corroborate final strides/fields; do not assert a worker/native bound. |
| 2 | `AssetStore.cs`, `AssetPackageBacking.cs` | Move issuance to the unpublished acquisition-free Store, attach original generation/close provisions, and fund/install the existing anchor before acquisition. Integrate candidate election and aggregate admission. `AssetStoreTests.cs`: positive tiny-budget refusal before a missing path is opened; exact-fit/one-byte-short source admission; held old package generations plus a larger replacement budget cannot refinance debt. No test-only issuer or mutated private counters. |
| 3 | `AssetPackageReader.cs`; opening storage/control in the same Assets owner area | Pass the installed owner through the existing reader; reserve complete source metadata/profile peak before its allocation, reuse one padding array, adopt actual entry capacity once, and retain failed acquisition prefixes. Preserve checksum/layout/None/Brotli behavior. Update direct reader tests to use real production issuance rather than an ownerless alternate opening route. Reuse literal package/profile fixtures. |
| 4 | `AssetStore.cs`, `AssetPackageBacking.cs`, `AssetLeaseLifetime.cs`, `AssetPackageLease.cs`, `AssetStore.PackagePin.cs` | Link real lease-retained/displaced/failed/returning generations to the same issuer; preserve carrier-before-reference acquisition and current release order. Install caller-side source handback at ordinary returned calls where proved. An unproved native/async suffix remains unavailable for profile activation, not a synthetic return acknowledgment. `AssetPackageLeaseTests.cs` and existing Store tests cover old digest/value usability, repeated close/no double return, and independent candidate/current debt. |
| 5 | Existing Assets test files above; `AssetStoreEntryBoundaryTests.cs` | Revalidate entry precedence, original token/fault identity, source admission, checksum-before-layout errors, retained generations, and original wire/decode/count restrictions together. Rework wire-only exact-fit fixtures to exercise the remaining wire headroom; merely increasing their budgets must not erase the original-budget regression. Record source-only results and unexecuted native/return branches distinctly. |

Ordinary generic/custom codecs remain ordinary registrations, not newly qualified Scene codecs. Full load/inspection/temporary-reference operation allocation closure belongs to the subsequent read batch; A does not infer it from O or existing wire/decode counters. Likewise, metadata surviving a described-read owner needs its real installed interest before the selected read route can activate.

The new capacity-subject decision tentatively permits these code/test changes for progress. **Contract review still must disposition the changed B0 subject, framework-shell/invocation-response scope, recovery delivery, and exact final declaration bytes.** No maintained design chapter is changed by this proposal or implicitly deemed accepted.

Use `global.json`, central packages, current analyzers, and existing NUnit/VSTest outputs. After one coherent A implementation, run:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetStoreAllocationTests|FullyQualifiedName~AssetStoreEntryBoundaryTests|FullyQualifiedName~AssetStoreTests|FullyQualifiedName~AssetPackageReaderTests|FullyQualifiedName~AssetPackageLeaseTests"
```

Run that same selection in Release after Debug. Confirm new cases actually execute and retain current TRX/exits under `artifacts\tst\<Configuration>`. Do not run Runtime/native/pixel consumers merely to hit a still-closed Scene guard.

## 8. Target and remaining gates

The numerical example uses **Windows x64, CoreCLR .NET `11.0.0-rc.3.26522.148`, unified-source revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`**, with the SDK selected by `global.json`. Local CoreLib SHA-256 is `df53885902e896a2b40988237cbe9cbb9f1bb0743200f05fe9257c56a29c5732`. This identifies a managed declaration/reference lane, not a loaded native consumer or a second SDK pin.

**No target-specific all-in worker/hash/native byte profile is viable from the inspected evidence, and none is selected.** The preliminary A managed-layout model is only the exact Windows-x64 CoreCLR lane above. A selection lacking that declared model fails closed before acquisition; do not copy these constants to another target or fall back to the old reader after refusal. The selected worker/port also remains fail-closed on every unqualified target. Windows arm64, Linux x64/arm64, NativeAOT, and Runtime Async require their own applicable source/generated/native/return evidence.

Still closed:

- Final source-declaration/extent coverage and scope review: rederive Q/strides for the implemented A carriers and every added source array/string/block, including any Builder holder, fault-delivery array, path copy, or later worker/port storage. A framework-shell exclusion cannot supply a missing source charge; unresolved membership keeps that materialization unavailable rather than receiving padding.
- Chapter 02/11 recovery and scope review; full ordinary-worker acquisition/start/hash/close/finalizer/caller-return closure.
- Ordinary typed-read operation provision and original P/planner/cache/receiver/context/publication closure.
- Scene `QualifiedFootprint`, bounded-v1 typed validation and CLI activation.
- Nonempty restore, Headless/native Scene-dependent rendering, positive native diagnostics, and public consumer/AOT qualification under Chapter 35.

This proposal supplies a finite implementable **accounting subject and A source slice**, not evidence that the old complete-memory promise has been achieved. Native resources remain genuinely owned; their unknown byte costs are expressly outside the newly proposed metric, and their unknown disposition/return is still a gate.

## 9. Bounded input freeze and preservation

Freeze UTC **`2026-11-21T07:58:50.991776Z`**, HEAD above. The input set below excludes this output; the reviewed pre-revision proposal raw SHA-256 was `549898302be0c6deffe14992c1b5d82246c3d9dca9331782d300a2ed14b66db6`. Only this proposal is revised. No source, tests, maintained contracts, other proposals, session artifacts, build outputs, or remote state are changed. No build, tests, allocation/native probe, commit, or publication is performed for this document. Declaration arithmetic and the preserved input hashes were checked before applying this scoped disposition.

Group hashes use ordinal-sorted `relative-Windows-path<TAB>lowercase raw SHA-256` lines, LF-joined, UTF-8 without BOM/trailing newline; ALL is their unique union. Before implementation disposition, rehash the exact set and preserved dirty inputs; changed bytes require scoped reassessment.

| Group | Members | SHA-256 |
|---|---:|---|
| Authority | 8 | `c6064513387eb52fe3638d13db30563f3ce224566041a580a36d734d65ee83e3` |
| Proposals | 6 | `e08b832928961b6963397655840e06e9b3365dd5efbc0db84d3bef2bd3646df4` |
| Build | 11 | `e4144a702114963f380246bdc01906109b0dfe60b305253d1d5bc8f95aaa7477` |
| Assets | 22 | `6d7201a78d6401c94a0fac609995188b4e0c0d5c291f881f90628a4d59ab197c` |
| Foundation | 18 | `8c760b580ced9b219c3c05e4af6db58b615bfd60404a838f1ada0bee4a56a5d5` |
| Runtime | 2 | `1de33d0bab742ea8851089edf6c46e20a412807aafa5249a03959a19b3c1b478` |
| Tests | 5 | `10498e07c4ddc5531d9249633b35f273fed45c5c947d8647aebff7e9a55ac2f3` |
| Schema | 2 | `e2b69ad23a59f5ad02e1d25f1714f9c90b2595f20b797bd5a6a19eec8f309f1f` |
| Protected | 5 | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |
| ALL | 79 | `e2c51c00da6c4d67d57342d313ddf95dfb5d994b0af9ec6536e07fd85eca404d` |

```text
[Authority: docs\design\]
02-dotnet-api-memory-and-ownership.md
04-capacity-and-workload-profiles.md
11-assets-import-streaming-and-packages.md
29-project-setup-build-and-deployment.md
30-public-api-review-surface.md
35-implementation-roadmap.md
[Authority: docs\project\]
Roadmap.md, Building.md
[Proposals: docs\proposals\]
m03b-scene-original-store-execution-closure.md
m03b-assets-prelease-bootstrap-and-close-closure.md
m03b-assets-prelease-operation-profile-capacity-addendum.md
m03b-assets-prelease-recovery-owner.md
m03b-assets-file-handle-release-acknowledgment.md
m03b-assets-file-io-port-implementation-closure.md
[Build]
.github\copilot-instructions.md, .github\workflows\ci.yml, .editorconfig
global.json, Directory.Build.props, Directory.Build.targets, Directory.Packages.props
sources\Directory.Build.props, sources\TerraFX.Assets\TerraFX.Assets.csproj
tests\Directory.Build.props, tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj
[Assets: sources\TerraFX.Assets\]
AssetStore.cs, AssetStoreOptions.cs, AssetPackageReader.cs, AssetPackageBacking.cs, AssetId.cs
AssetPackageEntry.cs, AssetPackageBackingToken.cs, AssetStore.PackageLease.cs, AssetStore.ReadDescriptor.cs
AssetStore.PackagePin.cs, AssetOwnedReadDescriptor.cs, AssetStoreRegistration.cs
AssetLease.cs, AssetLeaseLifetime.cs, AssetPackageLease.cs, AssetPayloadReader.cs
AssetStore.Bounded.cs, AssetStoreBoundedProvision.cs, AssetStoreBoundedBootstrap.cs
AssetBoundedNativePort.cs, AssetBoundedNativeFile.cs, AssetBoundedNativeApi.cs
[Foundation: sources\TerraFX.Core\]
EbinReadLimits.cs, FoundationCodec.cs, FoundationCodecContext.cs
FoundationProfileBinding.cs, SpatialProfileId.cs, SpatialSettings.cs, SpatialProfiles.cs
FoundationProfileTuple.cs, FoundationPersistenceBinding.cs
EbinCodec.cs, EbinField.cs, EbinRecord.cs, EbinScanState.cs, EbinStructuralReader.cs
EbinStatus.cs, EbinReadDiagnostic.cs, EbinDiagnostic.cs, EbinRecordView.cs
[Runtime: sources\TerraFX.Runtime\Scenes\]
PreparedScene.cs, PreparedScene.Preparation.cs
[Tests: tests\TerraFX.Assets.Tests\]
AssetStoreAllocationTests.cs, AssetStoreEntryBoundaryTests.cs, AssetStoreTests.cs
AssetPackageReaderTests.cs, AssetPackageLeaseTests.cs
[Schema]
sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
[Protected: tests\TerraFX.Runtime.Tests\Scenes\]
PreparedSceneAssetRetentionTests.cs, RenderSpriteCapacityTests.cs
RenderSpriteComponentTests.cs, RendererExtractionTests.cs
SceneAssetStoreTests.EngineHost.cs
```

The existing execution proposal raw SHA-256 is `75f444657f6d58dce7b5a7023caaf984dabbf554c0b62cc1d6d225c2561936d7`. Schema hashes remain:

```text
6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a  NativeAssetSchemaCatalogue.cs
cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a  SceneSchemaCatalogue.cs
```

The five pre-existing dirty files retain these raw hashes:

```text
e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4  PreparedSceneAssetRetentionTests.cs
caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239  RenderSpriteCapacityTests.cs
8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60  RenderSpriteComponentTests.cs
abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a  RendererExtractionTests.cs
4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039  SceneAssetStoreTests.EngineHost.cs
```
