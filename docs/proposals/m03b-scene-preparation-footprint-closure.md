# M03-B Scene preparation footprint and last-access closure

**Status: tentative gate-closure procedure, not a closed gate or authorization to implement the production path.** The finite J/K catalogue and producer-last-access condition in [handoff closure](m03b-scene-preparation-handoff-closure.md), sections 5/7/9, remain prerequisites to production implementation. Source inspection has not established them. This addendum specifies the bounded prototype, derivation and observations needed to remove that blocker; it supplies no numerical allowance.

## 1. Decision and scope

Use the handoff proposal's stable `PreparedScene`, explicit asynchronous-continuation TCS, three ordinary driver parts and joined producer/body ownership. First compile the concrete private candidate for inspection, derive its claims, and establish its release boundaries; only then execute it as a funded qualification candidate. Do not require a qualified size before allowing the compile-only experiment needed to determine that size.

Preserve:

- Exact captured `P = MaximumPreparationBytes`, its existing default/range, preparation admission of `256 + P`, and independent activation admission of the same amount. Neither the control's 256 bytes nor activation's current bootstrap/completion constants fund preparation J/K.
- The handoff's **external-continuation provenance rider**: external registrations own their continuation/list/context/scheduler work and runtime adapters/queue capacity created solely to dispatch those registrations, including inside `TrySet*`. External observer-requested metadata follows its observer. This is an explicit proposed policy, not an effect of TCS options.
- Scene ownership of its own callbacks, child registrations, context/dispatch holders, publication bookkeeping, diagnostics and cleanup. The rider does not exempt Scene-created continuations or all CLR work. A captured caller-owned input reference and a newly allocated Scene-owned holder are different rows.
- Existing synchronous usage/freeze errors, pre-cancellation ordering, admitted fault/cancellation channels and selected token, original-fault identity, bounded original-cleanup sampling, admission-only body capture, reusable queue refusal, and the separate activation delivery tail.

Planner traversal/cache/frame quantities, Rendering helpers, Assets payloads and Persistence preflight remain with [workspace closure](m03b-scene-preparation-workspace-closure.md). This addendum requires their **producer-boundary evidence**, not another inventory of their allocations. It changes no public declaration, schema, diagnostic, acceptance ID, quota, process-U role or maintained milestone state.

## 2. Evidence and limits

| Frozen owner/source | Consequence for this gate |
|---|---|
| Chapter 02 section 3 | Task terminality requires ended/transferred input consumption; completion is outside gates with asynchronous continuations. ExecutionContext and caller scheduling semantics survive changing the orchestration syntax. |
| Chapter 04 section 3 | Charge complete capacity, headers and coexistence before allocation. Separately owned controls/Tasks/backing need a named grant; a measured payload size is not an allowance. |
| Chapter 12 process-registry owner table; Chapters 16/30 Scene-loading contracts | One exact preparation use funds the working/result/task envelope. Aliases do not create release rights; cleanup and quarantine retain the original grants. |
| `SceneLoader.cs:145-222,246-397`; `PreparedScene.cs` | Current freeze preclaims two binding backings, but the result is late and the public Task is compiler-completed. These bytes cannot determine the proposed root/driver layout. |
| `SceneActivationRequest.cs`; `SceneComponentRestoreContext.cs`; `SceneActivationExecutor.cs:227-320` | Body return must follow executor/callback unwinding and snapshot revocation. Changed request fields need their own activation requalification, not a preparation grant. |
| `WorldGuidUseControl.Release`; `WorldAdmissionDomain.ArrayCharge` and workspace claim/return methods | Lower release rejects active workspace. Successful release clears the anchor; partial lower failure is not an atomic successful drain. Keep the stable root and exact release debt. |
| Exact .NET sources identified by handoff sections 7/10 | Builder completion and state/context clearing can follow the source body. Task result visibility precedes continuation dispatch returning. Raw Action dispatch can allocate per registration; fault publication creates contingent/holder/list/EDI state. |
| `SceneAssetStoreTests.PreparedAssetLeaseRetainsBundleAcrossActivationUnloadAndDispose` | Contains a real caller-owned blocking scheduler for activation publication. It is a reusable stimulus pattern, not an executed preparation-tail observation. |

