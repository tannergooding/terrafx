# M05 skinned-normal diagnostic public carrier resolution

## 1. Disposition and scope

**Tentative proposal for owner review, not a contract amendment or acceptance evidence. The public-delivery blocker remains open.**

The existing `Diagnostic` shape can carry count telemetry without another public property or diagnostic identifier. However, the current contracts do not close its Rendering producer, count encoding, and frame/draw delivery semantics. The existing submission receipt has no diagnostic payload. Consequently, neither is an already closed, authorized end-to-end route for this implementation.

**Recommend resolving the existing Diagnostics route first, without adding public API.** Chapter 20 does not itself require the stronger, lossless typed receipt introduced by the tentative [delivery proposal](m05-skinned-normal-diagnostic-delivery.md). If owners require that stronger result, they must explicitly review its declaration and behavior amendments rather than infer them from “GPU diagnostic count.”

This assessment changes only this proposal. It does not change source, tests, maintained chapters, other proposals, numerical behavior, schemas, budgets, or milestone status. It introduces no acceptance or diagnostic IDs. Tentative approval permits planning within this resolution path, not publication of an unreviewed API.

## 2. Existing contracts and current source

| Input | Exact finding |
|---|---|
| Chapter 20, section 3, lines 49–53 | `DegenerateSkinnedNormal` is already named. The prescribed compute pass counts near-zero blends, reads back asynchronously after its fence, and tags diagnostics with frame/draw IDs. Raster fallback has no storage writes. This does not declare a public result type, zero-count row, complete per-submission array, or diagnostic completion guarantee on `SubmitAsync`. |
| Chapter 30, section 7, lines 5229–5241 and 5298 | `Renderer.SubmitAsync` returns `Task<RenderSubmissionReceipt>`. The receipt declares only `long SubmissionSequence`, `long CompletedFence`, `long? PresentationSequence`, and `uint DeviceEpoch`, `SurfaceEpoch`, `SwapchainEpoch`, with an internal six-argument constructor. These are sequences/fences/epochs, not spare count fields. Its existing GPU/pin completion and separate presentation observation must remain valid. |
| Chapter 30, section 10, lines 7696–7763 | Existing declarations include `DiagnosticContextEntry(string key, string value)`, `Diagnostic.Code`, `Diagnostic.Message`, `Diagnostic.CommandIdentity`, `Diagnostic.Context : ImmutableArray<DiagnosticContextEntry>`, `DiagnosticsService.GetSnapshot(DiagnosticQuery)`, and `DiagnosticSnapshot.Records : ImmutableArray<Diagnostic>`. These provide a generic string/context carrier and copied observation, not a public emitter or typed numeric result. No skinned-normal context grammar is declared. |
| Chapter 28, section 1; Chapter 04, line 60 | Ordinary telemetry may coalesce/drop in a bounded service. `OccurrenceCount` is service-owned warning-occurrence metadata; `DroppedCount` and `CoalescedCount` describe telemetry retention. None counts degenerate vertices. The retained-byte limits, independent fault/result retention, and copied-snapshot ownership are unchanged. |
| Chapter 29, “Diagnostics service ownership and issuer,” lines 37–41 | Core owns the service; Hosting owns the host instance. The existing `WorldDiagnosticsCapability` permits only the World subscription-capacity diagnostic and is not transferable general Rendering emission authority. No reverse Hosting dependency or general callback grant follows. |
| `DiagnosticsService.cs:250–363`; `WorldDiagnosticsCapability.cs:71–90`; `TerraFX.Core.csproj:10–18` | An internal `Write(Diagnostic, Guid?, uint?)` physically exists, and Runtime has Core friendship. Reachability is not semantic authorization. The current World capability is bound to an exact World/owner; its runtime checks do not expand Chapter 29's permitted use. |
| `Renderer.CpuExtractionSupport.cs:16–28,53–56`; `EngineHost.cs:80,764–765,845`; `EngineHost.GraphicsRecovery.cs:278` | Renderer currently receives only the Graphics device and residency service. Host startup/recovery does not supply a diagnostic publisher. Core `Write` references include Hosting and the World capability, not Rendering. |
| `RenderSkinnedNormalDiagnostic.cs:8–82`; `RenderSubmissionReceipt.cs:11–46,58–59`; `Renderer.Submit.cs:831–856`; `RenderMeshC0NormalDiagnosticGraphPass.cs:202–275` | The workspace decodes GPU rows into the new public value and attaches the array after readback. The candidate also adds `IEquatable<T>`, equality operators, `Equals`, and `GetHashCode`. These are source observations, not Chapter 30 declarations or executed evidence. |
| [Delivery proposal](m05-skinned-normal-diagnostic-delivery.md), sections 1 and 3; [Frame-bindings addendum](m05-skinned-normal-diagnostic-delivery-frame-bindings-addendum.md), section 1 | The public additions were explicitly proposed, not accepted. The binding correction retains that proposed surface; it does not promote it into the public baseline. |
| [Budget-evidence rectification](m05-skinned-normal-diagnostic-budget-evidence-rectification.md), section 1 | Its tentative unblocker explicitly grants no public API. Budget work cannot supply the missing declaration authority. |

