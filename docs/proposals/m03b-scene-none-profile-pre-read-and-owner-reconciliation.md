# M03-B Scene None pre-read bounds and owner reconciliation

**Status: tentative decision for review under the requested unblocker policy. Profiled None checks and the metadata-only root/Prefab read path are implemented behind the preparation guard; complete operation/preparation admission remains NO-GO. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision and scope

Retain the [closed built-in admission decision](m03b-scene-built-in-codec-profile-admission.md): factory-registered Scene/Prefab 2.0 typed readers, package compression None, no selected `ISceneExpansionResourcePreparer`, and one Assets operation slot per preparation without a queue.

Use exact metadata for a **pre-read structural count/length envelope**, not a complete `C_op`. Reconcile the selected Assets slot as a funded child of the already precreated `PreparedScene`, not another preparation carrier, root, use or purse. Reuse the implemented None restriction on described typed reads and their automatic reference inspections; do not extend it to ordinary public Assets reads.

The decision freeze remains at HEAD `8c607f9bc09277518372c692acb5b1c574abea1c`, reassessed after source commit `f0a7e28edac3f9df5f688db8a5e94f6a33ed9ab4`. The post-freeze implementation delta is `295e96ee7ee412fef466249fbc0eef99c103be8d`; it wires metadata preflight and described root/Prefab loads into the compile-only candidate without changing the NO-GO gates. It supersedes this document's `7a4b6d98` freeze and its claim that described automatic reference reads still accept Brotli. Earlier proposals retain their scoped ownership recommendations, not competing current-state assessments or qualification of these bytes. Dirty design edits remain inspected context, not accepted amendments.

## Present boundary

Paths in the evidence column are beneath `sources`; the complete raw-file identities are below.

| Evidence | Established boundary | Remaining action |
|---|---|---|
| `TerraFX.Runtime\Scenes\SceneSupport.cs`; Scene/Prefab codecs; `TerraFX.Assets\AssetPackagePayloadCodec.cs`, registration adapters and receipts | Factory registration binds each exact Scene/Prefab 2.0 adapter to its private versioned origin and `RequiredPackageCodec = None`. Ordinary registration of the same built-in class receives neither restriction nor origin. | This establishes provenance and restriction metadata, not a byte profile or a global Brotli prohibition. Qualify actual typed reads, not every unused registration in the Store. |
| `TerraFX.Assets\AssetOwnedReadDescriptor.cs`; Store/lease description methods | Metadata-only descriptions capture Store, backing token, entry lengths/codec, receipt and required package codec, immutable source limits, registration/schemas and spatial context without acquiring a backing or lease credit. Profile-incompatible root entries return no descriptor. | A description is neither a pin nor admission. It does not read the envelope to attest the entry's actual payload schema. Its carriers, shared metadata and cold infrastructure still need ownership attribution. |
| `TerraFX.Assets\AssetStore.ReadDescriptor.cs`; `AssetOwnedReadDescriptor.Matches`; `AssetStore.AcquireLoad` | Described reads revalidate exact source/profile inputs, including the required package codec, before typed-load credits. A Store-current stale descriptor does not silently rebind. An explicitly anchored old backing remains usable while admitted. | Anchored facades acquire a temporary backing reference **before** entering the described async method; its refusal catch releases that reference. The descriptor path adds no preinstalled operation owner, funding or return acknowledgment. |
| `TerraFX.Assets\AssetStore.PackageLease.cs`; `AssetStore.ValidateAssetReferencesAsync`; `AssetPayloadReader.cs` | Described loads carry the required codec through the existing pipeline. Automatic reference validation rejects an incompatible target entry before that target's decode-budget reservation and payload read. Inspection invokes no target codec `Read`. | This closes the profiled None target-filtering gap in source, not whole-operation admission or cleanup. Ordinary reads supply no restriction; actual target schema/exact-codec membership is checked after reading its envelope. |
| `TerraFX.Runtime\Scenes\SceneSupport.cs`, `ScenePrefabExpansionPlanner.cs`, `SceneLoader.cs` | The candidate now captures a Store-root descriptor into `PreparedScene` and uses described Store/root-lease reads for root Scene and Prefab loading only when a preparation owner is supplied. Direct measurement/restore paths and ordinary public Assets reads remain unchanged. CPU resource-preparer refusal occurs before descriptor capture or root loading once the candidate enters. | This is still only metadata and dispatch wiring. The child Assets operation is not installed/rooted, so the described-read candidate must remain unreachable behind the null-footprint guard. Do not add a preliminary package pin. |
| `TerraFX.Runtime\Scenes\PreparedScene.cs`, `PreparedScene.Preparation.cs` | The candidate still anchors `use → PreparedScene`, with phase/pending owners and separate producer/body state. `QualifiedFootprint` remains null and its guard precedes the new preflight and use acquisition. | There is no Assets operation slot or complete operation grant. The existing pending-owner construction election is not one. The new descriptor field adds to the final carrier catalogue; do not construct or execute this candidate to obtain sizing feedback. |

