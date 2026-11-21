# M03-B Scene original-Store execution closure

**Disposition: tentatively selected for implementation progress under the user's blocker-proposal instruction. Not an accepted contract amendment, numerical profile, or qualification result. `PreparedScene.QualifiedFootprint` stays null; all existing consumer/native/CLI guards stay closed.**

## 1. Selected bridge and unavoidable decisions

Implement the **ordinary construction-bound AssetStore**, not the bounded-v1 validation Store, with:

1. One Assets-owned package-I/O worker and original lifetime grant shared by that Store's backing generations. Opening, hashing, offset reads and final file disposition all use that worker's private native port. Its ordinary callers use synchronous scoped spans over explicitly owned storage; Windows overlapped input remains owned through actual completion, and Linux `pread` remains owned through syscall return.
2. Acquisition-free, installed Assets opening/read controls. The selected Scene/Prefab reads are ordinary acquisition/decode/inspection/issue/cleanup steps, driven by the real Scenes invoker. The public Assets `Task` signatures remain facades over the same lower ownership, not another read implementation.
3. Separate resource-consumption, result-adoption, registration and **actual caller-return** interests. A ready lower result enables consumption, not reuse of the request or refund of its claim.

The [original-P/cache proposal](m03b-scene-original-p-cache-and-assets-return-rectification.md) owns the existing P/cache/receiver partition; the [recovery-delivery proposal](m03b-scene-original-store-recovery-delivery-ownership.md) owns the proposed original-fault projection. This document supplies their missing original-Store execution bridge and its ordered implementation boundaries. It does not repeat their allocation catalogue or declare those proposals accepted.

Use the dedicated-worker/native mechanism selected in [the file-I/O implementation proposal](m03b-assets-file-io-port-implementation-closure.md), but integrate it into **ordinary** Create/replacement/read/close. Do not import the bounded-v1 options, two-entry registration, scalar public failures or token/context restrictions. The old invoker proposal's `RandomAccess.ReadAsync` edge is not selected here: exporting its `ValueTask` would retain the forbidden engine-authored `Memory<byte>` bridge.

**No full all-outcome route fits the existing strict grant interpretation without explicit owner decisions.** Two counterexamples distinguish the necessary changes:

- With positive but tiny `CpuAssetBudgetBytes`, ordinary opening allocates a stream, hash/parser state and metadata despite having no decoded-wire debit. Repeated replacement can retain many old package leases/generations with zero decoded bytes. Wire/lease counters cannot conserve the Store's complete original lifetime grant.
- Identical tiny Scene bytes can be called with arbitrarily large token/context graphs. A pending raw public Task admits arbitrarily many caller continuations, and an exposed original exception admits arbitrary `Data` mutation and accumulated diagnostic history. There is no interface here that accepts a finite prepaid grant for those descendants and their future growth.

Sections 3 and 5 select the smallest explicit budget/ownership dispositions needed to proceed. They narrow a capacity guarantee, not cancellation, context, fault identity, registration or package selection. If that narrowing is unacceptable, **the compatible alternative requires a separately reviewed bounded caller-ownership protocol/public API change**; a scalar allowance, another Scene array, an unrestricted “application grant”, or permanent normal-success quarantine cannot solve it.

Initial Scene scope is built-in Scene/Prefab **2.0**, EPKG2/native-envelope **2.0**, package `None`, no CPU-resource-preparer. The original Store's opening, replacement and shared I/O must nevertheless retain ordinary package admission and None/Brotli behavior. Unselected/custom typed codecs do not acquire a Scene allocation profile.

## 2. Verified current boundary

Review uses HEAD **`71ed2f0a08a0183f0a2593920c7c83b46eb0083b`**, “Guard Scene cleanup completion ownership”. The bounded input freeze is in section 10. This assignment changes only this proposal; it runs no build, tests, native probe or remote mutation.

