# M05 diagnostic command-payload owner transfer

## 1. Decision and implementation boundary

**Tentative implementation authority, pending review. A bounded, non-refunding command-owner handoff batch can proceed; complete payload-alias qualification and the full 32/16-MiB diagnostic gate remain NO-GO.**

[Physical-storage and alias qualification](m05-skinned-normal-diagnostic-physical-storage-and-alias-qualification-addendum.md) selects the correct command-backing owner, `RecordedPassCommandList`, but is not a complete implementation contract. It does not settle whether a copied pass is an independent holder, how the recipe binds to that owner, or who retains the independently shared binding graph. Its final proposal-only stop cannot be read as approval of an arbitrary owner wrapper or span substitution.

This addendum replaces that stop **only for the structural batch in section 5**. Reuse the existing recorded-list object as the command lifetime shell, carry it through pass/submission handoff, and preserve the exact completed-backing witness. Do not introduce a byte refund, declare a complete retained graph released, or enable complete diagnostic admission in this batch. Implementing this component does not require pretending that its as-yet-unqualified controls have a known footprint.

The buffer-import index implemented in `b447c4c2` remains a separate component; this decision neither reopens nor extends its allowance. Preserve the joint **33,554,432-byte** preparation/recording and independent **16,777,216-byte** completion ceilings, complete membership, four diagnostic bindings, command representation, command/barrier schedule, Core readback, and native fence/quarantine ownership. No new public API, acceptance/diagnostic ID, cardinality cap, object charge, or qualification result is selected.

## 2. Source-established owner and alias coverage

These are static observations of the frozen workspace, not executed lifetime or allocation measurements.

| Actual source owner or holder | What it owns or retains now |
|---|---|
| `CommandRecorderState.Complete` | Creates the immutable command backing from its mutable list, validates that exact backing, and supplies it to `RecordedPassCommandList`. Mutable and frozen command storage coexist during completion. This handoff is not a third command copy. |
| `RecordedPassCommandList` | Retains `Commands`, the frozen use-lease array, and the optional recipe. Safe `Dispose` clears those fields and releases its uses/recipe; uncertain retention prevents ordinary disposal. Neither action revokes a previously copied array or element reference. |
| `RenderGraphPlan.CreateGraphicsSubmission` and `GraphicsGraphSubmissionPass` | Copy the immutable command-array value and recipe reference into a readonly pass value. Submission construction copies outer pass/list arrays, not their inner command payloads. Independent pass/outer-array copies remain roots. A C# struct copy cannot automatically acquire a new counted interest. |
| `RecordedGraphicsCommand` | Directly retains `BindingSet`, `GpuPipeline`, buffer/texture copy versions and storage-barrier versions. Its vertex/index binding values also contain `GraphBufferVersion` references. Graph versions retain resource descriptions; a pipeline retains its own owner/layout graph. The real diagnostic recorder emits pipeline, binding, buffer-barrier and dispatch commands, not every possible command kind. |
| Public `BindingSet` and binding objects | The constructor copies and sorts reference arrays, not the referenced binding objects. `Buffers`, `Textures` and `Samplers` return independently retainable immutable-array values. A retained element can further retain a graph version or sampler. There is no disposal, interest registration or revocation protocol on those public arrays. |
| `RenderMeshC0NormalDiagnosticGraphPass` | Its private `_bindings` table remains in graph/compiled-plan state through `RenderSpriteGraphState`; recording does not consume it. For D draws using M geometries, the current producer shares M vertex bindings, one palette binding and one result binding, and creates D work bindings and D sets: **M + D + 2 binding objects**, not 4D distinct objects. The recipe creates another outer reference array over the same sets. These are separate backings with shared descendants. |
| `SkinnedNormalDiagnosticRecordingRecipe` | `_verifiedCommands` retains the exact completed immutable-array backing. `ValidateNativeSubmission` checks array identity, pass/epoch/access witnesses and the complete schedule. `_bindingSets`, resource entries, accesses and scratch retain additional roots. `Release` clears only the recipe's roots, not the plan's binding table or previously exported child arrays. |
| Submission request, backends and quarantine | `SubmitGraphRequest` retains the submission through actual work; safe cleanup detaches recording and uncertainty roots the same submission. Both native backends traverse copied command values and invoke an optional `Action<GraphicsGraphSubmission>` observer. Backend descriptor sets retain their own backend-resource/pipeline arrays; those are not the original `BindingSet.Buffers` backing and have separate native/managed obligations. |

