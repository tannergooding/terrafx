# Implementation roadmap

This page tracks current milestone state and open gates; it is not implementation history. Milestone scope, dependencies, and acceptance criteria are defined in the [design roadmap](../design/35-implementation-roadmap.md). Build and qualification commands are in [Building](Building.md). Use Git history for provenance and ignored artifacts for detailed test output.

| Milestone | Status | Current state and remaining gates |
|---|---|---|
| M01 - Repository and implementation readiness | Complete | Repository and first-unit prerequisites are established. |
| M02 - Core ownership, numerics, and spatial data | Qualification pending | Selected implementation and managed regressions are complete. Target-profile NativeAOT and performance/capacity qualification remain. |
| M03 - Worlds and native semantic content | In progress | M03-A selected World, Scene, control, provider, and generator work is implemented. Production Standard/StrictLocal Save and selected Assets/Scene producers exist. End-to-end Load, candidate restore/replacement, and remaining producer/content-residency closure remain open. |
| M04 - Native host, input, and presentation resources | In progress | Selected Windows/Linux Platform, recipient routing, text/clipboard, D3D12/Vulkan graph and WSI paths, snapshot residency, and host recovery are implemented. Native color/depth attachments support Clear/Load/DontCare and Store/Discard for the admitted graph shape. Remaining: physical input/IME and device lifecycle, actual low-budget D3D12 paths, driver-generated loss/SurfaceLost, real submitted-work/presentation timeouts and uncertain-work retirement, broader shader/resource/allocator coverage, and final native/AOT acceptance. WSLg uses llvmpipe; test-owned keyboard and compositor events are synthetic. |
| M05 - Renderer, animation, and source adapters | Not started | Schedule capability-ready units under Chapter 35; renderer-wide AR08, built-in Sprite/material composition, animation, and source adapters remain. |
| M06 - Standalone custom physics | Not started | Dimensional cooking, solver, queries, CCD, and checkpoint qualification remain. |
| M07 - Custom audio and subsystem integration | Not started | Custom mixer/endpoints and integrated subsystem observations remain behind their applicable prerequisites. |
| M08 - Editor, extensibility, and release qualification | Not started | Editor/tooling, closed modules, full acceptance, and release measurements remain. |

## Next work

1. Continue dependency-ready M04 implementation and qualification. Keep physical-device, actual budget-pressure, and driver-generated failure observations open until the required hardware or event is available.
2. Continue M03-B Load, candidate restoration, and replacement through the actual owner contracts; do not add placeholder producers.
3. Start M05-M08 work only at the capability and observation boundaries in Chapter 35.