The eight upstream files and hashes were independently verified in the preceding read-only review at monorepo revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; this draft imports those bounded observations through the frozen handoff document. It performs no new download or runtime experiment.

In particular, `Exception.CoreCLR.CaptureDispatchState` calls `GetFrozenStackTrace`. That managed source alone does **not** bound native capture work or an arbitrary exception's graph. Likewise, the existing three-part counter does not join child async frames, continuation adapters or context-restoration epilogues.

### Current narrow disposition

| Cost/boundary | Derivable from the reviewed source or frozen policy now | Requires the concrete prototype/profile |
|---|---|---|
| Existing control and options | Exact `256 + P` admission; the two-copy `2*A(n)` peak and one retained `A(n)`; snapshot value copies allocate no separate snapshot object. These are existing control/options charges, **not J/K values**. | Final sort/cold-helper provision and its coexistence with the new bootstrap. |
| J: root/driver/bootstrap | The current root's fields/Lock and loader's async orchestration are visible. The handoff specifies two callback delegates and three ordinary parts, but those proposed fields/methods are not the current implementation. | Final whole-carrier layout, TCS/Task/delegate/registration/context holders, lazy synchronization, cold state, multiplicities and actual release intervals. No numerical J bound is established. |
| K: protected terminal/cleanup | Managed Task source identifies contingent state, exception holder, List/EDI bookkeeping and completion dispatch. The handoff bounds publication input to one execution exception plus one original cleanup sample, not their foreign graphs. | Exact copies/capacities/layout, native dispatch-state capture, cancel/refusal paths, cleanup/publication overlap and final release bookkeeping. No numerical all-path K bound is established. |
| Producer last access | Task terminal visibility can precede dispatch returning; TCS returning exposes an ordinary publisher boundary. Neither fact proves a child frame or dispatch/context wrapper has ended. | Final source/native return-site proof and production-valid end/transfer edge for every remaining Scene-owned producer provision. |

**Go/no-go:** only the bounded compile/inspect prototype in section 5 is proposed for separate authorization now. Production implementation is **no-go** until the frozen final candidate/lane has (1) a finite, preclaimed, disjoint J/K catalogue with no unexplained Scene-owned row, (2) a valid last-access/transfer edge for every section 6 producer, and (3) the required funded behavior observations in section 7. A missing row, unobservable Scene-owned epilogue or unexecuted required variant is a specific failed gate, not permission to choose a margin or start the broader allocation work.

## 3. Fix the qualification lane before deriving a number

The locally identified lane is Windows x64, SDK `11.0.100-rc.3.26522.148`, CLR `11.0.0-rc.3.26522.148`, `net11.0`, `LangVersion=latest`, `Features=strict`, with the source-folder `SkipLocalsInit` default. The binary/metadata hashes in section 8 identify this observation, not a shipping-support certification. `global.json` still permits roll-forward; do not turn developer SDK selection into an exact pin.

| Lane | Required separate evidence |
|---|---|
| Baseline managed candidate | Debug first, then Release, on the identified runtime/architecture; include every enabled JIT tier or precompiled version that can execute the methods. Record actual optimization/tiering settings rather than assuming one disassembly covers all executions. |
| Engine `runtime-async=on` | Separate compiler/source/native-code and lifetime evidence, with the same caller semantics. Engine baseline syntax does not imply the BCL has no Runtime Async. Do not enable this lane by changing shared defaults. |
| NativeAOT | Publish and execute the actual selected public consumer on its target OS/architecture, following `docs\project\Building.Consumers.md`. Managed layout and JIT return sites are not AOT evidence. Windows x64 can be the first candidate; it establishes nothing for Linux x64 or another target. |
| Diagnostic/instrumentation variants | Declare modes supported by the selected artifact before deriving claims: ordinary execution, applicable Task/TPL tracing, debugger/profiler-enabled dispatch and context paths. Cover enabled alternatives or explicitly leave that mode unqualified; do not silently omit a branch because it allocates. |

