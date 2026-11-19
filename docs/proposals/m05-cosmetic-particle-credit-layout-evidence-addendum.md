# M05 credit-cell layout evidence slice

## Decision and boundary

**Tentative evidence result, not an executable owner profile.** The managed element stride and World accounting charge for the actual `WorldCosmeticCreditOwner._cells` backing were observed in the current Debug and Release lanes. This closes that concrete **size/capacity input**, independently of any particle Task publisher.

It cannot close the complete storage-owner admission/lifetime row. A real World by itself does not supply the missing standing preclaim, lifetime-to-owner retaining edge or terminal storage handback. Keep candidate construction, particle admission and Scene preparation unavailable.

The [budget/return addendum](m05-cosmetic-particle-budget-return-addendum.md), sections 3 and 7, already owns the accounting/catalogue requirements; the [credit-handback addendum](m05-cosmetic-particle-credit-handback-addendum.md), sections 2, 4 and 5, owns the grant/return contract. This slice selects an actual compiled storage row, not another budget or handback policy.

## Why this row is separable

`Entities\WorldCosmeticCreditOwner.cs` now contains one fixed `new Cell[MaximumCredits]`, with capacity 128 and no resize path. The actual private `Cell` contains thirteen four-byte scalar/enum fields, one Boolean and three managed references. The referents are not part of the element stride: a reference slot is not another prepaid use control or an owned exception graph.

`HasQualifiedStandingOwner` remains false, and the sole construction entry rejects before allocating the owner/backing. No World construction/disposal integration supplies this graph. Existing passing legacy tests therefore do not establish candidate storage ownership or lifecycle.

Layout observation need not call that entry, acquire a World use, create a Task, invoke an observer, or construct an emitter. Its inspection overhead belongs to the test process; that is not permission to exempt corresponding allocations in a future production constructor.

## Executed evidence batch

The existing targeted managed-layout case in `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` was run with the ordinary Runtime test project. No production accessor, alternate owner, test-only producer, qualification flag or new runner was added.

1. Freeze the candidate source, test source, `global.json`, resolved repository-local runtime, and emitted Runtime/test assemblies. The test output reports the loaded Runtime module's MVID per configuration; the exact file hashes and result files are listed below.
2. Resolve the actual private nested `Cell` from that Runtime assembly. The test verifies its field/type inventory, sequential value layout, enum storage, array element type and capacity. It does not construct `WorldCosmeticCreditOwner`, `Cell[]`, a World, or any particle operation.
3. Derive the target-specific expected managed stride from thirteen four-byte scalar/enum fields, one Boolean and three references, including pointer alignment; observe the actual `Unsafe.SizeOf<Cell>` through reflection. `Marshal.SizeOf` and the class-reference size are not used as substitutes.
4. Apply the existing `World.ArrayBytes` rule to the observed stride and capacity. The rule's charge is recorded separately from managed stride; it is not a measured GC allocation size or an admitted World claim.
5. Run exactly this case in Debug and Release with nonzero selected/executed counts and retain the current-run TRX files under ignored `artifacts\tst`.

Do not invoke the candidate constructor or methods, bypass its gate, allocate an uninitialized owner and call it operational, attach anything to a World, or execute Scene/pool work.

The measured result is **80 bytes per `Cell`**, capacity **128**, and **10,304 bytes** under `World.ArrayBytes`, in both configurations on Windows x64, `.NET 11.0.0-rc.3.26522.148`. The result closes only the array's stride/capacity/accounting input for this lane. The owner object's shallow size, actual standing preclaim, other construction/runtime children, failure overlap, retention and final release remain separate rows. This is not a numeric construction profile, an operation/publisher envelope, native/AOT qualification or milestone acceptance.

| Configuration | Selected/executed | Cell stride | Capacity | World accounting charge | Runtime module MVID |
|---|---:|---:|---:|---:|---|
| Debug | 1/1 | 80 B | 128 | 10,304 B | `6c925afe-0228-4ce0-823a-ea89dbb120c2` |
| Release | 1/1 | 80 B | 128 | 10,304 B | `4188fb12-62d1-4894-8d5f-116c5527e18b` |