The current `SubmissionRecordingRootsDetachOnlyAfterSafeRelease` fixture demonstrates the raw pass/command-array escape with an **empty** `BindingSet`. It does not exercise a nonempty child binding array. The existing diagnostic recorder fixture has real nonempty graph-resource bindings, but does not yet test independently retained child arrays or an equal-content replacement command backing.

A concrete distinguishing case is two draws sharing geometry. The real schedule has nine commands and three barriers, with different work-packet ranges. Retaining `Commands[1].Bindings.Buffers` before cleanup can retain that first set's binding backing and elements after every original command-list field is cleared. Replacing the pass's raw array with a reference to its recorded owner removes one escape route; it does not retroactively revoke that child array or transfer the plan's persistent binding ownership.

## 3. Selected minimum mechanism

### One recorded owner, not one lease per pass copy

`RecordedPassCommandList` remains the owner of the existing frozen command backing. `GraphicsGraphSubmissionPass` carries that same recorded owner instead of independently carrying a raw command array and a separately pairable recipe. A pass with no recorded list carries no owner; a successfully recorded empty list need not be discarded. Preserve pass ID/kind matching and the existing synthetic terminal-pass restrictions.

Runtime's recorded-list result, its pass builder, submission passes and `RecordedPassCommandLists` may all reference the **same lifetime shell**. Copying one of those references or a containing pass does not create another backing, another release right, or an automatically counted consumer. Keep the existing operation-owned cleanup authority; passive aliases observe the same terminal state.

An independently retained asynchronous consumer is different. Before its publication, its recipient must acquire an explicit retained-owner interest with an actual release boundary and funded control/carrier. A copied struct, shared `IDisposable` reference or `GC.KeepAlive` is not that acquisition. The first batch adds no general retained-consumer facility and must not claim to qualify an observer or arbitrary new consumer.

### Checked synchronous reads and terminal detachment

The command array stays private to the recorded owner. Runtime handoff no longer obtains it. Validation and both backends consume it through an owner-bound synchronous read protocol while retaining that owner for the entire call.

The protocol must synchronize read entry with safe-close/uncertain transitions. Safe close stops new reads and detaches command/recipe roots only after existing reads have returned; repeated close ends each existing release right at most once. Do not block the graphics owner waiting for a consumer that needs that owner. Current operation tasks must still become terminal only after their actual consuming calls end. Additional state belongs to this existing owner and remains part of its unqualified control group, not an unexplained fixed overhead.

A scoped span can be the implementation's view, **not its alias proof**. C# permits copying a command and retaining its `BindingSet`, pipeline or version fields, and permits retaining a binding-array value. Audit the actual synchronous readers. A reader that retains such a value beyond the call needs the corresponding retained ownership established before leaving the call; otherwise that path remains unavailable for complete byte-qualified admission. No forced GC, finalizer, weak-table registry or per-command receipt is selected.

### Bind the recipe once, before publication

Keep the current order: freeze one immutable command backing and verify its complete sequence before publishing a completed recording. Bind the recipe once to the recorded owner that received **that very backing**, after its constructor/use-cohort checks succeed. A failed transfer leaves the prior holder responsible for cleanup.

Native validation must establish both the same bound recorded owner and the same originally verified backing, in addition to the existing pass, epoch, binding-reference, packet-range, access and schedule checks. A different owner over the same array is not a transfer; an equal-content array is not the verified backing. Do not weaken the witness to count equality, sequence equality, matching pipeline handles or a span with the same contents.

The recipe's backing witness becomes part of the recorded owner's lifetime rather than a separately exportable array root. A recipe retained after safe release is not authority to validate or replay released recording. Preserve failure unwind and clear its owner/witness roots at the actual safe terminal boundary.

### Keep the shared binding baseline separate

Do **not** make the recorded list the alleged exclusive owner of the plan's binding graph. The real Runtime producer retains `_bindings`, and the recipe/commands borrow those same sets. Public immutable binding arrays that have already escaped remain valid managed values.

For later accounting, distinguish:

