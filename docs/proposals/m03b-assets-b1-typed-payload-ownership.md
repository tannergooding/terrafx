# M03-B Assets B1: bound typed Mesh/Material payload ownership

**Status: tentative blocker guidance for review, not an accepted contract or qualified profile. Native initialization/acquisition and CLI activation remain unavailable.** This complements [the worker-envelope rectification](m03b-assets-batch-b-worker-envelope-rectification.md#6-staged-implementation-and-focused-evidence); it does not reopen its caller-executed port selection or B2 gates.

## 1. Decision and source boundary

Complete the **compile-only** bounded path using one operation-owned entry buffer, the existing envelope/reference helpers, and both sealed 1.0 codecs. Preclaim their complete simultaneous storage under the original Store grant before entry staging or decoding. Select a conservative metadata-length bound first; do not add a second semantic decoder or a general sizing API.

The gap is not already resolved:

| Current source fact | Consequence |
|---|---|
| [`AssetPackageReader.cs:225–661`][metadata] hashes/parses two None entries and returns entries/profile/settings plus the exact `AssetPackageBackingToken` only after padding validation. Its new `ReadBoundedPayload` requires that token and a caller-owned span, validates codec/range/decoded-length/destination capacity, then reads from the same backing; neither helper has a production caller. [`AssetPackageBacking.cs:54–69,232–248`][backing] still leaves bounded `Entries` empty and refuses ordinary reference acquisition. | The read primitive now enforces same-backing identity and destination/range preconditions, but owns no staging and has no typed caller. Complete its funded installation/lookup/lifetime on the same original backing; do not construct an ordinary FileStream backing. |
| [`AssetBoundedNativePort.cs:527–572,861–898`][port-read] copies through 65,536-byte scratch into a caller destination. [`AssetPayloadReader.cs:16–35`][payload] allocates an entry-sized array and calls the FileStream-dependent async path. | Scratch is not entry storage. The bounded reader now has a caller-span path; the operation must preclaim and retain that buffer, not call `ReadAsync`, `RandomAccess`, or an async bridge. |
| [`AssetEnvelopeDecoder.DecodeBounded`][bounded-decoder] | Uses caller-owned reference/index/result arrays with `CountAssetIds`, `ScanInto`, and `CopyUniqueReferences`, then invokes the supplied typed codec and returns a scalar failure. It does not allocate the ordinary scanner's `List`/`HashSet` or an immutable reference result. Its caller must supply the exact B1 registration's codec/context and preclaimed buffers; it does not select or own them. The 3/3 helper tests exercise only `TestAsset`, not Mesh/Material or a bounded package. |
| [`AssetStore.Bounded.cs:80–118`][preclaim] funds the construction prefix; [`AssetStoreBoundedProvision.cs:28,116–150`][provision] has readonly `_reservedBytes` and an unstarted-release guard. | Neither supplies a payload/result claim or an execution permit. Keep that guard; do not enlarge or refinance an already-issued Batch A delivery. |
| [`StaticMeshCookCommand.cs:193–252`][cook] writes a temporary package, uses ordinary `CreateAsync`/`LoadAsync`, then checks both values. | Reuse its selected single-group/single-material/unskinned/textureless-PBR checks, not its BCL transport. Standard mode remains unchanged. |

Keep the [factory's exact two-codec registration][factory], backing-derived `AssetCodecContext.ReadLimits`, and ordinary public contracts unchanged. Future bounded construction must supply Runtime's immutable closed-code sizing facts through the sealed construction path, never an upward sizing callback into Runtime from Core-only Assets ([Chapter 29:18–33][dag]).

## 2. Exact ownership boundary

Selected obligations to implement, not existing capabilities. Preserve the rectification's single nonwaiting ordinary slot and source-free, noncancelable interval; returning operations still occupy that slot.

| Storage | Payer and end/transfer boundary |
|---|---|
| Two entries and package provenance | Original unpublished generation, installed only after successful metadata and cleanup observations. Its registration/file/metadata debt survives replacement, logical Store close, and retained or failed generations. |
| Entry staging | Original issuer's admitted operation owns one `byte[]` of capacity `max(Lmesh, Lmaterial)`. Read/expose only the selected entry's length. Reuse requires native completion **and** return of every reader/scanner/codec borrowing that slice. Partial reads never expose a value. Fixed profile/directory buffers remain separate owners. |
| Codec work | The operation's claim covers callee-local arrays, UInt16 conversion temporary, reference workspaces, contexts, diagnostics, exceptional unwind, and caller stack. Install the pending-result/control interest before invoking either codec; the enclosing invocation retains its claim until actual helper unwind and result installation. |
| Successful result | Transfer the result portion, not another entitlement, to the exact generation's internal bounded lifetime/handle control. Initially retain its whole conservative `Qmesh`/`Qmaterial`, including temporary allowance, until final result-interest return. Retained handles share the physical graph/debit once but each consumes lease capacity. No raw value/staging alias may escape a funded internal interest; caller-local value aliases remain charged through actual return. |
| Terminal delivery | Existing scalar outcome/delivery selection owns a prepaid return partition. Detach Store/backing/codec/context/exception aliases before terminal detachment; do not refund that transferred partition into reusable Store capacity. |

The result graphs are concrete: [`RenderMeshAsset.cs:14–96`][mesh-value] retains streams, indices, groups, slots and encoded bytes; [`RenderMaterialAsset.cs:12–52,103–123`][material-value] retains encoded bytes with inline parameter/binding values.

## 3. Derivation before entry reads

Map the two positive distinct caller-selected IDs to the returned entries, **not** to directory position. The directory has no schema tuple ([Chapter 11:79–87][package-contract]); expected Mesh/Material identities remain hypotheses until envelope inspection. Let `Lmesh`/`Lmaterial` be their checked stored=decoded lengths. Each must fit `MaximumDecodedAssetBytes`; the staging capacity must fit `MaximumConcurrentDecodeBytes`. The two pending validation handles and existing handles must fit the lease limit. Reject missing IDs, overflow or insufficient remaining original grant before allocating entry storage.

**Derived data bound, not an all-in allocation profile:** structural validation precedes codec actions and ensures nonoverlapping record/array extents ([`EbinStructuralReader.cs:20–39`][structural], [Chapter 10:62–64][arrays]). The Mesh read makes at most eight stream arrays, two index arrays, one slot array, one group array and one encoded-record array: **13 arrays**, including rejected-input work. Streams/groups/slots do not expand their encoded data; UInt16 indices alone can require `ushort[n]` plus `uint[n]`, three times their index-data bytes. Thus all bulk array data excluding the record copy is at most `3*Lmesh`; the copy is at most `Lmesh`.

These source-level counts follow [`RenderMeshAssetCodec.cs:189–349,350–497,610–700`][mesh-codec], not the eventual one-group shape. Duplicate stream-rank rejection occurs **after** allocating that stream; include it. Both codecs copy their entire nested record ([Mesh:496][mesh-copy], [Material:155–172][material-codec]). Material parameters/bindings add no variable result graph. The selected x64 Debug/Release tests assert `Vector2=8`, `Vector3=12`, `Vector4=16`, `RenderMeshGroup=12`, and `AssetId=4`. Compiled Debug/Release IL shows the 13-array maximum is the eight source-count-bounded stream arrays, two index arrays (the UInt16 temporary and retained UInt32), slots, groups, and one record copy. The collection expression calls `ImmutableArray.Create<Byte>(ReadOnlySpan<Byte>)`; on the selected .NET 11 runtime, that calls `ReadOnlySpan<Byte>.ToArray`, which allocates one nonempty `byte[]` and copies once. This closes element-stride and array-membership evidence only; it does not close overlap/liveness, `Coperation`, admission or return.

For an admitted target, let `H` bound one array's header/rounding overhead, and `M`/`T` be actual Mesh/Material shell charges:

```text
Qstage    = max(Lmesh, Lmaterial) + H
Qmesh     = 4*Lmesh + 13*H + M
Qmaterial = Lmaterial + H + T
Qnew      = Qstage + Qmesh + Qmaterial + Qreferences + Coperation
ExistingDebit + Qnew <= original B0
```

For the selected x64 profile, Chapter 04's `ArrayCharge` gives a conservative `H = 79` bytes (64-byte header plus at most 15 bytes of rounding). The focused `TypedAssetClassShellAllocationsMatchSelectedRuntime` test measures `M = 176` and `T = 240` bytes in Debug and Release. It uses `RuntimeHelpers.GetUninitializedObject`, so these are shallow-shell observations only; no constructor or dependent result array ran.

`Coperation` covers all additional contexts, controls, metadata installation, structural/semantic stack, runtime/static initialization, transient failure and protected return costs not already in `ExistingDebit`. That debit includes already-funded construction/shared state and every old/current/candidate/quarantined/returning obligation. Count physical storage once; preserve the existing decoded-wire minimum as a separate admission constraint, not an extra duplicate physical allocation.

`Qreferences` can be finite before decoding: the sealed descriptors admit at most **257 Mesh occurrences** (256 slots plus optional Skeleton), and **4 Material occurrences** (four PBR bindings, or one Sprite binding). [`RenderingSchemaCatalogue.cs:159–166,214–249`][schemas] and [`AssetReferenceScanner.cs:347–353,522–642`][scan-validation] enforce these cardinalities before visiting children. Do not assume one/zero before the final static-family checks. `AssetReferenceScannerMeasuresFixedScanDataBytes` now locks the 257/257 Mesh maximum at 7,344 workspace bytes plus 6,240 result bytes (13,584 total); its existing 4/4 case charges 240 plus 160 bytes for Material. Reuse one 257-capacity set for sequential Material-then-Mesh decoding only after each reference result has been validated/consumed; the actual caller sequencing is still unimplemented. Use exact-count slices with `CountAssetIds`, `ScanInto`, and `CopyUniqueReferences` ([scanner:36–224][scanner]). Ordinary `Scan` instead allocates List/HashSet growth, so [`AssetEnvelopeDecoder.cs:264`][decode-scan] cannot be reused unchanged.

**Distinguishing example:** three UInt16 indices have six data bytes, but the current codec constructs six temporary plus twelve retained index bytes, alongside array headers, entry staging and the record copy. None compression and `_liveAssetBytes += decodedBytes` do not establish that ownership.

No exact pre-payload count is obtainable from this directory alone. The conservative bound avoids requiring one. Any later tightening must borrow a structurally validated payload under an already-funded coarse claim, reuse codec checks such as [`PreflightSharedIndices`][index-preflight], and count **all allocations made before rejection**, not just the valid final graph.

## 4. Reuse and failure/return semantics

Reuse Store arithmetic and generation identity, not ordinary execution. [`AssetStore.cs:419–445,448–491`][accounting] maintains declared-wire live/decode counters and lease counts; [Chapter 30:3913–3915][public-assets] explicitly excludes codec allocations from those budgets. Bounded admission needs the additional checked all-in debit under the **same** `B0`. Preserve all caller limits; do not reinterpret concurrent decoded bytes as a complete codec-memory allowance.

Decode Material then Mesh through the sealed adapters, using the one staging buffer and private record copies. The new generic `AssetEnvelopeDecoder.DecodeBounded` helper provides envelope validation and caller-buffer reference scanning, and rejects mismatched root identity, but it does not enforce the B1 family or own its codec/context/buffers. The future Runtime caller must supply the factory's exact codec/context and preclaimed storage, then validate full consumption, exact 1.0 roots, same-generation reference IDs/schema majors and the existing static-family checks before publication. A validated target-schema fact may serve only this admitted operation's exact backing; no persistent cache or new registration is needed. Later anchored loads must retain that same backing, not consult a replacement. Do not call `AcquireLoad`/`LoadAssetAsync`/public lease disposal to simulate this: they reach BCL reads and exception-retaining lifetime paths ([`AssetStore.PackageLease.cs:130–209`][ordinary-load], [`AssetLeaseLifetime.cs:162–239`][ordinary-release]).

On any read/scan/decode/shape/installation failure, publish no typed result. Preserve the scalar primary and independently safe cleanup facts after actual helper unwind; transient exceptions/diagnostics still cost provision but must not become retained outcomes. Check `hashProviderRelease` separately: [`OpenBoundedMetadata:258–261`][hash-cleanup] can return a default primary when that cleanup failed. Default primary alone is not success.

Keep staging/results/claims through native access and all installation/return interests. Release only after actual last use or an acknowledged already-funded transfer. Failed/uncertain cleanup retains the exact original owner/debt; a consumed file is never closed again. Reuse the rectification's [one-shot close and terminal protocol](m03b-assets-batch-b-worker-envelope-rectification.md#5-operation-unwind-and-return-protocol): helper return is observed by Runtime, driver return by the actual Content.Tools recipient before the proposed `EndDelivery`. The current [delivery:50–67][delivery] implements only `ReleaseUnstartedPreclaim`, not this chain. Readiness, a self-written flag, GC eligibility, or Task terminality is not a return receipt.

## 5. Evidence and no-go gates

| Gate | Permitted work/evidence and remaining prerequisite |
|---|---|
| Acquisition-free now | Existing six `BoundedStaticMeshPreclaim*` regressions; five `BoundedMetadataParser*` tests plus four `BoundedPayloadRangePreflight*` tests (reader selection 15/15 Debug and Release); three `AssetEnvelopeDecoderBoundedDecode*` tests (3/3 within the 60/60 `AssetStoreTests` class selection in Debug/Release); `AssetReferenceScannerMeasuresFixedScanDataBytes` includes the maximum Mesh 257/257 case; `TypedAssetResultArrayElementStridesMatchSelectedRuntime` passes 1/1 in Debug and Release; the remaining `AssetReferenceScanner*` workspace/order/overlap tests ([Store tests:748–934][scanner-tests]); the declaration/buffer tests ([allocation tests:64–106][allocation-tests], declaration case x64-only); and `TypedAssetClassShellAllocationsMatchSelectedRuntime` (1/1 Debug and Release, `M/T = 176/240`). The three decoder tests use only the generic `TestAsset` codec and in-memory envelopes; the rest of `AssetStoreTests` uses ordinary Store/FileStream paths. These observations establish no Mesh/Material package or native consumer. The shell test bypasses constructors; create no new test-only producer/package. |
| B1, after real private implementation | Compile the complete bounded reader/decode/lifetime/return candidate without calling native initialization. Add focused pure checks in owning fixtures for checked length/claim overflow, phase exact-fit/one-byte-short refusal, referenced target larger than parent, UInt16 expansion, late duplicate-rank/shape rejection, reference cardinalities, and no refund/double transfer while returning. Exercise codecs only over existing reviewed record bytes. These cannot establish complete-profile fit or native retirement. |
| B2, **before any native acquisition** | Close target-specific `H`, value/carrier sizes, allocator/provider/interop/static and caller-stack costs, failure peaks, full original-grant ledger, useful default-budget fit, scalar delivery and final-generation actual-return ownership. Reconcile the previously selected owner-contract exceptions. Then, and only then, admit real existing production GLB/OBJ packages on the named target and observe chunked reads, failure/unwind, replacement/retained generations, one-shot close and the actual recipient. Missing safe stimuli remain explicit Chapter 35 gates. |

Use ordinary owning-project Debug then Release commands and fresh nonempty selections per [Building](../project/Building.md). Existing Mesh/Material package codec fixtures invoke ordinary Store/file paths; they are **not** acquisition-free bounded-path evidence. The range preflight and generic decoder tests used no package-file/native I/O; the generic helper tests invoke only `TestAsset`. No bounded Mesh/Material codec, package generation or B2 qualification ran. TRX, assembly identities/hashes, and compiled IL identity are in the ignored `artifacts\log\m03b-assets-b1-typed-payload-proposal\inputs.json` manifest.

**NO-GO:** any unbounded/unproved term, unavailable target layout/runtime/provider or protected return/disposition suffix, incompatibly initialized shared prefix, or checked original-grant refusal keeps execution unavailable. Do not substitute zero/observed-high-water charges, an increased budget, another grant, an always-refusing “qualified” profile, BCL fallback, fabricated producer or weakened acceptance. B1 source completion does not waive B2 execution prerequisites or the later C/D/E typed/recipient/CLI observations. [Chapter 35:40–48,123–129][gates] permits independent M05 progress but supplies no Scene/renderer qualification; keep `PreparedScene.QualifiedFootprint` null.

## 6. Review input identity

Frozen UTC `2026-11-20T19:24:21.9151865Z`, HEAD `55c378c781c03da8e8d2d2787a010f6727ece913`; selected SDK remains `global.json`. The 62 directly scoped input hashes and five unrelated dirty-test preservation hashes are in ignored `artifacts\log\m03b-assets-b1-typed-payload-proposal\inputs.json`. Input-set SHA-256: `e71103a58be982348fd873a78fc3567bf44688ab9933baf71721daab8e439bbd`; preservation-only set: `0247a70f5dedcaac56865e99640c263109f72120dff8cf7a79b78257305b2162`. Sets use ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` rows, LF-joined UTF-8 without BOM/trailing newline; exclude this output.

| Preserved schema input | Raw SHA-256 |
|---|---|
| `CoreContentSchemaCatalogue.cs` | `91cf1aa2ff4feaba898d83e587d4be403de6cfea97cfbb59fb15d5b67edf2853` |
| `NativeAssetSchemaCatalogue.cs` | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |
| `SceneSchemaCatalogue.cs` | `cf0253cf1c207644f0b6c76b0392977e02b0939ff355ed825f38d42705f2b32a` |
| `RenderingSchemaCatalogue.cs` | `de25a8c0e249ff9ab5b592fe848c85429c004fe629694c64ef1afef3ba731614` |

Recheck exact inputs before review disposition or implementation; acceptance cannot transfer to changed bytes. The production changes add a token-bound caller-span payload read/range preflight and generic caller-buffer envelope decode; neither has a production caller. Tests add pure range and decoder helper cases plus the constructor-free shell measurement. No maintained chapter, budget, public API, schema, diagnostic/acceptance ID or remote publication is changed.

[metadata]: ../../sources/TerraFX.Assets/AssetPackageReader.cs#L225-L661
[bounded-decoder]: ../../sources/TerraFX.Assets/AssetEnvelopeDecoder.cs#L70-L420
[backing]: ../../sources/TerraFX.Assets/AssetPackageBacking.cs#L54-L69
[port-read]: ../../sources/TerraFX.Assets/AssetBoundedNativePort.cs#L527-L572
[payload]: ../../sources/TerraFX.Assets/AssetPayloadReader.cs#L16-L35
[preclaim]: ../../sources/TerraFX.Assets/AssetStore.Bounded.cs#L80-L118
[provision]: ../../sources/TerraFX.Assets/AssetStoreBoundedProvision.cs#L28-L150
[cook]: ../../sources/TerraFX.Content.Tools/StaticMeshCookCommand.cs#L193-L252
[factory]: ../../sources/TerraFX.Runtime/Rendering/BoundedStaticMeshAssetRegistrationFactory.cs#L15-L33
[dag]: ../design/29-project-setup-build-and-deployment.md#L18-L33
[mesh-value]: ../../sources/TerraFX.Runtime/Rendering/RenderMeshAsset.cs#L14-L96
[material-value]: ../../sources/TerraFX.Runtime/Rendering/RenderMaterialAsset.cs#L12-L123
[package-contract]: ../design/11-assets-import-streaming-and-packages.md#L79-L87
[structural]: ../../sources/TerraFX.Core/EbinStructuralReader.cs#L20-L39
[arrays]: ../design/10-binary-data-and-authoring-formats.md#L62-L64
[mesh-codec]: ../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetCodec.cs#L189-L497
[mesh-copy]: ../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetCodec.cs#L496
[material-codec]: ../../sources/TerraFX.Runtime/Rendering/RenderMaterialAssetCodec.cs#L141-L172
[schemas]: ../../sources/TerraFX.Graphics/RenderingSchemaCatalogue.cs#L159-L249
[scan-validation]: ../../sources/TerraFX.Assets/AssetReferenceScanner.cs#L347-L642
[scanner]: ../../sources/TerraFX.Assets/AssetReferenceScanner.cs#L36-L224
[decode-scan]: ../../sources/TerraFX.Assets/AssetEnvelopeDecoder.cs#L264
[index-preflight]: ../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetCodec.Shared.cs#L10-L37
[accounting]: ../../sources/TerraFX.Assets/AssetStore.cs#L419-L491
[public-assets]: ../design/30-public-api-review-surface.md#L3913-L3915
[ordinary-load]: ../../sources/TerraFX.Assets/AssetStore.PackageLease.cs#L130-L209
[ordinary-release]: ../../sources/TerraFX.Assets/AssetLeaseLifetime.cs#L162-L239
[hash-cleanup]: ../../sources/TerraFX.Assets/AssetPackageReader.cs#L258-L261
[delivery]: ../../sources/TerraFX.Runtime/Rendering/BoundedStaticMeshAssetDelivery.cs#L50-L67
[reader-tests]: ../../tests/TerraFX.Assets.Tests/AssetPackageReaderTests.cs#L131-L226
[scanner-tests]: ../../tests/TerraFX.Assets.Tests/AssetStoreTests.cs#L748-L934
[allocation-tests]: ../../tests/TerraFX.Assets.Tests/AssetStoreAllocationTests.cs#L64-L106
[gates]: ../design/35-implementation-roadmap.md#L40-L129
