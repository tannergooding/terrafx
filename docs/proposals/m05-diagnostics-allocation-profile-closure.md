# M05 Diagnostics allocation-profile closure

## 1. Recommendation and status

**Tentatively approved as implementation direction per user instruction; pending owner review. C1-C3 are closed at `4069a0a7`. No numeric eligibility is selected or qualified; complete protected/owner funding remains open.**

Keep the current immutable public representation initially. The smallest behavior-preserving closure is to replace ambient CLR-layout assumptions with one closed, owner-reviewed allocation profile for an identified runtime/build/target, selected before diagnostics-owned backing allocation. Until that profile has a complete conservative derivation, the corresponding service construction/admission must remain unsupported. Moving today's constants into a profile-shaped type does not establish such a derivation.

The measured repository-local Windows x64 RC3 runtime, prior-session preserved images, and current source-matching emitted images are identified. Sections 4.3-4.4 derive fixed layouts and direct owned-heap construction bounds from the matched runtime algorithm and metadata, not traffic. Section 4.5 records the closed admission/root corrections and remaining P1/P2 funding obligations. A size table or passing managed regression selection alone cannot qualify the service.

The inspected supported APIs do **not** establish a portable full managed-object-graph bound before allocation. The matched implementation does permit the fixed-layout derivation below. With C1-C3 corrected, the smallest next closure is protected/control/producer funding and the complete reviewed profile, not replacement of every Diagnostic with an arena. A bounded representation redesign remains necessary only for storage/construction that cannot be bounded under an accepted implementation profile, or if implementation-specific sizing is rejected. Increasing constants or measuring more traffic does not establish those missing obligations.

This proposal changes no owner contract, public API, quota, diagnostic identity, or milestone gate. Tentative approval permits work on the closure mechanism, not permission to mark an unproved profile eligible.

## 2. Contract and implementation evidence

