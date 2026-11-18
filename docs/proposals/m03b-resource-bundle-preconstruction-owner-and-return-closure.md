# M03-B resource-bundle preconstruction owner and return closure

**Status: tentative rectification for later review. This document selects prerequisite work; it neither qualifies nor enables Scene preparation. Keep `PreparedScene.QualifiedFootprint` null.**

Scope: the actual CPU-extraction resource adapter, `RenderSceneAssetBundle.PrepareAsync`, its planner/Assets/helper dependencies, and their construction, cleanup and return lifetimes. This is not a new asynchronous framework, public API, acceptance identifier, quota, or milestone exit. The user's tentative-unblocker policy permits pursuing the proposed prerequisites, not executing an unfunded producer. This task creates this proposal only.

## 1. Decision

Use the **existing `RenderSceneAssetBundle` itself as the precreated construction owner and eventual retained bundle**. Root it in the existing preparation root before allocating its arrays or invoking its first asset/helper operation. Keep unfinished helper state in that bundle's funded fields, not exclusively in static async machines. Transfer the identified retained partition only after its complete inventory and receiver exist; scratch and invocation/return claims remain with their original issuer.

This makes concrete the next rectification in [World binding-owner retirement fence](m03b-world-binding-owner-retirement-fence.md). It retains the operation-specific owned-progress route selected by the [finite-envelope and return addendum](m03b-scene-preparation-finite-envelope-and-return-addendum.md), rather than selecting a decorator over stock async builders or an unavailable CLR notification. The [workspace](m03b-scene-preparation-workspace-closure.md), [footprint](m03b-scene-preparation-footprint-closure.md), [supplier-return](m03b-scene-preparation-supplier-return-closure.md), and [owner-aware handback](m03b-scene-preparation-owner-aware-async-handback.md) obligations remain.

**No complete numerical J/K or bundle-control charge follows from the present inputs.** The current code does, however, provide a concrete material-reference bound for the inspected native Mesh 1.0 codec: at most 256 slots per decoded Mesh, not one Material per Mesh. The remaining unavailable inputs/capabilities and exact resumption conditions are in section 5.

## 2. Current evidence, not proposed behavior

Source paths below are beneath `sources\TerraFX.Runtime` unless qualified otherwise. Method names identify the frozen bytes in section 7.

| Boundary | Confirmed current fact |
|---|---|
| `PreparedScene.CreatePreparation` | The null-footprint throw precedes `world.AcquireGuidUse`. Neither ordinary preparation nor this bundle producer was executed for this assessment. |
| `RenderSceneAssetBundle.PrepareAsync`, initial phases | `scratchReservation` is a local. Three ID arrays are allocated, then `AtlasEntry[atlasCount]` and `MeshEntry[meshCount]`, before `finalReservation` exists. The first scratch formula contains the ID-array terms, not those entry-array terms or a closed transitive helper inventory. No `SetRetainedOwner` roots this scratch reservation's value graph. |
| Atlas loop | The loaded Atlas lease, helper-retained Atlas lease, page-lease builder/copy, current page and returned page set precede final bundle construction. The outer array receives the page set only after `RenderAtlasPageLoader.RetainAsync` returns its outcome. Inner helper progress is not a field of an independently rooted bundle. |
| Mesh loop | The original Mesh lease, `LoadSkeletonAsync`'s additional retained Mesh lease, optional Skeleton lease and final retained Mesh lease precede `finalReservation`. Material-slot counting occurs after the Mesh entry array has been filled. |
| Material phases | Both material-ID arrays are allocated after a second workspace reservation. Only then is final storage reserved and the Material entry array allocated. A loaded Material, its retained alias or texture set, and the texture helper's current lease/builder/copy add further intermediate state. |
| Final attachment | Binding validation precedes `new RenderSceneAssetBundle`. Only afterward does `ownedStorageReservation.SetRetainedOwner(bundle)` run. The ordinary driver stages the returned resource still later, in `OnResourcesPrepared`. None of these later edges roots earlier partial acquisitions retroactively. |
| Construction cleanup | `cleanupFailed` prevents reservation disposal after a throwing cleanup loop, but can leave later siblings unattempted. Keeping a numeric credit does not establish an independent reference path to its failed partial graph. |
| Existing cleanup corrections | Bundle `Dispose` returns storage **after** child-cleanup blocks; it does not return it from their `finally` clauses. `PreparedAssetsLifetime` now clears `_value` only after successful child disposal. The older finite-envelope descriptions of these two boundaries are stale for these bytes. Logical disposal latches and a repeated no-op still do not establish physical release. |
| Actual adapter | `ISceneExpansionResourcePreparer.PrepareAsync` exposes only `Task<IDisposable?>`. The built-in participant is itself async and awaits the async bundle. A selected participant returning null has still invoked both producers. Only absence of the participant skips the resource role. |
| Actual planner | `PrepareCoreAsync`, `PrepareTakingLeaseCoreAsync`, `BuildAsync`, counting, Prefab lookup, selector traversal and nested Task/ValueTask expansion remain in the closure. Builder initializers allocate before its constructor body; root loading precedes builder staging. Two top-level supplier roles do not bound this graph. |
| Supplier/callback return | LSP finds only the supplier-latch declaration, its true assignment and its end predicate: no clearing assignment. Both child registrations use flowing `OnCompleted`. Task readiness and a decrement inside the callback are not supplier or callback-invoker return acknowledgments. |
| Measurement/restore | Resource measurement allocates an array of borrowed `Mesh.MaterialSlots` arrays and disposes each Mesh lease before their last use. `ScenePersistenceRestorer` measures the planner before obtaining its temporary plan reservation. The existing Persistence reservation does not presently supply a Scene measurement funding/root port at that first call. |

The absence of an independent partial-owner edge is a **source-level ownership gap**, not a claim that the CLR necessarily collects a particular async frame. Incidental stack, awaiter, continuation or Task retention can keep values alive on a particular schedule; it supplies neither the required independent owner nor a return right.

### Lower-owner evidence has a narrower scope