In the source tables, `Assets\` means `sources\TerraFX.Assets\`; `Scenes\`, `Entities\` and `Rendering\` mean the corresponding folders under `sources\TerraFX.Runtime\`.

| Current location | Verified fact / required integration |
|---|---|
| `docs\design\02-dotnet-api-memory-and-ownership.md:76,87-121` | No engine-authored Memory/ReadOnlyMemory bridge exception. Dedicated synchronous span I/O or reviewed native async ownership; actual consumption before terminal delivery; original execution faults, independently safe cleanup, charged debt and no implicit retry. The linked Chapter 11 “M03 4.7” definition is absent. |
| Chapter 11 `:39-55,93-103`; Chapter 30 `:3825-3918` | Exact backing/registration/lease semantics and ordinary public Task signatures. Chapter 30 currently charges decoded wire length, not arbitrary codec-managed storage. No current ordinary Store control/worker grant is defined by those counters. |
| `Assets\AssetPackageReader.cs:23-223,583-606` | Ordinary open constructs FileStream, uses `SHA256.HashDataAsync`, reads header/profile/directory/padding, then constructs backing. Offset reads loop on `RandomAccess.ReadAsync(Memory<byte>)`; a positive short read does not end the extent. Cleanup is asynchronous stream disposal. |
| `Assets\AssetPackageBacking.cs:31-53,187-193,300-408`; `AssetPayloadReader.cs:16-37,42-148` | Ordinary backing sets `_boundedNativeFile = null` and uses the Memory-based reader. None still allocates decoded storage; Brotli additionally rents input and uses `AsMemory`. The failed-release root retains reachability, not a grant or confirmed native disposition. |
| `Assets\AssetStore.cs:50-80,238-334,374-563`; `AssetStore.PackageLease.cs:73-209` | Store construction follows opening. Replacement elects one candidate and keeps a pending release failure. Load admission charges existing wire/lease/decode counters; initial decode credit ends before sequential target inspections while value/references remain live. A throwing cleanup in `finally` can skip later accounting or replace the primary fault. |
| `Assets\AssetLease.cs:102-110,140-147`; `AssetLeaseLifetime.cs:77-93`; `AssetPackageLease.cs:95-117,174-208`; `AssetStore.ReadDescriptor.cs:59-104` | Anchored facades acquire a temporary backing reference before lower admission/cancellation checks. It can require final release after refusal without an issued typed lease. Install the recovery/read control before this edge, not after `AcquireLoad`. |
| `Assets\AssetStoreRegistration.cs:13-78,144-174`; `AssetOwnedReadDescriptor.cs:7-90`; `Scenes\SceneSupport.cs:23-68` | The same General registration, complete maps/schema graph and caller codec state remain reachable. Built-in receipts select exact execution, not a pruned registration. Descriptor matching checks Store, backing token, codec/entry/limits and provenance. |
| `Assets\AssetEnvelopeDecoder.cs:14-152,474-537`; `AssetReferenceScanner.cs:116-205` | Ordinary decoding calls allocating `Scan`. Fixed scan functions exist. `DecodeBounded` changes failure projection to scalar facts; reuse its workspace technique, not that projection, for the original route. Inspection checks target schema/registration without running target codecs. |
| `Assets\AssetStore.Bounded.cs:26-120`; `AssetStoreBoundedProvision.cs:101-141`; `Rendering\BoundedStaticMeshAssetDelivery.cs:50-76` | Bounded-v1 rejects cancellable tokens, non-null SynchronizationContext and non-MTA Windows entry; that does not establish arbitrary ExecutionContext coverage. Release refuses after native initialization begins. Delivery has only `ReleaseUnstartedPreclaim`. Production search finds declarations, not callers, for `OpenBoundedMetadata`/`ReadBoundedPayload`. This is not the original Store path. Its provision also exposes ReadOnlyMemory (`:61-63`), so it is not a Chapter 02 shortcut. |
| `Assets\AssetBoundedNativePort.cs:56-131,148-150,861-1025` | There is a separate native candidate with scratch-copy reads and one-shot close facts, not ordinary Store integration. Current Linux selection is x64 only. Its no-token blocking completion loop is not an ordinary cancellation implementation. |
| `Scenes\PreparedScene.cs:61-176`; `SceneLoader.cs:163-203,236-269`; `PreparedScene.Preparation.cs:129-267,400-430,580-912` | The footprint is null; description precedes use acquisition; the constructor creates TCS/fault/callback storage. Public pre-canceled delivery precedes P. Producer subclaims exist, but the supplier-return latch is never cleared. `OnCompleted`, `TrySet*` and callback-local finally do not acknowledge outer dispatch/context return. |
| `Scenes\ScenePrefabExpansionPlanner.cs:143-250,2236-2280` | Root staging follows the await. Prefab `.Value` is now inside insertion cleanup, but a lower owner still must cover read return and failed installation. Keep the exact cache and leases across count/build. |
| `Scenes\SceneExpansionPlan.cs:292-424`; planner `:823-912`; `Entities\WorldSceneStorageReservation.cs:69-104` | Current plan cleanup already preserves its first EDI, rethrows later failure without retry, detaches payload before storage return, and checks receiver `IsDisposed`. Preserve this HEAD's fixes; old proposal statements about that solved repeated-disposal defect are stale. An in-progress Dispose return still is not completed ownership release. |
| `Entities\WorldGuidUseControl.cs:103-171,326-365`; `WorldAdmissionDomain.ScenePreclaimedWorkspace.cs:52-151` | Existing exact-use/World/domain checked transfer and parent return-part retention are real lower mechanisms. They are not permission for Assets to accept Runtime capabilities or for Scenes to mint another grant. |

Chapter 16 `:116-128` and Chapter 30 `:3682-3683` retain exact W/P, preallocation admission and actual-data-only transfer. Chapter 29 `:15-29,51-109` preserves Assets → Core and Entities-owned World authority. Chapter 35 `:28-58,101-129` separates implementation, native/AOT/public-consumer observations and acceptance.

## 3. Explicit owner dispositions to carry into implementation

These clauses are **proposed**, not edits to maintained Chapters 02/04/11/16/30.

### 3.1 Original Store envelope

Let **B0** be the original Create options' `CpuAssetBudgetBytes`. Add one Assets-issued, nonrenewable original lifetime ledger before external acquisition. Its service-owned provision is an earmark **inside B0**, never a new budget. It covers bootstrap/shared anchors, worker/native controls, all exact generations, opening/request/return controls, engine-owned protected faults and unreleased cleanup.

Keep the current decoded-wire/count/decode checks independently intact. For conservative admission, the existing live-wire debit plus service-owned earmarks must fit B0; a P-funded allocation is not charged again as an Assets physical allocation. The wire debit remains a quota proxy, not proof of managed ownership. This expressly reduces available ordinary wire headroom by the new control earmark; do not conceal it under an existing constant.

Replacement options still select their applicable existing wire/decode/count limits and exact registration. **They do not issue another lifetime purse, enlarge B0, refinance old debt, or borrow Scene P.** Candidate admission includes current/older/failed/returning debt; insufficiency uses the existing faulted-Task capacity channel before acquisition and leaves current selection intact. Preserve single-replacement and unresolved-pending-release refusal.

This is the minimum capacity-policy delta: without it, whole-Store conservation is impossible. If owners require the existing wire headroom and generation-retention/admission behavior unchanged, they must instead define a genuinely funded separate public Store provision; none exists now.

### 3.2 Narrow application-protocol ownership boundary

Select an explicit exception to the strict transitive-grant interpretation:

- An application-provided token source, captured context values/notification bodies, prebuilt public registration/codec state, caller-added Task observations and application mutation/history of an exposed original exception remain **application-owned borrowed graphs**. Store/Scene retains positive, bounded engine interest/holder storage, not ownership of those arbitrary descendants.
- Public response storage transfers to the application at the actual language-level return/delivery boundary. A producer may retain its funded completion/access interest in that same Task afterward; transfer of delivery storage does not discharge the producer.
- Standard CLR storage/work that is variable solely because of those application protocol graphs—caller continuation dispatch, context-notification effects, and original-exception diagnostic propagation/capture of application history—belongs to that explicitly excluded application-protocol domain. This applies even when CLR allocation occurs during standard public delivery. It is a **specified capacity-scope exception**, not an assertion that these bytes fit B0/P or have a hidden prepaid grant.
- Engine-created owners, context adapters, Task/TCS shells before transfer, bounded fault/outcome holders, fresh engine diagnostic payloads, native controls, queues, engine continuation wrappers and all engine-only dispatch/return tails still require complete positive original provision. “BCL” or “external callback” is not a blanket exemption. No engine helper may allocate per caller continuation or hide a resource in the excluded graph.

Thus the qualified guarantee would cover the complete **engine-owned** graph/capacity and real resource consumption, not the unrestricted application's total reachable heap. Identity/context/token behavior remains unchanged; callers are not restricted to `None`, empty ExecutionContext, immutable exceptions or a two-codec registration.

This is a consequential owner-contract decision. The current recovery proposal instead requires an independently prepaid transfer of the complete arbitrary graph; **this clause explicitly replaces that requirement only at the listed application protocol boundaries**. It must be reviewed as a narrowing, not described as existing borrowing authority. If declined, no finite all-outcome footprint with the current unrestricted signatures is available. Keep that exact blocker rather than inventing an unbounded owner.

Engine-produced semantic/cache data is **not** exempt. Its original P/lease/receiver ownership and actual consumer end remain mandatory. Nor does this exception authorize disposing input/native storage while a callback, decoder or application-authorized lease consumer uses it.

### 3.3 Recovery and entry delivery

Use section 4 of [the recovery-delivery proposal](m03b-scene-original-store-recovery-delivery-ownership.md#4-proposed-chapter-11-recovery-delivery-closure) to close Chapter 02's missing Chapter 11 recovery definition, except for the explicitly different application boundary above. Preserve original exception objects, token and diagnostic context; retain first distinct cleanup plus bounded scalar additional-failure facts. Do not substitute scalar failures from bounded-v1.

Implement Chapter 02's required synchronous owner/argument/mode validation and ordinary pre-canceled Task channel. Current ordinary `async` Create/load/admission wrappers capture some usage errors into Tasks and some lower checks follow pre-cancellation. Extracting fronts is therefore **an explicit repair of a source/contract discrepancy**, not a behavior-free rewrite. Add disposed/invalid/mode/pre-canceled precedence tests; do not introduce an undocumented owner-specific exemption. The native-I/O migration must not accidentally choose exception timing by whether a helper contains `async`.

## 4. Concrete lower controls and package lifecycle

Proposed names below are private implementation responsibilities, not new public contracts or qualified byte sizes.

| Control / owning location | Exact retained responsibilities |
|---|---|
| Original ledger in new `Assets\AssetStore.OriginalOwnership.cs`, attached to `AssetStore` | Immutable B0/selected target profile; original shared-provision interest; checked nonwrapping issuer/generation/request identities; unique generation/debt links; all service-owned reserved/spent/returning amounts; close-admission and retirement progress. No World/Scenes fields, delegates or funding callbacks. |
| Generation/opening progress in `AssetPackageBacking.cs` | Acquisition-free construction; exact original ledger, path/digest/options/registration interests; immediately installed native handle/descriptor; open/hash/parser progress; entries/provenance/read limits; reference and native-use counts; reserved final-close node; actual one-shot close outcome, fault and return/debt state. Never retrofit this around an already acquired FileStream. |
| `Assets\AssetPackageIoWorker.cs` | Static nonflowing thread entry; private intrusive FIFO and original ledger interest; one 65,536-byte scratch owner; Windows event/initialized OVERLAPPED; exactly one native read at a time; active request and shutdown/join/return provision. No codec, Scene traversal or public Task completion on this worker. |
| `Assets\AssetLoadOperation.cs` / opening operation | Acquisition-free constructor, source/descriptor/receipt, temporary backing reference, each ordinary credit fact, typed parent/target buffers/ranges, scan storage, semantic value, lease/lifetime/state candidates, selected outcome/token/fault/cleanup, single-consumption lower request and independent installation/return interests. Selected read storage is attributed to its original P claim; ordinary public read adapters use an independently admitted Assets operation partition. |
| Existing Scenes root/invoker | Dedicated lower-operation slot and destination election, separate from pending/phase owners; exact C_op/P claim; entry/resume/registration/context/publication/return cohort. Only Scenes accesses `ScenePreparationFunding` and Entities capabilities. |

No per-request queue node, emergency cleanup list or waiter is allocated after admission. Nodes are embedded in positively funded operation/generation controls; queued, active, ready-unconsumed and returning membership all counts. Inspections and zero-credit temporary-reference refusal are not bounded by `MaximumLiveAssetLeases + 1`. Derive membership from positive byte claims and actual call flow, not a guessed lease-based queue length.

The private lower request is **one complete checked extent**, not a new Task/source for each short native read. Short-read offsets and scratch copies advance inside the worker's synchronous helper. Embed its single-consumption completion core in the installed control; expose only a private typed ticket carrying the exact control and nonwrapping full request generation. Complete it outside gates with asynchronous, nonflowing wake dispatch; the I/O worker must never inline semantic progress. The invoker owns logical context application. A reused BCL source-version token alone is not its identity. The invoker validates the full ticket before registration/consumption; reset is forbidden until native access, publication-call return, consumption-call return and all registration/dispatch interests have joined. Native error/cancel facts can be a lower reply value; public delivery still creates/preserves the original conventional exception, not a scalar substitute. Any managed helper exception already created is retained as the original object. No public raw ValueTask or owner-specific Runtime callback/funding upcall is added.

Private ordinary responsibilities are `Acquire`, `QueueExactRead`, `ConsumeReply`, decode/inspection advances, `StageLease`, exact destination acceptance and `Cleanup`; the enclosing caller records each returned boundary. All default/progress/native structures are explicitly initialized, including stack/native storage under source `SkipLocalsInit`. A native byte count authorizes only that initialized returned range.

For ordinary Brotli payloads, use an exact operation-owned input buffer instead of silently inheriting `ArrayPool.Shared`'s retained cache; preclaim its 65,536-byte capacity, overflow byte and actual decoder workspace. Preserve the existing decompression/length/trailing-input checks. Reuse owned target/scan capacity between sequential inspections only after their actual consumers and returning calls end; do not create one uncharged buffer/completion source per target or native chunk.

### Opening and replacement sequence

1. Validate usage/options/mode and pre-cancellation without acquiring a resource. The separately defined public-protocol boundary covers unadmitted canceled/usage delivery; do not debit a future P/B0 or register a token callback.
2. Elect the original value-only bootstrap claim before creating its first owner, shared anchor, worker, TCS or path copy. The first issuer prepays identified shared runtime/dispatch provision from its B0; subsequent issuers retain a compatible interest, not an assumed free static service. Failed construction before installation acquires no native resource and preserves the constructing caller's original claim through unwind.
3. Construct and install the unpublished Store ledger and acquisition-free opening backing. Preclaim path conversion, digest copy, parser/hash/native/startup/failure/return peaks. Start the worker with an installed startup interest; allow file acquisition only after the initiating ordinary caller observes start return. Ambiguous startup retains its original control/claim.
4. Worker prepares/opens the **same selected file**, installs its result before any query, obtains type/length, then hashes the complete file through bounded scratch chunks and BCL incremental SHA-256. Check expected digest before layout errors as now. No `HashDataAsync(FileStream)` or second FileStream/SafeFileHandle acquisition.
5. Read/admit EPKG2 2.0 header, Foundation profile, EIDX directory and every zero-padding range through the same port. Reuse existing validation and exact-count entry construction; retain full directory membership, not the two-entry bounded parser. Preclaim each phase before allocation, including old/new array overlap and unreturned helper storage. Bound file extent separately from allocation.
6. Create/accept immutable entries, provenance and limits only after complete validation. For Create, publish that original Store once. For replacement, recheck cancellation and disposal at the existing switch boundary, atomically make the exact candidate current, and retain the displaced generation before releasing its owner reference. Cancellation after the switch never rolls it back.
7. Cleanup the candidate on prepublication failure and the displaced owner after publication. A failed candidate close and a displaced/older generation failure retain separate ledger entries. The existing pending-release policy still blocks another replacement when applicable; ordinary leases to older backings remain valid.

Opening has concrete raw bounds: 64-byte header, Foundation profile at most 65,536 bytes (`sources\TerraFX.Core\EbinReadLimits.cs:51`), directory at most `64 + 32 * 131072 = 4194368` bytes, at most 131,072 entries and at most 15 bytes per alignment gap. Add actual managed entry stride/capacity, hash/provider/parser/native/control and return storage; wire stride is not a CLR charge. For per-asset/storage limits use the actual validated options/descriptor values: current `AssetStoreOptions.Validate` checks nonnegativity only, so Chapter 04 hard option ceilings are not observed source enforcement.

Native path handling must preserve every path shape admitted by `AssetStoreOptions`, including Unicode and supported absolute/UNC/long paths. Do not reuse bounded-v1's ASCII/path-length grammar. Preserve conventional file-error types/HResults and path context; qualify mapping against ordinary admission. A native adapter constructs the original managed failure once from its raw operation facts; subsequent layers retain that object rather than constructing substitutes.

### Port operations and close

- **Windows:** same read-only/share-read/open-existing/random-access selection, private overlapped file/event, bounded ReadFile scratch input. Distinguish immediate completion, nonaccepted failure and pending I/O. After cancellation, request `CancelIoEx` at most once and still obtain the actual completion. Event signaling, cancel return/`ERROR_NOT_FOUND`, observation timeout and reply readiness are not completion proof.
- **Linux:** preserve selected regular-file/seekable/error and sharing behavior using reviewed target-specific open/type/length/pread bindings, not the private .NET PAL. A blocking `pread` cannot be canceled by closing its descriptor; retain input/backing until return. Retry read EINTR only under the selected syscall policy. The existing x64 stat declaration is not an arm64 ABI.
- Check the original token before issue and at current logical checkpoints; worker-owned polling can avoid a cancellation registration. Windows finite wait quanta are a polling mechanism, not a deadline or latency promise. Do not let this physical policy alter the public commit/cancellation boundary.
- On final reference/native-use quiescence, issue the generation's already reserved close once. Store the exact return/error and consumed/consumed-with-error/retained/indeterminate classification **before** other cleanup or accounting. Never retry a consumed or uncertain handle number. Reuse the candidate's one-shot technique only after target-specific disposition rules are verified; do not generalize Linux EINTR or a failed Windows CloseHandle into success.
- Attempt safe hash/event/control siblings independently; retain uncertain file/scratch use live and nonterminal, stopped-but-unreleased debt charged. Public Store disposal closes admission and releases only its owner reference; it must not stop the worker while an older lease, temporary reference or returning request can need it.
- Last-generation retirement signals stop and uses a separately preclaimed **non-worker** caller to join the actual thread outside gates. The worker never joins itself or waits for the consumer needed to accept its reply. Finalizer abandonment uses a preinstalled nonblocking retirement handoff; no new owner/allocation/retry in the finalizer. Idle worker state must not strongly root the public Store/backings forever. Qualify partial-start, idle-finalizer, final-close and shutdown races before selecting the profile.

## 5. Grant conservation across generations and Scene operations

Use one ledger entry per exact generation. Current/candidate/displaced/failed are residency/selection facts; native close and returning are separate axes, not reasons to charge the same backing twice.

```text
B0 = free original Assets capacity + outstanding original Assets provision