| Frozen input/result | SHA-256 |
|---|---|
| `global.json` | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| `sources\TerraFX.Runtime\Entities\WorldCosmeticCreditOwner.cs` | `7eeff227d9654552997bf3515e29ca32e3791f0a155fbf1aa37c274508e2cea6` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `f52196a4ea7f99ab47931b436cf7ce9512d7a10f8ada80860d6928da90f552ae` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Private.CoreLib.dll` | `df53885902e896a2b40988237cbe9cbb9f1bb0743200f05fe9257c56a29c5732` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\coreclr.dll` | `46a09d97ad8dd30d400a86c90af707bee7593b585d3ccb8586c4fffca404d1e2` |
| `artifacts\bin\sources\TerraFX.Runtime\Debug\net11.0\TerraFX.Runtime.dll` | `e0173d41726353fff15f0642cee7893b856acd49624b1c9afbe9873c43281e95` |
| `artifacts\bin\sources\TerraFX.Runtime\Release\net11.0\TerraFX.Runtime.dll` | `4c9de2f924c5bdbb3dc3bc960a75c3c37ff207e5ee417fd09e72b5bdc9d9968d` |
| `artifacts\bin\tests\TerraFX.Runtime.Tests\Debug\net11.0\TerraFX.Runtime.Tests.dll` | `9c52c8d45959c498041f2946703fa4d10cab0ea0491385bc77c1cd2a788987ac` |
| `artifacts\bin\tests\TerraFX.Runtime.Tests\Release\net11.0\TerraFX.Runtime.Tests.dll` | `0fa9e6bad38f02bf165ab63fa5d6f6e0b3c2e5b2b1381c2960ee9d11c56aa28` |
| `artifacts\tst\Debug\particle-credit-cell-layout-current-debug.trx` | `952d95d0c2a7d598b200df007cf8275bade42c03aed4414eb8099f5937b8247c` |
| `artifacts\tst\Release\particle-credit-cell-layout-current-release.trx` | `90667ee196ecd22634e086fa1d88eade5230b97f0f38f3ccae2b2cdb21f51f46` |

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
| `Entities\World.cs` | `45291C235C234214DDCF4FEF45E02D1416D5F1EFFD3E39A1729C4396199FA36C` |
| `Entities\WorldAdmissionDomain.cs` | `0AFBC247E5CE649EDBD6F26A01F67BD291D7F23CD1529DD5CD735E89DFFD70F7` |
| `Entities\WorldLifetimeControl.cs` | `5A304AAF8B2353E28F6F743D5587E82F12E979DCF0DFEBF55103E075CCAF54F2` |
| `Entities\WorldLifetimeControl.CosmeticCredits.cs` | `C6F41DE02F0896016FA55845A61661BCD1A4DFE085BF68302767FE9C94DA3228` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `F52196A4EA7F99AB47931B436CF7CE9512D7A10F8ADA80860D6928DA90F552AE` |
| `docs\proposals\m05-cosmetic-particle-budget-return-addendum.md` | `1574E6D573BDFE2AF45A3E44EE3BAB5FE52131A02547C8610294F8BBF3C4B119` |
| `docs\proposals\m05-cosmetic-particle-credit-handback-addendum.md` | `6FF6EA4E6BA175558C492155FC95E08B936C1171B6747CA6E499166A2F8CB19C` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `artifacts\bin\sources\TerraFX.Runtime\Debug\net11.0\TerraFX.Runtime.dll` | `E0173D41726353FFF15F0642CEE7893B856ACD49624B1C9AFBE9873C43281E95` |
| `artifacts\bin\sources\TerraFX.Runtime\Release\net11.0\TerraFX.Runtime.dll` | `4C9DE2F924C5BDBB3DC3BC960A75C3C37FF207E5EE417FD09E72B5BDC9D9968D` |
| `artifacts\bin\tests\TerraFX.Runtime.Tests\Debug\net11.0\TerraFX.Runtime.Tests.dll` | `9C52C8D45959C498041F2946703FA4D10CAB0EA0491385BC77C1CD2A788987AC` |
| `artifacts\bin\tests\TerraFX.Runtime.Tests\Release\net11.0\TerraFX.Runtime.Tests.dll` | `0FA9E6BAD38F02BF165AB63FAF5D6F6E0B3C2E5B2B1381C2960EE9D11C56AA28` |
| `artifacts\tst\Debug\particle-credit-cell-layout-current-debug.trx` | `952D95D0C2A7D598B200DF007CF8275BADE42C03AED4414EB8099F5937B8247C` |
| `artifacts\tst\Release\particle-credit-cell-layout-current-release.trx` | `90667EE196ECD22634E086FA1D88EADE5230B97F0F38F3CCAE2B2CDB21F51F46` |

This update adds the managed size result and its current-run evidence only. No source/tests, maintained contracts or roadmap are changed. No owner constructor, World, Scene, pool, particle operation or lifecycle probe was executed; the full standing-owner and publication profile remains unavailable.