- the per-recording command backing, frozen use-reference array, recipe outer binding-reference array and private verification scratch;
- the still-retained graph/plan binding baseline, including its set backings, shared binding objects, geometry/version/description references and construction overlap;
- pipeline/resource/native use interests, backend descriptor groups and any independently retained child/observer interest.

Count a shared allocation once while retaining all obligations that keep it live. Command-array detachment cannot refund the shared binding baseline. A command value copied off the array need not retain the **array**, but can still retain its **referents**; those are different release claims. Do not attach a credit only to `BindingSet` and assume an independently retained `Buffers` array keeps that credit alive.

The graph/plan binding baseline and independent child interests are not qualified by this proposal. Their charge cannot end at recording completion, frame-slot return, task completion or source-scope exit. A later refundable payload implementation must close their real owner/recipient contract or refuse that diagnostic-bearing path. This batch deliberately changes no refund or complete-admission decision.

### Uncertainty transfers the same owner

Retain the same recorded owner, exact backing, recipe and existing native-use interests through the established submission/quarantine path. Coordinate the owner transition with submission's existing safe-versus-uncertain decision so uncertainty cannot follow successful payload detachment. Ordinary disposal cannot release quarantined recording.

This selects no new quarantine bag, copied command carrier, CPU-credit use of a native lease, or safe release based on a missing fence receipt. Existing quarantine publication controls/copies remain unqualified; keeping the owner alive does not prepay them.

## 4. What the batch does and does not close

| Witness or holder | Structural batch disposition | Gate still open |
|---|---|---|
| Recorded-list result, copied outer pass array, copied pass, retained submission | Carry the same recorded owner; no unaccompanied inner command-array field in a pass. Passive copies share terminal state. | Actual holder/control sizes and any independent retained-consumer interests. |
| Active validation/backend read | Owner/read protocol prevents detachment during actual synchronous consumption; later reads after safe release fail. | An opaque reader or callback is not proved nonretaining merely because it returned. |
| Copied command, `BindingSet`, child array or individual binding/version/pipeline reference | Explicitly not revoked by owner close. Preserve public binding-array semantics. | Retained child/source owner and full transitive accounting; no whole-payload refund. |
| Diagnostic textures/samplers | Recipe continues to require initialized empty arrays, alongside exactly four nonempty buffer bindings. | This does not qualify nonempty texture/sampler arrays from ordinary passes elsewhere in a diagnostic-bearing graph. |
| Recipe/native handoff | Exactly one recorded owner and the original verified backing; unchanged binding/access/schedule witnesses. | Footprints of recipe controls, private arrays and all simultaneous construction/transfer storage. |
| Backend descriptors and resource owners | Preserve their existing native/resource ownership; do not label them released with CPU commands. | Backend retained capacity and actual native retirement evidence. |
| Uncertain recording | Same owner and backing remain retained against ordinary close. | Preadmitted quarantine publication and complete lifetime/byte transfer. |

Full alias qualification still requires every admitted reader/export to have a covered owner lifetime. Missing coverage refuses the whole diagnostic-bearing graph when complete admission is implemented; it does not authorize dropping the diagnostic or changing unrelated ordinary graph semantics.

## 5. Bounded implementation order and focused evidence

The following is **one coherent integration batch**, not permission to stop with a half-converted carrier:

1. Make `RecordedPassCommandList`'s existing backing and safe/uncertain cleanup obey the synchronized owner-read boundary. Keep command representation and actual native-use release rules unchanged.
2. Convert `GraphicsGraphSubmissionPass` and Runtime's `RenderGraphPlan.Submission` handoff to carry that recorded owner. Update submission validation, both backend traversal paths/helpers and affected constructors/tests together. Remove the production unaccompanied command-array handoff rather than retaining it as a fallback.
3. Bind `SkinnedNormalDiagnosticRecordingRecipe` once to the completed owner/backing before task publication. Preserve `CommandRecorderState.Complete`'s single freeze and `GraphicsDevice.RecordPass`'s callback/recorder-root cleanup before completion publication.
4. Add the focused owner/alias regressions below. Keep all existing component checks and full-budget NO-GO status. No positive allocation/refund claim or public contract change belongs in this batch.

Use the existing Graphics harness and `GraphResourceRegistry` resource producers; no fabricated `RenderSnapshot`, Scene substitute, test-only production hook, or second recording implementation is needed.

