# M05 Diagnostics extended component evidence

## 1. Disposition and exact scope

**Component-evidence follow-up only, not retained-root, byte-budget or M05 qualification.** Commit **`6aaeda7fc9f5d4feb1ae6acc17232ead687b6142`** supplies new observations for the [previous admission-sequence proposal](m05-diagnostics-component-evidence-and-admission-sequence.md). It supersedes only that proposal's missing-component observations; its frozen bytes remain untouched. The [retained-byte proposal](m05-skinned-normal-diagnostic-retained-byte-admission.md) still owns the conditional charging/ownership design. Neither proposal's remaining prerequisites nor the maintained owner contracts are amended here.

The two extended TRX files listed in §5 each report **2 selected/discovered, 2 executed, 2 passed, zero failed/skipped/not-executed**: Debug, then Release, runtime **`.NET 11.0.0-rc.3.26522.148/X64`**, NUnit Adapter 5.2.0.0, `net11.0`. Exact membership under `TerraFX.Core.Tests.DiagnosticsServiceTests`:

- `MeasuresBoundedWarningRecordAllocationTraffic`
- `MeasuresDiagnosticRootAndContextCopyAllocationTraffic`

These are two framework measurement tests per configuration, not the Core suite or admission/conformance cases. The new helper logs measurements without asserting allocation ceilings. Diagnostic/string values are warm 1024-iteration averages with preconstructed inputs and only the last constructed object explicitly kept alive. Service construction measures one fresh instance at each capacity after warmup/collection. No full retained-root census, cold-path bound, NativeAOT or Renderer execution is established; TRX does not attest a binary/source digest.

## 2. Component terms now supported

Let **P** mean the frozen current construction paths on the logged managed x64 runtime/configurations, not a portable allocation profile.

| Term / observation | Exact-profile evidence and boundary |
|---|---|
| `D_P = 216 B` | Observed Diagnostic construction with empty context and preconstructed strings. Supports the Diagnostic-root allocation component, excluding separately referenced strings/context. |
| `s_context,P = 16 B`; `Q_P(7,16) = 136 B` | The managed struct stride is directly logged by `Unsafe.SizeOf<DiagnosticContextEntry>()`. Seven-entry construction is 352 B versus 216 B empty, hence a 136-B copied-context component. Its 112-B entry payload leaves a 24-B residual in this shape; that is not a universal array-header/alignment law. |
| Empty service: capacity 1 **144 B**, capacity 8192 **393,312 B** | Unlike the earlier getter-only experiment, the new helper actually constructs both empty services/backing arrays. It does not populate 8192 retained diagnostics or isolate each control object's extent. |
| `E_P = 48 B` for the current entry representation | Independent construction difference: `393312 - 144 = 393168`; `393168 / 8191 = 48`, remainder **0**. This corroborates the getter's runtime stride. Recheck after adding/changing private controls or ring entries. |
| Source reservation remains **393,536 B** at 8192 slots | Still accounting arithmetic, distinct from the 393,312-B construction observation. Their 224-B difference is not proven workspace/headroom or permission to reduce reservations. |

These observations support recording the named `D_P`, seven-entry `Q_P` and the fixed-length `T_P` values below as **measured construction/allocation components**, rather than continuing to label them unmeasured. They do not alone certify the complete retained graph, its simultaneous ownership roots or a production upper-bound profile.

The thirteen measured string-object extents, in bytes:

```text
length:   6  10  14  15  17  18  19  20  21  23  32   65  110
T_P:     40  48  56  56  56  64  64  64  64  72  88  152  248
```

They support the declared M05 edge-length worksheet, not a size law for arbitrary strings, large objects, formatting temporaries, other runtimes or AOT.

## 3. Independently derived 18-edge charge

Use the **unchanged declared length operands** in the retained-byte proposal §3, including a 19-digit occurrence reservation from first admission. Looking up each operand in the measured table:

| Edges | Declared lengths | Measured byte charges in the same order | Sum |
|---|---|---|---:|
| Seven context keys | `10,6,18,14,17,21,15` | `48,40,64,56,56,64,56` | **384 B** |
| Seven context values | `32,32,19,19,6,6,19` | `88,88,64,64,40,40,64` | **448 B** |
| Code, Operation, Message, CommandIdentity | `23,20,65,110` | `72,64,152,248` | **536 B** |

There are **18 edges**, totaling the prior worksheet's **452 UTF-16 units**. The independent string-charge sum is **`384 + 448 + 536 = 1368 B`**. Thus:

```text
observed-component subtotal_P = D_P + Q_P(7,16) + edge-string charges
                             = 216 + 136 + 1368 = 1720 B
candidate retained reservation_P = 1720 B + A_P    (A_P still unresolved)
```

This is a conservative **per-edge worksheet**, not 18 necessarily distinct string objects, an observed formatted-record allocation delta, or a certified complete retained size. Aliases/literals/cached values can share backing; no sharing credit is taken. It excludes ring/service backing, private retention/attribution controls, transition workspace and producer publication work. The older 2384-B context/string illustration used a different unqualified conservative rule; these measurements do not retroactively qualify that rule or change a budget.

The other selected test still reports producer traffic **1088 B**, first Warning `Write` **488 B**, combined **1576 B**. Those measurements have different lifetimes/sharing/occurrence width and must not replace the 1720-B worksheet, `A_P` or `W_P`. Do not divide 8 MiB by 1720 to derive capacity or infer a one-record fit at an exact lower World limit.

## 4. Remaining blockers and next action

