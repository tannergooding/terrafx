# Specification acceptance and implementation qualification (informative)

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Contract and qualification map

Subsystem chapters define behavior, [30](30-public-api-review-surface.md) defines public signatures, and acceptance cases define required observations. [35](35-implementation-roadmap.md) assigns qualification to coherent implementation milestones.

| Contract | Qualification |
|---|---|
| [02 API and ownership](02-dotnet-api-memory-and-ownership.md), [30 public API](30-public-api-review-surface.md), [31 consumers](31-public-api-consumer-guide.md) | M01 maps repository readiness and contract gaps. Each implementation unit reviews its transitive declaration closure and compiles public-only consumers; the complete catalogue/programs and AR01-AR12 close at M08. Shared usage/admission/execution, cancellation, continuation and disposal rules apply at each owning milestone. |
| [05 buffers and pools](05-owned-buffers-and-internal-collections.md), [06 geometry](06-owned-geometry-and-numerics-interchange.md) | M02 implements and qualifies ownership, concrete scoped callables, bounded pools, exact storage/interchange, narrow operator interfaces, formatting and Angle evaluation. |
| [12 entities](12-entities-components-and-queries.md), [16 scenes](16-scenes-transforms-and-cameras.md) | M03 qualifies tag registration/atomic membership edits, scene budgets and preparation/activation lifetimes. |
| [14 input and text](14-custom-input-actions-and-text.md), [25 UI and editor](25-ui-text-and-editor-tools.md) | M04/M08 qualify Unicode/selection/caret/revision validation, owner-thread sessions, layout and independently retained UI snapshots. |
| [23 physics queries](23-custom-physics-shapes-queries-and-ccd.md), [24 audio](24-custom-audio-mixing-and-spatialization.md) | M06/M07 qualify scoped hit delivery, dimensional physics, command/voice completion, endpoint behavior and cached snapshot publication. |
| [26 generated modules](26-csharp-systems-plugins-and-hot-reload.md), [29 setup](29-project-setup-build-and-deployment.md), [32 integration](32-end-to-end-integration-scenarios.md) | Generated descriptors/roots, supported toolchain profiles, native retirement, integrated consumers and measured release workloads close at their roadmap milestones. |

The specification contains 320 acceptance IDs. They are implementation obligations, not passes inferred from reference helpers.

## 2. Implementation qualification

The chapter-30 facade-only projection excludes 112 supporting declarations, many defined by owner constructor/field tables outside those blocks. This is a projection boundary, not evidence of 112 missing design contracts. Each implementation unit reviews its exact transitive declaration catalogue before implementation and compiles consumers against the implemented surface. Complete headless/first-sprite/model/editor/standalone physics/audio callers qualify at their owning milestones under [35](35-implementation-roadmap.md), not before Core exists. Supporting types come from their owning declarations or exact generation descriptors, not invented empty stubs. The four selected caller units are smaller checks, not substitutes.

Production formatting text/provider/destination/allocation behavior, Angle evaluation/trig, owner/channel/continuation behavior, generated roots and layout/codegen, NativeAOT/native/GPU/audio integration and measured workloads require implementation execution. Existing reference and compiler witnesses do not establish those results.

The RC compiler pin is not a shipping pin beyond its support period; [29](29-project-setup-build-and-deployment.md) requires supported pins and GA requalification.

