# M05 credit-cell layout evidence slice

## Decision and boundary

**Tentative evidence plan, not an executable owner profile.** Select one dependency-ready observation: the managed element stride and World accounting charge for the actual `WorldCosmeticCreditOwner._cells` backing. This can close that concrete **size/capacity input**, independently of any particle Task publisher.

It cannot close the complete storage-owner admission/lifetime row. A real World by itself does not supply the missing standing preclaim, lifetime-to-owner retaining edge or terminal storage handback. Keep candidate construction, particle admission and Scene preparation unavailable.

The [budget/return addendum](m05-cosmetic-particle-budget-return-addendum.md), sections 3 and 7, already owns the accounting/catalogue requirements; the [credit-handback addendum](m05-cosmetic-particle-credit-handback-addendum.md), sections 2, 4 and 5, owns the grant/return contract. This slice selects an actual compiled storage row, not another budget or handback policy.

## Why this row is separable

`Entities\WorldCosmeticCreditOwner.cs` now contains one fixed `new Cell[MaximumCredits]`, with capacity 128 and no resize path. The actual private `Cell` contains thirteen four-byte scalar/enum fields, one Boolean and three managed references. The referents are not part of the element stride: a reference slot is not another prepaid use control or an owned exception graph.

`HasQualifiedStandingOwner` remains false, and the sole construction entry rejects before allocating the owner/backing. No World construction/disposal integration supplies this graph. Existing passing legacy tests therefore do not establish candidate storage ownership or lifecycle.

Layout observation need not call that entry, acquire a World use, create a Task, invoke an observer, or construct an emitter. Its inspection overhead belongs to the test process; that is not permission to exempt corresponding allocations in a future production constructor.

## Smallest next evidence batch

Propose one targeted managed-layout case in the existing `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs`, alongside the existing lower-control layout observations. No production accessor, alternate owner, test-only producer, qualification flag or new runner is needed.

1. Freeze the actual candidate/source and resolved SDK, compiler, CoreLib/CoreCLR, architecture, configuration and binary inputs. `global.json` selects the SDK; it does not identify every runtime/layout input. Establish source-to-binary correspondence before applying a result.
2. Resolve the actual private nested `Cell` from the Runtime assembly. Verify its exact field/type inventory and compiled layout metadata against the frozen source. Do not copy it into a probe/shadow struct.
3. Derive the target-specific expected managed stride/alignment from that inventory and the target runtime's actual managed-layout rules, then observe the closed `Unsafe.SizeOf<Cell>` result through reflection. Do not use `Marshal.SizeOf` for this reference-containing managed value, or `Unsafe.SizeOf<WorldCosmeticCreditOwner>` as class instance size. If the target's layout derivation cannot be established, leave the numeric row open.
4. Apply the existing `World.ArrayBytes` rule to that stride and capacity 128. Record the single backing's accounting charge separately from the observed stride. The rule's allowance is not a measured GC allocation size, a claim already admitted by a World, or an end-to-end allocation observation.
5. Use ordinary owning Runtime commands, Debug then Release, for that exact new case. Require nonempty execution, matching frozen inputs and explicit per-lane results. Preserve readouts in ignored TRX/logs, not maintained prose.

Do not invoke the candidate constructor or methods, bypass its gate, allocate an uninitialized owner and call it operational, attach anything to a World, or execute Scene/pool work. No such observation runs in this proposal turn.

The result closes only the array's stride/capacity/accounting input for the measured lane. The owner object's shallow size, actual standing preclaim, other construction/runtime children, failure overlap, retention and final release remain separate rows. This is not a numeric construction profile, an operation/publisher envelope, native/AOT qualification or milestone acceptance.

## Why a lifecycle probe is not enabled yet

| Question | Disposition |
|---|---|
| Does observer multiplicity invalidate this layout-only observation? | No. It neither creates nor publishes an operation result. |
| Can real World ownership alone make the current ledger executable? | No. The production strong edge, admitted standing provision and close/retention integration are absent; passing actual grants as constructor arguments does not supply them. |
| Would a no-particle-Task acquire/release cycle necessarily be publisher-free? | No. `WorldAdmissionDomain.TryReleaseIndependentUse` can complete `_retirement.TrySetResult()` after actual domain release; rollback paths also contain retirement publication. `RunContinuationsAsynchronously` does not prove that suffix free or returned. |
| Can keeping the World/domain open isolate a later ordinary release observation? | It can exclude the retirement-publication branch for that reached interval, once actual ownership/funding exists. It cannot qualify World close, domain retirement or failed cleanup, nor justify abandoning the probe's final ownership. |

The minimal capability before a **candidate lifecycle** observation is the already selected production World-lifetime binding: actual preclaimed storage/construction/cleanup provision, a strong retaining edge installed before acquisition, and original-issuer retention/close handback through the real caller suffix. Its closed synchronous storage-only graph can be qualified separately from cosmetic Task publication; do not require a fictitious particle publisher to measure it. Conversely, neither an open-World-only result nor the layout readout pays for a later domain-retirement publisher.

Stop after the layout input if that capability remains missing. Do not flip the construction gate, invent a byte margin, treat the existing 65536-byte request as measured capacity, or duplicate the legacy primitive tests as candidate lifecycle evidence.

## Inspected input boundary

Source paths below are under `sources\TerraFX.Runtime` unless qualified otherwise. These are inspection hashes, not proof of source/binary correspondence or runtime allocation/return qualification. Reconcile changed bytes before using a disposition.

| Input | SHA-256 |
|---|---|
| `Entities\WorldCosmeticCreditOwner.cs` | `7EEFB227D9654552997BF3515E29CA32E3791F0A155FBF1AA37C274508E2CEA6` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\WorldAdmissionDomain.cs` | `2CE1C0B90157ADE6F82605409AB599F7415B7D8AA0A63A4BF4327263610248D9` |
| `Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `Entities\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `B4E0683405ADAB476B5B4CF9108E8C7B1E30E33B6CD48EC55A28AECA57EDFFBB` |
| `docs\proposals\m05-cosmetic-particle-budget-return-addendum.md` | `1574E6D573BDFE2AF45A3E44EE3BAB5FE52131A02547C8610294F8BBF3C4B119` |
| `docs\proposals\m05-cosmetic-particle-credit-handback-addendum.md` | `6FF6EA4E6BA175558C492155FC95E08B936C1171B6747CA6E499166A2F8CB19C` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `artifacts\bin\sources\TerraFX.Runtime\Debug\net11.0\TerraFX.Runtime.dll` | `E3CB2F7208A7E1DC8D2EC5C2A33A4489AC69E01F31D0C95CDC44B4FBC2FB24E8` |
| `artifacts\bin\sources\TerraFX.Runtime\Release\net11.0\TerraFX.Runtime.dll` | `23A4D2548321868F1FC5954D018384A2FA0FC7BE642967046C748D3C3076898F` |

Only this proposal is added. No source/tests, maintained contracts or roadmap are edited; no build, test, allocation probe or Scene/pool execution occurs.