Chapter 29/35's mandatory NativeAOT/public-consumer obligations remain mandatory for the corresponding product claim. A managed-only gate result must be labeled managed-only; optional-lane deferral is not acceptance of an unexecuted mandatory lane. A compiler/runtime update or changed private fields invalidates the affected catalogue and return-site proof, not unrelated behavior evidence automatically.

## 4. Derive a disjoint catalogue from the compiled candidate

### 4.1 Required row and ownership partition

For each newly owned allocation/capacity, record: exact creating call site/type; maximum multiplicity or input-derived capacity; byte derivation for this lane; containing grant; earliest allocation; all simultaneously live replacements; last consumer/transfer; release site; and source/native-code evidence. Reuse an existing named grant only with its owner's explicit coverage and end condition.

Use the handoff's four ledgers:

| Partition | Required contents and end condition |
|---|---|
| `J_shared` | The **whole** final root carrier, gate/Lock, embedded state/debt fields and any separately allocated shared control. Include lazy/inflated synchronization state. It remains funded through both producer and body completion; clearing producer fields does not shrink the allocation. |
| `J_producer` | Explicit TCS and exact `Task<PreparedScene>` shape, both bound callback delegates, pending-stage/registration/context/call holders and any other producer-only allocations. Include inline/suspended and racing-registration peaks. End only at the section 6 last-access boundary; completed immutable observation ownership may then pass to the caller. |
| `K_body` | Protected Scene-owned bookkeeping needed to close the Building/Available/Admitted body, preserve one original cleanup sample and debt, and retain a failed graph. Embedded fields are already in `J_shared`, not charged again. Keep the cleanup-dependent partition on actual failure. |
| `K_publication` | The complete supported success/fault/cancel/capacity-refusal publication peak: precreated two-exception source array where used, TCS enumerable-overload copy/enumerator/List capacity, contingent state, exception holder/EDI backing, cancellation state, required new contextual exception/message, and final release bookkeeping. Include cleanup/publication overlap; optional work cannot spend it. |

An object allocated during bootstrap may belong to K rather than J. Count the object once and reserve it before construction. Never refund a whole root carrier while its body is still engine-owned, or reuse protected bytes solely because the public Task is already terminal.

For non-array objects, derive the aligned managed instance extent from the final field layout, object header and runtime allocation rules, then corroborate with the exact closed type's allocation/layout observation. `Unsafe.SizeOf<class>()`, field-count estimates and constructor-skipped probes alone are insufficient. Inventory native/lazy state separately where present.

Arrays use Chapter 04's `64 + RoundUp16(checked(capacity * stride))`, including spare capacity and full old/new coexistence. Use actual payload stride only for inline values. Shared empty backing is different from allocating a zero-length array. Strings, delegates, generic Task shapes and boxed/enumerator objects require their own layout/creation rows, not an array payload shortcut.

Cold generic/comparer/delegate/Lock/Task/runtime-metadata paths need an explicit owner and lifetime. Warming them before measurement is not a disposition. If a once-created cache survives the operation, either demonstrate its already funded existing owner or leave that precise row unresolved; do not refund a per-operation claim while still owning that cache, add a new global owner, or quarantine normal work.

### 4.2 Compute the protected peak, not a sampled total

Enumerate the source-supported branches: success; one execution fault; execution fault plus the first cleanup failure; cancellation with its selected token; cancellation followed by cleanup fault; contextual capacity failure; synchronous freeze/construction failure; and publication/release failure before or after terminal visibility. Additional cleanup failures retain only the handoff's bounded numeric facts and unreleased owners, not another exception list.

For each branch, follow allocations through the actual TCS overload and native/runtime helpers until every new object has a row. Preserve original exceptions as original identities with a named retained input owner; do not clone, truncate, stringify or replace an arbitrary foreign exception graph to manufacture a constant. New EDI/capture/copy work remains a Scene obligation.

