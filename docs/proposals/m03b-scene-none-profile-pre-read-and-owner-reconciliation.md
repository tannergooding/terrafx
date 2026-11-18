# M03-B Scene None pre-read bounds and owner reconciliation

**Status: tentative decision for review under the requested unblocker policy. No complete numerical operation profile is admitted. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision and scope

Retain the [closed built-in admission decision](m03b-scene-built-in-codec-profile-admission.md): factory-registered Scene/Prefab 2.0 typed readers, package compression None, no selected `ISceneExpansionResourcePreparer`, and one Assets operation slot per preparation without a queue.

The distinct rectification here is to use the now-available exact metadata for a **pre-read structural allocation-site envelope**, while explicitly separating that envelope from a complete `C_op`. Reconcile the Assets slot as a funded child of the already precreated `PreparedScene`, not another preparation owner or another use. Enforce the selected None restriction on automatic reference inspections as well as typed reads.

This is the current decision/index for that seam at HEAD `7a4b6d988abe9382466936d8a4c885170e903c66`. The earlier proposals retain their scoped ownership recommendations; their source/binary freezes do not review this revision. This document neither promotes dirty design edits nor reaccepts historical compiled observations.

## Present boundary

Paths in the evidence column are beneath `sources`; the complete raw-file identities are below.

| Evidence | Established boundary | Remaining action |
|---|---|---|
| `TerraFX.Runtime\Scenes\SceneSupport.cs`; Scene/Prefab codecs; `TerraFX.Assets\AssetStoreRegistrationBuilder.cs`, `AssetStoreRegistration.cs`, `AssetCodecRegistrationReceipt.cs` | Factory registration binds each exact adapter/codec to its private versioned origin. Ordinary registration of the same built-in class receives no origin. | This establishes implementation provenance, not a byte profile. Qualify actual typed reads, not every unused registration in the Store. |
| `TerraFX.Assets\AssetOwnedReadDescriptor.cs`; Store/lease description methods | Metadata-only value descriptions capture Store, backing token, entry lengths/compression, receipt, immutable source limits, registration/schemas and spatial context without acquiring a backing or lease credit. | A description is neither a pin nor admission. It does not read the envelope to attest the entry's actual payload schema. Its carriers, shared metadata and cold infrastructure still need ownership attribution. |
| `TerraFX.Assets\AssetStore.ReadDescriptor.cs`; `AssetStore.AcquireLoad` | Described reads revalidate exact source/profile inputs before typed-load credits. A Store-current stale descriptor does not silently rebind. An explicitly anchored old backing remains usable while admitted. | Anchored facades acquire a temporary backing reference **before** entering the described async method; its refusal catch releases that reference. The descriptor path adds no preinstalled operation owner, funding or return acknowledgment. |
| `TerraFX.Assets\AssetStore.PackageLease.cs`; `AssetStore.ValidateAssetReferencesAsync`; `AssetPayloadReader.cs` | Described reads use the existing load/decode/inspection pipeline. Reference validation reads each target's envelope without invoking that target's codec `Read`. | Neither description nor the current pipeline selects a None-only profile. The automatic target reads still accept Brotli. Their metadata checks must precede their own payload read in the future qualified route. |
| `TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs`; `SceneLoader.cs` | Ordinary planning still calls `LoadAsync<SceneAsset>` and the root lease's ordinary Prefab load. Current Scene description helpers accept package leases; they are not execution wiring. Resource-preparer selection currently occurs after planning. | Use metadata-only Store preflight for the ordinary root and root-asset-lease preflight for Prefabs. Do not acquire a preliminary package pin merely to use the current helpers. The selected no-preparer restriction must refuse before the first root load, even if a preparer would return null. |
| `TerraFX.Runtime\Scenes\PreparedScene.cs`, `PreparedScene.Preparation.cs` | The candidate already anchors `use → PreparedScene`, with phase/pending owners and separate producer/body state. The null-footprint guard precedes use acquisition. | There is no Assets operation slot or complete operation grant. The existing pending-owner construction election is not one. Do not construct or execute the guarded preparation to obtain sizing feedback. |