The committed receiver code now has the exact-parent linked R root, constructor/install election, active-X settlement and outer-use return fencing. The inspected existing records `m03b-world-owner-debug.trx` and `m03b-world-owner-release.trx` each report **332 total/executed/passed, zero failed/not-executed**. Their membership includes the 20 `WorldSceneReceiverTests` cases; it does not include execution of this Scene producer. These records were read, not rerun or certified against a new transitive build-input set.

The current factory measurement records 72 bytes for `WorldSceneStorageReservation` and 64 bytes for the warmed parent factory. They do not establish whole World H or Scene J/K. Historical **48-, 616-, and 1,576-byte component observations are not whole-producer bounds**: 48 predates the receiver extension; the supplier-return proposal's 616 is a constructor-only root/TCS/Task/Lock/array/delegate observation. The task-supplied 1,576 figure is not independently requalified by this freeze and is not used in any amount below.

Do not reopen the superseded active-transfer or rollback-predicate findings in older proposals. Conversely, do not treat the committed lower protocol as an implementation of the higher producer's missing roots, funding, or return channel. It conservatively retains the whole parent rather than installing an unproved H carve-out.

## 3. Exact owners and preclaim order

### 3.1 Original issuer and construction publication

For ordinary preparation, the issuer is **that exact `PreparedScene.PreparationControl`**, through its existing allocation-free `TryReserveWorkspace`/`TryReleaseWorkspace` accounting and the existing exact-parent Scene storage transactions. It is not the participant's fixed World charge, a returned reservation, activation's use, or a new use.

Keep the use anchor's retained owner equal to the existing `PreparedScene`. Its `_phaseOwner` retains the planner/built plan; its `_pendingOwner` is the resource-construction destination. Do not overwrite that anchor with the latest bundle, leaf lease or scratch reservation.

Before entering `SceneLoader.StartResourcePreparation`, preclaim its complete fixed call envelope, including any adapter/cursor/callback/context storage, and the whole empty bundle carrier plus protected cleanup/return bookkeeping. Replace self-funding captured `Func` creation with the workspace proposal's **Scene-specific by-value funding argument**, identifying the original issuer/root and existing owner slots. It owns no independent credit. Any retained delegate implementation must instead be prepaid before its construction. Scenes continues to call its existing internal interface; Rendering constructs its own bundle. Assets receives no Runtime funding callback or reverse dependency.

Use the bundle's embedded scalar claim ledgers for ID/material scratch, not a new `WorldSceneStorageReservation` per scratch claim. Only the settled retained bundle issues its receiver. Re-derive control and measurement terms from that final graph; removing the two current scratch carriers does not establish a saving or a qualified total.

Construction/publication must be ordered as follows:

1. The root elects its empty pending slot and records the original construction claim and live supplier part before invoking the ordinary adapter entry. Slot election prevents concurrent body cleanup/refund until this construction attempt commits or unwinds.
2. The adapter constructs an **empty, non-escaping bundle** using a nonallocating, assignment-only constructor: no array/helper/property validation or allocating field initializer. Its unescaped ordinary caller owns the already claimed constructor interval; this is not an independent root for an escaping partial graph.
3. The first publication is a prevalidated, value-only commit of that exact object into the elected root slot. There must be no intervening fallible helper/attachment or user call. Keep constructor/install failure ownership in the root's elected part until full unwind. If the final implementation cannot prove this publication edge, construction remains blocked.
4. Only after `use anchor → PreparedScene → pending bundle` exists may the bundle allocate its first backing or invoke a helper. A fallible later attachment cannot replace this edge. The current throwing `StagePreparationOwner` call after resource completion is not this protocol.

The same rule applies to the actual planner: preclaim its call/carrier/initializer peak before invocation, root the builder's unfinished graph before dependent helpers, and retain root Scene/Prefab acquisitions in its pending fields before validation/cache insertion. Do not declare the resource phase closed while the preceding planner still loses partial ownership or invocation return.

### 3.2 Partial-value ledger

These are required fields/roles of the **existing owners**, not names of new carriers. Their final complete layouts must be derived before construction. Store a received value in its indicated field before the next fallible getter, validation, retain, registration or attachment. Clear a sending field only after the receiving owner is installed and the exact transfer succeeds.

| Partial value / fallible boundary | Strong owner before that boundary | Before-allocation issuer and eventual disposition |
|---|---|---|
| Bundle carrier, phase/count/claim/cleanup-debt fields | Root's elected construction part, then `_pendingOwner` as above | Exact preparation control; whole carrier provision is claimed before `new`. Moving a few fields does not shrink its charge. |
| Atlas IDs, Sprite Material IDs, Mesh IDs; sorted/compacted counts | Bundle fields, populated immediately after each allocation | Original control claims all actual backing/cold sort provision first. These remain scratch X until their last consumer returns. |
| Atlas, Mesh and Material entry arrays, including partly filled entries | Bundle fields plus initialized counts and per-entry ownership/release state | Claim complete capacities before each `new[]`, **not** from a later final reservation. Their retained partition can later move X→R. |
| Current original Atlas lease, helper-retained Atlas lease, current page lease, page backing/builder/copy and pending page-set result | Bundle's Atlas-progress fields, then the completed `RenderAtlasPageSet` in its entry | Caller preclaims the load/retain/call and lease-carrier provision. After Atlas counts are available, claim page/copy/validation capacities before allocation. Helper-local async fields are not substitutes. |
| Atlas rectangles, both tree arrays, tree carrier, queue and sort/validation progress | Bundle's validation-progress fields before the next allocation/helper; tree may subsequently retain its already rooted arrays | Preclaim each complete capacity and call peak through the original control. Initialize/store the two arrays separately before constructing the existing tree over them; stage the queue before enqueue/validation. Retain all scratch through its actual last validation consumer, outside R. |
| Current original/retained Mesh and optional Skeleton leases | Bundle's Mesh-progress fields; completed entry only after all inputs are owned | Claim before `LoadFromPackageAsync`, `LoadSkeletonAsync` and each `Retain`. Include the helper's extra Mesh alias even when no Skeleton is selected. |
| Combined Material IDs, Mesh-only IDs and their counts | Bundle fields while every source Mesh lease remains owned | Claim exact checked capacities before copies/sorts. IDs are X, not R; read-only views of lease-owned arrays do not transfer their backing. |
| Current Material lease, retained Material alias, texture slots/current texture lease, texture builder/copy and pending texture set | Bundle's Material-progress fields; completed Material entry afterward | Original control preclaims each helper/retain/carrier peak. The selected slot counts below size the backing; independently funded Assets payload is not charged twice. |
| Page/texture set construction and entry insertion | Bundle retains **all constructor inputs and the returned set** before fallible attachment | Preclaim set object and any copy before the constructor. An unescaped failed constructor does not take disposal authority from these input fields. Retire sending aliases only after commit. |
| Final storage receiver and its opaque retained-owner attachment | Rooted bundle owns the constructing receiver; receiver contains the same bundle before linked installation | Preclaim receiver Q and preserve existing retained X; use the covered-claim construction sequence below. A reservation local with `RetainedOwner == null` is not a graph root. |
| `PreparedAssetsLifetime`, its Lock, and transfer into `SceneExpansionPlan` | Bundle remains in the root's pending slot; root stages the wrapper through the existing Scenes attachment path, then bundle retains that wrapper as part of its graph | Preclaim complete wrapper/Lock/attachment peak through the same issuer before construction, outside gates. Include the whole surviving wrapper/Lock provision in bundle D before settlement. Plan installation consumes the prebuilt wrapper; it does not allocate another one. Acknowledgment follows actual installation. |
| Registration/context/invoker controls and pending lower Task handles | Root/bundle fields and distinct live registering/invoking parts | Original control claims each actual simultaneous peak **before** exposure. A rooted Task may retain an outcome; its terminal flag does not discharge producer or invoker funding. |
| Current cleanup item, unreleased entries, original grants and failure progress | Bundle/helper fields; root retains failed pending/phase owner and exact original debt | Protected original K/X, or explicitly transferred receiver cleanup provision in R. No emergency claim, allocating failure list or new use after failure. |

