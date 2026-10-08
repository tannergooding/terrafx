# General-purpose C# engine specification

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

Revision: 0.21. [37](37-completeness-and-audit.md) records specification evidence and implementation qualification.

This is a separate engine design, not the game design's technology appendix. It serves 2D games, 3D games, editors, visualization, and headless simulation. The existing tower game is one demanding adapter example; its elements, currencies, twelvefold laws, progression and content are not engine services.

## 1. Product and scope

A native desktop engine written primarily in modern C#, targeting .NET 11/C# 15 for its first release. Public application programming interfaces (APIs) follow applicable .NET Framework Design Guidelines and current library guidance, not the legacy .NET Framework runtime. [29](29-project-setup-build-and-deployment.md#2-sdk-selection) owns the exact qualification pins, support checks, optional .NET 10 comparison lane, and required .NET 11 general availability (GA) requalification before shipment.

[02](02-dotnet-api-memory-and-ownership.md) owns public API, failure, asynchronous-operation, and ownership rules. New language/runtime features do not override those contracts or introduce a mandatory preview dependency.

The canonical world is left-handed: +X right, +Y up, +Z forward; the identity camera faces +Z. [03](03-shared-conventions-and-clocks.md) owns profile `LeftHandedYUpForwardZ1`, row-vector algebra, reversed depth, and color/clock conventions. glTF is an import choice, not the authority for engine coordinates; [11](11-assets-import-streaming-and-packages.md) owns its complete boundary conversion.

Public geometry uses [06](06-owned-geometry-and-numerics-interchange.md)'s small `TerraFX.Numerics` family with explicit `System.Numerics` interchange and component masks with explicit reductions. [07](07-spatial-coordinates-and-indexing.md) combines integer cells with bounded float offsets and exact decimal unit exponents; [08](08-binary32-kernels-and-predicates.md) supplies kernels without required hardware binary64. Geometry profile `EngineGeometryBinary32Rows2`, spatial profile `FloatChunk32V2`, units, and runtime identity remain separate compatibility dimensions. [10](10-binary-data-and-authoring-formats.md) pins a versioned SHA-256 UUIDv8 recipe and explicit legacy migration, not a mutable identity algorithm.

The engine owns entity-component-system (ECS), scheduling, scenes, resource abstraction/rendering, asset/runtime tools, audio mixing/spatialization, input/actions, and 2D/3D rigid-body physics. Native operating system (OS)/graphics processing unit (GPU) APIs, low-level bindings, shader compilation and font shaping are reusable foundations, not substitute game engines. No SDL/MonoGame/Unity/Godot, Box2D/Jolt/Bullet/Havok, OpenAL/FMOD/Wwise engine dependency is part of the core.

| Required axis | Version-one contract |
| --- | --- |
| Platforms | Windows 11 x64 and Ubuntu 24.04 LTS x64 desktop, CPU x86-64-v2 minimum; .NET-supported servicing OS variants recorded in build manifest |
| Graphics | D3D12 on Windows; Vulkan on Windows/Linux; neither is a secondary experimental renderer |
| `Headless` | Explicit no-window/no-GPU/no-device profile; same ECS, assets, physics and logical APIs, not a success-shaped GPU fallback |
| 2D | Sprites/atlases/tile layers, orthographic cameras, sorting, UI/text, animation, 2D rigid bodies and queries |
| 3D | Meshes/instancing, perspective/orthographic cameras, metallic-roughness materials, lights/shadows, skeletal animation, 3D rigid bodies and queries |
| Tooling | Own editor/runtime UI, scene/prefab/property/asset inspectors, undo/play isolation, data packages, diagnostics and command-line validation |
| `Audio`/input | Custom mixer/action processing with explicit platform device adapters and lifecycle |
| Extensibility | Trusted C# systems/modules in generated closed registries; runtime, editor and tools are AOT-compatible, with no dynamic managed assembly loading or sandbox claim |

[17](17-graphics-device-and-resources.md) owns the D3D12/Vulkan capability baseline. [19](19-render-graph-synchronization-and-presentation.md) owns additional interactive presentation requirements and retirement. Unsupported capabilities refuse the selected profile; they do not trigger a hidden backend switch.