**Distinguishing source-derived schedule, not an executed test:** a factory-coded Scene has a None root entry and a declared asset reference whose target entry is Brotli. Its root description satisfies origin and root-compression checks, but today's described load reaches `ReadBrotliAsync` during automatic reference validation. The selected route must reject that target before its payload allocation/read; root provenance alone cannot admit this call closure. A target that is merely inspected does not need its codec body profiled, because that body is not called.

## What can be derived before reading

Let `d` be the described decoded envelope length and `L` its exact immutable `ReadLimits`. Package admission bounds `d` and requires equal stored/decoded lengths for None. `EbinStructuralReader.Read` invokes its action only after complete recursive structural/canonical validation. `EbinCodec.ScanRecord` rejects overlapping positive field ranges; `ScanRecordArray` requires ordered, nonoverlapping children with complete record headers.

Consequently, for a complete validated envelope:

```text
r = min(L.MaximumAggregateRecords, floor(d / 64))
a = min(L.MaximumArrayElements, L.MaximumAggregateArrayElements)
n = min(a, r)
```

Here 64 is the existing EBIN record-header width, **not an object allowance**. Record headers occupy distinct byte ranges despite parent/child containment. Thus `r` bounds records in the envelope, `a` bounds any admitted array, and `n` bounds any record array. Incomplete/malformed structure cannot enter the semantic action; its read/failure costs remain required profile terms.

This supplies non-circular inputs for the built-in readers:

| Allocation family | Pre-read count/length bound; outstanding byte derivation |
|---|---|
| Hierarchy validation workspace | Entity and placement counts are each at most `n` and their existing Scene/Prefab reader ceilings. Substitute those upper bounds for discovered counts in the admission proposal's eight-array expression. Include the whole workspace carrier and sort/cold-comparer work. Its current `GetRequiredByteReservation` rounds array payloads to eight bytes, unlike `WorldAdmissionDomain.ArrayCharge`'s sixteen-byte rule; copying that number is not qualification. |
| Entities/components, hierarchy nodes/children, placements, selectors, references, overrides, slots and root poses | Their constructors/builders are reached by bounded record/array visits, with the readers' additional component/path/selector ceilings. Each copied encoded slice is at most `d`. Enumerate actual sites and their nesting/multiplicity rather than multiplying entity count by an assumed object size. Include component/literal copies, final asset encoded bytes, placement `ToArray` plus immutable conversion, and constructor collection conversions. Resolve actual copy/alias behavior and builder backing transfer from the final compiler/BCL, not source appearance. |
| Envelope asset-reference scan | Unique references are bounded by `r` and the scanner's existing reference ceiling. `Scan` nevertheless constructs a growing `List` and `HashSet`, then an immutable result. Their capacities, growth/replacement overlap, carriers and result copy require their own profiles. Directory entry count alone is not a failure-path bound on distinct reference tuples. |
| Automatic None reference inspections | Each target decoded length is at most `q = min(descriptor.MaximumDecodedAssetBytes, descriptor.PackageByteLength)` once its own exact entry passes None admission. The number of inspections is at most the scan's reference bound. These are inspection, not recursive typed-load, calls. |

For example, `ArrayCharge(d, 1)` bounds the initial direct payload array's accounting term. If `R` is the reference bound above, `R * ArrayCharge(q, 1)` conservatively covers all direct target payload arrays without assuming that sequential awaits joined their tails. These are **partial array terms only**, with checked arithmetic/conversions and refusal on overflow or insufficient original provision. They omit semantic results, collections, carriers, I/O/runtime machinery and protected failure/publication work.

A finite conservative **payload-derived storage catalogue is therefore derivable in principle** from exact metadata, immutable limits and these closed readers: sum reviewed site charges over independently bounded executions, including old/new growth and copy coexistence. No decoded-output measurement is needed to choose those bounds. Exact registry metadata remains an input; a receipt does not canonicalize every application component descriptor or fund registration storage.