outstanding provision includes:
  issuer/shared/worker anchors
  each unique current/candidate/displaced/failed generation's Keep
  opening/request/public-adapter provision
  protected native/cleanup/fault/actual-return provision

existing live-wire debit + service-owned earmarks <= B0
existing current/backing-sensitive wire, decode and lease checks also hold
```

Reservation-to-spend changes membership, not entitlement. Returning or failed portions cannot finance a candidate or next request. A successful replacement merely moves “current” identity; it does not refund the old generation. A native file consumed with error does not erase remaining accounting/fault/control debt. Do not release legacy lease/wire credits on an unsuccessful release unless its independently recorded safe accounting step actually completes; never consume the backing reference again to retry accounting.

`Keep(g)` includes the backing/metadata/limits, native file/close node, exact independent registration interests and generation retirement provision. A prebuilt public registration is borrowed only under section 3.2's explicit decision; the Store-created retained adapters/maps and any transferred engine storage still need real funding. Do not prune unused codecs. Metadata retained by a descriptor or semantic value must retain its charge or transfer its exact surviving subset even after file close.

For Scene:

```text
P = W = original MaximumPreparationBytes, unchanged
P = F + X + T
U = 256 + F + X

C_op inside X =
  installed lower operation + upper invocation/context/registration
  parent/scan/semantic/automatic-inspection/issue peaks
  cleanup/fault/installation/actual-return provision

