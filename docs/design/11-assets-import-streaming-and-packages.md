# Asset import, streaming, and packages

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Asset identity and content-set scope

`AssetId` is a nonzero signed 32-bit integer, unique within one selected content set. Zero is invalid. The same integer may exist in another independently loaded content set; v1 does not define cross-set references or a global asset-ID service. An asset reference also names its expected schema ID and major, so resolving an integer as the wrong asset type is rejected. Asset IDs are not paths, content hashes, package IDs, runtime handles, or proof of authenticity.

An ID is assigned once and retained across source rename, content edits, and recooking. Native authoring stores it in the source document; importers for formats without a suitable field use an adjacent ID sidecar that moves with the source. A small project allocator stores the next/high-water ID. It records no deleted-asset ledger; deleted IDs are not reused, and checked exhaustion never wraps. Duplicate live IDs in a content set reject.

Normal allocation is monotonic. Parallel tools may reserve disjoint ranges from the same allocator; checked bounds prevent overlap and overflow. Temporary worker IDs may be wider than the persisted domain. Before persistence or publication, final serialization orders occupied contiguous ranges by stable logical input, assigns each a consecutive persisted range using checked actual counts, and relocates every declared reference by that range's offset. Worker assignment or completion order cannot affect the finalized IDs or package bytes. Only new, unpublished IDs may be relocated; published IDs are never compacted or moved.

Reservations coordinate only within one allocator domain. Disconnected project or VCS branches that allocate independently may collide; if that workflow is supported, a merge detects collisions and explicitly remaps every affected reference. Thread-local ranges alone do not coordinate branches, and no global identity service is implied.

The v1 deployment unit is one immutable package for a coherent content set and selected platform/profile. Package entries are unique by unsigned AssetId order. External asset references resolve within that selected set; cross-package references, overlays, independent DLC updates, and “latest version” selection are not part of this contract. A build may traverse direct typed references to ensure the package is complete, but it does not serialize a general transitive dependency graph.

## 2. Source adapters and deterministic content builds

Source formats are build-time adapters, not runtime models. Native authoring, procedural content, and source adapters produce the same validated native asset records. Conversion happens once at the adapter boundary. The runtime consumes only the package format below; it does not load source formats, invoke importers, or reconstruct how an asset was cooked.

Use ordinary .NET/MSBuild inputs and outputs for cooking. Declare source files, relevant options, adapter/tool version, schema version, and target profile as the inputs to each output. Up-to-date checks skip current outputs and rebuild affected outputs when a declared input or output changes. A change to an importer/tool version invalidates its build outputs but does not by itself change runtime content identity if the final package bytes are identical. Do not persist a runtime cook graph, source fingerprints, producer receipts, or a chain of per-asset cook/publication hashes.

Deterministic cooking means identical effective inputs under the declared producer profile yield identical package bytes; it does not promise identical output across unqualified platforms or toolchains. The content build validates direct AssetId references and the schema/version requirements it can interpret. It rejects missing targets and duplicate IDs before publication. Special owners, such as scene/prefab expansion, separately reject cycles where cycles are semantically invalid. These checks do not require a serialized package-wide dependency graph.

Importers report unsupported required source features and unapproved approximations explicitly. A failed build never replaces the previous package. Source names and license/attribution data may be retained as useful metadata, but never act as runtime paths, IDs, executable instructions, or identity proofs.

## 3. Package loading, leases, and update behavior

The AssetStore opens one immutable package for its selected content set. The caller supplies its trusted absolute location and expected 32-byte package SHA-256 from the application/deployment configuration. The store verifies that checksum over the complete package before publishing the content set. The package digest detects an exact-byte mismatch; it is not a signature and does not authenticate whoever supplied both the package and expected digest.

The store validates package bounds and directory ordering, then decodes each requested asset under the selected schema-version contract. Asset references resolve by AssetId in the same content set and must satisfy the schema ID/major declared by the reference. Missing IDs, wrong types, malformed records, unsupported schema versions, corrupt bytes, and budget violations fail explicitly before an unusable asset or partial World activation is published.

`AssetLease<T>` pins the immutable package backing needed by its value. Replacing the selected package is an atomic content-set switch; outstanding leases keep the old package alive until their real consumers finish. Cancellation does not release backing storage while a decoder or consumer is still using it. Parsing, decompression, queueing, and retained storage remain bounded. CPU package readiness does not imply GPU residency, audio readiness, or a live World binding.

