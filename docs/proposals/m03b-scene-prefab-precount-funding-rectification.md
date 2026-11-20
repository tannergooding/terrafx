# M03-B Scene/Prefab pre-count funding rectification

**Status: tentative implementation guidance, not an accepted contract, allocation profile, or execution authorization. `PreparedScene.QualifiedFootprint` remains null; all Scene, resource, package, and native admission guards remain closed.**

## Decision and scope

Charge the cache's construction and each miss's complete acquisition/retention prefix to the **original `PreparedScene` preparation grant before that work**, not to the reservation calculated after traversal. Preserve the same cache and exact package-backed leases across counting and building.

The candidate is field-free **Scene bookkeeping**: use the existing producer-claim scalar, phase/pending-owner slots, construction election, and dictionary `Count`, without additional Scene fields, per-entry claim objects, public API, budget, World use, or package generation. Whether those slots suffice for the complete acquisition/result/return overlap remains a gate, not a proved implementation. Added locals, delegates, async lowering, and a future lower Assets owner still require whole-layout funding.

**An executable, all-outcome field-free route is not proved by the frozen inputs.** Missing are a qualified cache-prefix charge, an original-grant-funded exact-backing read/acquisition/installation boundary, exact receiver settlement, and actual return/cleanup closure. Existing described-read wrappers do not provide those capabilities. The sequence below is conditional on closing them; an unavailable term means refusal, not zero.

No **cache-specific contract decision** is required. Chapters 04, 16, and 30 already require reserve-before-allocation and explicitly put pre-count/cache work inside W. A genuine separate decision would be required if closure instead changes cancellation/context behavior, public failure ownership/precedence, or package selection. This proposal selects none of those changes. In particular, a finite owner for retained caller/fault graphs is still missing; if it cannot be supplied under existing semantics, that ownership/error-contract decision must precede admission.

Scope is the original-use Scene preparation path with the existing built-in Scene/Prefab **2.0**, package **None** selection. This does not admit the ownerless measurement/package-lease overloads, restore, Rendering resource preparation, other codecs, or the separate bounded tool lane. The two consulted earlier proposals remain tentative context, not authority or inherited qualification.

## Frozen source evidence

Scene filenames below are under `sources\TerraFX.Runtime\Scenes`; Assets and Entities filenames use their corresponding owning folders.

| Source | Current behavior and consequence |
|---|---|
| `ScenePrefabExpansionPlanner.cs:254-319` | The actual nested type is `SceneExpansionBuilder`. `_prefabLeases = []` is a field initializer, executed during constructor construction before the caller stages the builder. The inline AssetId path avoids path-array growth, but does not fund the dictionary. |
| `ScenePrefabExpansionPlanner.cs:895-908, 1441-1472, 1718, 2223-2275` | `BuildAsync` first awaits `CountAndMeasureAsync`. Counting loads Prefabs and traverses their retained values before calculating required bytes; only afterward does Build invoke `ReserveWorkspace`/`ReserveStorage`. The formula's `256 * prefabLeases.Count` and final `2048` are estimates, not earlier ownership claims. Reservation does precede the subsequent fill/build/validation allocations; this is not a claim that all reservation follows validation. |
| `ScenePrefabExpansionPlanner.cs:2179-2221, 2330-2351, 1020-1048` | A cache hit returns the retained value without a new load. A miss loads through the root Scene lease's exact backing, with a described read on the owned path, then checks context and inserts the lease. Before insertion the lease is local; unsuccessful insertion/checks dispose it in `finally`. The same dictionary and Scene lease move into the plan. |
| `PreparedScene.cs:88-149`; `PreparedScene.Preparation.cs:129-267, 879-905`; `SceneLoader.cs:236-269` | The null footprint guard prevents public candidate execution. Scalar producer claims and pending-owner construction exist, but planner/cache sites do not use them. The planner reservation callbacks request workspace through the ordinary World reservation path. `_supplierProducersUnjoined` remains set; child Task readiness cannot authorize handback. |
| Assets `AssetLease.cs:140-147`, `AssetLeaseLifetime.cs:77-92`, `AssetStore.ReadDescriptor.cs:59-104`, `AssetStore.PackageLease.cs:127-213`, `AssetPayloadReader.cs:16-27` | An anchored facade acquires a temporary backing reference before typed admission. None still allocates a payload and executes the async read/decode/reference-inspection chain. Store lease/wire/decode credits do not measure the codec's managed graph or the caller's cache. Failure before an issued lease can still own a backing reference. |
| Entities `World.ScenePreclaimedWorkspace.cs`, `WorldAdmissionDomain.ScenePreclaimedWorkspace.cs`, `WorldAdmissionDomain.SceneStorage.cs`; `PreparedScene.Preparation.cs:176-250` | A real same-use preclaimed receiver mechanism exists. The higher helper currently requires the expected **pending** owner, total outstanding producer claims equal to retained data, and its one-shot construction election. A phase-owned builder with live temporary/return claims does not satisfy that contract. A callback substitution alone cannot settle the cache. |
| `SceneExpansionPlan.cs:285-369`; `SceneInstance.cs:411-416`; Assets `AssetLeaseLifetime.cs:163-233`, `AssetPackageBacking.cs:300-401` | Instance revocation disposes its plan; plan cleanup attempts retained leases before returning storage. Failed backing release and failed Store accounting are distinct states. These source paths and their early-return latches are not actual-return or native-close evidence. |

