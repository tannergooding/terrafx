# Capacities, defaults, and workload profiles

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

Powers of two are the default for storage capacities, rings, grids, subdivision, page classes, and resource budgets where they simplify allocation/indexing and compose cleanly. They are not a reason to replace SI constants, supported native rates, real durations, mathematical tolerances, or representative benchmark sizes.

## 1. Selection and exceptions

Choose a named frozen limit, checked arithmetic, and an observable full-state response. A capacity is not a promise that the implementation sustains that much simultaneous work on arbitrary hardware. Memory reservations include metadata, padding, dependent leases, staging, histories, and in-flight work, not just logical payload.

Changing a limit updates all API options, validators, format budgets, scenarios, endpoint tests, and manifests. No subsystem silently rounds a user's capacity upward without recording the admitted value. A ring may round its private backing allocation to a power of two while retaining the exact public credit limit.

| Category | Rule |
|---|---|
| Slots/rings/tables | Prefer exact power-of-two hard/default capacities; grow only at admitted noncritical boundaries |
| Chunk/grid/tree | Exact power-of-two cell widths and bounded subdivision; sparse occupancy does not allocate the theoretical domain |
| Byte budgets | Powers of two with explicit byte units; reserve before allocation |
| Schema/native layouts | Exact specified strides/headers/alignment; do not pad a `Vector3` merely to make 12 into 16 |
| Rates/durations | Preserve 60-Hz timing, 48-kHz audio, supported native rates, and real deadlines |
| Solver work | Twelve velocity iterations remain a deliberate tuning/work split, not a storage capacity; qualify rather than numerologically changing them |
| Workload counts | Benchmarks may deliberately use 100000 entities or 1000 ticks; these are measurement fixtures, not hard limits |

The default 60-Hz simulation rate supports familiar frame pacing and exact rational clock accounting. Twelve solver velocity iterations and four position iterations are bounded tuning choices; factorization makes work grouping convenient but proves no convergence. Pi and physical standards retain their mathematical meanings. The game's twelvefold progression is not an engine allocation law.

## 2. Binding defaults

| Surface | Default / ceiling |
|---|---|
| Entities / archetypes / queued commands | 1048576 / 4096 / 65536 |
| Scene entities / prefab expansion / retained occurrences | 1048576 / 65536 / 1048576 |
| Draw instances / particles / UI nodes | 131072 / 131072 / 131072 |
| Cameras / lights / bone matrices per frame | 16 / 256 / 2048 |
| Presentation snapshot queue | 4 pending snapshots; existing lossy replacement policy applies only to unconsumed snapshots |
| Native backbuffers / frames in flight | 3 where the presentation profile specifies triple buffering; this deliberate latency/lifetime choice is not a ring-storage requirement |
| Audio voice / streaming / bus / command credits | 256 / 32 / 64 / 1024 |
| Audio tail slots / retirement backing ring | 32 / 512; at most 288 live voice/tail retirement records |
| Asset worker channel / process capture | 128 tasks / 8 MiB raw captured bytes |
| Trusted package entries / dependencies / index | 131072 / 524288 / 64 MiB |
| CPU asset / simultaneous physics memory | 512 MiB / 512 MiB |
| World scheduling: registration / graph / work plans / command staging | 16 MiB / 8 MiB / 64 MiB / 32 MiB |
| World identity assignments / retained branches | 33554432 / 1024 |
| World allocated entity slots, including retired / active scene instances | 2097152 / 65536 |
| World authoritative Entity-reference positions | 4194304 |
| World scheduling: shared World state / diagnostics | 2 GiB / 8 MiB |
| World resource and system state subceiling | 128 MiB, also constrained by remaining shared credits |
| World snapshot bytes | 2147483584 |
| Aggregate World/persistence admission domain | 8 GiB |
| World scheduling conflict comparisons | 16777216 |
| Spatial cell references per ordinary proxy | 64; wider proxies use the explicit wide-proxy tier |
| Registered spatial profiles per process | 4096 including the fixed Default; immutable registrations cannot be evicted/rebound |
| Spatial traversal / candidate budget per query | 65536 nodes / 1048576 candidates unless a stricter subsystem limit applies |

