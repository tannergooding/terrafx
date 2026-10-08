# Engine acceptance cases

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

These 144 cross-system cases supplement the PH/PF/AU/IN subsystem cases and the 16 NM geometry cases in [Chapter 06](06-owned-geometry-and-numerics-interchange.md). They are implementation obligations, not claims of executed engine tests. Passing requires the exact observation, not merely compiling, creating a window, or inspecting an unrelated demo. Numeric tolerances are absolute unless explicitly relative. Record the profile and native/headless lane defined in [Chapter 28](28-diagnostics-performance-and-validation.md) and [Chapter 29](29-project-setup-build-and-deployment.md).

## 1. Units, clocks, and math

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN001 | Convert 60 completed ticks at 60 Hz to `TimeSpan`; inspect the first 3 intervals. | Exactly 10000000 `TimeSpan` ticks/1 s; intervals 166666,166667,166667; no accumulated rounded-dt drift. |
| EN002 | Run tick rates 1,59,60,1000 for one ideal second. | Each final boundary is exactly 1 s with positive intervals; rates 0/1001 are rejected before construction. |
| EN003 | Inject mapped simulation input times 0,166666,166667 `TimeSpan` ticks at 60 Hz. | The first two belong to tick 0 once; the third belongs to tick 1. |
| EN004 | Pause for 10 host seconds, service UI, then resume with Jump held. | No simulation debt or tap replay; resume works without a tick; held Jump stays disarmed until release. |
| EN005 | Accumulate 251 ms debt under the default policy versus `DropDebt`. | Default reports `SimulationOverrun` and pauses with debt retained; explicit `DropDebt` records a discontinuity and invalidates strict replay. |
| EN006 | Apply `TimeScale = 1.25` across 7 ms host increments, then restore 1. | Fractional remainder is retained; total elapsed agrees with the reference within 1 `TimeSpan` tick, without per-frame truncation drift. |
| EN007 | LH project positive `viewZ` 0.1/1000, `behindZ` = -1 and orthographic/infinite variants. | `Near`/far depths 1/0 within 1e-6; clip.w = `viewZ`, infinite depth = near/`viewZ`; increasing `positiveZ` moves away/decreases depth. Center unprojection points +Z, not -Z. |
| EN008 | Project NDC triangle (0,1),(1,-1),(-1,-1) into viewport (10,20,800,600); reverse indices/mirror instance. | Pixels(410,20),(810,620),(10,620) within 0.5 pixel; framebuffer shoelace 480000/Vulkan area-240000; D3D12 `FrontCounterClockwise` = false/Vulkan negative-height CLOCKWISE draw fronts. Reverse/mirror parity flips once. |
| EN009 | `Scale`, rotate, translate a point, then apply its parent translation. | Row-vector local * parent agrees with the direct reference within 1e-5; translation occupies `M41`..43. |
| EN010 | `Compose` X then Y90-degree rotations; compare Rfirst * Rsecond. | Transformed vectors agree within 1e-5; ordinary quaternion multiplication is not substituted with the wrong order. |
| EN011 | Change the derived frame origin to spatial (100, 0, -200) meters. | Persistent chunks/offsets/body compensation/witnesses/velocities do not change; derived caches and `FrameRevision` publish once, distances/candidates/history remain consistent and retained snapshots keep their original frames. |
| EN012 | Supply NaN/infinity dimensions, dt or colors; exhaust `TimeSpan`/owner/generation arithmetic. | Typed argument/exhaustion failure precedes commit/native allocation; no success-shaped clamp or identity wrap. |

## 2. API and ownership

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN013 | Compile public consumer scenarios in a separate assembly; check `TryGet` absent/foreign, `TryInvoke`/`TrySubmit` capacity/invalid input and dedicated query/preparation receipts. | `bool` false has its single documented absence/capacity meaning and out validity; misuse/unexpected failure throws, domain enum/response payloads are coherent and no public generic Result/Option, union, tuple/positional record is required. No internal access, native pointers, unsafe casts or process globals; nullable/analyzers clean. |
| EN014 | `Start` the host asynchronously; Invoke from another thread; throw in an accepted callback, then fill its mailbox and use `TryInvoke`. | Owner executes the synchronous callback; exception faults `InvokeAsync`'s `Task` with original context, not a successful `Failed` wrapper. `QueueFull` gives `TryInvoke` false/`null` completion/no acceptance. Continuations never run inside native callbacks; wrong-thread mutation rejects. |
| EN015 | Attempt an awaited callback, retained `EngineContext` or caller-thread native pump. | The borrowed-context API forbids asynchronous retention; native affinity is not inferred from await. |
| EN016 | `Dispose` the host synchronously on its owner versus from another thread. | Owner synchronous disposal rejects rather than self-joining; `StopAsync` is a safe request; off-owner disposal completes orderly or reports timeout/fault. |
| EN017 | Release a pooled owner while a consumer lease remains active. | Bytes cannot be reclaimed before the lease ends; the last lease returns storage exactly once to the correct owner. |
| EN018 | `Dispose` a submitted texture before its fence completes. | Logical access is immediately invalid; resource, descriptors and allocation remain physically retained until all protections complete. |
| EN019 | Mutate the caller's `Span` after an accepted upload. | The GPU consumes copied accepted bytes, not the caller's later mutations. |
| EN020 | Cancel/fault while an operation still consumes its admitted independent buffer/chain sublease; qualify task forwarding versus an async await/finally wrapper under baseline and Runtime Async. | The original operation `Task` becomes terminal only after actual consumption and operation-credit cleanup, and faults with the exception rather than successful `Failed`/default payload. Canceling a separate observation does not free backing ownership; chosen wrapper semantics preserve exception timing/finally/lease cleanup in both code-generation lanes. |
| EN021 | `Dispose` twice, reuse an index and submit the stale handle. In native-owner variants, race close with admitted native calls/callbacks, force leaked-owner GC, fail construction after each successful acquisition, and exercise legal versus unavailable thread-affine retirement. | `Dispose` is idempotent; later access fails; the stale handle cannot affect the new resource. Fake-native counters and real boundary conformance prove successful cleanup releases acquired ownership exactly once in dependency/legal-thread order, never before actual-use/fence completion. Partially constructed/finalized owners touch no unacquired handle. Strong roots preserve native use; `GC.KeepAlive` alone does not license concurrent close. Finalizers do no engine-controlled waits/user cleanup and cannot throw; failed/unavailable release retains safe ownership with diagnostics, not successful shutdown. Public disposal/base-type contracts remain unchanged. |
| EN022 | Attempt user-continuation dispatch inside native/audio processing callbacks. | Dispatch is refused; copied completions are delivered outside the callback on the correct control thread. |
| EN023 | Block a native driver beyond the default 5-second wait. | Explicit timeout/fault retains live ownership; no DLL unload/free beneath a callback or fake clean disposal. |
| EN024 | Execute generated/explicitly typed registries in NativeAOT player, editor and content tools; request dynamic managed loading or omit a required component/query/codec registration. Repeat any selected JIT comparison. | Required registrations execute with rooted metadata and no runtime code generation; missing instantiations fail the build/admission explicitly. Dynamic managed loading throws `NotSupportedException` with `UnsupportedEngineFeature` diagnostics in every build; JIT exposes no extra engine features. |