The closure must include Task await/registration, ExecutionContext restoration, Lock contention/inflation, ThreadPool dispatch and the native implementation reached by `GetFrozenStackTrace`, plus the chosen Runtime Async/AOT counterparts where applicable. These are **next proof inputs**, not source already reviewed by this draft: resolve their actual files at the selected revision, hash them, and add their transitive allocating calls to the candidate review set. Stop only when each call is allocation-free in the relevant state, has an explicitly derived finite multiplicity/size, or belongs to a specifically identified existing owner. The external-dispatch rider cannot discharge a Scene-created registration.

If capture copies storage proportional to an unbounded foreign stack/exception/context, identify the exact call and unbounded dimension. An old constant, a finite sample or re-labeling the copy as its input owner does not close K. A revised publication/funding boundary then needs review before production implementation.

Use checked arithmetic to compute the maximum **simultaneously live** rows in each independently usable partition. Sum body and publication provisions when they can overlap; do not take a maximum across concurrently possible cleanup and dispatch. Reuse capacity only after a proved end/transfer, not based on test timing.

With `A(n)` defined by the handoff and `S_lane(n)` the separately derived temporary sort/comparison provision, the pre-freeze requirement is:

```text
J = J_shared + J_producer
K = K_body + K_publication
entry requirement = J + K + 2*A(n) + S_lane(n)
P = free purse + active/protected claims + cumulatively transferred capacity
```

These are derivation formulas, not selected values. Catalogue every included sort/cold allocation exactly once. A valid P below the reached entry requirement refuses synchronously before root/TCS/copy/root-read work; it is not retried smaller. After promise escape, exhaustion uses the handoff's protected contextual fault channel.

**Distinguishing example:** dispose an Available result while its successful `TrySetResult` is held in external dispatch. A and body-only cleanup may end, but the whole root and producer/publication provision remain charged. A catalogue which refunds the whole carrier at Dispose, or funds body and publication by the same concurrently spendable K bytes, fails even if ordinary successful loads allocate less.

## 5. Smallest prototype and instrumentation step

**Proposed next authorization is a compile-and-inspect prototype, not unrestricted production implementation.** This documentation-only request creates no prototype. After approval, use the existing Runtime project and a reviewable candidate patch:

1. Make the handoff's final `PreparedScene` private carrier and ordinary staged driver concrete, including constructor order, exact TCS options/overloads, two delegates, three parts, four ledgers, failure slots and exact release tail. Split only ordinary loader orchestration into its three proposed helpers. Keep real validation and real supplier calls; no second driver in a probe, substitute preparer, test-only yield, `Task.Run` production wrapper or generic async framework.
2. Limit dependent changes to the handoff's request backlink/body-return and expired-context clearing, plus ordinary planner root/phase/pending retention routing where necessary. Those are retention seams, not permission to redesign/count planner, Rendering or Persistence allocations. Restore retains its separate route. Do not execute an integrated candidate while its stable root can still be overwritten by a leaf.
3. Compile the candidate through ordinary `dotnet build` for its owning project, Debug before Release. Capture exact source/dirty hashes, compiler/options, resulting assembly and generated/native method versions. Inspect fields, IL/exception regions and native call/return paths. This compile-only step needs no invented J/K constant and performs no unfunded public preparation.
4. Corroborate carrier and TCS component rows with a single-use diagnostic expression/profiler, retaining outputs under ignored `artifacts\log`. Constructor-skipped layout and direct TCS success/fault/cancel/two-original-exception probes are component observations only. Their harness-owned objects do not represent a fake Scene producer or establish Scene cleanup behavior. Do not check in a second runner or tests of BCL constants.
5. Once the source-derived provisional catalogue covers every row, wire those **derived** claims through the existing issuer before running the candidate. A provisional derivation can be falsified by observation; it is not accepted qualification. No placeholder allowance, larger P/World limit, suppressed guard or unfunded live call is permitted merely to collect a trace.

The prototype has two bounded outputs: the final candidate bytes/compiled graph with the row-by-row catalogue, and the producer return-site proof in section 6. Neither needs a completed planner/Rendering/Persistence allocation redesign. Integrated observations additionally require the actual supplier ownership prerequisites; do not use an unqualified child as evidence that the full path is funded.