Other existing power-of-two hard limits remain in their owning chapters. In particular, physics collider/body/joint/contact/mesh/predicate budgets and input map/event/command credits do not rise incidentally. Native texture limits are hardware admission minima, not allocations the engine must make.

`WorldSchedulingLimits.Default` sets the scheduler byte budgets above and a conflict-comparison ceiling of 16777216; the sum of the byte budgets is 2176 MiB, not a total host/process-memory limit. `MaximumOwnedStateBytes` is the shared World-state ceiling, including identity history and indexes, entity storage, scene provenance, resource/system state, control metadata and their admitted transition peaks. The existing seven-argument constructor remains; a World may lower each configured byte budget to an exact value from 4096 bytes through its ceiling and the comparison budget to any value from zero through its ceiling. `MaximumResourceAndSystemStateBytes` is derived as `min(128 MiB, MaximumOwnedStateBytes)`. Admission uses configured exact values without rounding them upward.

Deferred subtree destruction adds an ordered receipt vector to the existing command result. Each `WorldEntityIdentity` contains four GUIDs, or 64 bytes of logical identity payload. One million entries are 64,000,000 bytes (about 61.04 MiB); the 1,048,576 live-entity ceiling would produce exactly 64 MiB of identity payload, already twice the default 32-MiB command-staging budget before expansion metadata, headers or other reservations. Preapplication preparation charges the vector to command staging plus destruction metadata (`CMD+D`); the prepared/published receipt charges its actual retained copy to owned World state, work-plan and destruction metadata (`O+W+D`). Reserve the complete simultaneous transfer peak before installation and refuse the whole operation when it does not fit; neither one submitted command nor the entity-count ceiling promises a fixed subtree receipt size.

The World identity limits are cumulative and exact: the 33554432 assignment ceiling includes every Entity and top-level scene-instance identity, live or retired; nested scene occurrences are provenance, not separate World assignments. The 2097152 entity-slot ceiling includes permanently retired slots, and retirement never requires another assignment. The 1048576 occurrence ceiling includes each root occurrence; the 65536 scene-instance ceiling counts active instances. The 4194304 reference-position limit includes optional null leaves. These count limits are subordinate to byte admission and do not promise that every maximum can be combined.

The 2-GiB shared World-state envelope has these qualification reservations:

| Reservation | MiB |
|---|---:|
| Resident identity ledger, exact index, reverse index and branch directory | 1152 |
| Entity slots, columns and attachments, including admitted transition storage | 256 |
| Scenes registry and provenance | 128 |
| Resource/system instances, authoritative state and derived caches | 128 |
| Other World-owned control and lease metadata | 64 |
| Subtotal | 1728 |
| Remaining shared admission credits | 320 |

World frame-cohort replacement uses the existing World shared-state and 8-GiB admission domains; it adds no global allocator or unmeasured per-object footprint. Before source leases or participant checks, reserve bounded source-use, diagnostic and scan scratch, then reserve the complete simultaneous old/current + prepared/new + scratch + retained-reader/pin + receipt/cleanup/retirement peak before replacement allocation or preparation callbacks. Charge actual backing, alignment, metadata and independent output/task leases through their real owners. A refusal leaves current caches live; old outputs remain independently charged until actual last use, and stopped-but-unreleased participant cleanup remains charged in quarantine. The 65,536 retained-control limit does not promise storage for that many simultaneous old cohorts; a replacement may explicitly refuse while earlier ownership remains live.

Up to 272 MiB of the remaining shared credits may be reserved for identity-index preflight; the separate 32-MiB command-staging category retains command payloads, keys, tokens and sorting. The 8-GiB World/persistence admission domain charges active, candidate, retiring and quarantined ownership; source and transformed records; preparation/replay scratch; outstanding remaps; and domain metadata until actual consumers release them. These are proposed measurable envelopes, not measured object footprints or a promise that all subsystem maxima fit simultaneously.

The default scheduler profile admits at most 4096 systems and 4096 resources, 256 access records per system and 65536 globally, and 65536 Before/After references globally across both lists. A dependency list contains at most `min(4095, knownSystemCount - 1)` entries. Component/tag work plans reserve for up to 1048576 live entities, including the capacity-one case; they are not bounded by the number of full 64-row chunks. These count limits are additionally constrained by actual byte reservations, and lowering a byte budget may reduce the admitted counts.

