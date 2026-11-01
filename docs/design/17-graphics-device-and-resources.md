# Graphics device, resource, and command contracts

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Required profile

Backend is explicitly D3D12 or Vulkan. The selected backend enumerates its native adapters in the API's preferred order and selects the first adapter that satisfies the requested profile; M04 exposes no adapter-selection override. Its native adapter identity is reported in `GraphicsCapabilities` before `EngineHost.StartAsync` succeeds. Vulkan uses the nonzero `VkPhysicalDeviceIDProperties.deviceUUID` bytes with the big-endian `Guid(ReadOnlySpan<byte>, bigEndian: true)` constructor. D3D12 builds the 16 bytes passed to `Guid(ReadOnlySpan<byte>)` from `AdapterLuid.LowPart` little-endian, `AdapterLuid.HighPart` little-endian, then the ASCII bytes `TFXD3D12`. These identifiers are native adapter identities, not portable or serialized asset identities. No OpenGL renderer or automatic hidden fallback. `Headless` Null renderer validates logical scene extraction without claiming graphics processing unit (GPU) output; offscreen native renderer uses real device/images and no swapchain.

| Capability | Portable core requirement |
|---|---|
| D3D12 | FL12_0, SM6.0; normal resource barriers/fences/root signatures, no enhanced-barrier dependency |
| Vulkan | 1.3; feature booleans `timelineSemaphore`/synchronization2/`dynamicRendering` enabled, graphics + compute queue; WSI maintenance1 only for interactive profile |
| Limits | max 2D texture dimension >= 8192; 3D >= 256; array layers >= 256; >= 4 color attachments; >= 16 sampled images/stage, >= 8 samplers/stage, >= 8 uniform buffers/stage; uniform range >= 16384 bytes |
| Required formats | RGBA8 UNorm/sRGB sampled + attachment; RGBA16Float sampled + attachment + storage; R8UNorm sampled for glyph coverage; R32Float sampled + storage; R32UInt storage + color attachment for picking; D32Float depth + sampled. Required uploads/copies/readbacks additionally validate actual transfer usages/footprints. |
| Required sampling | Sample count 1; 2D textures nearest/linear/mips; anisotropy optional and explicitly clamped/reported, not assumed |

Validate format usages independently; native enum presence is not support. Multisample anti-aliasing (MSAA) is excluded from first renderer; optional hardware sample counts are metadata only. Swapchain BGRA/RGBA output negotiated separately. No shader float16/64, wave-op, descriptor-indexing or scalar-block-layout feature assumption.

Skinned-normal diagnostics use a compute validation/readback pass, not vertex-stage storage atomics; no undeclared Vulkan `vertexPipelineStoresAndAtomics` requirement.

`GraphicsCapabilities` is an immutable device-generation snapshot containing `Backend`, `Profile`, `AdapterId`, `VendorId`, `DeviceId`, `AdapterName`, `DriverName`, `DriverVersion`, `ApiVersion`, `HardwareFeatures`, `ImplementedFeatures`, `Limits`, `Formats`, and `MemoryHeaps`. `AdapterId` is the backend-native identity described above; it is not an engine-generated selection token. The hardware feature list contains supported native features, while the implemented list is the subset the selected TerraFX backend actually enables and uses. Both lists are nonempty, sorted by numeric `GraphicsFeature` value, and contain no `None` or duplicate entries. Format entries are nonempty, sorted by `TextureFormat`, report actual supported `TextureUsage` independently, and list supported sample counts in ascending order. Heap entries are nonempty and sorted by native index; `BudgetBytes` is nullable when no budget estimate is available and may be zero when exhausted. The immutable arrays are copied at construction. `GraphicsLimits` reports actual positive limits and byte alignments: maximum 2D/3D texture dimensions, array layers, color attachments, sampled images/samplers/uniform buffers per stage, uniform range, uniform/storage/copy buffer alignment, texture row-pitch/placement alignment, compute group sizes/invocations, vertex-input attributes/bindings, and maximum buffer byte length. It is device-scoped and does not claim per-window surface formats, image counts, extents, or present modes; those belong to the exact `SurfaceCapabilities` snapshot in [19](19-render-graph-synchronization-and-presentation.md). `Unsupported` profile returns all missing requirements; never create a half-functional device.

`TerraFX.Graphics` owns `GraphicsBackend` (`None = 0`, `D3D12 = 1`, `Vulkan = 2`) and the opaque immutable `GraphicsBackendRegistration`. `None` is only the unselected default and is rejected for device startup. The selected backend package exposes a static `CreateRegistration()` method; the registration contains a private, stateless backend factory and no device, owner, adapter, surface, or native handle. An application references one backend package and passes its registration and matching backend value to `EngineHostOptions`. `EngineHost` is the only creator of `GraphicsDevice`; it issues a Core `OwnerId` through Runtime's existing issuer grant and passes only that value through the internal Graphics startup port. Graphics does not receive Core allocator access. Missing/mismatched registrations, unsupported capabilities, or failed native initialization fail host startup without a placeholder device or backend fallback.