| Still unresolved | Required closure, unchanged in substance |
|---|---|
| **`A_P`** | Specify and measure actual private retention/account controls, funded metadata roots and transfers. Absence of those controls in the experiment is not `A_P=0`. |
| **`W_P` and full retained-root certification** | Enumerate and fund append/merge intermediate backing, old/new immutable coexistence, polling copies, failure cleanup and actual Close/last-use release. Ring/latest-fault aliases and independently retained engine results require explicit ownership; caller DiagnosticSnapshots retain data, not service/account grants. Neither a component sum nor `488 B` measures this peak. |
| **Mandatory fault/saturation ownership** | Close bounded retained Critical and `DiagnosticCounterSaturated` producer shapes, independent caller/World failure results, nonrecursive publication and old/new/replenishment reservations. An unbounded Diagnostic cannot be promised inside a finite ceiling; silently dropping/truncating mandatory faults is not a rectification. |
| **Generic strings/context** | Qualify length/capacity-derived charging and checked refusal before service-owned allocation/retention. The thirteen fixed lengths and one seven-entry copy do not bound arbitrary valid Diagnostic payloads. |
| **Operational World account** | Close the non-emitting Core account's actual issuance, atomic cross-service claims, metadata funding and lifetime through World replacement/close and retained publication/results. A WorldGuid or existing capability is not that account. |
| **Real Renderer publication** | Still requires the reviewed service injection/key/account handoff, whole-readback validation, streaming formatting, joined publication/stop lifetime and complete proposed 16-MiB completion peak. These Core tests establish none of those paths, the separate 32-MiB ledger, or native/public-consumer behavior. |

**Next:** carry the now-measured components into the previous proposal's step 2; concentrate new work on concrete control/root layouts, `A_P`/`W_P` and mandatory producer contracts rather than repeating unchanged component probes. Retry the all-record preclaim/refund implementation once those exact inputs define a funded simultaneous peak under the fixed host ceiling and applicable exact World limits. Retry Renderer publication only after its additional owner/producer/completion closure. The central barrier must cover all severities/owners, not an M05-only exception.

Chapters 04/28/30 still require host/World byte limits, independent critical failure retention, visible ordinary loss and caller-owned snapshots. Chapter 35 and the project roadmap keep dependency-ready M05/M03-B work distinct from these blocked observations; unrelated bounded work can proceed without borrowing unqualified diagnostic credits. Native/AOT, whole-ledger and milestone acceptance remain open.

## 5. Fresh direct-input freeze

Freeze **2026-11-18T02:38:46.3965259Z**; HEAD **`6aaeda7fc9f5d4feb1ae6acc17232ead687b6142`**; branch `tannergooding-m05-implementation-plan`. This follow-up plus the **15 full-file hashes** below defines the review input. Core source hashes are unchanged from the prior freeze; the committed measurement test and new TRX files are fresh inputs. Previous proposal manifests remain historical, not claims about the new test bytes.

C = tracked/clean; M = pre-existing dirty authority; I = existing ignored TRX. Manifest SHA-256: **`7FC1BA7A80C50FF543D573A0CB63EDD24E53200491F96ECF9151D720A3C4E8B2`**. Canonicalization: ordinal Windows-path order; `State<TAB>Path<TAB>UPPERCASE-SHA256<LF>`, UTF-8 without BOM. File digests cover raw bytes, including dirty Chapter 04/30 content. No schema/fixture/binary input is used; unrelated dirty work is outside this review. Reverify every listed hash before disposition.

| Direct input | State | SHA-256 |
|---|---|---|
| `artifacts\tst\Debug\m05-diagnostic-components-extended-debug.trx` | I | `1F9CAEBEB5D32BD6BE95594E17044D3B8854FD453EAAF42E0885FE16B0F5E718` |
| `artifacts\tst\Release\m05-diagnostic-components-extended-release.trx` | I | `7254F2033C8E6A3DECB649AC136A9689F3F609879F06165E36E5CE53486568E5` |
| `docs\design\04-capacity-and-workload-profiles.md` | M | `A4D86F3FA373A64548435C53582BF5916E29D77F34650F9D161CCEC90E6F0245` |
| `docs\design\28-diagnostics-performance-and-validation.md` | C | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `docs\project\Roadmap.md` | C | `1C8C3F8E140F47492255534284ABBF67C5776210DC7A68F2C01155D0BE773F46` |
| `docs\proposals\m05-diagnostics-component-evidence-and-admission-sequence.md` | C | `7286D7AE704A0CF53C7B01E5021F5986B6D37E18E2760B14FF8E7831CC2C613B` |
| `docs\proposals\m05-skinned-normal-diagnostic-retained-byte-admission.md` | C | `A5297E203DB144D38DD1F4F47F1EFCE1601F785DABB3E3A18585EDC117F5DB96` |
| `sources\TerraFX.Core\Diagnostic.cs` | C | `B827997B35EE8ACCCCFA069905865C1E155508401FE7256EACBD0B20E2A6B840` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | C | `C2A1E74E0AE9929638C9255CFAA8DD7235CE37509483660B740569B775BEF530` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | C | `916F5FB2AD709C4F0EF12FC83FF8E997619F1C1B9F6B52234655E91D45EFC242` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | C | `6B2606EDC15849EF83CABBE4E561F2ABCB51954505F363785585D447823C705A` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | C | `77D3CBC729325420643EAF196C7B30AB6EBD178A883CFB417CC64E7E41644A5E` |
| `tests\TerraFX.Core.Tests\DiagnosticsServiceTests.cs` | C | `CA8EB8043E43B545E67710EAB290972179039CEA1E598516A904B44FC62108FA` |

Only this follow-up is added. No source, tests, maintained contracts or prior proposal is edited; the supplied TRX is read, not regenerated. No build/test, commit or push is performed.