## 3. ECS and structural transactions

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN025 | `Query` a default, stale or foreign entity, including a second host's same-index/generation entity; query an absent component on a live entity. | `Process`-wide owner identity keeps hosts distinct; invalid identities throw the specified error; only the live absent component yields false. |
| EN026 | `Destroy`/reuse an entity index, then reach generation `uint.MaxValue`. | Old handles reject; ordinary reuse increments generation; the exhausted slot retires permanently. |
| EN027 | Register a managed/pointer-bearing portable component or duplicate schema GUID. | Registration refuses; no serialized pointers or boxed archetype columns are admitted. |
| EN028 | Use `Position` 12, `Velocity` 12 and Entity12 logical bytes per row. | Capacity = `min(256, floor(32768 / 36))` = 256; actual arrays/headers remain accounted separately, not claimed as a32 KiB footprint. |
| EN029 | Request logical row sizes 32768 and 32769 bytes. | The first admits capacity 1; the second rejects before archetype/entity mutation. |
| EN030 | `Remove` the middle row of three entities. | The last row moves and its lookup updates; no stale slot or query misaddress. |
| EN031 | Register read/read versus read/write query access; attempt an undeclared write; traverse through ordinary/ref-struct generic chunk callables and throw after updating their state. | Read/read may run concurrently; conflicts order deterministically; undeclared writes reject and structure remains locked until leases join. Concrete `ref` callable state survives invocation/exception without copying, boxing or delegate adaptation; fault cleanup releases query access without inventing rollback. |
| EN032 | Attempt `Create`, await or an escaping column `ref` inside a query. | Compiler/API/runtime admission prevents invalid retention or mutation during iteration. |
| EN033 | Queue `Create(token)`, `Set(token)`, `Destroy(token)`. | The receipt is `Canceled`; no live entity or integration reservation leaks. |
| EN034 | Reference a canceled token or another writer's token. | The entire batch fails precommit with command key/reason; no invented `null` target. |
| EN035 | `Destroy` one existing entity twice; separately inject a reservation failure. | Duplicate ordered destruction is invalid; preflight failure leaves structural storage unchanged. |
| EN036 | Execute one command batch with 0/1/4 workers and shuffled completion. | Command order, receipts and component membership agree; thread IDs/dictionary order do not decide results. |

## 4. Scheduling and publication

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN037 | Register a dependency cycle or duplicate system `Order`/GUID. | Startup fails with a bounded conflict/cycle path; no partial DAG runs. |
| EN038 | Shuffle worker completion for a floating reduction. | Partition-ordered folding produces the same exact-profile result; no nondeterministic atomic `float` accumulation. |
| EN039 | An `Early` system queues a new body for `Structural` commit. | The body is visible after commit and first simulates next tick; no extra physics step occurs. |
| EN040 | Deliver an internal physics contact to `Late`, then throw from a `Late` system. In a separate successful tick producing `BeginContact`, register healthy subscriber A, throwing subscriber B and healthy subscriber C in that order; B throws after inspecting the batch. Then close the world normally. | The `Late` failure faults without `TickIndex` advance or external event/render/audio publication; prior committed state remains visible and local physics is not claimed to roll back. In the post-commit variant, every callback sees advanced `TickIndex` and the committed publication/receipts; A/C each receive `Begin` once, B is attempted once with `Failed(SubscriberException)`/diagnostic, and C is not skipped. The ledger/save eligibility remain committed, no tick retries or rollback occur, and A/C each receive one `WorldDisposed` End on closure. |
| EN041 | Write a component, then fail structural preflight. | `Structural` storage is unchanged; the world is faulted and cannot save; no whole-world rollback claim. |
| EN042 | Fill the 3-entry render snapshot queue. | Oldest unconsumed data may drop with a counter/lease release; consumed data is not overwritten and gameplay events are not lost. |
| EN043 | Hold an old asset version through reload and world unload. | CPU leases/GPU fences retain it; the new version activates only at the named boundary. |
| EN044 | Supply nonconsecutive pose snapshots or teleport. | Render current pose with a discontinuity marker; do not interpolate across missing history or teleport. |
| EN045 | `Stage` audio in a faulted tick; then fill audio admission after a successful tick. | Faulted tick emits nothing; successful tick delivery failure produces an explicit ticket/diagnostic, not retroactive gameplay rollback. |
| EN046 | Eight catch-up ticks are pending while close/focus/control work becomes ready. | Pump between ticks, not only after the entire batch. |
| EN047 | With no admitted ticks, request pause, resume or input-overflow recovery. | Host control is serviced without waiting for a paused tick. |
| EN048 | `Stop` with jobs, assets, snapshots and native callbacks active. | Shutdown follows dependency order and reports terminal outcomes; storage/surfaces are not freed before joins/protection. |

