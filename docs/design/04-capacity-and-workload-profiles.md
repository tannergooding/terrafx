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
| Shared host `DiagnosticsService` retained storage | 8 MiB |
| World resource and system state subceiling | 128 MiB, also constrained by remaining shared credits |
| World snapshot bytes | 2147483584 |
| Aggregate World/persistence admission domain | 8 GiB |
| World scheduling conflict comparisons | 16777216 |
| Spatial cell references per ordinary proxy | 64; wider proxies use the explicit wide-proxy tier |
| Registered spatial profiles per process | 4096 including the fixed Default; immutable registrations cannot be evicted/rebound |
| Spatial traversal / candidate budget per query | 65536 nodes / 1048576 candidates unless a stricter subsystem limit applies |

Other existing power-of-two hard limits remain in their owning chapters. In particular, physics collider/body/joint/contact/mesh/predicate budgets and input map/event/command credits do not rise incidentally. Native texture limits are hardware admission minima, not allocations the engine must make.

`WorldSchedulingLimits.Default` sets the scheduler byte budgets above and a conflict-comparison ceiling of 16777216; the sum of the byte budgets is 2176 MiB, not a total host/process-memory limit. `MaximumOwnedStateBytes` is the shared World-state ceiling, including identity history and indexes, entity storage, scene provenance, resource/system state, control metadata and their admitted transition peaks. The existing seven-argument constructor remains; a World may lower each configured byte budget to an exact value from 4096 bytes through its ceiling and the comparison budget to any value from zero through its ceiling. `MaximumResourceAndSystemStateBytes` is derived as `min(128 MiB, MaximumOwnedStateBytes)`. Admission uses configured exact values without rounding them upward.

`MaximumDiagnosticBytes` is an independently enforced per-World diagnostic sublimit, not an addition to `MaximumOwnedStateBytes`; it includes the actual retained diagnostic records attributable to that World. A standalone World owns its diagnostics service and charges its retained records to this sublimit. Hosted Worlds share `EngineHost.Diagnostics`: each World retains the same per-World sublimit for its records, while the service enforces one fixed 8-MiB host-wide ceiling across the host and all hosted Worlds. Shared-service bytes have one physical charge at the service owner; per-World limits are attribution quotas, not duplicate physical charges. The host-wide value is a ceiling, not a preallocation or configurable option. Returned immutable snapshots are caller-owned copied arrays bounded per call by `DiagnosticQuery.maximumRecords`; they are not retained service storage, and their later lifetime does not extend the service's charge.

Deferred subtree destruction adds an ordered receipt vector to the existing command result. Each `WorldEntityIdentity` contains four GUIDs, or 64 bytes of logical identity payload. One million entries are 64,000,000 bytes (about 61.04 MiB); the 1,048,576 live-entity ceiling would produce exactly 64 MiB of identity payload, already twice the default 32-MiB command-staging budget before expansion metadata, headers or other reservations. Preapplication preparation charges the vector to command staging plus destruction metadata (`CMD+D`); the prepared/published receipt charges its actual retained copy to owned World state, work-plan and destruction metadata (`O+W+D`). Reserve the complete simultaneous transfer peak before installation and refuse the whole operation when it does not fit; neither one submitted command nor the entity-count ceiling promises a fixed subtree receipt size.

For an M03 structural boundary trial, let `B` be the independently measured baseline charge at the request boundary, including existing owned backing and outstanding reservations without counting a grant twice, and let `D` be the request's complete additional simultaneous peak above that baseline. The exact-fit configured limit is `B + D`; the one-byte-short limit is `B + D - 1`. `D` includes all applicable old/new storage, headers, metadata, work, reservations, conversions and retirement retention, not only logical payload or candidate pages. Setup and every preceding required stage must fit in the short trial; vary only the named budget. If the actual target is not reached, its observer cannot be qualified, or the limit is outside the existing domain, the witness is unsupported and remains open. A budget set to `D` alone is not an exact-fit test when `B` is nonzero.

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

Attachment installation/replacement preparation, distinct installation obligations, bounded release-report/finalization carriers and unreleased cleanup/quarantine remain charged to these existing World-owned state, control, transition and retirement peaks; they add no budget category or unbounded failure-detail list. System cleanup/finalization carriers, report state and unresolved ownership use the same existing budgets; for N claimed systems requiring cleanup, retained failure details are bounded by `min(N, 64)`, with every omitted failure counted and no unbounded exception list. Resource partitions remain under the existing resource/system-state subceiling. Refusal to reserve a mandatory precommit report occurs before a canonical write grant.