Scheduler reservations charge actual owned metadata and padding, canonical copies, indices, current and candidate plans, scratch, command payloads/tokens/sequences/sort results, system/resource state, diagnostics and in-flight work before owner or live storage allocation. At maximum capacity, two component/tag plans each reserve for 1048576 entries. Conflict work is checked before pair enumeration; the profile admits at most 16777216 comparisons. Count-cell estimates are not substitutes for actual byte accounting, and aggregate host/process admission remains separate from each World limit.

The system-factory freshness ledger has a qualification target of 131072 remembered live object identities and 16 MiB of actual ledger metadata. Admission must reserve before factory execution and cannot evict an identity while its object remains reachable. These ceilings are not qualified until the implementation accounts for object/weak-handle storage, buckets, indices, sweeping, staging and quarantine retention on the selected runtime; no runtime memory guarantee follows from the target counts alone.

The queue/backing-ring changes are intentional compatibility/profile revisions. Three native presentation images do not imply only three pending CPU snapshots. Audio retirement storage may contain at most one record per each of 256 voices and 32 tails; a 512-slot ring still returns only those 288 semantic slot credits and cannot fabricate extra playable voices.

## 3. Budget composition and failure

Request admission validates counts, checked byte products, aggregate outstanding credits, and subsystem/world/host budgets before mutation. A local capacity reduction cannot violate a dependent minimum (for example, retirement acknowledgement storage for all admitted voice/tail slots).

Resource pressure chooses only explicitly evictable/unpinned versions or authored observational LOD/work policies. It does not drop reliable input/commands/contact events, free borrowed storage, skip required collision neighbors, silently reduce physics fidelity, or claim a partial asset is ready.

Limits/settings/profile identity appear in diagnostics, cook/cache keys where relevant, save/replay manifests, and performance results. Boundary qualification exercises zero where admitted, the exact ceiling, and one beyond; floating ranges test the nearest representable outside value, not an arbitrary distant failure.

The editor shows requested/admitted/used/pinned/staged/in-flight counts and bytes. A maximum-cost request can be identified before it produces a frame-time spike. Performance targets are measured with named hardware, content, configuration, runtime, and revision; no power-of-two choice is itself certified faster.

The domain-local `LineageMembershipAccounting1` metadata candidate is a sub-budget of the existing 8-GiB World/persistence envelope, not an added entitlement. Per domain, inclusive ceilings are 4096 registered/prospective roots (M), 262144 distinct live/reserved keys (K), 1048576 root-to-key edges (E), 65536 independent root/operation interests (I), 4096 pending start-new reservations (R), and 66561 raw lineage references per root; total charged registry metadata B is `min(134217728, domain.MaximumBytes)`. Root, source/snapshot, candidate, retiring, quarantined and prospective state counts before allocation. No public option raises these ceilings, and count maxima do not imply joint byte fit.

Reserve 256 bytes per root cell (including its cleanup/quarantine control), 128 per interest/operation cell, 256 per reservation cell, 64 per shared full-GUID index slot, and 16 bytes per root-vector capacity entry. The power-of-two index capacity is at least 16, at most 524288, and at most half occupied. Every separately allocated array/buffer is charged `ArrayCharge(capacity, elementBytes) = 64 + RoundUp16(checked(capacity * elementBytes))`, where `RoundUp16(n)` is the checked least multiple of 16 greater than or equal to nonnegative `n` (and zero for zero). Per-cell reservations include each cell's own header/fields; they are not added a second time within the cell. Charge whole array pages, spare capacities and separately owned controls/wrappers/Tasks/backing arrays/sort buffers when not covered by a named cell. Each domain first reserves its fixed 4096-byte registrar control. A default-domain handoff additionally reserves its fixed 256-byte ticket in the domain control accounting and one separate H interest cell; it creates no second owner credit.

Each named domain interest occupies one distinct 128-byte interest/operation cell. Every default-domain handoff also charges its fixed 256-byte ticket to that domain's control accounting; the ticket is not free merely because it is covered by the registrar's fixed 4096-byte control, and it creates no second close-owner credit. Carrier fields and retention nodes reside in prepaid, qualified controls/cells. Separately allocated controls, wrappers, Tasks and backing are charged before allocation under the existing domain formula; a process cell's reference to a domain-owned control does not make that control free. These additions change neither domain M/K/E/I/R/B ceilings nor process K/O/P/U/B ceilings.