## 5. Scene, camera, and animation

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN049 | Reparent to self, introduce a cycle, exceed depth 256 or use a foreign parent. | Whole transaction rejects; old child order/world matrices remain unchanged. |
| EN050 | `KeepWorld` reparent under a sheared or noninvertible parent. | Reject if SRT recomposition residual exceeds 1e-5 times `max(1, maxAbsEntry)`; no approximation. |
| EN051 | `Destroy` a parent with default child detachment. | Every child preserves world SRT or the command rejects; `DestroySubtree` is a distinct explicit operation. |
| EN052 | Expand nesting 33, a prefab cycle or65537 entities. | Import/admission rejects before live allocation. |
| EN053 | Write a bound dynamic `Transform` directly or parent its root. | Ownership validation faults/refuses; use the physics teleport/root contract, not an ignored competing pose write. |
| EN054 | Supply coincident camera target, parallel up, singular scale or nonfinite/nonpositive aspect override. | Typed failure before inversion; `null` aspect derives from viewport, valid override fixes projection only; no identity fallback/NaN/clamped dimension. |
| EN055 | `TryWorldToScreen` behind/offscreen/pole; center rays identity/rotated/ortho/infinite; glTF camera scales 0.1/1/2 with ortho source near 0/positive; separate native near -1 and invalid glTF near -1. | `False` only perspective clip.w = 0; otherwise meaningful flags/`viewZ` > 0 in front. `Identity` ray +Z, yaw + Y90 deg ray +X; ortho starts near plane. Import scales `Near`/`finiteFar`/xmag/ymag once, height = 2 * s * ymag/aspect = xmag/ymag; source boundaries/clipping/ray origins match within 1e-5. FOV/aspect/infinite mode unchanged. Native signed `Near` works; negative glTF `Near` rejects, unsupported negative magnification reports explicitly. Invalid arguments throw, no zero/NaN fallback. |
| EN056 | Loop a 1-second clip with events at 0/1 s across its end. Set speed to -4 and +4, then attempt the nearest finite representable speed values below -4 and above +4 in the declared input type. | Both speed endpoints are admitted and produce the defined reverse/forward sampling; each adjacent outside value is rejected without changing animation state, event cursor or output. Prior-loop end then new-loop start fire once each; pose wraps; default Seek emits no events. |
| EN057 | Reverse sampling and a Seek crossing 4097 events. | Reverse interval/order is defined; explicit `EmitCrossedEvents` rejects the whole over-limit seek, without truncation. |
| EN058 | Blend negative-dot/cubic tracks with unequal intervals; convert RH quaternion keys/derivatives to LH. | Shortest-arc normalized samples; Hermite uses actual interval; reflection commutes with sampling. Derivatives map (-x,-y, z, w) without normalization; no zero quaternion fallback. |
| EN059 | Test RH GLB bind/two-bone/four-weight skins, transformed hierarchy/nonuniform/mirrored nodes, invalid joint/zero weights. | H4-converted nodes/inverse binds/palettes yield identity bind and reflected source skinned positions within 1e-5; normals/weights/joints validate; no mesh-only Z patch. |
| EN060 | Block the 24 sphere actor's extracted root motion; test singular bones and a zero blended normal. | Actual body travel is collision-limited, visual pose continues; singular palette fails extraction; zero blend uses the documented dominant-bone normal with diagnostic and no NaN. No capsule/controller capability is inferred. |