`Core` uses one graphics queue; copy/compute work initially executes there with real barriers. Dedicated asynchronous queues, bindless descriptors, ray tracing, mesh shaders, high dynamic range (HDR) displays, VR, mobile, consoles, web, network replication, cloth/fluids/soft bodies and arbitrary downloaded executable plugins are excluded from version one. Capability reporting separates hardware support from implemented engine features. These exclusions prevent a general-purpose core becoming an incomplete promise of every engine feature.

The first renderer uses direct-light physically based rendering (PBR), constant ambient irradiance and optional unlit sky, not image-based lighting/post-effect promises. `Physics` supports the exact shape set and conditional translational continuous collision detection (CCD) defined in [Chapter 23](23-custom-physics-shapes-queries-and-ccd.md), not capsules or arbitrary rotating CCD. Application circle/sphere movement in [Chapter 32](32-end-to-end-integration-scenarios.md) demonstrates collision-aware root motion without advertising an engine character-controller/navigation service.

## 2. Sequential reading order

Read the numbered chapters in order. The groups below separate shared foundations from their consumers and place declarations, executable acceptance obligations, and implementation guidance after the subsystem contracts.

| Chapters | Purpose |
| --- | --- |
| 00-03 | Scope, reading conventions, public API policy, and shared numerical/clock contracts |
| 04-08 | Capacities, storage ownership, geometry, spatial data, and numerical kernels |
| 09-11 | Native semantic content, binary formats, and the asset pipeline |
| 12-16 | Entities, scheduling, input, platform hosting, and scenes |
| 17-21 | Graphics resources, shaders, render graphs, animation, and rendering |
| 22-28 | Physics, audio, UI/editor, modules, persistence, and diagnostics |
| 29-35 | Repository guidance, API declarations, consumer/integration scenarios, acceptance, and implementation milestones |
| 36-37 | Informative source rationale and revision evidence |

Forward references connect a shared concept to a later consumer or acceptance fixture; they are not a different reading sequence or an assembly dependency. The glossary's reference tables may be consulted as needed. Before implementation, use [35](35-implementation-roadmap.md)'s milestone prerequisites rather than treating chapter order as build order.

[29](29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag) separates logical namespaces from physical assemblies: the coupled world/scene/animation/render/UI/integration/host path is one `TerraFX.Runtime` library. Independently reusable foundations/services and native/tooling boundaries remain separate. Each library package contains one product assembly; applications normally reference Runtime and one selected graphics backend, not a package per namespace.

## 3. Document ownership

