# M03-B Scene actual-invoker return closure

**Status: tentative implementation guidance under the requested blocker-rectification policy. The boundary below is selected, not implemented or qualified. Complete producer admission remains NO-GO; keep `PreparedScene.QualifiedFootprint` null.**

## Decision

Place the actual selected-read invoker in the existing **Runtime-owned `PreparedScene` driver**, above one installed **Assets-owned `AssetLoadOperation<T>` progress owner**. Replace the selected internal described-read Task chain with ordinary acquisition/decode/inspection/cleanup steps and the real lower read's single-consumption `ValueTask<int>`. The Runtime invoker owns consumption, context, registration and return interests; Assets owns backing references, Store credits, buffers, semantic intermediates and the staged lease. Ordinary returned calls, rather than a second Task observer, connect these owners.

This makes the earlier [owned-invocation proposal](m03b-scene-none-read-owned-invocation-boundary.md) concrete at the caller edge. Assets receives no Runtime type, funding callback, World capability, scheduler or upward completion port. Runtime can consume a lower I/O result without acquiring authority over its package. Neither owner calls a new `Returned` method from inside an old compiler-generated producer.

Retain the selected factory-registered Scene/Prefab 2.0, package-None, no-resource-preparer slice and one child-operation slot without a queue. Reuse the existing decoder, readers, validation order and expansion algorithm. Do not introduce a second decoder, preparation carrier/use, arena, generic builder/task-like framework, worker or scheduler.

**A safe, fully funded executable implementation is not presently available.** In particular, the real Windows read returns reusable I/O machinery to the **file handle's lifetime**, not necessarily to the preparation. Its original backing-lifetime provision is not established by today's Assets budgets. The exact context/fault/dispatch closure also remains unfulfilled. Sections below select bounded prerequisite work and identify the authority needed; they do not fill unknown terms with constants or approve a lane.

This proposal is independent of M05 diagnostic carrier/budget work. It changes no public API, package/schema version, cancellation rule, acceptance identifier, P, World/domain ceiling or existing refusal.

## 1. Verified current boundary

Analysis uses HEAD `4b48e01798151bf3cc844871137b0a0798caaffd`, including existing dirty bytes. Its only change from entry HEAD `c9302fa1` is the concurrently committed Assets bootstrap proposal; the inspected source/contract bytes are unchanged. `dd4fd10f` is the phased-restore implementation ancestor, not a Scene producer qualification.

| Current source | Source fact and consequence |
|---|---|
| `Scenes\SceneLoader.cs:165-206`; `Scenes\PreparedScene.cs:85-160` under Runtime | Public entry checks options/identity/shape and pre-cancellation before creating preparation. The null-footprint guard precedes description and use acquisition. No ordinary nonempty preparation executes through this entry. |
| `SceneLoader.cs:237-269`; `PreparedScene.Preparation.cs:59-110,874-910` | The existing root invokes the planner, including captured reservation delegates. `_supplierProducersUnjoined` is set before invocation and never cleared. Initial `AsyncTaskMethodBuilder.Start` encloses initial context restoration only. |
| `ScenePrefabExpansionPlanner.cs:143-240,254-313` | The described root read completes before its lease is staged. Builder field initializers allocate before builder staging. Neither interval has an installed child read owner. |
| Planner `BuildAsync`, `CountAndMeasureAsync`, nested traversal and `GetPrefabAsync` | The closure includes nested Task **and** ValueTask producers. `GetPrefabAsync` derives a descriptor from the root Scene lease, then keeps a new Prefab lease local through context checks and cache insertion. One pending read does not count returning invocations. |
| Assets `AssetLease.cs:139-147`; `AssetStore.ReadDescriptor.cs:29-102` | Anchored reads acquire a temporary backing reference before the async admission wrapper. Refusal releases it in a catch; the reference can be final after source/Store close even though no child lease was issued. |
| Assets `AssetStore.cs:360-468`; `AssetStore.PackageLease.cs:130-223` | Admission acquires backing/lease/decode obligations; payload decoding and reference inspections follow. There is still no preinstalled progress owner spanning these acquisitions and their actual return. |
| Assets `AssetPayloadReader.cs:16-28`; `AssetPackageBacking.cs:107-112`; `AssetPackageReader.cs:219-242` | None allocates the decoded array and awaits the backing/read-exactly wrappers over potentially short `RandomAccess.ReadAsync` reads. None does not remove I/O, cancellation or suspension machinery. |
| Assets `AssetEnvelopeDecoder.cs:225-288`; `AssetReferenceScanner.cs:80-93`; `AssetStore.cs:498-571` | Scanning precedes typed decoding. The decoded value/reference result coexist with sequential target inspections. A described target's None check precedes its decode reservation/read; actual schema and exact registered-codec membership are checked afterward. Inspection invokes no target codec body. |
| Runtime `SceneSupport.cs:22-54`; Assets descriptor/receipt/registration implementations | Exact factory origins, adapters, schema tuples, source limits and backing identity are revalidated. These are provenance and restriction facts, not allocation grants. Unused/custom registrations do not make the selected built-in typed read execute custom codec bodies. |
| `PreparedScene.Preparation.cs:580-676,690-850` | The root's flowing completion Actions consume child Task outcomes. Public `TrySet*` can expose the result before its caller returns; source-local completion and the three driver parts do not join enclosing supplier/context invokers. |