Contract owners are `docs\design\04-capacity-and-workload-profiles.md` sections 1, 2, and 4; Chapter 11 section 3; Chapter 16 section 2; Chapter 27's admitted-consumer and cleanup rules; Chapter 29's Entities ownership/layering rules; Chapter 30's Scene/Assets contracts; and Chapter 35's M03-B and capability/observation sequencing. W is the exact requested `MaximumPreparationBytes`; original P here means that original W workspace grant, not the process registry's P credits. `256 + W` remains the independent-use charge. Cache/read subclaims partition that existing workspace, not an additional quota. Assets-owned physical package backing stays charged at Assets; Scene-owned containers, managed outputs, and operation storage cannot be excluded merely because Assets charges wire bytes.

### Distinguishing schedule, not an executed fixture

Use Chapter 16's existing A/P/Q repeated-placement example. Both P and Q select B, so the cache retains one B lease while expansion records distinct occurrences. Empty Prefabs still require cache/lease ownership; entity count is not cache cardinality.

If package replacement occurs between count and build, the existing cache continues using the exact retained old backing. Replacing it with `AssetStore.LoadAsync` could select the new generation. Reloading through the original Scene lease would preserve backing identity, but would perform another read/decode and consume new credits; Store disposal between passes would refuse that new load while an existing cache hit remains usable. Therefore neither blind eviction nor anchored reload closes the funding dependency.

## Owner, claim, and end semantics

| Owned interval | Claim and real owner | Permitted end |
|---|---|---|
| Supplier invocation and builder construction, including field initializers and failed construction | Original `PreparedScene`, reachable through its issued `WorldGuidUseControl`; existing producer provision and construction election | Actual enclosing construction/invocation return or unwind, with no surviving child obligations; unproved or failed unwind stays anchored and charged |
| Dictionary shell and cumulative capacity/growth peaks | Builder's existing `_prefabLeases`; original-root scalar subclaims made before construction/miss | Exact surviving cache ownership transfers with the same dictionary, or all cache consumers and cleanup actually end; counting/measurement completion is not an end |
| Each miss: description failure prefix, temporary backing reference, decode/inspection, issue, value checks, insertion, and return | Original-root provision plus the required Assets-owned acquisition/read boundary; existing pending slot protects the outstanding owner/result while the builder remains phase owner | Unambiguous cache installation plus actual lower/caller return; failed or uncertain installation retains the owner and charge |
| Surviving Prefab value and lease carriers | Cache, then the same `SceneExpansionPlan` and actual instance owner | Exact data/control transfer to the retained receiver, then its actual final consumer/lease/storage release |
| Temporary, publication, fault, cancellation, cleanup, and return storage | Existing preparation partitions and their actual lower lifetime owners | Required consumers, registration/dispatch/context suffixes, and accounting return all end; visibility, cancellation, cleared fields, and GC are not substitutes |

Let `CachePrefix(n)` denote a **monotone, qualified charge function**, not a new budget. It must cover constructor/comparer work and charged bucket/entry capacities, including old-plus-new growth at every allocation-reaching prefix. Keep its temporary/return terms distinct from the exact retained cache.

`SceneHierarchyValidatorTests` contains shell and growth tests using `80 + 256*n` for the closed dictionary type through 65,536 insertions. After the input freeze, a targeted Runtime selection passed **4/4 in Debug and Release**, including both cache cases. The shallow shell assertion is 80 B; the growth test passes the cumulative-allocation bound with **null lease values**. This confirms only the dictionary carrier/growth candidate. It does not establish cold-state costs, the prescribed `64 + RoundUp16` charged-array ledger, lease/Prefab graphs, `.Values` collection construction during cleanup, faults, or exact surviving capacity. Therefore it is not an accepted `CachePrefix` or retained-data amount.