**A complete finite operation-byte profile is not established by that argument.** Whole-type/collection/runtime charges and the final control layout are not supplied by the descriptor or semantic count ceilings. The existing Assets budgets charge declared decoded wire bytes, not the codec-owned managed graph. Do not turn either budget, a successful decode, or the available remainder of P into an object-size multiplier.

Pinning the runtime does not make arbitrary flowing `ExecutionContext`/`AsyncLocal` behavior or external Task-observer fanout a constant derived from `d` or `L`. The older handoff's tentative external-continuation provenance rider covers only its named external registrations; it exempts neither Scene-created registration/context work nor the producer's enclosing return. Those ownership/runtime boundaries remain prerequisites, not unspecified constant terms.

The decoded value and reference array coexist with target inspection. `LoadAssetAsync` clears one `encodedEnvelope` alias and returns its initial decode counter before validation; neither establishes every compiled producer's last access. Also, `ReserveDecodeBudget(backing)` consults the Store's current backing ceiling when present: replacement can change admission/refusal, but cannot enlarge the qualified source-storage envelope. Keep that admission policy distinct from immutable profile inputs.

## One root, one subordinate operation slot

The [older handoff](m03b-scene-preparation-handoff-closure.md) selects one stable `PreparedScene` and says not to allocate another per-operation owner. The [pre-lease proposal](m03b-assets-prelease-recovery-owner.md) subsequently selects Assets-owned operation controls. Reconcile these exactly:

- The prohibition remains absolute for a **second preparation carrier, root, use or purse**. It does not prohibit the newer, explicitly funded lower Assets progress owner. That is the only clarification to the older wording; its producer/body join and the [compiled-producer addendum](m03b-scene-preparation-compiled-producer-evidence-addendum.md)'s unresolved proof obligations remain.
- Embed one fixed child-operation slot/election in the existing `PreparedScene` carrier and include those fields in its whole-carrier catalogue. Keep `_phaseOwner` and `_pendingOwner` for their actual planner/receiver handoffs; do not overwrite a builder with an Assets operation or replace the use anchor with either object.
- Under the existing preparation gate, require Building/Running state and the empty child slot, elect construction and debit the complete disjoint original-P subclaim **before** constructing the empty Assets owner or invoking an async facade. Construct outside gates, install that exact empty owner, then authorize acquisition. Assets records every acquired reference/credit/intermediate before another fallible step or async escape. Failed installation starts no lower acquisition.
- Automatic reference validation uses this same owner and profile; it does not reserve another slot. Revalidate each exact source entry and its None/inspection membership before reading. Assets receives opaque identities/ownership state, never a Runtime sizing callback, World capability or `ScenePreparationFunding`.
- Install a returned lease into the existing rooted planner destination under the selected installation/close fence, then acknowledge that exact transfer once. Keep any result storage funded by P as an identified retained partition with that destination. Emptying the temporary operation slot cannot refund live result/lifetime/handle storage. Only independently proved Assets provision is exempt; later receiver settlement uses the existing exact covered-workspace transfer, not all of `C_op`.
- Extend the original close election to retain construction/start/installation interests and all still-consuming phase/pending inputs. Close may release only obligations whose consumers ended or transferred; it cannot race the operation's fields or recipient handoff. Park cleanup eligibility without a callback, wait, I/O or physical release under the preparation gate.
- Release the slot only after all lower obligations ended/transferred **and actual operation/invoker return joined**. Task readiness and destination assignment are not that event. A real failure retains the slot, original claim and root; there is no queue, successor cleanup allowance or retry of uncertain release.

`TryClaimProducerWorkspace` currently subdivides one scalar producer allowance. It neither supplies this construction election nor proves a partition between fixed producer controls, child workspace and protected cleanup/publication. Close that partition in the final catalogue; do not spend fixed J/K provision as scratch or treat an empty slot as a transitive Task/context count. Root release must join the child slot as well as its existing producer/body conditions. Do not clear `_supplierProducersUnjoined` on a child Task outcome.