Use allocation-complete profiler/runtime diagnostics available for the selected target, with constructor/native disassembly as the independent derivation. Where only sampled events are available, resolve the candidate closure's actual allocation helpers and observe their call sites with external debugger/profiler control; do not assume that NativeAOT supports the JIT profiler interface. Record gaps if neither mechanism can cover a required site. A sampled allocation stream, `GC.GetAllocatedBytesForCurrentThread` around an asynchronous call, GC collection, or a zero-allocation warm `TrySetResult` cannot close this gate. Cover all participating producer threads and correlate by the real operation/object and call sites. Attribute external observer/tool allocations separately; do not subtract unrelated process totals to invent an expected value.

For lifetime races, use the existing caller scheduler and external debugger/profiler breakpoints at the candidate's actual publication/registration return sites. Observer synchronization/logging is caller/tool-owned; no production test switch or delayed producer is added. A profiler method-leave notification may precede the actual return: pair it with the caller return address or a complete native post-notification path. Re-JIT or source-instrumented results alone cannot prove the uninstrumented final artifact's tail.

## 6. Prove last access with an explicit producer table

For each row below, identify the actual method/object, grant, source/native return site, synchronization edge, any work after the signal, and the production predicate which makes release legal. A diagnostic observation must verify that predicate, not supply a missing production acknowledgment.

| Producer | Required boundary |
|---|---|
| Synchronous entry/freeze | No escaped promise on failure; option/sort consumers actually unwind before their claims return. The captured Task local is returned without rereading the root after a synchronous driver finish. |
| Start, plan-ready and resource-ready parts | Reserve Pending before publishing registration/inline invocation; Enter consumes that same part. End once after the part's last root/registration access, including registration returning. Clear input locals before enabling the next stage. Account for nested/racing parts, not just the callback which published. |
| Explicit public publisher | Exactly one selected `TrySet*` outside gates. After its actual return, do not reread/recomplete the TCS or access the body; include clearing the owned publication references/exception array and final release in the funded tail. Publication returning and part-count zero are separate conditions. No compiler public-Task completion follows. |
| Scene-created registration/dispatch/context wrapper | Follow the chosen awaiter/ExecutionContext/runtime implementation beyond the Action's return. Prove that any residual code neither allocates nor accesses Scene-owned state after its grant ends, or identify the existing owner and actual later end condition which retains that grant. Calling it “metadata” is not a proof. |
| Planner/resource supplier producer | For each producing entry kind, the workspace owner supplies its exact grant and body-consumption boundary, remaining lowered-frame/publication work, and actual last-access/transfer fact. A completed child Task or the parent's three parts does not provide this fact. Reuse the owner proof for repeated invocations; do not duplicate its variable allocation inventory here. |
| Body cleanup and final credit release | Executor/callback/stack aliases have ended; body and producer conditions both hold. Include field/debt clearing, release-failure paths and lower-release bookkeeping. The root stays installed through fallible release; a previously released process cell is not re-created if domain release fails. |

The **final use-release join** therefore requires all of: fixed outcome/publication returned for an escaped promise, or qualified synchronous unwinding for an unescaped failure; no pending/running ordinary part; ended or validly transferred supplier/wrapper provisions; body Ended after actual cleanup/transfer; and no outstanding preparation claims except through proven returns/transfers. A body Quarantined under this same root does not meet that join: its use and still-required claims remain. Producer completion is independent of an Available/Admitted body's later end. Still-consuming work prevents terminality; stopped-but-unreleased storage may retain quarantine without pretending that producer access continues. No transfer may create an independently escaping producer or a new passive owner.

**Handle an unobservable epilogue explicitly.** If the compiled candidate still performs Scene-owned work after its final observable signal, keep the grant live and identify the first enclosing return/ownership transfer that a real owner can observe. Name that owner, the existing retained grant and the callback/return path that updates the join. Do not create another Task observer or recursive wrapper chain as a supposed join.