### Profiled versus ordinary reads

`LoadDescribedAsync` and `LoadDescribedFromPackageAsync` supply the descriptor to `AcquireLoad`. After `Matches` succeeds, `AcquireLoad` supplies `descriptor.RequiredPackageCodec` to `LoadAssetAsync`, which forwards it to `ValidateAssetReferencesAsync`. Each target's entry-codec check precedes `ReserveDecodeBudget` and `AssetPayloadReader.ReadAsync`. The root has already been read, decoded and scanned at this point; this is a target pre-read check, not a claim that the whole reference closure was admitted before root allocation.

Public `AssetStore.LoadAsync` and `AssetLease.LoadFromPackageAsync`, and the ordinary package-lease route, instead use `expectedDescriptor: null`. `AcquireLoad` deliberately supplies a null restriction even when the registered adapter carries None. Their root and automatic reference reads retain normal None/Brotli behavior. Explicit `MatchesAssetReferenceAsync` inspection is also unrestricted; it is not the automatic described-load path.

**Distinguishing source-derived schedule, not an executed test:** a factory-coded Scene has a None root entry and a declared reference to a Brotli target. Its description succeeds, but its described load now throws `NotSupportedException` before reserving decode bytes or allocating/reading the **target** payload. The same ordinary load remains able to reach `ReadBrotliAsync`. A target that is merely inspected needs an exact registered codec after envelope inspection, but not a profiled codec body: that body's `Read` is never called.

The new `preparationOwner` branch in `ScenePrefabExpansionPlanner.PrepareCoreAsync` consumes the descriptor captured by `PreparedScene`; `GetPrefabAsync` derives each Prefab descriptor from the exact root Scene lease. It does **not** preinstall an Assets progress owner. These branches cannot execute through current `SceneLoader.PrepareAsync` because `QualifiedFootprint` is still null; they are compile-only candidate wiring, not successful producer evidence.

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
| Automatic None reference inspections on the described path | Each target decoded length is at most `q = min(descriptor.MaximumDecodedAssetBytes, descriptor.PackageByteLength)` once its own exact same-backing entry passes the implemented None check. The number of inspections is at most the scan's reference bound. These are inspection, not recursive typed-load, calls. This bound does not select None for ordinary public reads. |

For example, `ArrayCharge(d, 1)` bounds the initial direct payload array's accounting term. If `R` is the reference bound above, `R * ArrayCharge(q, 1)` conservatively covers all direct target payload arrays without assuming that sequential awaits joined their tails. These are **partial array terms only**, with checked arithmetic/conversions and refusal on overflow or insufficient original provision. They omit semantic results, collections, carriers, I/O/runtime machinery and protected failure/publication work.

These are pre-read upper bounds for the listed **payload-governed counts and lengths**, not a completed storage catalogue. Site multiplicity, concrete whole-type/collection charges, growth/copy coexistence and actual end/transfer must still be derived. No decoded-output measurement is needed to choose these count bounds. Exact registry metadata remains an input; a receipt does not canonicalize every application component descriptor or fund registration storage.

**A complete finite operation-byte profile is not established by that argument.** Whole-type/collection/runtime charges and the final control layout are not supplied by the descriptor or semantic count ceilings. The existing Assets budgets charge declared decoded wire bytes, not the codec-owned managed graph. Do not turn either budget, a successful decode, or the available remainder of P into an object-size multiplier.

Pinning the runtime does not make arbitrary flowing `ExecutionContext`/`AsyncLocal` behavior or external Task-observer fanout a constant derived from `d` or `L`. The older handoff's tentative external-continuation provenance rider covers only its named external registrations; it exempts neither Scene-created registration/context work nor the producer's enclosing return. Those ownership/runtime boundaries remain prerequisites, not unspecified constant terms.

The decoded value and reference array coexist with target inspection. `LoadAssetAsync` clears one `encodedEnvelope` alias and returns its initial decode counter before validation; neither establishes every compiled producer's last access. Also, `ReserveDecodeBudget(backing)` consults the Store's current backing ceiling when present: replacement can change admission/refusal, but cannot enlarge the qualified source-storage envelope. Keep that admission policy distinct from immutable profile inputs.