## Open categories and admission disposition

| Category | Current evidence and precise missing prerequisite | Disposition |
|---|---|---|
| Construction, Task/context and cold state | Described methods remain async wrappers over async payload and `RandomAccess` I/O; root TCS/Task/delegates, gates, metadata initialization and operation controls have no complete final catalogue. Semantic record counts do not size CLR boxes, registrations, cancellation, dispatch or context restoration. | **Numerical `C_op` and preparation J/K: NO-GO.** Freeze final layouts and the exact supported compiler/BCL/runtime/target closure, derive each complete charge and coexistence before construction. |
| Retained result | `SceneAssetReader` retains semantic arrays and encoded copies; `SceneAsset`/`ScenePrefabAsset` retain them plus schema/limit aliases. Planner caches and `SceneExpansionPlan` retain leases beyond a read. No complete result charge/partition or independent managed-storage exemption is established. | **Result settlement: NO-GO.** Identify the complete retained graph, issuer, exact installation acknowledgment and existing receiver transfer. No temporary-claim refund of live retained bytes. |
| Cleanup, fault and publication overlap | Issued lease/lifetime/backing state now retains failed release inputs and records backing success before accounting; old clear-before-release descriptions are not current. Pre-lease gaps remain: `CreateLease` constructs its lifetime before `try` and sets transfer in cleanup `finally`; anchored refusal/inspection finally can release a temporary final reference without an issued result. Package-pin construction rollback returns its credit before backing release. Plan cleanup also reaches EDI capture. | **Complete cleanup/fault/publication profile: NO-GO.** Preinstall ownership and protected peak provision, preserve the original outcome and bounded cleanup facts, and retain irreversible progress. A retained exception reference does not bound new capture/native fault work. |
| Actual return | `PreparedScene` sets `_supplierProducersUnjoined` before planner invocation and never clears it. Stock Task/ValueTask suppliers, I/O and flowing completion wrappers remain. The three ordinary driver parts and one proposed Assets slot prove neither enclosing runtime/context return nor transitive last access. | **Normal reusable return lane: NO-GO.** Require the real enclosing invoker's last-access/transfer proof for the claimed target. No observer chain, `UnsafeOnCompleted`/flow suppression, or permanent quarantine of normal success substitutes for that lane. |
| Physical close | `AssetPackageBacking` keeps its stream through `Dispose` and retains failure; issued handles record lower release progress. `AssetPackageReader.OpenAsync` creates the real read-only asynchronous `FileStream`. No reviewed complete nonthrowing FileStream/SafeFileHandle/native closure or ownership-preserving lower acknowledgment is supplied here; no reachable valid close failure was observed. | **Physical-close qualification: NO-GO.** Retaining a stream object is not proof of retained uncertain OS ownership. Reconsider only with the missing lower proof/capability or a supported real stimulus; never infer success from a second Dispose or repeat an unchanged blocked probe. |
| Other callers and whole preparation | Public Assets pre-lease recovery, package opening/bootstrap, Persistence/measurement/restore, Rendering and Brotli remain outside the selected caller-rooted slice. Ordinary planner construction/cache/workspace and whole preparation acceptance also remain open. | **Unselected, not repaired by descriptions.** No new Store pool or borrowed Scene grant. A future complete child profile would still not enable whole Scene preparation or establish M03/M05/native/AOT/public-consumer acceptance. Independent dependency-ready M05 work can continue. |

## Prerequisites and bounded next action

The next bounded batch is the **None allocation-site catalogue and final compile-only child-slot/control shape**, using the existing planner/read/release algorithms behind the unchanged guard:

