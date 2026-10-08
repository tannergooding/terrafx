# Shared numerical, lifetime, and clock contracts

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

These are engine laws, unrelated to any game's reservoir or economy.

## 1. Space and owned geometry

Public geometry uses `TerraFX.Numerics.Vector2`, `Vector3`, `Vector4`, `Quaternion`, `Matrix3x2`, and `Matrix4x4` from the `TerraFX.Core` assembly. [Chapter 06](06-owned-geometry-and-numerics-interchange.md) defines their layouts, defaults, arithmetic, application programming interface (API), and exact `System.Numerics` interchange.

The world is left-handed (LH): `+X` points right, `+Y` up, and `+Z` forward. The identity camera looks along `+Z`, so forward movement increases Z in that frame. A rotated camera's right, up, and forward directions are its quaternion-transformed `UnitX`, `UnitY`, and `UnitZ`, not fixed world axes.

Distance uses frozen `SpatialSettings.MetersPerUnitExponent`, defining exactly `10^e` meters per engine unit with default exponent zero; mass uses kilograms, time seconds, and runtime angles radians. Linear velocity uses engine units per second, angular velocity radians per second (rad/s). Physical defaults stated in meters are converted once with the exact dimensional scale and final destination rounding. [07](07-spatial-coordinates-and-indexing.md) defines all dimensional conversions. User interface (UI) and texture coordinates have top-left pixel origins. 2D physics uses the XY plane; a positive angle rotates counterclockwise (CCW) from `+X` toward `+Y`. Its `+Z` angular scalar is not a third simulated translation axis.

### Transform notation and order

Vector-transform formulas use **row vectors**, on both the central processing unit (CPU) and graphics processing unit (GPU). This is a multiplication convention, not a choice of handedness or memory layout.

| Symbol | Meaning |
|---|---|
| `S` | Scale matrix |
| `R` | Rotation matrix |
| `T` | Translation matrix, built from `LocalTransform.Position` |
| `M` / `world` | Model-to-selected-frame matrix, including the complete parent hierarchy |
| `V` / `view` | Selected-frame-to-camera matrix: the inverse rigid camera transform in that same frame |
| `P` / `projection` | Camera-to-clip projection matrix |
| `position` | Local-space homogeneous vertex position `(x, y, z, 1)`, not the model matrix |

The local matrix is `local = S * R * T`: scale first, rotate second, translate last. Within one admitted spatial frame, the hierarchy composes as `world = local * parentWorld`; a root translation is its spatial position evaluated in that frame. The model-view-projection (MVP) chain is `clip = position * M * V * P`, equivalently `position * world * view * projection`. These operations apply left to right. A column-vector source expresses the equivalent chain in reverse order.

`Mij` denotes the element in one-based row `i`, column `j` in equations; the public matrix indexer is zero-based, `matrix[i - 1, j - 1]`. Elements `M41`, `M42`, and `M43` contain translation. [Chapter 06](06-owned-geometry-and-numerics-interchange.md) exposes copied rows and read-only element access, not sixteen mutable public fields. Shader matrices explicitly use `row_major` storage and `mul(rowVector, matrix)` multiplication. A source format's column-vector matrices are transposed once at import; there is no casual transpose at every draw. [Chapter 16](16-scenes-transforms-and-cameras.md) owns hierarchy and camera behavior; [Chapter 18](18-shaders-and-material-pipeline.md) owns packed shader layout.

`Quaternion.Compose(first, second)` applies the first rotation and then the second, matching row-matrix `firstRotation * secondRotation`. It is `Multiply(second, first)`; ordinary Hamilton multiplication retains its mathematical order. Use the named operation when expressing application order. [06](06-owned-geometry-and-numerics-interchange.md) specifies the operations; [08](08-binary32-kernels-and-predicates.md) explains the clean-slate names and profile revision.

Reuse the .NET base class library (BCL) component algebra unchanged: `cross(a, b) = (a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X)`, Hamilton products, and the component behavior of `Quaternion.CreateFromAxisAngle` and vector transformation. A positive `+Z` rotation maps `+X` toward `+Y`; positive `+Y` maps camera-forward `+Z` toward `+X`; positive `+X` maps `+Y` toward `+Z`. These rotations have the left-hand physical sense in this world. No blanket cross-product or quaternion negation is made.