| Configuration | Runtime assembly (MVID; SHA-256) | Test assembly (MVID; SHA-256) | TRX (path; SHA-256) |
|---|---|---|---|
| Debug | `62d263b1-740a-414e-862d-1465f7ad10c2`; `C68EF8E8C5834D724F38C0325759BCE8EA4352897B5FC46A304CB66D92DCC928` | `3fc8bfa8-25af-4b8d-bd53-29b01931b22a`; `214963112BB3A9D366F886077E34BCD71574877A5FC52DBDE197F49F14221A55` | `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_11_51_19_net11.0.trx`; `D856BBC58B3F5F735C79E6F452EA8149188A3802B012A9F827AFC59481543CAD` |
| Release | `94a49a9f-934f-4705-9aff-fd3cf461c79d`; `F942A806D60D0EF0A837BEC41D929A84444E2802C7E1FABEA92F80BF8A664915` | `10ce38dc-a896-47b7-88a2-2024601053bc`; `D408B459387202594D64317A7BB330CB4F7E8CB982A4002106EDA87FB026EC10` | `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_11_51_25_net11.0.trx`; `D68028AD7C32279503D307974BF15BD012BF9F5878832DDC129DED12E5308FC8` |

These are managed dictionary-shell/growth observations only. They exercised no Scene package, lease value, replacement, Store close, preclaim, receiver transfer, or actual-return path.

| Configuration | Runtime assembly (MVID; SHA-256) | Test assembly (MVID; SHA-256) | TRX (path; SHA-256) |
|---|---|---|---|
| Debug | `62d263b1-740a-414e-862d-1465f7ad10c2`; `C68EF8E8C5834D724F38C0325759BCE8EA4352897B5FC46A304CB66D92DCC928` | `3fc8bfa8-25af-4b8d-bd53-29b01931b22a`; `214963112BB3A9D366F886077E34BCD71574877A5FC52DBDE197F49F14221A55` | `artifacts\tst\Debug\tagoo_GOODING-ZEN4_2026-11-20_11_51_19_net11.0.trx`; `D856BBC58B3F5F735C79E6F452EA8149188A3802B012A9F827AFC59481543CAD` |
| Release | `94a49a9f-934f-4705-9aff-fd3cf461c79d`; `F942A806D60D0EF0A837BEC41D929A84444E2802C7E1FABEA92F80BF8A664915` | `10ce38dc-a896-47b7-88a2-2024601053bc`; `D408B459387202594D64317A7BB330CB4F7E8CB982A4002106EDA87FB026EC10` | `artifacts\tst\Release\tagoo_GOODING-ZEN4_2026-11-20_11_51_25_net11.0.trx`; `D68028AD7C32279503D307974BF15BD012BF9F5878832DDC129DED12E5308FC8` |

These are managed dictionary-shell/growth observations only. They exercised no Scene package, lease value, replacement, Store close, preclaim, receiver transfer, or actual-return path.

## Possible implementation sequence