Two findings in prior proposals are **stale after `362f8847`**. Current `CreateLease` sets `ownershipTransferred` only after both lifetime and handle construction succeed; it has no cleanup `finally` setting that flag. Current load rollback releases the backing before `CompleteLoad` returns lease accounting. Preserve those corrections. They do not install pre-lease ownership or establish physical close.

The [binding/receiver fence](m03b-world-binding-owner-retirement-fence.md) has also progressed: current `WorldGuidUseControl.Release` retains an exact-parent outer return part, and the domain clears control state before refunding its interests. Current Scene installation prevalidates before its nonthrowing field-store commit. Do not re-propose the old lower ordering defects or infer higher Scene-invoker closure from those fixes.

The later [phased restore proposal](m03b-world-restore-phased-owner-protocol.md) is implemented in the inspected source: `WorldLoadExecution` drives lower phases and higher restoration. Its empty-Scene success does not fund this read protocol. Shared restore/measurement helpers have distinct Persistence owners and do not themselves consult `PreparedScene.QualifiedFootprint`; their mere callability is not ordinary-preparation authority or qualified nonempty restore evidence. Preserve their existing refusals and unqualified disposition.

## 2. Actual .NET boundaries, not an outcome-only model

Read-only local metadata identifies CoreLib `11.0.0-rc.3.26522.148+5254d8bcf59917edc1e1abc33bdf2442b77b1c70`, SHA-256 `df53885902e896a2b40988237cbe9cbb9f1bb0743200f05fe9257c56a29c5732`. The linked sources were inspected at that unified revision. This identifies a **reference source lane**, not the CLR, ThreadPool mode or native code loaded by a future consumer.

| Inspected runtime source | Established fact; limit |
|---|---|
| [Task builder][box], [ValueTask builder][value-builder] | A stock box calls `MoveNext`, then tests completion and clears its state/context. Suspended ValueTask methods delegate to the same machinery. Outcome visibility precedes these accesses; another await cannot recover their return. |
| [Initial builder entry][start], [ExecutionContext][context] | `Start` restores initial synchronization/execution context after the call. Public `Capture` returns null for suppressed flow; internal `CaptureForSuspension` uses a distinct suppressed sentinel. Public `Run` rejects null. Public `Restore` exists, but is not an after-invoker notification or an automatically equivalent suppression protocol. |
| [Configured Task awaiter][awaiter], [Action invoker][action], [ThreadPool dispatch][dispatch] | `OnCompleted` flows ExecutionContext despite `ConfigureAwait(false)`. A flowing queued Action can leave context reset/AsyncLocal notifications to the outer dispatch loop. An Action-local `finally` is inside that boundary. |
| [Windows RandomAccess][random-access], [overlapped source][io-source] | The actual async-handle branch obtains an overlapped source, pins memory and registers cancellation. `Complete` disposes the cancellation registration, unpins and frees the operation's native-overlapped allocation **before** setting the value-task source outcome. This is positive evidence for the buffer-consumption boundary, not a whole-control return. |
| Same [overlapped source][io-source] | `GetResult` runs `SafeFileHandle.TryToReuse` in its `finally`. That resets the source and either caches it in the handle's single reusable-source slot or disposes it. A cached source and its preallocated-overlapped graph survive the read. A cache miss or concurrent checkout can construct another source. |
| [ValueTask source core][value-source], [raw worker wrapper][worker] | Source completion clears stored continuation state/context and queues the ordinary non-flowing continuation when required. Its generic default-context wrapper clears its callback field before invoking the callback. These are candidate enclosing-suffix facts, not permission to omit wrapper/queue/cold-state provision. |
| [Portable I/O callback][portable-io], [bound-handle callback][bound-io], [Windows-pool callback][windows-io] | Selected unsafe callback branches return after invoking the callback, while the Windows-pool outer path still performs completed-work bookkeeping and callback-wrapper exit. Identify and fund the actual selected enclosing path; a native callback name does not prove its entire suffix. |
| [Context notifications][notifications] | Restoration can invoke caller AsyncLocal notification bodies and those bodies can reenter/change context. A thrown context-change notification follows this runtime's `FailFast` path; it is not an ordinary recoverable Scene Task fault to translate. Preserve that policy. |
| [Fault holder][fault-holder], [CoreCLR capture][exception], [native freeze][native-freeze]/[clone][native-capture] | Task exception publication captures new EDI for Exception inputs, but can internally retain existing EDI inputs. Native capture can clone stack and keep-alive arrays at their **old capacities**. Two exception references do not bound those bytes or work. |
| [Same-type Task relay][relay] | `TaskCompletionSource<PreparedScene>.TrySetFromTask` can relay existing dispatch state only from `Task<PreparedScene>`, not the current planner/resource Tasks. It neither supplies their return nor removes initial capture. |
| [File-stream close][stream-close], [SafeHandle release][handle-close], [Windows handle release][io-source] | The unbuffered strategy disposes binding then handle. SafeHandle commits closed/disposed state before `ReleaseHandle` and ignores its Boolean result. Retaining a stream after failure cannot reconstruct severed ownership; normal managed return alone is not OS-close attestation. |

