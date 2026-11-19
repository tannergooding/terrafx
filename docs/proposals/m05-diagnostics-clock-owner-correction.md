# M05 Diagnostics clock-owner evidence

**Tentative evidence addendum; owner disposition pending.** The [allocation-profile proposal, section 4.7](m05-diagnostics-allocation-profile-closure.md#47-p1p2-owner-trace-and-minimum-protected-state-decision)'s **40-byte boxed static is supported for its identified Windows x64 CoreCLR lane**. It is neither merely inline static data nor the `TimeProvider.System` singleton. Removing this P2 subcase because `ClockState` is a struct would be incorrect. The supported funding conclusion is “no lifetime payer demonstrated by the examined paths,” not “no possible payer exists.”

## Reviewed inputs

Read-only review on 2026-11-19:

| Input | SHA-256 |
|---|---|
| Current `sources\TerraFX.Core\DiagnosticClockDomain.cs` | `d194906d28d774df01a83dc7c554e0af82645de90fa9931d415dbd02134bd680` |
| Unchanged allocation-profile proposal | `f43381af98c8c02d587a95bf02f467b4f7f32d23f9ceaf421b69cedc54d56924` |

Existing Debug/Release Core images were copied to stable evidence locations before final inspection. Their embedded PDB SHA-256 checksums match the current clock and `DiagnosticsService.cs`; the latter is not the older proposal's service input. Context-copy behavior is outside this review. The reviewed MVIDs are Debug `cfa77c17-aa4c-4d93-a594-cc43d755cddb` and Release `e828551c-5c3a-4485-96b6-4527f13d45e6`. This is a scoped checksum/IL inspection, not whole-image qualification.

The complete source, contract, image and runtime hash freeze, with a baseline of pre-existing unrelated changes, is preserved in ignored `artifacts\analysis\diagnostics-clock-owner-review\inputs.json`, SHA-256 `58f6b5a587a971fac36926f10138ab4d04b35aec9c2f96e648705e1692819bb3`. `metadata.log` and `runtime-source.log` beside it retain the evidence. One unrelated-change row recorded `docs\proposals\m05-cosmetic-particle-standing-owner-funding-route.md` at SHA-256 `e2378fa0c28fa28f23fb3c25f3be873314360d5c99b785d03e9b5ba356d8f358`; that document is now committed and its current hash is `DF70AB845257744B1E76B71558FCDFC27C4228A0CBF4D1B35BDC88BABF3AC93A`. It is not one of the 22 reviewed source/contract/image inputs; reconcile this unrelated baseline before any broader disposition. The target runtime is `11.0.0-rc.3.26522.148`, commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; this review did not execute that target.

## What actually occupies storage

Both Core images declare `s_state` as a non-RVA, static, init-only **value-type** field. Their initializer is:

```text
call   TimeProvider.get_System
newobj ClockState..ctor
stsfld DiagnosticClockDomain.s_state
ret
```

There is no C# boxing conversion or IL `box`. [`newobj` for a value type](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.emit.opcodes.newobj?view=net-11.0) constructs a value; it does not itself establish a heap allocation. That does **not** determine the VM's physical static-field representation.

The matched CoreCLR [field classifier](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L4460-L4507) classifies this non-RVA value static as boxed. [`AllocateRegularStaticBoxes` → `AllocateRegularStaticBox` → `AllocateStaticBox`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtable.cpp#L3440-L3530) allocates and roots the box. Because `ClockState` contains a GC reference, it cannot take that path's reference-free frozen-object shortcut; it reaches `AllocateObject`.

| Storage | Exact distinction on this lane |
|---|---|
| Core's `ClockState` box | One provider reference and two `long` fields give 24 bytes of value data. The matched [boxed-size rule](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L11448-L11456) and [object constants](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.h#L85-L108) give 40 allocated bytes, including 16 bytes of object overhead. The 24-byte value is inside that box, not another allocation. |
| Static field storage | An 8-byte GC-static reference cell roots the box; `s_lastTimestamp` is an 8-byte primitive in non-GC static storage. Neither cell is another boxed object. Static-base/loader bookkeeping is separate; these cell sizes do not bound it. |
| BCL provider | The matched [BCL source](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/Common/src/System/TimeProvider.cs#L21) and CoreLib IL initialize `TimeProvider.System` with one `SystemTimeProvider` class instance. It has no instance fields, including inherited fields: its shallow extent is the lane's 24-byte minimum, **not 40**. BCL static fields are separate again. |

`TimeProvider.get_System` subsequently returns that same reference. Its type initialization may precede Diagnostics or occur on the cold path that first uses it; `BeforeFieldInit` prevents claiming an exact first-call allocation point. This is runtime-shared implementation allocation, not `new TimeProvider` per service. The clock box is likewise per loaded clock type, not per service. Its lifetime is the type/loader lifetime—process lifetime for a noncollectible load—not a universal promise for collectible loads. No `Unsafe.SizeOf` result or allocation-traffic delta is used as allocation proof.

## Ledger consequence and smallest next step

[Chapters 04](../design/04-capacity-and-workload-profiles.md#2-binding-defaults), [28](../design/28-diagnostics-performance-and-validation.md#1-observable-system) and [29](../design/29-project-setup-build-and-deployment.md#diagnostics-service-ownership-and-issuer) cover engine-owned controls/backing and construction peaks; they are not a process-RSS or universal CLR-loader ledger.

- The **Core-specific box is real shared engine control storage**. Runtime allocation machinery does not make it BCL-owned infrastructure. It needs one actual accounting owner covering its shared lifetime, not a duplicate physical charge to every World or service.
- The two static cells are fixed type/static-base data, not additional managed payload objects. Do not invent object headers or per-World diagnostic payload debits for them. Any profile accounting for fixed static/control infrastructure must identify its common owner explicitly.
- The independently BCL-rooted provider is not a World payload or service-owned instance. Separate any applicable one-time BCL cold-start construction accounting from Diagnostics ownership; neither charge it per service nor silently claim all runtime first-use activity is free.

`DiagnosticsService` initializes the clock during construction. Its 256-byte control term and `Close` do not identify a persistent clock reservation or transfer one after the last service closes. Constructing a service, closing it, then constructing another reuses the clock origin/state and provider; spare credit held only during a live service does not cover the intervening lifetime.

**P2 remains open, not disproved.** The smallest next evidence step is to identify an existing process/control reservation and trace its claim before clock initialization and its coverage beyond the last service close. If no such route can be demonstrated, return the private-storage choice to the owner rather than assigning spare service credit by inference. Any later storage change must preserve [Chapter 03's single captured origin and nondecreasing shared clock](../design/03-shared-conventions-and-clocks.md#2-four-clocks), not reset the clock per service.

Other P2 obligations are untouched. This addendum selects no allowance, API, acceptance ID or numeric eligibility and makes no native/AOT claim. The frozen proposal, production sources, tests, Roadmap and design chapters are unchanged by this review.
