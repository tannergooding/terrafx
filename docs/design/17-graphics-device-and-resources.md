# Graphics device, resource, and command contracts

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Required profile

Backend is explicitly D3D12 or Vulkan; selection/adapter globally unique identifier (GUID) reported before creation. No OpenGL renderer or automatic hidden fallback. `Headless` Null renderer validates logical scene extraction without claiming graphics processing unit (GPU) output; offscreen native renderer uses real device/images and no swapchain.

| Capability | Portable core requirement |
|---|---|
| D3D12 | FL12_0, SM6.0; normal resource barriers/fences/root signatures, no enhanced-barrier dependency |
| Vulkan | 1.3; feature booleans `timelineSemaphore`/synchronization2/`dynamicRendering` enabled, graphics + compute queue; WSI maintenance1 only for interactive profile |
| Limits | max 2D texture dimension >= 8192; 3D >= 256; array layers >= 256; >= 4 color attachments; >= 16 sampled images/stage, >= 8 samplers/stage, >= 8 uniform buffers/stage; uniform range >= 16384 bytes |
| Required formats | RGBA8 UNorm/sRGB sampled + attachment; RGBA16Float sampled + attachment + storage; R8UNorm sampled for glyph coverage; R32Float sampled + storage; R32UInt storage + color attachment for picking; D32Float depth + sampled. Required uploads/copies/readbacks additionally validate actual transfer usages/footprints. |
| Required sampling | Sample count 1; 2D textures nearest/linear/mips; anisotropy optional and explicitly clamped/reported, not assumed |

Validate format usages independently; native enum presence is not support. Multisample anti-aliasing (MSAA) is excluded from first renderer; optional hardware sample counts are metadata only. Swapchain BGRA/RGBA output negotiated separately. No shader float16/64, wave-op, descriptor-indexing or scalar-block-layout feature assumption.

Skinned-normal diagnostics use a compute validation/readback pass, not vertex-stage storage atomics; no undeclared Vulkan `vertexPipelineStoresAndAtomics` requirement.

`GraphicsCapabilities` immutable data contract includes adapter/vendor/device/driver/application programming interface (API) versions, implemented/hardware features separately, actual limits/alignments/formats/heaps, surface modes and selected profile. `Unsupported` profile returns all missing requirements; never create half-functional device.

## 2. Typed resources and memory

