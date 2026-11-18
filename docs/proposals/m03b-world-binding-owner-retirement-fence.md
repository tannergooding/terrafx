# M03-B World binding-owner retirement fence

**Status: tentative rectification and implemented lower-owner candidate, not complete receiver/Scene producer qualification. Scene preparation remains unavailable.**

## Disposition

Refine the [receiver-control funding proposal](m03b-scene-receiver-control-funding.md): retaining the original lifetime is necessary, but it does not by itself fence the process registry's raw release endpoint. Add one owner-bound flag to the already charged process binding cell and require the original retirement protocol to authorize its release. This adds no carrier, worker, interest, public API, acceptance identifier, or quota.

The working candidate also implements the conservative domain-cell R-pin protocol. It retains the whole existing World reservation and actual World carrier through close and outstanding returns, rather than subtracting an estimated A/H. It does **not** establish total H, a complete Scene producer footprint, partial-construction graph closure, or native/AOT acceptance. The [finite-envelope addendum](m03b-scene-preparation-finite-envelope-and-return-addendum.md) remains applicable and unchanged.

## Concrete remaining bypass and selected fence

Before this refinement, `WorldGuidRegistry.TryReleaseFresh` accepted a revoked binding when its independent-use count was zero. R is not an independent U. For a closed World with R=584 and U=0, that endpoint could therefore free its binding-owner roles even though the domain still retained the receiver. A copied grant must not supply that release right.

This is a source-derived distinguishing example, not an unsafe execution against the old implementation.

Select these rules:

1. A fresh `WorldLifetimeControl` binds its exact process owner once, after its fields and original permit are initialized. A replacement-prepared lifetime does not own that grant; `AdoptReplacementGrant` binds it before acknowledging ownership transfer.
2. The binding's owner-bound flag survives activation and revocation. A second owner bind refuses. Grant, key, binding, owner-use indices/generations and owner role must match.
3. Raw release still supports an **unbound** construction/preparation grant. A World-bound cell additionally requires a consumed matching `WorldGuidRetirementPermit`; merely possessing the original unconsumed permit is insufficient.
4. Permit consumption checks the original lifetime and, for an enrolled World, its elected retirement condition. Normal data waiting cannot consume the permit or report the old independent-use `IsRetirementPending` condition.
5. Binding-cell release clears the flag before generation advancement/freeing. A consumed permit from another binding/generation cannot release a successor.

The flag is value metadata inside the existing 128-byte binding cell, not a reference that purports to fund a lifetime. Unique binding plus the lifetime/permit state machine supplies authority; the original World/domain grants still fund and retain the actual control graph.

## Exact component accounting

Current observations are for `.NET 11.0.0-rc.3.26522.148/X64`, Debug and Release:

| Component | Extent/charge and disposition |
|---|---|
| `WorldDomainReservation` | Factory allocation remains **64 bytes**; no instance field was added. |
| `WorldSceneStorageReservation` | Factory allocation is **72 bytes**. Every factory claims `R = D + 72` before construction. Workspace uses the same full charge inside unchanged P; transferred storage does not replenish P when disposed. |
| Domain `InterestEntry` | Compiled stride remains **128 bytes**. S/C, pending construction, return-part counts, close/election flags, actual World/lifetime roots and receiver head replace reserved fields. No new cell or backing charge is issued. |
| Domain `LineageEntry` | Exact owner-interest index/generation adds eight scalar bytes. The closed field set has **96-byte managed extent** on this lane, within its existing **256-byte named root-cell charge**. The charge is not `Unsafe.SizeOf<LineageEntry>()`; it remains 256. |
| Process `BindingCell` | Compiled stride remains **128 bytes**, including the new owner-bound flag. Existing slab charge, K/O/P/U occupancy and ceilings are unchanged. |
| H and other World control | **Unqualified.** The candidate keeps the whole existing parent reservation until actual final retirement; it installs no estimated H floor or partial A refund. |

Parent and receiver observations are isolated warmed factory deltas, not World/Scene allocation totals. The cell tests independently specify complete field membership and derive/check managed stride; they do not qualify referenced graphs or other runtime lanes.

The factories' argument is data/reserved capacity D; `reservation.Bytes` includes Q. Planner retained/preflight terms and bundle measurement account for the full receiver control, rather than assuming old 2048/256/128/64-byte constants already paid it. Those other producer terms are still not a qualified footprint.

## Lower-owner state and terminal return