## 6. Graphics resources and shaders

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN061 | Omit a required format usage, limit or feature while its enum exists. | Profile admission reports actual missing capabilities; no partial device or silent API switch. |
| EN062 | Overflow buffer offset + length; misalign a uniform offset. | Reject before native work; alignment uses the least common multiple of native alignment and 256. |
| EN063 | Reflect 192-byte `ObjectGpuData` on a Vulkan device requiring 512-byte alignment. | Fields at 0/64/128/144/160/176; allocation stride 512; DXIL/SPIR-V reflection agrees. |
| EN064 | Declare Vulkan `b0` and `t0` in one space. | Distinct mapped bindings 0/16; no collision between register classes. |
| EN065 | Use shader `bool`, raw CLR `Vector3` or an unsupported 64-bit layout. | Portable schema rejects; semantic `Vector3` uses explicit 16-byte slots, not raw CLR memory. |
| EN066 | Mismatch descriptors, stage I/O, workgroup size or pipeline targets. | Offline import/recording rejects; no undefined native draw. |
| EN067 | Reset a D3D12 allocator while a submitted list remains pending. | Reject/wait for the protecting fence; command-list reset alone does not authorize allocator reset. |
| EN068 | Wrap the upload ring over live bytes; request an oversized dedicated upload. | Reserve waits/rejects; live bytes are never overwritten; dedicated uploads count against budget. |
| EN069 | Read back exact buffer ranges and complete texture subresources, cancel an observation, then lose the device. | Owned results follow actual transfer/map-copy completion, preserve format/extents/pitches and zero exposed padding; no fake successful zero bytes on failure. Source/staging credits survive cancellation until actual completion and the observation reports its explicit outcome. |
| EN070 | Update a submitted descriptor/material version. | Allocate a new immutable range/version; retain the old one through last use. |
| EN071 | Reload invalid syntax/layout, then a valid shader version. | Old version remains visibly active after `ReloadFailed`; valid packers/materials/pipelines activate atomically and old resources retire by fence. |
| EN072 | Request ray tracing or mesh shaders on capable hardware. | Version 0.1 reports `UnsupportedEngineFeature`; hardware support is not reported as engine implementation. |

## 7. Graph and native presentation

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN073 | Upload complete 2D/3D texture mips/layers with explicit source pitches, then sample/readback; repeatedly write storage without changing layout. | Checked usage/aspect/extents/native footprints and retained byte credits; exact uploaded content returns through owned readback. Correct transition and RAW/WAW memory dependencies, including same-layout storage/UAV barriers. |
| EN074 | Read an unwritten mip, load an uninitialized attachment or read after discard. | Compile fails before recording/submission; another mip does not imply valid content. |
| EN075 | Schedule a v1 read after declaring v2 overwrite. | WAR edge places the reader before overwrite; incompatible requirements produce a graph cycle. |
| EN076 | Compare overlapping aliases with disjoint compatible A/B. | Overlap rejects; valid reuse emits alias barrier and new-content initialization; live frame-slot fences still protect allocations. |
| EN077 | Use three frame slots with two actual swapchain images. | Acquire/render-finished semaphores use actual image identity; frame storage uses slot identity. |
| EN078 | Signal submission fence while presentation fence remains pending. | Frame allocator may recycle; presentation semaphore/views/swapchain remain retained; no `WaitIdle` shortcut. |
| EN079 | Return Acquire `NotReady`, Timeout or `OutOfDate`. | No unsignalable fence reset/submission; explicit retry/recreate without a hung future wait. |
| EN080 | Fail recording after acquire but before Present. | Correct acquire-wait/image-release path; no recycled signaled binary semaphore or leaked acquisition. |
| EN081 | Rapidly resize, minimize and restore. | Coalesced extents, nonzero gate, at most two retiring generations; `SwapchainEpoch` changes, `SurfaceEpoch` only for native surface replacement. |
| EN082 | Omit maintenance1 on a Vulkan 1.3 device; render/read back through a real `OffscreenNative` texture target. | Interactive admission refuses with reason; offscreen uses actual GPU completion/readback with no WSI, presentation sequence or surface/swapchain epoch. No fake output or unsupported shutdown proof. |
| EN083 | Resize D3D12 with live backbuffer references. | `Stop` old submissions, wait and release references first; rebuild views with a new `SwapchainEpoch`. |
| EN084 | Inject driver hang, surface loss or device loss during present retirement. | Scope-correct fault/quarantine closes new admission; submit completion alone never authorizes destruction. |

## 8. Rendering semantics

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN085 | Draw transparent sprites with equal ordering but different materials. | Preserve specified stable order; only adjacent compatible batching is legal. |
| EN086 | Use fractional pixel-art scale while a body moves continuously. | Explicit noninteger profile, not a pixel-perfect claim; screen snapping does not change body position. |
| EN087 | Change one tile in a32x32 chunk. | Rebuild the affected chunk; collision changes validate separately, without unnecessarily rebuilding 1000 bodies. |
| EN088 | Import RH triangles/strips/fans/tangents, render mirrored/nonuniform/`double`-sided normal maps and generated sprite/UI quads. | Reflection + one index reversal yields clockwise fronts/reflected cross normals; `tangentW` flips once and bitangent matches. No handedness-only UV/green flip; instance determinant flips pipeline parity once. Equal-layer sprites draw larger camera-`viewZ` first, including rotated cameras/stable ties. |
| EN089 | Sample `sRGB = 0.5`, apply tint and blend before output; output final linear SDR 0.5 through ordinary/custom graphs to linear/sRGB targets. | Decode to about 0.214041 linear, operate in linear space and encode once; no `double` gamma or gamma-space premultiplication. Final linear 0.5 remains 0.5 in linear output and encodes once in sRGB; terminal output does not repeat the preceding HDR tone map or darken composed UI. |
| EN090 | Evaluate roughness 0.045/1, `NdotL = 0`, and metallic 0/1. | Finite nonnegative BRDF; zero light contribution is 0; no grazing-angle division NaN. |
| EN091 | Evaluate point light at distances 1/2 and range boundary. | Specified inverse-square/squared smooth falloff; finite near clamp; zero outside range. |
| EN092 | Request over four spot/point shadows; run directional cascades for native orthographic `Near` 0/-1, `Far` 10 and perspective; set end <= `Near`. | Explicit budget/unshadowed/unsupported outcomes. Ortho splits 2.5/5/7.5/10 or1.75/4.5/7.25/10 are finite/ordered; perspective practical split retained. Signed light-space ortho clipping maps near 1/far 0; spot near stays positive. Empty range reports `ShadowRangeInvalid` before allocation, no NaN/pow/clamp fallback. |
| EN093 | Sample cascade PCF beside adjacent atlas cells with reversed Z. Use receiver depth 0.5, zero slope and default constant bias 0.001 against stored depths 0.5,0.5005 and 0.7. | Clamp taps to the cell and compare reference 0.501 `GreaterEqual` stored depth: equal-depth and small-error taps are lit, genuinely occluded 0.7 is shadowed. `Add` receiver bias; subtraction 0.499 is an explicit failing witness. No neighboring-cell bleed. |
| EN094 | `Create` camera-target dependency A -> B -> A. | Graph cycle fails, not arbitrary draw order or accidental previous-texture feedback. |
| EN095 | Extract 131072/131073 draw instances and 256/257 lights under defaults through dimensional `Renderer.Extract`. | Exact ceilings succeed; one beyond returns `CapacityExceeded` with exact requested/limit/kind and no partial snapshot. Objects are not invisibly dropped. |
| EN096 | Fill the cosmetic particle pool. | Explicit `RejectSpawn`/counter; authoritative physics/replay hash is unaffected. |