The [retained-payload addendum](m05-diagnostics-retained-payload-accounting-addendum.md), section 3, explicitly requires owner-reviewed conservative profile bounds and rejection of unknown profiles. The current [Chapter 04, section 2](../design/04-capacity-and-workload-profiles.md#2-binding-defaults), [Chapter 28, section 1](../design/28-diagnostics-performance-and-validation.md#1-observable-system), and [Chapter 29 diagnostics ownership](../design/29-project-setup-build-and-deployment.md#diagnostics-service-ownership-and-issuer) already cover complete payloads, construction/replacement coexistence, and exact cross-service World attribution. The addendum's older statement that these clarifications are still needed is not authority to ignore the current chapters.

The fixed host-service ceiling remains **8,388,608 bytes**. The exact configured `MaximumDiagnosticBytes` remains an attribution sublimit within existing World funding, not additional physical storage. Service/control/ring backing, retained graphs, protected fault/counter roots, polling construction, and simultaneous replacement work must fit their existing owners. Ordinary loss remains observable; critical state and operation results are not expendable telemetry. Copied caller observations do not prolong service/account debits.

Source observations from the frozen review inputs below:

| Input | Observation and consequence |
|---|---|
| `DiagnosticsService` constants | `DiagnosticObjectBytes = 64 + ((176 + (5 * 8) + 15) & ~15)`, `DiagnosticRetentionControlBytes = 128`, and `ServiceControlBytes = 256` are unconditional candidate reservations. Their apparent slack is not an upper-bound proof. This proposal neither certifies an undercount on a particular runtime nor approves these numbers. |
| `ArrayCharge`, `ObjectCharge`, `StringCharge` | Arrays/objects use a fixed 64-byte allowance and 16-byte rounding. Strings reuse that formula for `length + 1` characters. Section 4.2 gives a conditional source comparison for ordinary SZ-array/string allocations on the identified lane. The helpers remain unconditional, and complete control-object layouts, capacities, construction multiplicities and peaks are unproved. |
| `DiagnosticsService` constructor and reservation helpers | Capacity selection, initial backing reservation, and `_records` allocation use these formulas without a runtime/profile eligibility check. Checked arithmetic protects arithmetic, not the validity of the size model. |
| `WorldDiagnosticAttributionAccount` and `DiagnosticAttributionReservation` | The account subtracts an unconditional `ControlByteCharge = 128`; atomic reservation/transfer/return cannot establish that the charged bytes cover the owned backing. The reservation is a `ref struct`, not another heap object to charge merely because it has a type name. |
| `Diagnostic`, `DiagnosticContextEntry`, retention controls | The graph contains strings, immutable context backing, and private retention/account controls. Copying context entries does not copy their strings. A reference-size measurement cannot account for this graph. Control ownership must also cover the capability and the service's embedded gate without charging embedded values as separate objects. |
| `CalculateWarningPayloadByteCharge` | Sizing uses `GetPositiveDecimalDigitCount` and `StringCharge(int)`, without formatting the count. `AppendOccurrenceCount` / `UpdateOccurrenceCount` format only after the applicable reservation checks. This closes the earlier formatting-during-sizing finding; it does not prove the object/array/string charge model. |
| Current `WriteNoncoalescingWarning` / `ValidateSkinnedNormalDiagnostic` | Before entering preclaimed `WriteCore`, this path now requires the exact `DegenerateSkinnedNormal` code, operation/message literals and six ordered context entries. It validates lowercase nonempty Guid identities, canonical positive long values, positive vertex counts at most 262144 with degenerate count no greater than tested count, and matching submission/snapshot/draw command identity. This closes a producer-shape boundary, **not** the allocation-size profile or its byte funding. |
| `GetSnapshot` and `DiagnosticSnapshot.CreateOwned` | The in-progress path transfers builder backing through `MoveToImmutable` and bypasses the snapshot constructor's explicit second copy. Qualify this actual construction path, not the older double-copy observation in the addendum. Builder, backing, snapshot, and any remaining temporary storage still need bounds. |

These are source observations, not executed accounting or allocation observations. No TerraFX.Runtime implementation or test source was reviewed for this proposal.

## 3. Public guarantees are not heap-layout guarantees

Public API documentation was checked on 2026-11-19:

| Supported API/fact | What it establishes | What it does not establish |
|---|---|---|
| [`Unsafe.SizeOf<T>()`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.unsafe.sizeof?view=net-11.0) | The managed value size; for a reference type, the reference size itself. Useful for the existing value-struct payload calculations. | A `Diagnostic` object's size, recursively referenced strings, array headers, or builder/control object sizes. |
| [`IntPtr.Size`](https://learn.microsoft.com/en-us/dotnet/api/system.intptr.size?view=net-11.0), string lengths, and `sizeof(char)` | Pointer width and logical character payload quantities. | Managed object headers, minimum allocation sizes, trailing allocation padding, or a portable string-allocation formula. |
| [`Marshal.SizeOf`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.marshal.sizeof?view=net-11.0) | The unmanaged representation's size where supported. | The managed retained graph; marshaling layout is not a replacement for GC layout accounting. |
| [`GC.GetAllocatedBytesForCurrentThread`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-11.0) | Managed allocation traffic during a thread's lifetime, including objects that did not survive. | Root ownership, pre-existing referenced objects, native allocations, or an upper-bound formula for all legal shapes. |
| [`GC.GetTotalMemory`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalmemory?view=net-11.0) | A heap-wide quantity excluding fragmentation. | A per-service/per-World graph size or a preconstruction admission bound. Collection does not make aggregate heap differences an ownership proof. |

**Distinguishing example:** construct a valid Diagnostic whose existing Message has a UTF-16 payload alone larger than the service ceiling, before starting an allocation counter interval or on another thread. `Write` can retain that Message by reference without allocating it during the interval. Its traffic delta therefore cannot establish a safe retention claim. Conversely, coalescing can allocate temporary context backing that becomes garbage; summing traffic forever is not the current retained balance.

An isolated traffic observation can corroborate that particular construction path, but cannot supply the missing general bound. Heap inspection can corroborate retained objects, but a postallocation observation is not permission to allocate first and charge afterward. Neither sampled maxima nor a safety multiplier proves a maximum over unobserved lengths, layouts, and construction paths.

The ledger bounds admitted **engine ownership and its construction peaks**, not caller retention, unreachable garbage awaiting GC, fragmentation, or process RSS. A caller keeping an old immutable snapshot after service release is consistent with that boundary; an engine-owned result/fault retaining the same graph still needs its own funded ownership.

## 4. Smallest closure sequence

### 4.1 Add eligibility before spending

Use one private, closed selection point for the existing charge helpers, not a public/pluggable sizing provider. Unknown or mismatched profiles must fail the existing construction/admission path before allocating diagnostics service/account backing or service-only payloads. Do not emit a new diagnostic through the service whose construction was refused, silently disable mandatory diagnostics, create a zero-cost service, or treat refusal as successful host/World startup.

Eligibility must identify the runtime implementation and relevant exact artifacts/configuration, not just pointer width, RID, `.NET 11`, or a framework-description prefix. A self-contained NativeAOT image needs its own build-bound proof; a JIT runtime's profile cannot certify it. A changed Core layout or compiler/BCL construction path also invalidates the corresponding proof.

[Chapter 29, section 2](../design/29-project-setup-build-and-deployment.md#2-sdk-selection) separates ordinary compatible SDK selection from exact qualification identities. The inspected `global.json` selects SDK `11.0.100-rc.3.26522.148` with `rollForward = latestFeature`; that is not an exact runtime deployment pin or a numeric size qualification. Chapter 29's older concrete SDK examples do not qualify this selected build either. Do not change developer SDK policy or duplicate its pin to manufacture profile eligibility.

### 4.2 Close one implementation-specific proof

#### Identified measured lane

The measured lane is **Windows x64, CoreCLR/JIT, runtime `11.0.0-rc.3.26522.148`, commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`**, not NativeAOT. Prior session provenance identifies the testhost runtime as the checkout's `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148`, matching the reported `RuntimeInformation.FrameworkDescription` in both configurations. Read-only inspection independently matched its four reported file hashes and the local compiler hash; section 6 is the canonical SHA-256 freeze.

| Artifact | Identity / scope |
|---|---|
| `System.Private.CoreLib.dll` | FileVersion `11.0.26.52348`; ProductVersion `11.0.0-rc.3.26522.148+5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. |
| `coreclr.dll`, `clrjit.dll` | Version metadata `11,0,26,52348`, commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. |
| `System.Collections.Immutable.dll` | FileVersion `11.0.26.52348`; ProductVersion identifies the same RC3 revision. CoreLib alone does not identify this builder/copy implementation. |
| SDK `11.0.100-rc.3.26522.148` `csc.dll` | Checkout-local `Roslyn\bincore\csc.dll`; a compiler-driver identity, not a substitute for the emitted-image inspection below. |

The separate globally installed RTM 11.0.0 runtime is **not the measured lane** and is excluded from this profile's artifact freeze. The runtime-provenance gap is resolved by the supplied testhost identity and verified local hashes, not by relabelling traffic or transferring RTM evidence. The source expressions below were rechecked at the RC3 commit.

#### Preserved Core emission inputs

Prior session preserved images use the neutral locators `session artifact: files\diagnostics-profile\TerraFX.Core.Debug.net11.0.dll` and `session artifact: files\diagnostics-profile\TerraFX.Core.Release.net11.0.dll`. Their exact file hashes are frozen in section 6.

| Preserved image | MVID | Embedded PDB check |
|---|---|---|
| `TerraFX.Core.Debug.net11.0.dll` | `57e2cbd8-ebb4-44f5-b84d-7268c225ce70` | Eight baseline document checksums inspected; seven match the current source freeze. `DiagnosticsService.cs` is superseded as explained below. |
| `TerraFX.Core.Release.net11.0.dll` | `0f4b3ef8-182f-49b4-bfa5-3f5c0335011c` | The same seven current-source matches and superseded service checksum. |

Both PEs contain an embedded portable PDB. The inspected documents are `Diagnostic.cs`, `DiagnosticContextEntry.cs`, `DiagnosticSnapshot.cs`, `DiagnosticsService.cs`, `DiagnosticAttributionReservation.cs`, `WorldDiagnosticAttributionAccount.cs`, `WorldDiagnosticsCapability.cs`, and `ReentrantGate.cs`; their PDB hash algorithm is SHA-256. Prior session records identify these as builds from clean Core sources at commit `231476d882e9746cd819cb3acb8c0c9990949047`. Both PDBs contain the earlier arithmetic-sizing service checksum `b4d169851298c4e02e2bd8ae53358dfb58b657ee26495677c8c5a4582910cf86`. **That checksum is invalidated as a current `DiagnosticsService.cs` review input**; it remains only an embedded-PDB fact identifying the older preserved images. The other seven source checksums still match section 6. This is a scoped checksum check, not an audit of every PDB document or all build inputs.

The current committed `DiagnosticsService.cs` at **`4069a0a706feabf4e66a3e6affafde503160b836`** was re-read and hashed from the same byte snapshot: **`7445732ebf740791290f32302ee30be5689cf28ec996f92502958b3ac44696d0`**. It retains the closed `ValidateSkinnedNormalDiagnostic` route and now includes the C1-C3 corrections in section 4.5. The closed format includes six ordered keys (`SnapshotId`, `DrawId`, `SubmissionSequence`, `CompletedFence`, `TestedVertexCount`, `DegenerateVertexCount`), canonical positive values and the matching command identity. This source supersedes every earlier current-file hash in the review freeze. Prior-session preserved images remain older emission evidence; the current images below match the corrected source. Neither producer-shape validation nor C1-C3 closure qualifies the allocation-size profile.

Static inspection did not load or execute either Core image. It compared 19 construction-relevant Core method bodies per configuration: the `Diagnostic` constructor; both `DiagnosticSnapshot` constructors and `CreateOwned`; all three `DiagnosticsService` constructors, `AppendOccurrenceCount`, `CopyDiagnostic`, `UpdateOccurrenceCount`, `GetSnapshot`, both capability factories, `WriteSubscriptionCapacityWarning`, and `WriteCore`; the account constructor; the capability constructor and subscription-warning forwarder; and the retention-node constructor. All 19 Debug/Release pairs have the same resolved call/new-object/new-array/boxing/stack-allocation instruction sequences. Their full IL, branches, code generation, lifetimes and allocated bytes are not asserted identical.

Additional ordinary developer outputs matching the committed service source were inspected read-only:

| Current emission input | MVID | Source/validation evidence |
|---|---|---|
| `artifacts\bin\sources\TerraFX.Core\Debug\net11.0\TerraFX.Core.dll` | `32c67849-2ac0-40c3-8eeb-26c2ebaf4778` | All 11 in-scope Core documents match section 6, including corrected `DiagnosticsService.cs` and the clock/query/severity leaves. |
| `artifacts\bin\sources\TerraFX.Core\Release\net11.0\TerraFX.Core.dll` | `8b8b84a3-44a4-4cdf-9c0e-44981ebd5117` | The same 11 source matches. |

Both current images contain the validator, its four static local-function methods, and `WriteNoncoalescingWarning`; their resolved call/allocation sequences agree between configurations. The validator's only `newobj` sites construct `ArgumentException` on rejecting branches. The four static helpers contain no allocation opcodes, boxing, closures or delegates. The entry method's successful `newobj` operations construct nullable **values**, not heap objects. The accepted path uses string indexing, spans, equality and `TryParse`; no input-dependent managed allocation is introduced by this validation. This is emitted valid-path evidence, not a claim about all runtime-global first-use activity. These are mutable developer-output locations: their section 6 hashes must still match at disposition.

At `4069a0a7`, emitted ordering was also checked for `FindRetentionIndex`, Close, `ReleaseRetention`, `WriteCore`, `WriteSubscriptionCapacityWarning`, and `ReleasePayload`: together with the six validation/entry bodies, 12 selected Debug/Release event sequences agree. Both coalescers call the live-index finder after room-making and before the commit element byref; Close initializes each entry to default before its release call; `ReleaseRetention` calls `ReleasePayload` before account refund. This corroborates the inspected source transitions without executing them.

#### Verified construction lowering

The following describes valid, successful paths in those images, not a complete byte/peak proof. Let `n` be incoming producer context length, `c` an existing aggregate's context length including its occurrence entry, and `r` returned snapshot count.

| Path | Emitted construction and backing |
|---|---|
| `Diagnostic` constructor | `Context` uses `ImmutableArray.AsSpan` → `ReadOnlySpan.ToArray` → `ImmutableCollectionsMarshal.AsImmutableArray`. Nonempty context therefore has one copied array of exactly its input length; strings remain shared references. The matched RC3 CoreLib `ToArray` IL returns `Array.Empty<T>()` for empty input and uses `new T[Length]` otherwise. |
| `AppendOccurrenceCount` / `CopyDiagnostic` | Append directly allocates a context array of `n + 1`, copies the existing entries, formats the occurrence count, wraps the array without another copy, and calls `CopyDiagnostic`. That creates a new Diagnostic whose constructor makes the final context copy of `n + 1`. There is no Builder object in append's own lowering. |
| `UpdateOccurrenceCount` | Creates `Builder<DiagnosticContextEntry>(c)`, adds exactly `c` entries, transfers its backing with `MoveToImmutable`, and calls `CopyDiagnostic`, which copies that context into the replacement Diagnostic. This has a builder and two context arrays of capacity `c`; exact filling avoids the builder's growth branch. The old aggregate remains separately live during replacement. |
| `GetSnapshot` / snapshot constructors | Creates a `Builder<Diagnostic>(r)`, adds exactly `r` references and transfers the backing to `CreateOwned`. `CreateOwned` passes literal `false` to the private snapshot constructor, bypassing its copy branch. This path has a builder, one reference array of capacity `r`, and a snapshot object; it does not copy Diagnostic graphs. The other constructor's copy branch still exists and is not an alias for this path. |
| First subscription-capacity warning | Direct `new DiagnosticContextEntry[2]` → source Diagnostic constructor copy `[2]` → append array `[3]` → retained Diagnostic constructor copy `[3]`, plus two Diagnostic objects and a retention node. These four array capacities follow from IL and the matched `ToArray` implementation, not from traffic differences. The coalescing branch instead uses `UpdateOccurrenceCount`. |
| `WriteCore` with a preconstructed Information Diagnostic | The normal retained-admission path keeps the existing Diagnostic and constructs a retention node; it does not enter Warning occurrence append/copy. A Warning uses append or replacement as above. These paths must not be conflated when interpreting allocation traffic. |
| Service constructors and control issuance | The full service constructor allocates `DiagnosticEntry[maximumRecords]`; the other two delegate to it. Each capability factory constructs a capability whose constructor creates an account. Account and retention-node constructors have no nested normal-path graph construction. This identifies controls that still need complete instance-size bounds and funded owners. |

The RC3 `ImmutableCollectionsMarshal.AsImmutableArray` IL constructs the **value-type** ImmutableArray wrapper, not another heap object or array. Likewise `newobj` for `Span`, `Nullable`, `DiagnosticContextEntry`, or `DiagnosticEntry` does not by itself establish a heap allocation. The matched [`ReadOnlySpan.ToArray`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/ReadOnlySpan.cs#L408) and [`Builder` implementation](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Collections.Immutable/src/System/Collections/Immutable/ImmutableArray_1.Builder.cs) support the array-copy/transfer statements; `EnsureCapacity` grows only when required capacity exceeds backing length.

This closes the selected collection-expression and valid-validator lowering questions. Fixed sizes are derived separately below; complete ownership/protected construction is not inferred from these allocation sites.

#### Source-derived facts, not an accepted profile

The following were rechecked at RC3 commit `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`; they are implementation facts, not public .NET layout guarantees or replacement reservation constants:

| Matched source | Derivation and limit |
|---|---|
| [`object.h`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.h), `OBJHEADER_SIZE`, `OBJECT_SIZE`, `ARRAYBASE_BASESIZE`, `MIN_OBJECT_SIZE` | For this Windows x64 target: object header 8 bytes, MethodTable pointer 8 bytes, `OBJECT_BASESIZE = 16`, SZ-array base including its header/length/padding 24 bytes, and minimum object size 24 bytes. A fixed class's instance fields are **additional**; these facts do not size `Diagnostic` or its controls. |
| [`vm/amd64/cgencpu.h`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/amd64/cgencpu.h#L26), [`gcenv.base.h`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/gc/env/gcenv.base.h#L423), [`gcpriv.h`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/gc/gcpriv.h#L1220) | VM data alignment is 8; GC data alignment is pointer-sized, with `Align` rounding up to 8 bytes on this lane, including the stated large-object alignment. This is object-allocation alignment, not a process/segment-memory bound. |
| [`array.cpp`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/array.cpp#L400), [`AllocateSzArray`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/gchelpers.cpp#L635) | The SZ-array MethodTable uses the 24-byte base; ordinary allocation requests base plus element count times runtime component stride. Multidimensional arrays have extra bounds and are not covered by this SZ-array expression. |
| [`StringObject::GetBaseSize/GetSize`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/object.inl#L49), [`AllocateString`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/gchelpers.cpp#L1097) | The string size expression is `16 + 4 + 2 + 2 * length`, including the terminator, and the ordinary allocator applies `PtrAlign`. Thus its aligned managed-object allocation expression is `RoundUp8(22 + 2 * length)` on this lane. |

For nonnegative, representable `P = capacity * actualComponentStride`, the ordinary SZ-array expression is `RoundUp8(24 + P)`. The current helper's `64 + RoundUp16(P)` dominates it: `24 + RoundUp8(P) <= 64 + RoundUp16(P)`. Likewise, the ordinary string expression above is bounded by the current `64 + RoundUp16(2 * (length + 1))`. These comparisons follow from the source expressions and checked nonnegative arithmetic, **not** from measured allocation traffic.

These comparisons establish the stated per-allocation array/string bounds. Fixed-class comparisons follow in section 4.3. Neither step establishes the operational and ownership invariants required for full-profile eligibility.

The matched RC3 Builder source and assembly metadata agree on its two instance fields, `_elements` and `_count`; their placement is included in section 4.3.

#### Reported allocation-traffic corroboration

Prior session reported `MeasuresDiagnosticRootAndContextCopyAllocationTraffic`, **Debug 1/1 and Release 1/1 selected/executed**, on **`.NET 11.0.0-rc.3.26522.148/X64`**:

| Observation | Reported value in both configurations |
|---|---|
| Diagnostic construction traffic | Empty context: 216 bytes; seven-entry context: 352 bytes; delta: 136 bytes. |
| `DiagnosticContextEntry` managed stride | 16 bytes. This value-stride observation does not include array headers or referenced strings. |
| Service construction traffic | Capacity 1: 184 bytes; capacity 8192: 393,352 bytes; 48 bytes per additional ring entry inferred from the traffic difference, **not** an independently established ring stride/layout bound. |
| Preconstructed Information admission traffic | Corrected measurement: average 48 bytes per retained admission over eight admissions, without Warning occurrence append/copy. The eight admissions are operations within the measurement, not eight framework test cases. |
| World attribution-account construction traffic | 72 bytes. Not an independently derived account-object size or control reservation. |
| String construction traffic, length → bytes | `6→40`, `10→48`, `14→56`, `15→56`, `17→56`, `18→64`, `19→64`, `20→64`, `21→64`, `23→72`, `32→88`, `65→152`, `110→248`. |

These are finite **allocation-traffic observations**, not retained-size or construction-peak proof. They were not used to obtain section 4.3's separate placement results, even where numbers agree. Compound Warning-path traffic is omitted rather than labelled retention-only. No TRX/log was inspected here; the nonzero counts, observations and testhost binding are attributed to prior session handoff summaries, with local artifact hashes independently checked.

The reported RC3 description now agrees with the identified measured runtime. Resolving that provenance does not turn traffic into layout proof or establish that a finite sample bounds every shape, replacement peak or cold path.

At `4069a0a706feabf4e66a3e6affafde503160b836`, focused `DiagnosticsServiceTests` are reported **22/22 in Debug and 22/22 in Release**, and the targeted Runtime host/World-subscription selection is reported **5/5 in each configuration**. These are reported managed regression results, not a fixed-control layout derivation, qualified allocation profile, or native/AOT/public-consumer acceptance. They are distinct from the prior-session allocation measurement's 1/1 selection and eight-admission average; those counts are not added together. No result files were inspected or tests rerun here.

### 4.3 Reproducible fixed-layout derivation

These are source-derived shallow allocation extents for the frozen Windows x64 CoreCLR build, **not selected admission charges or owner acceptance**. They assume the recorded metadata without EnC/dynamic field changes and the normal allocator paths above. There are no explicit sizes, nontrivial instance bases, SIMD or Int128 fields in the controls listed below. The tables close the in-scope engine controls and their stored value leaves; they do not purport to enumerate all runtime/loader process-global singleton graphs.

The matched algorithm is decisive:

1. [`InitializeFieldDescs`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L4253) normalizes references to pointer-sized fields and enums to their underlying scalar; actual by-value fields are excluded from scalar size bins and keep their nested type's size.
2. [`PlaceInstanceFields`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L8307) routes Auto layout to `HandleAutoLayout`. A Sequential type containing GC fields is also routed to Auto; declaration order alone is therefore **not** the managed layout of `DiagnosticEntry`.
3. [`HandleAutoLayout`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L8491) starts after parent field bytes, places scalar size bins largest first, puts GC references first within the pointer-size bin, and assigns fields in metadata order within each group. It then appends embedded values in metadata order, aligning each to 8 if it contains GC pointers, otherwise to its nested alignment. GC-containing values round their final size to 8.
4. Non-GC sequential leaves use [`InitializeSequentialFieldLayout`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/classlayoutinfo.cpp#L518): align each field, add its size, then round the value extent to the largest applicable alignment. These metadata layouts have no explicit size/packing reduction; this loader's [`DEFAULT_PACKING_SIZE=64`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/fieldmarshaler.h#L29) does not reduce their 1/4/8-byte member alignments.
5. [`SetupMethodTable2`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L11448) sets a class/boxed-value base to `RoundUp8(max(24, 16 + fieldBytes))`; [`ALLOC_ALIGN_CONSTANT`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/inc/stdmacros.h#L167) is pointer-size minus one. Unboxed values and array elements do **not** get another object header.

For the tables, value offsets start at the unboxed value. Class offsets start at the first instance-data byte, i.e. `object reference + 8`; the 8-byte negative object header and 8-byte MethodTable pointer are added only in the allocation column. `name@offset/size` includes every instance field; padding is implicit in gaps/end rounding.

| Value / embedded leaf | Complete layout | Size / alignment |
|---|---|---|
| `DiagnosticSeverity` | Underlying `int` at 0. | 4 / 4 |
| `Guid` | `_a@0/4`, `_b@4/2`, `_c@6/2`, `_d` through `_k` at 8 through 15, one byte each. | 16 / 4 |
| `Nullable<int>`, `Nullable<uint>` | `hasValue@0/1`, `value@4/4`. | 8 / 4 |
| `Nullable<long>` | `hasValue@0/1`, `value@8/8`. | 16 / 8 |
| `Nullable<Guid>` | `hasValue@0/1`, embedded Guid `value@4/16`. | 20 / 4 |
| Public `System.Threading.SpinLock` | Its only instance field is volatile `_owner@0/4`; not the unrelated nested runtime SpinLock type. | 4 / 4 |
| `ReentrantGate` | `_spinLock@0/4`, `_ownerThreadId@4/4`, `_recursionCount@8/4`. | 12 / 4 |
| `ImmutableArray<T>` | Its only field is `array@0/8`, an array reference. | 8 / 8 |
| `DiagnosticContextEntry` | `Key@0/8`, `Value@8/8`. | 16 / 8 |
| `DiagnosticEntry` | `Retention@0/8`, `OccurrenceCount@8/8`, `ExecutionSessionId@16/20`, `WorldOwnerId@36/8`; tail padding 44-47. | 48 / 8 |
| `DiagnosticAttributionReservation` | `_account@0/8`, `_diagnostics@8/8`, `_diagnosticsReservedBytes@16/8`, `_reservedBytes@24/8`. | 32 / 8; ref struct, no heap header |
| `ReentrantGate.Scope` | Managed byref `_gate@0/8`, `_threadId@8/4`, `_hasGate@12/1`. | 16 / 8; ref struct, no heap header |
| `Span<T>`, `ReadOnlySpan<T>` | Managed byref `_reference@0/8`, `_length@8/4`. | 16 / 8; ref struct, no heap header |
| `DiagnosticQuery` | `MaximumRecords@0/4`, `MinimumSeverity@4/4`, `MinimumTick@8/16`, `WorldGuid@24/20`. | 48 / 8; argument value |
| `ClockState` | `_provider@0/8`, `_frequency@8/8`, `_origin@16/8`. | 24 / 8 |

The Guid/Nullable/SpinLock metadata and implementation fields were checked in the frozen CoreLib; the Core field orders were checked in the recorded PEs. In particular, false nullable values still occupy their full storage. `Span<T>`/`ReadOnlySpan<T>` temporaries are byref/length value descriptors, not additional array or Builder objects.

| Heap control | Complete instance-data layout | Field extent → allocated extent |
|---|---|---|
| `Diagnostic` | `Code@0/8`, `CommandIdentity@8/8`, `Message@16/8`, `Operation@24/8`, `MonotonicTimestamp@32/8`, `Severity@40/4`, `AssetId@44/8`, `Context@56/8`, `DeviceEpoch@64/8`, `DeviceId@72/8`, `HostId@80/8`, `NativeResult@88/8`, `PassId@96/20`, `SurfaceEpoch@116/8`, `SurfaceId@124/8`, `SwapchainEpoch@132/8`, `SystemId@140/20`, `Tick@160/16`, `WorldGuid@176/20`. | 196 → **216** |
| `DiagnosticsService` | `_latestFault@0/8`, `_records@8/8`, `_coalescedCount@16/8`, `_droppedCount@24/8`, `_maximumBytes@32/8`, `_preparingPayloadBytes@40/8`, `_preparingSnapshotPayloadBytes@48/8`, `_protectedSnapshotBytes@56/8`, `_retainedPayloadBytes@64/8`, `_count@72/4`, `_head@76/4`, `_maximumRecords@80/4`, embedded `_gate@84/12`. | 96 → **112** |
| `DiagnosticRetention` | `_account@0/8`, `_record@8/8`, `_payloadBytes@16/8`, `ServiceReferenceCount@24/4`. | 28 → **48** |
| `WorldDiagnosticAttributionAccount` | `_maximumBytes@0/8`, `_reservedBytes@8/8`, `_worldOwnerId@16/8`, `_executionSessionId@24/16`, `_worldGuid@40/16`. | 56 → **72** |
| `WorldDiagnosticsCapability` | `_attributionAccount@0/8`, `_diagnostics@8/8`, `_worldOwnerId@16/8`, `_closed@24/4`, `_ownsDiagnosticsService@28/1`, `_executionSessionId@32/16`, `_worldGuid@48/16`. | 64 → **80** |
| `DiagnosticSnapshot` | `CoalescedCount@0/8`, `DroppedCount@8/8`, `Records@16/8`. | 24 → **40** |
| `ImmutableArray<T>.Builder` | `_elements@0/8`, `_count@8/4`. | 12 → **32** |
| `TimeProvider.SystemTimeProvider` | No instance fields in it or `TimeProvider`. | 0 → **24**, runtime-shared |

Core's static `ClockState s_state` is not another per-service inline field. A non-RVA static value is a boxed static in this runtime ([field classification](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/vm/methodtablebuilder.cpp#L4477)); its boxed extent is **40**, plus the static reference cell. `s_lastTimestamp` is an 8-byte static value. These have process/type lifetime, not a disposed service's lifetime. Loader/static-base bookkeeping and the runtime-shared provider are not made free by the 112-byte instance result; section 4.5 retains their ownership/funding closure.

For these shallow instances, the current 288-byte Diagnostic, 128-byte retention/account, and 256-byte service allowances exceed the derived 216/48/72/112 extents. `ObjectCharge(8)=128` exceeds Builder 32 and `ObjectCharge(4)=96` exceeds snapshot 40. These inequalities are useful; they do **not** prove that every separately owned control has a payer or that live/preparing balances are correct.

The corrected retention node's field names/mutability were checked in both current PEs. Its field extent remains 28 and allocation extent 48; clearing payload references does not shrink the node object. The stored charge is stable while owned and reset only at final release. Returned immutable Diagnostics/snapshots are not mutated by clearing the node.

### 4.4 Capacities and simultaneous owned-heap peaks

Use checked nonnegative arithmetic. Let `A_s(n)=RoundUp8(24+s*n)` for an ordinary SZ array with actual capacity `n` and component stride `s`, and `S(l)=RoundUp8(22+2*l)` for an ordinary string. The relevant strides are context 16, ring entry 48, and Diagnostic reference 8. `T` below sums `S(length)` for Code/Operation/Message, present CommandIdentity, and producer context keys/values, counting aliases conservatively per retained payload. The per-entry expressions include the record/node/context/strings, not another copy of the separately funded shared account, capability, service or clock. Ref-struct/value descriptors are not additional heap objects; these heap formulas are not a bound on JIT/native stack frames.

| Valid construction path | Conservative direct owned-heap expression |
|---|---|
| Service instance and ring, capacity `N` | `112 + A_48(N) = 136 + 48*N`, with `1 <= N <= 8192`; excludes separately owned process statics. Current backing charge is `320 + 48*N`. |
| Preconstructed non-Warning retained graph, context `n` | `R_info = 216 + 48 + A_16(n) + T`. Only the node is newly constructed in normal `WriteCore`, but retention must claim the whole graph. Empty backing may be shared; charging `A_16(0)` remains conservative. Producer/result construction needs its independent payer. |
| First Warning from preconstructed input, context `n`, count digit length `d` | `R_warn = 216 + 48 + A_16(n+1) + T + S(15) + S(d)`; additional append workspace `A_16(n+1)`. The formatted count string is already in `R_warn`. |
| Replacement of aggregate with context `c` including its count | Old retained graph **plus** complete new graph using `A_16(c)` and the new count string **plus** `32 + A_16(c)` for Builder/temporary backing. Old count text is not retained by the new graph. The current charge additionally counts a spare context entry/old count text and is conservative for this direct copy path. |
| Service-only first subscription warning | `R_warn(2) + 216 + 2*A_16(2) + A_16(3)`: two Diagnostics, one node, and context capacities 2, 2, 3, 3. Known shared input strings need no newly formatted copies. |
| Copied polling, returned count `r` | `32 + A_8(r) + 40 = 96 + 8*r`, `0 <= r <= min(N, query.MaximumRecords)`. No copied Diagnostic graph/node/account is created. Current protected construction charge is `288 + 8*N`, not a caller-retention allowance. |

The current append workspace `128 + 2*ArrayCharge(n+1)` dominates its one direct temporary array; update workspace `128 + 2*ArrayCharge(c)` dominates Builder plus its temporary array. Combined with the shallow/string/array inequalities above, these close the **direct** copy/append/replacement/polling heap factors, provided the old graph remains charged, the right target survives eviction, and credits are returned only after ownership ends. Those are substantive preconditions, not assumptions that can be skipped.

Positive default-format `Int64.ToString` follows [`FormatInt64`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Number.Formatting.cs#L1355) → `UInt64ToDecStr`: one result string of 1-19 characters, or a cached result; no per-call heap formatting Builder is used. The runtime's lazily populated 300-element small-number cache has a **2424-byte reference array** ([source](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Number.Formatting.cs#L266)). That is runtime-global state, not another retained Diagnostic context array. First-use traffic, shared-global ownership, and explicit temporary owner storage must not be conflated. The formulas do not claim a process RSS/stack/loader-memory ceiling or silently fund runtime-global initialization from a record's charge.

For the closed positive skinned-normal shape, the literal lengths are Code 23, Operation 20 and Message 65; maximum CommandIdentity is 110. Producer key/value length pairs are `(10,32)`, `(6,32)`, `(18,19)`, `(14,19)`, `(17,6)`, `(21,6)`; service occurrence adds `(15,1)`. Independently applying `S` gives **1328** conservatively counted string bytes and a retained graph bound **1728**. Under the source-construction model assumed by Core's preclaim helper—one input context array plus the constructor copy—the direct source/append peak is **2320**; the current helper reserves **3856**. These are formula results, not traffic measurements or eligibility. Any external producer Builder, rented formatting buffer, queue/control or other construction storage must have its actual capacity and payer proved separately; the validator cannot retroactively fund it.

The latest-fault pointer is an alias to a retention node, not a second copy: its second service reference keeps the same graph/account charge alive after ring eviction. Caller snapshots contain only copied arrays and Diagnostic references, not node/account/service capabilities. Their later caller lifetime does not keep a service debit alive; a separate engine owner still needs its own funded use. Normal eviction and Close clear their ring references before releasing the node; final release clears the node's payload/account references before refund. Reservation transfer moves retained credits and leaves preclaimed temporary credits until the producer disposes its reservation. These corrected transitions do not establish P1/P2's protected/control funding.

### 4.5 Current disposition at `4069a0a7`

The committed corrections were inspected in source and tied to the current emitted images through all 11 in-scope PDB checksums. C1-C3 are closed for these revision-specific transitions, not evidence of numeric eligibility.

| Item | Disposition at `4069a0a706feabf4e66a3e6affafde503160b836` |
|---|---|
| C1 — Preclaimed ring-slot admission | **Closed.** Preclaimed `WriteCore` calls `TryMakeServiceRoom` with `bytes: 0` and `requireRingSlot: true` before insertion; the already-held byte claim is not charged twice. |
| C2 — Coalescing target after compaction | **Closed.** Both paths copy the prior entry, reserve/make room, find the protected retention's live index with `FindRetentionIndex`, and only then acquire the commit byref. |
| C3 — Actual-release ordering | **Closed.** Close defaults each ring entry before release. Final `ReleaseRetention` captures the charge/account, calls `ReleasePayload` to clear the node's record/account references and charge, then reduces balances and refunds the account. |

**P1 — Required protected roots/peaks are not funded.** The only explicit unused protected reservation is `_protectedSnapshotBytes`. An admitted fault remains charged through `_latestFault`, but there is no protected next-fault/counter construction/replacement envelope. Critical input follows ordinary room-making/refusal before `_latestFault` is updated. Saturation helpers cap counters without constructing the independently required `DiagnosticCounterSaturated` observation. No finite mandatory fault/counter producer representation or complete protected peak is supplied by this Core implementation. Do not invent a numeric allowance or treat ordinary refusal as closure of that guarantee.

**P2 — Complete control/caller funding is not established by shallow sizes.** The account's 128-byte control allowance covers its 72-byte object; it does not itself prove payment for the 80-byte capability, process-lived Core clock storage, external source construction/queue backing, or account-control lifetime after World close. Those require exact existing owner credits and actual last-use rules. ServiceControlBytes' apparent slack is not an ownership mapping. For M05 source construction, the exact missing caller evidence is the lowering/actual capacities and payer for any command-identity formatter, input-context Builder/rental, and pending producer control used before `WriteNoncoalescingWarning`; the closed six-entry validator and Core's 3856-byte prospective claim do not establish that work. Runtime-global BCL caches, including generic Empty storage, numeric strings, comparers and culture state, must be distinguished from Core-owned statics and per-operation borrowed/work storage; warm traffic is not proof of no first-use allocation. No new quota or silent exemption is proposed.

Smallest remaining contract-respecting closure: fund closed mandatory fault/counter and control/producer lifetimes under the existing ceilings, establish the complete conservative protected peak, then obtain owner disposition for the exact profile inputs. C1-C3 no longer block that work, but neither an arena nor larger guessed constants supplies P1/P2's missing evidence. No code or contract was changed by this proposal update.

### 4.6 Connect the proof to admission and existing validation

Preserve the gate/account/retention mechanisms rather than introducing a new emitter or independent quota. All sizing, initial capacity/headroom selection, preclaims, and protected construction must use the same accepted profile. Check impossible shapes and overflow before allocation, then reserve the full simultaneous peak against the existing service and exact source account.

Preserve the current allocation-free count sizing through `GetPositiveDecimalDigitCount` and `StringCharge(int)`, with formatting after reservation. The remaining object/array/string profile and complete construction peak still require proof. If builders or redundant context copies are removed internally, review the resulting construction graph rather than continuing to charge an assumed old lowering. Already-constructed operation results require independently funded construction and a retention claim before the service keeps them; that path is not a loophole for unbounded telemetry construction.

Use the existing [build procedures](../project/Building.md) and [consumer/NativeAOT procedures](../project/Building.Consumers.md), not a new qualification runner. Future focused Core Debug then Release regressions should cover unknown-profile refusal before backing construction, exact-fit/one-byte-short admission above the real baseline, overflow, oversized pre-existing strings, replacement coexistence and decimal digit transitions, protected roots/polling workspace, cross-service account pressure, and release with retained caller snapshots. Expected storage bounds must come from the reviewed derivation, not solely from the production charge helpers. Confirm nonempty selected/executed tests and current-run results under `artifacts\tst\<Configuration>`.

These managed checks establish their executed behavior only. Published NativeAOT and real standalone/hosted consumers require separate execution on each applicable target under [Chapter 35](../design/35-implementation-roadmap.md#4-completion-gates). They do not inherit acceptance from a managed Core test or this proposal.

## 5. When a representation redesign is required

**Not required merely to size the listed immutable controls:** the addendum permits owner-reviewed implementation-specific bounds, and section 4.3 derives their fixed layouts. C1-C3 are corrected; the current non-eligible disposition concerns remaining complete ownership/protected peaks, not evidence that these CLR objects are intrinsically unsizeable. Section 4.5 gives the smaller closure direction.

**Required if remaining representation/construction cannot be bounded, or if implementation-specific sizing is unacceptable:** replace that unbounded path with owner-reviewed bounded storage whose record, text, context, bookkeeping and workspace extents can be accounted before use. A candidate is private value/offset records and text in explicitly owned backing, with occurrence counts kept numerically and immutable public observations constructed at the observation boundary. This is a direction for a complete storage design, not an accepted arena layout or new format; it does not discharge protected/owner funding.

The redesign must still:

- Preserve the existing public Diagnostic/snapshot/latest-fault behavior, ordinary warning first-payload/coalescing semantics, noncoalescing positive skinned-normal records, loss counters, exact attribution and immutable observations.
- Include all remaining managed controls and all managed materialization **before caller handoff** in the existing construction limits. Merely returning those objects to a caller does not make their engine construction free.
- Prove owned backing and allocator/bookkeeping bounds. A fixed `byte[]` still has a managed header; an `ArrayPool` request need not equal actual rented capacity. [`NativeMemory.AlignedAlloc`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.interopservices.nativememory.alignedalloc?view=net-11.0) specifies a requested block/alignment and wraps a platform allocator; it does not certify total allocator overhead. Moving bytes to native storage alone is not a complete proof.
- Fund service controls, mandatory fault/counter roots, replacement overlap, and polling workspace under the unchanged ceilings. Do not preallocate an assumed full 8-MiB arena, add a separate allowance, truncate payloads, or silently change snapshot semantics.

If these remaining allocations cannot be bounded either, the redesign is also blocked. Any necessary storage/observation contract clarification requires the Chapters 04/28/29 owners' explicit disposition before implementation; this proposal makes none. Do not replace one unproved size model with another.

## 6. Frozen review inputs

Refreshed on 2026-11-19 at Core source/worktree revision **`4069a0a706feabf4e66a3e6affafde503160b836`**. The service row is the inspected committed C1-C3 correction, superseding all previous current-source hashes; the reviewed Core files are clean. Prior-session preserved images remain older emission inputs. The current Debug/Release outputs match all 11 in-scope source documents, including corrected service, validator, clock, query and severity. Their mutable output paths must retain these exact hashes for review. The checkout-local RC3 runtime, not global RTM, is frozen. No schema or protected fixture is changed or accepted. Other workspace changes are outside this review.

| Input | SHA-256 |
|---|---|
| `global.json` | `1bce01871321c2df82e887da428ce5c121a6c6541fe65017fe3d249be47e190c` |
| `sources\TerraFX.Core\TerraFX.Core.csproj` | `bda8230d77298ee852b28ed8446c66e7add9227f6ad9285087e3408f351e0088` |
| `sources\TerraFX.Core\Diagnostic.cs` | `b827997b35ee8accccfa069905865c1e155508401fe7256eacbd0b20e2a6b840` |
| `sources\TerraFX.Core\DiagnosticContextEntry.cs` | `c2a1e74e0ae9929638c9255cfaa8dd7235ce37509483660b740569b775bef530` |
| `sources\TerraFX.Core\DiagnosticSnapshot.cs` | `d474d194cd990ccd71c6d919ccbb75166d1e15891cfb56ca473c86a4388a28e8` |
| `sources\TerraFX.Core\DiagnosticsService.cs` | `7445732ebf740791290f32302ee30be5689cf28ec996f92502958b3ac44696d0` |
| `sources\TerraFX.Core\DiagnosticClockDomain.cs` | `d194906d28d774df01a83dc7c554e0af82645de90fa9931d415dbd02134bd680` |
| `sources\TerraFX.Core\DiagnosticQuery.cs` | `e8574d8da53a59f71557eab8ff101f8ee56a9cb126ac26614ce4d7f6795f6de2` |
| `sources\TerraFX.Core\DiagnosticSeverity.cs` | `e90303d70cecfd9b83ec4d9d5716697230442896d997059cf29a24eb49ae2327` |
| `sources\TerraFX.Core\DiagnosticAttributionReservation.cs` | `9bcc73583910112962119bb82d283e7b3a722338daf93705bac2dcf64d1b9ddf` |
| `sources\TerraFX.Core\WorldDiagnosticAttributionAccount.cs` | `75b41f64fcb6af40bfdb7d0c1203596dedd3f8826ea8aa006af9dabe81e25e5b` |
| `sources\TerraFX.Core\WorldDiagnosticsCapability.cs` | `7feb090087103d651e7674ed75d90d103bb7aa2ea039e273e7b41b67d2a2806d` |
| `sources\TerraFX.Core\ReentrantGate.cs` | `8f0ef274c39c3e07e691951011ac86c574311912d7f78348eb8d6780555f0c71` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Private.CoreLib.dll` | `df53885902e896a2b40988237cbe9cbb9f1bb0743200f05fe9257c56a29c5732` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\coreclr.dll` | `46a09d97ad8dd30d400a86c90af707bee7593b585d3ccb8586c4fffca404d1e2` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\clrjit.dll` | `633dea0ca27ad2aa673d7a085cc36c2880bb44d1948b0f1750e38376380c023f` |
| `.dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\System.Collections.Immutable.dll` | `e00e76c930111923ac39029451096126126d4e167fe9d8df018eb2c49c9c477b` |
| `.dotnet\sdk\11.0.100-rc.3.26522.148\Roslyn\bincore\csc.dll` | `a3c6fcd1fb9ae921a985e7d55f79ce921eee79a5a61a11f6d8ce9776baf81172` |
| `session artifact: files\diagnostics-profile\TerraFX.Core.Debug.net11.0.dll` | `00a56c4bb8a8995a0c5e254155e013b31205c1ac3ec3fc82392e9a81a762c20b` |
| `session artifact: files\diagnostics-profile\TerraFX.Core.Release.net11.0.dll` | `bfff18b4953ce6f19d1d566b0e64d815e3c1179e743dcc20e79e43952cdb47fa` |
| `artifacts\bin\sources\TerraFX.Core\Debug\net11.0\TerraFX.Core.dll` | `b0d1778169b2a5e2b78a19473ec04dbfe4a43f7b90b5f1c4fc1b762cf60fe731` |
| `artifacts\bin\sources\TerraFX.Core\Release\net11.0\TerraFX.Core.dll` | `ff1b7106417139b68535cd021a36ddcf5cbc7e8663af6461cc826435d0122dd8` |
| `docs\proposals\m05-diagnostics-retained-payload-accounting-addendum.md` | `8200267782bfb5a8058fb2a0537e5477ccd79368b2b76b63f64e4ee91200eaab` |
| `docs\design\04-capacity-and-workload-profiles.md` | `60047dd1eacf26196b0bf23f16798e2aa265fd26cd36b9a0b4df1b79135730d7` |
| `docs\design\28-diagnostics-performance-and-validation.md` | `0e3c895bee5f76118ce87d208170cb6e02e9a19de3f286b7e243b28165370d14` |
| `docs\design\29-project-setup-build-and-deployment.md` | `49598161138febbf889f2a5176e3504af8d8bb8481d2157fbcfbbd94d2b46604` |
| `docs\design\35-implementation-roadmap.md` | `24eec6e92b7ad904d552f290ef748ffc8fce13b6b94a89ebd9f39487d4584d87` |
| `docs\project\Roadmap.md` | `fd84bbdf11fef04c15fc09489dd663864664efadebf1e294748c83e8a73b1bf8` |
| `docs\project\Building.md` | `f043a352870efb6c7d4a61998225e2508a8f4370a57e784483a41819d452075f` |
| `docs\project\Building.Consumers.md` | `8af46079a9e11e01578cc9b76b695801b585f58148df36418ee5506ca4006442` |

Review this proposal's exact bytes with these 30 inputs and its RC3 commit-pinned source links; recheck hashes before disposition and refreeze changed relevant inputs. The API links establish public guarantees and the runtime links establish only the stated implementation facts, not qualification.

## 7. Unresolved gates and concrete next action

| Gate | State / remaining action |
|---|---|
| Complete protected/owner peak | **Open.** Close finite fault/counter representations and protected coexistence; name the capability/clock/account/producer payers and actual last-use credits. The fixed layouts and direct heap factors do not discharge P1-P2. |
| Numeric eligibility | **Not selected.** Current validator emission and fixed sizes are derived, but the complete existing-ceiling admission/ownership bound fails to close. Keep unknown/unqualified profiles closed. |
| Owner disposition | **Open.** Review the frozen profile scope and bounds; tentative implementation guidance is not numeric acceptance or approval of a storage redesign. |
| Final profile implementation and managed behavior | **Partial evidence only.** Managed Core regressions are reported 22/22 per configuration and the targeted Runtime selection 5/5 per configuration at `4069a0a7`. Complete protected funding, final profile implementation and exact byte-boundary checks remain open. No tests were run or result files verified here. |
| NativeAOT and real consumer observations | **Open and distinct.** Execute actual target artifacts and genuine producer paths when their prerequisites exist; preserve the [current M03-B/M05 gates](../project/Roadmap.md). |

This documentation-only update performed source and static PE/PDB inspection, not a build, test, allocation probe, heap inspection, native run, or AOT execution. Reported traffic and regression summaries remain attributed evidence, not runs performed or result files verified here. Independent dependency-ready work may continue; the blocked diagnostics prerequisite is not waived.

**Next action:** close P1/P2's finite protected-root and control/producer funding inputs and the complete protected coexistence bound, then freeze matching final source/images for owner disposition and focused byte-boundary validation. Reuse the fixed-layout derivation only where the relevant metadata/runtime layout remains unchanged; do not rerun traffic to choose constants. Until all of that closes, select no numeric eligibility and retain fail-closed behavior.