**Inference:** an operation-specific ordinary caller can replace the selected read's opaque stock invocation boundary. It can observe its own calls, result consumption and context scopes returning. This is not a theorem that public .NET APIs cannot implement an invoker.

**Missing evidence:** final source-to-binary/code-generation correspondence, complete storage/coexistence charges, all supported context/fault branches, shared dispatch ownership, and real native return/release closure. No build, test, allocation probe or runtime/native experiment supplies them in this proposal.

## 3. Selected owner/caller protocol

`AssetLoadOperation<T>` is the previously proposed Assets progress owner, not a second preparation root. New method labels below describe private responsibilities, not added public declarations or a completed layout catalogue.

### Installation and acquisition

1. The existing root, under its gate, requires Building/Running state and an empty child slot. It elects construction and subdivides the complete original-P claim **before** calling the empty-owner factory. Construction/start/install interests remain charged across gate release.
2. Construct outside gates, acquiring no backing, Store credit, I/O or result. Install that exact owner before authorizing its one start. Failed installation starts nothing. Retain the current phase/pending owners; the Assets slot must not overwrite the builder or stable use anchor.
3. An ordinary Assets acquisition step acquires the source reference **inside this installed owner**, then records every reference/credit and exact rollback disposition before the next fallible step or escape. Include the temporary anchored reference when typed admission rejects. Preserve existing source/lifetime/backing/Store lock ordering; do not hold the preparation gate during lower acquisition.
4. Revalidate the exact descriptor and source facade. A stale Store-current descriptor refuses; an admitted old anchored backing never rebinds to current. Keep the existing descriptor, codec, credit, cancellation and target-validation precedence.

### Concrete invocation and suspension edge

| Caller → callee | Sole responsibility and returned boundary |
|---|---|
| Existing `PreparedScene` entry → ordinary Assets advance step | Acquisition, allocation, decoding and automatic-reference progress run only under the installed owner and prior claims. Reuse `AssetEnvelopeDecoder.Decode`/`Inspect` and the exact Scene/Prefab codec `Read` calls, including their existing diagnostic translation. |
| Assets step → real `RandomAccess.ReadAsync` | Stage the destination, exact offset/remaining extent, backing reference and outstanding-read state before issue. Return its existing `ValueTask<int>` as a one-shot lower consumption obligation; do not wrap it in another Assets Task or export a raw handle. |
| Runtime invoker → BCL readiness/registration/`GetResult` | The root owns that transferred single-consumption obligation. Register directly through the lower awaiter; no Assets-to-Runtime callback or funding upcall is required. The Assets owner retains physical inputs until Runtime supplies the exact count or original failure through an ordinary Assets call. No second consumer, premature reset or repeated `GetResult`. |
| Runtime invoker → Assets accept/advance/cleanup | After `GetResult` returns or unwinds, including its recycle/disposal `finally`, report that outcome once. Assets performs existing short-read/EOF checks, decode/reference steps or cleanup. Those ordinary calls must return/unwind before their invocation interests end. |
| Runtime invoker → existing planner destination | Install the exact staged lease and its retained charge under the installation/close election. Acknowledge that exact transfer once. Keep the source operation responsible until acknowledgment; Task readiness or a historical field assignment is insufficient. |

This division changes no public Assets entrypoint. Ordinary public Assets reads, package opening, package pins and unselected restore/measurement retain their existing APIs/owners. Passing a numeric allowance does not give Assets a second purse. Runtime pays its invoker from the original use; Assets executes only the closed implementation whose complete claim was preaccepted.

