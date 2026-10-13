# Specification acceptance and implementation qualification (informative)

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

This chapter separates specification acceptance from implemented-engine qualification. [The project roadmap](../project/Roadmap.md) owns current implementation status; [36](36-primary-sources-and-rationale.md) owns published sources and design rationale.

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

The specification defines 321 acceptance IDs, including the opt-in application-adapter case EN143. They are implementation obligations, not passes inferred from reference helpers. [28](28-diagnostics-performance-and-validation.md#4-release-gates) defines the mandatory release selection.

## 2. Specification acceptance

EN144 checks an exact repository revision: local file/heading links, unique acceptance IDs, declaration/owner consistency, and cross-chapter schema/default/ownership constraints. Reviewers derive these checks from the checked-in documents and cited public sources, not private attachments, session files, review caches or previous delivery archives. Revision and source hashes identify the checked inputs.

Specification integrity, reference arithmetic and declaration compilation do not qualify implemented engine behavior.

## 3. Implementation qualification

A facade-only declaration projection is not a complete supporting-type catalogue. Each implementation unit reviews its transitive declarations against the owning constructor/field tables or exact generation descriptors, then compiles consumers against the implemented surface. Absence from a projection is not evidence that a contract is missing, and invented empty stubs do not establish it. Complete headless/first-sprite/model/editor/standalone physics/audio callers qualify at their owning milestones under [35](35-implementation-roadmap.md); selected caller units are smaller checks, not substitutes.

Production formatting text/provider/destination/allocation behavior, Angle evaluation/trig, owner/channel/continuation behavior, generated roots and layout/codegen, NativeAOT/native/GPU/audio integration and measured workloads require implementation execution. Reference arithmetic and declaration-only compiler witnesses do not establish those results.

The RC compiler pin is not a shipping pin beyond its support period; [29](29-project-setup-build-and-deployment.md) requires supported pins and GA requalification.

## 4. Revision-bound evidence

Qualification uses the actual outputs of the ordinary build/test/native/workload workflows for the observed revision and target profile. Cite their command results, nonempty selected/executed counts, result artifacts and unsupported capabilities when reporting a gate; do not create a qualification-record schema, receipt service, archive or ledger. The project roadmap owns current status. Revalidate affected checks when inputs change; results for unchanged relevant inputs remain usable.