- **Construction:** claim original X or exact World/restore C and a constructor interest before `new`. Construct outside Scene/domain gates. Installation checks the exact parent and open World admission, then links the same initialized receiver in a value-only C→S or X→R transaction. Close-first permits only original-claim cancellation. An installed R is not constructor rollback to P.
- **Admission close:** serialize the World latch with domain admission closure, then revoke outside the domain pair. Cleanup and the World close tail retain the whole parent. Ordinary post-close release does not reclaim an imagined A/H partition.
- **Data wait:** retain the actual World, lifetime, original process owner and exact S/C/U. Last R, U, constructor unwind or close can progress; none waits under a gate or schedules a worker.
- **Other return tails:** persistence/resource reservations retain a prepaid parent return part. Their bookkeeping, disposal-gate exit and actual last World access precede the terminal handback. An independent-use control clears its own fields before its domain grant ends; a separate exact-parent part spans its entire outer `Release`, so nested Domain parts cannot elect retirement early.
- **Retirement:** after successful close, no constructors/return parts, and ended S/C/U, elect one ordinary return thread. Acknowledge original lineage retirement before consuming the process permit. Record irreversible progress; failure retains the remaining reservation/root instead of recreating a released role.
- **Parent last:** clear parent/World/lifetime links before its final interest refund. Domain Task publication is outside both gates; the domain's fixed registrar control remains charged through publication return and is not refunded on a thrown publication call.

The occupied World interest and lineage root record each other's exact index/generation. Unenrolled raw reservations retain their existing non-receiver semantics, including history retirement after the raw reservation itself ends.

There is a narrow existing **unpublished-history exception**, not an R-pin bypass: an unused, uncommitted fresh lineage reservation can end during failed construction when S=C=U=0, no constructor/return part remains, and the root is still reserved. The failed World/binding/storage remain retained or quarantined. Committed history, live receivers, construction claims and independent uses cannot take this path. This preserves the existing unpublished cleanup contract.

Lock order remains Scene gate → domain growth gate → domain gate. Persistence/owner-control latch gates end before entering the domain pair. Original lifetime/process retirement runs after dropping that pair; no higher-owner graph is invoked through the opaque receiver root.

## Evidence and limits

Ordinary Runtime tests ran Debug before Release:

| Selection | Debug | Release |
|---|---|---|
| Receiver/domain/lifetime/persistence/resource, World lifecycle and actual process-registry fixture | [110 executed/passed](../../artifacts/tst/Debug/m03b-transfer-return-debug-20261118.trx) | [110 executed/passed](../../artifacts/tst/Release/m03b-transfer-return-release-20261118.trx) |

The 20 receiver cases use real Worlds, registered Scene participants, original uses and lower reservation methods. They cover control exact/one-byte-short admission, Domain-owned install election and duplicate refusal, active transfer, original cancellation across close, C rollback, head/middle/last and duplicate returns, R/U/domain-close orders, actual World/receiver strong roots, a last return on another ordinary thread, foreign-parent/lineage refusal, and GUID exclusion through R and persistence tails. The raw-release case uses the real World and original permit; it constructs no substitute owner. The combined selection executes twelve fixtures, including the actual `TerraFX.Runtime.Tests.WorldGuidRegistryTests` namespace.

A pending-constructor claim voluntarily canceled after close is **not** an OOM observation. The source protocol retains uncertain cleanup/release debt, but real constructor failure, partial process/domain failure and physical cleanup stimuli remain unsupported where not actually reached. These are framework-test counts, not conformance-case membership. No Scene preparation, fabricated plan/snapshot/owner, production fault hook, native/AOT consumer or milestone acceptance ran.

Restore/untransferred routing and revised Q terms were compiled; actual Scene restore/preparation execution was not run or qualified by these selections.

## Remaining producer blocker and rectification proposal

The lower credit protocol must not be presented as a completed receiver feature.

`RenderSceneAssetBundle.PrepareAsync` still acquires partial Atlas/Mesh/Material state before creating the final bundle and setting that bundle as the receiver's retained owner. Its static async invocation is not itself a precreated owned construction graph. Keeping reservation credits on a cleanup exception, as the candidate now does, does not independently establish a strong root for every partial resource or the invoker's final return.

**Selected next rectification:** reuse the existing bundle as its actual precreated construction owner, not a proxy or additional carrier:

1. Close its complete partial/final field set: arrays, current acquisitions/release item, loaded counts, exact grants and failure progress.
2. Claim that owner's whole construction/control peak through the original preparation or Persistence scalar issuer **before** constructing it. Root the actual owner in the existing stable preparation/restore operation before the first fallible allocation or asset/helper invocation.
3. Store every acquired value before fallible validation/attachment. Transfer only its identified materialized/reserved capacity into the settled R; residual scratch and invocation/publication work stay on their original claims.
4. Release independently safe siblings while retaining failed items and exact debt. A no-op repeated disposal is not physical acknowledgment.
5. Complete the addendum's owned ordinary supplier/resumption/context/registration and final handback protocol. Task readiness or another observer is not the missing invoker return.