1. **Close the claim inputs first.** Derive the cache function and complete selected miss `C_op` from the frozen implementation/runtime and exact descriptor. Include invalid/canceled/failed prefixes, not only successful final graphs. Existing live-lease ceilings constrain successful retained handles, not returning controls or arbitrary fault graphs. An absent function refuses before supplier/read invocation.
2. **Preclaim before construction.** At `PreparedScene.StartPlanPreparationCore`, before calling `SceneLoader.StartPlanPreparation` (which allocates captured reservation delegates and invokes the async supplier), use the existing producer-claim mechanism for the complete invocation/construction prefix, including `CachePrefix(0)`. This is within the root's Running producer state, as the claim helper requires. The funded root owns unsuccessful construction even when no builder returns. Do not move the check into the constructor after its field initializers.
3. **Preclaim each miss without per-entry fields.** Hits continue borrowing the cached value. For a miss with existing count n, claim the positive `CachePrefix(n+1)-CachePrefix(n)` increment before fallible miss work. Description's refusal/cold prefix must already be funded; then claim the complete read/inspection/issue upper bound before the facade can take a backing reference. Use the exact root Scene lease, backing token, receipt, and immutable descriptor; revalidate ordinary Store admission independently. Count and the root aggregate identify these serialized provisions; no claim node is needed.
4. **Root acquisition and installation.** Keep the builder as phase owner and use the existing vacant pending-owner/construction election for the outstanding child. This requires an actual lower owner installed before acquisition; today's thin Task wrappers are not that owner. Stage an issued lease before `Value`/context checks or dictionary insertion. After the dictionary owns that exact lease and its retained charge, `AcknowledgePendingOwnerTransfer` supplies the existing destination election. It does not prove lower producer return. If the required acquisition/result/return overlap cannot be represented by the existing slots and a real lower owner, stop: the missing capability is that rooted handoff, not permission to add uncharged fields or clear a live pending owner.
5. **Make post-count use cache-only.** Prove the exact pre-count acquisition membership covers fill, expansion, RootPose/override selector traversal, and `GetSource`. Route the selected post-count call graph through retained lookup; a missing entry is a failed invariant, not permission to reload or query the current Store. Use call structure rather than a new phase flag. Preserve authored order, repeated occurrence identity, depth/cycle checks, and exact backing. Keep cache claims live across the pass boundary.
6. **Settle actual ownership, not the count heuristic.** Preclaim subsequent build allocations from the same original provision. Derive exact surviving data/control ownership, including cache capacities and retained semantic/lease graphs; `256*n` cannot substitute for it. Use the Entities preclaimed receiver transaction without refund/reacquire or double charging. The existing higher helper needs a proved phase-specific election/partition treatment: temporary and protected return claims cannot be passed as retained data, and its pending-owner/equality/one-shot checks cannot simply be removed or reused for both plan and resource receivers. Until that field-free settlement is proved, retain the original claim and refuse plan admission.
7. **End only after actual returns.** Preserve the same cache through plan/instance transfer. Return unused/temporary provision only after its real consumers and enclosing callers stop; retain return/fault debt separately. Keep `_supplierProducersUnjoined` and publication/resource guards closed until the actual supplier/registration/context-return set is satisfied. Lower World/domain retirement fences do not discharge Scene or Assets work.

These steps change neither public caps nor Assets quotas. They also do not provide the still-unqualified preparation root bootstrap, root-description failure ordering, Rendering receiver, or whole-generation native I/O profile. Implementation guidance is not evidence that any step is currently callable.

## Failure and return rules

- Unknown profile, insufficient original provision, occupied/closed ownership election, or failed exact-backing admission refuses before the next allocation/acquisition. Do not wait unboundedly, reduce W, fall back to ownerless/ordinary loading, or report an empty successful Scene.
- Preserve existing usage/pre-cancellation ordering and admitted Task fault/cancellation channels. Stop new misses on cancellation, but join accepted consumers before releasing their destinations/backing. Changing externally retained token/context/exception ownership requires its own reviewed boundary.
- Before successful insertion, the outstanding lease remains pending/lower-owned through validation and cleanup. After insertion, transfer only that exact ownership; uncertain acknowledgment keeps both relevant roots until disposition. Attempt independently safe cleanup outside gates, preserve the original execution failure and existing bounded cleanup sampling, and do not allocate recovery lists.
- Record irreversible backing release before Store accounting. Accounting failure after consumption retains accounting debt, not authority to close again. Failed/indeterminate release, registration return, installation, or claim return retains the exact graph and charge in quarantine. A subsequent no-op `Dispose`, Task completion, field clear, or capacity estimate cannot acknowledge release.
- No original-use handback occurs while any cache/read/return claim remains. Receiver transfer never replenishes original P, and no failed sibling's charge is treated as unused provision.

## Remaining evidence gates and next action

All rows remain open; these are existing owner obligations, not new acceptance IDs.

