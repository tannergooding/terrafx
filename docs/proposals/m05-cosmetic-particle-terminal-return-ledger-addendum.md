# M05 bounded terminal-return ledger

## Result and scope

**Tentative source/IL ledger, not allocation/return qualification.** The current chain provides exact identity/debit facts and ordinary call returns, but no identified funded handback covering the last independent-use/domain publication suffix. The 10,592 World-associated sizing inputs remain unfunded and exclude protected work.

Do not treat this as one linear “registry publishes” operation:

- The **process `WorldGuidRegistry`** retires binding/owner/independent-U cells. Its inspected retirement path publishes no Task.
- The **domain registrar** releases history/operation interests and backing charges; `WorldAdmissionDomain` owns the retirement `TaskCompletionSource`.
- CoreLib publishes that Task's outcome and handles continuations before its completion caller returns.

The primary slice is a valid independent use with no active workspace, held across ordinary World close, then released as the last use of a closed private default domain. The same final frontier is reachable without an independent use when normal World disposal ends the last World interest. These are source-derived paths, not operations executed here; no particle request or Scene producer is fabricated.

“Claim live” below means an actual named count/byte provision remains. It does not silently assign that provision to a different object or invoker. Process/domain backing high-water charges can survive a cell's release; those unrelated metadata charges are not automatically a publisher grant.

## 1. Claims at entry

| Claim | Actual issuer / scope |
|---|---|
| Current process binding/owner roles and independent U | `WorldGuidRegistryAccounting1`, exact captured grant/cell generations and permit. Separate from World/domain bytes. |
| Independent-use data and interests | Exact control's remaining `Anchor.OperationBytes` (initially `256 + W`, before actual transfers), history interest, operation interest and domain operation credit. Not another World owner. |
| Domain registrar backing/control | Actual charged registry/control/page/membership storage. A named 4096-byte control reservation is not an unlimited release envelope. |
| Original World data interest | May already be released by `World.Dispose`; independent-use data is separately retained. A surviving reservation object is not proof its World interest is live. |
| New cosmetic standing provision | **None installed/admitted.** Candidate fields, copied bindings and handback values create no grant. |

`WorldLifetimeControl.BeginRetirement` revokes new binding use. With outstanding independent uses, `CompleteRetirement` leaves deferred retirement pending. Default-domain close marks the domain disposed but cannot complete it while these interests remain. Thus the independent-use and domain claims genuinely survive the earlier World-close return; this is not inferred from Task state.

## 2. Last-independent-use ledger

All member names here refer to `sources\TerraFX.Runtime\Entities`.