**No exact final bundle-control amount or complete J/K can be supplied from this candidate.** The smallest missing input is the final actual construction/invocation graph and disjoint before-allocation grant ledger, including the existing native fault-capture obligation. Compile/layout evidence can corroborate that graph; it cannot replace it. Do not execute preparation to discover the amount or add a new worker, generic async abstraction, synthetic owner, guessed constant, smaller cap or new option.

Keep `PreparedScene.QualifiedFootprint` null. Neither the new fence nor the lower tests authorize public preparation, producer/native qualification, or a numerical H carve-out. Existing restore issuers do not acquire a qualified producer footprint merely by using this lower protocol.

## Review correction: installation and outer-use return

This correction supersedes the earlier candidate's staged installation and use-return suffix reasoning. It does not weaken the producer gates above.

### Nonthrowing managed installation commit

`WorldSceneStorageReservation.TryTransferWorkspaceToStorage` no longer sets `StateTransferring`. Both `TryCompleteSceneReservationConstruction`'s storage branch and `TryInstallSceneWorkspace` enter the domain pair and use the same `TryInstallSceneStorage` transaction:

1. Before changing state, validate the exact open parent, expected Constructing/Workspace lifecycle, empty receiver links, current parent B/U and S/C/pending ledgers, constructor count and existing head ownership/link state.
2. For use funding, validate the original active use/lifetime, operation-interest index/generation/kind/identity and matching lineage, active X, operation bytes and parent U. Capture the operation's managed array reference before the election. For World/restore funding, validate the exact original C/pending subset.
3. Perform **every checked sum/subtraction before election**, including new parent B/U, operation bytes, active X, S/C/pending values and constructor count. These calculations and all refusal paths have no ledger or lifecycle side effect. A validation exception leaves the original Workspace/Constructing state and exact original claims intact.
4. Only then CAS the expected lifecycle to `StateTransferring`, while both gates remain held. If disposal won that CAS, return false without a transfer or state reset.
5. After a successful CAS, execute only assignments of precomputed values, the two nonvalidating `CommitSceneWorkspaceTransfer` field writers, atomic intrinsics, and captured nonnull neighbor/receiver field stores. No checked arithmetic, index lookup, allocation, callback, physical release, validation helper or throwing ownership handback follows the election. `MarkStorage` publishes `StateStorage` with a final volatile write.

For active use funding the single transaction is:

```text
X' = X - R                   B' = B + R
U' = U - R                   S' = S + R
operation.Bytes' = operation.Bytes - R
C' = C                       F' = F
domain total is unchanged
```

There is no intermediate uninstalled C or `UninstalledActiveWorldWorkspaceBytes` provision to undo. Scene installation no longer calls `TryTransferActiveWorldGuidUseWorkspace` followed by `CompleteWorldWorkspaceInstallation`; the latter helper is removed. World/restore installation instead moves C/pending→S without changing B/U. The parent/control writers contain only scalar field stores or `Interlocked.Exchange`; captured references are nonnull and both gates prevent array replacement or competing ledger mutation. Neighbor setters contain only field stores. Gate exits occur after `StateStorage` is published.

Consequently, a normal refusal does not need to restore Workspace, and the code never resets an uncertain completed transfer to retryable Workspace. The new real lower-owner case calls the Domain endpoint without a pre-elected receiver and verifies successful election, exact conservation, duplicate refusal, subsequent disposal and post-close use return; it would refuse under the former caller-CAS contract.

No valid admitted input supplies a deterministic post-election managed exception: counts and byte products are bounded, and all checks/calculations now precede the CAS. No private-state corruption or production fault hook was added to simulate one. This is a source-level managed transaction proof, **not** qualification of asynchronous CLR/OS interruption, JIT/native failure or an unreachable partial native store sequence. No recovery/refund is inferred for those unexercised modes, and complete preparation remains unavailable.

### Full outer `WorldGuidUseControl.Release` part

After winning Active→Releasing, `Release` captures its Domain/registry locals and obtains `TryRetainWorldGuidUseReturnPart` before process release. Registration checks the original operation interest and exact parent/lifetime/lineage; it does **not** require open World or domain admission. Already accepted U can therefore return after either closure. The part occupies the existing prepaid parent-cell counter and retains the whole original World/parent/lifetime; it creates no new interest, carrier, U or byte allowance.