Native device/resource ownership follows [02 section 4](02-dotnet-api-memory-and-ownership.md#4-native-memory-handles-and-disposal). A device implementation whose managed owner and native device form a single lifetime unit can directly own its private `ID3D12Device*`/Vulkan handles, without a second `SafeHandle` allocation/indirection or a public `SafeHandle` base. Backend substitution does not justify an additional handle-only owner with the same lifetime. The public `GraphicsDevice` remains the existing sealed host-owned facade; deterministic device cleanup participates in host shutdown, not a new independent public `Dispose` contract. Direct finalizer fallback still requires the native API's legal release context, active-use retention and actual GPU/presentation completion; unreachable managed state is not proof of GPU idleness.

`GpuBufferHandle`/`GpuTextureHandle`/`GpuPipelineHandle` contain device owner/epoch/index/generation; generation starts 1 and retires before wrap. Device rebuild changes epoch; all previous native handles unusable even when a new pool reuses indices. Central processing unit (CPU) asset version may survive and be uploaded afresh.

`BufferDescription`: `long` `ByteLength` > 0, Usage flags (Vertex/`Index`/Uniform/Storage/`CopySource`/`CopyDestination`), `MemoryKind(DeviceLocal/Upload/Readback)`, positive `int` stride optional for structured buffers, `DebugName`. Uniform declared ranges <= `min(65536, device limit)`, dynamic offsets aligned to `lcm(256, Vulkan uniform alignment)`; checked arithmetic. Upload buffers host-written/GPU-readable; readback host-readable only after copy completion. `DeviceLocal` is never exposed as a CPU `Span`.

Graphics descriptors use `int` dimensions, subresource/vertex/binding indices, strides and draw/dispatch counts; graph-buffer byte ranges use `long`. CPU upload lease offsets/counts and texture row/slice pitches use `nint`; buffer readback requests use a `long` GPU source offset and `nint` byte count, and copied texture readback pitches/byte extent fit `nint`. Validate inadmissible negatives, domain-specific zero rules, products/ranges and native/device limits before admission. `DrawIndexedArguments.BaseVertex` retains native signed adjustment semantics and is not rejected merely for being negative. Native unsigned sizes/indices are checked private conversions, not a reason for unsigned public counts. Device/surface/swapchain identity epochs retain their frozen `uint` protocol; full shader bit patterns/metadata layouts remain unchanged.

`TextureDescription` includes `Dimension2D`/`Dimension3D`, width/height/depth, `arrayLayers`, `mipLevels`, format, usage (`Sampled`/`Storage`/`ColorAttachment`/`DepthAttachment`/`CopySource`/`CopyDestination`), `sampleCount = 1`, and `DebugName`. Mip levels range from 1 through `floor(log2(maxDimension)) + 1`. 3D textures exclude arrays and cubes; cube views select 6 layers in face order +X, -X, +Y, -Y, +Z, -Z. All subresource ranges are checked. Unsupported depth storage/copy combinations are rejected; no implicit typeless reinterpretation is allowed.

Default per-device owned allocation budget 512 MiB and transient subbudget 128 MiB, configurable and checked against declared native heap/residency availability. Counters distinguish requested bytes, allocator blocks and driver budget estimates; they do not claim total driver memory. Native allocation-info/alignment queries decide placement, not hand-derived bytes-per-pixel assumptions. Budget reservation precedes allocation; unexpected native OOM releases reservations and throws contextual `OutOfMemoryException(or faults the request Task)`, never a successful invalid handle.

`Core` uses committed/native individual allocations for persistent resources initially; transient graph resources use pooled compatible blocks with explicit aliasing. Dedicated queue/residency eviction algorithms are outside first core; no automatic discard of live data. Required texture uploads and readbacks use copyable native footprints, including D3D12 row pitch/placement alignment; native footprints override assumed contiguous rows.

## 3. Uploads, mappings, and disposal

### Allocator and reuse policy

Allocation strategy is private and versioned: committed/dedicated persistent allocation remains the initial baseline above. A later qualified placed/suballocated policy may use D3D12MemoryAllocator or an equivalent backend implementation without exposing allocator objects or adding a public/native cross-backend dependency. Admission queries actual native size/alignment/heap/resource class, reserves requested and rounded block bytes, and separates allocation/residency availability from logical resource readiness. Thread-safe allocator calls do not prove GPU lifetime safety.

Upload rings, temporary descriptor pages and command pools each own their exact allocation/version and last-use completion vector. Retirement capacity is reserved before accepting work; no frame-index/modulo, CPU End/recording completion, allocator lock or generic ObjectPool.Return permits reuse. Submitted descriptor addresses never move; in-flight allocation relocation/defragmentation is outside baseline. A future relocation must create/copy a new backing version, patch only new bindings, and retain every old resource/descriptor/pipeline reference until actual completion. Diagnostics distinguish requested/live/retired/quarantined/block bytes and native budget estimates. Failed native allocation or uncertain submission retains or releases ownership according to actual consumption, never by guessing from a missing receipt.

`GraphicsDevice.CreateBufferAsync`, `CreateTextureAsync`, `CreateSamplerAsync`, and `CreatePipelineAsync` are bounded ordinary-caller requests that marshal native creation to the graphics owner. Successful completion returns the sealed owned `GpuBuffer`, `GpuTexture`, `GpuSampler`, or `GpuPipeline` with its device-epoch handle; it never supplies a successful invalid owner. Public signatures and descriptor members are in [30](30-public-api-review-surface.md).

Render thread owns `Create`/`Destroy`/Record/Submit; public ordinary callers use bounded request tickets or assets, not native calls from entity-component-system (ECS) workers. Request channel capacity 256, awaiting admission supports cancellation. Cancellation before acceptance changes nothing; after acceptance cancels observation where possible, not GPU storage lifetime.

Upload staging ring 64 MiB default. Reserve aligned ranges with pending fence ownership; wrap only if no overlapping live range. If insufficient space, wait bounded on the earliest protecting fence or reject `UploadCapacityExceeded`; never overwrite live bytes. Oversize admitted uploads use a separately budgeted dedicated staging owner. Caller `ReadOnlySpan` is copied during the synchronous admission call; asynchronous uploads acquire an independent owned buffer/chain sublease before admission. Its operation task becomes terminal only after actual source consumption ends, and any still-live native staging remains fence-owned.

Upload `Ready` means all required copy/transition work completed. A graph may consume earlier only through an explicit wait/dependency; the upload receipt is not `Ready` before its actual fence completes. A descriptor existing is not content readiness. Readback returns an owned CPU buffer after fence completion with format/row pitch/range; caller disposes it. Invalid ranges/device loss fail task, no zero-filled success.

`UploadTextureAsync` uploads one complete selected mip/layer/aspect, named by `TextureSubresource`, from a retained `BufferReadLease<byte>` with explicit source offset and `TextureDataLayout` row/slice pitches. Validate dimensions, format transfer support, usage, source extent and checked pitch arithmetic before acceptance. Pack the admitted source into the backend's queried copy footprint; source row layout is not native allocation layout, and 3D mip depth is distinct from array layers. Independent input credits survive caller disposal/cancellation until actual consumption ends.

`ReadbackBufferAsync` returns an `OwnedBuffer<byte>` for the exact requested range; `ReadbackTextureAsync` returns an owned disposable `TextureReadback` with immutable subresource/format/extents/pitches and an `AcquireReadLease` path. Both require valid same-device `CopySource` usage and retain the source through the actual transfer fence and map/copy completion. Texture padding exposed to callers is zeroed, never uninitialized native storage. A result becomes terminal only when transfer credits are safe to release; cancellation cannot free an in-flight source/staging range. Readback budgets and native layout validation precede work, and failed transfers never become zero-filled successful content.

`Dispose` revokes logical handles immediately, then queues resource/descriptors/pipeline/allocation/storage retirement against last-use vector `(graphics,compute,copy)` plus applicable presentation fence. `Core` compute/copy timeline entries 0 because all work uses graphics queue. Release only after every nonzero required queue value completed. Future queue support must obey same vector, not use graphics-only shortcut.

## 4. Descriptors and pipeline binding

No bindless baseline. The four semantic groups are Frame 0, Material 1, Object 2, and Pass 3; they map to D3D12 register spaces and Vulkan descriptor sets. Within each group, D3D12 registers are `b0` through `b3`, `t0` through `t15`, `u0` through `u7`, and `s0` through `s7`; Vulkan binding numbers are 0 through 3 for b, 16 through 31 for t, 32 through 39 for u, and 40 through 47 for s. Only statically declared bindings count; shader reflection checks cumulative per-stage/device limits.

D3D12 root signature has descriptor tables per nonempty category/group, at most 16 table DWORDs, no giant inline root constant payload. Vulkan equivalent set layouts. Descriptor sets/heap ranges are immutable after submission; update by allocate-copy-new-version and retire old ranges with fence. Shader-visible heap growth creates a new heap/version and binding identity; never relocate submitted descriptor addresses.

`PipelineDescription` identifies shader digests/reflection, vertex layout, target/depth formats, topology, blend/depth/raster states and binding layout version. Cache key includes all these plus backend/driver/compiler/numerical flags; pipeline mismatch fails recording. Persisted native cache is optional disposable acceleration with validated identity, not sole source of shaders.

`GraphicsDevice.CreatePipelineAsync(PipelineDescription, CancellationToken)` accepts a closed typed graphics or compute description and returns an owned `GpuPipeline`. `ShaderBytecodeInput` and `ShaderReflection` belong to `Graphics`, using `Core` buffer leases rather than an `Assets` dependency. Validate backend bytecode, reflected stages/layouts, device capabilities, target formats and binding version against the actual bytes. Independent stage-bytecode credits survive through actual backend creation; cancellation is not permission to release them early. Disposing the returned pipeline revokes later binding, while already admitted recording/submission retains its native state to the last-use fence. The recorder consumes its `GpuPipeline.Handle`, not a fabricated pipeline handle.

`CommandRecorder` is `scoped` synchronous render-thread access created by graph executor. It validates device/epoch, pipeline compatibility, declared resource access, byte/index/instance limits, and render-scope legality before native dispatch. It cannot escape/await/retain arbitrary mutable scene refs. Texture/buffer views bind declared subranges, not unbounded raw pointers.

## 5. Completion and command storage

Per frame slot command allocator/pools, temporary descriptors and uploads protected by the actual submission fence/timeline. D3D12 allocator reset only after all associated submitted lists complete and no list recording/concurrent reset; list reset is a different operation. Vulkan command pool reset follows all associated submissions. Recording failure discards unsubmitted command storage safely and emits diagnostic, never submits partial graph.

Public submission/presentation/fence timeline values use nonnegative `long` and increase monotonically from 1; zero means none. Refuse before `long` overflow, drain/recreate a new identified timeline only under device-wide safe transition. Private native fence/semaphore values retain their required unsigned representation, with checked conversion and no admission above the engine's signed maximum. Wait checks fresh completion value/native errors with timeout, not task existence. Device loss closes new admissions and rejects old epochs; chapter [27](27-state-serialization-replay-and-recovery.md) owns restoration.