Position, linear velocity, force, and direction are **polar** vectors. Angular velocity, torque, angular impulse, and quaternion vector parts use the **axial** convention implicit in that algebra. Axial quantities acquire an extra sign under a reflection; the conversion below makes this distinction explicit. [Chapter 22](22-custom-physics-world-and-solver.md) preserves its impulse and integration equations.

Render triangles and cooked 3D face loops are clockwise (CW) when viewed from their front or outside. Their numerical edge cross produces the outward normal; signed-volume formulas remain unchanged. 2D convex boundaries remain CCW with positive signed area. Physics boundary order is not render-triangle index order; [Chapter 23](23-custom-physics-shapes-queries-and-ccd.md) defines dimensional cooking and query geometry.

For a right-handed (RH) source converted by Z reflection, define the diagonal matrices `H3 = diag(1, 1, -1)` and `H4 = diag(1, 1, -1, 1)`. Polar row vectors map as `vEngine = vSource * H3`; axial row vectors map as `aEngine = -aSource * H3`. Row transforms map as `mEngine = H4 * mSourceRow * H4`; quaternion `(x, y, z, w)` maps to `(-x, -y, z, w)`. Source column-vector matrices first transpose into the row convention. [Chapter 11](11-assets-import-streaming-and-packages.md) owns the corresponding normal, tangent, winding, skin, and animation import conversions. Arbitrary application fields require declared migration semantics, not a generic `Vector3` sign patch.

### Projection, depth, and screen coordinates

Clip space is the projection output before division by `clip.W`. Normalized device coordinates (NDC) are obtained after that division: X and Y lie in `[-1, 1]`, and depth lies in `[0, 1]`. Depth uses **reversed Z**: near is 1, far is 0, depth clears to 0, and comparison is `GreaterEqual`.

For LH perspective, let `f` be the vertical field of view (FOV), `a` the aspect ratio, `n` the near distance, and `F` the finite far distance. The nonzero matrix entries are `M11 = 1 / (a * tan(f / 2))`, `M22 = 1 / tan(f / 2)`, `M33 = -n / (F - n)`, `M34 = 1`, and `M43 = F * n / (F - n)`. All other entries are zero. Thus `clip.W = viewZ`, where `viewZ` is the vertex's camera-space Z coordinate; positive Z is in front.

Explicit infinite far uses `M33 = 0`, `M43 = n`, and `M34 = 1`, keeping the same XY scale. Orthographic depth uses `M33 = -1 / (F - n)`, `M43 = F / (F - n)`, and `M44 = 1`, with the ordinary XY scale and offset. Increasing positive `viewZ` moves away from the camera while reversed depth decreases. World direction and depth-buffer reversal are independent.

Perspective requires finite `0 < f < pi`, `aspect > 0`, `near > 0`, and either finite `far > near` or explicit `Infinite`. Orthographic requires finite `far > near`, positive finite height, and positive finite aspect; its near plane may be zero or negative. Derived matrix entries must be finite and inversion must succeed. [Chapter 16](16-scenes-transforms-and-cameras.md) defines viewport-derived versus explicit aspect. `System.Numerics` normal-depth factories are not these reversed-Z matrices; RH `LookAt` or `LookTo` output is not interchangeable with an LH camera frame.

For a physical-pixel viewport with origin `(x0, y0)`, width `w`, and height `h`, screen mapping is `screen.X = x0 + (ndc.X + 1) * w / 2` and `screen.Y = y0 + (1 - ndc.Y) * h / 2`. Direct3D 12 (D3D12) uses its normal viewport. Vulkan uses `viewport.Y = y0 + h` and `viewport.Height = -h`; there are no per-shader Y patches. [Chapter 21](21-2d-and-3d-rendering.md) maps CW geometry to D3D12 `FrontCounterClockwise = false` and Vulkan `VK_FRONT_FACE_CLOCKWISE`. A negative transform determinant flips front-face parity once.

World positions use signed 32-bit chunk indices and binary32 local offsets, with default chunk width 4096 units. Closed chunk geometry includes both endpoints; persistent point encoding is uniquely normalized. Bounded physics calculation frames retain the default `[-4096, +4096]`-meter extent, independently of the total addressable world. [07](07-spatial-coordinates-and-indexing.md) owns nearby arithmetic, precision admission, units, and sparse indexing. A rebase changes derived `SpatialFrame` caches and `FrameRevision`, not persistent positions, velocities, or cell identity. [16](16-scenes-transforms-and-cameras.md) owns its transaction. No global triple of doubles or hardware FP64 is required.

