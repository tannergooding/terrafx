# M05 diagnostic buffer-import index owner

## 1. Implemented component and unchanged gate

**Tentative component closure, pending review. The complete 32/16-MiB diagnostic gate remains NO-GO.** This implements only the first import-index target selected by [physical-storage qualification](m05-skinned-normal-diagnostic-physical-storage-and-alias-qualification-addendum.md).

The actual owner is **`GraphicsGraphSubmission`**, not a temporary validator object or backend-local cache. It retains its frozen `BufferImports` and one private readonly `int[]` index from construction through its actual managed lifetime. Existing uncertain-submission quarantine retains that same submission, so it retains the same index/backing; no new quarantine carrier, reference copy or lookup-table transfer is introduced.

The index replaces only `bufferImportsByResourceId`. Other maps/sets, backend-retained capacity, owner/control groups, Runtime baselines, payload aliases, request/return controls and completion terms are still unqualified.

## 2. Exact physical storage and allowance

- The constructor checks the component allowance before its array construction and allocates exactly `new int[BufferImports.Length]` for a nonempty index. This is the explicit capacity of the new representation, not an inference about the old Dictionary's buckets or entries.
- The backing charge is `64 + roundUp16(actual index capacity * sizeof(int))`. Empty input reuses shared empty storage and has no new per-submission index backing. Shared/cold initialization is not thereby declared qualified.
- One or four indices have an **80-byte** conservative backing charge; seventeen have **144 bytes**. These are array charges, not the size of import records, GPU buffers, native resources or the entire submission object.
- A caller can supply a smaller internal component allowance. Diagnostic-bearing submissions additionally clamp that allowance to the unchanged **33,554,432-byte** joint ceiling. Ordinary submissions acquire no new M05 numeric cap by default.
- `BufferImportIndexStorageCapacity` and `BufferImportIndexBackingByteCharge` report the actual retained array. Safe recorded-command detachment, frame-slot reuse, repeated validation and uncertain retention do not shrink/refund it.

This is a **necessary one-component preallocation check and retained charge**, not a complete joint-headroom reservation. In particular it does not fund the existing submission allocation/control group, its added reference field, other constructor arrays, Runtime handoff or simultaneous preflight peak. Passing it cannot certify whole-graph admission.

## 3. Construction and validation ordering

The index stores original import ordinals, sorted by `ulong ResourceId` and then original ordinal. An in-place heap sort uses only the allocated index and scalar/local state: no comparer/delegate object, temporary sorting array, pool or opaque hash capacity is introduced. Imported records and their immutable identities are not copied or reordered.

Preparation does **not** reject duplicates. The original import-validation loop still visits input order. For import i, the sorted index finds the first original ordinal with that identity; only a first ordinal less than i triggers the existing duplicate-import refusal at that point. An unsupported earlier import therefore still fails before a later duplicate; a duplicate encountered first still fails before its later usage checks.

Null malformed entries are merely ordered during preparation. They are not assigned a synthetic accepted record or validated early; the original ordered validation remains responsible for their failure. This prevents sorting from moving a later malformed-entry failure ahead of an earlier established usage refusal.

The new binary lookup is used for the former map's alias-barrier resource lookups. Missing IDs still return false to the existing alias-barrier validation. Other existing lookup/error paths are unchanged. Repeated prequeue/backend validation borrows this immutable index and does not reconstruct the removed Dictionary.

## 4. Owner/lifetime evidence and remaining boundary

The owning Graphics fixture uses real Graphics device/buffer/import/submission owners in its existing managed backend harness:

- empty, single, permuted, unsigned-high/sparse, sorted, reverse, duplicate and uneven larger literal membership;
- exact physical index capacity, independently derived Q charge, first-original-member lookup and zero warmed lookup allocation;
- exact-fit component allowance, one-byte-short refusal and checked negative/overflow planning;
- duplicate-versus-usage and malformed-entry validation precedence;
- identical backing across repeated validation and safe/uncertain recording cleanup.

No Scene, fabricated snapshot, native diagnostic consumer, NativeAOT or public-consumer observation is added. The existing command representation, bindings, schedule, complete result membership, Core readback and native use/fence/quarantine semantics are unchanged.

The private index cannot escape through a production array getter. A command/pass/binding alias, however, remains outside this component: the new backing does not create the qualified payload-owner/borrow and element-reference interest required by the parent proposal. No independent alias refund is introduced.

**Remaining NO-GO:** complete joint phase maxima/retained baseline and submission controls; all other physical map/set/backend groups and mutable owner reservations; payload/child-array aliases and quarantine publication; request/TCS/async-return/fence controls; independent **16,777,216-byte** completion/result/receipt terms. These require their real qualified owners, not reuse of this component's allowance.

## 5. Frozen inputs

Whole-file SHA-256 values identify the narrow dirty-workspace inputs based on HEAD `636102d8b58cee8f966520eaec80aff08cc2d376`. HEAD alone is not the reviewed input. Recheck before applying a disposition. Component tests do not establish full object/native qualification.

| Input | SHA-256 |
|---|---|
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `docs\proposals\m05-skinned-normal-diagnostic-physical-storage-and-alias-qualification-addendum.md` | `FB9182D9FEBD235EC602114EA3944791B92D77D9BC0775584DCEFEDE26FB8D23` |
| `sources\TerraFX.Graphics\GraphicsGraphSubmission.cs` | `AFFB6015FAAB8941529A124875482B97A147136F60B8A7DB2A6B89046482689B` |
| `sources\TerraFX.Graphics\GraphBufferImportRecord.cs` | `BAEC4E978C8DA0E23F3ACACDAB9B46D15C2B065C5EAEF579CEA21E51967DE120` |
| `sources\TerraFX.Graphics\GraphBufferVersion.cs` | `FEE9EE352B9C46186FA19BE2F28AFAB9EBC00D441BC23BE596569E5090BAE6BE` |
| `sources\TerraFX.Graphics\GraphicsDevice.SubmitGraph.cs` | `C68E23B5FFA591C639714101FB23A1F1AE883FAC28F722EEB5B034725C3A97FD` |
| `sources\TerraFX.Graphics\RecordedPassCommandList.cs` | `36C99EEC7846E37DB528F69421B79D957BBDBAC5B11A4A83E6D62F837AC25571` |
| `sources\TerraFX.Runtime\Rendering\RenderGraphPlan.Submission.cs` | `34AB6687DED10903F40453441F3158F78DC4D8C78D82982863CADE038870525C` |
| `tests\TerraFX.Graphics.Tests\GraphicsBufferTransferTests.cs` | `D7F9D4B4842D5026F5C4902866B7E2FEABC3509E61A6F0B8CD75CE2F9549A6B8` |