## One root, one subordinate operation slot

The [older handoff](m03b-scene-preparation-handoff-closure.md) selects one stable `PreparedScene` and says not to allocate another per-operation owner. The [pre-lease proposal](m03b-assets-prelease-recovery-owner.md) subsequently selects Assets-owned operation controls. Reconcile these exactly:

- The prohibition remains absolute for a **second preparation carrier, root, use or purse**. It does not prohibit the newer, explicitly funded lower Assets progress owner. That is the only clarification to the older wording; its producer/body join and the [compiled-producer addendum](m03b-scene-preparation-compiled-producer-evidence-addendum.md)'s unresolved proof obligations remain.
- Embed one fixed child-operation slot/election in the existing `PreparedScene` carrier and include those fields in its whole-carrier catalogue. Keep `_phaseOwner` and `_pendingOwner` for their actual planner/receiver handoffs; do not overwrite a builder with an Assets operation or replace the use anchor with either object.
- Under the existing preparation gate, require Building/Running state and the empty child slot, elect construction and debit the complete disjoint original-P subclaim **before** constructing the empty Assets owner or invoking an async facade. Construct outside gates, install that exact empty owner, then authorize acquisition. Assets records every acquired reference/credit/intermediate before another fallible step or async escape. Failed installation starts no lower acquisition.
- Automatic reference validation must use this same proposed owner and profile; it does not reserve another slot. Reuse the implemented exact target-entry None check before its decode reservation/read. Actual schema and exact registered-codec membership remain post-inspection checks. Assets receives opaque identities/ownership state, never a Runtime sizing callback, World capability or `ScenePreparationFunding`.
- Install a returned lease into the existing rooted planner destination under the selected installation/close fence, then acknowledge that exact transfer once. Keep any result storage funded by P as an identified retained partition with that destination. Emptying the temporary operation slot cannot refund live result/lifetime/handle storage. Only independently proved Assets provision is exempt; later receiver settlement uses the existing exact covered-workspace transfer, not all of `C_op`.
- Extend the original close election to retain construction/start/installation interests and all still-consuming phase/pending inputs. Close may release only obligations whose consumers ended or transferred; it cannot race the operation's fields or recipient handoff. Park cleanup eligibility without a callback, wait, I/O or physical release under the preparation gate.
- Release the slot only after all lower obligations ended/transferred **and actual operation/invoker return joined**. Task readiness and destination assignment are not that event. A real failure retains the slot, original claim and root; there is no queue, successor cleanup allowance or retry of uncertain release.

`TryClaimProducerWorkspace` currently subdivides one scalar producer allowance. It neither supplies this construction election nor proves a partition between fixed producer controls, child workspace and protected cleanup/publication. Close that partition in the final catalogue; do not spend fixed J/K provision as scratch or treat an empty slot as a transitive Task/context count. Root release must join the child slot as well as its existing producer/body conditions. Do not clear `_supplierProducersUnjoined` on a child Task outcome.

## Open categories and admission disposition