| Gate | Exact evidence needed / condition for retry |
|---|---|
| M03-B original grant and complete `C_op`/J/K | Final target-specific whole-root, constructor/unwind, dictionary capacity/growth, read/inspection, retained-data, cold/helper/cleanup, async/context and fault-retention partitions; every reachable prefix fits original W before work. Existing shell/traffic tests alone are insufficient. |
| Chapter 11 exact-backing acquisition and delivery | A real funded source-bound Assets owner covers the temporary-reference-before-admission window, payload/reference work, issued result staging, insertion failure, and lower return. The original Store/backing lifetime independently funds shared I/O/cache/dispatch and actual final-close disposition. None or a separate synchronous tool owner does not establish this async capability. |
| Chapter 16/30 receiver conservation | Proved phase-specific construction/installation election and exact surviving membership/capacity; temporary/protected claims remain outside retained data. Demonstrate same-use covered transfer and applicable `R-C` admission without weakening pending-owner, equality, one-shot, World, or domain guards. |
| Chapter 27 actual consumer and cleanup end | Genuine enclosing-caller observations for construction, load, count/build, insertion, callback registration/dispatch, context restoration, publication, final lease/storage/accounting return, and failed-cleanup quarantine. Readiness or a self-reported returned bit is insufficient. |
| Retained caller/fault ownership | A real finite, independently funded lifetime/transfer boundary compatible with current token/context/original-failure semantics. If unavailable, a separately reviewed ownership/error-contract decision is required; no all-outcome numerical profile may be asserted meanwhile. |
| Chapter 35 implemented behavior and consumer qualification | Once the above capabilities exist, use ordinary owning-project Debug then Release checks with nonempty executed selections, reviewed literal data, and real producer paths for cache reuse, replacement/Store-close behavior, resize/refusal boundaries, and cleanup. Compiled/allocation evidence, managed behavior, actual package consumers, native/AOT observations, and milestone acceptance remain distinct. Scene-dependent persistence/renderer consumers wait for their named producers; this proposal supplies none. |

**Next action:** close the cache-prefix function and the acquisition-to-cache owner/claim/end projection against these frozen inputs, including whether existing pending/phase slots really suffice. If either cannot be proved, record that exact missing lower capability or separate contract decision; do not implement a late-reservation workaround or retry operational Scene/package/native execution. Only after that review should the field-free front-end/miss/lookup/settlement changes be implemented and profiled with the guards still closed. No code, maintained chapter, Roadmap, session plan, other proposal, fixture, commit, push, or PR is changed by this artifact.

## Frozen raw-input manifest

Freeze completed **2026-11-20T19:38:37.9926473Z**, HEAD **`fe7a4031868e41be01dfb30a31028ac155569db9`**. The input set is these 47 evidence/guidance files plus the five preservation files below. SHA-256 covers raw file bytes without newline/BOM normalization. Paths are repository-relative. Whole-file hashes bind even where only relevant ranges were inspected; prior proposals' embedded historical hashes/results are not adopted.

This is a task-scoped review freeze, not a repository-wide inventory, runtime manifest, new acceptance pipeline, or claim of qualification. The new proposal is the output, not its own input.