Split the existing Scenes attachment path's wrapper construction from its final plan installation: construct/stage it before final R settlement, preserve the pending bundle and constructor inputs through every fallible step, and include the bundle→wrapper edge in the closed carrier layout. This keeps the wrapper independently rooted/funded when its last lease outlives the plan. Later `PreparedAssetsLifetime.Lease` allocations need their actual consumer's preclaim; the one retained wrapper provision does not pay for arbitrary new handles.

`OnResourcesPrepared` must validate the already staged result's exact identity, not call `StagePreparationOwner` a second time. A selected null result must finish/retire its empty construction owner through the real owned-return path without attaching that empty owner as prepared resources. Its invocation obligation remains distinct from an absent participant.

For pending Assets work, retain its returned Task handle before registration and transfer the returned lease into the current field without an intervening fallible operation. The Assets producer must retain its own in-flight graph and invocation under a named Assets grant, or its Scene-attributable portion must remain covered by the original Scene invocation claim. The present decoded-byte and lease-count checks do **not** prove that exclusion.

In particular, `AssetLease.Dispose` clears its state before calling `AssetLeaseLifetime.ReleaseReference`; that lifetime also clears final backing/store references before lower release. Keeping the public lease alone is not proof of failure retention at those boundaries. This ordering is confirmed; a valid-input physical failure there was **not** executed or proved inevitable. Before relying on it, establish either nonthrowing valid release through the complete lower closure, or an Assets-owned pending-release root/debt installed before severing those references. Do not reach into private lease state from Rendering or introduce an Assets→Runtime dependency.

### 3.3 Settling already materialized X, without paying twice

The current factories' `TryClaimSceneReservationConstruction` **claims fresh free workspace** when given a use. `TryInstallSceneStorage` can settle active workspace, but a second ordinary factory call would first claim another full amount. Neither fact is permission to return live X to F and reacquire it.

Extend the **existing private construction path**, not the installed receiver's committed transaction, with exact covered-claim provenance:

1. From the bundle ledger identify the full retained data/carrier capacity D already claimed in X. Claim the receiver's qualified Q before its construction; on the current unchanged x64 receiver layout Q is 72. Protect `R = D + Q` from other root subclaims. Do not use the old bundle constant 256 as an itemized D.
2. Under existing exact-parent/use validation, record a constructor part backed by **these original active claims**, with no second F debit and no early X refund. Allocate the receiver outside gates. Its constructor inputs and opaque bundle reference are already funded/rooted; store the receiver in the bundle before another fallible attachment.
3. Populate its existing retained-owner reference before linked installation. Reuse the prevalidated value-only install election: `X -= R; T += R; parent B += R; parent U -= R`, with F and the domain total unchanged. The receiver then roots the same bundle independently of preparation.
4. On refusal/constructor failure cancel only that original constructor part. Existing live D remains X/rooted; return newly claimed Q only after its actual construction consumers end. The present fresh-claim cancellation path must not blindly refund a covered live graph. Record the provenance before leaving the begin transaction, not in an exception handler afterward.
5. After successful settlement, ordinary R disposal never replenishes P. Scratch, active invokers and publication remain on their distinct original claims. Receiver construction/installation/return after close follows the committed exact-parent rules, not new admission or a successor binding.

The covered-construction entry/cancellation variant is **proposed work, not an already available factory overload**. Its accounting/provenance and final layout require review/validation. An alternative that simply double reserves the live graph is not the selected transfer protocol.

### 3.4 Cleanup and parent join

Reuse the entry arrays for per-entry release progress rather than creating a separate cleanup collection. Close their final field membership/stride with that state included. Keep the current acquisition, helper backing and counts directly in the bundle until transferred or physically released. Completed page/texture set owners likewise need explicit partial-release progress; their current one-shot disposal latch is not a success record for every child.

Attempt independently safe siblings outside gates, marking only successful release/transfer. Keep failed items, exact grants and bounded debt in their original owner; preserve the first original cleanup sample and bounded numeric additional-failure facts without a growing exception list. Do not retry an uncertain release because a repeated Dispose returned normally.

