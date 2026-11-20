# M03-B Scene original-Store recovery-delivery ownership

**Disposition: tentatively approved for bounded implementation under the requested blocker-proposal policy; owner decisions and executable qualification remain open. This proposal does not authorize opening any operational gate. `PreparedScene.QualifiedFootprint` remains null.**

## 1. Decision

Rectify the retained-input/fault/return gap in section 6 of [the original-P/cache/Assets-return proposal](m03b-scene-original-p-cache-and-assets-return-rectification.md#6-smallest-missing-capability-and-owner-decisions) with **one installed Assets read/recovery owner and a separately funded Scenes invocation/publication cohort**:

1. Before description, temporary backing acquisition, callback registration, or read construction, the original preparation root reserves the complete operation-private provision and installs the exact lower owner in a dedicated slot. Assets owns acquisition, typed input-use, decode/inspection, lease issuance, and its original faults/debt. Scenes owns its calls into Assets, destination installation, traversal/context scopes, and public delivery. Assets receives neither a World capability nor a Runtime callback/funding delegate.
2. Transfer only the installed lease/value graph into the existing cache, and subsequently the exact retained Scene graph into the existing Entities-issued receiver. Input/context, fault delivery, registration, and returning-call provision do **not** accompany that data transfer or become spendable on Task readiness.
3. The actual enclosing caller drives ordinary operation-specific advances and observes their return, including context restoration. The relevant owner retains registration and dispatch interests separately. Replace the unfulfilled supplier-return latch only after this complete join exists; do not replace it with a producer-written `Returned` flag.
4. Close Chapter 11's missing recovery-delivery contract with the clauses in section 4. Original exceptions and tokens are retained without substitution. An arbitrary reachable graph can leave the preparation's attribution only through a **verified independently prepaid ownership transfer**, not an assertion that its references are borrowed.

This chooses the internal control/transfer architecture and the recovery-delivery behavior. It does **not** assert that an unrestricted `CancellationToken`, ambient `ExecutionContext`, original mutable exception, or exposed `Task` has a payload-derived finite bound. Section 6 identifies the exact additional owner decisions needed to make that architecture executable. If no compatible finite grant/transfer exists, the all-outcome profile remains unavailable; tentative approval does not convert that result into zero bytes.

Scope is the original construction-bound AssetStore, built-in Scene/Prefab **2.0**, EPKG2/native-envelope **2.0**, package `None`, no CPU-resource-preparer path. Keep the same retained cache/backing between count and build. Do not select the separate synchronous two-codec tool lane, retarget a backing, prune caller registration, change public signatures/options/budgets, suppress caller context, change cancellation/error precedence, or add acceptance IDs.

## 2. Authority and current evidence

Decision inputs are frozen at **`8765ac76ef1200d977b079a6c7af4a93af77af46`**, branch `tannergooding-m05-implementation-plan`, UTC `2026-11-20T22:04:36.0351928Z`. Five unrelated dirty tests are preserved in section 9. The older proposal's source manifest is not this revision's evidence.

| Current owner text | Preserved requirement |
|---|---|
| `docs\design\02-dotnet-api-memory-and-ownership.md:87-121` | Explicit retained owners; actual consumption before terminal cancellation; asynchronous engine continuations outside gates; synchronous usage errors, ordinary pre-canceled Task delivery, original execution faults, independently attempted safe cleanup, charged live/quarantined debt, no implicit retry. Its Chapter 11 “M03 4.7” link has no definition in current Chapter 11. |
| `docs\design\11-assets-import-streaming-and-packages.md:39-55` | Exact immutable backing and registration, independent admitted backing/lease/decode ownership, unchanged replacement/disposal admission, and actual decoder/consumer end. Bounded parsing/queueing/storage is required, not established by a reference count. |
| `docs\design\16-scenes-transforms-and-cameras.md:116-128` | Exact original W before allocating work; W includes copies, cache growth, suspended Tasks/results, callbacks and unreleased cleanup. Transfer only actual retained allocation/capacity; temporary work remains under the use. |
| `docs\design\27-state-serialization-replay-and-recovery.md:55-69` | Wrapper closure, captured consumer, data owner, cleanup and quarantine have different lifetimes. Cancellation, metadata and GC do not discharge them. Empty-Scene restore does not qualify this read producer. |
| `docs\design\29-project-setup-build-and-deployment.md:15-29,51-109,397-426` | Assets depends only on Core; Entities issues World/domain/use capabilities. Allocation, codecs, result delivery and physical cleanup stay outside gates. Ordinary .NET and optional Runtime Async compilation are separate qualification inputs. |
| `docs\design\30-public-api-review-surface.md:3497-3550,3682-3683,3825-3918` | Preserve `Task<PreparedScene>`, existing Assets Task/lease signatures, exact construction-bound Store, input-copy ordering, caps, provenance and conventional channels. Codec-managed allocations are not measured by the wire-length counters. |
| `docs\design\04-capacity-and-workload-profiles.md:7-21,58-65,193-209`; Chapter 35 `:28-58,101-129` | Reserve complete peaks without changing quotas; attribution/physical ownership are not interchangeable. Managed helper, native/AOT, public consumer and milestone acceptance remain distinct. |

Source references below are repository-relative; `Scenes`, `Entities`, `Rendering`, and `Assets` expand to their corresponding folders under `sources\TerraFX.Runtime` or `sources\TerraFX.Assets`.

| Current source | What is actually present |
|---|---|
| `Scenes\PreparedScene.cs:15-88,102-176`; `Scenes\SceneLoader.cs:163-203` | The root retains Store/descriptor, token, TCS/Task, fault pair and delegates. The null footprint and CPU-resource-preparer refusals remain. Description currently precedes `AcquireGuidUse`; pre-canceled `Task.FromCanceled` precedes either. Neither earlier interval is financed by a future use. |
| `Scenes\SceneLoadOptions.cs:73-137`; `Scenes\SceneLoadOptionsSnapshot.cs:8-16`; `Scenes\SceneExternalBinding.cs:9-36`; `Entities\Entity.cs:9-36` | Freeze copies/sorts arrays but retains their sealed binding elements. Those elements have only Guid/nullable scalar Entity facts; they are a closable bounded graph, unlike arbitrary token/context roots. Do not accidentally add the entire World graph to an Entity value's membership. |
| `Assets\AssetOwnedReadDescriptor.cs:7-90`; `Scenes\PreparedScene.cs:17-18,93-95`; `Scenes\SceneAsset.cs:65-96`; `Scenes\ScenePrefabAsset.cs:59-87` | Descriptor and semantic values retain registration/schema/read-limit roots. The root's descriptor/Store fields are readonly; clearing child Task aliases cannot retire this graph. Descriptor uses are the planner's validation/root-read prefix (`ScenePrefabExpansionPlanner.cs:157-172`), but exact Store identity is also checked during activation (`PreparedScene.cs:570-582`). |
| `Assets\AssetLease.cs:140-147`; `Assets\AssetLeaseLifetime.cs:77-93`; `Assets\AssetPackageLease.cs:95-117,199-208`; `Assets\AssetStore.ReadDescriptor.cs:59-104` | Anchored facades acquire a temporary backing reference before lower descriptor/admission/cancellation checks. Refusal may require final release despite having no issued result or typed lease credit. There is no installed all-outcome read owner at that boundary. |
| `Assets\AssetStore.PackageLease.cs:73-209`; `Assets\AssetPayloadReader.cs:16-37`; `Assets\AssetPackageReader.cs:583-606` | Real reads retain buffers/context/semantic output and inspect references. `None` still reaches the short-read `RandomAccess.ReadAsync` loop through engine-authored `Memory<byte>`. This proposal does not make that Chapter 02 violation, native input lifetime, or runtime invoker profile disappear. A throwing release in `finally` can replace an earlier execution fault or skip subsequent accounting. |
| `Assets\AssetStoreRegistration.cs:13-78,144-174`; `Assets\AssetStoreRegistrationBuilder.cs:27-29,112-209`; `Scenes\SceneSupport.cs:23-68` | Receipts identify the executed built-in Scene/Prefab adapters. The General registration still retains all schema/type maps, adapters and caller codecs. Automatic target inspection tests registration membership without invoking those target codecs. A two-entry execution set is not a two-entry retained registration graph. |
| `Assets\AssetLeaseLifetime.cs:163-235,266-271`; `Assets\AssetLease.cs:250-324`; `Assets\AssetPackageLease.cs:260-340`; `Assets\AssetPackageBacking.cs:300-408`; `Assets\AssetStore.cs:86-184` | Physical-reference consumption precedes final Store accounting. Original release failures survive in multiple carriers and repeated close paths recapture/rethrow them. Store disposal retains its completion Task. The intrusive failed-release root proves reachability, not financing or confirmed native disposition. |
| `Scenes\PreparedScene.Preparation.cs:59-111,384-430,580-776,798-912` | `Start` protects entry context isolation; completion callbacks use `OnCompleted`. Publication preserves primary/first-cleanup identity ordering and the cancellation token. `_supplierProducersUnjoined` is never cleared. Callback `finally`, `TrySet*`, and `MarkPublicationAccessEnded` are not actual enclosing dispatcher/context-return acknowledgements. |
| `Scenes\ScenePrefabExpansionPlanner.cs:143-247,819-912,2236-2280`; `Scenes\SceneExpansionPlan.cs:285-369` | Root lease/build ownership is staged after read return. New Prefab `.Value` now lies inside disposal `try/finally`; context checks and `Add` still precede retention acknowledgement. A disposal failure can replace the insertion/value failure. Plan's repeated-disposal latch can return while failed first cleanup debt persists. |
| `Scenes\PreparedScene.Preparation.cs:129-267`; `Scenes\ScenePreparationFunding.cs:13-44`; `Entities\WorldGuidUseControl.cs:103-171`; `Entities\World.ScenePreclaimedWorkspace.cs:15-72` | Original producer subclaims and lower checked preclaimed receiver construction exist. A temporary/return claim is not retained D, and the pending-owner helper cannot be reused as a phase-wide blanket transfer. |

### Observations already on disk

`SceneAssetStoreTests.cs:1804-1918` retains **256 sequential real leases**. Both existing Debug/Release one-test TRX files report `AllocatedBytes=22312`, `AllowanceBytes=65616`, and no exceeded insertion prefix. The measured intervals are dictionary construction/insertion; reads, semantic values, all-fault delivery, cold initialization and return are outside them. This is stronger live-cache insertion evidence than null entries, not a read/recovery profile.

The subsequent Debug/Release three-test artifacts each passed nested expansion, retained-root-backing after replacement, and cycle rejection. These are internal planner observations, not execution through the null-footprint public preparation route. Section 9 names all four artifacts. No build, test, native run or physical-failure injection is performed for this proposal.

## 3. Exact ownership partition

The proposed lower owner is an operation-specific Assets control, not a generic result/task framework. Its installed state owns: exact descriptor/source identity; temporary backing-reference facts; existing budget credits; typed input buffers and actual native-use interests; decode/reference/semantic progress; lifetime/lease/state construction candidates; one destination-election generation; original failure/outcome and cleanup samples; and lower consumption/registration/return interests. Its acquisition-free construction must itself be preclaimed.

The Scenes invocation/publication cohort owns every upper invocation/resume carrier, callback/delegate and captured context, pending result alias, installation interest, failure conversion/publication allocation, and actual upper return. Both controls have bounded fixed membership; no per-failure lists, late helper construction, dynamic recovery queue, or unbounded waiter is introduced.

| Graph and paying owner | Start, before the relevant fallible work | End or exact transfer |
|---|---|---|
| Entry/refusal delivery: Scenes front end, with a separately established paying lifetime | Usage-error construction or pre-canceled Task creation, before an operation use exists | Actual synchronous unwind/public return plus an independently funded public-observation transfer. It cannot debit a never-issued P; this paying lifetime is an unresolved owner decision. |
| Frozen options and sealed binding graph: original preparation P | First allocating freeze/copy/retention | Body/options cleanup or exact retained adoption. Include all surviving arrays and retained sealed row storage, not only array headers. Scalar Entity identity does not confer a World retention capability. |
| Full shared Store/backing/registration graph: original Assets lifetime | Original Store/generation construction and each independently admitted reference | Actual last read/value/descriptor consumer and required native/control release. Exact membership includes unused retained General-registration codecs. A Scene can exclude these bytes only against that real independent grant. |
| Operation-private read graph: installed Assets control, attributed to the same original P | Description/construction, temporary source reference, read/buffer/context work and issue candidates | Exact result-and-charge adoption moves the surviving lease/value subset to the cache. All temporary, fault and lower return interests stay with this control until their actual end. |
| Cache/plan retained D: builder, then plan/Entities receiver | Before dictionary/growth/miss and before receiver construction | Same leases/capacities and semantic graph move once. Installation refusal retains the donor and candidate; successful transfer does not transfer read/return scratch. |
| Upper invocation/context/registration graph: Scenes cohort, original P | Supplier entry, registration and each resumption/context boundary | The real caller observes ordinary invocation and context restoration returning, and registration plus queued/running/returning dispatch consumers end or transfer. |
| Original primary/cleanup/cancellation delivery graph: its read or Scenes publication owner, original P | Before any failure-capable acquisition/invocation, not after catching the exception | Exact independently prepaid fault/public-delivery owner accepts the complete graph and all outstanding aliases, or the original owner remains charged. A nullable reference slot is not a byte bound. |
| Release/accounting/quarantine debt: the actual resource owner and its existing attribution anchor | Before acquisition; progress installed after every irreversible step | Actual release/accounting return or an explicit same-charge retirement transfer. Store/World/domain closure, repeated no-op disposal, Task terminality and GC do not discharge debt. |
| Public producing Task/result and engine publication return: Scenes publication cohort | Before creating/exposing the public promise | Engine-created delivery storage transfers only to a defined prepaid protocol owner. Caller-added observation graphs need independent real ownership; they are not included in a guessed two-slot fault allowance. |

Conservation stays within the existing budgets:

```text
P = original MaximumPreparationBytes = W, unchanged
original independent use initially prepays 256 + P

Every live engine-owned input/read/fault/return allocation or reserved capacity
belongs to an outstanding original-P partition, an exact retained receiver,
or a demonstrably independent existing lower owner.

Data adoption: move exact D and receiver-owned provision once.
Leave temporary/input/fault/return claims outstanding with their original owners.
Returning claims cannot finance the successor operation.
```

This is not another World reservation or a new Assets budget. Existing wire-byte/lease/decode counters continue unchanged; they cannot be renamed into a grant for managed fault/context/control storage. Assets may enforce the supplied operation-private scalar provision, but World attribution and release remain in Scenes/Entities. No Assets object calls back into Runtime to refund it.

Private description references should live in the owned read interval rather than indefinitely in a result facade once no read uses them. If implementation shortens that retention, preserve exact Store identity and activation misuse precedence: clearing the current `_assets` field would otherwise make a correct-but-disposed preparation appear foreign. An opaque identity without a Store back-reference can preserve comparison, but is not backing access authority. Its construction and all remaining shared roots must be funded; an identity token alone cannot discharge registration or package debt.

## 4. Proposed Chapter 11 recovery-delivery closure

These are proposed clauses for the **existing Chapter 02 cross-reference**, not a new public exception family, schema, acceptance case, or amendment made by this document.

1. **Preowned record.** Before external acquisition, install the record holding every acquired reference/credit, partial/candidate graph, native-consumer interest, and complete cleanup/delivery provision. A failed constructor before installation leaves those obligations with the preowned constructing caller. Capturing an exception after an unowned acquisition is too late.
2. **Original objects.** Keep the original execution exception and the first distinct original cleanup exception, with their existing diagnostic context. Two sample slots bound reference count only. Their transitive fields, `InnerException`, `Data`, strings, stack/EDI/runtime captures and live aliases require complete ownership/provision. Do not clone, truncate, stringify, clear user `Data`, synthesize scalar-success delivery, or substitute a smaller exception to obtain a bound.
3. **Precedence.** Preserve the current Scene publication projection: a faulted execution plus a distinct first cleanup sample publishes `[primary, cleanup]`; identical samples are not duplicated. Otherwise a cleanup failure is the fault. An otherwise canceled operation with failed cleanup faults with that cleanup sample, not a successful cancellation. With successful cleanup, cancellation publishes the original token; do not add a synthetic cancellation exception to the fault pair. Additional cleanup failures retain the existing bounded scalar count/stage facts, not an accumulating exception collection.
4. **Independent cleanup.** Attempt independently safe acquired releases outside gates even after a sibling fails. Record irreversible backing consumption before accounting. A consumed reference is never consumed again to retry accounting. Retain stopped-but-unreleased debt; still-consuming work remains live. A blocking/unreturned consumer does not acquire a falsely terminal success/fault/cancellation Task by being called quarantined.
5. **Repeated close.** Repeated close reports the existing original failure/outcome without starting another physical attempt or clearing debt. Explicit retry, if separately authorized, owns its next attempt. Qualify the original EDI/stack delivery behavior, including repeated calls; do not assume repeated `ExceptionDispatchInfo.Capture` has constant storage or silently change stack semantics to make it so.
6. **Delivery handoff.** The donor retains all fault/token/context/result aliases and their charge until one exact recipient, already funded for the complete graph and its future permitted retention/growth, atomically accepts them. Only then may donor aliases retire after their own actual calls return. The recipient may share the original object, but every continuing engine alias must own a funded interest in that same lifetime. Completing a Task, exposing an exception, or clearing one field is not this transfer.
7. **Caller observation is not an engine grant.** A caller can retain/mutate an original exception and register arbitrary work on a public Task before it is terminal. A protocol owner must distinguish those independently paid caller allocations from engine-created holder/list/dispatch storage and establish actual retained interests. Do not treat the CLR, caller, a borrowed reference, or “already allocated” as an unspecified unlimited allowance. If that distinction cannot be implemented or proved with the current raw Task/token/error surface, the selected all-outcome profile is unavailable.
8. **Publication boundary.** Publish only after protected borrows/mutation and actual input consumption end and required cleanup attempts return, outside gates on a permitted control thread. A possibly published success is never rewritten into rollback. A subsequent publication/return/retirement failure keeps its real owner and separate debt; it does not replace an already committed public result or authorize early credit return.

These clauses preserve the conventional .NET channels. They rectify the current lower `finally` paths that can replace a primary failure with cleanup; that accidental source behavior is not a new precedence contract.

## 5. Actual caller-return protocol and schedules

Use the actual caller, not an observer added after the fact:

- The Assets driver invokes the selected ordinary read/codec/inspection/release advance inside its retained input/context interval. Only after that call and its context scope return can the driver record that **callee** interval ended. It then returns to its own prepaid caller; its own tail remains charged.
- The Scenes driver calls Assets and the existing planner/destination advances. It records callee return only after the lower single consumption and installation/context scopes return. The same rule applies to cleanup/accounting and `TrySet*` publication.
- An admitted dispatcher/trampoline is the enclosing caller of a driver callback. It observes driver and context-restoration return before retiring its dispatch interest. Any remaining framework/native tail must be parent-only, with no reference to the retiring operation, or transferred to an identified prepaid lower lifetime.
- The outer public facade has no engine caller that can magically acknowledge its return. Its actual ABI/unwind/context tail must be proved parent-only, or belong to the separately closed public-delivery lifetime. A worker watching the returned Task cannot provide this proof.

For each exact operation generation, retain distinct construction/start, native input-use, completion consumption, destination installation, registration, dispatch/context return, cleanup/accounting, and publication-return interests. Registration and dispatch distinguish **reserved, registering, queued, running, returning, ended, uncertain/quarantined** membership. Fixed membership is allowed; one coarse complete bit is not. Check generation/counter exhaustion before publication, never wrap or overwrite an occupied generation.

Synchronous completion remains synchronous at the existing inline points; `ConfigureAwait(false)` is not an instruction to yield. Suspended completion preserves logical default/nondefault/suppressed ExecutionContext behavior and does not capture arbitrary caller SynchronizationContext for internal owner work. Do not force all entry work onto a worker, switch blindly to `UnsafeOnCompleted`, suppress flow, or replace ordinary cancellation with observation-only cancellation.

The following distinguishing schedules are derived from source/contracts, **not newly executed**:

| Schedule | Required ownership and caller result |
|---|---|
| Invalid usage, then valid pre-canceled entry | Preserve synchronous usage before canceled Task and no admitted operation. The pre-admission promise/token/unwind graph needs the separate front-end delivery owner; a later P cannot pay it. |
| Anchored reference acquired; descriptor/Store/quota check refuses synchronously | Installed Assets control owns the temporary reference before the check. If source/Store close makes its release final, retain native/release and accounting debt even with zero issued-lease credits. Preserve the original admission failure plus cleanup projection. |
| Read completes inline; codec or descriptor validation fails | The ordinary caller catches the original failure while its context and construction/return interests remain owned. Independent cleanup returns before delivery selection. No artificial suspension hides a synchronous failure interval. |
| Read suspends; cancellation or replacement/disposal occurs | Actual accepted native input/backing ownership survives cancellation. Consume the real completion once, finish existing decode/inspection cancellation checkpoints, and preserve old-backing selection. No cancellation/ready signal frees the destination buffer or advances the next read into returning storage. |
| Lease issued; `.Value`, context comparison or dictionary `Add` fails | The operation/destination election still owns the lease before acknowledgement, even though cache Count did not increase. Capture the original failure before disposal; a disposal fault is the cleanup sample. Retain uncertain adoption roots rather than refunding one guessed side. |
| Callback runs inline while `OnCompleted` is still registering | Registration and callback are simultaneously live. A returned callback cannot retire the registering caller. Installation or public readiness does not reuse the generation until both real returns are fulfilled. |
| Registration throws after possible publication; callback is queued/running | Keep the exact reserved part and both possible consumers. No refund on the assumption that throwing registration published nothing; no re-registration/second completion to recover. |
| Context restoration or publication does not return, or produces its existing failure | Retain the enclosing call/context interest and original failure/debt under the selected runtime's behavior. A callback `finally` before context restoration is not return evidence. Do not manufacture a managed success if the runtime boundary aborts or remains live. |
| Backing consumption succeeds; Store accounting fails | Preserve the consumed-reference fact and accounting-only debt, attempt other independently safe cleanup, retain original failure, and never physically close again to settle accounting. |
| Successful cache/plan/result installation | Exact retained data adopts once. Input/fault/registration/publication-return claims stay separate; only the actual fulfilled caller joins release them. The prepared body's use remains until its established disposal/activation lifetime ends. |
| Public Task/exception aliases grow while cleanup debt remains | Preserve original references and semantics. All permitted growth must have a real independent paying owner or fit the original protected grant. Without such proof, no all-outcome numerical profile is usable. |

## 6. Finite attribution: decisions still required

A selected receipt or payload length cannot close these three counterexamples:

1. Identical tiny Scene/Prefab bytes and limits can be invoked with a CancellationTokenSource holding arbitrarily many registrations, or an AsyncLocal value retaining an arbitrarily large mutable graph. Exact token preservation/context flow retains those roots; inspecting the token or counting context entries is not a safe graph-size/provenance proof.
2. An original cleanup exception retained by a failed lease/backing/preparation can also be visible through Task delivery. Adding a large object to its public `Data` changes the retained graph without changing either fault-slot count or asset length. Custom exception fields and repeated EDI/stack captures add independent cases. Clearing/cloning it would violate original-fault delivery.
3. After `PrepareAsync` returns its still-pending raw Task, a caller can register any number of continuations with arbitrary captures. Engine producer storage, BCL continuation storage, caller observation Tasks, and dispatcher tails share reachability but not necessarily a paying owner. Task terminality does not identify their allocation provenance or actual return.

For the selected route, the complete peak includes the engine read/control/data prefixes **and** the non-overlapping retained input, original-fault/public-delivery, registration/context and actual-return terms. Each term must be a checked finite upper bound or an exact, already funded independent grant with specified consumers/end. An unknown term makes the profile **unavailable**, not zero and not “covered by the existing constant.”

| Exact owner decision | Recommended disposition and remaining proof |
|---|---|
| Chapter 11 recovery record/sample/delivery contract | Adopt section 4 for this lane: original identity/token, existing two-slot projection, independent safe cleanup and charged debt. No scalar error substitute, implicit retry, new error precedence or public exception is selected. |
| Who prepays front-end/public protocol storage before P exists, and who receives escaped delivery storage? | Supply an actual issuer, existing-budget provenance, complete graph membership, construction point, concurrent-call peak, and end/transfer transaction. Do not debit future P or reinterpret wire budgets. Current source has no such compatible grant; retain NO-GO rather than altering the pre-canceled/usage channel. |
| Can arbitrary token/context/original-fault/public Task observation roots obtain a finite compatible grant with current signatures? | Require a real nonrevocable paying interest covering current descendants, permitted mutable growth, engine-held aliases, and restore/dispatch/delivery lifetime. Keep the original root charged until that transfer. No such general mechanism is demonstrated by the inspected interfaces. If it cannot exist, a **separate explicit owner-reviewed semantic change** is needed; this proposal does not choose narrower tokens, empty contexts, immutable/rewritten faults or a replacement Task API. |
| Full retained General-registration and shared Store lifetime | Original Assets owner must provide the exact graph grant through descriptor/semantic/lease consumers and actual original-generation release. Built-in receipts do not grant arbitrary retained caller codec graphs. No pruning, two-codec substitution, Store migration or Runtime dependency is permitted. |
| Actual native/control/dispatcher/public-facade return | Name and qualify the real enclosing callers and exact generated/runtime/native tails. Supply finite original-Store I/O/lifetime financing from its owning workstream; the current B1 port and `Memory<byte>` path are not this capability. Default/no-context success alone is not all-outcome closure. |

**NO-GO remains:** no useful all-outcome `C_op`, preparation J/K/footprint, original-Store operational selection, receiver qualification, or Scene-dependent native/public-consumer activation while any required grant/return is unproved. The CPU-resource-preparer refusal (`PreparedScene.cs:122-126`), funded bundle-settlement refusal (`Rendering\RenderSceneAssetBundle.cs:469-473`), bounded tool token/context refusals (`Assets\AssetStore.Bounded.cs:36-52`), and absence of bounded-tool CLI activation remain unchanged. Ordinary pre-entry validation/canceled delivery is preserved, not claimed to be newly qualified.

Independent contract-complete M05 kernels/layout, shader compilation, adapters, diagnostics and other dependency-ready work can continue under their own owners. This proposal does not qualify positive native diagnostics/pixels, World extraction, nonempty restore, dynamic animation, or other missing producers.

## 7. Ordered implementation batches

Each batch must leave the footprint and consumer guards closed. These are owner-scoped recommendations, not code changes performed here.

| Order | Coherent batch and owning files | Integration boundary / dependency |
|---|---|---|
| 1 | Close section 4's Chapter 11 recovery-delivery clauses and the exact grant decisions in section 6. Derive retained-input and failure/delivery membership from the current types, including General-registration roots and exposed Task sharing. | Freeze the complete chosen contract/profile inputs. Pure bounded membership/control work may proceed tentatively; arbitrary-input/public-delivery operational execution waits for a compatible paying lifetime. |
| 2 | In `Scenes\PreparedScene.cs`, its preparation partial and `ScenePreparationFunding.cs`, implement dedicated lower-owner attachment and disjoint original-P construction/data/input/fault/return claims. Preserve existing pending/phase elections. Shorten description retention only with unchanged exact-Store checks. | Bootstrap claims precede constructor/description/callback work. Prove failed construction and closure races retain the exact root and claims; no second World use or refund/reacquire interval. |
| 3 | In the original-Store Assets workstream (`AssetStore.ReadDescriptor.cs`, `AssetStore.PackageLease.cs`, lease/backing ownership paths), implement a complete acquisition/read/inspection/issue/cleanup slice under the installed owner. Include zero-credit temporary-reference failure and primary/cleanup capture before unwind. | Requires the real original-Store input-use/lifetime grant and Chapter 02-compatible I/O. Do not substitute the bounded two-codec/scalar-failure decoder lane or a new Store. Whole acquired progress is installed before the next fallible operation. |
| 4 | Integrate ordinary Assets and planner advances with the actual Scenes/Assets callers, context scopes, registration and dispatcher-return cohorts (`SceneLoader.cs`, `PreparedScene.Preparation.cs`, `ScenePrefabExpansionPlanner.cs`). Preserve existing algorithms and same-cache membership. | Exercise inline and suspended return joins, uncertain registration, installation failure and public publication overlap. Remove the supplier latch only when all its real obligations are fulfilled. No extra observer, always-queued wrapper or context suppression. |
| 5 | Complete exact cache/plan receiver adoption and fault/public-delivery transfers using existing Entities preclaimed transactions. Keep protected read/return claims out of D. | Closed World/domain, constructor failure, failed cleanup and repeated close retain exact debt. Receiver or delivery acceptance must precede donor retirement; destination success alone does not end the caller. |
| 6 | Derive and corroborate the final all-outcome numerical profile, then seek owner acceptance of those exact bytes. | Debug/Release managed observations, selected generated/runtime/native return evidence and public/native consumers are separate gates. Any unproved term keeps the profile null; Rendering/tool/other producer closure remains separately required. |

## 8. Focused validation and evidence criteria

Use the SDK selected by `global.json`, ordinary owning-project NUnit/VSTest commands from `docs\project\Building.md`, Debug before Release, combined related filters, current-run nonzero selected/executed counts, and retained TRX/logs. Do not build a separate qualification runner or rerun unchanged blocked native probes.

- **Managed ownership/behavior:** add data-driven regressions in the existing Assets Store/package-lease tests and Runtime Scene tests for every distinct schedule in section 5. Preserve the real 256-lease cache test and replacement/nested/cycle cases. The selected owner path must execute; an internal planner pass alone does not establish a public preparation producer. Keep unrelated dirty tests and all guards intact.
- **Conservation and failure:** independently derive complete exact-fit/one-byte-short peaks, including baseline, candidate/old overlap, fault/context and returning interests. Confirm the named boundary was reached, no acquisition preceded its positive claim, no double physical charge/World debit occurred, and no successor spent an outstanding returning claim.
- **Fault/cancellation/context:** assert original exception references and primary/cleanup order, cancellation-token equality, default/nondefault/suppressed context isolation, no unintended SynchronizationContext capture, preserved pre-admission channel, and no implicit close retry. Exercise caller-visible exception mutation and pending-Task continuation registration to test the proposed ownership boundary, not to declare those graphs bounded.
- **Actual return:** identify real compiled callers of ordinary read, codec, installation, cleanup, `GetResult`, registration and `TrySet*`; observe context restoration and queued/running/returning overlap. Retain the selected compiler/runtime/native correspondence and whole carrier sizes. A producer bit, cleared alias, callback-local `finally`, ready Task or post-hoc observer cannot pass this criterion.
- **Lower lifetime/native debt:** use real original packages/generations, accepted input consumers, nonfinal/final release, consumption-before-accounting failure and Store/World/domain closure. Managed throwing schedules qualify only their managed ownership behavior. Actual short-read/cancellation/close/dispatcher failure requires a safe real stimulus; report the missing Chapter 35 capability and retry condition when unavailable.
- **Final acceptance:** recheck all contract/source/profile inputs before applying owner disposition. The current 22,312-byte dictionary observation is not the complete read/return peak. No footprint, native/AOT/public-consumer or CLI gate opens from this proposal or an incomplete/unsupported selection.

## 9. Bounded decision-input freeze

This 60-file freeze binds this proposal's inspected owner/source context, preserved dirty files and existing observations. It is not a repository inventory, binary/runtime closure, executable qualification or new acceptance pipeline. The output proposal is excluded. Before applying a later disposition, verify HEAD, dirty state, exact membership and raw hashes; changed inputs require reassessment.

For each group, SHA-256 hashes raw file bytes. Its digest hashes UTF-8 without BOM/trailing newline over ordinal-sorted `relative-Windows-path<TAB>lowercase-file-SHA256` lines joined with LF. ALL is the unique union.

| Group | Files | SHA-256 |
|---|---:|---|
| Contracts | 11 | `ed6ec7416fb908fb936b22a6edf965378dbb7ee81b3341ca4367289bd6009d07` |
| Build | 8 | `c4361501aad603abe08a446afc21c9eaed606826c4b9aee84e6eca009b6f0e4e` |
| Source | 30 | `20d9cd1d3eef27b6fd32986f3c5881d8448f0092fbe7e92d43fa92086a10a2b3` |
| RelatedTests | 2 | `f886c9b75b6aaee9569eeb1d1f2b98350db42646055625e2fc0f73394cfb4ea0` |
| PreservedDirtyTests | 5 | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |
| ExistingEvidence | 4 | `aec8ada0f9f2aa400f5d82c255bb0e2dd02c3f552d2a2a2037b3cb250527d7b9` |
| ALL | 60 | `bdb3f794dd027a6af258140507f672a878306624e5a61c994cbff0f0d8a66d36` |

Expand each bracketed prefix into repository-relative paths; each following line is one group member.

```text
[Contracts docs\design\]
02-dotnet-api-memory-and-ownership.md
04-capacity-and-workload-profiles.md
11-assets-import-streaming-and-packages.md
16-scenes-transforms-and-cameras.md
27-state-serialization-replay-and-recovery.md
29-project-setup-build-and-deployment.md
30-public-api-review-surface.md
35-implementation-roadmap.md
[Contracts docs\project\]
Roadmap.md
Building.md
[Contracts docs\proposals\]
m03b-scene-original-p-cache-and-assets-return-rectification.md
[Build .]
.editorconfig
.github\workflows\ci.yml
global.json
Directory.Build.props
Directory.Packages.props
sources\Directory.Build.props
sources\TerraFX.Assets\TerraFX.Assets.csproj
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
[Source sources\TerraFX.Runtime\Scenes\]
PreparedScene.cs
PreparedScene.Preparation.cs
SceneLoader.cs
ScenePreparationFunding.cs
ScenePrefabExpansionPlanner.cs
SceneLoadOptions.cs
SceneLoadOptionsSnapshot.cs
SceneExternalBinding.cs
SceneSupport.cs
SceneAsset.cs
ScenePrefabAsset.cs
SceneExpansionPlan.cs
[Source sources\TerraFX.Runtime\Entities\]
Entity.cs
WorldGuidUseControl.cs
World.ScenePreclaimedWorkspace.cs
[Source sources\TerraFX.Runtime\Rendering\]
RenderSceneAssetBundle.cs
[Source sources\TerraFX.Assets\]
AssetStore.cs
AssetStore.ReadDescriptor.cs
AssetStore.PackageLease.cs
AssetLease.cs
AssetLeaseLifetime.cs
AssetPackageLease.cs
AssetPackageBacking.cs
AssetPayloadReader.cs
AssetPackageReader.cs
AssetStoreRegistration.cs
AssetStoreRegistrationBuilder.cs
AssetOwnedReadDescriptor.cs
AssetStore.Bounded.cs
[Source sources\TerraFX.Content.Tools\]
Program.cs
[RelatedTests tests\TerraFX.Runtime.Tests\Scenes\]
SceneAssetStoreTests.cs
SceneHierarchyValidatorTests.cs
[PreservedDirtyTests tests\TerraFX.Runtime.Tests\Scenes\]
PreparedSceneAssetRetentionTests.cs
RenderSpriteCapacityTests.cs
RenderSpriteComponentTests.cs
RendererExtractionTests.cs
SceneAssetStoreTests.EngineHost.cs
[ExistingEvidence artifacts\tst\Debug\]
tagoo_GOODING-ZEN4_2026-11-20_13_47_10_net11.0.trx
tagoo_GOODING-ZEN4_2026-11-20_13_51_45_net11.0.trx
[ExistingEvidence artifacts\tst\Release\]
tagoo_GOODING-ZEN4_2026-11-20_13_48_05_net11.0.trx
tagoo_GOODING-ZEN4_2026-11-20_13_52_45_net11.0.trx
```

The first Debug/Release pair executes `RetainsRealPrefabLeasesWithinCacheGrowthAllowance` (1/1/1 per configuration); the second executes the three planner cases named in section 2 (3/3/3). No framework-test count is a conformance/native/public-consumer count.