The registration's internal factory is invoked on the dedicated Graphics owner thread. Device initialization enumerates the selected API's adapters, selects an adapter that satisfies the requested profile, and reports its stable adapter GUID and actual capabilities before `EngineHost.StartAsync` succeeds. Vulkan instance/device extensions are admitted only for the selected profile and actual platform kind; `OffscreenNative` requests no WSI extensions. Runtime maps the selected `PlatformHost` result to a Graphics-owned `GraphicsPlatformKind`; Graphics and backend assemblies never reference `TerraFX.Platform`. For window presentation, Runtime copies an exact live native-window binding from Platform into an internal `GraphicsNativeSurfaceBinding` containing only the selected backend's required native handles and surface epoch. That binding is not public, is never cached as a Platform object by Graphics, and is retired through the Graphics owner after GPU and presentation completion.

The internal startup port is `GraphicsDevice.CreateAsync(uint ownerId, GraphicsBackendRegistration registration, GraphicsDeviceProfile profile, GraphicsPlatformKind platformKind, CancellationToken)`. `GraphicsDeviceProfile` is `OffscreenNative = 1` or `Interactive = 2`; `GraphicsPlatformKind` is `None = 0`, `Win32 = 1`, `Wayland = 2`, or `X11 = 3`. Runtime maps `HostProfile` and the actual initialized `PlatformHost` backend to these Graphics-owned values. `None` is required for offscreen startup; a native platform kind is required for interactive Vulkan instance-extension selection. D3D12 interactive admission requires Win32. The call issues no successful facade until the internal `IGraphicsBackendFactory` selected by the registration has created a real device and passed the full profile capability gate. The factory has `GraphicsBackend Backend { get; }` and `IGraphicsBackendDevice Create(uint ownerId, GraphicsDeviceProfile profile, GraphicsPlatformKind platformKind, CancellationToken cancellationToken)`; the backend object is created and first used only on the dedicated Graphics owner thread. The backend port remains internal and has no callback or type reference to Runtime, Platform, or Hosting.

`GraphicsNativeSurfaceBinding` is an internal value with constructor `(GraphicsPlatformKind platformKind, nint nativeDisplayOrInstance, nint nativeWindowOrSurface, uint surfaceEpoch)`. `surfaceEpoch` is positive; Win32 receives `(HINSTANCE, HWND)`, Wayland receives `(wl_display*, wl_surface*)`, and X11 receives `(Display*, Window)` in the two native-handle slots. No native handle is exposed by `Surface`, `EngineHost`, or a public descriptor. Runtime obtains the live tuple from the exact Platform window generation and forwards it once to the Graphics owner for surface creation; it does not retain or reinterpret a stale binding. Graphics verifies the platform kind matches the initialized device and binds the returned `Surface` to the exact host/window/surface epoch. Platform keeps ownership of the window and pump; Graphics owns the backend surface/swapchain and its GPU/presentation retirement.

The internal startup types are:

```csharp
public enum GraphicsDeviceProfile
{
    None = 0,
    OffscreenNative = 1,
    Interactive = 2,
}

internal enum GraphicsPlatformKind
{
    None = 0,
    Win32 = 1,
    Wayland = 2,
    X11 = 3,
}

internal interface IGraphicsBackendFactory
{
    GraphicsBackend Backend { get; }

    IGraphicsBackendDevice Create(
        uint ownerId,
        GraphicsDeviceProfile profile,
        GraphicsPlatformKind platformKind,
        CancellationToken cancellationToken);
}

internal interface IGraphicsBackendDevice : IDisposable
{
    GraphicsCapabilities Capabilities { get; }
}
```

`GraphicsDevice.CreateAsync` starts its dedicated graphics owner thread, invokes the selected factory there, and returns only after `IGraphicsBackendDevice` has created a real native device and passed profile admission. Backend packages implement `IGraphicsBackendFactory` and return their live native owner; they never create or return the public facade. The factory checks cancellation between native operations and unwinds every successfully acquired native object on cancellation/failure; it does not pretend an in-progress native call was canceled. `IGraphicsBackendDevice` is an internal owner port, not a feature-success façade: every public Graphics operation admitted by the selected profile must be implemented by that backend or reject with its typed/native failure. The bounded request channel and last-use retirement remain owned by `GraphicsDevice`, not by the registration token.

## 2. Typed resources and memory

Native device/resource ownership follows [02 section 4](02-dotnet-api-memory-and-ownership.md#4-native-memory-handles-and-disposal). A device implementation whose managed owner and native device form a single lifetime unit can directly own its private `ID3D12Device*`/Vulkan handles, without a second `SafeHandle` allocation/indirection or a public `SafeHandle` base. Backend substitution does not justify an additional handle-only owner with the same lifetime. The public `GraphicsDevice` remains the existing sealed host-owned facade; deterministic device cleanup participates in host shutdown, not a new independent public `Dispose` contract. Direct finalizer fallback still requires the native API's legal release context, active-use retention and actual GPU/presentation completion; unreachable managed state is not proof of GPU idleness.

`GpuBufferHandle`/`GpuTextureHandle`/`GpuPipelineHandle` contain device owner/epoch/index/generation; generation starts 1 and retires before wrap. Device rebuild changes epoch; all previous native handles unusable even when a new pool reuses indices. A retained CPU asset lease may survive and be uploaded afresh.

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