## 9. Assets and inert content

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN097 | Rename source with its sidecar; change settings, include or dependency. | Rename preserves `AssetGuid`; complete build-key change creates a new version; timestamps alone are insufficient. |
| EN098 | Lose a required dependency, introduce a cycle or fail the first shader build. | `Failed` ticket/activation refusal; no automatic placeholder in the player. |
| EN099 | Use invalid sparse glTF ranges, multiplication overflow or remote URI. | Reject before allocation/network access; no out-of-root read. |
| EN100 | Require unsupported compression, morph targets or five skin weights. | Explicit unsupported result or recorded opt-in largest-four approximation with removed counts; no full-glTF claim. |
| EN101 | Test atlas padding, premultiplied color,17 pages and a3x3-to 1x1 general texture mip. | Two-pixel extruded border/atlas mip 0 only; general mip averages all 9 source texels in linear space; page overflow is an error, not corruption. |
| EN102 | Fail/cancel import before publication. | Old version remains visibly active; new version is not admitted; temporary owners reclaim safely. |
| EN103 | Fill CPU asset budget with all versions pinned. | `AssetBudgetExceeded`; no eviction of live leases; GPU/audio lifetimes remain independent. |
| EN104 | Validate 128-byte EPKG2 header, binary EIDX tables and EBIN1 schemas; migrate a legacy EPKG1 source. | Exact [10](10-binary-data-and-authoring-formats.md) offsets/counts/flags/alignment/digests, no runtime JSON index; migration validates old bytes and emits fresh container/schema/profile identity. |
| EN105 | Duplicate GUIDs, overlap payload/header ranges or set reserved bits. | Reject before any scene/asset activation. |
| EN106 | Brotli expands beyond declared size, ends early or has invalid trailing data. | Decode/budget failure; no unbounded allocation or successful partial payload. |
| EN107 | Put C#, HLSL, DLL or arbitrary URI/path in inert content. | Reject; no execution, attacker-chosen extraction or network fetch. |
| EN108 | Supply a valid hash from an unsigned/unknown publisher. | Integrity does not confer executable trust; the package remains inert. |

## 10. UI, editor, and modules

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN109 | Lay out grid/flex at 150% DPI with nested scrolling, hidden/collapsed children, undersized nine-slice and recycled virtual rows. | DIP measurement, physical edges and hit tests agree; Hidden retains size/Collapsed does not; borders never invert; recycled rows preserve stable item selection, not prior slot state. |
| EN110 | Introduce layout cycle or exceed four recalculation passes. | `LayoutNotConverged` rejects new hit-test state; previous layout remains visibly active; no busy loop. |
| EN111 | Release a button inside/outside; Escape a slider preview; destroy capture or open a modal over held gameplay input. | `One` activation or cancellation; slider restores initial value without undo commit; `CaptureLost` occurs once; modal traps focus and [Chapter 14](14-custom-input-actions-and-text.md) cancels/disarms masked gameplay controls. |
| EN112 | Edit combining marks, surrogate pairs, emoji and Arabic/Latin/CJK text. | Grapheme cursor, shaping, bidi and line breaking are correct; missing glyphs are visible and diagnosed; no string reversal/`char` splitting. |
| EN113 | Cancel IME preedit; commit text; apply a stale/out-of-range deletion intent. | Preedit is not saved/game input; commit is delivered once; invalid intent rejects/resynchronizes. |
| EN114 | Deny clipboard access; paste more than 1 MiB. | Visible unavailable/length error, not fake empty text; sensitive text is not logged. |
| EN115 | Fail a multi-edit, exceed undo budget or complete one gesture. | Validated edits are all-or-nothing; budget notification is explicit; one undo reverses the whole gesture. |
| EN116 | Mutate a play world, `Stop`, then `ApplyPlayChanges`. | Edit world remains unchanged; explicit typed diff excludes transients; services release by protocol. |
| EN117 | `Complete` picking after resize, entity reuse or rebase. | Validate epochs/origin/stable GUID; map or refuse, never select the new entity by reused index. |
| EN118 | Register duplicate/cyclic trusted modules or an untrusted assembly. | Refuse before world construction; downloaded code does not execute. |
| EN119 | Reload pure data/shaders, then build a changed closed managed registry; fail candidate admission, save, shutdown and startup/migration execution separately, with live callbacks/native consumers. | Data/shader reload works in AOT. Code changes require explicit restart; failed build/admission leaves the current application active. Failed save/shutdown/startup is caller-visible, preserves artifacts/checkpoints and never frees live consumers or claims external-effect rollback. Successful relaunch uses fresh handles and validated `Standard` stable-ID remap. |
| EN120 | Request in-process managed assembly/type replacement from player/editor/tools, including an optional JIT comparison; change the editor's linked accessor registry and preserve unsaved documents. | `RestartRequired`, no collectible-context loading/unload claim or JIT-only bypass. Save and retire the affected executable before explicit relaunch; changed code/profile cannot inherit `StrictLocal` continuation. |