For each domain, the simultaneous accounting is:

```text
RegistryCharge =
  4096-byte registrar control
  + sum ArrayCharge(root-page capacity, 256)
  + sum ArrayCharge(interest-page capacity, 128)
  + sum ArrayCharge(reservation-page capacity, 256)
  + sum ArrayCharge(shared-index capacity, 64)
  + sum ArrayCharge(root GUID-vector capacity, 16)
  + separately owned controls/wrappers not covered by the named cells
  + owned extraction/sort/commit scratch
  + unspent prospective allocation grants.

DataCharge =
  actual active/candidate/retiring/quarantined World storage
  + source/transformed/snapshot/recording bytes and scratch
  + maps, asset/version/source pins and their operational ownership
  + actual data-consumer/use credits under the accepted owner accounting.

RegistryCharge <= B.
RegistryCharge + DataCharge + all other domain-owned credits <= domain.MaximumBytes.
```

Growth reserves complete old-plus-new peak, candidate vectors and scratch before allocation; old storage remains charged until actual release. Spending converts a grant to allocated charge rather than double-counting the same allocation. Failure preserves old membership and ownership, and quarantine keeps the existing charged credit; no capacity is recovered by dropping a lineage key.

### M03-A process-wide WorldGuid registry accounting

`WorldGuidRegistryAccounting1` is a separate, fixed process-wide bound for active/reserved WorldGuid keys and their operational binding, preparation and use controls. It is not part of any `WorldAdmissionDomain.MaximumBytes`, the domain-local `LineageMembershipAccounting1` M/K/E/I/R/B envelope, Core OwnerId issuance, canonical state, or caller-configurable profile. Count and byte limits are inclusive; checked admission occurs before any associated allocation or identity issuance.

| Process resource | Fixed ceiling |
|---|---:|
| K: distinct retained/prospective WorldGuid keys | 16,384 |
| O: binding-generation cells | 65,536 |
| P: construction/preparation/claim cells | 32,768 |
| U: independent/prospective operational-use cells | 131,072 |
| Claims touching one key / one exact target binding | 16 / 16 |
| G: concurrent pending slab-allocation tickets | 16 |
| B: charged registry metadata | 67,108,864 bytes |
| Separately allocated algorithm scratch | 0 |

The index is fixed at 32,768 32-byte buckets, at most half occupied, with full-GUID equality, stable key-record slots, backward-shift deletion, and no resizing or rehash scratch. K/O/P/U backing uses fixed 256-cell slabs and fixed directories; generations never wrap and exhausted cells retire. Every separately allocated array uses the existing checked `ArrayCharge(capacity, stride) = 64 + RoundUp16(checked(capacity * stride))` rule. The complete declared backing/control charge is 36,773,696 bytes; the cold reservation is 1,060,672 bytes, leaving 30,335,168 bytes below B that does not authorize undeclared allocation. The control charge includes bootstrap state and the 16 embedded growth tickets. Sixteen largest pending slab grants total 1,049,600 bytes but occupy already reserved page ordinals and do not add pages beyond the complete-layout charge. Installed empty slabs remain charged at high water; quarantine reuses preowned cells.

| Declared backing/control | Capacity and stride | Charged bytes |
|---|---:|---:|
| Control, bootstrap and 16 embedded growth tickets | Fixed | 4,096 |
| Full-GUID index | 32,768 x 32 | 1,048,640 |
| Four fixed directories | Fixed | 7,936 |
| K slabs | 64 slabs x 256 cells x 128 bytes | 2,101,248 |
| O slabs | 256 slabs x 256 cells x 128 bytes | 8,404,992 |
| P slabs | 128 slabs x 256 cells x 256 bytes | 8,396,800 |
| U slabs | 512 slabs x 256 cells x 128 bytes | 16,809,984 |
| **Full declared layout** |  | **36,773,696** |
| **Cold control/index/directories** |  | **1,060,672** |

