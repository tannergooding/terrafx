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
| Scene entities / prefab expansion | 1048576 / 65536 |
| Draw instances / particles / UI nodes | 131072 / 131072 / 131072 |
| Cameras / lights / bone matrices per frame | 16 / 256 / 2048 |
| Presentation snapshot queue | 4 pending snapshots; existing lossy replacement policy applies only to unconsumed snapshots |
| Native backbuffers / frames in flight | 3 where the presentation profile specifies triple buffering; this deliberate latency/lifetime choice is not a ring-storage requirement |
| Audio voice / streaming / bus / command credits | 256 / 32 / 64 / 1024 |
| Audio tail slots / retirement backing ring | 32 / 512; at most 288 live voice/tail retirement records |
| Asset worker channel / process capture | 128 tasks / 8 MiB raw captured bytes |
| Trusted package entries / dependencies / index | 131072 / 524288 / 64 MiB |
| CPU asset / simultaneous physics memory | 512 MiB / 512 MiB |
| Spatial cell references per ordinary proxy | 64; wider proxies use the explicit wide-proxy tier |
| Registered spatial profiles per process | 4096 including the fixed Default; immutable registrations cannot be evicted/rebound |
| Spatial traversal / candidate budget per query | 65536 nodes / 1048576 candidates unless a stricter subsystem limit applies |

Other existing power-of-two hard limits remain in their owning chapters. In particular, physics collider/body/joint/contact/mesh/predicate budgets and input map/event/command credits do not rise incidentally. Native texture limits are hardware admission minima, not allocations the engine must make.

The queue/backing-ring changes are intentional compatibility/profile revisions. Three native presentation images do not imply only three pending CPU snapshots. Audio retirement storage may contain at most one record per each of 256 voices and 32 tails; a 512-slot ring still returns only those 288 semantic slot credits and cannot fabricate extra playable voices.

## 3. Budget composition and failure

Request admission validates counts, checked byte products, aggregate outstanding credits, and subsystem/world/host budgets before mutation. A local capacity reduction cannot violate a dependent minimum (for example, retirement acknowledgement storage for all admitted voice/tail slots).

Resource pressure chooses only explicitly evictable/unpinned versions or authored observational LOD/work policies. It does not drop reliable input/commands/contact events, free borrowed storage, skip required collision neighbors, silently reduce physics fidelity, or claim a partial asset is ready.

Limits/settings/profile identity appear in diagnostics, cook/cache keys where relevant, save/replay manifests, and performance results. Boundary qualification exercises zero where admitted, the exact ceiling, and one beyond; floating ranges test the nearest representable outside value, not an arbitrary distant failure.

The editor shows requested/admitted/used/pinned/staged/in-flight counts and bytes. A maximum-cost request can be identified before it produces a frame-time spike. Performance targets are measured with named hardware, content, configuration, runtime, and revision; no power-of-two choice is itself certified faster.