## 11. Persistence and replay

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN121 | Save/restore a fragmented `StrictLocal` world with nonassociative reductions and warm contacts. | New owner IDs invalidate old handles while slot/chunk/row/allocator ordering restores; stable references remap and canonical field bits do not renormalize; warm starts/sleep/clock/animation/PRNG continue identically without a hidden tick. |
| EN122 | Change map insertion order or CLR struct padding. | Canonical field/key encoding gives the same hash; padding/process hashes are irrelevant. |
| EN123 | Seed PCG32 with seed 42/stream 54 and read six words. | a15c02b7,7b47f409, ba1d3330,83d2f293, bfa4784b, cbed606e. |
| EN124 | Request uniform bounds 0/3/large `uint`. | Bound 0 fails; specified rejection threshold avoids modulo bias; exact state persists. |
| EN125 | Load unknown major, required schema or unavailable content/profile. | Load fails before activation; old world continues with `LoadFailed`. |
| EN126 | Exceed 16 migration steps, use an invalid reference or fail shape allocation. | Refuse the whole staged load; no partial replacement or native-owner leak. |
| EN127 | Crash before/after flush and before/after atomic replacement. | Valid committed primary/backup generation is recoverable; a prepared temp is not automatically treated as committed. |
| EN128 | `Complete` an older async save after a newer generation. | Older data cannot overwrite the newer target; report its named outcome/generation. |
| EN129 | Cancel save before versus after replacement. | Before commit, remove owned staging without changing target; after commit, report committed, not pretend cancellation. |
| EN130 | Use a network/cross-volume filesystem without required durability. | `DurabilityUnsupported` or explicit `BestEffort` with a weaker reported guarantee. |
| EN131 | Replay exact profile with different frame pacing and worker count. | Admitted inputs/canonical hashes agree; cosmetic frames are excluded; report earliest mismatch otherwise. |
| EN132 | Replay different OS/ISA/runtime/compiler/JIT-AOT-SIMD/coordinate/geometry profile, including.NET 10/11, old RH/new LH or BCL-public/`EngineGeometryBinary32Rows2`; overflow/`DropDebt`/required redacted input. | `StrictLocal` mismatch/discontinuity/incompleteness refuses before continuation; `StrictPortable` unavailable. Explicit validated `Standard` migration starts new lineage, never relabels old content or certifies exact cross-profile replay. Canonical negative-zero/finite bits/traversal remain explicit. |

## 12. Native/build/integration witnesses

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN133 | Run the same2D/3D reference scenes on Windows D3D12, Windows Vulkan and Linux Vulkan. | Camera/color/normal/material/depth and native-validation obligations pass in every lane; unrelated demos do not substitute. |
| EN134 | Negotiate non 48 kHz endpoints with WASAPI and `PipeWire`. | Internal 48 kHz is unchanged; endpoint format/frame conversion/phase are reported; no hidden unsupported downmix. |
| EN135 | Schedule a future Play, unrelated immediate `Stop` and an event inside a256-frame block. | Calendar/frame ordering is exact; a future command does not block the unrelated immediate command; late commands reject explicitly. |
| EN136 | Disconnect an endpoint with pending commands and invoke the configured retry. | Sample clock freezes, leases remain retained and receipts stay unapplied until valid processing; no silent endpoint/backend switch. |
| EN137 | Exercise real focus, IME, gamepad hotplug and device identity. | PF/IN cases pass on actual adapters without stuck controls; synthetic tests do not certify native behavior. |
| EN138 | Run fast-body/thin-wall, inside-query, capacity, stack and joint fixtures; force an unexpected solver fault. | PH expectations hold; `Step` throws `PhysicsSimulationException` after local rollback/fault marking without tick advance. `Query` `Complete`-with-zero-hits differs from `Indeterminate`/`BufferTooSmall` and default Uninitialized; no tunneling or partial-success fallback from exhausted bounds. |
| EN139 | Run headless 1000 ticks, save, then 500 versus uninterrupted 1500. | Canonical exact-profile hashes/stable references agree; no native display/GPU/audio endpoint opens. |
| EN140 | Build a developer checkout with a compatible newer .NET 11.0 SDK; separately resolve supported exact qualification SDK/runtime pins and test RC support expiry, GA release gate, v1/v2 CPU and mandatory AOT artifacts plus selected optional JIT/R2R comparisons. Compile optional runtime-async and updated-memory-safety lanes, including geometry bitcast bodies/generated adapters and separate safe consumers. | Developer builds use normal SDK roll-forward and need no qualification manifest or private SDK path. Qualification pins 11.0.100-rc.1.26425.128/11.0.0-rc.1.26425.128 match manifest only while supported; expired RC qualification refuses and shipment requires requalified GA. v1 unsupported, R2R v3 does not imply AVX2 minimum, AOT ISA recorded. Optional flags do not change public `Task`/ownership contracts or silently enter default C# 15. Proved interchange facades remain callable with unsafe compilation disabled; unsafe operation/caller annotations and generated blocks are audited without assuming today's BCL metadata is permanent. |
| EN141 | Pinned-toolchain restore/build/cook/package through standard `dotnet`/MSBuild on both RIDs. | Fresh zero exits and nonempty selected results; compiler/native/content manifests, SBOM and production licenses are complete. |
| EN142 | Execute chapter [28](28-diagnostics-performance-and-validation.md)'s benchmark scenes with every object selected; measure mandatory.NET 11 AOT/async/SIMD settings and matched delegate/interface-erased/concrete generic callback work; optionally compare recorded.NET 10/11 portable/JIT harnesses. Exercise tool stdout/stderr including an unterminated oversized line and opt-in crash diagnostics. | Actual median/p95/p99, startup, allocations, waits/ownership, constrained dispatch/code-size effects and numerical profiles reported against unmeasured targets; engine callable dispatch has zero steady managed allocation and no assumed universal inlining claim. No empty-filter/best-run/cross-runtime strict claim or rewrite from release-note performance. `Process` limit precedes decoding/line accumulation with safe exit/pipe cleanup; crash artifacts follow explicit local privacy/retention policy, not automatic upload/recovery. |
| EN143 | Opt-in application lane: run the separate game adapter's tutorial, circuits and offline growth. | Accepted game outcomes remain unchanged; no engine elements/currencies/FPS or hidden physics absence catch-up. This is not a core engine release prerequisite; no adapter means explicitly not-applicable. |
| EN144 | Perform direct and independent specification checks for an exact repository revision using checked-in documents and cited public sources. | File/heading links, acceptance-ID membership and cross-chapter declarations/constraints agree. This checks design integrity only; runtime, performance and native execution are not claimed. |

