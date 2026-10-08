# Render graph, synchronization, and presentation

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Graph records and compilation

The public construction path is `Renderer.BuildGraph(snapshot, surface)` for interactive output or `Renderer.BuildGraph(snapshot, offscreenColorTarget)` for a real `GpuTexture` target. A raw `RenderGraph(RenderGraphOptions)` uses the corresponding typed constructor and exactly one target. Options bind snapshot/world/tick, dimensional frame/profile/revision, ordered camera IDs and positive physical extent. `Headless` permits logical extraction only; native graph construction/submission refuses without a real device. Offscreen graphs never acquire a window, WSI object or presentation fence.

`ImportBuffer`/`ImportTexture` expose exact ranges and `GraphImportOptions` with initial state, content validity and `WaitForLastUse`/`RequireIdle`. Track actual owner state/fences rather than trusting a caller's claim of readiness. `CreateTransientBuffer`/`CreateTransientTexture` start at version 0 with invalid contents. Passes declare `ReadBuffer`/`ReadTexture`, `WriteBuffer`/`WriteTexture` or named color/depth attachment writes; every write returns the next range-scoped version. IDs and versions are graph-owned, device-epoch checked and cannot cross graphs. Construction/declarations/`Compile` run on the graph-creator thread; compiled callbacks run only on the graphics owner.

Graph is built from an immutable `RenderSnapshot`. Pass record: stable `Name`/`Order`, kind Raster/Compute/Copy, logical resource-version accesses with subranges, attachment Load(Clear/Load/`DontCare`)/Store(Store/Discard), explicit predecessors and `scoped` recording callback. Imported resources declare actual native state/content validity/epoch/completion requirement; transient resources declare descriptions and initially invalid content.

Each Write produces a new version. Reads depend on its writer; writing the next version depends on prior writer AND all prior-version readers (WAR). A Load attachment implicitly reads the prior version; Clear/`DontCare` don't. Same-pass sampled/storage read plus write to overlapping texture range is prohibited except declared storage read-write with explicit intra-pass algorithm/barriers; attachment load/blend is native attachment access, not sample feedback.

Compilation validates handles/ranges/stage usage/formats/layouts, initialized reads, write-after-discard, feedback, cycles, conflicting aliases and imports. Stable topological order by declared `Order` then `Name`, not insertion/hash traversal. Culling only unused passes with no declared side effect; Present/Readback/external callback are side effects. Compute/Copy execute on graphics queue in core; no fake asynchronous overlap.

`Compile` returns a `RenderGraphCompileResponse`: `Compiled` has an immutable non-null plan and no rejection diagnostics; `Rejected` has diagnostics and no plan. Every imported version's declared access/binding/write lies inside its checked imported range. Submission revalidates active target/resource epochs and acquires independent pins before acceptance; compilation alone does not grant native lifetime.

Subresource tracking is mip/layer/aspect for images, bounded byte intervals for buffers. An unwritten mip/layer/range is invalid even if another range was cleared. First overwrite covers its declared range, not inferred whole allocation. Discard ends content validity after pass; transition state remains tracked.

## 2. Barrier ownership

### Future effect and temporal-history gate

Core does not implement temporal upscaling/FSR/TAA/bloom/motion blur. Hardware/vendor presence cannot report implemented engine support. Before accepting any such extension, its selected typed descriptor must pin implementation/ABI/shader/license versions, backend support, formats/color/depth conventions, input/output resolution, motion-vector direction/scale/jitter, resource access and complete algorithm/quality acceptance. Current capabilities return UnsupportedEngineFeature for unimplemented requests.

An extension with history additionally owns per-camera/snapshot/device-epoch history identity, first-use initialization and explicit invalidation on camera discontinuity, rebase, resize, projection/profile change, effect setting/version change or device rebuild. Import history only with recorded valid content, graph dependencies and actual last-use completion; old/new histories cannot alias while either is live. History/scratch budgets and retirement are reserved before acceptance. No frame-slot modulo or stale previous-image success is sufficient. This is the acceptance boundary for later features, not a baseline promise to implement an unspecified vendor effect.