| Fixed route | Process K/O/P/U | Newly prepaid domain interests |
|---|---:|---|
| Fresh World construction | 1/1/1/2 | Candidate history owner and domain operation: 2 |
| Replacement preparation, external domain | 1/1/1/3 | Source/target data owners, candidate owner, preparation source/target/domain consumers, commit source/target/domain consumers: 9 |
| Replacement preparation, default domain | 1/1/1/3 | The preceding interests plus the distinct default-domain handoff interest H: 10 |
| Independent active-binding use | 0/0/0/1 | World-history consumer and domain operation: 2 |

The `U = O + 2P` value is the maximum baseline only while each binding owner retains O and each coordinator/commit role retains its associated P and U. It does not include additional independent consumers: each consumes remaining U and may be refused at baseline saturation. A 4,096-World sizing example requires four distinct keys per target (current, one changed destination, and two retiring keys) distributed across enough domains; it is not a single-domain capacity promise. Two changed destinations plus both retirees require 20,480 keys and are not promised. A changed or unknown replacement destination consumes prospective K before inspection, so full K may refuse a replacement later shown to have the same GUID. Multiple plans are subject to aggregate admission, not guaranteed; invalidated-but-retained claims continue to count. These examples do not qualify a workload or imply simultaneous fit.

## 4. Asset and cooker retention admission

### M03 4.6 Finite operational limits, admission domains and charging

These are frozen operational limits, not additional EBIN fields, C0 rules, compressed-format limits or accepted measurements. MiB=1048576 bytes; GiB=1073741824 bytes. Counts and byte demands use checked arithmetic; a scalar format ceiling does not promise all ceilings fit together. An operation must satisfy every applicable object, domain, store/decode and format limit. The limits appear in the operational configuration/evidence manifest and diagnostics, not the semantic native/package keys: lowering capacity changes admission, never a successful output's bits. Actual compiled producer/artifact operands still change when implementation changes.

#### M03 4.6.1 Detached objects and exact per-object bounds

An ordinary UnmanagedBuffer/lease, NativeAssetNode, unqualified StagedAssetGraph, AssetCookRequest or immutable specification is caller-owned detached data. Its constructor is not engine/cooker admission, creates no worker/queue/domain and makes no process-memory guarantee. Many such bounded objects can be created by an ordinary program; their sum is the caller's responsibility. They are not counted as engine retained merely because their bytes may later be submitted. Engine work never keeps the original disposable wrappers as its credit carrier: it captures independently owned bounded snapshots.

Core BufferChain construction has a hard input-array limit65536, checked after IsDefault and before Segment[segments.Length], enumeration or Clone. It counts raw input entries, including zero-length ones, so an all-empty input cannot bypass descriptor allocation. Above65536 throws ArgumentOutOfRangeException naming segments with no acquisition. Zero-length segments remain omitted; new immutable `int SegmentCount` reports0..65536 canonical nonempty segments even after close. Retain/engine capture allocates for this canonical count, not the original raw array capacity; it never flattens/coalesces payloads to evade admission. This numeric guard is a required implementation prerequisite.

NativeGraphLimits is an Assets-owned sealed immutable public options value with get-only properties named below, a constructor taking every value in table order, and static Default. A seven-input StagedAssetGraph constructor uses Default; add an overload taking NativeGraphLimits last. NativeAssetNode applies the intrinsic node ceilings; graphs/requests apply their selected local limits before copying/acquiring. Values may be lowered exactly, without rounding, within0..hard; a graph must still allow its one required root. Defaults/hards for fixed schema limits are identical; byte limits may be zero for an intentional no-capacity policy. Default below is not a claim of simultaneous capacity.