The whole bundle carrier and its retained arrays must remain charged while **either** a resource/body consumer **or** a construction/helper/invoker consumer can access them. A ready bundle can be disposed/admitted while its supplier is still returning. Body release must therefore neither refund R nor report physical cleanup complete until that return condition holds, unless it explicitly transfers the remaining cleanup to the same already funded receiver owner. The last eligible ordinary return can progress cleanup; no worker or GC-dependent disposal is introduced.

That join includes the retained wrapper's outer `Dispose`/`ReleaseLease` interval: current code locks/clears `_value` after child Dispose returns. A nested bundle Dispose cannot refund wrapper-funded R before those outer accesses end. Add the required return-part state to the existing wrapper/bundle owner, record it before the child call, finish bookkeeping while funded, then perform its actual terminal handback. A later lease carrier's own field clearing stays on that consumer's separately identified claim. Derive these final controls/overlap; do not allocate a return token in order to request its own funding.

Before final receiver handback, settle bundle/helper fields and arrange the exact failure-retention/debt path while R is still rooted. The successful final suffix cannot access the retired bundle/receiver. If final bookkeeping needs the parent's existing return part, identify its precise preowned coverage and end; do not assume that part funds new callback/context storage. The existing domain→World interest→parent→receiver→bundle chain and original binding owner survive World close until actual construction, R, U and return obligations end.

The root's shared carrier similarly survives both producer and prepared/admitted body conditions. Supplier-return, registration-return, callback-invoker/context return, public `TrySet*` return, body cleanup/transfer and the parent's terminal return remain **separate facts**. Clearing the supplier latch on readiness would replace a permanent hold with an unsafe refund, not implement their join.

## 4. Derivable capacities and amounts

Use Chapter 04's existing accounting function, also implemented by `WorldAdmissionDomain.ArrayCharge`:

```text
A(n, s) = 64 + RoundUp16(checked(n * s))
P = F + X + T
remaining preparation operation charge = 256 + F + X
```

These array charges apply to allocated capacities, including an actually allocated zero-length array. Do not substitute reference size for a class extent. Shared empty backing is excluded only when the actual selected path uses it. The bundle/planner's local eight-byte-rounding `ArrayBytes` functions and unexplained 128/64/256 constants are not the complete closure.

Let S and C be the checked Sprite and Mesh component counts actually scanned from the owned plan; a and m their distinct Atlas/Mesh counts; p the explicit Sprite Material reference count; `l_i` the slot length of distinct Mesh i; `L = sum(l_i)`; and q the distinct Material count.

```text
0 <= a <= S; 0 <= m <= C; 0 <= p <= S
1 <= l_i <= 256 for the inspected native/mesh 1.0 codec
L <= 256*m; q <= p + L
```

The 256 derives from `RenderMeshAssetCodec.ReadMaterialSlots` and its checked `GetRequiredRecordArray`, not from the one-Mesh retention fixture. `RenderMeshAsset`'s constructor alone checks only nonempty slots; the bound is specifically for that real codec path, not arbitrary future constructors/codecs or M05 LOD consumption. Check all sums and signed array representability before requesting backing. The expanded-Entity ceiling alone is not a proof of S, C or Material multiplicity.

| Inventory | Supported derivation / remaining qualification |
|---|---|
| Initial three ID arrays | `2*A(S,4) + A(C,4)`, before allocation. Counts are semantic demand, not permission to spend bytes. |
| Atlas/Mesh/Material entry arrays | `A(a,stride(AtlasEntry)) + A(m,stride(MeshEntry)) + A(q,stride(MaterialEntry))`. Current declarations contain one `int` plus one/two references; final cleanup-state fields require final compiled strides. Do not price these as class-pointer arrays or silently reuse old strides after edits. |
| Both Material-ID arrays | `A(p+L,4) + A(L,4)`, simultaneously with the needed initial IDs, entry arrays and retained leases. Deduplication reduces q, **not** these pre-compaction capacities. |
| Atlas page holders | The actual Atlas exposes `n <= 16` pages before helper allocation. Price every separately acquired page lease and the actual builder/backing/final-copy coexistence; shared page AssetIds do not merge independently disposable handles. |
| Atlas validation | For f frames: rectangle backing `A(f,20)` from the five-int `PaddedRectangle`; the two existing 16,384-int arrays cost `2*A(16384,4) = 131200` accounting bytes. This excludes the tree object, queue/control, sort, leases and invocation. Pre-size/reuse the existing queue from the known frame distribution and derive its actual runtime backing capacity/stride before construction; do not assume the default queue's growth is prepaid. |
| Material textures | The actual helper selects at most one Sprite slot or four PBR slots. Determine the selected unique texture count t before backing construction; `t <= 1` or `t <= 4` respectively. Repeated slots still owe each slot's semantic validation. Preclaim exact chosen builder/copy/set capacity, not its current opaque default growth. |
| Mesh/Skeleton holders | Per distinct Mesh, one retained final Mesh lease and zero/one final Skeleton lease; include original and helper-retained Mesh aliases and their actual coexistence before those final values. This counts handles, not their complete byte cost. |
| Lease and set carriers | `AssetLease<T>` constructs a separate `LeaseState`; fresh decode constructs a shared `AssetLeaseLifetime`; Retain creates a new handle/state but shares that lifetime. Derive each final closed managed extent and actual shared lifetime ownership once. Assets' decoded wire-length/lease counters do not size all of these carriers. |
| Retained bundle D | Whole final bundle and surviving wrapper/control provision, retained entry backing, sets, lease-holder backing/carriers and required retirement/failed-cleanup state, excluding independently funded Assets payload and ended scratch. This is an inventory to close, not the current helper's `256 + entry arrays`. |

**Distinguishing arithmetic example, not a fabricated Scene or executed qualification:** at reached counts `p=1, m=1, l_1=256`, Material scratch must accommodate 257 combined and 256 Mesh-only IDs, even when many references deduplicate. Their normative array charge is `1104 + 1088 = 2192` bytes, before owners/sort/calls. At the earlier first Atlas await, the Atlas/Mesh entry backing already exists and final R is still absent. A later adequate R reservation or a larger receiver-control observation repairs neither that earlier claim nor its missing root.