Graph executor owns all state/barriers; pass cannot secretly transition or access an undeclared resource. Required states: `CopySource`/Destination, Vertex/`Index`/`UniformRead`, `ShaderSampledRead`, `StorageRead`/Write, `ColorAttachment`, `DepthRead`/Write, Present. Host writes become visible through specified submission/memory dependencies.

D3D12 maps usages to legal legacy resource states, emits transitions and UAV barriers for storage ordering even if state unchanged, alias barriers at placed allocation identity changes. Upload heap remains GENERIC_READ; readback remains COPY_DEST. Present/common ambiguity is handled by explicit imported swapchain state, not arbitrary default.

Vulkan synchronization2 maps prior producer stage/access to consumer stage/access and image layout, including storage RAW/WAW dependencies without layout changes. Present ownership/acquire signals are external dependencies; queue-family transfer only when actual present family differs, paired release/acquire. `Core` uses concurrent sharing for graphics/present differing families to avoid invented ownership transfer omissions; creation records families. No ALL_COMMANDS/MEMORY_READ_WRITE blanket used as proof of correct layout.

Resource last-use updated by every submitted command containing a read/write/view, including indirect copy and pipeline/descriptor references. Submit signals completion after graph work. Submission failure retains all uncertain native ownership until device fault/teardown guarantees permit reclamation.

## 3. Transient aliasing

Lifetimes use first/last scheduled access, including side-effect/readback exports and graphics processing unit (GPU) completion, not lexical declaration order. Two resources may reuse a compatible block only if lifetimes don't overlap in submitted execution, native requirements/alignment/heap class compatible, and a discard/new-content initialization plus native alias transition is emitted. Never alias immutable snapshot data or exported/readback resources before their protection ends.

The default frame-slot count is 3; configurable integer counts lie in `[1, 4]`. A different slot's transient allocation remains protected by its own last-use timeline, so graph lifetime intervals alone cannot alias across live slots. Deterministic allocator first-fit aligned block order; reject budget overflow before submit, retain previous completed frame with an explicit failed-frame event.

## 4. D3D12 presentation

DirectX Graphics Infrastructure (DXGI) flip-discard swapchain, requested 3 buffers (clamped to supported profile), waitable frame-latency object, standard dynamic range (SDR) RGBA/BGRA UNorm with matching sRGB RTV when legal. If hardware sRGB output attachment unavailable, explicit final shader sRGB encoding to UNorm; never both.

Acquire the actual back-buffer index. Wait for submitted commands protecting that buffer/frame slot, transition PRESENT -> RENDER_TARGET -> PRESENT, order `ExecuteCommandLists` and Signal on the graphics queue, then Present (vsync default 1). The fence value protects submitted GPU work; native DXGI resize/release requirements additionally apply.

Resize: owner publishes latest nonzero client physical extent; render thread stops old submissions, waits all referring graphics work, releases old backbuffer refs/descriptors, invokes `ResizeBuffers`, rebuilds views, increments `SwapchainEpoch`. `SurfaceEpoch` remains unchanged unless the native surface was replaced. `Zero` size/minimized skips acquire/present without creating zero-size resource; simulation/audio continue. Occluded status pauses presentation and periodically tests visibility through native status, not a busy loop. Tearing enabled only explicit option plus feature/fullscreen/vsync compatibility.

## 5. Vulkan acquire, present, and retirement

Interactive admission requires `VK_EXT_swapchain_maintenance1` or promoted KHR equivalent feature enabled; normalize aliases but enable an actually advertised supported path. Use its present-fence structure to prove each queued presentation's native resources no longer in use. An ordinary submit fence/timeline or Queue/`DeviceWaitIdle` is NOT substitute proof for presentation completion.

Enable the selected path's registry dependencies, not just its device feature: VK_KHR_surface and the selected Win32/Wayland/Xlib window-system integration (WSI) extension, VK_KHR_get_surface_capabilities2, and matching VK_EXT_surface_maintenance1 or VK_KHR_surface_maintenance1 at instance creation; VK_KHR_swapchain and matching swapchain_maintenance1 at device creation. Vulkan 1.3 satisfies the promoted get_physical_device_properties2 dependency. Enumerate/validate required entry points and actual surface support before creating the interactive device/swapchain; missing dependency refuses admission. `OffscreenNative` does not enable this instance/device WSI dependency chain.