Chapter 30 places Diagnostics in `TerraFX.Diagnostics`; the frozen Core source declares the inspected types in `TerraFX`. The table identifies the normative declarations and actual files separately; it is not a compiled namespace/API-conformance claim or authorization for a namespace refactor.

### Distinguishing example

Consider two completed readbacks for the same World/tick, reporting counts 3 and 9 with their original frame/draw tags. Simply using the host's current null-session/null-owner `Write` call for both as warnings would match the existing coalescing key. The retained record keeps the first payload and gains service-owned `OccurrenceCount=2` (`DiagnosticsService.cs:112–135,144–160,309–324`). That is neither a vertex count of 12 nor two complete results. Later eviction can remove the record.

This illustrates why polling cannot substitute for the delivery proposal's complete typed array. It does **not** establish that Chapter 20 requires such an array: best-effort telemetry may be sufficient if its owner explicitly closes that interpretation. `Message` can describe a count, and `Context` can encode one, but inventing a key/format and treating it as an existing reviewed contract would not close the gap. Tick alone also cannot distinguish resubmissions of one snapshot.

## 3. Minimal owner-resolution path

1. **Animation/Rendering and Diagnostics owners:** review post-fence structured telemetry through the existing `Diagnostic`/`DiagnosticsService` as the recommended interpretation of Chapter 20. Retain the existing `DegenerateSkinnedNormal` name. Do not infer mandatory zero rows, lossless delivery, or another public result API.
2. **Close the behavioral amendment before implementation:** define the counted membership, original frame/draw correlation, count representation in existing fields, severity, zero behavior, coalescing identity/payload policy, visible loss, and publication point after real readback. The delivery proposal's membership rules are review input, not already normative. This proposal deliberately assigns no context keys, severity, synthetic identities, or new fields. Do not overload `NativeResult`, sequence/fence/epoch fields, `OccurrenceCount`, or World/session identities to avoid that review.
3. **Core/Hosting/Rendering owners:** authorize a closed, non-owning publication path from the host-owned Core service to Renderer, including startup and device recovery. Rendering decodes/count-tags its own copied result; Core owns the clock/ring; Graphics supplies lower-owned bytes/completion, never Rendering types. Do not reuse the World capability, pass Hosting objects/callbacks into lower owners, add a service locator, or add friendship. Define admission and pending-publication retirement before host-service closure; a captured World GUID does not authorize accessing a disposed/replaced World. Preserve Chapter 04's attribution/retention ceilings.
4. **Public API owner:** confirm that the selected telemetry route reuses Chapter 30's existing declarations. On that route, both `RenderSkinnedNormalDiagnostic` and `RenderSubmissionReceipt.SkinnedNormalDiagnostics` are unnecessary. If owners instead require reliable submission-correlated results, review an explicit Chapter 30 receipt amendment together with Chapter 20's membership/completion behavior, full supporting-type/equality/default closure, cancellation/failure, and data-only lifetime. The existing delivery proposal is the starting candidate, not permission to bypass this review.
5. **Implementation handoff:** freeze the selected owner amendments and affected source anew; obtain a disposition on those exact bytes before committing the public-delivery implementation. Then validate its actual consumers separately. No proposal/hash comparison establishes managed behavior, native/AOT output, budget fit, or milestone acceptance.

The generic carrier is therefore reusable, but its missing producer and semantics are a narrowly scoped owner-contract decision—not evidence that a new public receipt is intrinsically necessary.

## 4. What remains gated

- Preserve the untracked `RenderSkinnedNormalDiagnostic.cs` and pending `RenderSubmissionReceipt.cs` additions byte-for-byte now. Do not commit or publish them as supported public API until the selected owner contract is reviewed.
- Hold pending API-dependent result construction, receipt integration, and public-consumer assertions from contract acceptance. Some dependent implementation is already tracked and clean relative to HEAD; that does not make its missing public declarations accepted. This task neither rewrites nor reverts it.
- Do not replace required count production with empty-success receipts, CPU-generated counts, fabricated tags, public emitter methods, or reserved telemetry-counter abuse.
- The diagnostic budget gate remains NO-GO independently. Unrelated dependency-ready M03-B/M05 work is not blocked by this carrier decision, but it gains no authority from this proposal to cross the missing public contract.

## 5. Frozen assessment inputs

Freeze: **2026-11-17T21:18:41.2383110Z**, HEAD **`cf4d53518de0c31663a200f9781062654f30d614`**. The following SHA-256 values identify the complete **bounded proposal-assessment** input set, not a whole-implementation review. `C` = tracked/clean, `M` = tracked/modified, `U` = untracked. Dirty rows identify workspace bytes, not just HEAD.