Color inputs declare linear or sRGB encoding. Arithmetic, lighting, and blending use linear values; sRGB textures decode and final standard dynamic range (SDR) output encodes once. Red-green-blue-alpha (RGBA) color versus vector is semantic metadata, not a second private math library. Alpha and premultiplication are explicit per material and pipeline.

## 2. Four clocks

| Clock | Source / behavior |
|---|---|
| Monotonic host | privately selected/injected monotonic source, initially `TimeProvider.GetTimestamp`/`GetElapsedTime`; wall pacing, profiling, input capture; never UTC duration differences |
| Simulation | nonnegative `long` tick index and configured integer rate in `[1, 1000]` Hz, default 60; per-world pause/time scale affects admission of ticks, not fixed solver dt |
| Presentation | interpolation between completed snapshots; never authority for physics/input outcomes |
| `Audio` | nonnegative `long` output sample index at mixer rate 48000; independent of simulation/graphics frame pacing |

Source selection/injection is an implementation/test boundary under [02's public type policy](02-dotnet-api-memory-and-ownership.md#public-type-boundary), not a required public `TimeProvider`/`Stopwatch` parameter. Clock-domain values and elapsed-time conversion must retain their stated epoch/unit, ordering and overflow behavior when the backing source changes; raw source ticks are not automatically `TimeSpan` ticks. A public raw-tick boundary must specify its domain/frequency and conversion without requiring callers to infer a BCL provider's configuration. This does not add a public clock service or revise fixed-tick, pause, scale, input or sample-clock semantics.

Public durations use `TimeSpan`, cancellation uses `CancellationToken`. Fixed tick's ideal elapsed time is `n/rate` seconds; its `TimeSpan` boundary is `floor(n * 10000000 / rate)` ticks, computed with checked `UInt128` intermediates. Do not repeatedly add a rounded `TimeSpan` for 1/rate. `Physics` dt is fixed 1/rate as a numeric scalar; `TimeSpan` intervals may alternate 166666/166667 ticks at 60 Hz. Events can have sub-tick `TimeSpan` timestamps; no mandatory render-frame/physics-tick snapping of unrelated timers.

`TickIndex` is the next zero-based tick: tick 0 ends at `floor(10000000 / rate)` `TimeSpan` ticks; after committing tick 0 the completed tick count is 1. Snapshot/event tick IDs identify the zero-based executed tick, while elapsed-time conversion uses completed count. Ticks, frame/structural revisions, publication/command sequences and their optional receipt values use `long`: ticks/counts permit zero, live revisions/sequences start at one unless the owner states otherwise. Negative arguments reject; nullable receipts express absence, not a negative sentinel. Refuse a new step before either tick index or the derived signed `TimeSpan` boundary overflows; do not wrap or lose time in a cast. Other ordered counters refuse before their next checked value exceeds `long.MaxValue`, under [02's integer policy](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries).

Wall-to-simulation conversion retains fractional scaled elapsed time. `TimeScale` defaults to 1 and must be finite in `[0, 4]`; it is applied at host-control boundaries between ticks and frozen for each admitted tick, and zero pauses. `Input` mapping records the conversion segment; pause discards gameplay transitions/cancels active actions while still serving UI. Resume cannot replay paused input or produce an artificial press from a still-held control. Game-specific cooldown rates do not secretly scale the entire engine.

Pause/resume/time-scale are host-control operations applied between ticks even when no tick is admitted; otherwise a paused world could never resume. `Editor` single-step explicitly admits one ideal fixed tick while paused and records that control action. It does not consume the paused interval's gameplay input.

Per host pump default maximum 8 catch-up simulation ticks. If retained debt exceeds 250 ms, default overrun state pauses simulation and emits `SimulationOverrun` with actual debt; UI/diagnostics can continue. No silently discarded debt/successful replay. Explicit alternate `DropDebt` policy records a discontinuity and invalidates strict replay; deterministic/headless tests step directly, not through wall-time throttling.

Events at exactly a simulation boundary are assigned to that boundary's tick once; consume ordered input timestamps in `(previous,current]`, with epoch-zero events specially consumed by first tick. A tick's commands/callbacks follow [13](13-jobs-and-frame-pipeline.md), not callback arrival race. Global rendering rate may vary independently.

## 3. Identities, bounds, and numeric safety

`Entity` and other owned handles have nonzero `OwnerId`, `Index` and `Generation`; all-zero is invalid, never an existing object. The Core assembly's private allocator assigns `OwnerId` monotonically from the inclusive integer interval `[1, uint.MaxValue]` across the process, including independent hosts and standalone subsystems, using one private atomic identity allocator. Allocation exhaustion refuses without wrap/reuse; this allocator is not a global service locator or engine lifetime. Per-host counters are insufficient because a handle carries no `HostId` and must reject a foreign host's object. `Generation` starts 1; retire an index before generation wrap. `Entity` is three `uint` fields/12 logical bytes. Stable persistent globally unique identifiers (GUIDs)/schema versions are distinct from ephemeral handles and survive serialization; process owner IDs do not.

Graphics processing unit (GPU) handles additionally bind device epoch; swapchain images bind surface epoch. Asset identities combine stable `Guid` plus content digest/version; old snapshots pin old versions. No user-facing native pointer, process hash or recycled bare index masquerades as identity.

`SurfaceEpoch` changes on native surface replacement; `SwapchainEpoch` changes on backbuffer/swapchain replacement including resize. Views/picks/acquired images validate both plus `DeviceEpoch`. Loss immediately revokes admissions to the failed epoch; successful replacement increments it, refusing before `uint` wrap.

`Input` boundaries reject NaN/infinity, invalid enum/flags, negative sizes, overflow and unsupported ranges before mutation/native call. Use checked arithmetic for allocation sizes/count * stride/offset/end; native-sized length after validating representability. Default numeric zero is not a convenient error result. Internal trusted invariants need no repeated arbitrary `null` guards, but unsafe bounds/lifetime remain established.

Raw geometry values, arithmetic, and interchange in [Chapter 06](06-owned-geometry-and-numerics-interchange.md) are explicitly outside domain admission: IEEE nonfinite and signed-zero values are representable without an exception or basis conversion. Finite geometry constructors/helpers and scene/physics/audio/input/content boundaries keep their documented validation, using `IsFinite().All()` when reducing a complete geometry value. A raw default quaternion or matrix is zero, not a valid identity pose. Geometry profile `EngineGeometryBinary32Rows2`, spatial profile `FloatChunk32V2`, and frozen unit/chunk settings are recorded independently from coordinate profile.

## 4. Transactions, faults, and recovery

Argument misuse throws conventional `ArgumentException`/`ArgumentOutOfRangeException`/`ObjectDisposedException`; stale/mismatched ownership is explicit `InvalidOperationException`. I/O/cancellation use established `IOException`/`OperationCanceledException`. Device loss has `GraphicsDeviceLostException` plus native code/device epoch/operation context.

[02](02-dotnet-api-memory-and-ownership.md) owns the `bool` Try/out, domain-response, exception and task-fault policy. `Pending`/`Ready`/`Failed`/`Canceled` names ticket lifecycle, not a generic result API. Symbolic alternatives describe outcomes/payload applicability, not permission to expose a language union; protocol receipts may separately acknowledge expected rejection/terminal shutdown. [28](28-diagnostics-performance-and-validation.md) owns diagnostic fields and caller-visible failure independent of telemetry; `null`/default never means success.

Validate and reserve before an individual command/structural transaction commits; preflight failure has no partial transaction effect. This does not promise rollback of earlier arbitrary tick writes. After a native command/stream read is committed, cancellation stops observation/new work where possible, not pretend rollback. Pre-commit tick faults suppress that tick's external publication; [13](13-jobs-and-frame-pipeline.md), [22](22-custom-physics-world-and-solver.md) define local journals and whole-world recovery. After chapter [13](13-jobs-and-frame-pipeline.md)'s irrevocable world marker, subscriber/audio/render delivery failures have explicit independent outcomes and cannot undo the committed tick or silently mutate canonical entity-component-system (ECS).

The fixed coordinate profile identifier is `LeftHandedYUpForwardZ1`, recorded in build/import/save/replay manifests; it is not a per-world selectable handedness flag. Previous right-handed specifications/data are incompatible without an explicit `Standard` migration. Strict replay certifies one recorded execution under exact coordinate/engine/runtime/numerical/content profiles, not universal cross-platform floating-point determinism. GPU pixels and device-specific audio are observational outputs, not authoritative state hashes. Portability requires actual target revalidation; physics approximations remain declared.