If no such production edge exists, the prototype's exact negative result is: method/native site, post-signal object/access or allocation, its current grant, and the absent acknowledgment. Production implementation stays blocked on that specific supplier/runtime boundary. The next permitted change is a separately reviewed minimal actual-return acknowledgment in that existing owner, or a runtime funding/publication hook with a proved finite owner; this addendum does not authorize either implicitly. Adding a numerical margin, force-yield, delay, spin on Task state, normal-path quarantine or new passive owner is not a disposition.

This makes success falsifiable: a trace with no observed late access is insufficient without the source/native proof; a complete proof with an observable later end can be implemented and then checked. No requirement depends on caller GC, disposal of a Task, or another independent use.

## 7. Exact acceptance sequence

The stages below are ordered gates, not new acceptance IDs. The first is the bounded work proposed for authorization; no row has executed in this draft.

| Stage | Required output / pass condition |
|---|---|
| Candidate freeze | Concrete prototype from section 5, complete direct/transitive proof inputs and dirty hashes, exact lane and actual compiled methods. No guessed J/K, fake producer or widened policy. |
| Finite catalogue | Every Scene-owned row has a finite derivation and one grant; every exclusion has an explicit provenance/owner; every cold/deferred/fault/cancel/cleanup overlap and new native/lazy allocation is covered. Observations reconcile with, rather than define, this catalogue. An unmatched row fails the stage. |
| Producer proof | Every section 6 row has a production-valid end/transfer edge, with no possible Scene-owned access after the corresponding release. Test callback/registration overlap and lower-release failure paths. A missing supplier/runtime edge fails even when the ordinary part counter reaches zero. |
| Funded managed behavior | Existing real-package NUnit paths exercise reached exact/one-byte-short claims, synchronous and genuinely suspended completion, success, admitted fault/cancellation and two-role races. Preserve original exception identities/selected cancellation token, immutable binding capture and refused-result reusability. Empty selections or unexercised asynchronous branches fail. |
| Final lane result | Final, uninstrumented Debug/Release bytes and each claimed Runtime Async/AOT/instrumentation variant are covered by the catalogue, proof and applicable actual executions. Record unsupported stimuli/variants precisely. No scope-wide or milestone acceptance follows. |

Required distinguishing observations:

- **Held public publication:** return a genuinely pending public preparation Task, register the legal caller scheduler, hold completion dispatch after terminal visibility, then Dispose the result or admit/execute activation. Observe body/A end or transfer, with the exact remaining preparation use/J/K still charged. A competing operation cannot spend it. Release dispatch and observe the qualified producer/join release once.
- **Registration overlap:** at a qualified external stop point, retain a registering ordinary part through its last access while a reserved completion part proceeds. Cover nested parts, duplicate/uncertain registration disposition and the actual supplier tail. If Start has not returned, the public Task may not yet have escaped; do not manufacture a caller observation of that hidden Task. Public-tail and internal-overlap witnesses are distinct.
- **Bounded engine publication versus external fanout:** zero/one/many Action/await/scheduler registrations exercise the provenance classification without claiming sampled fanout proves a universal bound. All additional Scene-owned allocations must have the catalogue's bounded multiplicity; external work may still hold the Scene tail inside `TrySet*`.
- **Boundary and semantics:** choose the catalogue-derived reached requirement and one byte less, with all earlier stages funded; denial precedes the named allocation. Exercise no-resource and real-resource routes, pre-cancellation versus admitted producer cancellation, original execution fault plus first cleanup sample, and cleanup/publication coexistence. A direct TCS probe establishes only its component row. If no safe real cleanup-failure stimulus exists, name that exact unexecuted behavior gate; do not substitute a throwing allocator/preparer or claim capacity refusal is cleanup failure.
- **Caller context and retained observations:** check ExecutionContext/AsyncLocal flow and restoration, including the synchronous Start path, with caller SynchronizationContext/TaskScheduler variations and real pending work. `UnsafeOnCompleted` is not a cheaper semantics-preserving substitute without that proof. Retained completed Tasks and disposed/consumed wrappers must not retain body inputs or require Task disposal to return credits; repeated wrapper disposal does not retry failed cleanup.
- **Unchanged integration:** preserve the default World/P, nonempty external binding, pixel-17 retention/unload/extraction fixture and separate activation-tail assertion. Attribute a still-live preparation tail separately; awaiting the result does not prove its refund. Domain closure/revocation changes no existing cleanup rights.