| NativeGraphLimits property / other intrinsic surface | Default | Hard | Scope and charge |
|---|---:|---:|---|
| MaximumSourceChainSegments | 4096 | 65536 | Each nonempty source/dependency-byte chain retained by a graph/request/domain; Core raw-input hard cap remains65536. |
| MaximumNodes | 131072 | 131072 | Graph Nodes, reusing cook/graph/package output ceiling. |
| MaximumSuppliedDependencies | 524288 | 524288 | Graph exact outside-node versions, reusing its schema ceiling. |
| MaximumSources | 65536 | 65536 | Graph recorded/retained sources, reusing cook/graph. |
| MaximumReadDescriptors | 1048576 | 1048576 | Per graph: node payloads + two read descriptors per supplied dependency (envelope/publication) + all source-chain nonempty segments. Empty-chain wrapper metadata is charged separately. |
| MaximumLogicalInputBytes | 1073741824 | 8589934592 | Checked sum of every declared node/source/dependency envelope/publication range, counting duplicate ranges here; detached graph shape bound, not shared-physical charge. |
| MaximumGraphMetadataBytes | 67108864 | 268435456 | All-in graph copied immutable facts, dictionaries/order/index/snapshot arrays, private descriptor/credit bookkeeping and construction peak. |
| MaximumNodeMetadataBytes | 33554432 | 33554432 | Each node's copied facts/dependency IDs/metadata/header/digest and node-private bookkeeping; no payload bytes. Dependency-ID count still <=1048576. |
| MaximumDependencyMetadataBytes | 16384 | 16384 | Each supplied dependency's copied full-version/schema/profile/hash/source facts; envelope/publication payload and credit bookkeeping are separately charged. |
| MaximumSourceMetadataBytes | 16384 | 16384 | Each source's copied fingerprint/count/optional UTF8 facts; chain bookkeeping separately charged. |
| MaximumSourceChainMetadataBytes | 4194304 | 67108864 | Each chain's all-in descriptor/registration/header/snapshot/temporary-traversal reservation, distinct from logical bytes and source facts. |
| Intrinsic payload/source/dependency-record range | 268435456 | 268435456 | Each node payload, source byte extent, supplied envelope and supplied publication; existing native/source/decoded ceilings, not a256-MiB allocation promise. |
| Intrinsic native/metadata encoded key/value aggregate | 65536 | 65536 | Existing metadata wire rule; each key/value/count cap remains [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records)'s. |
| Intrinsic copied trusted absolute path UTF8 extent | 4096 | 4096 | Cache/output/source-registration path facts; valid absolute OS path required independently. No URI/source-name path authority. |

Node/source/dependency metadata limits include actual retained managed storage and padding attributable to that object, not merely its canonical serialized length. The graph limit includes both complete existing storage and provisional constructor/Retain peaks. Different wrapper instances cannot be treated as free because their payloads match. Count1048576 plus the required node means the nominal524288 outside-dependency maximum cannot fit two descriptors per dependency in a single graph; classify that shadowed endpoint honestly. Other simultaneous maxima likewise need their exact reachability ledger.

For source-byte/import request variants, the primary source plus dependency-byte chains is one bounded source-input set: initialized arrays, at most65536 total chains including the primary, each<=256MiB/MaximumSourceChainSegments, aggregate read descriptors/logical bytes/metadata within the same NativeGraphLimits. A lease overload is a one-segment chain and receives identical charging. An importer-produced native graph must obtain its additional graph/credit/metadata reservation before creation while old source credits still exist, not replace/discharge consumed source ownership early. Source adapters execute at their later owner; the native M03 credit/domain path is not their acceptance.

Detached construction uses checked preflight under its selected per-object limits and the build's trusted footprint plan before any copied metadata/credit allocation. Expected object-limit failure is synchronous ArgumentException/ArgumentOutOfRangeException with the offending operand/claim/limit; arithmetic overflow is OverflowException. Caller input bytes/credits are not consumed or disposed. These checks do not call a cooker or borrow a store's quota. Engine capture repeats policy admission against its own limits; a detached object valid under larger explicit limits can receive an expected domain refusal, not a fake constructor error.

#### M03 4.6.2 Explicit store and cooker domains

Assets owns sealed `AssetRetentionLimits`/`AssetRetentionDomain` and immutable `AssetRetentionSnapshot`. AssetRetentionLimits' get-only members/defaults/hards are the operational rows below except queue/workers and existing store byte options. Its constructor takes NativeGraphLimits first, then the listed count/byte/cleanup values; static Default uses the default column. AssetRetentionDomain(limits,maximumRetainedBackingBytes) is a managed-only synchronous factory: it validates/fixes the complete limits and trusted charge profile, reserves its own bookkeeping and Core retention subdomain, starts no worker and acquires no filesystem/process resource. Its constructor can conventionally fail without a partially returned native owner.