| Existing owning fixture | Required focused evidence |
|---|---|
| `GraphicsBufferTransferTests.SubmissionRecordingRootsDetachOnlyAfterSafeRelease` | Extend to real nonempty bindings. Retain the recorded owner, outer pass array, copied pass and submission independently. Prove the same owner/backing before cleanup, shared terminal behavior after safe close, idempotent cleanup, and unchanged retention under uncertainty. Do not count cleared fields as proof that copied elements disappeared. |
| The same owner fixture | Exercise close while a synchronous read is active, read failure/exception unwind, later read refusal and safe-versus-uncertain ordering. Verify cleanup does not end a live read or release uses twice. Deliberately retain a copied command, set, nonempty buffer-array value and one binding: they remain ordinary managed references, not evidence of revocation or a complete refund. |
| `GraphicsPipelineRequestTests.SkinnedNormalDiagnosticRecorderBoundsHazardsAndValidatesNativeWitness` | Preserve all six schedules below, four exact buffer bindings and packet ranges. Accept repeated validation of the original owner/backing; reject an equal-content **physically distinct** backing, a different recorded owner even over the same backing, wrong epoch/pass, rebinding a completed recipe to another recording, and any validation after release. Counterfeit copies are test inputs, not a third production freeze. |
| Existing pipeline recording/lifetime tests | Keep pipeline use acquisition/release counts, live-owner refusal, recording failure unwind, callback-root cleanup and prequeue refusal behavior. Retained CPU references do not become extra native `UseLease`s. |
| `RenderMeshC0NormalDiagnosticBudgetTests` and Runtime build | Preserve command stride, mutable/frozen capacities and existing known-component arithmetic. Compilation must cover Runtime and both backends against the new private carrier. These checks do not qualify the binding baseline or new owner control fields. |

Independent literal schedule expectations for the existing recorder fixture:

| Geometry schedule | Draws / distinct geometry | Commands | Barriers | Pending-write high water |
|---|---:|---:|---:|---:|
| A | 1 / 1 | 3 | 0 | 4 |
| AA | 2 / 1 | 9 | 3 | 5 |
| AB | 2 / 2 | 8 | 2 | 6 |
| AAA | 3 / 1 | 15 | 6 | 6 |
| ABC | 3 / 3 | 13 | 4 | 8 |
| ABA | 3 / 2 | 14 | 5 | 7 |

The Graphics fixture currently constructs four new buffer-binding objects per draw. It is suitable for the recording-owner/witness tests; it is **not** the expected-value source for the Runtime producer's shared M + D + 2 object membership.

Use the ordinary commands in [Building](../project/Building.md), Debug before Release. The focused selections for this integrated change are:

```powershell
dotnet test .\tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~GraphicsPipelineRequestTests|FullyQualifiedName~GraphicsBufferTransferTests.Graph|FullyQualifiedName~GraphicsBufferTransferTests.SubmissionRecordingRootsDetachOnlyAfterSafeRelease"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~RenderMeshC0NormalDiagnosticBudgetTests"
```

Run the same selections with `--configuration Release` after Debug. Include any new owner cases in the owning selected fixture/method and verify their actual discovery/execution; expand the filter if they are separate methods. Confirm nonzero current-run TRX executions under `artifacts\tst\<Configuration>`, and distinguish the six conformance schedules from the number of framework tests. This review changes documentation only; none of these implementation checks has been run or claimed here.

## 6. Remaining gates and disposition

**This is no longer an implementation blocker for the specific non-refunding owner-backed carrier/read/witness batch above.** It remains an implementation/qualification blocker for **any complete payload refund or full diagnostic admission claim**.

Before that later claim, close:

- the real graph/plan binding baseline and each independently retained command-element/child-array/observer interest, including ordinary participating passes;
- exact owner/read/interest controls, actual physical backings, constructor/freeze/transfer overlap and complete joint phase maxima, with a nonzero retained baseline;
- preadmitted uncertain-work publication and actual safe/uncertain terminal ownership;
- the still-unqualified preflight/backend, request/TCS/return and independent completion/result terms from the existing owning proposals.

Only then can exact-fit/one-byte-short **complete** grants and all lifetime transfers qualify the full row. Do not invent such grants for this structural batch. Chapter 35's real native, NativeAOT, public-consumer and milestone observations remain distinct gates. No synthetic loss/timeout observation or repeated unchanged blocked native probe is selected.

