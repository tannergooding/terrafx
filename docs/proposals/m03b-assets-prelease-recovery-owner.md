# M03-B pre-lease Assets recovery ownership

**Status: tentative proposal for review, not an implemented owner or qualification. Keep `PreparedScene.QualifiedFootprint` null.**

This adds the missing pre-work owner to the [Assets release rectification](m03b-scene-producer-return-and-assets-release-rectification.md#assets-owned-release-rectification). It does not duplicate `b662e15d`'s issued-lease state machines or replace the existing loading/decoding algorithms.

## Current boundary

At the frozen source below:

- `CreateLease` constructs a lifetime before its `try`, and reports `ownershipTransferred = true` even when construction cleanup throws.
- `AcquirePackageLease` reserves a backing reference/lease credit before constructing its handle; rollback returns the credit before backing release.
- Anchored loading acquires a temporary backing reference before store admission. Admission failure and `LoadAssetAsync`'s `finally` can release it without an issued handle retaining the inputs. Package-reference inspection has the same temporary-reference boundary.
- The Store's serialized replacement slot and asynchronous disposal join cover Store-owner retirement, not these independently admitted load/inspection/construction obligations.
- The planner stages the root Scene lease only after `LoadAsync` returns; Prefab and Rendering dependency loads likewise need an owner before their current awaits.

Distinguishing schedule, not an executed failure: an anchored call acquires its temporary reference; another caller disposes the source lease and Store; admission then rejects and the temporary reference becomes the last backing reference. No new handle exists, and retaining the now-disposed source handle does not recover the backing. The proposed preinstalled operation retains that exact reference and its zero-or-admitted credit dispositions. A failure of its final close remains conditional and unobserved.

## Selected owner and capacity boundary

Select **caller-rooted, Assets-owned operation controls for the ordinary M03-B Scene path**. Proposed internal boundary types are `AssetLoadOperation<T>`, `AssetPackagePinOperation`, and `AssetReferenceInspectionOperation`; they own progress for the existing producers, not a second loader, scheduler, generic async builder, or retry service. Runtime uses their opaque ownership/acknowledgment contract, never their private counts, backing, or lease state.

The issuers are distinct:

| Obligation | Issuer and independent root |
|---|---|
| Control construction, invocation/return machinery and protected cleanup/publication | The **original** `PreparedScene.PreparationControl` and its existing `P` purse. Root: original domain/use anchor → `PreparedScene` → installed operation, or an already rooted planner/bundle/helper owning that operation. |
| Backing reference, live-lease credit and decode/decoded-value charges | The exact originating `AssetStore`; acquired into the installed operation under Assets-owned admission. These are not replacements for the control's byte provision. |
| Successful issued result | The existing issued lease/lifetime, then its actual Scene/Rendering owner after explicit installation acknowledgment. |
| Failed cleanup | The same operation and original claim/root, including across Store/World/domain close. No successor use or new cleanup allowance is acquired. |

Before calling a control factory, the caller must preclaim its complete `C_op`: whole carriers, cold gates, real capacities, read/decode progress, Task/context/registration suffixes, installation/return parts, and protected fault/cancellation/cleanup peaks. Include retained value/backing storage without independently established Assets provision; wire-length counters do not establish its actual managed footprint. Do not double-charge storage whose real independent grant remains live. Claims are disjoint portions of original `X`; fixed producer/publication provision is not spendable scratch. The sum of live and quarantined claims must fit the original purse. No Store-wide collection or unbounded recovery queue is introduced.

**No numerical `C_op` profile is established here.** Missing derivation or insufficient capacity refuses before control construction; failed attachment refuses before backing acquisition, decode, or lease construction. A control factory only creates an empty managed owner and acquires no external resource. Assets receives no Runtime funding callback or Runtime capability; Runtime retains and returns its own claim only after the opaque owner has genuinely ended.

This selects no independent Store-wide recovery pool. Covering arbitrary existing public Assets callers without a caller-held grant/root requires a separate capacity decision: explicit recovery control/byte capacity, its construction issuer, and an external anchor retained after failed shutdown. Neither `MaximumLiveAssetLeases` nor decoded-wire-byte limits establish that provision. Do not invent a default, steal caller-visible lease credits, or treat a `Store ↔ record` cycle as a root. The Scene-owned route must not be presented as fixing those general public entrypoints.

Selected operations use an already admitted Store and open no package files. `CreateAsync`/candidate `OpenAsync` need their own bootstrap owner before `FileStream` construction; this proposal supplies neither that issuer nor permission to open through a Scene load control. Persistence package-pin/inspection and shared measurement/restore similarly require their actual original Persistence seed/root, never borrowed ordinary Scene `P`. Those callers remain unselected until that construction/capacity decision closes.

## Installation, states and transfer

1. Preclaim the factory/call envelope through the original issuer. Construct the empty control, then install it in the existing preparation pending/phase mechanism or the actual rooted helper's preclaimed operation field **before** invoking its producer. Construction/attachment failure has acquired no backing/lease/decode obligation.
2. The control retains its Store, source facade, identity/reference/minor/token, exact entry/codec, owned-credit amounts, backing, actual read/decode progress, decoded value, lifetime/handle under construction, original outcome and bounded cleanup facts. Source binding retains the public source facade, not an unprotected private `LeaseState` or decoded-value borrow. Source disposal before acquisition therefore rejects without manufacturing ownership.
3. Under the existing Assets gates, acquire each reference/credit and attach its exact disposition to the already installed control before another fallible step or asynchronous escape. No physical cleanup, callback, wait, or publication runs under those gates.

| State | Ownership and allowed next transition |
|---|---|
| `Installed` | No lower obligation yet; one producer election may enter `Acquiring`. |
| `Acquiring` / `Producing` | Every acquired obligation and materialized intermediate is attached; success enters `ResultStaged`, fault/cancellation requests `Cleaning` after actual consumers stop. |
| `ResultStaged` | Exact issued handle, or inspection result, remains owned by the control; Task readiness does not transfer it. |
| `InstallingResult` | One preclaimed installation interest retains control and exact destination/result while close is fenced. Stage the destination, acknowledge that exact transfer through Assets, then finish installation. Neither side cleans the child during this interval. A failed installation retains the original interest/inputs, not an assumed rollback. |
| `Transferred` / `Cleaning` | Child authority has transferred once, or owned obligations are released once. Separate invocation/return obligations remain. |
| `Ended` | Every owned obligation ended/transferred **and** actual return obligations joined; only now may the original claim/root be returned. |
| `FailedRetained` | Preserve remaining debt, original failure, inputs and irreversible progress. Repeated close reports retained failure without another lower release. |

The installation fence is an extension of the existing parent owner transition, not an Assets-to-Runtime callback. Its complete carrier/overlap belongs in `C_op`. An acknowledgment cannot be inferred merely because a destination field was once assigned; cancellation and installation must share the same election.

## Existing lanes and cleanup ordering

| Lane | Required ownership change |
|---|---|
| `CreateLease` | The operation already owns value/backing/credits. Attach the newly constructed lifetime before constructing the public handle. Commit responsibility to that exact lifetime once; if handle construction fails, retain value plus lifetime and invoke its existing release protocol once. Never infer transfer in a `finally`. |
| Package-pin construction | Attach backing reference and live-lease credit before `new AssetPackageLease`. Until a complete handle is attached, the operation owns rollback. |
| Anchored load admission | Install the operation before source reference acquisition. Record even the temporary reference acquired before an admission refusal; do not pass an unowned backing tuple into an async method. |
| Load/reference inspection | Carry the same owner through payload reads, Brotli input/overflow/decoder progress, codec output and sequential reference validation. Root each intermediate before the next allocation. Preserve actual None/Brotli behavior; no comparison implementation or test producer. |

After actual consumers stop:

1. End scratch/read/decoder obligations and release their independent decode credits only when their storage/access has ended or validly transferred. Attempt independently safe cleanup even after a sibling failure.
2. For operation-owned references, prevalidate the store commit, release the backing reference once, record the acknowledgment, then commit the associated lease/decoded charges. Nonfinal release acknowledges responsibility held by remaining real references, not physical file closure.
3. If a lifetime/issued handle already accepted those credits, use its existing release state machine instead; never also run `CompleteLoad` rollback for them.
4. A backing failure retains that backing and unreturned charges. A later accounting failure retains the Store, still-required value/inputs, and accounting debt **with backing-success recorded**; it never reacquires or closes again. Clear only obligations actually ended.
5. Preserve the primary exception/selected cancellation token and bounded original cleanup facts. Do not allocate recovery records or an exception list in cleanup. A cleanup failure is not successful cancellation or rollback.

The provenance getters need no new temporary owner only if their complete valid nonfinal-release closure is proved from the enclosing issued-handle gate. The same proof obligation applies to retained-handle construction rollback; do not assume every temporary reference can be final, or every rollback nonthrowing.

## Implementation and evidence gates

The bounded next batch is the empty controls, original-issuer preclaim/installation seam, and actual producer progress plumbing through the lanes above. Keep the existing Scene guard and settlement refusals. Do not silently route a general public call through an unfunded control or describe these proposed types/claims as already present.

Future managed checks use real packages: exact/short original claims through the real lower World owner; success and capacity reclamation once; malformed envelope/Brotli/reference rejection; pre-cancellation and cancellation by an actual registered codec; source/store close and replacement during admitted work; and exact-result installation/close ordering. Preserve the existing package-identity and pixel-17 consumers. Use owning Assets/Runtime NUnit selections, Debug before Release, fresh nonzero membership, and ordinary TRX output. No guarded Scene preparation is an allocation experiment.

| Unclosed gate | Condition for reconsideration |
|---|---|
| Lease/lifetime constructor failure | No deterministic valid allocation-failure stimulus is established. Review the real unwind/progress statically; execute only when a supported real stimulus exists. No injected allocator, private-count corruption, or artificial constructor failure. |
| FileStream/SafeFileHandle/native close | Retaining a stream object does not preserve an OS handle already severed below it. Require a proved complete valid nonthrowing closure or a lower ownership-preserving acknowledgment. Repeat a blocked observation only when that capability or a supported real failure stimulus becomes available; never retry uncertain close. |
| Producer/native return and fault capture | The existing Task/ValueTask/context/native suffix and finite fault-capture gaps remain. Outcome readiness, a new control, or another callback is not actual return. No normal-path permanent quarantine substitutes for a missing return lane: that lane remains unavailable. |
| Capacity/acceptance | Derive the final `C_op` catalogue and original `P` partitions before admission. Independent service-owned recovery still requires the separate capacity/issuer decision above. Managed tests do not establish native/AOT/public-consumer or Chapter 35 acceptance. |

## Frozen review inputs

UTC `2026-11-18T15:36:55.0650854Z`; HEAD `a2a7fecb97eff72b6193d5577b63dbfcb28a188b`. This supersedes no historical binary/native freeze. Dirty maintained contracts are context, not accepted amendments. No schema/fixture version is changed or qualified.

Each group hash is SHA-256 of UTF-8, no BOM or trailing newline, over ordinal-sorted lines `relative-Windows-path<TAB>lowercase-raw-file-SHA256`, joined with LF. `ALL` uses the union. The closed input sets are:

```text
A: sources\TerraFX.Assets\
AssetLease.cs, AssetLeaseLifetime.cs, AssetPackageLease.cs, AssetPackageBacking.cs,
AssetStore.cs, AssetStore.PackageLease.cs, AssetStore.PackagePin.cs,
AssetPackageReader.cs, AssetPayloadReader.cs, AssetStoreOptions.cs, TerraFX.Assets.csproj

R: sources\TerraFX.Runtime\
Scenes\PreparedScene.cs, Scenes\PreparedScene.Preparation.cs,
Scenes\ScenePreparationFunding.cs, Scenes\ScenePrefabExpansionPlanner.cs,
Scenes\SceneExpansionPlan.cs, Scenes\PreparedAssetsLifetime.cs,
Scenes\SceneExpansionLimits.cs, Scenes\ISceneExpansionResourcePreparer.cs,
Rendering\RenderSceneAssetBundle.cs, Rendering\RenderAtlasPageLoader.cs,
Rendering\RenderMaterialTextureLoader.cs, Rendering\Renderer.CpuExtractionSupport.cs,
Entities\WorldGuidUseControl.cs, Entities\WorldSceneStorageReservation.cs

T: tests\
TerraFX.Assets.Tests\AssetStoreTests.cs, TerraFX.Assets.Tests\AssetPackageLeaseTests.cs,
TerraFX.Assets.Tests\AssetPackageReaderTests.cs,
TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs,
TerraFX.Runtime.Tests\Scenes\PreparedAssetsLifetimeTests.cs,
TerraFX.Runtime.Tests\Scenes\RenderAtlasAssetCodecTests.cs,
TerraFX.Runtime.Tests\Scenes\RenderMaterialAssetCodecTests.cs,
TerraFX.Runtime.Tests\Entities\WorldSceneReceiverTests.cs

C: repository root
.github\copilot-instructions.md, .github\workflows\ci.yml,
Directory.Build.props, Directory.Build.targets, global.json,
sources\Directory.Build.props, tests\Directory.Build.props,
docs\project\Building.md, docs\project\Roadmap.md,
docs\design\02-dotnet-api-memory-and-ownership.md,
docs\design\04-capacity-and-workload-profiles.md,
docs\design\11-assets-import-streaming-and-packages.md,
docs\design\16-scenes-transforms-and-cameras.md,
docs\design\29-project-setup-build-and-deployment.md,
docs\design\30-public-api-review-surface.md,
docs\design\35-implementation-roadmap.md,
docs\proposals\m03b-scene-producer-return-and-assets-release-rectification.md
```

| Set | Files | SHA-256 |
|---|---:|---|
| A | 11 | `c67e76d4173ddf7b782caebec32312e8b7a6a453ba72f08cf03f4a021389c884` |
| R | 14 | `8b2fe9f9ec2f4d7c7be16b65211c6297f083d53634ac687e906c758808fc3982` |
| T | 8 | `9501bcd85d6e0d81739ac3fc0350a10478571db765fe4d529b8819e21a465d68` |
| C | 17 | `ea674dd39c428236bf71cdd826c9460008911baa90e54901052f6bfa4c96bad7` |
| ALL | 50 | `ec28c815b4e478ed0ba714a2ca46883bdffaf54dcf9f04b3482d3f77f215748e` |

The four dirty input hashes are respectively: Chapter 04 `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245`; Chapter 16 `b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a`; Chapter 30 `15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b`; `PreparedSceneAssetRetentionTests.cs` `e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4`.

Recheck HEAD, membership, dirty state and hashes before applying a disposition; changed inputs require reassessment. This is a bounded proposal-input identity, not a transitive build/runtime attestation. Only this proposal was authored; no implementation, build/test result, physical-failure observation, commit or publication is claimed.