shared Store worker/generations belong to B0, not another P debit
application-protocol exclusions are only the explicit section 3.2 boundary
```

Before description/construction/miss, elect and claim the entire C_op while prior cache D stays charged. Do not read first to discover a smaller grant. Parent envelope length bounds parent storage; automatic targets use that exact backing's admitted target maximum. Empty input, an empty package or zero issued lease credits still require positive owner/control provision.

Only exact installed lease/value/cache/plan D and receiver-owned provision move through the existing Entities-issued preclaimed transaction. P scratch, faults, native-use/registration and old caller-return provision do not accompany D. The same cache/backing survives count/build. Current `TryReserveProducerStorage` requires the exact pending owner and no unrelated claims: implement the partition-aware adapter already proposed, rather than relaxing that lower invariant or transferring a whole builder phase.

Do not hardcode B1 shell measurements, the old builder measurement, `256 * prefabCount`, or a nominal fault-pair size as the full peak. Freeze the final field/array/generated/native membership and derive checked finite functions. Unknown runtime/context/return terms make a profile unavailable.

## 6. Read, public delivery and actual return

### One installed Scene read

1. Original P claims construction before the empty Assets control factory. Scenes attaches that exact control in its dedicated slot before start. Construction/attachment failure remains with the constructing caller; pending/phase destination slots are untouched.
2. The Assets control acquires the source facade's temporary backing reference inside its recorded progress, preserving source/lifetime/backing/Store lock order and required validation precedence. Then revalidate exact descriptor, codec receipt, entry and ordinary counters. On refusal, the same control owns temporary-reference release even with no typed credits/result. A stale current descriptor refuses; anchored reads never retarget.
3. Stage typed buffer/range, checked offset/length, original backing/native-use interest and request identity before queueing. Worker performs bounded native reads and synchronous scratch copies. No span survives the synchronous worker call; no Memory field/parameter/slice is introduced. Advance positive short reads, reject zero before extent as EOF, and copy only bytes whose completion is confirmed.
4. Real invoker consumes the request outcome once, after actual input use ends; it observes the consumption method returning/unwinding before another Assets step. Assets decodes using the existing typed codec and diagnostics. Add an ordinary-status caller-buffer decode overload using Count/ScanInto/CopyUniqueReferences while preserving `Decode`'s structural/status/diagnostic mapping and first-occurrence reference ordering.
5. Release only the initial decode credit at its current checkpoint. Keep semantic value/reference storage under C_op. Inspect automatic targets sequentially through the same port: check missing ID and required package codec before reservation/read, then envelope/schema ID/major/exact registered-codec membership. Standalone reference matching retains its existing false/error/minor rules. No target codec is executed for inspection.
6. Construct lifetime, handle and state candidates under the installed owner, keeping backing/credits there until construction and exact destination adoption succeed. Preserve current successful `CreateLease` ordering and package-pin carrier-before-reference ordering. `.Value`, context comparison, insertion or adoption failure leaves the donor/lease and original failure protected through independent cleanup.
7. Transfer exact retained ownership/charge once. Read/inspection/input/registration/fault and enclosing-return interests stay with their real owners. A cache count increment, returned lease or ready Task cannot retire them.

### Caller and context boundaries

Replace the selected internal Assets **and transitive planner** Task producers with operation-specific ordinary advances at the actual invoking call sites; do not retain opaque producers and add a second observer. Keep traversal/cache algorithms. `SceneLoader.StartPlanPreparation`, planner root acquisition/`PrepareTakingLeaseCoreAsync`, Build/count/selector resumes and `GetPrefabAsync` must participate in one real invoker closure, not just the outermost awaited load.

Select one precreated root wake wrapper with checked running/pending-wakeup election and a synchronized exit recheck. Reserve its registration/invocation interests before publishing a lower ticket. The wrapper calls the ordinary Scene advance within the owned context scope; only **after that scope returns** may it join that returned invocation. Temporary-claim handback additionally requires every other registration/consumer/return interest to have joined. It drops old ticket/result/context aliases before another operation can enter the slot. The root publication/outer-return cohort remains separate, so an inline wake or racing public publication cannot retire the still-registering/enclosing caller. This wrapper replaces the outcome-only `OnPlanPrepared` path for the selected lane; it does not add an observer to it.

| Call boundary | Actual observer and handback rule |
|---|---|
| I/O worker loop → open/read/copy/hash/close helper | Loop observes helper return/unwind and records actual native consumption separately. Its producer/publication suffix stays under B0 worker provision. |
| Invoker → lower registration/consumption/Assets advance | Enclosing invoker observes each ordinary return/unwind; keep separate registering, queued/running and returning interests, including inline callback during registration and uncertain publication when registration throws. |
| Scenes wake/entry wrapper → semantic invocation within context | Wrapper encloses **both** semantic call and context restoration, then observes return. The semantic body never writes its own “caller returned” acknowledgment. |
| Invoker → destination adoption/cleanup | Actual returned call establishes that invocation's end. Donor retirement additionally requires exact acceptance, independent cleanup and all old result/task/context aliases ended or transferred. |
| Public facade/publisher | Entry caller observes `Start`/factory return; actual publisher caller observes `TrySet*` return/unwind. Neither acknowledges outer dispatcher return. Public Task observation is application protocol under section 3.2, not another engine producer join. |
| Non-worker retirement caller → thread termination | Check actual Join/termination and the selected runtime/native control suffix, then retire B0 worker provision; `IsAlive`, Task terminality and worker exit bits are insufficient. |

At a genuine suspension preserve the current logical ExecutionContext, including suppressed flow, and avoid capturing arbitrary SynchronizationContext. Use a nonflowing wake only to enter the owned invoker; explicitly reproduce the existing async isolation/context boundaries. `ConfigureAwait(false)` is not affinity, context suppression or return acknowledgment. Preserve inline completion instead of forcing every call to yield. A context notification's target-runtime fail-fast/nonreturn behavior must not be converted into a recoverable success or synthetic Task fault.

The caller-side handback is the last access to the retired child claim. Any remaining wrapper/gate-exit/dispatch/runtime tail must either have proved no access to the retired graph or retain an **identified, already positively funded** parent/lower return cohort. If a cohort must move, accept its exact graph/charge before donor retirement through the existing owner transaction; never borrow the successor operation's C_op. Final whole-P release still uses Entities' exact-parent return mechanism, not an Assets upcall.

This last suffix is a required implementation/profile proof, not a new “returned” flag. If the selected stock dispatch cannot expose the needed enclosing boundary, retain its exact original provision and implement the explicit caller wrapper before enabling the route. Do not clear `_supplierProducersUnjoined` until the full participating caller/context/registration DAG is joined. Callback finally, clearing references, polling and a post-hoc continuation do not satisfy it.

Public `CreateAsync`, `LoadAsync`, anchored loads, replacement and DisposeAsync keep their signatures and ordinary observer contracts. Their Assets-owned adapters use the same lower controls with independently admitted operation/return provision, without a World. Original errors travel in owned state rather than through repeated intermediate Task recaptures. Section 3.2 is the explicit public storage/borrow boundary; it never permits early Task terminality while resources remain in use.

## 7. Failure, cancellation and cleanup precedence

| Schedule | Required result and retained obligations |
|---|---|
| Invalid/disposed/mode misuse plus pre-canceled token | Required usage exception first, synchronously; valid ordinary pre-cancellation returns canceled Task with the original token, no admitted I/O. Front-end public delivery is not financed by a later preparation. |
| Acquired temporary reference; Store/descriptor/capacity refusal | Capture original refusal before cleanup. Control owns possible final close with zero issued typed credits. Preserve the original primary; record distinct cleanup separately. |
| Read/decode/inspection/issue failure plus cleanup failure | Original primary remains first, distinct first cleanup second in the reviewed Task fault projection. Attempt every independently safe release/accounting sibling. Identical samples are not duplicated; additional failures retain bounded scalar facts, not a growing list. |
| Cancellation before publication | Finish actual accepted input/decoder/consumer use and safe cleanup. With successful cleanup deliver the original token; cleanup failure faults cancellation with that original cleanup sample. Observer `WaitAsync` cancellation changes no lower ownership. |
| Candidate failure/cancellation before atomic switch | Current package remains usable. Failed candidate native/control debt stays in B0; no task claiming rollback while a consumer still runs. |
| Failure after successful package switch | Preserve the new selected content set/digest; fault retirement through the existing channel. Never report failed publication/rollback or implicitly reselect old current. |
| Backing reference consumed; accounting or control release fails | Persist consumption fact before the next step; retain accounting/control-only debt and original failure. A repeated close reports the same outcome without another physical attempt. |
| Native completion/close/registration/dispatch outcome uncertain | Keep live consumers nonterminal; stopped unresolved debt remains charged. Do not refund an uncertain registration, reuse scratch/node, or manufacture a success-shaped scalar outcome. |
| Repeated plan/lease/backing/Store close | Preserve original failure identity and reviewed EDI behavior, no implicit retry. An in-progress/no-op Dispose return does not acknowledge completion. Preserve current plan payload-detachment and receiver-completion checks. |

Exception-specific CLR history is governed only by the explicit section 3.2 decision. Fresh engine failures, fault holders and postpublication cleanup debt are otherwise fully funded. Do not truncate/clear `Data`, clone/stringify faults, synthesize small cancellation faults, or change public aggregate ordering to obtain a bound.

## 8. Ordered implementation batches

Each batch implements real production responsibilities and focused regressions; no test-only producer, separate qualification runner or operational gate override.

| Order | Files/call sites and deliverable | Completion boundary |
|---|---|---|
| 1 | Carry section 3's tentative dispositions into the owning contract review; close exact selected target/runtime/storage membership. In `AssetStore.OriginalOwnership.cs`, implement scalar original issuance and unique generation/operation/return ledger; integrate options/current/replacement admission in `AssetStore.cs`. | Exact-fit/short admission and old/candidate/failed/returning conservation; no file/worker acquisition before grant installation. No public budget default or hard ceiling silently changes. |
| 2 | Implement `AssetPackageIoWorker.cs`, acquisition-free backing/open progress, native helper adaptation, and ordinary opening validation in `AssetPackageReader.cs`. Route `AssetStore.CreateAsync` and `ReplacePackageAsync` through it, including documented entry-front correction. | Same-file digest/layout/None/Brotli/padding, original path/error/cancellation semantics; start/failure/finalizer/close and actual worker-return ownership. The existing bounded-v1 route is not activated or relabeled. |
| 3 | Implement installed read/inspection/issue/cleanup controls in `AssetLoadOperation.cs`; integrate `AssetStore.ReadDescriptor.cs`, `AssetStore.PackageLease.cs`, `AssetPayloadReader.cs`, lease/lifetime/package-lease acquire/release and package-pin paths. Add ordinary-status workspace decode in `AssetEnvelopeDecoder.cs` using the existing scanner. | Original Store source has no Memory-based I/O; temporary-ref refusal, target inspection, ordinary counters and original faults all use the installed owner. Public generic/custom behavior stays separate from Scene profile selection. |
| 4 | Integrate original-P slot/construction/return claims and actual invoker in `PreparedScene.cs`, its preparation partial, `ScenePreparationFunding.cs`, `SceneLoader.cs`, planner root/build/count/selector/`GetPrefabAsync` edges. Preserve the same cache and current cleanup fixes. | No unowned read/result/installation interval; real returned semantic/context/registration calls join. Protect original/returning claims across inline and suspended progress. |
| 5 | Complete exact retained receiver adoption using `World.ScenePreclaimedWorkspace.cs`/the existing domain transaction and partition-aware Scenes adapter. Integrate public-delivery and retired-call ownership from sections 3/6. | D moves once; C_op scratch/debt/return does not. Closed World/domain, failed adoption and repeated cleanup preserve exact attribution; no extra use or refund/reacquire gap. |
| 6 | Derive/corroborate final engine-owned numerical profile and seek owner disposition of the **exact** contract/source/generated/native bytes. | Debug then Release behavior/allocation/return evidence; supported native ABI/AOT and public consumers remain separately required. Only the fully closed selected profile can later replace the null footprint. |

These batches permit dependency-ready engineering under tentative decisions without presenting an unavailable term as zero. They do not authorize changes outside this assignment; implementation is subsequent work.

## 9. Focused verification and fail-closed gate

Use `global.json`'s SDK, existing root/folder props, central packages and NUnit/VSTest. No pin/tooling duplication. After a coherent implemented boundary, combine related owning-project selections and run Debug before Release:

```powershell
dotnet test .\tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj --configuration Debug --filter "FullyQualifiedName~AssetPackageReaderTests|FullyQualifiedName~AssetStoreTests|FullyQualifiedName~AssetPackageLeaseTests|FullyQualifiedName~AssetStoreAllocationTests"
dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "FullyQualifiedName~OriginalStore|FullyQualifiedName~LoadsSceneAndPrefabThroughRegisteredCodecs|FullyQualifiedName~ExpandsNestedPrefabsWithAssetStoreLeasesAndStableOccurrences|FullyQualifiedName~RetainsRealPrefabLeasesWithinCacheGrowthAllowance|FullyQualifiedName~RejectsPrefabCycleDuringExpansion|FullyQualifiedName~RepeatedPlanDisposeRethrowsCleanupFailureWithoutRetry"
```

Run the same two selections with `--configuration Release` after Debug. `OriginalStore` denotes subsequently added behavior tests in the existing clean Scene test area, not a presently executed case. Confirm those new cases individually selected/executed, not just a nonzero old baseline. Retain fresh exits/counts/TRX under `artifacts\tst\<Configuration>`; a class/layout test or internal planner pass does not establish the public preparation path. Do not edit the five protected dirty tests.

Focused cases must cover:

- Literal EPKG2/profile/directory/padding/checksum and existing None/Brotli/reference diagnostics; malformed-but-correctly-hashed input; original path and file-error projection.
- Acquisition-free refusal, partial start/open/hash/parse/issue failure, zero-credit final temporary-reference release, old/candidate/displaced/failed/returning overlap, and exact-fit/one-byte-short peaks with independently derived nonzero baseline.
- Original token/error identity and primary/cleanup ordering, usage precedence, cancellation while queued/native-active/inspection-active, replacement/disposal races and no retry/double accounting.
- Default/nondefault/suppressed ExecutionContext, no SynchronizationContext capture, inline completion/registration overlap, uncertain registration, context restoration/reentrancy, result `.Value`/insertion/adoption failure and actual caller return before claim reuse.
- Exact cache/receiver transfer and cleanup debt across World/domain close; metadata/registration interests after file close; finalizer abandonment, last older lease after Store disposal, worker shutdown/join and late return.
- Caller Task continuation/fault mutation counterexamples explicitly exercising the proposed **scope exception**, not asserting a finite bound on those application graphs. Measure engine holder/wrapper/cohort/capture provision and identify every remaining target-specific runtime tail.

Add only focused tests for implemented behavior in the existing Assets/Scene files. Managed fault schedules and allocation/layout observations are not real native failure or ABI evidence. No copy of the production decoder is an expected-value oracle; use owner-reviewed literal fixtures and independently derived expectations.

**Target limitations:** current WSL is Ubuntu 26.04 without a C compiler; no Linux ABI/native probe is available. Do not rerun unchanged probes, install a toolchain for this proposal, infer arm64 from x64 fields, or claim Linux/native/AOT results. Retry the Chapter 35 native/AOT gate only on the actual target with its native compiler/headers/libc, exact bindings and safe real read/cancel/close stimulus. Windows x64/arm64 and Linux x64/arm64 remain distinct qualification inputs. NativeAOT is published/executed on the target OS using [Building.Consumers](../project/Building.Consumers.md); managed/JIT compilation and B1 declarations do not replace that evidence. Optional Runtime Async compilation also needs its own applicable generated/return evidence.

**Crisp gate:** the original-Store Scene profile is unavailable if any original B0/P grant, proposed ownership-scope disposition, retained membership, native actual-completion/disposition, recovery projection, engine caller/context/dispatcher return, or exact receiver transfer remains unresolved. It is not a zero-byte/fallback/default-token profile. This proposal and partial tests never open it.

Keep `PreparedScene.QualifiedFootprint = null`, the CPU-resource-preparer and Rendering bundle-settlement refusals, bounded-v1 token/context restrictions and CLI inactivity unchanged. Scene-dependent nonempty restore, public Headless/native rendering, positive native diagnostics and other M05 consumers still need their actual producer and Chapter 35 observations. Continue unrelated dependency-ready M03-B/M05 implementation under its own gates.

## 10. Bounded evidence freeze and preservation

Freeze UTC: **2026-11-21T06:12:38.3217003Z**. This is the proposal's bounded inspection input set, not a repository inventory, executable profile or acceptance pipeline. It excludes this output and contains no asserted runtime/native observation. Before applying a later disposition, verify HEAD, these exact members/raw hashes and dirty preservation; changed inputs require reassessment.

Each group digest hashes UTF-8 without BOM/trailing newline over ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines joined by LF. ALL is the unique union.

| Group | Members | SHA-256 |
|---|---:|---|
| Authority | 11 | `64a8e2cebea8778021fedf4d05b4f6e7c79b4fe11f2fc16fa0c5b0f1baa0e1b7` |
| Guidance | 5 | `f1e95e8f775a406228cead1669786b990bf619283fca7546f51bec9d61f9e14f` |
| Build | 9 | `6c569794cf49557974e1a85b734f6aa687f82bdefaab95de264d16acb072a714` |
| Assets | 22 | `b8f4d1c85c3ad2461e5535734e932355880483bc57364944e2ff8f3c120394e0` |
| Foundation | 1 | `6d61f43c7454ad6ca58a95c0b22ab70ee551219578759583783f3c9b85c8010f` |
| Runtime | 17 | `3e62cdd14f95de08fec60d93fb322706bdf462ba1bf02db62e6f07c03d0baf94` |
| Tests | 4 | `f4d9f319e3f365ca8c52b81f1053de126b31b82ff0468cc337856a82178f8190` |
| Protected | 5 | `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162` |
| ALL | 74 | `cb86edf418f093fd534aae56a7916f0569b6df45ed77287ab90c8930981de00b` |

Evidence members:

```text
[Authority]
docs\design\02-dotnet-api-memory-and-ownership.md
docs\design\04-capacity-and-workload-profiles.md
docs\design\11-assets-import-streaming-and-packages.md
docs\design\16-scenes-transforms-and-cameras.md
docs\design\27-state-serialization-replay-and-recovery.md
docs\design\29-project-setup-build-and-deployment.md
docs\design\30-public-api-review-surface.md
docs\design\35-implementation-roadmap.md
docs\project\Roadmap.md
docs\project\Building.md
docs\project\Building.Consumers.md
[Guidance]
docs\proposals\m03b-scene-original-store-recovery-delivery-ownership.md
docs\proposals\m03b-scene-original-p-cache-and-assets-return-rectification.md
docs\proposals\m03b-assets-file-io-port-implementation-closure.md
docs\proposals\m03b-scene-actual-invoker-return-closure.md
docs\proposals\m03b-scene-assets-original-p-operation-and-return-closure.md
[Build]
.github\copilot-instructions.md
.github\workflows\ci.yml
global.json
Directory.Build.props
sources\Directory.Build.props
sources\TerraFX.Assets\TerraFX.Assets.csproj
sources\TerraFX.Runtime\TerraFX.Runtime.csproj
tests\Directory.Build.props
tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj
[Assets]
sources\TerraFX.Assets\AssetStore.cs
sources\TerraFX.Assets\AssetStoreOptions.cs
sources\TerraFX.Assets\AssetStore.ReadDescriptor.cs
sources\TerraFX.Assets\AssetStore.PackageLease.cs
sources\TerraFX.Assets\AssetStore.PackagePin.cs
sources\TerraFX.Assets\AssetOwnedReadDescriptor.cs
sources\TerraFX.Assets\AssetPackageBacking.cs
sources\TerraFX.Assets\AssetPackageReader.cs
sources\TerraFX.Assets\AssetPayloadReader.cs
sources\TerraFX.Assets\AssetLease.cs
sources\TerraFX.Assets\AssetLeaseLifetime.cs
sources\TerraFX.Assets\AssetPackageLease.cs
sources\TerraFX.Assets\AssetStoreRegistration.cs
sources\TerraFX.Assets\AssetEnvelopeDecoder.cs
sources\TerraFX.Assets\AssetReferenceScanner.cs
sources\TerraFX.Assets\AssetStore.Bounded.cs
sources\TerraFX.Assets\AssetStore.Bounded.Native.cs
sources\TerraFX.Assets\AssetStoreBoundedProvision.cs
sources\TerraFX.Assets\AssetStoreBoundedBootstrap.cs
sources\TerraFX.Assets\AssetBoundedNativePort.cs
sources\TerraFX.Assets\AssetBoundedReply.cs
sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
[Foundation]
sources\TerraFX.Core\EbinReadLimits.cs
[Runtime]
sources\TerraFX.Runtime\Scenes\PreparedScene.cs
sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
sources\TerraFX.Runtime\Scenes\SceneLoader.cs
sources\TerraFX.Runtime\Scenes\ScenePreparationFunding.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
sources\TerraFX.Runtime\Scenes\SceneSupport.cs
sources\TerraFX.Runtime\Scenes\SceneAssetCodec.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabAssetCodec.cs
sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.ScenePreclaimedWorkspace.cs
sources\TerraFX.Runtime\Entities\WorldGuidRegistry.TerminalAnchor.cs
sources\TerraFX.Runtime\Rendering\BoundedStaticMeshAssetDelivery.cs
sources\TerraFX.Runtime\Rendering\BoundedStaticMeshAssetValidation.cs
sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
[Tests]
tests\TerraFX.Assets.Tests\AssetStoreTests.cs
tests\TerraFX.Assets.Tests\AssetStoreAllocationTests.cs
tests\TerraFX.Assets.Tests\AssetPackageReaderTests.cs
tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.cs
[Protected]
tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
```

Schema input hashes:

```text
6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a  sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a  sources\TerraFX.Runtime\Scenes\SceneSchemaCatalogue.cs
```

The only pre-existing dirty files are the following. Preserve their raw bytes; they are not evidence of a test pass:

```text
e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
caa6610cf55f8fffa077745fd96baa45fcecfed2fd051cc763901aeed5500239  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteCapacityTests.cs
8e65b71b5ac1e0ef9d1f47aa56963054d091df8c229e74ddbb1cae37bb59ab60  tests\TerraFX.Runtime.Tests\Scenes\RenderSpriteComponentTests.cs
abaeea4d0f6be941af73e640b7fce41ec753d2199654343fa4668d54ec20fc6a  tests\TerraFX.Runtime.Tests\Scenes\RendererExtractionTests.cs
4c0841a40295a891b68974fddd8078dfd0181c31b35ab0bcb0b5114fe0fd1039  tests\TerraFX.Runtime.Tests\Scenes\SceneAssetStoreTests.EngineHost.cs
```