## 7. Frozen review inputs

HEAD is `b447c4c24f1e15a6eed9850c529a61d7a6b2e030`. The working tree includes pre-existing dirty/untracked M03-B/M05 work. In particular, the reviewed `GraphicsGraphSubmissionPass` already has a dirty optional recipe field absent from HEAD; HEAD alone is not the review input. Whole-file SHA-256 values below freeze the material source, owner-contract, fixture and build-command inputs. Recheck before applying this disposition. No source, schema, fixture or existing documentation file is changed by this review.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `tests\Directory.Build.props` | `281AAD4237121FEAB1903C2384F1A5F0AED7E218A704C918FE953E9FC8E75764` |
| `.github\workflows\ci.yml` | `03EFEA672CD52E9EF053E9CE0285E7CA02C6CE6FB0D21268ADE4255ED5A39D58` |
| `docs\project\Building.md` | `F043A352870EFB6C7D4A61998225E2508A8F4370A57E784483A41819D452075F` |
| `docs\project\Roadmap.md` | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\17-graphics-device-and-resources.md` | `8157E7DA5472B9AABD891A05E84F2B997487A5C5E3FF67CB589681747D628718` |
| `docs\design\19-render-graph-synchronization-and-presentation.md` | `AB673DEDBD85B7A161C5D7DC871BF5B486911DC27EC4279AC43827B005241B8B` |
| `docs\design\30-public-api-review-surface.md` | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\proposals\m05-skinned-normal-diagnostic-physical-storage-and-alias-qualification-addendum.md` | `FB9182D9FEBD235EC602114EA3944791B92D77D9BC0775584DCEFEDE26FB8D23` |
| `docs\proposals\m05-skinned-normal-diagnostic-preflight-alias-workspace-addendum.md` | `AE89901571E050F4B9F29997F7B3559B15021396F4C0BFC360A7FB05CE83BB3B` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-workspace-admission-addendum.md` | `BC52399DE52D48A5425BD36D7ECC6D132B7BA3C9D792040F2FA9ABF8BCB24CE8` |
| `docs\proposals\m05-skinned-normal-diagnostic-recording-budget-closure.md` | `5787039DC3BA43A862529CD9F01D82623DDFEC1B273194CC73FC50F079E0C753` |
| `docs\proposals\m05-skinned-normal-diagnostic-buffer-import-index-owner-addendum.md` | `FC18D0C3722007F5F076CA81F644F9CC50403F7B56C2F7C70F2852D7DB740DBF` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `36C99EEC7846E37DB528F69421B79D957BBDBAC5B11A4A83E6D62F837AC25571` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmissionPass.cs` | `4BE4B04419FAD2F72C791358A8D7A784367E748D4F4A8037725AD1B35FDB645B` |
| `sources\TerraFX.Graphics\RecordedGraphicsCommand.cs` | `346862C8D8FFFCE1A264E7EA970B0B1404CABAB75753CA1C3CA12B140A082A9A` |
| `sources\TerraFX.Graphics\BindingSet.cs` | `7389057053DBACE10ADC2B491AFF8B4FD7CED34D6797F206578B26F56252606C` |
| `sources\TerraFX.Graphics\BufferBinding.cs` | `98BCE317C28F2A14E0065FBF538899319AA2CAB755D884F922CCADBCF26B8513` |
| `sources\TerraFX.Graphics\TextureBinding.cs` | `30CF197D604AFE05C21E71D0BB7314CE5855C0937C27709B393D6E57E498F0CE` |
| `sources\TerraFX.Graphics\SamplerBinding.cs` | `FCE3C6F6530C14F17DC133739304219E402EF0B17D8848B4BD001BF5A758C990` |
| `sources\TerraFX.Graphics\GraphBufferVersion.cs` | `FEE9EE352B9C46186FA19BE2F28AFAB9EBC00D441BC23BE596569E5090BAE6BE` |
| `sources\TerraFX.Graphics\GraphTextureVersion.cs` | `FAC36E064D065C5901DDBF6C52B90B5ADCC655E630830FD1E4DEF6A9F9D75012` |
| `sources\TerraFX.Graphics\VertexBufferBinding.cs` | `3673F0E50881816DB9A08FE3197AF8E718349ED1207D2AE6A685BDDF97E02C3B` |
| `sources\TerraFX.Graphics\IndexBufferBinding.cs` | `E309A6D514B17EF4E724C4E7E6EC3FD160777CF4658BD75AD13760D68CB6139C` |
| `sources\TerraFX.Graphics\CommandRecorderState.cs` | `8154B152867064FF89D9B74D6F0639EF593A9F475A6D2C783B8E34F9C4287AFA` |
| `sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingState.cs` | `3F4B5B5F9B9B1C9F93E1B68CD45697D185CD7A6B92EE95741F16191EB0AE7199` |
| `sources\TerraFX.Graphics\SkinnedNormalDiagnosticRecordingRecipe.cs` | `FCEA487B4B8E92FC46C11FEEF4C8A4080BD96356A8189A2E9A182BE25F0CCF26` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `AFFB6015FAAB8941529A124875482B97A147136F60B8A7DB2A6B89046482689B` |
| `sources\TerraFX.Graphics\GraphicsDevice.RecordPass.cs` | `264194C27CE8E864685F00A7BA0B644A4525A5816FFAEB8D76C1D358A260C64D` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\IGraphicsBackendDevice.cs` | `5A39A41A96528156D774EFED908AEE3661B5DA1A6E8EE3E3A0FE005F39F9BD0E` |
| `sources\TerraFX.Graphics\TerraFX.Graphics.csproj` | `E04F4E614E10B07D60A9E56B1C60E69D85B63240339587271235D079E197CCD4` |
| `sources\TerraFX.Graphics.D3D12\D3D12GraphicsBackend.cs` | `3A78D1E2A78ABAEDCD1CFCBA2AC685DD85B5995A1A0808AEDAB78E1EFE702239` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDevice.GraphSubmission.cs` | `8182B1C554B908812F32926B1E16E3222378816E68A9FE16C28E1C549EDBA19A` |
| `sources\TerraFX.Graphics.Vulkan\VulkanBackendDescriptorSet.cs` | `FB3F31180F5AEA6BF87296B6F2E2C866152D4CCC074B590A578EE4D7AAE7AB8F` |
| `sources\TerraFX.Runtime\Rendering\RenderMeshC0NormalDiagnosticGraphPass.cs` | `554750E946DCADFD826C2B2348550B5ECE7C78B5BBC93084CB4F1BFCFD3FBB9B` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Recording.cs` | `ED5E694C9B698F6EA3535E46F80149A97CC8F9A67F8048240525D6BABFE28316` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `34AB6687DED10903F40453441F3158F78DC4D8C78D82982863CADE038870525C` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.cs` | `CF206DBB501A547170ED5CEAF8797BBB18AA1881EDFE2FB8B70DD5B2CD29F0D7` |
| `sources\TerraFX.Runtime\Rendering\RenderGraph.cs` | `47BFA4EF2FB3C0A374CC1C102A73BCD2F5874715346775DA6097F2696B7D3A78` |
| `sources\TerraFX.Runtime\Rendering\RenderSpriteGraphState.cs` | `BD9674DB5C661F0017CD0E0CDD99B6384898A89242B4F199EC2FA3771517BC14` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | `D7F9D4B4842D5026F5C4902866B7E2FEABC3509E61A6F0B8CD75CE2F9549A6B8` |
| `tests\TerraFX.Graphics.Tests\GraphicsPipelineRequestTests.cs` | `098FB1C79989C9356E8F1EAB175EDF6139D9BC95347E672E95411294EC20D3CC` |
| `tests\TerraFX.Graphics.Tests\TerraFX.Graphics.Tests.csproj` | `D23122E033214FE925BFD56D696E98A259C94DAE6896E0BF16895462644987BD` |
| `tests\TerraFX.Graphics.D3D12.Consumer\TerraFX.Graphics.D3D12.Consumer.csproj` | `C3A5489A9D808F3A7FAA0E21079C344D14E73C5640598BC8360C03131C19DEE1` |
| `tests\TerraFX.Runtime.Tests\Rendering\RenderMeshC0NormalDiagnosticBudgetTests.cs` | `F86055DEA983DD4E22AA5DF60937C2AFA34C514C85B2BE829D05C8E33ADF8175` |
| `tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj` | `B8DFBC8438BD32A93ACB54DD420AD09A7BF259C2DB5D366D2100229D066CF583` |