```text
342683de2b1c262624546f312f7d8e2078f89185add39f43f1fb5013376fd553  .editorconfig
12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e  .github\copilot-instructions.md
03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58  .github\workflows\ci.yml
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  global.json
770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a  Directory.Build.props
d44373af315523f8af9f09f36bded2a688e2b5c1572fcc0732310c7ef590ead8  sources\Directory.Build.props
2f1c81e54f0fbf12d01e34d0d840885f5eb0e94a8b8890865ef721b8c6a3c65d  sources\TerraFX.Runtime\TerraFX.Runtime.csproj
f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f  docs\project\Building.md
f9598d3f8005ec0e73200f7ec30882914f15f275d238707d1f502f5948015f3e  docs\project\Roadmap.md
60047dd1eacf26196b0bf23f16798e2aa265fd26cd36b9a0b4df1b79135730d7  docs\design\04-capacity-and-workload-profiles.md
de1032a04217b2a08933332aee7db22df849cbfc069e76cf6b2263bf469df109  docs\design\11-assets-import-streaming-and-packages.md
b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a  docs\design\16-scenes-transforms-and-cameras.md
b0bfa5771f1029bdfb21a0e01556467d3169953f71cbfdf18a06381a11cee461  docs\design\27-state-serialization-replay-and-recovery.md
49598161138febbf889f2a5176e3504af8d8bb8481d2157fbcfbbd94d2b46604  docs\design\29-project-setup-build-and-deployment.md
d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed  docs\design\30-public-api-review-surface.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  docs\design\35-implementation-roadmap.md
0ab2776d4b0baa87e7ee2bbb2b97e6e79d38d815701a523715f094557663ec53  docs\proposals\m03b-scene-assets-original-p-operation-and-return-closure.md
6f06e3099002b5a05fec47fc71075ebfd3b4ace8bfc834f19c6c98b3fcf82490  docs\proposals\m03b-scene-assets-allocation-site-catalogue-addendum.md
aa32cf9b40d034a94b6972f8b716519710a70d9f797f91830dc616f71b1db9e4  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
4903a86fc323ed906a0045288bebab68b37c5b6beeff6312ea0440a9d05d453c  sources\TerraFX.Runtime\Scenes\SceneLoader.cs
8a822c991792d18bbcf3d58718c7c420833c45a261f406487c7a6dd2e2ef3df0  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
beed8cc76c4ccd55b487a96b52a31d4765a4e40513ef09883d7b0f56a201a8ff  sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
4d02c65f83b6c06a62408c93f1e1af9912e3446f6575614483b0ff6b83096522  sources\TerraFX.Runtime\Scenes\ScenePreparationFunding.cs
bf2a625cf9fb3d0eac647bba622d31b245066fb23c78e1eba96bef7398fc2725  sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
28f32fc8dcd1354a96a6e66844ed16b7b93e7a36e169cdfcefe812b62c10235d  sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs
2feda43f00600d4dd9ee73704750b41cb9f60e02ae33d29ad2484907abeb511c  sources\TerraFX.Runtime\Scenes\ScenePreparationDataBounds.cs
fbcdc763f47a83518e05f10062dbdab371d28aa59a4fb7f9d84083334bdb0778  sources\TerraFX.Runtime\Scenes\SceneSupport.cs
70e859e5d7dd16731d731ae8f1347a7c406218e0180b2f0a7a6d06b90ae5c7c3  sources\TerraFX.Runtime\Scenes\ScenePrefabAsset.cs
b7a29a5bdb1d73fd62d29f4c943b81b0582100a7dd4fd41fc7dcb73faaf8bf53  sources\TerraFX.Runtime\Scenes\SceneAssetCodec.cs
fcd81d97d3e07956aa35b12871b7c3bccd398ddf8761c3e5a4d38e84f7bcfbcd  sources\TerraFX.Runtime\Scenes\ScenePrefabAssetCodec.cs
cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a  sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
56cc97329bcf128c270873f0b3743944f435e932822e0d3e413f33df80fcd7a7  sources\TerraFX.Runtime\Scenes\SceneInstance.cs
f00b54b0e8f305f90df0d63ffdf6c3419feb8f3d2542e72d76a79ad2d0990946  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
a91aac1a9eb2ba7b43c111fab8baa9d6588a6c75e725c28ea9825f049472c969  sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
f4cf773ccbadd8564bd4eeff261c6a0bbc89f1a00ed71507bbdfafbe8e2021a6  sources\TerraFX.Runtime\Entities\World.ScenePreclaimedWorkspace.cs
ceac98719116305ba2727b892ce540a3b6b26d1ddf72afe06e21504909747fb0  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.ScenePreclaimedWorkspace.cs
ead70618fac9ef668fbebaefa35678d19dbfbbe944710d1418cc354c306d42f0  sources\TerraFX.Runtime\Entities\World.cs
1f66b39f8346edc4663813b409f5016656840f7861423b71d8f42beace2a6bd8  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
e20933f27395c0de0c44139331c27ef51d4a48db0999b9e1fb3146c46296da1d  sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs
ba6d607e72562f86b17dd71afd18c4169281716a6bb7490f3c73c1c06c6036e3  sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs
a6247f55f2d715aafec0428645de58a64a2cf786adaa7f6679afabae590bec71  sources\TerraFX.Assets\AssetStore.cs
1a8085a575f4c16ab05189a2271da28a607417209dd9136785d774fe0c043edd  sources\TerraFX.Assets\AssetStore.PackageLease.cs
cddee36ba0c515a5bd2a0155aa5b5812ea9762cf50919c602bf4775e5c39f18a  sources\TerraFX.Assets\AssetLease.cs
c28c48aa9f1423285e447c8aa2b70e907aa1fb3bec5f1f48352bb8370c26d19f  sources\TerraFX.Assets\AssetLeaseLifetime.cs
248e4d31711c60944861361d7d0d0cf9ab93e9dc74fb11e9e1ef57350328dc0b  sources\TerraFX.Assets\AssetPackageBacking.cs
1bdf040211fdaad0a1347ce9c1d8a478d65743ed543f362bf60d1e16159b5d03  sources\TerraFX.Assets\AssetPayloadReader.cs
60230adfefb17269fe1dd3a5519f57571244a73febf334d154c033e3cc2494cd  tests\TerraFX.Runtime.Tests\Scenes\SceneHierarchyValidatorTests.cs
```

### Five existing dirty tests: preservation only

These were the five dirty files at entry. Their raw hashes were captured before investigation and match the freeze. They are not acceptance evidence and were neither edited nor executed. No other worktree change is classified as user preservation.

```text
e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a  tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
```