1. Close the site set above through real reader constructors, scanner/collection growth, reference inspection, actual I/O/runtime helpers, construction unwind and cleanup/publication overlap. Record full type/capacity charges, pre-read count inputs, issuer, retained partition and actual end/transfer for every term. Missing terms leave the profile unavailable; no placeholder Ready entry or guessed constant.
2. Make only the subordinate slot/construction/installation boundary concrete, with metadata-only Store/root-lease preflight and per-target None enforcement on that qualified route. Preserve ordinary public Assets behavior, exception/cancellation precedence, exact-package binding and the existing lower receiver mechanism. Final slot/control fields must be included before numerical review. Compiling a shape does not authorize constructing it through a guarded preparation.
3. Before any live admission, close the complete operation and preparation claims, actual return and physical-close gates in the table. Use real packages/registrations for later behavior checks; capacity/slot-return checks require a genuinely admitted profile. Do not forge a profile, producer, return acknowledgment, allocation failure or native-close stimulus.

No source or test change is made by this proposal. Supplied verification remains Assets **44/44 Debug** and the focused Runtime Scene registration/metadata-preflight selection **16/16 Debug**, not rerun here. The reported broader partial-class selection reached guarded Scene preparation and failed; it supplies no GPU submission evidence. Historical producer IL, managed helper tests and the dirty retained-asset consumer do not supply current return/native/AOT qualification.

## Frozen review inputs

Captured UTC `2026-11-18T16:47:54.8290512Z`; HEAD `7a4b6d988abe9382466936d8a4c885170e903c66`. All 90 listed inputs were checked against this capture before writing the proposal; recheck them before disposition. Changed bytes require reassessment.

`C` means tracked and clean at capture; `D` means a pre-existing tracked modification. Dirty maintained chapters are inspected context, not accepted amendments. Other original dirty/untracked paths are outside this review's evidence scope and must remain untouched. No schema or fixture version is changed.

Each entry gives its exact relative Windows path and SHA-256 of raw file bytes. The union digest is `6f4fce4779a088336953aea87bc0f235550a5bad0206c6c749b1472e42346527`: SHA-256 of UTF-8 without BOM/trailing newline, over ordinal-path-sorted `path<TAB>hash` lines joined with LF, omitting the state marker. This bounded source/document freeze is not a transitive runtime/binary attestation.