## 13. Authoring presentation witnesses

These editor/source-adapter observations exercise [25's authoring contract](25-ui-text-and-editor-tools.md#authoring-presentation-and-conversion), without changing engine numerical, coordinate or byte contracts.

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN145 | Display/commit a positive 90-degree 2D rotation and a parent-relative XYZ triplet `(90, 0, 90)` degrees. Repeat under a rotated parent and both graphics backends. | 2D `+X` rotates toward `+Y`. Fixed parent-axis X-then-Y-then-Z gives `Rx * Ry * Rz` and the corresponding application-order quaternion; the local unit axes map `+X` to `+Y`, `+Y` to `+Z`, and `+Z` to `+X` before the parent transform. Reversed composition is an explicit failing witness. Inspector units do not change canonical radians/quaternions, gestures or backend signs. |
| EN146 | Bind equivalent `q`/`-q`, exact and near middle-axis `+90`/`-90` degree orientations, and a registered scalar angle allowing multiple revolutions. Edit across a displayed branch boundary, then deliver an external revision. | Initial Euler branch/ranges and singular X-zero/nonunique indication follow chapter 25 and recompose within chapter 16's budget. Unrounded active edits do not rebranch per preview; stale edits invalidate. Display cannot canonicalize quaternion sign, wrap an untouched scalar angle or rewrite animation tracks; invalid conversion is visible, not fabricated angles. |
| EN147 | Display/edit one physical length in meters/centimeters/feet with default and nondefault `MetersPerUnitExponent`; exercise named SI profile presets/advanced exponent input, a derived quantity, canonical snap/range, overflow, unsupported unit and missing/stale spatial/window context. Compare source-adapter and inspector commits. | The admitted exact decimal/rational mapping gives the same canonical interpretation within its recorded final binary32 rounding budget; dimensional powers and actual profile/context are respected. Presets map to one validated integer exponent, not a rounded factor/runtime enum or a live-profile mutation. One canonical validation/snap grid applies. Invalid/unrepresentable edits reject before mutation; no global float position, double scaling, silent clamp or fallback value. |
| EN148 | Bind declared XY/XZ map bearings at 0/90/180/270 degrees; rotate the camera, then use a different registered north/east frame or omit it. | Cardinal default directions are north/east/south/west: `+Y/+X/-Y/-X` in XY and `+Z/+X/-Z/-X` in XZ. Registered references, not camera orientation, control other maps. Default XY Cartesian angle is `90 - bearingDegrees`; direction mapping and scalar target signs agree. Missing/incompatible reference rejects, and bearing rules do not leak to angular velocity or accumulated angles. |
| EN149 | Record typed field bits, source/document version, undo count, cook key and canonical bytes. Open inspectors; change degrees/length units/precision, equivalent Euler branch and scene/UI views; pan/resize at 150% scale. Then edit/save a different property, cancel a preview and reject a multi-edit. | Presentation-only actions invoke no setters and change none of the recorded semantic state. Saving another property preserves untouched typed values, not rounded display text. Cancel/rejection restores original bits without conversion drift. Only an explicit validated real edit commits intended fields; world/profile/schema interpretation and frozen `MetersPerUnitExponent` never change through preferences. |

## 14. Signed integer boundary witnesses

[02](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries) owns the default and exceptions; [05](05-owned-buffers-and-internal-collections.md)'s BF13 covers native-width CPU ownership. These cases exercise the actual generated/API/native/codec paths, not only a mathematical range model.

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN150 | Compile separate AOT consumers of signed counts/indices/dimensions, CPU ranges/pitches and logical/GPU extents. Supply negative, zero/end, maximum and overflowed values to buffers/chains, graphics descriptors/transfers/readback, input/schema adapters, physics commands/queries and diagnostic filters. Include valid negative priorities/registered orders/base-vertex adjustments. Decode unchanged unsigned wire fields just inside/outside the target signed range; exercise retained high-bit identities/context/collision/clip masks. | `int`/`nint`/`long` declarations and owner semantics agree; ordinary span lengths require no unsigned cast. Invalid arguments, checked products/narrowing and unrepresentable wire values reject before reservation/native use/activation; legitimate negative and zero rules remain intact. Native `nuint`/unsigned graphics arguments are validated conversions. Full-bit exceptions, permanent wire tags/kinds/layouts, geometry and PRNG semantics survive exactly. Narrow scalar promotion is not falsely presented as universal CLS compliance or a measured performance win. |
| EN151 | Place tick/frame/structural/episode/input/publication/audio/save/graphics sequence domains near their admitted signed maximum; attempt the next operation. Separately drive dropped/coalesced/clipped/underrun/histogram/replacement telemetry to its maximum. | Ordered counters check the next value and derived clock/range arithmetic before externally visible work, then refuse/fault under the existing owner protocol with unchanged committed state. They never wrap, clamp into successful work or reuse identity. Observational telemetry follows chapter 28's explicit `long.MaxValue` saturation and bounded diagnostic policy without blocking audio; it cannot hide an independent caller-visible failure. Unsigned owner/generation/epoch retirement remains its separate frozen protocol. |

## 15. Assembly, package, and feature-root witnesses

These observations exercise [29](29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag)'s physical/logical/package boundaries and [26](26-csharp-systems-plugins-and-hot-reload.md)'s selected registrations without changing existing subsystem semantics.

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN152 | Inspect the thirteen product projects, namespace ownership, public/member dependency rules and actual `.nupkg` target/RID contents. Restore separate Core, Physics, Input, Assets, Audio, Platform, Graphics/backend and integrated Runtime consumers using the selected toolchain/package versions. Inject a namespace-to-assembly-name assumption, same-assembly forbidden lower-layer/issuer access, project/package cycle, extra product DLL, misaligned engine release versions and broad registration root. Publish headless and each selected backend AOT profile on admitted targets, inspecting code/metadata/native inventories. | Core assembly types use root `TerraFX` and semantic Buffers/Numerics/Diagnostics namespaces, never `.Core` imports/qualified type names; seven coupled namespaces have one Runtime owner, with no legacy namespace-shaped product/shim or forbidden Runtime dependency from standalone services. One product assembly per library package/asset group; generator is build-only, optional metapackage has dependencies/metadata only, engine release versions align and project references use normal SDK-generated NuGet dependency ranges under chapter 29, not custom exact-version pack hooks. The documented direct/transitive closures agree; headless Runtime restores its declared adapter libraries but references no native graphics backend or editor/tools. Invalid graph/package/registration/issuer configurations diagnose before activation. Selected factory/generic/codec roots remain functional, missing roots refuse, unselected services do not initialize, and native shipped assets match the explicit publish manifest/dependency/license closure. No hypothetical size/performance or old assembly-qualified compatibility claim substitutes for real evidence. |

## 16. Research-integration witnesses

| ID | Setup / operation | Expected observation |
|---|---|---|
| EN153 | Separate public consumers build the first sprite and imported model with input, defaults/typed overrides and interactive/offscreen/headless profiles | Existing host/assets/scene/Renderer path needs no allocator/descriptor/native helper facade; default state is discoverable, transparent ordering/material/skin versions remain correct, failures are visible, CPU End is not native completion |
| EN154 | Exhaust upload/descriptor/command/transient budgets, wrap rings with live ranges, dispose resources and inject allocation/submit faults; inspect private allocator selection/accounting | Exact native alignment/heap class and actual completion vectors gate reuse; requested/block/live/retired bytes are distinguished, submitted descriptors never move, no frame-modulo/ObjectPool/allocator-lock reuse proof. Optional suballocation preserves committed baseline/public lifetime and owns failures |
| EN155 | Cook seam/hard-edge/material/skin meshes with invalid indices, degenerates and optional locked triangle/vertex remaps | Validate before native invocation; prescribed one-time conversion/normal/MikkTSpace/reorder order preserves all dependent streams, winding/parity/material partitions. None is baseline, locked cook is byte-reproducible, changed recipe changes identity; no implied meshlet/LOD support |
| EN156 | Fill read queues, complete out of order, cancel/corrupt/fault accelerated requests and request an unsupported GPU-decode path | Reserve all credits before bounded worker enqueue; per-request actual completion and verified decoded content precede activation/GPU Ready. CPU I/O and None/Brotli remain portable baseline; no whole-stream chunk split, owner-thread blocking, partial Ready or weakened integrity |
| EN157 | Request unsupported temporal/vendor effects despite hardware support; inspect an explicitly proposed history extension and force first-use/discontinuity/rebase/resize/projection/profile/device changes | Baseline reports UnsupportedEngineFeature. Before promotion, typed algorithm/backend/version/license/color/depth/motion/resolution/budget and history validity/retirement contracts are complete; old/live history cannot alias by frame modulo. No FSR/TAA/bloom implementation is implied |

## 17. Execution bookkeeping

The implementation harness records case ID, fixture/version, profile/lane, expected/executed assertion counts, result/errors/tolerances and fresh output revision. Data-driven fixtures may share setup, but case IDs cannot disappear into one broad smoke test. Property/reference checks strengthen these cases; they do not replace platform/physics/audio integration obligations.