Subscriber-failure delivery uses the existing World-owned budgets. The current
M03 C# tests validate observable refusal and result behavior through ordinary
.NET tests; they do not capture stack traces, profile-specific scratch, or
process/accounting receipts. The 64-subscription ceiling bounds result
multiplicity, not traversal work.

Capacity checks account for backing storage, grants, interests, observer
buffers, pooled storage, and retained lifetimes under their owning World,
domain, or pool. They do not add budget categories or create capacity credit
from copied or aliased references.

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

The domain-local `LineageMembershipAccounting1` metadata candidate is a sub-budget of the existing 8-GiB World/persistence envelope, not an added entitlement. Per domain, inclusive ceilings are 4096 registered/prospective roots (M), 262144 distinct live/reserved keys (K), 1048576 root-to-key edges (E), 65536 independent root/operation interests (I), 4096 prospective, pending or closed-but-unreleased start-new reservation controls (R), and 66561 raw lineage references per root; total charged registry metadata B is `min(134217728, domain.MaximumBytes)`. Root, source/snapshot, candidate, retiring, quarantined and prospective state counts before allocation. No public option raises these ceilings, and count maxima do not imply joint byte fit.

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
  + maps, package-backed asset/source pins and their operational ownership
  + actual data-consumer/use credits under the accepted owner accounting.

RegistryCharge <= B.
RegistryCharge + DataCharge + all other domain-owned credits <= domain.MaximumBytes.
```

Growth reserves complete old-plus-new peak, candidate vectors and scratch before allocation; old storage remains charged until actual release. Spending converts a grant to allocated charge rather than double-counting the same allocation. Failure preserves old membership and ownership, and quarantine keeps the existing charged credit; no capacity is recovered by dropping a lineage key.

R occupancy includes reservation controls still in actual use, cleanup or quarantine after logical exclusion ends. Logical closure does not refund R occupancy; returning a control credit does not release installed backing. Passive copies do not extend actual ownership.

K accounting and prospective index capacity are not semantic membership or admission priority. Prospective retained grants never win membership priority. E charges each unique exact-root-generation/key pair once; duplicate validated references add no edges. Conversion of the exact target root's prospective grants into membership plus its reservation adds neither a duplicate K entry nor a duplicate E edge. Independently registered roots sharing a key have separate edges.

Candidate, data-owner and operation/consumer I credits remain distinct even when they share the exact target root. Transfers move existing credits without minting another.

Every root, key, edge, interest, reservation control, allocation, scratch buffer and backing charge ends on its own actual lifetime. Claim closure, reservation-control release, history release and backing release are separate facts. Committed membership persists through actual histories/consumers and quarantine; refused prospective ownership retains its actual charges without gaining membership priority. Existing ceilings, formulas, spare-capacity charges and complete old-plus-new growth admission remain unchanged.

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

## 4. Content package and asset loading limits

The package ceiling is a file-extent bound, not an allocation promise. Package loading validates the header and directory with checked integer arithmetic before allocating decoded or retained payload storage. The fixed EIDX directory contains one 64-byte header and at most 131072 32-byte entries, so its maximum encoded size is 4194368 bytes. Package bytes, directory bytes, per-asset decoding, concurrent decoding, and retained CPU backing are separate limits; exceeding any one rejects before the corresponding allocation or publication.

| Asset loading bound | Default | Hard | Scope |
|---|---:|---:|---|
| Maximum package bytes | 8589934592 | 8589934592 | Complete EPKG2 file extent; streaming only, never a request to allocate the whole package as one array. |
| Maximum directory entries | 131072 | 131072 | Live content-set AssetIds; IDs themselves range through 2147483647 and are not compacted to this count. |
| Maximum directory bytes | 4194368 | 4194368 | 64-byte header plus 32 bytes per permitted entry; no dependency/name/profile-index tables. |
| Maximum decoded asset bytes | 268435456 | 268435456 | One decoded payload before schema validation. |
| Maximum concurrent decoded bytes | 268435456 | 268435456 | Aggregate reserved decoded output across concurrent loads. |
| Retained CPU backing bytes | 536870912 | 8589934592 | Distinct package-backed or decoded storage retained by live leases in the selected store. |
| Maximum live asset leases | 4096 | 65536 | Caller-visible leases; lease-count exhaustion does not evict an in-use asset. |

The package and directory ceilings are fixed format bounds. Store options may lower the decoded-asset, concurrent-decode, retained-backing, and live-lease defaults within their hard limits. The store checks every requested range, decompressed length, and aggregate budget before the associated allocation; a package that fits the file-size limit can still fail a tighter decode or retained-memory limit. Package replacement switches the selected content set atomically, while old leases keep their own package backing until consumers release them. Source-adapter and offline build-process budgets belong to their actual implementation owner and are not represented as serialized graph/source records here.