The outer part remains live across process release, the Domain's nested return part, and every control write. `TryReleaseIndependentUse`/`RollbackUnissuedWorldGuidUse` still call `CompleteDomainRelease` **before** refunding the use credit: anchor ownership/links and `_carrierState` are settled while the original `256+F+X` is still charged. The new part is not an emergency replacement for that 256-byte control grant.

For an enrolled World, the nested Domain part cannot be the last part: its end leaves at least the outer part live. On Domain success, the rest of `Release` uses only captured locals and the final `finally` handback; it does not dereference or write the ended control. The only post-Domain quarantine write is on a **false** return, whose validation has not refunded U. The exception catch that writes quarantine covers only registration/process work while U remains funded, not a successfully refunded Domain/publication suffix.

The outer `finally` ends its exact-parent part as its last owned operation. Until then neither a nested return nor a racing World-close acknowledgment can retire the parent. Normal return does not become quarantine. Unenrolled raw-reservation behavior remains distinct. The unissued rollback caller has no later control-field cleanup; its own terminal field clearing remains before its Domain refund.

## Frozen scope

Review-correction hashes were captured at `2026-11-18T00:42:11Z`. HEAD is `7ffbef5cbbf2cbd0068e93573c01f4e8d1cee919`; the corrected implementation remains uncommitted. The correction explicitly reassesses/replaces the affected input identities from the earlier proposal revision; its prior source hashes and 98+11 selections are not reaccepted as evidence for the changed transaction. The following raw-file hashes identify the complete scoped current review input, not a transitive build receipt or repository inventory.

