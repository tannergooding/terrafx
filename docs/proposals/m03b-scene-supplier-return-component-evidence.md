# M03-B Scene supplier-return component evidence

**Status: tentative evidence addendum, not an accepted contract, finite J/K catalogue, return capability, or qualification. Keep `PreparedScene.QualifiedFootprint` null.** This addendum supplements [the supplier-return closure proposal](m03b-scene-preparation-supplier-return-closure.md); it does not change its disposition.

## Current RC3 component observations

The selected lane is Windows x64, SDK/CLR `.NET 11.0.0-rc.3.26522.148`, `net11.0`. In a fresh process, a temporary harness invoked the actual private `PreparedScene` constructor through a dynamic call site with null `World`/`AssetStore` and default value inputs, retained the resulting objects, and exited without calling preparation methods. Debug and Release both measured 992 B for the first constructor call and 776 B per warmed root.

Existing component tests on the same lane account for the warm constructor total:

| Component | Bytes |
|---|---:|
| `PreparedScene` carrier shell | 488 |
| `TaskCompletionSource<PreparedScene>` and its Task | 96 |
| `Exception[2]` fault pair | 40 |
| Two bound root callbacks | 112 |
| `Lock` | 40 |
| **Warm root total** | **776** |

A separate fresh-process component probe measured the TCS/Task at 312 B cold and 96 B warm. Its 216-B cold excess equals the difference between the root's first and warmed construction. This associates the cold increment with first use of `TaskCompletionSource<PreparedScene>` on the selected runtime, but does not establish a process-lifetime payer or the complete cold-root path.

The Release JIT for `SceneLoader.StartPlanPreparation` contains three allocations before entering the async planner: one compiler-generated closure holding `SceneLoader` and `WorldGuidUseControl`, plus the `ReserveStorage` and `ReserveWorkspace` delegates. Fresh-process component probes measured the closure at 32 B and each delegate at 56 B, for a 144-B producer prefix. RC3 Release compiles `ScenePrefabExpansionPlanner.PrepareCoreAsync` as a 136-B value-type state machine carrying the 48-B `SceneExpansionLimits` and two 16-B awaiters. Debug uses a class with a 192-B constructor-skipped shell and additional spill/cleanup fields. Neither measurement includes the actual async Task/state-machine box, nested planner and AssetStore operations, completion dispatch, context restoration, or their last-access boundary.

## Return disposition

`PreparedScene.StartPlanPreparationCore` sets `_supplierProducersUnjoined` before invoking the planner; no code path clears it. `EndPreparationProducer` refuses to finish while that latch remains set. The Task exposes result readiness, not return of the supplier's generated state machine or continuation/context tail. The observations above identify storage rows for a future catalogue but provide no production return receipt.

The exact missing capability and alternatives remain those in the supplier-return closure proposal. Do not clear the latch on Task completion, add another observer Task, or activate the public Scene path. A separately accepted and funded last-access handback or another owner-approved return boundary is still required before deriving an executable footprint.

## Input identity and scope

No maintained design chapter, source file, test, public declaration, or acceptance ID is changed by this addendum. No Scene preparation, package read, native operation, public consumer, or NativeAOT program was executed.

| Input | SHA-256 |
|---|---|
| `sources/TerraFX.Runtime/Scenes/PreparedScene.cs` | `1cf87fa755f76034509866dee5060f2be4828b1843845d86a02d9f87ceb5c6c8` |
| `sources/TerraFX.Runtime/Scenes/PreparedScene.Preparation.cs` | `beed8cc76c4ccd55b487a96b52a31d4765a4e40513ef09883d7b0f56a201a8ff` |
| `sources/TerraFX.Runtime/Scenes/SceneLoader.cs` | `4903a86fc323ed906a0045288bebab68b37c5b6beeff6312ea0440a9d05d453c` |
| `sources/TerraFX.Runtime/Scenes/ScenePrefabExpansionPlanner.cs` | `fe4c33428b41ffe48e7360db7100d7cd3a3fa4d137f9de520dc6bb5ebce8989e` |
| `artifacts/bin/sources/TerraFX.Runtime/Debug/net11.0/TerraFX.Runtime.dll` | `093de88b29b805f65f19c2acf28fcfba5839199d08014ec75da2c563b93d2ebd` |
| `artifacts/bin/sources/TerraFX.Runtime/Release/net11.0/TerraFX.Runtime.dll` | `cb02fdf8bab1e2cd4562872dc4d20cc3b5bc78bac80c1cd4804bd5eae2ca0027a` |
| `HEAD` | `a7bf12bc94430f0111a51216ce05254617aa9abc` |