Measurement must obey the same preclaim/root rules. Copy borrowed Material IDs into funded owned scratch while the Mesh lease remains live, then release the lease; do not retain its immutable backing after ending its lease grant. Account complete old/new capacity overlap if collection grows. Return the **retained** requirement, with separately identified measurement scratch, rather than `scratch + R` presented as retained storage. The current measurement's three receiver-control additions are not funding for its call envelope.

## 5. Genuine unresolved prerequisites and exact retry conditions

The following are missing inputs/capabilities, not assertions that every CLR allocation is mathematically unbounded. No constant, fraction of P, sampled allocation total or count of syntactic async methods closes them.

| Missing input/capability | Condition for resuming the dependent work |
|---|---|
| Final J and bundle/plan/helper construction graph | Compile the **real** operation-specific progress implementation with the guard still null. Close whole field/layout membership, constructor initializers, TCS/Task/delegates, Lock lazy state, cold metadata/cache ownership, retained wrappers and covered-X construction/cancellation. Every creating site needs checked capacity/multiplicity, coexistence, original issuer, root, and actual end/transfer. Layout observations corroborate that inventory; they do not supply it. |
| Complete K, including native fault capture | The prior finite-envelope addendum identifies pinned native `GetFrozenStackTrace`→stack-clone work dependent on the exception's old stack/keep-alive capacities. This assessment imports that **prior** source finding; it does not reverify upstream native bytes or identify a future testhost's loaded CLR. Resolve actual loaded runtime/native versions, those admissible capacities, capture multiplicity/coexistence at child `GetResult` and public fault publication, and a real before-capture grant. If admitted inputs cannot provide a useful finite funded term, a fulfilled budgeted capture or non-copying relay preserving original faults/order/token is required. Neither capability is supplied here; `TaskCompletionSource<PreparedScene>.TrySetFromTask` does not accept the differently typed planner/resource Tasks. Denial must not replace the required original fault or recurse into unfunded capture. |
| ExecutionContext and registration/invocation overlap | The final ordinary invoker must preserve initial isolation, nondefault/default/suppressed flow, AsyncLocal notifications/reentrancy and restoration, and lack of arbitrary caller SynchronizationContext capture. Derive all coexisting registering, queued and running invocations from actual code, including callback-before-registration-return. One pending I/O and three root parts do not establish that peak. Public capture-null under suppression is not the stock builder's internal sentinel. No mechanical `UnsafeOnCompleted` substitution or unowned context bootstrap is approved. |
| Actual supplier and callback-invoker return | Replace the Scene-owned stock invocation boundaries transitively with the finite-envelope addendum's operation-specific ordinary progress/invocation route. Include planner nested Task/ValueTask work, the real adapter/bundle/helpers and both root Actions. After semantic work, context/publication scopes and alias clearing, hand back the exact original part/claim once outside gates. Prove the remaining raw Task/ThreadPool/managed/native suffix has no retired-grant access or Scene-owned allocation in every supported branch. A signal in another Action, source `finally`, Task observer, elapsed time or GC does not satisfy this. If the chosen public mechanism cannot enclose a suffix, that exact lane stays unavailable. |
| Assets carrier/invocation and release boundary | Identify the existing independent Assets grant/root/end for excluded machinery, or keep the actual Scene-attributable call/holder provision on its original claim. Close the conditional early-severing release concern in section 3.2 before treating an attempted lease Dispose as release. Do not infer this from live-lease counts or pass Runtime authority down into Assets. |
| Persistence measurement/restore seed and root | `WorldPersistence.PrepareLoadCoreAsync` must attribute an already admitted, finite Scene measurement envelope and actual retained operation root **before** its `MeasureRestoredScenePlanStorageBytesAsync` call. The concrete candidate issuer is its existing `WorldPersistenceAdmissionReservation`, using nonallocating `AcquireUse` and exact covered candidate/World storage attribution; no current call supplies/derives that seed for this closure. Its allocating `AcquireRetainedUse` wrapper/delegate also needs prior funding if used. Until the amount, strong owner and return boundary are established, this shared route remains blocked. Never borrow ordinary P/defaults, manufacture a PreparedScene, or make null funding unlimited. |
| Final parent lifetime and failed return | Demonstrate that proposed higher-owner transfers/cleanup do not bypass the committed constructor/return parts and original binding fence. Retain the whole parent as implemented; no estimated H subtraction. Close the higher invoker's last access and exact failure debt before its final handback. Lower 332/332 tests are not this proof or native/AOT evidence. |

The two-original-exception source array bounds **original samples**, not EDI/native copy bytes, fault-wrapper capacities or foreign exception graphs. Preserve the tentative external-observer provenance rider only in its stated scope: caller-origin continuations are not Scene-created registration/context/publication machinery. Do not use it to erase the native-capture row.

The known Material bound closes that dimension for the stated codec only. No fixed whole-producer allowance can be obtained by multiplying it by an Entity ceiling and adding the historical 48/616/1576 observations.

## 6. Ordered implementation and validation sequence

All work in this section is subsequent work, **not performed by this documentation task**.

