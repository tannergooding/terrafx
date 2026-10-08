# Implementation roadmap

The [design roadmap](../design/35-implementation-roadmap.md) defines milestone
scope, dependencies, and acceptance gates. This page tracks the current
implementation state and remaining qualification.

## Milestone status

| Milestone | Status | Progress / remaining work |
|---|---|---|
| M01 - Repository and implementation readiness | Complete | Repository, build, test foundation, and first-unit prerequisites are established. This is readiness, not engine qualification. |
| M02 - Core ownership, numerics, and spatial data | Ready to start | Angle/AngleUnit contracts are reviewed; no Core behavior is implemented yet. Foundation ownership, numerics, spatial data and codec qualification remain. |
| M03 - Worlds and native semantic content | Not started | Planned scope and prerequisites are defined in the design roadmap; no M03 implementation or qualification has started. |
| M04 - Native host, input, and presentation resources | Not started | Requires the applicable M02/M03 capabilities and observations. |
| M05 - Renderer, animation, and source adapters | Not started | Schedule by the capability and observation gates in design chapter 35. |
| M06 - Standalone custom physics | Not started | Requires M02; dimensional cooking, solver, queries, CCD, and checkpoint qualification remain. |
| M07 - Custom audio and subsystem integration | Not started | Requires the applicable M02-M06 units. |
| M08 - Editor, extensibility, and release qualification | Not started | Requires M03-M07. |

`Ready to start` means prerequisites are accepted, not that a whole milestone is
implemented. `Qualification pending` means implementation is present but
required observations remain. `Complete` requires the entire design milestone
exit boundary; a managed build or reference helper is not engine/native/AOT
qualification.

## Maintaining status

Update this page when a coherent unit starts, completes, or encounters a
material blocker. Reference checked-in contracts or durable published records,
not checkout-local artifacts. Use Git history rather than repeating
implementation history here. Do not change design contracts to describe
implementation progress.