Assets are not independently versioned in v1. A normal save stores the AssetIds it references, not a per-asset version tuple or build history. The application decides whether its save format remains compatible with a changed content package. Missing required AssetIds or schema versions reject during load; there is no generic runtime migration catalogue. An explicitly supported StrictLocal replay may pin the single package SHA-256 used by that recording; ordinary save/load does not require byte-identical content.

## 4. EPKG2 package layout

EPKG2 is one deterministic container for one content set. The whole-package SHA-256 is supplied externally; no hash of the index, asset, cook key, or publication is repeated inside the package.

The 64-byte package header is:

| Offset / bytes | Field |
|---|---|
| 0 / 4 | ASCII `EPKG` |
| 4 / 2, 6 / 2 | Format major `2`, minor `0` |
| 8 / 8 | Total package bytes |
| 16 / 8, 24 / 8 | Profile-record offset and byte length |
| 32 / 8, 40 / 8 | Directory offset and byte length |
| 48 / 4 | Asset entry count |
| 52 / 4 | Flags, zero in v1 |
| 56 / 8 | Reserved zeros |

The single profile record is one canonical `foundation/profile` EBIN record. It applies to the package content set; packaging separate selected profiles produces separate packages.

The directory begins with a 64-byte `EIDX` header: magic at 0; directory version `1` and payload alignment `16` as UInt16 at 4/6; entry count and flags as UInt32 at 8/12 (flags zero); entry-table offset and byte length as UInt64 at 16/24; total directory bytes as UInt64 at 32; reserved zeros at 40..63. The only table in v1 is the entry table. Entries are fixed 32-byte records sorted by unsigned AssetId:

| Offset / bytes | Field |
|---|---|
| 0 / 4 | AssetId UInt32, range 1..2147483647 |
| 4 / 1 | Codec (`None = 0`, `Brotli = 1`) |
| 5 / 1, 6 / 2 | Entry flags and reserved bytes, all zero in v1 |
| 8 / 8, 16 / 8, 24 / 8 | Payload offset, stored length, decoded length |

The directory has no dependency, profile-index, name, or per-asset digest tables. Its exact length is `64 + 32 * entryCount`, at most 4194368 bytes for 131072 entries. The indexed payload is a complete canonical `native/envelope` 2.0 record with tag 1 AssetId UInt32 and tag 2 Payload Record. The payload record's schema header identifies its registered semantic schema/version. `None` requires equal stored and decoded lengths. Ranges are aligned, positive, nonoverlapping, and disjoint from the package header, profile record, and directory; reserved bytes are zero and all extents are checked before allocation.

Payloads follow the directory in ascending AssetId order, aligned to 16 with zero padding. The final package length is the aligned end of the last payload. Repacking or changing any asset changes the package SHA-256; there is no separate per-asset version. The default package ceiling is 8 GiB. Tighter input, index, decompression, and memory limits remain enforced before allocation; the package limit does not raise those limits.

## 5. Focused acceptance

Test a deterministic package against literal expected bytes/checksum; verify the same effective build inputs produce the same bytes and that a relevant input change or missing output invalidates the appropriate MSBuild target. Test package checksum mismatch and correctly checksummed malformed input separately. Reject zero, negative, out-of-range, duplicate, or content-set-external AssetIds; verify that an ID reused in another content set cannot silently retarget a reference. Test directory ordering, checked offset/range/count overflow, alignment violations, missing/wrong-schema references, decompression under/overrun, and unsupported schema versions.

Verify serial and parallel provisional allocation never overlaps; range reservation, aggregate counts, and final allocation reject overflow without wrap. Final relocation order and output must be independent of worker scheduling, and every declared reference must be rewritten. Verify published AssetIds survive rename, edit, rebuild, and unrelated insertion/deletion; deleted IDs are not reused, and no retired-ID ledger grows with ordinary history. Independently authored content-set merges reject collisions or explicitly remap every affected reference. Confirm encoded widths and package size from the actual layout rather than inferring savings from CLR field sizes.

Verify failed package validation/replacement leaves the current content set usable, previous leases remain valid across atomic replacement, and cancellation cannot release backing storage before actual consumers finish. Test save/restore using AssetIds against the selected current package; old-save conversion, independent package updates, random-access chunk verification, authenticity/signatures, and deterministic replay remain separate opt-in requirements.