1. **Keep the current refusal and resolve the owner input set.** Preserve `QualifiedFootprint == null`; freeze the actual source adapter, planner, bundle, helper, lower-owner and selected runtime/code-generation inputs. Retain the selected operation-specific route; Chapter 02's prohibition on engine-specific async abstractions is unchanged. Do not introduce a custom generic builder/task-like/scheduler/worker to bypass an unresolved ordinary-invoker boundary.
2. **Implement one complete partial-owner/funding batch, compile-only.** Thread the private exact-issuer argument through the real loader/interface/participant/planner/bundle/helpers. Implement pre-elected empty-owner publication, all pending fields, arrays/entry state, per-call preclaims, covered-X receiver issuance/cancellation and preclaimed lifetime attachment. Keep ordinary, measurement and restore issuers distinct. Compile Runtime Debug then Release using ordinary commands below; do not execute preparation to discover its required amount.
3. **Close release and independent retention before integrating the producer.** Complete bundle/set/lifetime/plan cleanup progress, Assets release disposition and exact debt. Validate any changed lower covered-construction/return logic through real Worlds and existing lower-owner methods, with the preparation guard still closed. Cover exact/one-byte-short claims, X→R conservation, close-first/install-first, duplicate/foreign return, no R refund into P and final original binding retention. A refusal is not an OOM or physical-cleanup failure witness.
4. **Close every invocation and all finite rows.** Compile/inspect the actual planner/adapter/helper progress and completion invokers, including registration return, ExecutionContext restoration, pending aliases, public publication and terminal parent handback. Bind final fields/IL/native suffixes to actual compiler/runtime binaries, executable tiers and supported instrumentation modes. Derive the disjoint `J_shared`, `J_producer`, `K_body`, `K_publication`, variable X and retained R catalogue using section 4's admitted inputs. Protected K precedes optional work and its own allocation. Stop at any section 5 missing row; keep the footprint null.
5. **Enable only a genuinely closed lane, then observe the actual producer.** A nonnull footprint requires the real source adapter/planner/bundle/helper calls, cleanup, supplier-return, callback-registration/return and parent lifetime to have the preceding complete funding/root/end proofs and lower observations. Only then install the derived values and execute real package-backed Scene tests. There is no test-only producer, fabricated Scene, guard bypass or guessed provisional footprint. A missing required observation leaves the affected qualification open; do not describe the whole capability as accepted or publish an unsupported lane.
6. **Validate the distinguishing behavior as one coherent batch.** Use existing package/codecs/registrations and the owning Runtime fixtures. Confirm fresh, nonzero selected/executed TRX membership, Debug before Release. Cover mixed Sprite/Mesh/Skeleton, actual multi-slot Materials, shared and distinct textures/pages, helper validation faults and selected-token cancellation; no participant versus selected-null versus real resources; exact/short early entry-array, helper and material-scratch claims with prior setup funded; and final R refusal after earlier acquisitions. Verify claims precede the named load/copy/retain, partial roots survive independently of incidental async retention, safe siblings clean, and failed graph/debt remains funded. Preserve the default retention fixture's nonempty binding, default quotas/options and pixel-17 witness through activation/unload/disposal.
7. **Observe readiness/return and body/parent races separately.** Stop actual compiled suppliers after outcome readiness but before owned return, the registering caller before registration return while its callback runs, the callback before its enclosing context/invoker returns, and public `TrySet*` after terminal visibility. Dispose/admit/end the body during these intervals. No original supplier/shared/return claim may refund early; after actual release, observe exactly one joined refund. Retained R must survive World close and final lease release with the original binding fence intact. Use safe external stops/caller scheduling at real sites, not production failure hooks or synthetic OOM. Record any unreached physical/partial-native stimulus and retry condition instead of substituting a pass.
8. **Qualify each actual consumer separately.** Complete funded measurement/real Branch restore and affected public Scene/renderer consumers after their own prerequisites. Managed helper tests do not establish those producers, NativeAOT, other targets/runtime modes or Chapter 35 acceptance. Dependency-ready M05 CPU/layout/source-adapter work can continue under Chapter 35; Scene-dependent consumers cannot take this proposal as their missing producer.

Use the SDK selected by `global.json` and `docs\project\Building.md`; do not add another runner or duplicate SDK pins:

```powershell
dotnet build .\sources\TerraFX.Runtime\TerraFX.Runtime.csproj --configuration Debug
dotnet build .\sources\TerraFX.Runtime\TerraFX.Runtime.csproj --configuration Release
```

For subsequent lower/prod behavior validation, select the actually changed owning fixtures together through ordinary `dotnet test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --filter "<owning fixture selection>"`, then Release. Preserve exact selected/executed counts and reached boundary evidence; do not run the guard-blocked full Scene suite as an allocation experiment. Allocation-complete and native return evidence, not sampled GC ticks, must corroborate the final catalogue where required.

### Preparation/activation cap mismatch remains explicit

Current code captures `MaximumPreparationBytes = P`, default **67,108,864**, valid range **[1, 2,147,483,648]**. `CaptureActivationHeader` returns that original P; `ActivateAsync` independently requests `256 + P` for **each** attempt. The XML calls it preparation and per-activation budget. In contrast, the dirty Chapter 16/30 descriptions specify the whole-preparation cap and separate activation admission without specifying that this option also selects activation's W.