| Actual edge | Claims surviving / exact effect | Existing return or handback; coverage disposition |
|---|---|---|
| `WorldGuidUse.Dispose -> WorldGuidUseControl.Release` | Captured generation checked; state becomes Releasing; active workspace refuses before release. Process U, operation bytes/interests and domain backing still live. | Ordinary call entry, no terminal handback. **Held**, subject to their real allocation rows. |
| `Release -> WorldGuidRegistry.TryReleaseIndependentUse` | Decrements independent-use count. For the last revoked/pending binding, keeps this U cell as `UseRoleIndependentRetiring` while invoking deferred lifetime completion. | No refund of that U yet; no new grant/receiver. |
| `TryReleaseIndependentUse -> lifetime.CompleteDeferredRetirement -> registry.TryRetire` | Retirement permit verifies/consumes exact authority. `TryReleaseFresh` retires binding/owner-use roles; the retiring independent U still anchors the key until its later release. Domain operation bytes/interests remain live. | `bool` is an identity-retirement fact, **not** an invocation-funding transfer. No process-registry Task completion occurs. |
| Deferred lifetime helper returns | Sets lifetime Retired and clears its reservation/lineage references. Independent U and domain-use claim still live at that point. | Ordinary helper return is real, but no separate protected caller-loan is issued. Do not equate reference clearing with end of all callers. |
| Process registry resumes and frees retiring U | Calls `ReleaseUseCell`, decrements key role count and possibly releases key. Domain operation bytes/interests remain; process backing pages may remain under their own registry. | `true` confirms this release. It transfers neither page bytes nor an implicit parent grant to the remaining use/domain caller. |
| `Release -> domain.TryReleaseIndependentUse` validates captured interests | Existing operation/history grants and domain metadata still present. | No Task boundary or new handback. |
| Domain debits operation and releases interests | Removes exact independent-use bytes, decrements World/domain use charge and counts, releases both interests and unlinks the actual control. Retired lineage membership may also end. Domain backing is still charged until its own release. | **Use grant ended.** No original-grant receiver for the remaining control/invoker interval is identified. |
| Last closed-domain branch calls `ReleaseRegistry` | Clears registrar arrays/counts and sets `_registryCharge = 0`. Process registry backing elsewhere is not reassigned. | **Domain registrar claim ended.** No prepaid publisher/return loan is issued. |
| `control.ClearDomainInterests`, gate exits, then `_retirement.TrySetResult()` | Clears control fields after the use debit; Task completion is reached after registry charge retirement. | **Uncovered terminal interval:** no surviving named World/use/domain provision is identified for these still-reached accesses/publication. No amount is invented. |
| CoreLib completion returns, domain returns `true` | Ordinary completion/domain calls have returned. CoreLib observer/dispatch tails require their own owner/end where reached. | `true` is a release/outcome fact, not a byte claim handed to `WorldGuidUseControl`. |
| `WorldGuidUseControl.Release` resumes | Clears `RetainedOwner`/`Next`, writes Released, then returns to the carrier's `Dispose` and its caller. | **Uncovered lower-control/caller suffix:** real post-domain field writes remain after its prepaid use ended. `IsReleased` is authoritative state, not evidence that the invoker has already returned. |

On the nondeferred **active-World** route, process U is released directly and a World interest/domain backing can remain. That is a different set of live claims, not an automatic grant for the ended use's final fields or callers. An explicit supported allocation/return attribution remains necessary.

On the **no-independent-use** World-close route, `World.Dispose -> WorldDomainReservation.Dispose -> domain.ReleaseWorld` ends the World interest. Closing the default domain then reaches `ReleaseRegistry -> TrySetResult`; or, if the domain was already closed, `ReleaseWorld` reaches that branch itself. Delaying one call only moves the final publication edge. The process registry and domain registrar must not be conflated.

In the current `ReleaseWorld` and domain `Dispose` implementations, the outer `using var growthScope` also remains in scope across publication and must exit afterward. Unlike the nested scopes in `TryReleaseIndependentUse`, that return/gate suffix is not before `TrySetResult`. It needs real funding/end coverage too; a lock-scope exit is not another grant.

### Failure and acquisition-unwind rows

| Actual branch | Retained fact / existing channel | Remaining gate |
|---|---|---|
| Active workspace or failure before successful process release | Actual outstanding claims survive; control becomes Quarantined and the same exception escapes. | Retain actual workspace/control/root; a normal repeated Quarantined `Release` is a no-op, not successful debt discharge. |
| Process release succeeds, domain release fails | Process U may have ended while actual remaining domain claims/control linkage survive. | Preserve precise partial facts; no all-or-none refund, recreated U or repeated confirmed destruction. |
| Lower debit completes but later cleanup/publication throws | Do not infer restored claims from Quarantined state or the exception. | Retained graph/failure needs a real surviving owner; the terminal provision remains **uncovered** absent a proved transfer. This is a conditional source failure disposition, not an injected failure observation. |
| `AcquireUse` fails and `RollbackWorldGuidUseRaw` / `RollbackUnissuedWorldGuidUse` completes | Exact raw/linked claim can end before enclosing rethrow/caller suffix; last closed-domain rollback can publish retirement too. | The candidate issuer journal records debt facts; its prospective credit/standing parent is not admitted in live construction. No no-debt flag proves caller return. Existing thrown/cancellation channels remain unchanged. |

## 3. Actual CoreLib completion suffix