| ID / document | Authority |
| --- | --- |
| [01 terminology](01-terminology-and-reading-guide.md) | Reading/authority conventions; informative terminology reference |
| [02 .NET API/lifetime](02-dotnet-api-memory-and-ownership.md) | Public API rules, BCL reuse, unsafe/ownership/async boundaries |
| [03 conventions](03-shared-conventions-and-clocks.md) | Units, coordinates, matrix/clip/color/time semantics, identities, cross-system invariants |
| [04 capacities](04-capacity-and-workload-profiles.md) | Shared storage/budget defaults and capacity composition |
| [05 buffers/collections](05-owned-buffers-and-internal-collections.md) | Large unmanaged ownership/leases/windows/chains and private collection invariants |
| [06 geometry](06-owned-geometry-and-numerics-interchange.md) | Public geometry values, layout/defaults, arithmetic/interchange, helper guarantees and qualification |
| [07 spatial foundation](07-spatial-coordinates-and-indexing.md) | Units/cells, shared boundaries, frames, nearby arithmetic, indices and residency |
| [08 numerical kernels](08-binary32-kernels-and-predicates.md) | Binary32 algorithms, exact predicates, compensated motion and audio headroom/phase |
| [09 native content](09-native-assets-and-source-adapters.md) | Native semantic records and common source-adapter boundary |
| [10 binary formats](10-binary-data-and-authoring-formats.md) | EBIN1/EPKG2 byte/schema contracts, authoring adapters and migrations |
| [11 assets](11-assets-import-streaming-and-packages.md) | Import/cook transactions, specific source conversion, versions/loading/packages |
| [12 ECS](12-entities-components-and-queries.md) | `Identity`/archetypes/query/mutation/lifecycle |
| [13 scheduling](13-jobs-and-frame-pipeline.md) | Jobs, stages/access, snapshots/failures/shutdown |
| [14 input](14-custom-input-actions-and-text.md) | Events/action contexts/text/focus/controller routing |
| [15 platform](15-platform-host-and-devices.md) | Windows/Linux windows, pump/device/native lifecycle |
| [16 scenes](16-scenes-transforms-and-cameras.md) | Hierarchy/prefabs/transforms/cameras/rebase |
| [17 graphics resources](17-graphics-device-and-resources.md) | RHI, handles, descriptors, allocations/commands/capabilities |
| [18 shaders](18-shaders-and-material-pipeline.md) | HLSL/DXC/DXIL/SPIR-V, reflection/layout/build/reload |
| [19 render graph](19-render-graph-synchronization-and-presentation.md) | Passes/barriers/fences/aliasing/D3D12 and Vulkan presentation |
| [20 animation](20-animation-and-deformation.md) | Sprite/skeletal clips/blending/skinning/root motion |
| [21 rendering](21-2d-and-3d-rendering.md) | Draw extraction, sprite/mesh/material/light/shadow/postprocess behavior |
| [22 physics core](22-custom-physics-world-and-solver.md) | Bodies/contacts/solver/islands/transactions |
| [23 physics shapes](23-custom-physics-shapes-queries-and-ccd.md) | Exact supported shapes, detection/queries/CCD/numerical refusal |
| [24 audio](24-custom-audio-mixing-and-spatialization.md) | Voices/mixer/DSP/device/stream/2D-3D contracts |
| [25 UI/editor](25-ui-text-and-editor-tools.md) | Layout/text/focus/widgets/property editing/undo/play separation |
| [26 modules](26-csharp-systems-plugins-and-hot-reload.md) | Trust, generated AOT registrations, data reload and managed-code restart |
| [27 persistence](27-state-serialization-replay-and-recovery.md) | State/migrations/save transactions/replay/device failure |
| [28 observability](28-diagnostics-performance-and-validation.md) | Instrumentation/workloads/measurements/hardware/runtime acceptance |
| [29 setup](29-project-setup-build-and-deployment.md) | Assembly dependency DAG, repository/toolchain/native dependencies/build/test/release setup |
| [30 API baseline](30-public-api-review-surface.md) | Public types, constructors and member signatures |
| [31 consumer guide](31-public-api-consumer-guide.md) | Informative API navigation and usage; no independent declarations or defaults |
| [32 integration](32-end-to-end-integration-scenarios.md) | Coherent 2D/3D/editor/headless/native-failure/game-adapter flows |
| [33 physics acceptance](33-physics-acceptance-and-reference.md) | Required physics normal/edge/failure/reference observations |
| [34 acceptance](34-engine-acceptance-cases.md) | Required cross-system normal/boundary/failure observations |
| [35 roadmap](35-implementation-roadmap.md) | Implementation prerequisites, coherent milestones and handoff gates |
| [36 sources](36-primary-sources-and-rationale.md) | Informative primary references and decision rationale |
| [37 audit](37-completeness-and-audit.md) | Informative exact-revision evidence and implementation limits |

Requirements are normative by default; [01](01-terminology-and-reading-guide.md#1-reading-conventions) defines informative labeling and authority. Each owner above defines its contract; consumers refer to it rather than maintaining an alternative. API declarations own member shape, subsystem chapters own behavior, and acceptance cases own required observations. A contradiction is a defect to resolve before implementation, not permission to choose the preferred paragraph.

Design chapters record the reviewed contracts and acceptance boundaries; implementation progress does not rewrite them. [The project roadmap](../project/Roadmap.md) tracks milestone status, implemented units, blockers and implementation evidence. Project documentation and artifacts record actual build/test/tool profiles and results; a changed contract still requires an explicit design review.

## 4. Delivery and progression

[35](35-implementation-roadmap.md) defines M01 through M08 with prerequisite revisions, API/schema review, coherent implementation/regression/integration gates and the user-supplied foundation. "Version one complete" requires every axis above, not merely a triangle, window or ECS benchmark.

Evidence applies to exact revisions; changed contracts require affected revalidation. Native correctness, performance, visual/audio quality, numerical stability, usability, and shipping-platform qualification remain implementation obligations.
