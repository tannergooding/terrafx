# M03-B Assets acquisition failure: package-opening ownership

**Status: tentative rectification direction under the requested unblocker policy, with an unresolved construction-funding gate. Not an implemented capability or physical-close observation. Keep `PreparedScene.QualifiedFootprint` null.**

## Additional scope and decision

The [release rectification](m03b-scene-producer-return-and-assets-release-rectification.md#assets-owned-release-rectification) already requires failure-preserving release and a pre-work acquisition owner. The [pre-lease recovery proposal](m03b-assets-prelease-recovery-owner.md) supplies the Scene-owned load-control direction and explicitly excludes package opening. Its [capacity addendum](m03b-assets-prelease-operation-profile-capacity-addendum.md), [built-in admission decision](m03b-scene-built-in-codec-profile-admission.md), and [None-read invocation proposal](m03b-scene-none-read-owned-invocation-boundary.md) retain their separate obligations.

This follow-up addresses the excluded **raw FileStream interval before an `AssetPackageBacking` exists**, including its handoff through factory creation and replacement. The new intrusive backing root does not cover that interval. In particular, joining the Store's replacement barrier does not acknowledge cleanup of a candidate that never returned a backing.

Select an **Assets-owned, unpublished backing construction owner before opening the file**, preferably by extending the existing backing carrier's construction lifetime rather than adding another Store, loader, or recovery service. However, the current contracts do not identify an admitted bootstrap grant and durable original root for that owner. **That design/funding gate prevents safe implementation of this selection today.** Tentative approval supplies a direction, not capacity or a physical-release guarantee.

## Current source facts

Paths below are beneath `sources\TerraFX.Assets` unless stated otherwise.

| Evidence | Current fact |
|---|---|
| `AssetPackageReader.OpenAsync` | Creates a private read-only FileStream with buffer size 1 and asynchronous/random-access options before its `try`. Hashing and package validation precede backing construction. Its catch awaits `stream.DisposeAsync()` and then rethrows; if that cleanup throws, it replaces the original failure. No backing recovery link exists on this branch. Constructor-internal partial acquisition also remains a framework obligation. |
| `AssetStore.CreateAsync` | Awaits `OpenAsync` before constructing a Store or entering its construction-cleanup `try`. A failed open therefore has no returned Store as its cleanup owner. Once a backing exists, failed Store construction calls `ReleaseOwner`. |
| `AssetStore.ReplacePackageAsync` | Serializes replacement and gives disposal a completion barrier. The local `replacement` stays null until `OpenAsync` returns. Its catch can retain cleanup failure only for a nonnull backing; its `finally` clears replacement activity and completes the barrier successfully regardless of the replacement outcome. |
| `AssetPackageBacking.ReleaseReference` | Final release retains `_stream` and the final reference during synchronous `Dispose`. On a caught close failure it records the exception, enters failed state, and links itself into `s_failedReleaseRoot`. New references reject and repeated release reports failure rather than retrying. This is a genuine managed root for an existing backing, not a root for the reader's earlier raw stream. |
| `AssetLease.Dispose`; `AssetLeaseLifetime.ReleaseReference` | The handle now retains `LeaseState` when lower release throws and rejects subsequent logical use through its state. Final lifetime release keeps Store/backing references, records backing success before Store accounting, and clears fields only after success. The earlier clear-before-release description is no longer current. |
| `AssetStore.CreateLease`; `AssetStore.PackageLease.cs:LoadAssetAsync` | Pre-lease gaps remain: lifetime construction precedes the `try`; failed handle construction sets `ownershipTransferred` in cleanup `finally` even if lifetime release throws. Load rollback calls `CompleteLoad` before releasing its backing. An issued handle's repaired state machine cannot own an unissued lifetime/value. These paths remain assigned to the existing pre-lease proposal, not a second mechanism here. |
| `AssetStoreOptions`; Chapter 30 section 6 | Existing byte accounting charges declared decoded wire lengths, and the lease limit caps counted handles/admitted typed loads. It is not a grant for opening controls, directory/validation workspace, retained failure machinery, or codec-owned managed allocations. Opening an empty package need not acquire a typed-load credit at all. |
| `tests\TerraFX.Assets.Tests\AssetStoreTests.cs` | `AssetPackageBackingFailedFinalStreamReleaseRootsBackingForProcessLifetime` uses `ThrowingFileStream`. That subclass throws before base disposal; the helper's `using` subsequently invokes disposal again. Its assertion concerns backing reachability, not an actual failing OS close or retained native handle. No result from this test is asserted here. |

**Distinguishing conditional schedule, not an executed failure:** a Store starts replacement with a correctly checksummed malformed candidate. Validation throws before backing construction; suppose the actual `DisposeAsync` also fails. `OpenAsync` cannot return the candidate, so `replacement` remains null and no candidate reaches `_pendingReleaseBacking` or the backing process root. The replacement barrier nevertheless completes. Disposal can then release the original package and succeed without knowing the candidate's cleanup disposition. A retained backing on a different path cannot repair that missing ownership edge.

This establishes a source-level coverage gap. It does **not** establish that the private production FileStream can experience such a valid close failure, that collection occurred, or that an OS handle leaked.

## Required construction owner and ordering

These are internal ownership roles, not new public declarations or schema states. They become implementable only after the funding gate below closes.

1. **Admit before construction and external acquisition.** The original Assets construction issuer must preclaim the whole opening carrier, actual read/validation workspace, invocation/return machinery and protected cleanup/failure retention. Install its construction interest in an independently retained root before FileStream construction. No cleanup-path allocation or later capacity admission may be needed to retain a failed open.
2. **Make the existing backing an unpublished owner.** Construct its empty ownership/progress carrier first; do not expose entries, provenance, read access or reference acquisition as though package validation had succeeded. Attach the returned FileStream before the next fallible step or asynchronous escape. A FileStream constructor that fails before returning still needs its complete framework/native unwind proof; the carrier cannot capture an object that was never returned.
3. **Keep one continuous acquisition graph.** Hashing, validation, temporary buffers and active reads remain under that same opening interest. Cancellation requests cleanup only after actual readers/decoders stop. The original owner persists through validation failure, cancellation, backing finalization and destination construction; a faulted Task is an outcome, not its replacement.
4. **Transfer to the exact destination.** Factory creation acknowledges handoff only after the actual Store successfully accepts the validated backing. Replacement acknowledges it at the existing atomic content-set switch. Retain the construction interest until acknowledgment and its actual last access/return. Transfer surviving backing/terminal-cleanup provision explicitly; do not refund it and reacquire a failure allowance later. Never publish an incompletely validated backing to make rooting convenient.
5. **Record cleanup disposition, not just invocation.** Close outside Assets/Store gates. Preserve the primary outcome and bounded original cleanup facts under provision established before work. Record confirmed release separately from uncertain/failed release and from remaining accounting/return work. No repeated Dispose, elapsed time, GC or finalizer observation upgrades uncertainty to success.
6. **Join candidate debt separately from replacement-body completion.** The serialized replacement owner must retain an unresolved opening candidate as well as the current backing or displaced backing it actually owns. Store disposal attempts independently safe current-package cleanup even if candidate cleanup failed, preserving both dispositions without overwriting one with the other. The existing completion barrier can signal that replacement execution ended; it cannot substitute for those release facts. A post-switch old-backing failure does not roll back the committed package.

The existing intrusive link can remain a retention implementation detail, but its storage and retained graph must have original admission. It is not an unlimited recovery budget. Do not solve missing return/funding by permanently rooting every normal successful operation.

Logical and physical outcomes remain distinct:

- Logical close prevents new use and may consume a handle/reference authority.
- A successful nonfinal backing-reference release transfers responsibility to remaining real references; it does not mean the file closed.
- A failed final close retains debt only to the extent that the lower boundary preserved ownership. Keeping a FileStream object cannot reconstruct a handle already severed below it.
- Returning lease/decode counters or completing an operation is not physical-close acknowledgment. Confirmed lower release followed by failed accounting must never cause another close.

## Why implementation remains funding-gated

Chapter 02 section 3 requires an admitted recovery record before external acquisition, but delegates its exact exception/task/sample/retained-state contract to a linked **“11 M03 4.7” section that is absent from the live Chapter 11**. That chapter currently has package loading, layout and focused acceptance sections, not that recovery definition. The requirement exists; the referenced definition cannot supply a bootstrap issuer or grant.

| Required decision | Missing authority |
|---|---|
| Factory bootstrap | Name the original issuer, before-work claim, construction root and failure-retention anchor when no Store has been returned. Define how unresolved failed creations remain charged and constrain further acquisition under that same authority. A local owner or a Store/record cycle is insufficient. |
| Replacement/bootstrap provision | Define the provision for the pending opening carrier and its coexistence with current/displaced package ownership, including retained cleanup and terminal delivery. The existing one-replacement election bounds concurrent replacement bodies, not those bytes or their release. |
| Complete footprint and outcome | Derive the selected carrier/workspace/cleanup/return peak and specify preservation/delivery of primary and cleanup failure within the existing public error contract. No numeric profile, extra option, exception family or success-shaped cleanup receipt is selected here. |
| Lower release | Establish the complete valid constructor/unwind and synchronous/asynchronous FileStream/SafeFileHandle/native-close closure, or supply an ownership-preserving lower acknowledgment. The process link alone establishes neither. |

Chapter 30's standalone and hosted callers use the same factory; Chapter 29 keeps Assets usable without a World or host. The original Scene `P`, a World process-registry cell, a Runtime funding callback, and the proposed already-open-Store Scene read slot are therefore not bootstrap authority for these public package-opening paths. Existing wire/count limits cannot silently acquire new meanings.

A complete valid nonthrowing cleanup proof can discharge its precise failure-retention branch without inventing a recovery pool. Otherwise Chapters 02/11/30 must close the original bootstrap ownership/funding contract before implementation. This proposal deliberately does not fabricate the missing budget or API.

## Implementation/test batches and retry gates

| Batch | Scope and exit condition |
|---|---|
| Close the owner contract and lower proof | Resolve the missing Chapter 11 recovery definition, original construction issuer/root, complete charge and public outcome semantics. Inspect the actual selected runtime/native constructor and both close paths. Choose between a complete valid nonthrowing proof and fulfilled ownership-preserving recovery; neither is established by this assessment. This batch gates code admission. |
| Implement package-opening ownership | After that gate, extend backing construction into the unpublished interval and wire the same owner through `OpenAsync`, failed Store construction and candidate replacement/disposal. Preserve exact package validation and atomic replacement. Keep issued-handle changes already present; implement typed pre-lease ownership only through its existing selected proposal, not a parallel Store queue. |
| Validate changed managed behavior | Extend existing Assets reader/Store/package-lease tests using real files/packages: empty-package success, checksum mismatch versus correctly checksummed malformed input, genuine cancellation and candidate rejection, old-package usability, Store close/replacement joining, exact handoff and once-only capacity return under the approved owner. Verify retained failure debt only with a supported reachable stimulus. Use ordinary owning-project Debug then Release checks and fresh nonzero selected/executed TRX membership per `docs\project\Building.md`; do not execute guarded Scene preparation for sizing. |
| Reconsider dependent consumers | Reconcile the final owner fields and retained debt with the existing Scene pre-lease/control and receiver catalogues. This does not close their actual-invoker, fault-capture, numerical-profile or retained-result gates. Native/AOT/public-consumer evidence remains separate under Chapter 35. |

Retry a blocked lane only when its missing input changes:

- **Bootstrap ownership/funding:** a reviewed original issuer, durable pre-work root and complete funded construction/failure/return profile exist, or a complete valid cleanup proof removes the relevant retention branch. Source edits that merely move a null assignment or add another process link are not that change.
- **Physical cleanup observation:** the real supported lower capability or safe real failure stimulus becomes available on the selected runtime/OS/filesystem path. A throwing subclass, fake close source, private-count corruption, injected allocator failure or second successful/no-op Dispose is not physical qualification. Do not repeat an unchanged blocked probe.
- **Scene-dependent execution:** the existing complete operation/preparation and actual-return gates close on the final inputs. Keep the footprint null meanwhile; Chapter 35 still permits unrelated dependency-ready M03-B/M05 work.

No build, test, Scene execution, native failure experiment or qualification was run for this proposal. Only this file was authored; no source, other document, commit or remote state was changed by this assessment.

## Bounded review inputs

Verified UTC `2026-11-19T02:50:25.3621873Z`; HEAD `7a19c777b7d6e1c32ad5ae0b4e231b87c6cd0cde`. The following 42 raw-file SHA-256 values freeze this assessment's inputs, not a repository inventory or transitive binary/runtime attestation. Chapters 04/16/30 were modified in the worktree and are inspected context, not accepted amendments; the other listed inputs were clean. Unrelated concurrent work is outside this assessment.

No schema/fixture contract is changed or qualified. Recheck HEAD, these bytes and their dirty state before disposition; changed inputs require reassessment, not silent hash refresh. Historical proposal freezes/results are not reaccepted.

```text
12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e  .github\copilot-instructions.md
03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58  .github\workflows\ci.yml
770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a  Directory.Build.props
aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105  Directory.Build.targets
40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291  docs\design\02-dotnet-api-memory-and-ownership.md
a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245  docs\design\04-capacity-and-workload-profiles.md
de1032a04217b2a08933332aee7db22df849cbfc069e76cf6b2263bf469df109  docs\design\11-assets-import-streaming-and-packages.md
b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a  docs\design\16-scenes-transforms-and-cameras.md
3b55a013eec327ecd648acbd5b53717dd218ad288e8047ac21277609efa2f47b  docs\design\29-project-setup-build-and-deployment.md
15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b  docs\design\30-public-api-review-surface.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  docs\design\35-implementation-roadmap.md
f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f  docs\project\Building.md
3086d68823cc53e67519adbe3ba71eac41a5b85054cb9ffc07432e387b33affe  docs\project\Roadmap.md
fb07baef70412a2ce33f56be8efdb3909046ad64a4fd5227e4731fa188e5a868  docs\proposals\m03b-assets-prelease-operation-profile-capacity-addendum.md
f947e6e040cf6dfa5a5a04de1c84c257536a9a37b3794c0cefcd5555cd55c18c  docs\proposals\m03b-assets-prelease-recovery-owner.md
8ca51d67ca00523270dd41a21a8c53cb4cdf65c08ee30fe127eba60f016c1b50  docs\proposals\m03b-scene-built-in-codec-profile-admission.md
514a1ac69d7ee47b7c94f143eb7c48383c60db4ee364b969197b102fa05e64a9  docs\proposals\m03b-scene-none-profile-pre-read-and-owner-reconciliation.md
9ea7d204e37425ceb45312bd78a763a789107b52e0af5a65573a672fb3136d9c  docs\proposals\m03b-scene-none-read-owned-invocation-boundary.md
3f8e4f0315c6cc2933fc982f64d5e94b9070eb1876a74b0688118bb17aff5a62  docs\proposals\m03b-scene-preparation-budget-accounting.md
199429a1eb94b5fe3f9f0da204343e310318d2336acdbc993a601e3a93db105b  docs\proposals\m03b-scene-producer-return-and-assets-release-rectification.md
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  global.json
d44373af315523f8af9f09f36bded2a688e2b5c1572fcc0732310c7ef590ead8  sources\Directory.Build.props
cddee36ba0c515a5bd2a0155aa5b5812ea9762cf50919c602bf4775e5c39f18a  sources\TerraFX.Assets\AssetLease.cs
c28c48aa9f1423285e447c8aa2b70e907aa1fb3bec5f1f48352bb8370c26d19f  sources\TerraFX.Assets\AssetLeaseLifetime.cs
f05ea48d34c98156f8db5da45c4436da9e9d00cfbeec40b4939c832b717d903e  sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs
266d3207e31e058268d4682aa7cedb4109aa316b8f285053f72bea22a61a475b  sources\TerraFX.Assets\AssetPackageBacking.cs
cc5657bb7135da606e16c9ef725f7b4cc779b434d83ee2e42f83be71198be20d  sources\TerraFX.Assets\AssetPackageLease.cs
06f12e7b2d7d89a44a92e28f41a8581af3a64f963df0dbdd935da2722984b4fd  sources\TerraFX.Assets\AssetPackageReader.cs
1bdf040211fdaad0a1347ce9c1d8a478d65743ed543f362bf60d1e16159b5d03  sources\TerraFX.Assets\AssetPayloadReader.cs
9f51cbfd4d0d6da21b088c5fed6765fc4a8b2879210ce7e5d2d3913b44d276a3  sources\TerraFX.Assets\AssetStore.cs
4b3f854a32824ae24f8051ef21886df78fcb561df00fb0a345b950dd90cc092a  sources\TerraFX.Assets\AssetStore.PackageLease.cs
270dabda38f2474b7d48b02a230f1492e2a3bcfcc6963a8ad1bd102ca6ceca53  sources\TerraFX.Assets\AssetStore.PackagePin.cs
ba6d607e72562f86b17dd71afd18c4169281716a6bb7490f3c73c1c06c6036e3  sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs
bb2123297d3e65309bd38ce5da0083e14319810139fa233ca0b0ebc4e5404594  sources\TerraFX.Assets\AssetStoreOptions.cs
76f077c074a4198c78984e46cf7adff94e23092abb3fa87ce18865bb31621101  sources\TerraFX.Assets\TerraFX.Assets.csproj
8a822c991792d18bbcf3d58718c7c420833c45a261f406487c7a6dd2e2ef3df0  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
beed8cc76c4ccd55b487a96b52a31d4765a4e40513ef09883d7b0f56a201a8ff  sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
4d02c65f83b6c06a62408c93f1e1af9912e3446f6575614483b0ff6b83096522  sources\TerraFX.Runtime\Scenes\ScenePreparationFunding.cs
87f7b8a4771020e43ce34b10ad636042c57ba1ae81661752bab65522bd7c1bcd  tests\TerraFX.Assets.Tests\AssetPackageLeaseTests.cs
e3afcd4326acda6ed627625da9e0b890f70db12a420bcd528e5c51d0467a6136  tests\TerraFX.Assets.Tests\AssetPackageReaderTests.cs
fd314681045e2a6a99fd2fefbf9abcd6f4e10caf8bb8029d69e28695fa52c626  tests\TerraFX.Assets.Tests\AssetStoreTests.cs
424bac52ec8a20742ae0e0de0df8416afe99db094488e18337ac842b7d8b033f  tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj
```