The local CoreLib has product version `11.0.0-rc.3.26522.148+5254d8bcf59917edc1e1abc33bdf2442b77b1c70`, SHA-256 `DF53885902E896A2B40988237CBE9CBB9F1BB0743200F05FE9257C56A29C5732`, MVID `4d3da96b-886f-4bbc-ab65-95c08e83c881`. Pinned reference-source ordering was checked against read-only IL inspection of ten actual completion members; no CoreLib target method was invoked.

| Boundary in that module | Actual relevant ordering / handback |
|---|---|
| `TaskCompletionSource.TrySetResult`, token `0x0600434B` | Calls its Task's `TrySetResult` at IL 6; false path can call `SpinUntilCompleted` at IL 20. The returned Boolean is outcome transition success. A false result does not join an earlier setter's invoker. |
| `Task.TrySetResult`, `0x0600425D` | `AtomicStateUpdate` publishes terminal state before contingent-property/parent completion and `FinishContinuations` at IL 41. **A consumer can observe completion before this suffix ends.** |
| `Task.FinishContinuations`, `0x06004261` | Exchanges the continuation field with the completion sentinel at IL 11; can then call `RunContinuations` at IL 22. Even no-observer completion includes the field exchange after terminal state publication. |
| `RunContinuations` | `RunContinuationsAsynchronously` disables the ordinary inlining choice; it does not skip completion processing, tracing, scheduling or all internal completion actions. Source also contains mode-specific Runtime Async handling; it is not qualified by this ledger. |
| `RunOrQueueCompletionAction`, `0x06004263` | An action declaring it cannot run arbitrary code can still be invoked directly; the queued branch constructs `CompletionActionInvoker` and queues it. Thus the option is not a universal “no inline invoker work” certificate. |
| Action/Task continuation scheduling | Raw Action scheduling can allocate `AwaitTaskContinuation` and queue it; standard Task continuation scheduling and captured-context invokers have their own reached work. Queued work-item return can precede ThreadPool context reset/AsyncLocal notifications. No publisher grant/end is handed back by those outcome interfaces. |

Pinned source: [TCS lines 214-223][tcs], [Task lines 3496-3513][task-result], [continuation processing lines 3602-3806][task-finish], [Action scheduling lines 741-771 and 831-843][action], and [ThreadPool reset lines 994-1018][pool].

The exact-source `WorldAdmissionDomain.Retirement` symbol references currently identify tests, not production engine subscriptions. The bounded private-default-domain profile adds no observer in the inspected production call chain. This is not a general bound on caller observers of the **public** domain Task, nor permission to assign all context/dispatch cost away. Most importantly, proving the no-observer branch would still leave the terminal-state/field-exchange ordering and the later TerraFX control writes above.

CoreLib/source/IL ordering is not native-code last-access or finite allocation qualification. Actual loaded runtime, generated native tiers, tracing/debugger/exception paths and any observed callbacks remain explicit evidence obligations; no future testhost/runtime mode is inferred from this file hash.

## 4. Relationship to the owner-aware async-handback proposal

Apply the existing [owner-aware async-handback assessment](m03b-scene-preparation-owner-aware-async-handback.md), especially sections 4, 6 and 7: an outcome observer cannot recover an unexposed invoker-end fact; original funding must survive until real end/transfer, and a proved harmless suffix may suffice without a CLR physical-return hook.

Do **not** duplicate its builder/invoker design here. This terminal release path is ordinary synchronous TerraFX code invoking a completion publisher, not a selected replacement for all Scene Task/ValueTask suppliers. It does not require inventing another public Task-like API or assuming that a speculative custom builder is available. Scene preparation remains unavailable under its own gates; that is not a blanket prerequisite for unrelated M05 work.

`TryReturnCosmeticOwner` is a useful candidate ordinary-caller shape: prepare returns before accept. Its current accept only marks logical ownership Returned; it neither issues protected funding nor closes the later World/domain suffix. No Task/ValueTask completion, observer Task, local `finally` or callback decrement repairs these rows.

## 5. Smallest capability/evidence required

The first missing capability is an **actual funded enclosing release invocation**:

- It retains the original control/standing provision while process/domain debit facts change, and through the final control fields, caller and reached publication/context suffix.
- It gives the real enclosing caller an exact original-grant end-or-transfer handback, with a proved no-grant-access/no-owned-allocation suffix. A scalar `true`, Released/Retired enum, Task result or copied grant is not that handback.
- It identifies a still-live original or lower-control claim for domain retirement publication before the last registrar/control provision ends. Array-page retirement, public retirement readiness and final publisher return remain separate facts.
- Partial release/cleanup keeps exact debts and primary exception/token identity; no additional U, unbounded global tail, new acceptance ID, quota increase or implicit borrowed grant is introduced.

Prefer proving an existing real parent/control claim covers the interval. No such coverage is established in the inspected inputs. If a private two-phase release/loan is needed, close its exact issuer, recipient, byte/count effects and failure/end semantics **before** its implementation can enable construction; do not casually reorder refunds around Task completion.

**Exact next owner/evidence action:** bind one real protected caller/claim to the two uncovered intervals—use-control post-domain release and domain terminal publication/final invoker—and derive its complete cold/failure/runtime/native peak on the frozen lane. If neither an existing claim nor a reviewed original-grant handoff can span them, record that precise unsupported lower-owner capability and keep admission unavailable. More header measurements or a completion-only probe cannot discharge it.

The 10,592 sizing inputs, separate registration increment, original World/domain limits and all selected construction/operation/Scene/pool gates remain unchanged and unfunded. This ledger supplies no new protected byte amount or live implementation readiness.

## Inspected boundary

`Entities` paths are under `sources\TerraFX.Runtime`. Hashes identify reviewed bytes, not native return/allocation qualification.

| Input | SHA-256 |
|---|---|
| `Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `Entities\WorldGuidUse.cs` | `A6120452989F29EC2EF9C2B90E7479CC6635B9B17848C6C683063B3F99D36D22` |
| `Entities\WorldGuidUseControl.cs` | `F3345FCEB06BEEA0DA4B89E2CF3106B1F60717A71C810AD3C34477762C6964F9` |
| `Entities\WorldGuidRegistry.cs` | `9A7339A8ECFD56C60F7C2A5C9251BCBBA2DA73A7A26520AFCDBE533B24BD39E9` |
| `Entities\WorldGuidRetirementPermit.cs` | `1849EAE697239482076F7EC3A1EA02DD6B3348094DAD6CB63668BF57FDB20529` |
| `Entities\WorldAdmissionDomain.cs` | `2CE1C0B90157ADE6F82605409AB599F7415B7D8AA0A63A4BF4327263610248D9` |
| `Entities\WorldDomainReservation.cs` | `EA012B9B40ACEDAA820885EAFE27DCD11DF55F842B68F2849785DAB756E8E3BC` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldDefaultDomainOwner.cs` | `BCF8F830FBB29E652D480608A3BDFC197D89D7E58299622941E87685B4C2AAD4` |
| `docs\proposals\m03b-scene-preparation-owner-aware-async-handback.md` | `0A9AE81EB42D886168D9E05970C7395B33C9EF70FCCADA9ED311CF2E1DC7AD89` |
| `docs\proposals\m05-cosmetic-particle-protected-close-coverage-addendum.md` | `098DA320E61AA3171B5B09D1C11DAA0CD03B67AFB5B1CA5DA5C75801987744FB` |
| `docs\design\04-capacity-and-workload-profiles.md` | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `artifacts\qcl\t\corelib-completion-il.json` | `3D16317E7B76ACF940EEB86E3B6827F4E8A632AD16B628A8731B853035839EC4` |
| `artifacts\qcl\t\pinned-reference-inputs.json` | `6F997FEFECF9702FD5C182415ED37D0D09C6C1C930C6385F0404824712E2E306` |

Only this proposal and ignored public-reference/read-only IL evidence are added. No source/tests or maintained chapters/roadmap are edited; no build/probe or World/Scene/pool/Task-publisher target operation runs.

[tcs]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskCompletionSource.cs#L214-L223
[task-result]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L3496-L3513
[task-finish]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L3602-L3806
[action]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskContinuation.cs#L741-L843
[pool]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/ThreadPoolWorkQueue.cs#L994-L1018