[Budget accounting section 9](m03b-scene-preparation-budget-accounting.md#9-concrete-tentative-rectification-captured-cap-and-funded-delivery-tail) tentatively selects `W_a = P`; the finite-envelope addendum already records the mismatch. Preserve that selection while obtaining separate owner/public-contract reconciliation before claiming conformance. **This proposal does not alter Chapter 16 or 30 or treat tentative prose as their amendment.**

With fully covered transfers and no intervening release, preparation plus one activation reserves `2*P + 512 = 134218240` at the default, counting transferred R once. That is accounting, not an executed observation or guaranteed fit. No zero activation cap, smaller adaptive P, quota increase, activation borrowing or silent success-shaped fallback is proposed.

## 7. Frozen direct input set and verification scope

Freeze UTC: **2026-11-18T01:24:24Z**. Branch: `tannergooding-m05-implementation-plan`. HEAD: **`3b37dd71ca4e6212255e7cebcb433cd917ffdf99`**. The following **74 raw-file SHA-256 values** identify every direct local source/proposal/test/build/guidance/evidence input inspected or fingerprinted for this assessment. They are hashes of current disk bytes, not Git-normalized or HEAD-only contents.

Material dirty inputs at this freeze: Chapters 04/16/30; Runtime's project file; `RenderSceneAssetBundle`, `Renderer.CpuExtractionSupport`, `ISceneExpansionResourcePreparer`, `PreparedScene`, `SceneExpansionLimits`, `SceneExpansionPlan`, `SceneLoader`, `SceneLoadOptions`, `SceneLoadOptionsSnapshot`, `ScenePrefabExpansionPlanner`; the Runtime test project and the three listed Scene test files. **`PreparedScene.Preparation.cs` is untracked.** The six TRX files are existing ignored observations. Other listed tracked inputs are clean. Including dirty maintained contracts does not accept their changes.

```text
12439df4bb5e46021891b234bec1104abdca57c11fdf3f610c8f32c5daf3001e  .github\copilot-instructions.md
03efea672cd52e9ef053e9ce0285e7ca02c6ce6fb0d21268ade4255ed5a39d58  .github\workflows\ci.yml
2a59ec696851ca27738d5c7891c1c362fab0032f371513a25db0201531af4bcd  artifacts\tst\Debug\m03b-scene-cleanup-debug-final2-20261118.trx
1f4d53d78438f44792dfea5cbf6d48fbe9731297e517460c74453206ab3b592b  artifacts\tst\Debug\m03b-transfer-return-verify-debug.trx
57b2557d653e8200481692dd13b4221f3361b4a42b8bd9b160b40772c891552b  artifacts\tst\Debug\m03b-world-owner-debug.trx
bf9e1f3ab53c50e806194402edfeb5f60fc9371a16b84418e69e3e9e097f27c2  artifacts\tst\Release\m03b-scene-cleanup-release-final2-20261118.trx
394645a0f740b9c519ef4c820556b1caaa65bad181ddc0e6df062ed3bf71b069  artifacts\tst\Release\m03b-transfer-return-verify-release.trx
3c83dac62ba1e035725fd13bb308913ba6b26548c2d026d5a6136e9269efde55  artifacts\tst\Release\m03b-world-owner-release.trx
770ee4b2a422861a6f23f0d39dd0eca361a81cb87e64ebac4e12170f71231d7a  Directory.Build.props
aeec1d08e8037470239035812351c4045124607d67a354d482112f4d642d4105  Directory.Build.targets
1e0e79719efc9259390d9cf4e63ac697561725c8cd910d2ab2541d6e5bb95a6d  Directory.Packages.props
40f913a50434209e09941c22625a6d0722202bab97ebac0e153c1ea3368e2291  docs\design\02-dotnet-api-memory-and-ownership.md
a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245  docs\design\04-capacity-and-workload-profiles.md
b012f42b23cede9c7cdb71203d8357cd19e1626823ea25ef3119961292d3698a  docs\design\16-scenes-transforms-and-cameras.md
3b55a013eec327ecd648acbd5b53717dd218ad288e8047ac21277609efa2f47b  docs\design\29-project-setup-build-and-deployment.md
15848c82f3858a77a87a0d3ea6fbcc07046eb6e77ca5b21748c72b90efab598b  docs\design\30-public-api-review-surface.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  docs\design\35-implementation-roadmap.md
f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f  docs\project\Building.md
1c8c3f8e140f47492255534284abbf67c5776210dc7a68f2c01155d0be773f46  docs\project\Roadmap.md
3f8e4f0315c6cc2933fc982f64d5e94b9070eb1876a74b0688118bb17aff5a62  docs\proposals\m03b-scene-preparation-budget-accounting.md
eccc174d5df86e195731eb3be74fbe5a349d0214c8f94de35769503d4418595c  docs\proposals\m03b-scene-preparation-compiled-producer-evidence-addendum.md
3eb20e1051c55ee77d008cf734007fde8c28aec24abd9755f4d1788475bd4605  docs\proposals\m03b-scene-preparation-finite-envelope-and-return-addendum.md
940baef78ccfbd720e292167c3af9c7c00aabdbc4f448e6e4a00793bb54230da  docs\proposals\m03b-scene-preparation-footprint-closure.md
0a9ae81eb42d886168d9e05970c7395b33c9ef70fccada9ed311cf2e1dc7ad89  docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md
199158c2357ac6b3fe551f568c0da69b739a0a30afdfc2372113dd4c6597f8fd  docs\proposals\m03b-scene-preparation-supplier-return-closure.md
9d7280a89c5bb75890adaf65e26f8c80766b8687ee0608623d0f97d519be32c3  docs\proposals\m03b-scene-preparation-workspace-closure.md
17671f087293fbfb4c454432b7c016076ab612508aa3eda85f13acf54d3cb6ec  docs\proposals\m03b-scene-receiver-control-funding.md
632fe2eadf72026d0d68fa093d260b92ca1be8d5eb6deae5fdf08bf48f471ab5  docs\proposals\m03b-world-binding-owner-retirement-fence.md
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  global.json
d44373af315523f8af9f09f36bded2a688e2b5c1572fcc0732310c7ef590ead8  sources\Directory.Build.props
4c0527a12e48d07401c69c06cf942f9145d6a8ccf98c4a7f052b11e463e1ce9a  sources\Directory.Build.targets
a639a5949353f2fb594b2c57569b2491d08988bfba48f551e666a36a6ea8d3da  sources\TerraFX.Assets\AssetLease.cs
2749b26e3a42809558558704b6d11c51e886ae40a933e29b7a205a10357881b2  sources\TerraFX.Assets\AssetLeaseLifetime.cs
6ee57d6c011bece27e5f1de33a5c1d27d9ae76a510ce825022c0cfe070b02b74  sources\TerraFX.Assets\AssetStore.cs
79a5872aebab1ea7447e064bc923227660056458b5b8e2b4af32a891e11789dc  sources\TerraFX.Assets\AssetStore.PackageLease.cs
bb2123297d3e65309bd38ce5da0083e14319810139fa233ca0b0ebc4e5404594  sources\TerraFX.Assets\AssetStoreOptions.cs
c7dcba4dfef58346c6c851f77a8e130b1a4b776248e94663438a5d9e449245a8  sources\TerraFX.Runtime\Entities\World.cs
a250eecca2cfb4e0eebff7f447e66ac7e44a8d23ad0d6de141e8e1d4c30614e3  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
53111972c5b480e8357c93d471edcb4e08c7bbe65bd341bfc7260fdc356ba5c3  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
d6cdd6105850b8076d40891b0ed1f583d9fcde6ee190444a7d570879964403dc  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
f00b54b0e8f305f90df0d63ffdf6c3419feb8f3d2542e72d76a79ad2d0990946  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
beef96488f9e99f8a852b8ae4d34fc82ca4d77af9ce508032ffb50a48fe3ccbb  sources\TerraFX.Runtime\Entities\WorldPersistenceAdmissionReservation.cs
a91aac1a9eb2ba7b43c111fab8baa9d6588a6c75e725c28ea9825f049472c969  sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
1105c3f9d6f65c8629cb8c730bee0bacbe7366ff6c9bfc63ecf7780e78d6c0f4  sources\TerraFX.Runtime\Hosting\WorldPersistence.cs
d8796b1c243915d87db9a88f0adf888bc8b1f34b3f50b80521932404c898c342  sources\TerraFX.Runtime\Rendering\RenderAtlasAsset.cs
c8a80f5a957ba0e1db6a56d390ccb04a8d807399ad18f4fb233297a10c8411be  sources\TerraFX.Runtime\Rendering\RenderAtlasPageLoader.cs
abc653b3150c4c3bde95a90fe39527d0f4b068c62dad96577a183e2c681f82bb  sources\TerraFX.Runtime\Rendering\RenderAtlasPageSet.cs
eaf1f94aff0886b3fa30af97bd7f83180a38ab744d4241d0cdd275a055eadb55  sources\TerraFX.Runtime\Rendering\Renderer.CpuExtractionSupport.cs
f4c183b5b28bc45f570166f3ecf8bb55d65bcbefbda11d6e68d40ed8bf36e1d7  sources\TerraFX.Runtime\Rendering\RenderMaterialAsset.cs
1f69743f5f4c300da9a0c20e174d21748ebd7119496d957855b9c098d72584b9  sources\TerraFX.Runtime\Rendering\RenderMaterialTextureLoader.cs
fc96d4afff183dc0ca5c9476f6ffb508e6c4ca3f98a4e6edbdbf3001bdf3656a  sources\TerraFX.Runtime\Rendering\RenderMaterialTextureSet.cs
de4681a7a5edfc39502faf9339bc5d97afd0ef2bbdf024aa0fd67f535829c49e  sources\TerraFX.Runtime\Rendering\RenderMeshAsset.cs
e4b7edbbc746cf29568d683c8ed0a8e0987fde6dc10ad4e390370bb8d12865d5  sources\TerraFX.Runtime\Rendering\RenderMeshAssetBinding.cs
b07a023942ec548fdffc0f1ddbdbfd4185fe50f52094da8220d3617c70e0d424  sources\TerraFX.Runtime\Rendering\RenderMeshAssetCodec.cs
689dd5a2237398fee46b01ed00538cae66d36b8e30ad2aaa0488cc3bb6c08260  sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
67ae936843f6329d95d12b784a4686a8b75b2cceb3932159e56bdd24f21ca423  sources\TerraFX.Runtime\Scenes\ISceneExpansionResourcePreparer.cs
846ddd30c039bc29f8a41dddc8642085e841f1d5bd39deff40daac3710e0111e  sources\TerraFX.Runtime\Scenes\PreparedAssetsLifetime.cs
b73dd4314b63084132afc2f9325ed98eeccce65a174f02b16f2ded7ec65f688c  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
5d210ae9266effe8c074dd0cadb8e6c8027a0155424a17f2e3cab47da479aa13  sources\TerraFX.Runtime\Scenes\PreparedScene.Preparation.cs
af9626c539b35c73338baac12409b647fa37bf92ee38fd5c2e6be892c40fe771  sources\TerraFX.Runtime\Scenes\SceneExpansionLimits.cs
4ec58808bb5400448f71327e75cab787ed03c1ce43fa39ffae832cea8c4ad137  sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
d53823c6c673e881aad32a3e94508ec5b252cc39d366a99458a6c487e0fe4886  sources\TerraFX.Runtime\Scenes\SceneLoader.cs
e95382b5f26b7dd398c34ba0b4f20a6a0e76bf9f392b5b3b4bad88ad1f898a80  sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs
004b203dc84037b76f854dde1b47c78d570a8085b6a9b48ee6423e27496223e9  sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs
698f36fb6207d09b395af7a0da85ff132dc8e7ed549e9b4e804df5d6728348c9  sources\TerraFX.Runtime\Scenes\ScenePersistenceRestorer.cs
1e01cc392652771a948d45f79fca19fc1ed5c4659439ad4ec8879c2e99127876  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
2f1c81e54f0fbf12d01e34d0d840885f5eb0e94a8b8890865ef721b8c6a3c65d  sources\TerraFX.Runtime\TerraFX.Runtime.csproj
281aad4237121feab1903c2384f1a5f0aed7e218a704c918fe953e9fc8e75764  tests\Directory.Build.props
4c5822faa56e345df0640be0c19d627a21d6a63965c539ab3e0d13fb14d0fdca  tests\Directory.Build.targets
9f884c2185cb3372914457dc72ec760666ede1bd2388a387a08b64f3e17b894c  tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
e342237b7645be805dd924855b2239907773d31753d22b8b2261bf26c20452f4  tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs
d077505a70e96ea9fb530d1d4b5a030cbf2c6bf599a867dc79256a5a99b10de8  tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs
b915bb3b96f7b5008e946c74f2eab726b05a200ea45db7cd7724e16e83e6cc60  tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
b8dfbc8438bd32a93acb54dd420ad09a7bf259c2db5d366d2100229d066cf583  tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
```

The two auxiliary `transfer-return-verify` records report 110/110 each; the two older `scene-cleanup-final2` records report 15/15 each. They were inspected as historical lower/component context, not added to the 332 counts or promoted to producer evidence.

This is a **direct scoped evidence freeze**, not a repository inventory, transitive compiler/runtime/native/build freeze, schema review, or source-to-binary attestation. No schema/fixture file was independently changed or added. Upstream/native and older compiled observations are imported only through the explicitly frozen proposals; their original manifests do not automatically become this review's accepted inputs. Recheck all 74 raw hashes before applying a disposition; changed bytes require reassessment rather than silently refreshing the expected hash.

At this freeze, only this proposal was authored; no source, test, maintained design contract, roadmap, existing proposal, project/build configuration or unrelated batch was edited or staged. This document does not claim a build/test run, Scene execution, allocation experiment, native/AOT qualification or source disposition.