Use ordinary owning-project `dotnet build`/NUnit commands and the public-consumer procedures already in `docs\project\Building.md` and `Building.Consumers.md`; Debug precedes Release. Select the affected `SceneAssetStoreTests`/`SceneLoadOptionsTests` cases together and verify fresh nonzero selected/executed TRX counts under `artifacts\tst`. Do not add a qualification runner. Per-run catalogues, disassembly, allocation events, native-return evidence and target identities belong in ignored artifacts, not a maintained narrative log.

**Disposition:** the gate can be removed only for the reviewed final candidate/lane after the finite catalogue and producer proof pass and the required behavior is actually observed. Until then the next bounded action is the section 5 compile/inspect prototype, not production allocation implementation. Workspace-owner acceptance, public-consumer/native qualification and maintained-contract promotion remain separate.

## 8. Frozen direct inputs and preservation state

Addendum-local snapshot began `2026-11-16T03:23:27Z`, HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Raw UTF-8/NUL `git status --porcelain=v1 --untracked-files=all -z` SHA-256: **`0A9C8C5B6926D243A9BC77081852045EA9A79427418987702764B8EF9AA34118`**, 30 ` M` and 12 `??` entries, no staged entries. This new proposal was absent.

This is a new task's local preservation snapshot, **not a refresh of the handoff review baseline**. The handoff's original `890C6C4A7D0D2272CFA6E7DEF36F591F18515B9DBDC16A9ED27BCAC4442688F6` dirty-state baseline and its documented concurrent mismatches remain unchanged. Existing unrelated M05 additions are not reviewed or adopted here.

All hashes below cover raw bytes, without newline/encoding normalization. `--` means absent from that raw dirty output, ` M` retains the leading index-status space, `??` is untracked, and `!!` identifies the ignored local lane files rather than output from the status command. No schema input is changed. The new proposal is excluded only from its own final preservation comparison; its output digest is reported separately.

