# Diagnostics reconciliation owner correction

**Tentative correction to [section 5 of the protected route](m05-diagnostics-protected-route.md#5-selected-funding-route), not permission to implement its incomplete Runtime-only reconciliation assumption.** The committed proposal is unchanged. P1/P2, numeric profile eligibility and the complete 32-/16-MiB ledgers remain open.

## 1. Finding and selected owner

The host schedule list is **not** the reconciliation owner:

- It has no standalone Worlds or standalone sources submitted to its Renderer.
- `HostedWorldSchedule.ApplyWorldReplacementAdoption` replaces its World reference; the schedule is not an immutable directory of old source accounts.
- `ShutdownOwnerServices` clears `_worlds` before closing Diagnostics.
- An externally retained `RenderSnapshot` can outlive its World and later submit elsewhere. A copied `DiagnosticSnapshot` cannot emit and holds no account.

Also, `WorldDiagnosticsCapability.Close` closes the **capability** and clears its account reference; it does not seal or dispose `WorldDiagnosticAttributionAccount`. That account has no closed state. Accepted snapshot/submission uses must remain possible after World closure. `WorldDomainOperationCredit` supplies accounting, not an independently rooted account-to-funding association.

Select one **Entities-owned, Runtime-private reconciliation owner per Core service**, provisionally named `WorldDiagnosticsServiceOwner`. This is a new implementation control, not an existing type or public API. Its ownership must not depend on `_worlds`:

| Lifetime | Strong root / transfer |
|---|---|
| Host service | An `EngineHost` field, created with the service and retained through successful service Close and reconciliation. The same owner is lent to `PlatformInputRoutingOwner`, hosted construction plans/Worlds and both old/replacement Renderers. |
| Standalone service | A local factory root, then `WorldConstructionPlan`, then a World field. Failed admission/construction retains the local/plan owner through its complete close/unwind. No Hosting reference is needed. |
| Each source envelope `E_i` | A separate Entities-private source funding control holds exactly one `WorldDomainOperationCredit`, its exact Core account and funded use state. The plan/World holds its initial use; each extracted snapshot and accepted submission owns an independent use. |
| Core-retained source use in service `s` | `service owner -> fixed attachment cell -> source funding control -> operation credit -> source domain`. This survives World close, schedule replacement and `_worlds.Clear()`. |

Rendering receives the source funding use alongside the non-emitting account, never the World capability. The reconciliation handle adds no World access or service-close authority. Core stores no Runtime control/interface/callback. Public Diagnostics properties still return the same read-only public service surface.

## 2. The missing observation and a finite attachment bound

**The current Core surface is insufficient for the originally suggested sweep.** `account.ReservedBytes` is global across services. It cannot say whether service A still holds that account. Keeping A's attachment whenever another service B has a debit leaves stale attachments after B's final refund, with no future A mutation required to remove them. Neither the host list nor public snapshots repair that ambiguity.

The smallest proposed Core addition is an **internal, allocation-free, read-only local-use query**: for an exact account reference, inspect the service's ring retentions and independent latest-fault retention under its existing gate. Return whether this service retains that account. No GUID lookup, payload copy, caller callback, emission or mutation is involved. Unlike public polling, the internal probe returns empty for a completed Close; Runtime's closing/failure state prevents interpreting a partially released service as empty. Successful Close reports no local retained use only after its whole release phase returns. Runtime separately tracks pending preclaims, whose identities Core's aggregate `_preparingPayloadBytes` does not expose.

This internal observation seam is **absent today**. Recommend authorizing it as part of the private implementation batch. If “no APIs” also forbids adding an internal non-emitting Core observation, that scope decision must be resolved first; a Runtime-only implementation using current scalar balances has not established the claimed bound/return behavior. No public hook or external mutation authorization is needed.

For the inspected production call graph, derive the fixed array capacity:

```text
N_s = this service's admitted ring capacity, at most 8192
retained distinct source accounts <= N_s + 1       // ring plus latest fault
P_host = 1; P_standalone = 0                       // outstanding external preclaim
C_s = N_s + 1 + P_s + 1                           // one entering mutation
```

Thus a full host service needs **8195 attachment cells**; a standalone service needs `N_s + 2`. These are derived cell counts, **not approved byte allowances**. `P_host=1` follows from the one active diagnostic submission per Renderer, synchronous per-row preclaim/write/finally-dispose, one host service per host, and recovery draining the old Renderer before creating its replacement. A stopped externally retained Renderer cannot accept another submission. No production preclaim currently targets a standalone service. This does not price future concurrent Critical producers: enabling one requires revisiting `P_s` and its funding, not asserting an empty Critical catalog passed.

Use fixed value cells, exact account-reference matching, one embedded mutation gate and scalar state; no per-write dictionary growth or retained drain list. Cells may be recycled as soon as **local** retention and Runtime pending preclaims are zero, even when that source is still funded in another service. A full table before a new registration necessarily contains a locally idle cell: stable retained/pending membership is at most `N_s + 1 + P_s`, one less than `C_s`. Do not retain idle cells merely because their World remains live.

For a qualified allocation function `Q`, the service's existing ceiling must include:

```text
R_s = Q(Runtime owner) + Q(array[C_s], actual cell stride)
      + any separately allocated synchronization/control backing
R_s + all Core service backing/payload/protected/construction claims <= L_s
```

Preclaim `R_s` before owner/array allocation. Give Core only `L_s - R_s` of spendable physical capacity, or enforce that exact equivalent in its constructor accounting. For a standalone source this is also part of its existing standalone baseline inside `E_i`; shared host backing is not charged to every World. A cell's source attribution/control use is prepaid from `E_i` without counting the physical array twice. Derive new source-control/operation-credit/holder extents and constructor overlap separately. If required backing/minima do not fit, refuse initial admission before allocation/authority; do not raise a limit or borrow 4096/256-byte “slack.”

No measured constructor-free object size establishes `Q`, `R_s`, synchronization cost or the complete cohort. There is no selected numeric eligibility here.

## 3. Mutation, reconciliation and actual return

All current Runtime service mutations use that service's Runtime owner. Serialize **registration plus the synchronous Core mutation** with its private gate; only one prospective cell exists at a time. Do not hold it across formatting, awaits, application callbacks, joins or domain returns. Core polling takes only the Core gate and cannot refund an account.

On every success, refusal **and exception**:

1. Register an attributed source before Core can retain/reserve it. On successful Renderer preclaim, mark its cell pending before leaving the Runtime gate. Keep it pending through formatting, commit and reservation Dispose, even if the remaining temporary byte count becomes zero.
2. In `finally`, reconcile **every occupied cell**, not just the incoming account. Core room-making can evict several unrelated accounts, including before ultimately refusing the new record.
3. Under Runtime-then-Core gate order, test local retention and pending count. Detach an idle cell by moving its existing funded attachment-use token into one call-local return token and clearing the cell. Outside both gates, release that token before detaching another. No allocated return batch is necessary. A registration needing a free cell can reclaim one this way; it must not unlock between installing its new prospective cell and completing its Core mutation.
4. An implementation may split the bounded sweep into one short gated visit per fixed array index, returning each detached use outside the gates. A concurrent mutation that evicts a cell already visited owns its own subsequent full sweep. Existing idle cells do not consume additional prospective capacity. No queued reconciliation task, next host pump, poll or GC is needed.

Registration and source-use acquisition must not take Runtime service gates while holding World/domain/registry/source-close locks; domain release occurs after the gates above. The simple exact-reference local query has bounded scan cost, not a claimed performance win. Qualify its workload before claiming a fast shipping path.

The source funding control owns the operation credit **undisposed** until World production has ended and its snapshot/submission/attachment uses have all returned. Use single-return value tokens embedded in already funded holders; do not silently allocate `AcquireRetainedUse` leases. If those existing allocated leases are chosen instead, their complete allocation and overlap must be preclaimed.

The final use also verifies zero global account payload/preparation/protected balance, no future producer use can be acquired, and any source-owned service storage has ended or has an authorized funded transfer. It clears account/domain/credit links and disposes the one credit exactly once. Source-control baseline/teardown metadata is distinct from the balance being tested, preventing self-pinning. A positive unexplained balance is an accounting failure, not successful retirement.

**Cross-service witness:** close standalone World W while its snapshot/submission and host H's record remain. W's service closes, but the snapshot/submission uses and H's attachment root `E_W`. Dispose the snapshot and finish submission; H's record alone still roots it. An unattributed H write evicts W's last row. H's mandatory full sweep observes no local retention, drops its attachment and returns `E_W` immediately if that was the final use. It requires neither W in `_worlds` nor another call on W's closed service. Conversely, an idle W-service attachment must already have detached even while H still had a positive global balance.

## 4. Exact production boundaries

Line locators below refer to the frozen source hashes. Wrap calls explicitly; no public property setter, outside mutator, Core event or callback is assumed.

| Boundary | Required reconciliation / lifetime action |
|---|---|
| World attributed `Write` | `World.cs:2196`; `World.Publications.cs:178`; `World.Controls.cs:411`; `World.OwnerControlApply.cs:77`; `World.OwnerControlAdmission.cs:63`; `World.OwnerControlRequests.cs:120`. Enter the associated Runtime owner around the complete capability call; sweep all cells in `finally`. |
| World capacity warning/drop | `World.Publications.cs:196,200`. Same owner, including OOM fallback. `RecordDropped` currently refunds no account; routing it here also prevents an unreviewed future counter publisher bypass. |
| Host unattributed `Write` | `EngineHost.cs:751,862,889,912,923`: scheduler debt, startup, gamepad, mode and captured-input forwarding. **Every call can evict attributed records**, despite null source account. |
| Input-owner unattributed `Write` | `PlatformInputRoutingOwner.cs:801,819,837,838,1051,1247,1293,1315,1369,1412,1478,1508`. Borrow the host's same Runtime owner; sweep after each complete mutator or one explicitly gated batch. |
| Renderer publication | `Renderer.SkinnedNormalDiagnosticPipeline.cs:109` preclaim, `:188` write and the `finally` reservation Dispose at `:130`. Reconcile after failed/successful preclaim, after commit/refusal, and after Dispose; pending identity survives outside the gate until that final disposal. Core-internal transfer/refund calls are covered by enclosing the whole method call. |
| World/service closure | `World.cs:772` failed construction capability Close, `:776` standalone service Close before capability creation, `:1458` World Dispose; `World.PersistenceAdmission.cs:763` preparation failure. Wrap the **entire** Core/capability Close, including payload release after Core nulls its fields; then sweep all cells before dropping the World/construction use. Hosted close clears the capability only, not the shared service. |
| Untransferred restore plan | `WorldConstructionPlan.ReleaseUntransferredRestoreResources` currently has no source-escrow cleanup and does not close its stored capability. The new batch must close/abandon that borrowed capability without closing the shared service, reconcile, and end the plan's funding use on both enrolled and unenrolled unwind paths; do not depend on plan collection. Transfer to World is single-use. |
| Extraction / snapshot | `World.CpuExtraction` -> `WorldFrameReadLease` -> `Renderer.Extract` snapshot construction at `:1004,:1160`. Acquire the independent funding use before publication; constructor failure returns it. `RenderSnapshot.RetainDiagnosticAttributionAccount`/Dispose currently copy/clear only a reference: acquire the submission use atomically with the account under `_assetPinGate`, and release snapshot use on Dispose. |
| Submission / drain | `Renderer.Submit.cs:107-115` admission/retain through its full `finally`; keep the funding use through readback/publication and actual cleanup, independent of snapshot Dispose. Its scope must also cover exceptions between retain and the current large submission `try`. Drain must not complete before that use returns. Recovery and replacement borrow the same service owner, not a new account directory. |
| Read-only paths | Public `World.Diagnostics`, `EngineHost.Diagnostics`, `GetSnapshot`, `TryGetLatestFault`, and internal `GetTimestamp`: no emitting/reconciliation hook. Copied Diagnostic observations do not retain the escrow. |

Core `Write` itself does not directly release an arbitrary Runtime receipt; Runtime's existing closed call graph supplies the enclosing owner. A new producer or account-refunding Core entry point invalidates this callsite closure until it is routed and its pending membership priced.

## 5. Host shutdown and smallest next batch

Keep the reconciliation-owner field independently rooted while:

1. Closing/draining Renderer admission, including all pending preclaim disposal and source-use returns.
2. Disposing hosted Worlds and closing their capabilities; each operation reconciles. `_worlds` may then be cleared, as today.
3. Closing the admission domain, if retaining today's order: existing operation credits remain outstanding and their actual later release may finish its retirement.
4. Calling Core Diagnostics Close through the Runtime owner, waiting for its synchronous payload-release phase, sweeping all attachments, then clearing/releasing the owner backing. An external snapshot may still hold its **source** envelope; service Close does not revoke that use.

Do not tear down the Runtime owner when clearing schedules or replacing a Renderer. A successful close/drain takes the deterministic return path above, not permanent quarantine. A real failed drain/cleanup retains its actual claims under the responsible failure owner and is reported as an unresolved failure; merely seeing Core `_records == null` during/after a failed Close is not release proof.

**Implement the owner bridge/local-use observation and all callsite wiring together, before enabling source escrow.** Derive/pay the complete control/backing cohort, then add plan/World/snapshot/submission source-use transfers and `E_i` admission in the same dependency-ordered feature batch. Do not ship a partially wired escrow that relies on later mutations to repair missed returns.

Focused Runtime regressions must cover unattributed cross-account eviction, preclaim refusal after eviction, coalescing and latest-fault overlap, multi-service same-account detachment, standalone close with an external snapshot, snapshot retain/Dispose races, restore-plan unwind/replacement, and shutdown clearing schedules before service Close. Assert domain retirement on the actual last mutation/Dispose without another poll, GC or admission; failed cleanup remains separately charged. Test capacity with `N_s + 1 + P_s` distinct held accounts plus the prospective cell, including recycling and concurrent pending return. Internal fixtures prove mechanics, not nonexistent Critical producers or native consumers.

The additional scope disposition is the private Core local-use seam and its Runtime control cohort, **not** a public API, quota, reverse dependency or external hook. Until approved/derived, section 5 is not implementation-ready on its original assumptions. Mandatory Critical inputs, clock payer, immutable-result ownership and profile/native/AOT qualification retain their original open gates.

## 6. Refrozen evidence

Initial snapshot HEAD **`71c796cc392c728eef9ec44bdf5c3fccce9f43a3`**, freeze `2026-11-19T21:05:45Z`; final recheck **`92afde31e36ee22d81c37f9c54dfbaa75e24e4fb`**. The lifetime test was dirty at the initial freeze and was then committed at `92afde31` with exactly the same `0605bf47…` bytes below. All listed inputs were clean and hash-matching at final recheck. Unrelated dirty Scene tests and the cosmetic-particle proposal are excluded and preserved. No schema/fixture changes, build/test runs, allocation probes or image inspection were performed for this correction.

`4785e1a3b38fd85fedf37f516e264253c81085cb` added constructor-free allocation measurements, not a complete owner-graph bound. Its lifetime-test **Git-blob bytes** have SHA-256 `82ba06d3855b045af5d473b23ba5ba72ef54160480ca276ddb283c9ac474ae40`; the `71c796cc` blob has SHA-256 `0a8810b8bc97d143423cdfab5ad56047083e7f74a48ef5b911020ca06f594dff`. Neither is the current input below, which also measures permit/Lock/operation-credit/use-lease allocations; both its worktree and `92afde31` blob SHA-256 are `0605bf47f60a2e22bdc951a2bcce938bc2594804603f4a5df7127d58af1a6329`. `GetUninitializedObject` omits constructor-created graphs. These observations cannot select a funding allowance for new controls or prove whole-cohort/lifetime admission.

Recheck the correction's exact bytes and these SHA-256 inputs before disposition; a moving test input requires refreezing, not silently substituting the older measurement commit.

| Input | SHA-256 |
|---|---|
| `docs\proposals\m05-diagnostics-protected-route.md` | `dc28f94efce9dc4d4024b3e452296867e6095e7d9f20264dfc9698218d94d321` |
| `docs\design\04-capacity-and-workload-profiles.md` | `60047dd1eacf26196b0bf23f16798e2aa265fd26cd36b9a0b4df1b79135730d7` |
| `docs\design\28-diagnostics-performance-and-validation.md` | `0e3c895bee5f76118ce87d208170cb6e02e9a19de3f286b7e243b28165370d14` |
| `docs\design\29-project-setup-build-and-deployment.md` | `49598161138febbf889f2a5176e3504af8d8bb8481d2157fbcfbbd94d2b46604` |
| `docs\design\30-public-api-review-surface.md` | `d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed` |
| `docs\design\35-implementation-roadmap.md` | `24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | `92002ce11c2a5817f0525a68b6f4074a35eeef3b53d8efd62ce8748a5f9108d1` |
| `sources\TerraFX.Core\WorldDiagnosticAttributionAccount.cs` | `75b41f64fcb6af40bfdb7d0c1203596dedd3f8826ea8aa006af9dabe81e25e5b` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | `7feb090087103d651e7674ed75d90d103bb7aa2ea039e273e7b41b67d2a2806d` |
| `sources\TerraFX.Core\DiagnosticAttributionReservation.cs` | `9bcc73583910112962119bb82d283e7b3a722338daf93705bac2dcf64d1b9ddf` |
| `sources\TerraFX.Runtime\Entities\World.cs` | `99df71a9956b22e072fdf5314056a6854880bbde83654d09e5eab4a197b92fe8` |
| `sources\TerraFX.Runtime\Entities\World.Publications.cs` | `ec1e3c92ae6e0e3f30a8dde7aa90ce4cc11141741286eccd753185c648a6d6fe` |
| `sources\TerraFX.Runtime\Entities\World.Controls.cs` | `4dd4b837c256d0ff5f120ccd4f71ec1ba6a732c41a64484e1375dc1f931402ff` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlApply.cs` | `50a932f1dc1657b8bac458580b28f9e8856fb39a80027da7bf38a15449157df9` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlAdmission.cs` | `fd7f0b0f15181ce6d94ee9cb1903b94aed8c7ffd3c96a60a16fe459d1d892003` |
| `sources\TerraFX.Runtime\Entities\World.OwnerControlRequests.cs` | `df45b48e8a7e570616b7bf511e0e4d72cc509a54aa79a823b79310b9d08fd9bb` |
| `sources\TerraFX.Runtime\Entities\World.PersistenceAdmission.cs` | `1dbac6f5d3da19a6e616bdd03e8f4a9f885cf74cc0fd40d4f3e34adac5da62b4` |
| `sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs` | `b2ee768a7cfd14753882452ce019ffff3df274969b3368572e3ef26ddffbfb06` |
| `sources\TerraFX.Runtime\Entities\WorldDomainOperationCredit.cs` | `1f005480fe39ace70b469848f943a5a72433c00a1f9e099075dccb604b2a0002` |
| `sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs` | `0afbc247e5ce649edbd6f26a01f67bd291d7f23cd1529dd5cd735e89dffd70f7` |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | `5a304aaf8b2353e28f6f743d5587e82f12e979dcf0dfebf55103e075ccaf54f2` |
| `sources\TerraFX.Runtime\Entities\WorldFrameReadLease.cs` | `d2516ee9b4845210bdf97ab6c52200213a18b5c70c8ac14d6350ec863f3d1af6` |
| `sources\TerraFX.Runtime\Entities\World.CpuExtraction.cs` | `eb717590a5cc31d05fa576e985f2d2e7fc20c029f561df4fcac1167b5cb96840` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.cs` | `2fd506f286e9d69e1f67b144cd113c4d023edb8ad9f8bfdfd079d4db417c6fa2` |
| `sources\TerraFX.Runtime\Hosting\EngineHost.GraphicsRecovery.cs` | `3426ced69741f3292673279010925a7b6f388fc3ab64bdebd69503868fd6cd93` |
| `sources\TerraFX.Runtime\Hosting\PlatformInputRoutingOwner.cs` | `524bc0faddc5fe75bf7536b5dd78f6adb43eba471ee05d603fabca53976e02b3` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | `8b75010ceef75e39ce2a5c35ced6da33ebfcac4385513772282581c68bb6fe16` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | `b74534ab4e8c9b35c46e2c70b640979e6c6b73220747c69f15dcda64264f2b2b` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Submit.cs` | `3f4b232e7c777fcf458c7e17bfc8bec028716e912f68e40eea0993371441c9c3` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SubmissionAdmission.cs` | `8b456af38eed738e7a02bd233ffeb83438cbd114b59ca3967ba6103a1e082d14` |
| `sources\TerraFX.Runtime\Rendering\Renderer.SkinnedNormalDiagnosticPipeline.cs` | `1eeb8c96a3f89f74f232a881ceea2ac3113f70db0a37b35c60beffbedc6159d2` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `0605bf47f60a2e22bdc951a2bcce938bc2594804603f4a5df7127d58af1a6329` |