AssetStore owns one such domain, exposed read-only as `RetentionDomain`. AssetStoreOptions gains `AssetRetentionLimits RetentionLimits { get; init; } = AssetRetentionLimits.Default`; the existing CpuAssetBudgetBytes is the sole source of its retained-backing ceiling, not a second shadow value. Existing MaximumDecodedAssetBytes/MaximumConcurrentDecodeBytes apply independently. Store factory freezes all options. Store creation validates an existing trusted absolute cache directory/registrations and creates managed idle workers; it does not create staging, open a producer, call ownership-bearing plugin factories or perform an asset load before returning the store. Registered supplied codecs remain caller-supplied immutable registrations. Persistent handle/data acquisition begins only inside an admitted load with a recovery record.

Content.Tools gains sealed immutable AssetCookerOptions(AssetRetentionLimits limits) with init-only ImportQueueCapacity=128, CookWorkerCount=2, RetainedBackingBudgetBytes=1073741824, MaximumDecodedAssetBytes=268435456 and MaximumConcurrentDecodeBytes=268435456. Its additional registration constructor accepts the complete verification `AssetStoreRegistration`; the existing constructor selects the compiled built-in registration. `AssetCookerAdmissionDomain.CreateAsync(options,token)` freezes those options, owns fixed worker execution over the Assets domain-owned queue and an Assets AssetRetentionDomain, and returns only an initialized managed idle service. There is no second Tools queue/credit ledger. It implements IAsyncDisposable, exposes its frozen Options/RetentionDomain, and exposes RetryCleanupAsync. It creates no staging/process before an operation is accepted.

For explicit participants, `AssetStoreRegistration` freezes one complete trusted `AssetRetentionParticipantRegistration` together with the selected catalogue, codecs and source rows. Store and cooker factories pass that exact operand through the Assets-owned lower initializer into each actual newly created domain; a detached caller-created domain or observational `RetentionDomain` property is not an installation route. The existing builder/cooker constructors retain built-in-only behavior. Assets and Tools framework entries are composed by their owners without an Assets-to-Tools dependency, and both retain the same physical capacity/charge ledger. Full deployment/qualification closure is checked before any participant callback; a valid earlier entry must not have its getter invoked if a later required binding is invalid.

Retain the public static AssetCooker class but replace the incomplete two-argument entry point with exactly:

```csharp
public static Task<AssetCookReceipt> CookAsync(
    AssetCookerAdmissionDomain domain,
    AssetCookRequest request,
    CancellationToken cancellationToken = default);
```

There is no legacy overload with an implicit global domain or a new domain per call. This is an explicit chapter11/30 API amendment, justified before implementation/API freeze. Ordinary detached requests remain reusable inputs. Add `StagedAssetGraph Retain(AssetRetentionDomain domain)` and `AssetCookRequest Retain(AssetRetentionDomain domain)` for explicitly domain-bound preparation. Request retention independently books one request snapshot and its graph/byte snapshot; native and byte variants preserve their variant. A bound no-argument Retain inherits the domain. Closing that prepared wrapper releases only its own charges/credits. Cooker admission always takes another operation snapshot; it does not consume an earlier prepared snapshot.