Surface format prefer `BGRA8_sRGB` then `RGBA8_sRGB` in SRGB_NONLINEAR; otherwise explicitly supported UNorm with shader encoding. FIFO mandatory default; Mailbox/Immediate require explicit supported selection. Requested image count min + 1 capped by nonzero max; actual returned count is authority. Extent comes from capabilities or clamped physical window extent, never logical device-independent pixel (DIP) dimensions.

Frame-slot indexed acquire semaphore/command storage protected by frame submission. Render-finished binary semaphore indexed by actual swapchain image, not frame slot. Acquire image, wait acquire semaphore in submission, render, signal that image's render-finished semaphore, Present waiting it and carrying a distinct unsignaled present fence. Reuse image semaphore after actual reacquire/wait guarantee or completed previous present fence.

Acquire Timeout/`NotReady` makes no submission and keeps fences valid. OUT_OF_DATE requests recreate; SUBOPTIMAL may complete current present then recreate. Do not reset a fence unless the operation that will signal it is actually admitted. If acquired image's rendering fails before present, maintenance release-image path releases acquired ownership after any acquire wait/scheduled work is completed; binary semaphore state is accounted for, never just reset/recycle.

On resize, create new swapchain using `oldSwapchain` linkage when valid; old/new may coexist. Increment `SwapchainEpoch` at successful replacement, retaining native `SurfaceEpoch`. `Stop` acquiring/submitting old swapchain epoch, retire each old image's GPU-use timeline AND all queued present fences; release outstanding acquired images through maintenance path; then destroy old views/semaphores/fences/swapchain. Maximum 2 retiring generations; further resize waits bounded for oldest, coalesces pending extent rather than leaks unbounded swapchains.

Present fence must be observed signaled before reset/reuse/destruction. Track fences also when present returns suboptimal/out-of-date/native error according to whether native presentation was enqueued; uncertain ownership follows fault quarantine, not assumed completion. Shutdown uses the same retirement proof; minimized window is not evidence GPU/WSI complete.

## 6. Failure and reference examples

`Renderer.SubmitAsync(snapshot, plan, cancellationToken)` accepts only the exact snapshot/plan/target identities. Its completion follows actual GPU work and pin retirement, with a `RenderSubmissionReceipt` carrying the completion fence and epochs. Interactive output also carries a distinct non-null presentation sequence; `Surface.WaitForPresentationAsync` observes that sequence independently, and caller cancellation never proves native presentation retirement. Offscreen output has no presentation sequence and zero surface/swapchain epochs; its texture can be read back after the real GPU fence without claiming visible presentation.

`PresentationSource` is the final **linear SDR** color version after high dynamic range (HDR) tone mapping and UI composition. `SetPresentationSource` selects a valid single-mip/layer color version with the target's physical extent; a custom/raw graph must establish those semantics itself. The interactive terminal operation encodes to the surface format exactly once and never tone-maps again. Offscreen output writes that same linear SDR result to the selected texture format, encoding only when its sRGB format requires it. Ordinary/custom passes cannot rely on an implicit second tone map.

Upload write -> sample read creates a `CopyDestination` -> `ShaderRead` transition; two storage writes require a UAV/memory dependency; an uninitialized mip read fails graph validation before command recording. A Frame 0 fence signaling while its image-presentation fence remains pending may reset ordinary frame storage, but cannot free the render-finished semaphore/swapchain.

Device/surface loss increments relevant epoch only at successful replacement, invalidates pending tickets, and rebuilds from central processing unit (CPU) source through [27](27-state-serialization-replay-and-recovery.md). Driver wait timeout quarantines live resources; no caller cancellation authorizes use-after-free. Native validation must cover repeated resize/minimize/reacquire/unsubmitted failure and shutdown, not merely successful steady-state frames.