```text
1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c  global.json
a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245  docs\design\04-capacity-and-workload-profiles.md
174b2465746da79cef560c8506216ef4f99f9d1aabac184f872c9e6cb0b94e0b  docs\design\12-entities-components-and-queries.md
24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87  docs\design\35-implementation-roadmap.md
17671f087293fbfb4c454432b7c016076ab612508aa3eda85f13acf54d3cb6ec  docs\proposals\m03b-scene-receiver-control-funding.md
3eb20e1051c55ee77d008cf734007fde8c28aec24abd9755f4d1788475bd4605  docs\proposals\m03b-scene-preparation-finite-envelope-and-return-addendum.md
c7dcba4dfef58346c6c851f77a8e130b1a4b776248e94663438a5d9e449245a8  sources\TerraFX.Runtime\Entities\World.cs
a250eecca2cfb4e0eebff7f447e66ac7e44a8d23ad0d6de141e8e1d4c30614e3  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
53111972c5b480e8357c93d471edcb4e08c7bbe65bd341bfc7260fdc356ba5c3  sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.SceneStorage.cs
d6cdd6105850b8076d40891b0ed1f583d9fcde6ee190444a7d570879964403dc  sources\TerraFX.Runtime\Entities\WorldDomainReservation.cs
a91aac1a9eb2ba7b43c111fab8baa9d6588a6c75e725c28ea9825f049472c969  sources\TerraFX.Runtime\Entities\WorldSceneStorageReservation.cs
ddeb051100a203a4662b883e64ab8d688321dec50000939123ee97c1718011fe  sources\TerraFX.Runtime\Entities\WorldGuidRegistry.cs
456587893972e153b872dbbd4d849b67260a297db4d6dea2fbde7cfe6e7ec0fc  sources\TerraFX.Runtime\Entities\WorldGuidRetirementPermit.cs
671390b19880e4ec007a88d588b4da318fa4c67b1d9b03a9cf00636ab051cff2  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs
f00b54b0e8f305f90df0d63ffdf6c3419feb8f3d2542e72d76a79ad2d0990946  sources\TerraFX.Runtime\Entities\WorldGuidUseControl.cs
c6f41de02f0896016fa55845a61661bcd1a4dfe085bf68302767fe9c94da3228  sources\TerraFX.Runtime\Entities\WorldLifetimeControl.CosmeticCredits.cs
a6120452989f29ec2ef9c2b90e7479cc6635b9b17848c6c683063b3f99d36d22  sources\TerraFX.Runtime\Entities\WorldGuidUse.cs
921343548584f389fb2fc0db6c03965a206c54c1ded4d35c6bcf5cccf157a7d9  sources\TerraFX.Runtime\Entities\WorldGuidReplacementPreparation.cs
bcf8f830fbb29e652d480608a3bdfc197d89d7e58299622941e87685b4c2aad4  sources\TerraFX.Runtime\Entities\WorldDefaultDomainOwner.cs
3e2594104dd57a2c85c84c5fc2f1a7c9895b2bc6590f17b57c063698a3c1a300  sources\TerraFX.Runtime\Entities\World.ResourceState.cs
ad8141a07fc8018117c4abeaefbaece3f94a639cb69379c83af99e3082fb40a5  sources\TerraFX.Runtime\Entities\WorldResourceOperationReservation.cs
de939aba0df03d9c8ba4430f6605e49c86d1a54b8aa8b36698dfb5de1fa5331a  sources\TerraFX.Runtime\Entities\World.PersistenceAdmission.cs
beef96488f9e99f8a852b8ae4d34fc82ca4d77af9ce508032ffb50a48fe3ccbb  sources\TerraFX.Runtime\Entities\WorldPersistenceAdmissionReservation.cs
d3b20bee7fbd4ca540eb9e082dfeca7acb533179bf20ae9ee13b54e5560845a5  sources\TerraFX.Runtime\Entities\WorldConstructionPlan.cs
4ec58808bb5400448f71327e75cab787ed03c1ce43fa39ffae832cea8c4ad137  sources\TerraFX.Runtime\Scenes\SceneExpansionPlan.cs
1e01cc392652771a948d45f79fca19fc1ed5c4659439ad4ec8879c2e99127876  sources\TerraFX.Runtime\Scenes\ScenePrefabExpansionPlanner.cs
b73dd4314b63084132afc2f9325ed98eeccce65a174f02b16f2ded7ec65f688c  sources\TerraFX.Runtime\Scenes\PreparedScene.cs
689dd5a2237398fee46b01ed00538cae66d36b8e30ad2aaa0488cc3bb6c08260  sources\TerraFX.Runtime\Rendering\RenderSceneAssetBundle.cs
8f0ef274c39c3e07e691951011ac86c574311912d7f78348eb8d6780555f0c71  sources\TerraFX.Core\ReentrantGate.cs
9f884c2185cb3372914457dc72ec760666ede1bd2388a387a08b64f3e17b894c  tests\TerraFX.Runtime.Tests\Entities\WorldAdmissionDomainTests.cs
70305f994186d58a14fa5a53dcab0bd976d8beaa0cf37106dff13816ef9e58b2  tests\TerraFX.Runtime.Tests\Entities\WorldSceneReceiverTests.cs
d044b061eeeb2a1b03b4f73ed8d640e4b6138ece9993f6446d9686d0e72b74eb  tests\TerraFX.Runtime.Tests\Entities\WorldDefaultDomainOwnershipTests.cs
6c987ab754e5e6fa92480bc35c8f449ed08bed3fa548692f46030cafdfee1420  tests\TerraFX.Runtime.Tests\Entities\WorldGuidRegistryTests.cs
f52196a4ea7f99ab47931b436cf7ce9512d7a10f8ada80860d6928da90f552ae  tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs
5743f15195dd8f45f825e07cbd6b0210255d5ce3f6f7c562665c10c34cab698a  tests\TerraFX.Runtime.Tests\Entities\WorldPersistenceAdmissionTests.cs
db8653c33687e1ec032c3817849f33b8dd3c93e159a3843c2135185a93de6748  tests\TerraFX.Runtime.Tests\Entities\WorldPersistenceCaptureAdmissionTests.cs
0c7daacb94810d867ffadda9c6549f795c006bdef290a48b73c939912a36a1ee  tests\TerraFX.Runtime.Tests\Entities\WorldResourceAdmissionTests.cs
e53a886238a95ff58fd683da45d68cee8d124d12c2087a11ffc27484d54fd385  tests\TerraFX.Runtime.Tests\Entities\WorldSystemFactoryTests.cs
8e98098c036c7692e9d6618d15d2912db904033c702d9279a1ae53a82ae7d2e9  tests\TerraFX.Runtime.Tests\Entities\WorldSystemStatefulFactoryTests.cs
76b9ed4bd4ea452b0f03626b2ae0b99cdcc5818a84e634d69823a9fd837f2668  tests\TerraFX.Runtime.Tests\Entities\WorldAttachmentTests.cs
b915bb3b96f7b5008e946c74f2eab726b05a200ea45db7cd7724e16e83e6cc60  tests\TerraFX.Runtime.Tests\Scenes\SceneSupportRegistrationTests.cs
a71d68c288f4a34c4123ee4e8a6d5e52649435037b834a60171667d0616e18a2  artifacts\tst\Debug\m03b-transfer-return-debug-20261118.trx
d3122a0306358c85a6dc855dba595ae112d293c18ec7664095949c733b1259d1  artifacts\tst\Release\m03b-transfer-return-release-20261118.trx
```

Recheck the scoped hashes before applying a disposition. This proposal captures the receiver candidate at the frozen revision above; it changes no source implementation, maintained contract, public API or cap.