| Admission-domain surface | Default | Hard | Exact interpretation |
|---|---:|---:|---|
| Queue capacity: existing store/new cooker ImportQueueCapacity | 128 | 128 | Aggregate pending work records across that owner's channels, not128 per hidden stage. Configurable1..128. |
| Store IoWorkerCount / CookWorkerCount; cooker CookWorkerCount | 4 / 2; 2 | 64 each | Configurable1..64; no inheritance from WorldOptions.WorkerCount or invented zero-worker semantics. |
| MaximumGraphSnapshots | 256 | 1024 | Engine/cooker prepared, accepted, provisional, candidate, retiring and quarantined graph-data snapshots; runtime reconstructed inert graphs count here. |
| MaximumRequestSnapshots | 256 | 1024 | Domain-owned prepared and accepted request/load work descriptors, not detached caller requests. |
| MaximumOutstandingOperations | 256 | 1024 | Accepted + provisional + cleanup/retirement/quarantine records until fully released. Queue/running ceilings additionally apply. |
| MaximumPinnedAssetLeases | 4096 | 65536 | Store CPU version/dependency pin wrappers; cooker has no CPU-ready result lease and uses this pool only if explicitly preparing the same Assets pin variant. |
| MaximumReadCredits | 262144 | 4194304 | All domain-created long-lived/provisional/read-child Core reference slots plus temporary traversal/actual-use reservations; backing aliases do not reduce this count. |
| MaximumMetadataBytes | 67108864 | 268435456 | Aggregate actual/planned owned metadata, shared immutable facts, Core registrations/descriptors, maps/capacity/padding/resize peaks, work/results/cleanup/scratch and diagnostic headers. |
| Store CpuAssetBudgetBytes / cooker RetainedBackingBudgetBytes | 536870912 / 1073741824 | 8589934592 each | Complete distinct rounded native backing retained/allocated by that domain: source windows, decoded/cache/dependencies, staging/scratch, candidate/retiring/quarantine. One backing charge per storage identity per domain. |
| Existing/new MaximumDecodedAssetBytes | 268435456 | 268435456 | Per decoded asset; reuses chapter11. |
| Existing/new MaximumConcurrentDecodeBytes | 268435456 | 268435456 | All concurrent decode reservations including provisional outputs; also fit aggregate backing. |
| MaximumNamedStagingBytes | 8589934592 | 8589934592 | Aggregate owned uncommitted filesystem extents, active/retiring/quarantine; distinct from memory and committed cache retention. File growth reserved before write. |
| MaximumNamedStagingEntries | 262144 | 1048576 | Aggregate owned file/directory/handle/process cleanup records; per operation at most262144 entries. Existing schema/package counts apply too. |
| MaximumRequestMetadataBytes | 16777216 | 67108864 | Request/specification/path/producer/policy copied facts and their peak, excluding separately counted graph/byte carriers. |
| MaximumOperationMetadataBytes | 67108864 | 268435456 | Per-operation total scratch/work/verification/writer/retirement metadata, also constrained by aggregate. |
| MaximumLiveHandlesPerOperation | 8 | 64 | File/stream/process/pipe ownership records, including provisional acquisition; shared physical handle counted once, each wrapper metadata charged. |
| MaximumLiveProcessesPerOperation / total domain | 1 / 2 | 1 / 64 | Each invocation's complete owned process/pipe lifecycle; native-only M03 may use zero. |
| Process raw capture per invocation / aggregate | 8388608 / 16777216 | 8388608 / 536870912 | Existing8-MiB per-process cap; aggregate cap also fits backing. Read chunks<=16384 before decoding; no new source codec. |
| MaximumCleanupFailureSamples | 16 | 16 | Detailed original cleanup-exception/resource samples per operation/pass; exact total failure count and each remaining resource state are retained separately. |
| Intrinsic cleanup-only metadata reserve | 1048576 | 1048576 | Within the aggregate metadata budget, reserved at domain initialization for one serialized retry/shutdown pass's header/selection/counters. Ordinary work cannot spend it; cleanup must remain possible when quarantine fills ordinary capacity. |
| ShutdownWaitMilliseconds | 5000 | 30000 | Configurable1..30000; chapter02's default5s retained. Bounds an observation, never forces native/process/file-I/O completion. |

All new aggregate count values may be lowered from0..hard; required initialization and a desired one-root operation still need their actual reservations. Byte budgets may be0..hard. A valid but insufficient initialization budget conventionally refuses before worker/resource acquisition; it is not silently increased. Workers/queue/wait have the positive ranges above. AssetRetentionLimits does not duplicate store CpuAssetBudgetBytes or decode settings. Store/cooker options reject an inconsistent profile at creation with a synchronous usage error; expected operation pressure follows [M03 4.6.5](11-assets-import-streaming-and-packages.md#m03-465-queue-full-execution-refusal-and-retry).

The schema-derived per-object counts and store512/256/128/4/2 defaults are reused. Chain4096/65536,64/256-MiB metadata envelopes,256/1024 outstanding objects,262144/4194304 reference slots,1-GiB cooker/8-GiB hard backing and bounded cleanup/handle settings are selected candidate envelopes: powers of two keep the metadata amplification finite while preserving explicit larger trusted workloads. The1-GiB cooker default allows separately charged source/output/scratch classes beyond the512-MiB resident-store default; it is not a measured sufficient capacity. Eight-GiB hard backing/staging permits bounded trusted streaming, not an8-GiB array or a coupling to the World admission domain. Different explicit domains charge the same borrowed backing independently; these are not a process-total or host-wide shared-memory promise.