Read-only byte copies and `frozen-inputs.json` are under `artifacts\log\m05-skinned-normal-public-contract-resolution-20261117\inputs` and its parent directory. The manifest SHA-256 is `461D58535B7CA5599919147BB06EC5AE1434E8153990E541B8DDFB34FC8BF88E`.

`dirty-baseline.json` in that directory records all **81** pre-existing dirty paths/statuses/hashes for preservation checking; its SHA-256 is `1A7FC5714B42AFBEBECF7CF18E202715BAB6DD93179EF1DCE43D271520FE32AC`. That safety snapshot is not acceptance of unrelated dirty work.

| Input | State | SHA-256 |
|---|---|---|
| `.github\copilot-instructions.md` | C | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| `docs\project\Roadmap.md` | C | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\17-graphics-device-and-resources.md` | C | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\20-animation-and-deformation.md` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\design\28-diagnostics-performance-and-validation.md` | C | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` |
| `docs\design\29-project-setup-build-and-deployment.md` | C | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\31-public-api-consumer-guide.md` | C | `EB725CB14D32754611B15B87870B85758600C42100510B25B13B730AE61A209B` |
| `docs\design\32-end-to-end-integration-scenarios.md` | C | `6AF2211CB79FECA449D0F701752F86B8D093162AD75673770B0C0DE0F49BA4FC` |
| `docs\design\34-engine-acceptance-cases.md` | C | `532A68F1717DC05901DFC4537204E65B31B28EE46326F64F5A1CD7834332AA41` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery.md` | C | `1D9ED2736D1C2520712D1E85A75AC5F0CF81D58C1EA48158F284B24B07C0DA3D` |
| `docs\proposals\m05-skinned-normal-diagnostic-delivery-frame-bindings-addendum.md` | C | `26F0A14AAB3453AA106ACBD5543BC434E4787CF2AFA51E80A542BCE9BF6C33B4` |
| `docs\proposals\m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | C | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `sources\TerraFX.Core\Diagnostic.cs` | C | `B827997B35EE8ACCCCFA069905865C1E155508401FE7256EACBD0B20E2A6B840` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | C | `C2A1E74E0AE9929638C9255CFAA8DD7235CE37509483660B740569B775BEF530` |
| `sources\TerraFX.Core\DiagnosticQuery.cs` | C | `E8574D8DA53A59F71557EAB8FF101F8EE56A9CB126AC26614CE4D7F6795F6DE2` |
| `sources\TerraFX.Core\DiagnosticSeverity.cs` | C | `E90303D70CECFD9B83EC4D9D5716697230442896D997059CF29A24EB49AE2327` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | C | `916F5FB2AD709C4F0EF12FC83FF8E997619F1C1B9F6B52234655E91D45EFC242` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | C | `004575041F3702786FD85DB0F7A5479CEB5C8C2717374D7B3FB325183E6B7F71` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | C | `77D3CBC729325420643EAF196C7B30AB6EBD178A883CFB417CC64E7E41644A5E` |
| `sources\TerraFX.Core\TerraFX.Core.csproj` | C | `BDA8230D77298EE852B28ED8446C66E7ADD9227F6AD9285087E3408F351E0088` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | C | `A3FB2415E31CB7B717F2AD382E1FACF874B6AC392A469B6E71BA198DF6A11743` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | C | `CC03DB8E1296B8A1C4CD1FAAC37F33BF4455D424C3EE66C6EC5DFE481B89B8D6` |
| `sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs` | M | `EAF1F94AFF0886B3FA30AF97BD7F83180A38AB744D4241D0CDD275A055EADB55` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | C | `8BF0D0662C9D3DE0A1670ED05C89FAB1E6A749D744BF796A9D5E0236BF4DDB79` |
| `sources\TerraFX.Runtime\Rendering\RenderSubmissionReceipt.cs` | M | `77120676F289AD7D8891875A783BAFA05E64B7F0317BC09BECC2545AC42E5846` |
| `sources\TerraFX.Runtime\Rendering\RenderSkinnedNormalDiagnostic.cs` | U | `E07287C05EA0E4A12F5ADF2FABCDC48B191C2D753B20E2636447CD83F1E2A41E` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | C | `554750E946DCADFD826C2B2348550B5ECE7C78B5BBC93084CB4F1BFCFD3FBB9B` |

No content schema or protected literal fixture is an input to this carrier decision; none is assigned, changed, or implicitly accepted. Shader/recording/budget implementation and tests are outside this review scope. Chapters 32/34/35 retain their existing observation gates.

For later review, include **this proposal plus the 30 frozen inputs**. Its own hash and frozen copy are recorded separately in `review-manifest.json` at finalization to avoid a self-referential digest. Recheck every hash before applying a disposition; changed owner amendments require a new input freeze. The ignored manifests/copies are per-run records, not another qualification pipeline. No build, test, native/AOT, or acceptance run is performed by this proposal-only assessment.