```text
C .github\copilot-instructions.md 12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e
C .github\workflows\ci.yml 03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58
C Directory.Build.props 770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a
C Directory.Build.targets aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105
C docs\design\02-dotnet-api-memory-and-ownership.md 40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291
D docs\design\04-capacity-and-workload-profiles.md a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245
C docs\design\11-assets-import-streaming-and-packages.md de1032a04217b2a08933332aee7db22df849cbfc069e76cf6b2263bf469df109
D docs\design\16-scenes-transforms-and-cameras.md b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a
C docs\design\29-project-setup-build-and-deployment.md 3b55a013eec327ecd648acbd5b53717dd218ad288e8047ac21277609efa2f47b
D docs\design\30-public-api-review-surface.md 15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b
C docs\design\35-implementation-roadmap.md 24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87
C docs\project\Building.md f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f
C docs\project\Roadmap.md 1c8c3f8e140f47492255534284abbf67c5776210dc7a68f2c01155d0be773f46
C docs\proposals\m03b-assets-prelease-operation-profile-capacity-addendum.md fb07baef70412a2ce33f56be8efdb3909046ad64a4fd5227e4731fa188e5a868
C docs\proposals\m03b-assets-prelease-recovery-owner.md f947e6e040cf6dfa5a5a04de1c84c257536a9a37b3794c0cefcd5555cd55c18c
C docs\proposals\m03b-scene-built-in-codec-profile-admission.md 8ca51d67ca00523270dd41a21a8c53cb4cdf65c08ee30fe127eba60f016c1b50
C docs\proposals\m03b-scene-preparation-compiled-producer-evidence-addendum.md eccc174d5df86e195731eb3be74fbe5a349d0214c8f94de35769503d4418595c
C docs\proposals\m03b-scene-preparation-handoff-closure.md 628b09039114aacaea65116dd584b2f94b554011306ab600414c5a2a809a7fba
C docs\proposals\m03b-scene-producer-return-and-assets-release-rectification.md 199429a1eb94b5fe3f9f0da204343e310318d2336acdbc993a601e3a93db105b
C global.json 1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c
C sources\Directory.Build.props d44373af315523f8af9f09f36bded2a688e2b5c1572fcc0732310c7ef590ead8
C sources\TerraFX.Assets\AssetCodecContext.cs 965107b1472dc5bcf29a60a427ad023d398faca47d7f318fd095912d1dddbfa4
C sources\TerraFX.Assets\AssetCodecRegistrationReceipt.cs 6a128c55231fbec296f493eda57f2a2e479eb1b2d9964436635fe18a96903493
C sources\TerraFX.Assets\AssetEnvelopeDecoder.cs a2b496af751b34c20a89d689f69a83277d3cebe961511676b69df9d0d4e8fb6f
C sources\TerraFX.Assets\AssetLease.cs 9f39981151348bc680fda81347312c7b79bbf7db8d3463cae352dba203f74bc4
C sources\TerraFX.Assets\AssetLeaseLifetime.cs 9bee340781b9b353d5a1a9b8bd7aaf45d23ecb8c884c615203594c9216cdc4b8
C sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs d4fbd00545f922f4cf20876a0723a5d36af7038213b1a597a92a6421fcf825ba
C sources\TerraFX.Assets\AssetPackageBacking.cs a81a26699cd8dff3d442745f3afad9e313349e771da0f95febfe2dbf9d0cf5c9
C sources\TerraFX.Assets\AssetPackageLease.cs 9046527a44e8500eee582e865e1f0bd0e293981985118af9699a5d2168cf3bdc
C sources\TerraFX.Assets\AssetPackageReader.cs 06f12e7b2d7d89a44a92e28f41a8581af3a64f963df0dbdd935da2722984b4fd
C sources\TerraFX.Assets\AssetPayloadReader.cs 1bdf040211fdaad0a1347ce9c1d8a478d65743ed543f362bf60d1e16159b5d03
C sources\TerraFX.Assets\AssetReferenceScanner.cs ef0232bcc4f384fe6025a8f98cb11d7c6e70033ff36f31e796994a314ae67bbf
C sources\TerraFX.Assets\AssetStore.cs 51cffa2ccc7f6e318db395fe353d342575253af4ae26a93c8a1925dc4066b850
C sources\TerraFX.Assets\AssetStore.PackageLease.cs 0a4f0452a6e55ca84540769d30ac8ff99f4ef8ecf05bafaec74cf766aedb50fe
C sources\TerraFX.Assets\AssetStore.PackagePin.cs 270dabda38f2474b7d48b02a230f1492e2a3bcfcc6963a8ad1bd102ca6ceca53
C sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs 71a4931374dd25495b9c1df0c8e76e2ceca911cb048bd59077736e3933756222
C sources\TerraFX.Assets\AssetStoreOptions.cs bb2123297d3e65309bd38ce5da0083e14319810139fa233ca0b0ebc4e5404594
C sources\TerraFX.Assets\AssetStoreRegistration.cs 08deeb9061644b821228ee6d794be012b4e7aed743860422e22273c7bd04c2f2
C sources\TerraFX.Assets\AssetStoreRegistrationBuilder.cs f41b64e36f19951b2a55d662d4f879f0d669a7b603faa0128f98acdf23197f38
C sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs 6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a
C sources\TerraFX.Assets\TerraFX.Assets.csproj 76f077c074a4198c78984e46cf7adff94e23092abb3fa87ce18865bb31621101
C sources\TerraFX.Core\EbinCodec.cs 65d47557eb96fd79831bf67f3198e46c6ae82567788ee92f13e0870bd211ab00
C sources\TerraFX.Core\EbinReadLimits.cs ec0f5ba10d1bca6bea3345c60de7bfa8b665e909b61f1f097e61d32783cf30a9
C sources\TerraFX.Core\EbinRecordReadView.cs 05dbde5a42dbefe96cfc14d5984d9ba74c1b0fab515c79c322a62baca2cc21a5
C sources\TerraFX.Core\EbinScanState.cs 19b4b605fd018b7e5650cd96962211c6b7d41bdf93a2d036043f5d9570d3c214
C sources\TerraFX.Core\EbinStructuralReader.cs 43b455f4a056a6df2bee31e52e0ecacf7c128bb33a8cb6a662386e8071575e1c
C sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs a250eecca2cfb4e0eebff7f447e66ac7e44a8d23ad0d6de141e8e1d4c30614e3
C sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.ScenePreclaimedWorkspace.cs ceac98719116305ba2727b892ce540a3b6b26d1ddf72afe06e21504909747fb0
C sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs f00b54b0e8f305f90df0d63ffdf6c3419feb8f3d2542e72d76a79ad2d0990946
C sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs a91aac1a9eb2ba7b43c111fab8baa9d6588a6c75e725c28ea9825f049472c969
C sources\TerraFX.Runtime\Scenes\PreparedScene.cs ebe0f9fca31779c5e4c3f8a1d4589ceae489536c904bad338f14acb052e843e4
C sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs beed8cc76c4ccd55b487a96b52a31d4765a4e40513ef09883d7b0f56a201a8ff
C sources\TerraFX.Runtime\Scenes\SceneAsset.cs 1865fdc7ad0b77fdcedc4a6d26b56358aceca95647ad55a63f1dbcd4d716ead6
C sources\TerraFX.Runtime\Scenes\SceneAssetCodec.cs b7a29a5bdb1d73fd62d29f4c943b81b0582100a7dd4fd41fc7dcb73faaf8bf53
C sources\TerraFX.Runtime\Scenes\SceneAssetReader.cs d0493b1f2d447db5a7ea5a5df501e046d0c53c3bae5219a0e626c0f25da6f90e
C sources\TerraFX.Runtime\Scenes\SceneAuthoredComponentRecord.cs 3a66068edf88575f61fbe23d54d745a1422465c6f7a3597e39714e39522b62f4
C sources\TerraFX.Runtime\Scenes\SceneAuthoredRecord.cs 78738c7d0e502ee400fc4160394b4e419aa3c5fa6855a7165114721040a9c8a6
C sources\TerraFX.Runtime\Scenes\SceneEntity.cs 6183b754e0a539b8e0297963ca1fc15bf72d361c5569c3af8a7446da990ce6f7
C sources\TerraFX.Runtime\Scenes\SceneEntityReader.cs 5678f2667e408562758aab6c23d0f0a57e34f5c3019daf2fb6f625da31a0d08b
C sources\TerraFX.Runtime\Scenes\SceneEntityReferenceBinding.cs eb4f2399b5f205f91c240d39c054331d63c6196bfd98701d6d119ef06fcd01fc
C sources\TerraFX.Runtime\Scenes\SceneEntityReferenceBindingValidator.cs a0db38b6008960e7e36779b7e42a2f291dcd830d0e2679037a02dbdfaa10512f
C sources\TerraFX.Runtime\Scenes\SceneEntitySelector.cs 343218eaa00bf04b2df9a79276f274ea2f78127332980ad014a46b02ddf6effb
C sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs 4161b2da562fe16b7d169c739402bd250fd498e0125d27964996868bfa9a9aec
C sources\TerraFX.Runtime\Scenes\SceneExternalSlot.cs 2153471a809cfc4b974747b6627640414922df2d7b700dc3ece16c192c04aeb0
C sources\TerraFX.Runtime\Scenes\SceneHierarchy.cs 4d96767385fe653a6be70e3d622086ab7809714b76518a0740f60ad4ee6af50c
C sources\TerraFX.Runtime\Scenes\SceneHierarchyEntry.cs b6c777d55cbe1c4a9cab9ca2820faae9016d8a2a08f66f5571524a47cbeb1238
C sources\TerraFX.Runtime\Scenes\SceneHierarchyNode.cs 4e008146d2ff16dd21780821c3d29927df9ee6706fd54586507c86d0c4cd33b6
C sources\TerraFX.Runtime\Scenes\SceneHierarchyReader.cs eaf025062187d009bd82f676952ccf760d208f152bcbb2796e0aa74481dcde5c
C sources\TerraFX.Runtime\Scenes\SceneHierarchyValidationWorkspace.cs c62ffcf12121713119a6492b2cb2eb7867946f0ba598f194acf71fa8101a572f
C sources\TerraFX.Runtime\Scenes\SceneHierarchyValidator.cs dd6c109c1e2b8cc87a51cbdba83e3abea8269d65b1f92dd925c9926feb064236
C sources\TerraFX.Runtime\Scenes\SceneLoader.cs 4903a86fc323ed906a0045288bebab68b37c5b6beeff6312ea0440a9d05d453c
C sources\TerraFX.Runtime\Scenes\ScenePlacementSlotBinding.cs 2f1fe3abc598765e24407c58aafaedd6f790ebc58747805261ee17ba920b56cb
C sources\TerraFX.Runtime\Scenes\ScenePrefabAsset.cs 70e859e5d7dd16731d731ae8f1347a7c406218e0180b2f0a7a6d06b90ae5c7c3
C sources\TerraFX.Runtime\Scenes\ScenePrefabAssetCodec.cs fcd81d97d3e07956aa35b12871b7c3bccd398ddf8761c3e5a4d38e84f7bcfbcd
C sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs 0dad6b609e7ef9fbad9ecc8f9221c4efac00fd05a1276d86939bf71c6fe47019
C sources\TerraFX.Runtime\Scenes\ScenePrefabOverride.cs beb8521edbaf8658ab576953037e31391d8620212fb270793f1961344bbd6ab2
C sources\TerraFX.Runtime\Scenes\ScenePrefabOverrideApplier.cs 24f869814ad49ddffd8cba7fe4e5d0ddf91a84b0ce3955e60db3878e6706638c
C sources\TerraFX.Runtime\Scenes\ScenePrefabPlacement.cs d1fc388ab9fb6b955f7bf106d1f7d9510c408df5f4465de36325da8f0979cba3
C sources\TerraFX.Runtime\Scenes\ScenePrefabRootPose.cs 122039ba23994c12c5ea09ad3bda1af4776236ade0e41dcaa69887c48766076f
C sources\TerraFX.Runtime\Scenes\ScenePreparationFunding.cs 4d02c65f83b6c06a62408c93f1e1af9912e3446f6575614483b0ff6b83096522
C sources\TerraFX.Runtime\Scenes\ScenePropertyPathSegment.cs 974ad1c933bc3acbae57354e0a96cae5930111e15d765a65c7d0653961e6d236
C sources\TerraFX.Runtime\Scenes\SceneReferenceTarget.cs 4656b39f2a0f4f444388c8020be0aa5b04488d8811ab9d8445b1a30ef876afde
C sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a
C sources\TerraFX.Runtime\Scenes\SceneSupport.cs 69749b52d524bf29c0f7d906676c6d233defb33dc034615fc9d549aa40ea7ed5
C tests\Directory.Build.props 281aad4237121feab1903c2384f1a5f0aed7e218a704c918fe953e9fc8e75764
C tests\TerraFX.Assets.Tests\AssetStoreTests.cs 12c44e3010c89793ab6ed4e1603cb490a535c42113ea664134a8b6238b9b75ad
D tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4
C tests\TerraFX.Runtime.Tests\Scenes\ScenePrefabPackageLeaseTests.cs 094d0a7760f042e5c1dd1904197e803bbb883029ee6e22ace33c233bc5899e38
C tests\TerraFX.Runtime.Tests\Scenes\SceneRestorePreparationGateTests.cs 3491589ec2719c488d1ec62601ab724a10aa36206b57a56a83a137ca295b5482
C tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs d0c2f403cfd67c4937b0e21a9a27a814eaac3f85fb178bb26eb3f4819d282421
```