| State | Direct input | Raw SHA-256 |
|---|---|---|
| `--` | `.github\copilot-instructions.md` | `12439DF4BB5E46021891B234BEC1104ABDCA57C11FDF3F610C8F32C5DAF3001E` |
| `--` | `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `--` | `Directory.Build.targets` | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `--` | `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `--` | `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `--` | `sources\Directory.Build.targets` | `4C0527A12E48D07401C69C06CF942F9145D6A8CCF98C4A7F052B11E463E1CE9A` |
| `--` | `sources\TerraFX.Runtime\TerraFX.Runtime.csproj` | `E019D4DBAD14FF2D5FF8D7365DDED0D09210575D5228A0D01CED70C99B3C2D33` |
| `--` | `tests\Directory.Build.props` | `281AAD4237121FEAB1903C2384F1A5F0AED7E218A704C918FE953E9FC8E75764` |
| `--` | `tests\Directory.Build.targets` | `4C5822FAA56E345DF0640BE0C19D627A21D6A63965C539AB3E0D13FB14D0FDCA` |
| `--` | `tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj` | `ECBFD99E4DD1EC2A32472D3187B6F2911D7297D1F74A52F2AFA600EB6AE5F06E` |
| `--` | `docs\project\Building.md` | `F53AB58B89EEAE7E17B70970075BE9322A936D443BB6D5239C3238E594DEABC6` |
| `--` | `docs\project\Building.Consumers.md` | `8AF46079A9E11E01578CC9B76B695801B585F58148DF36418EE5506CA4006442` |
| `--` | `docs\project\Roadmap.md` | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| `--` | `docs\design\02-dotnet-api-memory-and-ownership.md` | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| ` M` | `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| ` M` | `docs\design\12-entities-components-and-queries.md` | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| ` M` | `docs\design\16-scenes-transforms-and-cameras.md` | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `--` | `docs\design\29-project-setup-build-and-deployment.md` | `3B55A013EEC327ECD648ACBD5B53717DD218AD288E8047AC21277609EFA2F47B` |
| ` M` | `docs\design\30-public-api-review-surface.md` | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `--` | `docs\design\35-implementation-roadmap.md` | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `??` | `docs\proposals\m03b-scene-preparation-workspace-closure.md` | `9D7280A89C5BB75890ADAF65E26F8C80766B8687EE0608623D0F97D519BE32C3` |
| `??` | `docs\proposals\m03b-scene-preparation-handoff-closure.md` | `628B09039114AACAEA65116DD584B2F94B554011306AB600414C5A2A809A7FBA` |
| `??` | `sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs` | `1931CD2B5DFD81A3F0CD8A4E6F3D09C5D9B006B2E29817F5C295C77F78202B68` |
| ` M` | `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `1682BCDF2B478EAE6F94DCA331FB7E05ED5B5357A81B36D06796E247D75886D6` |
| ` M` | `sources\TerraFX.Runtime\Scenes\PreparedScene.cs` | `E4837CEB4F9E8A62A3ABB6A50F6668207AB431D7FDA5896E92BA0FD22A5BFB29` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneLoader.cs` | `2E3F6C347AA01B5914980064CDBF7A2A0414DCD74C31A5223365F4DD4D15FEBC` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneLoadOptions.cs` | `E95382B5F26B7DD398C34BA0B4F20A6A0E76BF9F392B5B3B4BAD88AD1F898A80` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneLoadOptionsSnapshot.cs` | `004B203DC84037B76F854DDE1B47C78D570A8085B6A9B48EE6423E27496223E9` |
| ` M` | `sources\TerraFX.Runtime\Scenes\SceneActivationRequest.cs` | `7EA293C13FB20992E04B3BC4FB7484A2708337ABDE759F86CDA609DAE030C16D` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneActivationExecutor.cs` | `7D12EB728B0563ED673A86A1E1846AAF0D3FC37A07C139CADECF8F34C646EC69` |
| `--` | `sources\TerraFX.Runtime\Scenes\SceneComponentRestoreContext.cs` | `0BE40B9D7A86A1C8112F85217C86A37474E785CB11E0BD332B5E3F5D9B5FCF82` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\PreparedSceneAssetRetentionTests.cs` | `E342237B7645BE805DD924855B2239907773D31753D22B8B2261BF26C20452F4` |
| ` M` | `tests\TerraFX.Runtime.Tests\Scenes\SceneLoadOptionsTests.cs` | `D077505A70E96EA9FB530D1D4B5A030CBF2C6BF599A867DC79256A5A99B10DE8` |
| `!!` | `.dotnet\sdk\11.0.100-rc.3.26522.148\.version` | `0C5E90CAD5281608AFE604B3C0B944F7A180267EAC2CB25FE2C3F1A723488647` |
| `!!` | `.dotnet\sdk\11.0.100-rc.3.26522.148\Roslyn\bincore\Microsoft.CodeAnalysis.CSharp.dll` | `25ED04EABEDF0B2ACF994D4606F4FD40328379106156C028AEB7D4A75154F442` |
| `!!` | `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\.version` | `E7E8C5CF7C39859338D6A80725750B3E4B2BF214C342C660CD25FB0B74C96B4E` |
| `!!` | `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Private.CoreLib.dll` | `DF53885902E896A2B40988237CBE9CBB9F1BB0743200F05FE9257C56A29C5732` |

The final review-input set is limited to these 37 reviewed source/contract/procedure and lane-identity inputs. Other dirty files are not direct inputs; the status digest records their presence, not a review of their bytes or allocation inventories.

Recheck these 37 direct inputs, HEAD and raw status excluding only this new file before handoff. Preserve any concurrent mismatch as a limitation rather than replacing an expected digest. This task changes only this proposal, creates no source/test changes, and runs no build, test, allocation probe, commit or remote action. The final verification result and proposal digest are reported separately.