The actual outer Runtime invocation must enclose the semantic step **and its context scope**, not merely call a step that completes another Task:

```text
prepaid entry / registration / wake / invocation interests
  -> ordinary lower calls and exactly-once I/O consumption
  -> those calls return or unwind
  -> owned context/restoration and any publication call return
  -> old read/task/context/result aliases end or transfer
  -> exact outstanding interests join
  -> original child temporary-claim handback
  -> independently funded parent-only tail, or proved no-owned-access suffix
```

The one pending-read limit is not a bound on callback-before-registration-return, queued/running invocations or parent tails. Serialize progress with a checked running/pending-wakeup election and synchronized exit recheck. Derive their maximum coexistence from the final control flow. Construction, registration uncertainty, cleanup or old-generation invocation interests prohibit slot reuse. An independently funded parent invoker may continue only after every old child alias/obligation has ended or transferred; do not hide an old child tail in a new operation.

The handback is the last operation touching the retired claim. Prove gate exit, exception paths, raw callback wrapper, dispatch reset and selected native suffix as well. If a suffix remains under a real parent/backing/runtime grant, identify that grant and transfer edge; otherwise the branch remains unavailable. A flag written inside `MoveNext`, another completion Action, elapsed time, GC or normal-success permanent quarantine cannot supply this condition.

### Context and fault preservation are part of this boundary