| Category | Current evidence and precise missing prerequisite | Disposition |
|---|---|---|
| Complete operation/preparation profile | Described methods remain async wrappers over async payload and `RandomAccess` I/O; root TCS/Task/delegates, gates, metadata initialization and operation controls have no complete final catalogue. Semantic counts do not size CLR boxes, registrations, cancellation, dispatch or context restoration. Profile-refusal fault paths also need charges. | **Complete numerical `C_op` and preparation J/K: NO-GO.** Freeze final layouts and the exact supported compiler/BCL/runtime/target closure, derive each complete charge and coexistence before construction. The payload-derived envelope is not this profile. |
| Retained-result partition | `SceneAssetReader` retains semantic arrays and encoded copies; `SceneAsset`/`ScenePrefabAsset` retain them plus schema/limit aliases. Planner caches and `SceneExpansionPlan` retain leases beyond a read. No complete result charge/partition or independent managed-storage exemption is established. | **Result partition/settlement: NO-GO.** Identify the complete retained graph, issuer, exact installation acknowledgment and existing receiver transfer. No temporary-claim refund of live retained bytes. |
| Pre-work operation owner and installation | Descriptions/revalidation and None filtering acquire no operation grant. The candidate planner now routes root and Prefab reads through described overloads, but no Assets child operation slot is installed. The anchored Prefab facade still acquires its temporary backing reference before the async admission wrapper; returned leases are still staged only after the load returns. | **Operation-owner construction/installation: NO-GO.** Close the original-P debit/construction/install/start election, lower progress recording, destination installation/close fence and exact acknowledgment without another preparation carrier/use. Do not make the described candidate reachable before that slot exists. |
| Cleanup, fault and publication overlap | Issued lease/lifetime/backing state now retains failed release inputs and records backing success before accounting; old clear-before-release descriptions are not current. Pre-lease gaps remain: `CreateLease` constructs its lifetime before `try` and sets transfer in cleanup `finally`; anchored refusal/inspection finally can release a temporary final reference without an issued result. Package-pin construction rollback returns its credit before backing release. Plan cleanup also reaches EDI capture. | **Complete cleanup/fault/publication profile: NO-GO.** Preinstall ownership and protected peak provision, preserve the original outcome and bounded cleanup facts, and retain irreversible progress. A retained exception reference does not bound new capture/native fault work. |
| Actual producer/context return | `PreparedScene` sets `_supplierProducersUnjoined` before planner invocation and never clears it. Stock Task/ValueTask suppliers, I/O and flowing completion wrappers remain. The three ordinary driver parts and one proposed Assets slot prove neither enclosing runtime/context return nor transitive last access. | **Actual producer/context return and normal reusable return lane: NO-GO.** Require the real enclosing invoker's last-access/transfer proof for the claimed target. No observer chain, `UnsafeOnCompleted`/flow suppression, or permanent quarantine of normal success substitutes for that lane. |
| Physical close | `AssetPackageBacking` keeps its stream through `Dispose` and retains failure; issued handles record lower release progress. `AssetPackageReader.OpenAsync` creates the real read-only asynchronous `FileStream`. No reviewed complete nonthrowing FileStream/SafeFileHandle/native closure or ownership-preserving lower acknowledgment is supplied here; no reachable valid close failure was observed. | **Physical-close qualification: NO-GO.** Retaining a stream object is not proof of retained uncertain OS ownership. Reconsider only with the missing lower proof/capability or a supported real stimulus; never infer success from a second Dispose or repeat an unchanged blocked probe. |
| Other callers and whole preparation | Public Assets pre-lease recovery, package opening/bootstrap, Persistence/measurement/restore, Rendering and Brotli remain outside the selected caller-rooted slice. Ordinary planner construction/cache/workspace and whole preparation acceptance also remain open. | **Unselected, not repaired by descriptions.** No new Store pool or borrowed Scene grant. A future complete child profile would still not enable whole Scene preparation or establish M03/M05/native/AOT/public-consumer acceptance. Independent dependency-ready M05 work can continue. |

## Prerequisites and bounded next action

The next bounded batch is the **None allocation-site catalogue and final compile-only child-slot/control shape**, using the existing planner/read/release algorithms behind the unchanged guard. Factory restriction metadata, exact Store/root-lease preflight, described root/Prefab load dispatch and automatic target None checks are now implemented; do not repeat that batch or impose its restriction on public reads.

1. Close the site set above through real reader constructors, scanner/collection growth, reference inspection, actual I/O/runtime helpers, profile-refusal faults, construction unwind and cleanup/publication overlap. Record full type/capacity charges, pre-read count inputs, issuer, retained partition and actual end/transfer for every term. Missing terms leave the profile unavailable; no placeholder Ready entry, object-size multiplier or guessed constant.
2. Make only the subordinate slot/construction/installation boundary concrete, reusing current metadata-only Store/root-lease preflight and None checks. Include final slot/control fields and disjoint fixed/temporary/retained/protected provision before numerical review. Preserve ordinary public Assets behavior, exception/cancellation precedence, exact-package binding and the existing lower receiver mechanism. Future production Scene dispatch needs the installed/funded owner before the described facade or temporary reference acquisition and no-preparer refusal before the first root load. No preliminary package pin or second preparation carrier/use is needed. A compile-only shape does not authorize constructing or executing the guarded preparation.
3. Before any live admission, close **each** operation-profile, result-partition, installation, cleanup/fault/publication, actual-return and physical-close gate in the table. A genuinely admitted profile is required before capacity/slot reuse can be observed. Do not forge a profile, producer, return acknowledgment, allocation failure or native-close stimulus.

## Evidence and remaining execution gates

The implementation delta at `295e96ee7ee412fef466249fbc0eef99c103be8d` was built and its focused descriptor/preflight selections were run in Debug and Release. These results validate description/error semantics and compilation, not execution through `PreparedScene` or qualification of the proposed child controls:

| Supplied selection | Debug | Release |
|---|---|---|
| Full Assets suite | 46/46 passed | 46/46 passed |
| Focused Runtime Scene registration/descriptor selection | 16/16 passed | 16/16 passed |

No Scene preparation or GPU submission was run. The broader partial-class Scene selection failed at the deliberate null-footprint guard; it is not GPU evidence. This proposal text update runs no tests; its source/test delta is the separately tested `295e96ee` commit. Historical producer IL, managed helper tests and the dirty retained-asset consumer do not supply current return/native/AOT qualification.

Chapter 35's M03-B ownership prerequisites and M05 World-bound extraction/renderer consumer gates remain distinct: the latter require their real admitted Scene/World producers and actual native submission/completion observations. This proposal supplies neither. Independent dependency-ready M05 work can continue under that chapter's sequencing; the None checks do not unblock dependent execution across these gates.

## Post-freeze implementation delta

Captured HEAD `295e96ee7ee412fef466249fbc0eef99c103be8d`. The candidate preflight/read wiring changes these nine direct inputs from the original decision freeze; their current raw-byte SHA-256 values are:

| Path | SHA-256 |
|---|---|
| `sources\TerraFX.Assets\AssetLease.cs` | `cddee36ba0c515a5bd2a0155aa5b5812ea9762cf50919c602bf4775e5c39f18a` |
| `sources\TerraFX.Assets\AssetLeaseLifetime.cs` | `c28c48aa9f1423285e447c8aa2b70e907aa1fb3bec5f1f48352bb8370c26d19f` |
| `sources\TerraFX.Assets\AssetPackageBacking.cs` | `58c37151e3b8aecc88ac9630d4ecd1ea425c34e059e7fd5be26963450657e760` |
| `sources\TerraFX.Assets\AssetPackageLease.cs` | `cc5657bb7135da606e16c9ef725f7b4cc779b434d83ee2e42f83be71198be20d` |
| `sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs` | `ba6d607e72562f86b17dd71afd18c4169281716a6bb7490f3c73c1c06c6036e3` |
| `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `8a822c991792d18bbcf3d58718c7c420833c45a261f406487c7a6dd2e2ef3df0` |
| `sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs` | `903d4abeba40aa4a06c8a1f76d32b0aa27db0affa7690e235804d58baebbd9a6` |
| `sources\TerraFX.Runtime\Scenes\SceneSupport.cs` | `fbcdc763f47a83518e05f10062dbdab371d28aa59a4fb7f9d84083334bdb0778` |
| `tests\TerraFX.Assets.Tests\AssetStoreTests.cs` | `c82cfdce9710025a56bef7e05a604f84eb566ef77b7d2b8855741f8f5718c36d` |

This delta updates implementation state only. It does not re-review or accept the tentative owner choices, close the child operation slot, or qualify the described-load path. The earlier complete input freeze below remains the basis for the original pre-read and owner policy; re-freeze all inputs before any new disposition.

## Original decision freeze

Captured UTC `2026-11-18T17:23:05.4451189Z`; HEAD `8c607f9bc09277518372c692acb5b1c574abea1c`. All 92 listed inputs were rehashed for this reassessment and checked before and after the edit; recheck them before disposition. Changed bytes require reassessment.

`C` means tracked and clean at capture; `D` means a pre-existing tracked modification. Dirty maintained chapters and the retained-asset test are inspected context, not accepted amendments or passing execution evidence. No untracked path is in this bounded evidence scope. Other original dirty/untracked paths, including Rendering/shader work, are preservation-only checks and remain unchanged. No schema or fixture version is changed.

Each entry gives its exact relative Windows path and SHA-256 of raw file bytes. The union digest is `a70c3d5bc05ea030e7ab76491bc53989da0497d1d520463d79e112dea471aaab`: SHA-256 of UTF-8 without BOM/trailing newline, over ordinal-path-sorted `path<TAB>hash` lines joined with LF, omitting the state marker. This bounded source/document freeze is not a transitive runtime/binary attestation. This proposal is the sole edited output, excluded from its own input digest; its pre-edit SHA-256 at the stated HEAD was `76ed33f10cad9165a969c3680bfcffacd3675623144324b5660457d4bab4d5d8`.

```text
C .github\copilot-instructions.md 12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e
C .github\workflows\ci.yml 03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58
C Directory.Build.props 770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a
C Directory.Build.targets aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105
C docs\design\02-dotnet-api-memory-and-ownership.md 40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291
D docs\design\04-capacity-and-workload-profiles.md a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245
C docs\design\11-assets-import-streaming-and-packages.md de1032a04217b2a08933332aee7db22df849cbfc069e76cf6b2263bf469df109
D docs\design\12-entities-components-and-queries.md 174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b
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
C sources\TerraFX.Assets\AssetCodecRegistrationReceipt.cs 99ffe7cfdea63d66d5da04d6c6408b3ff62aad8974046c9803c322df31987fc5
C sources\TerraFX.Assets\AssetEnvelopeDecoder.cs a2b496af751b34c20a89d689f69a83277d3cebe961511676b69df9d0d4e8fb6f
C sources\TerraFX.Assets\AssetLease.cs 9f39981151348bc680fda81347312c7b79bbf7db8d3463cae352dba203f74bc4
C sources\TerraFX.Assets\AssetLeaseLifetime.cs 6dacaa57d9936312bc8e90e85e6cd2b447dc0cf8e8679fe0d68550ce89bf184a
C sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs f05ea48d34c98156f8db5da45c4436da9e9d00cfbeec40b4939c832b717d903e
C sources\TerraFX.Assets\AssetPackageBacking.cs a81a26699cd8dff3d442745f3afad9e313349e771da0f95febfe2dbf9d0cf5c9
C sources\TerraFX.Assets\AssetPackageLease.cs 7f0c6c574ee691f120927f804ec5991defc3885f94c4d13ff3e78dbf0dcf3528
C sources\TerraFX.Assets\AssetPackagePayloadCodec.cs e3cb970cd84baff5d350e768f8c97f0d3b696fddc9a5bfe3a472503011dc628c
C sources\TerraFX.Assets\AssetPackageReader.cs 06f12e7b2d7d89a44a92e28f41a8581af3a64f963df0dbdd935da2722984b4fd
C sources\TerraFX.Assets\AssetPayloadReader.cs 1bdf040211fdaad0a1347ce9c1d8a478d65743ed543f362bf60d1e16159b5d03
C sources\TerraFX.Assets\AssetReferenceScanner.cs ef0232bcc4f384fe6025a8f98cb11d7c6e70033ff36f31e796994a314ae67bbf
C sources\TerraFX.Assets\AssetStore.cs 9f51cbfd4d0d6da21b088c5fed6765fc4a8b2879210ce7e5d2d3913b44d276a3
C sources\TerraFX.Assets\AssetStore.PackageLease.cs 4b3f854a32824ae24f8051ef21886df78fcb561df00fb0a345b950dd90cc092a
C sources\TerraFX.Assets\AssetStore.PackagePin.cs 270dabda38f2474b7d48b02a230f1492e2a3bcfcc6963a8ad1bd102ca6ceca53
C sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs 86526332fb1ce6fff75e623c57830d651f2ece8a782f49f7940d1941b5ac67dc
C sources\TerraFX.Assets\AssetStoreOptions.cs bb2123297d3e65309bd38ce5da0083e14319810139fa233ca0b0ebc4e5404594
C sources\TerraFX.Assets\AssetStoreRegistration.cs 48d70b4c2d7fef5f4cdf12766688ebda6a661024e74d5eb7c3df3adb1a1dc62b
C sources\TerraFX.Assets\AssetStoreRegistrationBuilder.cs d1789c3cd71d8356e76d264c043c78d5938fa9e738cd5a686111e587b5e51b07
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
C sources\TerraFX.Runtime\Scenes\SceneSupport.cs 3b8d0884550f52d7f894e6ba7f5215e6da5989010a632cd1ca3241004b04200e
C tests\Directory.Build.props 281aad4237121feab1903c2384f1a5f0aed7e218a704c918fe953e9fc8e75764
C tests\TerraFX.Assets.Tests\AssetStoreTests.cs fa6de6a3379866c7560d6609fd5d597448bf7cc4803caa443b247f6551b1a685
D tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4
C tests\TerraFX.Runtime.Tests\Scenes\ScenePrefabPackageLeaseTests.cs 0b831e5ba489149f1c553855f9d6f1825a627403a8912f69408892113f00d7ec
C tests\TerraFX.Runtime.Tests\Scenes\SceneRestorePreparationGateTests.cs 3491589ec2719c488d1ec62601ab724a10aa36206b57a56a83a137ca295b5482
C tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs d0c2f403cfd67c4937b0e21a9a27a814eaac3f85fb178bb26eb3f4819d282421
```