- Preserve initial entry isolation using the existing enclosing `Start` return. A synchronously completed read stays in its current logical invocation; do not force a yield or introduce blocking synchronous I/O.
- At each genuine selected suspension, preserve the context that the current logical await would capture. Distinguish default, nondefault and suppressed flow. In the inspected runtime the suppressed box sentinel skips context application; it does not mean “flow the suppressed caller's AsyncLocals” or “reapply suppression on every worker.”
- A non-flowing lower wake plus explicit owned context invocation is the selected mechanism to develop, **not a mechanical `OnCompleted` → `UnsafeOnCompleted` change**. Prove synchronous completion, registration races, notification/reentrancy, restoration and absence of arbitrary caller SynchronizationContext capture. Do not collapse logical context boundaries merely because adjacent decoder steps are synchronous.
- Retain the [handoff proposal's narrowly scoped external-observer provenance decision](m03b-scene-preparation-handoff-closure.md#7-public-task-producer-exact-clr-gate-and-proposed-closure) as pending authority. Caller-created observer dispatch is not a Scene capacity bound. Any analogous attribution for caller-owned AsyncLocal notification bodies needs explicit owner review; Scene-created context holders, adapters, restoration machinery and publication remain charged. No blanket free-runtime exemption is selected.
- Carry an original primary fault/token between the selected ordinary steps as owned state, instead of repeatedly throwing through heterogeneous intermediate Tasks. Catch within the owned semantic invocation before leaving its context callback where possible; preserve the existing codec translations and primary/cleanup ordering. This removes particular recapture sites, not every capture.
- Catalogue the remaining **actual** exception origins: fresh built-in validation failures, fresh runtime I/O/cancellation exceptions, native-source EDI/`GetResult`, lifetime/backing retained failures, cleanup and public Task publication. Establish their stack/keep-alive capacities, multiplicity and pre-capture issuer. Exact factory codecs remove arbitrary codec bodies, not caller-context behavior or every retained-failure relay.
- Keep current public delivery: synchronous usage/pre-cancellation ordering, original execution fault first and distinct first cleanup failure second; cleanup failure faults an otherwise canceled operation; successful cancellation preserves the selected token. Do not substitute exceptions, cap a foreign exception graph, mutate `Exception.Data`, or silently convert a publication failure to success.

If that origin catalogue cannot derive a useful funded capture bound, the missing capability is a **real budget-aware capture or non-copying dispatch-state relay at the named lower fault edge**, including destination holder provision. Today's same-type relay is not that bridge. Do not declare a stub, use reflection into CoreLib, or claim that retaining `Exception` makes its next capture free.

## 4. Symbolic funding and ownership ledger

All symbols denote disjoint existing-owner charges or reserved capacity, with checked arithmetic. They are not new quotas or numerical profile values.

```text
P = the original SceneLoadOptions.MaximumPreparationBytes, unchanged
B_op = 256 + P
P = F + X + T
U = 256 + F + X

F: still-available original workspace capacity
X: active original workspace, including retained cleanup debt
T: capacity irrevocably transferred out, including receivers subsequently released
```

P retains its existing default `67_108_864` and range `[1, 2_147_483_648]`. The use-control 256 bytes are not spendable Scene scratch. The exact parent's owned bytes plus all its independent-use charges must fit the configured World ceiling; all also remain subject to the existing domain limits. Neither a profile nor this subdivision raises those ceilings.

Use the existing `A(n,s) = 64 + RoundUp16(n*s)` for separately allocated arrays. This is the domain accounting rule, not a measurement of every CLR allocation. Whole objects, lazy locks, collection growth, runtime/native controls and cold state need independent final-lane charges.

Partition active preparation provision as follows; never spend protected or fixed terms as child scratch:

```text
X = J + K_body + K_publication + O + W_planner + H_results
    + sum C_active-read

C_read = F_read-control + W_read/decode/inspect
         + I_entry/registration/invocation/return
         + H_result-upper + K_read-cleanup/fault
```

`J` covers the final whole root/shared producer controls and their overlap, not just reference fields. Fixed root invoker/callback storage charged there is not charged again to per-read `I`; the latter covers additional per-read machinery/capacity. Common protected K and child-specific K likewise have disjoint membership. `O` covers option-copy/sort coexistence. `H_results` includes previously accepted root/cache values while another read runs. `H_result-upper` prepays the complete possible lease/state/lifetime, semantic graph and surviving copies before decoding; exact-count settlement may reduce it afterward, never fund earlier allocation retroactively. Move its actual surviving subset into `H_results` at exact handoff; refund only ended temporary/unused provision.

For descriptor decoded envelope length `d`, immutable limits `L` and exact schema metadata `S`, the existing structural facts still provide:

```text
r = min(L.MaximumAggregateRecords, floor(d / 64))
n = min(L.MaximumArrayElements, L.MaximumAggregateArrayElements, r)
R_scan = min(r, 1_048_576)
q = min(descriptor.MaximumDecodedAssetBytes, descriptor.PackageByteLength)
```

Here `r` bounds validated record headers, `n` bounds a record array rather than every primitive array, `R_scan` bounds unique scanned references, and `q` bounds one admitted same-backing None target length. The record-header width and scanner ceiling are existing format/source facts, not object allowances. Full structural validation precedes semantic actions; include malformed scanner/diagnostic paths and bounded semantic-invalid prefixes. Derive actual site multiplicities, reader-specific limits, metadata walks, collection spare/growth capacity, copies and coexistence from `d,L,S`. Close the actual admitted application metadata S and its independent retained ownership; a factory receipt does not freeze that entire registry.

`A(d,1)` and even `R_scan*A(q,1)` are only direct payload-array terms. Neither expression pays decoded values, the scanner's List/HashSet, eight-array hierarchy workspace, retained results, invokers or K. The workspace's current eight-byte local rounding is not a replacement for domain accounting.

For retained Scene data `D`, let `Q` be the final qualified receiver-control provision and `R = D + Q`. Let `B_parent` be the exact parent's owned-byte ledger and `S_parent` its included installed-Scene-storage ledger. Reuse the existing preclaimed construction and covered-workspace transfer. The ordinary fully covered installation performs:

```text
X -= R; T += R; U -= R
B_parent += R; S_parent += R
B_parent + sum of this World's U, and the domain total, are unchanged
```

A partially covered transfer requires its existing `+R/-C` admission and changes attribution by `R-C`; this proposal supplies no extra entitlement. Release of installed R reduces its receiver charge, **not** F or the original P. Retain the actual receiver/World/binding through close and failed release. Each concurrent independent operation still needs its own `256+P`; each activation keeps its separate captured-P admission. The current XML/code dual preparation/activation use and incomplete dirty-contract reconciliation are not repaired here.

### A newly identified, longer-lived I/O term

The real source requires a separate term:

```text
Keep(backing) includes I_cache:
    the one parked reusable I/O-source capacity,
    its preallocated-overlapped/handle-related retained graph,
    surviving cancellation-registration/input aliases,
    and its actual final-close/return provision
```

Checked-out sources and per-read wrappers/registration work belong to the active read claim unless another **established** original grant pays them. Returning a source into the preprovided cache is a lifetime transfer, not proof it was destroyed. A vacant reserved cache capacity and an active checkout are distinct capacities; do not charge one physical source twice or leave the transition unfunded. Include simultaneous old checkout/return/next issue where the actual runtime permits it.

**Distinguishing source-derived schedule, not an experiment:** a None read's `GetResult` returns, then its lease is disposed while the Store stays open. The payload pin has ended, but the cached source can remain. Refunding all read-control provision as temporary would therefore refund surviving storage. `TryToReuse` resets `_source`, not the complete containing object; classify surviving fields/inputs rather than assuming cancellation-registration disposal clears every alias.

Today's decoded-wire/decode/lease counters do not establish this managed/native provision. Nor does an open FileStream, warm cache or historical package read prove who paid it. Shared ThreadPool queue growth/cold dispatch also needs a named independent issuer or a complete attributed term; code merely being in CoreLib is not exemption.

The concurrently present [Assets bootstrap/close proposal](m03b-assets-prelease-bootstrap-and-close-closure.md) selects an **explicitly conditional** same-envelope package-control earmark. This Scene proposal neither approves that compatibility/budget change nor implements package opening. If separately authorized, its `Keep(g)` must include `I_cache` and the final supported runtime closure. Otherwise this exact original backing-lifetime authority is absent. Keeping normal read slots occupied until Store close, charging a successor Scene use, or inventing an independent Store pool is not a substitute.

## 5. Bounded prerequisites and implementation batches

The next work is not a transitive planner rewrite or another aggregate allocation probe.

| Batch / owner | Deliverable and exit condition |
|---|---|
| **Close the real lower port — Assets and Scene ownership reviewers** | Specify the ordinary-call/read-token protocol above for the exact factory None read; identify the original payer/root/end of `I_cache`, shared dispatch and close. Resolve the bootstrap proposal's still-unapproved scope change and missing Chapter 11 recovery definition, rather than treating spare wire-budget capacity as authority. Produce the context branch table and exception-origin/capture ledger for this one path. Unassigned funding/ownership blocks implementation admission; numerical terms may be closed against final compile-only layouts in the next batch, never by running an unfunded operation. |
| **Implement the selected read seam — Runtime caller and Assets progress owner** | After those authorities are complete, add the one fixed child slot/election, preclaim, empty owner, inside-owner acquisition, real I/O single-consumption handoff, ordinary semantic/cleanup steps and exact result acknowledgment. Extend the existing root for the actual invoker; no new preparation carrier. Keep the footprint null. Compile and inspect this real code before converting recursive planning. Final fields/capacities replace proposed names as catalogue inputs. |
| **Close the selected planner/root — Scenes** | Replace the selected planner's Task/ValueTask suspension plumbing with operation-specific progress in its existing builder/root, preserving traversal and context semantics. Cover root loading, counting, override/selector lookup, root-count filling, expansion and Prefab cache insertion. Fund builder initializers, captured reservation-call envelopes and validation scratch before construction. Replace the supplier latch only with fulfilled joins; remove selected intermediate Task observers, not the public promise. Leave Rendering absent and unselected Persistence paths with their real original issuers. |
| **Profile, then observe actual consumers — owning Assets/Runtime tests** | Close final `C_read`, J/K, retained graph/receiver and native-return terms, including cold/failure paths and useful fit under unchanged legal P. Only a genuinely complete lane may later be considered for footprint installation and real Scene consumers. Read-seam compilation, lower-owner tests and empty-Scene restore are not that decision. |

**Next concrete action:** resolve the first row against the real `RandomAccess`/SafeFileHandle cache and the existing unpublished-Store bootstrap decision. Its output must identify a permitted original backing-lifetime debit and return disposition, plus a fulfilled non-flowing-wake/context/fault mapping—not another `Returned` flag or an unspecified “runtime-owned” allowance. Then implement the single read seam behind the guard. If the budget-scope authorization is rejected, the required alternative capability is an independently authorized, completion-owned lower read with no unprovided retained cache; no such replacement is asserted available here.

## 6. Required focused tests and observations

These are future checks, not results from this task. Use ordinary owning-project NUnit/VSTest commands from `docs\project\Building.md`, Debug before Release, and verify nonzero selected/executed membership with fresh TRX. No new runner or acceptance IDs.

| Area | Required distinguishing behavior |
|---|---|
| Original grant and slot | Exact/one-byte-short complete claims refuse before owner construction/acquisition; preserve original P and World/domain totals. Construction/start/close and callback-before-registration-return cannot reuse the slot or refund a live claim. Use actual lower owners, not fabricated grants or return acknowledgments. |
| Real packages and identity | Extend `AssetStoreTests.cs`, `ScenePrefabPackageLeaseTests.cs` and registration tests for exact factory receipts, stale-current refusal, old anchored backing through replacement, automatic None target rejection before target read, and actual schema checks after inspection. Preserve ordinary Assets None/Brotli behavior. |
| Acquisition/result/cleanup | Source/Store close around temporary-reference acquisition; cancellation at existing checkpoints; semantic-invalid root/Prefab and target failures; exact root/cache installation versus close. Preserve original primary/token and cleanup ordering; verify irreversible release progress and once-only accounting. A no-op repeated Dispose is not release evidence. |
| Context and invocation | Actual synchronous and suspended reads, default/nondefault/suppressed flow, AsyncLocal notifications/reentrancy, changed context restoration, custom caller SynchronizationContext without capture, and registration/completion races. Check the real invoker and its final compiled suffix, not a test-only dispatcher or naked unsafe callback. |
| Runtime cache and return | Attribute cache miss, reuse, concurrent checkout/return and final backing close on the selected real runtime/ThreadPool path. Observe native buffer end separately from source recycle and outer callback return. Complete source/IL/native analysis plus actual observations must cover the claimed modes; a sampled/warm allocation count is insufficient. |
| Public publication/body | Hold real observer dispatch after Task terminality, end/dispose/admit the body concurrently, and verify J/K/use retention until publication and actual invoker return. Preserve the existing default-cap, nonempty-binding and pixel-17 retention consumer for later composite qualification; do not weaken it to fit this slice. |

Where real suspended/short-read, constructor-failure, final-close or native-return stimuli cannot safely reach the required boundary, record that specific missing stimulus/capability and retry condition. Do not forge producers/snapshots, insert production failure hooks, fabricate OOM, or count an unreached selection as a pass. NativeAOT, other targets/runtime modes, public renderer consumers and Chapter 35 acceptance remain separate.

## 7. Hard NO-GO conditions

- Acquisition before owner installation; a read token with two consumers; result/cleanup escape; cached controls losing provision; or transferred result bytes refunded with temporary workspace.
- Remaining P, historical constructor sizes, decoded wire length, lease counts or a fixed exception count substituted for a complete pre-work charge. An always-refusing formula does not establish a useful supported lane.
- Task terminality, source `finally`, `GetResult` alone, unsafe registration alone or another observer used as producer/invoker return. Every retained suffix must end or transfer to a named, already funded real owner.
- Changed usage/cancellation precedence, context suppression/capture, original fault identity/order or public completion behavior used to obtain a smaller profile.
- An unknown native constructor/close disposition called successful release. SafeHandle logical closure, a throwing FileStream subclass or a later Dispose cannot supply the missing native acknowledgment.
- Unapproved Assets bootstrap earmarks, upward Runtime funding callbacks, a successor use, normal-path permanent quarantine, new worker/pool, or M05 diagnostic provision used to fill the gap.
- Enabling preparation, nonempty restore or a Scene-dependent submission from this proposal, the phased restore tests or a lower read proof alone. Full planner/root/receiver/publication closure remains required.

## Decision input freeze and preservation scope

Captured UTC `2026-11-19T10:02:15.1005339Z` at the HEAD above. This is the bounded **analysis input set**, not a repository inventory, complete allocation catalogue or binary/native qualification. Dirty maintained contracts are inspected context, not accepted amendments. No schema/fixture version is changed.

The exact 87 local inputs are grouped below; each filename takes its group's prefix unless a full path is given:

```text
Configuration:
.github\copilot-instructions.md, .github\workflows\ci.yml,
Directory.Build.props, Directory.Build.targets, Directory.Packages.props,
global.json, sources\Directory.Build.props, tests\Directory.Build.targets

docs\project\:
Building.md, Roadmap.md
docs\design\:
02-dotnet-api-memory-and-ownership.md, 04-capacity-and-workload-profiles.md,
11-assets-import-streaming-and-packages.md, 12-entities-components-and-queries.md,
16-scenes-transforms-and-cameras.md, 29-project-setup-build-and-deployment.md,
30-public-api-review-surface.md, 35-implementation-roadmap.md
docs\proposals\:
m03b-assets-acquisition-failure-owner.md,
m03b-assets-prelease-bootstrap-and-close-closure.md,
m03b-assets-prelease-operation-profile-capacity-addendum.md,
m03b-assets-prelease-recovery-owner.md,
m03b-resource-bundle-preconstruction-owner-and-return-closure.md,
m03b-scene-built-in-codec-profile-admission.md,
m03b-scene-none-profile-pre-read-and-owner-reconciliation.md,
m03b-scene-none-read-owned-invocation-boundary.md,
m03b-scene-preparation-finite-envelope-and-return-addendum.md,
m03b-scene-preparation-handoff-closure.md,
m03b-scene-preparation-owner-aware-async-handback.md,
m03b-scene-receiver-control-funding.md,
m03b-world-binding-owner-retirement-fence.md,
m03b-world-restore-handle-actor-enrollment.md,
m03b-world-restore-operation-owner-alignment.md,
m03b-world-restore-phased-owner-protocol.md

sources\TerraFX.Assets\:
AssetCodecRegistrationReceipt.cs, AssetEnvelopeDecoder.cs, AssetLease.cs,
AssetLeaseLifetime.cs, AssetOwnedReadDescriptor.cs, AssetPackageBacking.cs,
AssetPackageReader.cs, AssetPayloadReader.cs, AssetReferenceScanner.cs,
AssetStore.cs, AssetStore.PackageLease.cs, AssetStore.ReadDescriptor.cs,
AssetStoreOptions.cs, AssetStoreRegistration.cs, AssetStoreRegistrationBuilder.cs,
NativeAssetSchemaCatalogue.cs, TerraFX.Assets.csproj
sources\TerraFX.Core\:
EbinCodec.cs, EbinReadLimits.cs, EbinScanState.cs, EbinStructuralReader.cs
sources\TerraFX.Runtime\Entities\:
World.cs, WorldAdmissionDomain.cs, WorldAdmissionDomain.SceneStorage.cs,
WorldGuidUse.cs, WorldGuidUseControl.cs, WorldSceneStorageReservation.cs
sources\TerraFX.Runtime\Hosting\:
WorldLoadExecution.cs, WorldLoadPreparedState.cs, WorldPersistence.cs
sources\TerraFX.Runtime\Scenes\:
PreparedScene.cs, PreparedScene.Preparation.cs, SceneAssetCodec.cs,
SceneAssetReader.cs, SceneAuthoredComponentRecord.cs, SceneEntityReader.cs,
SceneExpansionPlan.cs, SceneHierarchyValidationWorkspace.cs, SceneLoadOptions.cs,
SceneLoader.cs, ScenePersistenceRestorer.cs, ScenePrefabAssetCodec.cs,
ScenePrefabExpansionPlanner.cs, SceneSchemaCatalogue.cs, SceneSupport.cs
sources\TerraFX.Runtime\TerraFX.Runtime.csproj

tests\TerraFX.Assets.Tests\AssetStoreTests.cs
tests\TerraFX.Runtime.Tests\Scenes\:
PreparedSceneAssetRetentionTests.cs, SceneAssetStoreTests.EngineHost.cs,
ScenePrefabPackageLeaseTests.cs, SceneRestorePreparationGateTests.cs,
SceneSupportRegistrationTests.cs
tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj
```

Digest recipe: SHA-256 of UTF-8 without BOM/trailing newline, over ordinal-path-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines joined with LF. Exclude this proposal from its own inputs.

| Input | SHA-256 |
|---|---|
| 87-file set | `0bce7e8054d396655f4c60620767690db4e9a83d53b5be970f12dabd1c0ef8e2` |
| `SceneSchemaCatalogue.cs` | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |
| `NativeAssetSchemaCatalogue.cs` | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| All 22 existing dirty/untracked paths at freeze, same recipe | `0015dc939dca630b1f68f23a904b1af2fb4c8eef080c6d5a01437aace865543e` |

All initial 22 dirty/untracked files were rechecked against their original raw hashes and remain unchanged. The additional Assets bootstrap/close proposal appeared and was committed concurrently; its inspected bytes are unchanged and included in this freeze. Recheck scoped membership, HEAD, dirty state and hashes before disposition. Changed inputs require reassessment. The runtime source links and CoreLib identity are separate pinned inputs, not evidence that a consumer loaded those bytes.

Only this new proposal is authored. No source, maintained contract, test, other proposal or index edit; no build/test, runtime experiment, commit, push or PR is part of this task.

[box]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncTaskMethodBuilderT.cs#L510-L579
[value-builder]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncValueTaskMethodBuilderT.cs#L45-L105
[start]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/AsyncMethodBuilderCore.cs#L20-L55
[context]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs#L54-L306
[awaiter]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/CompilerServices/TaskAwaiter.cs#L438-L450
[action]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs#L634-L728
[dispatch]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L990-L1018
[random-access]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/RandomAccess.Windows.cs#L254-L349
[io-source]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.OverlappedValueTaskSource.Windows.cs#L16-L263
[value-source]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs#L65-L264
[worker]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L1271-L1296
[portable-io]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/IOCompletionCallbackHelper.cs#L33-L58
[bound-io]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolBoundHandleOverlapped.cs#L36-L54
[windows-io]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolBoundHandle.WindowsThreadPool.cs#L145-L162
[notifications]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ExecutionContext.cs#L329-L455
[fault-holder]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs#L145-L187
[exception]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs#L239-L249
[native-freeze]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/comutilnative.cpp#L83-L118
[native-capture]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.cpp#L1629-L1738
[relay]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskCompletionSource_T.cs#L324-L345
[stream-close]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/OSFileStreamStrategy.cs#L111-L130
[handle-close]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/SafeHandle.cs#L224-L261
