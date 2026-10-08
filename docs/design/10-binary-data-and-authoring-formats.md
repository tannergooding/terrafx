# Versioned binary data and authoring formats

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

Runtime content, input maps, scenes, clips, save/checkpoint data, and package indices use explicit versioned binary schemas. JSON, XML, and other admitted text formats are editor/import adapters into the same semantic model; none is the permanent runtime representation. [09](09-native-assets-and-source-adapters.md) owns native semantics, [05](05-owned-buffers-and-internal-collections.md) owns bytes and leases, and [27](27-state-serialization-replay-and-recovery.md) retains transactional save/replay rules.

## 1. Format boundary

Authoring adapters parse bounded input into registered typed semantic records. JSON rejects duplicate properties; XML disables DTDs and external entity/network resolution. Unknown required fields/schema versions reject. Optional inert extension data has an explicit preservation/size policy. An adapter cannot execute constructors/plugins, resolve arbitrary paths, or bypass common semantic validators.

The common cooker validates records, canonicalizes their declared field semantics, and emits binary data. Editor open/edit/save maintains the selected authoring format and identity; runtime loading uses the cooked cache/version. Text and binary recipes/digests are distinct and recorded. Switching an authoring format does not change entity/asset/schema GUIDs or silently discard unknown optional data.

Source literals use the units/references declared by their adapter/schema and convert into canonical typed records before cooking; a runtime loader does not apply the authoring conversion again. Inspector display preferences are not source-field interpretation or build-recipe inputs. Untouched typed values remain authoritative when another field is saved; viewing rounded degrees or another length unit cannot rewrite those values. [25's authoring contract](25-ui-text-and-editor-tools.md#authoring-presentation-and-conversion) owns display/commit isolation and shared typed conversion; this adds no wire kind, tag or implicit Euler representation.

Generated readers/writers come from explicit schema descriptors and stable numeric field tags. Public .NET property names are not byte offsets. Tags are never reused for another meaning. Schema-major changes require migration; schema-minor changes may add only optional tagged fields with declared defaults. Removing/changing a required field, scalar interpretation, units, or profile is not an additive minor change.

No binary codec dumps CLR padding, object graphs, process pointers, method names, or public collection backing storage. Native/GPU layouts remain separate explicit packers. Binary format efficiency and compression are workload measurements, not guaranteed merely by avoiding JSON.

Signed semantic APIs follow [02's integer policy](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries); that policy does not itself alter container integers, permanent wire-kind IDs or established tag meanings. Decode an unsigned field in its wire domain, validate its declared budget and semantic range, then perform checked conversion to `int`, `nint` or `long`. Values above the target's signed maximum reject before allocation/activation; they never wrap, clamp or become a successful negative value. Opaque identities/masks retain their declared full unsigned domain. Encode validated semantic values through the corresponding explicit wire packer. Each codec's prerequisites under [35](35-implementation-roadmap.md) record these admission rules in its accepted descriptors/golden rejection fixtures before its reader/writer is implemented; narrowing a previously shipped schema's valid range requires explicit compatibility review/versioning/migration, not an unannounced same-profile reinterpretation. Specification-level checks are not shipped-codec qualification.

### Definition-preserving authoring angles

The source-only `authoringDefinitions` extension has version 1 and bounded entries keyed by the owning document/entity GUID plus stable registered property-binding GUID. Each AuthoringAngleDefinition1 entry contains `value` (one scalar or exactly three X/Y/Z Euler values), `unit` (matching one or three exact ASCII Degrees/Turns/Radians names), and `canonicalDigest` (SHA-256 of that target's registered canonical field bytes). JSON numbers parse to finite binary32 with nearest-even rounding and serialize with round-trip scalar formatting; XML uses the same invariant scalar tokens and fields. Preserve negative zero explicitly. Unknown version/unit, duplicate binding, wrong arity/nonfinite value, unsupported property mapping or mismatched digest rejects before activation. The existing source/document and aggregate extension budgets apply; no executable expressions, global reference inference or arbitrary precision literal language is introduced.

JSON envelope fields are `version` and `entries`; each entry has `ownerId`, `bindingId`, `value`, `unit`, `canonicalDigest`. IDs are canonical GUID strings; digest is exactly 64 lowercase hex digits. Scalar value/unit are a number/string; Euler value/unit are exactly three-element arrays in X/Y/Z order. XML root `<authoringDefinitions version="1">` contains `<entry ownerId="..." bindingId="..." canonicalDigest="...">`; scalar child is `<value unit="Degrees">-0</value>`, and Euler child is `<value><X unit="Degrees">0</X><Y unit="Degrees">0</Y><Z unit="Degrees">90</Z></value>`. Mixed arity, repeated/missing axes or duplicate attributes reject. No display precision string replaces the round-trip scalar.

Registered binding metadata fixes scalar/Euler/bearing kind, axes/order/reference/sign, snap policy, canonical target fields and converter recipe version. A real edit validates definition and canonical target together and writes the entry/digest atomically; a recognized external canonical edit removes the superseded binding in that same transaction. Old tooling that preserves an inert entry but changes its target produces a digest mismatch, not permission to overwrite either side silently. Documents with no entry retain their original source-schema unit interpretation; loading them never infers authored degrees from a cooked quaternion/radian value.

Source cooking retains the definition until the explicitly registered conversion/evaluation boundary, uses [06](06-owned-geometry-and-numerics-interchange.md#definition-preserving-angles), validates the resulting existing canonical fields and records converter/recipe identity in the cook/cache manifest. Digest agreement alone is insufficient: scalar definitions must reproduce the canonical float under the locked converter, and Euler definitions must reproduce the orientation within [16](16-scenes-transforms-and-cameras.md)'s existing recomposition budget. Inconsistent bindings reject, not silently overwrite either side. A validated unchanged binding preserves its existing canonical bytes, including quaternion sign; a real edit generates both target and binding atomically. Untouched source saves preserve definitions and target bits. Recipe changes require new cook digests and explicit compatibility review. Replacing an existing source field's interpretation requires its source-schema major migration, not this optional extension or a display preference.

This is editor/source metadata, not EBIN runtime data: geometry wire kinds, profile identities, UUID assignments and EPKG2 profile-table version remain unchanged. No runtime Angle kind or implicit encoding of its 8 CLR bytes is added. Explicit typed/generated converters are rooted for selected authoring/tools artifacts; player loading consumes only canonical cooked data.

## 2. EBIN1 typed records

All integers are little-endian. Floating values use IEEE binary32 component bits with property-specific finite/signed-zero rules. GUIDs use canonical RFC byte order, not a platform-dependent raw `Guid` memory dump. Strings use length-delimited UTF-8; user text is not normalized without an explicit field contract.

Each record begins with a 64-byte header:

| Offset / bytes | Field |
|---|---|
| 0 / 4 | ASCII `EBIN` |
| 4 / 2, 6 / 2 | Container major `1`, minor `0` |
| 8 / 4, 12 / 4 | Header bytes `64`, field count |
| 16 / 16 | Schema GUID |
| 32 / 8, 40 / 8 | Total record bytes, field-table bytes |
| 48 / 4, 52 / 4 | Schema major, schema minor |
| 56 / 4, 60 / 4 | Flags zero, reserved zero bytes |

The field table immediately follows the header. It has exactly `fieldCount * 32` bytes, with one entry per tag in strictly increasing tag order:

| Offset / bytes within entry | Field |
|---|---|
| 0 / 4 | Stable field tag |
| 4 / 2, 6 / 2 | Wire kind, field flags |
| 8 / 8, 16 / 8 | Offset from record start, byte length |
| 24 / 8 | Element count |

Payload starts at the next 16-byte boundary after the table. Every field start and total record length is 16-byte aligned; padding is zero. Canonical writers place fields in tag order with only necessary alignment padding and no trailing bytes beyond final alignment. Positive field ranges are disjoint, wholly inside the record, and outside header/table/padding. Empty UTF-8/byte fields may point at the aligned payload cursor (including record end) only when permitted; fixed-size scalars cannot be empty. Tags are nonzero. Counts/products are checked before allocation or views. Known field flags must match the schema's required/optional declaration; omitting a required tag rejects regardless of incoming flags.

The permanent version-one wire-kind IDs are `UInt8 = 1`, `UInt16 = 2`, `UInt32 = 3`, `UInt64 = 4`, `Int8 = 5`, `Int16 = 6`, `Int32 = 7`, `Int64 = 8`, `Boolean = 9`, `Binary32 = 10`, `Guid = 11`, `Utf8 = 12`, `Bytes = 13`, `Vector2 = 14`, `Vector3 = 15`, `Vector4 = 16`, `Quaternion = 17`, `Matrix3x2 = 18`, `Matrix4x4 = 19`, `SpatialPosition2 = 20`, `SpatialPosition3 = 21`, `Record = 22`, `FixedArray = 23`, and `RecordArray = 24`. Boolean is one byte, exactly 0 or 1. Geometry uses the explicit binary32 component/row order; spatial strides are 16/24 bytes. Kind zero is invalid. Field flag `Required = 1` is the only admitted flag; unknown flags reject.

Scalar/geometry kinds have count one and exactly their fixed stride. UTF-8/bytes count equals byte length. Nested records have count one and a complete EBIN record. A fixed array starts with a 32-byte descriptor: sixteen zero bytes at 0, unsigned 16-bit element wire kind at 16, two reserved zeros at 18, unsigned 32-bit element stride at 20, and eight reserved zeros at 24. Element kind must be a fixed-size kind from 1 through 11 or 14 through 21 and stride must equal that kind's wire stride; no arbitrary CLR struct schema is admitted. Data immediately follows and field length is exactly `32 + count * stride`, including 32 bytes for an empty array. Nested fixed arrays/variable strings are not fixed elements; variable objects use record arrays.

A record array starts with an unsigned 64-bit count followed by `count + 1` unsigned 64-bit offsets relative to the array start. Count matches the field table. First offset is `AlignUp(8 + 8 * (count + 1), 16)`; table-to-record padding is zero. Offsets are strictly increasing for nonempty arrays and 16-byte aligned. Every range contains exactly one complete EBIN record whose declared total equals that range length. Final offset equals field length. A zero-count array is exactly 16 bytes: count zero and end offset 16. Null/empty placeholder records are not array elements.

Unsupported required wire kinds reject; optional unknown kinds may be retained as inert bytes within budget. There is no inferred language union or arbitrary type activation. Each registered schema supplies permanent field tags, kinds, cardinalities, defaults, validation, and ordering; readers do not invent them from reflection. The frozen descriptors below govern container/foundation/input records. Subsystem payload descriptors are generated from an explicitly reviewed, versioned schema manifest, not public property order or reflection; [35](35-implementation-roadmap.md) requires that manifest and independent golden fixtures before each subsystem's reader/writer is implemented. Adding a field appends a permanent tag rather than renumbering existing fields.

Profile-bearing records explicitly encode coordinate, numerical, unit/chunk, and schema identity. `SpatialPosition3` contains three signed 32-bit cells and three binary32 offsets in X/Y/Z order; canonical offset validation/carries follow [07](07-spatial-coordinates-and-indexing.md). A matrix encodes documented rows, not an implicit transpose. Runtime data with an incompatible profile rejects or undergoes an explicit bounded migration.

### Built-in schema registry

Built-in schema IDs use the frozen UUIDv8 recipe below, namespace `2ed8b750-e1a0-4b46-9000-000000000001` and exact ASCII keys in this table, independent of CLR type names. `foundation/profile` starts at schema major 2/minor 0; other built-ins start at major 1/minor 0. Numeric tags are explicit; `?` means optional/omitted for null, never an empty scalar. Required fields are encoded even when an authoring default supplies their value. Legacy UUIDv5/SHA-1 identities are migration input, not newly generated active built-ins.

| Stable key | Frozen tag: field / wire kind |
|---|---|
| `foundation/profile` | 1 CoordinateId/UInt32; 2 GeometryId/UInt32; 3 SpatialId/UInt32; 4 reserved legacy MetersPerUnit/Binary32, absent in major 2; 5 ChunkSizeExponent/UInt8; 6 SettingsDigest/Bytes; 7 RegistryDigest/Bytes; 8 KernelDigest/Bytes; 9 ProfileId/Guid; 10 MetersPerUnitExponent/Int32 |
| `foundation/frame2` | 1 WorldGuid/Guid; 2 Profile/Record; 3 Origin/SpatialPosition2; 4 Revision/UInt64; 5 Minimum/Vector2; 6 Maximum/Vector2 |
| `foundation/frame3` | Same tags as frame2, with SpatialPosition3/Vector3 |
| `native/envelope` | 1 AssetId/Guid; 2 Profile/Record; 3 Dependencies/RecordArray; 4 Payload/Record |
| `native/dependency` | 1 AssetId/Guid; 2 ContentDigest/Bytes; 3 SchemaId/Guid; 4 SchemaMajor/UInt32 |
| `registry/manifest` | 1 Schemas/RecordArray |
| `registry/schema` | 1 SchemaId/Guid; 2 Major/UInt32; 3 Minor/UInt32; 4 Fields/RecordArray |
| `registry/field` | 1 Tag/UInt32; 2 WireKind/UInt16; 3 Required/Boolean; 4 MinimumCount/UInt64; 5 MaximumCount/UInt64; 6 RuleId/Guid; 7 DefaultValue?/Record |
| `input/map` | 1 SchemaVersion/UInt32; 2 MapId/Guid; 3 Revision/UInt64; 4 InitialActiveContextMask/UInt64; 5 Actions/RecordArray; 6 Contexts/RecordArray; 7 Bindings/RecordArray |
| `input/action` | 1 ActionId/Guid; 2 Name/Utf8; 3 Kind/UInt8; 4 DownPressThreshold?/Binary32; 5 DownReleaseThreshold?/Binary32 |
| `input/context` | 1 ContextId/Guid; 2 Bit/UInt8; 3 Priority/Int32; 4 CaptureClasses/UInt8 |
| `input/binding` | 1 BindingId/Guid; 2 ActionId/Guid; 3 ContextMask/UInt64; 4 Priority/Int32; 5 Kind/UInt8; 6 Enabled/Boolean; 7 Flags/UInt8; 8 Source/Record |
| `input/button` | 1 Control/Record |
| `input/chord` | 1 Controls/RecordArray; 2 CompletionWindowMilliseconds?/UInt32 |
| `input/axis1keys` | 1 Negative/Record; 2 Positive/Record |
| `input/axis2keys` | 1 Left/Record; 2 Right/Record; 3 Down/Record; 4 Up/Record |
| `input/gamepadaxis1` | 1 PlayerSlot/UInt16; 2 Axis/UInt8; 3 Invert/Boolean; 4 Deadzone/Binary32 |
| `input/gamepadstick2` | 1 PlayerSlot/UInt16; 2 Stick/UInt8; 3 InvertY/Boolean; 4 RadialDeadzone/Binary32 |
| `input/gamepadtrigger` | 1 PlayerSlot/UInt16; 2 Trigger/UInt8; 3 Deadzone/Binary32 |
| `input/axisthreshold` | 1 Source/Record; 2 Direction/UInt8; 3 PressThreshold/Binary32; 4 ReleaseThreshold/Binary32 |
| `input/keyboardhid` | 1 UsagePage/UInt16; 2 Usage/UInt16 |
| `input/logicalkey` | 1 Name/Utf8 |
| `input/mousebutton` | 1 Button/UInt8 |
| `input/gamepadbutton` | 1 PlayerSlot/UInt16; 2 Button/UInt8 |

### Versioned name UUID recipe

`TerraFXNameUuidSha256V1` is an engine-specific deterministic UUIDv8 recipe, not UUIDv5 with an undeclared hash substitution. Input namespace bytes are the sixteen canonical RFC bytes. The name is nonempty exact ASCII, at most 1024 bytes, with no trimming, case folding, Unicode normalization or culture-dependent encoding; invalid inputs reject before generation. Hash input is the concatenation of ASCII `TerraFX.NameUuid.SHA256.v1` plus one NUL byte, namespace bytes, name-byte count as UInt32 little-endian, and name bytes. SHA-256 produces 32 bytes; take the first sixteen, set byte 6 to `(byte6 & 0x0F) | 0x80` and byte 8 to `(byte8 & 0x3F) | 0x80`, then interpret those bytes in RFC order. A .NET implementation uses explicit big-endian GUID byte APIs, not `Guid.ToByteArray()`'s legacy mixed-endian representation.

The version/variant overwrite leaves 122 hash-derived bits, not 256-bit collision resistance or proof of authenticity. Full content/provenance digests remain separate 32-byte SHA-256 values. Duplicate emitted IDs with different declared namespace/name/recipe assignments are a build/registry error before publication, never silently aliased.

Descriptor/build manifests pin the namespace, exact key and `TerraFXNameUuidSha256V1` recipe for each assigned identity; generated lookup consumes these locked assignments, not "latest hash algorithm". A future algorithm gets a new immutable recipe ID/domain separation and its own golden vectors. Existing assigned UUIDs keep their original recipe and bytes; an algorithm upgrade does not recalculate them. Intentional reassignment requires an explicit registry/schema migration covering dependent references, fresh digests and new lineage. UUIDv8 itself does not encode which hashing algorithm was chosen or supply automatic version negotiation.

| Name under the namespace above | Frozen UUIDv8 |
|---|---|
| `foundation/profile` | `3b9760ce-468a-8aab-bb89-f38db9e3bc00` |
| `foundation/frame2` | `ae928678-285e-82be-b250-cea4bb2cb7c3` |
| `foundation/frame3` | `42626aff-fc5c-864d-867c-43c666699777` |
| `input/map` | `9eeaa7c1-1da1-8a39-bf59-626f8622e255` |
| `registry/manifest` | `a842b722-20a3-8e3e-9824-39b812becd31` |
| `spatial/default/FloatChunk32V2` | `c2ddc5d8-9ee6-8676-bdef-dffd36a56d89` |

The spatial-default key is a profile assignment, not an additional EBIN schema. The active built-in registry replaces the previous UUIDv5 assignments. Legacy mappings must identify their original namespace/recipe/key and validate their original schemas before any explicit migration; arbitrary application asset/document IDs are not rewritten or required to use UUIDv8.

### Profile identity and semantic admission

Profile digests and content digests are exactly 32 bytes. `ProfileId` is the nonempty GUID in `SpatialProfileId`; registration/restoration rejects that identity with different locked manifest fields. Default uses chapter [07](07-spatial-coordinates-and-indexing.md)'s fixed GUID/settings/kernel manifest; custom unit/chunk settings must use their explicitly registered different identity. Decoding never silently overwrites a registered manifest. Frame records persist world GUID/profile, not process owner ID; restore assigns a new runtime owner. Frame extents are ordered finite displacement bounds. Envelope payloads name their registered native schema in their EBIN header; no arbitrary type activation follows. Dependencies sort by RFC GUID bytes/digest. Registry schemas sort by RFC GUID bytes, fields by tag; the registry digest covers the exact canonical `registry/manifest` EBIN bytes. `RuleId` resolves a locked validator/version with its own declared semantic/default encoding; absent validators reject. A manifest/default is data, not executable constructor code.

The major-2 foundation profile requires tag 10 within `[-30,30]` and forbids the reserved legacy tag 4. Its settings digest is SHA-256 over ASCII `TerraFX.SpatialSettings.v2` plus one NUL byte, Int32 little-endian `MetersPerUnitExponent`, and one UInt8 `ChunkSizeExponent`; no approximate floating factor or presentation label participates. Exponents and locked kernel/profile/registry identities agree with the package profile table before activation. A legacy binary32 factor is not automatically converted by rounding a logarithm or recognized as an intended SI prefix; an explicit migration supplies the target exponent and any required content conversion, records rounded results, and cannot certify unchanged StrictLocal continuation.

Input action kinds are Digital=1, Axis1=2, Axis2=3. Binding kinds are Button=1, Chord=2, Axis1Keys=3, Axis2Keys=4, GamepadAxis1=5, GamepadStick2=6, GamepadTrigger=7, AxisThreshold=8. Their `Source` schema must match exactly. Remaining closed enum choices use their displayed order in [14](14-custom-input-actions-and-text.md), starting at zero; explicit flags retain that chapter's values. Control schema GUID identifies the control kind, with no extra union representation. Null thresholds/window omit their optional fields; nonnull/defaulted thresholds and processor fields use that chapter's finite/bounded validators. Arrays retain authored order except where semantic canonical ordering is explicitly prescribed; reordering bindings cannot be used to change their ID/priority reducer.

The registry is extensible for application components and subsystem payloads, with assigned GUIDs/tags and reviewed version transitions. It is not permission for an implementation to invent a new tag order for these built-ins or ship a payload codec without its milestone's locked descriptor manifest/golden bytes.

## 3. EPKG2 binary index

`EPKG2` replaces the old JSON-index container; `EPKG1` is an import/migration source, not silently reinterpreted. None and Brotli remain the required codecs. Future codecs need a declared capability/format revision, verified implementation, bounded consumed-length behavior, and new conformance cases.

The package header is 128 bytes:

| Offset / bytes | Field |
|---|---|
| 0 / 4 | ASCII `EPKG` |
| 4 / 2, 6 / 2 | Major `2`, minor `0` |
| 8 / 8 | Total package bytes |
| 16 / 8, 24 / 8 | Index offset, index bytes |
| 32 / 4, 36 / 4 | Entry count, dependency count |
| 40 / 4, 44 / 4 | Flags zero, native-profile-table version `2` |
| 48 / 32 | SHA-256 of exact index bytes |
| 80 / 16 | Package GUID |
| 96 / 32 | Reserved zeros |

The binary index starts with a 64-byte `EIDX` header: magic at 0, version `1`/alignment `16` as unsigned 16-bit fields at 4/6, entry/dependency/profile counts as unsigned 32-bit fields at 8/12/16, diagnostic-string bytes at 20, and unsigned 64-bit offsets for entry/dependency/profile/string tables at 24/32/40/48, with total index bytes at 56. Offsets are relative to index start, aligned to 16 where applicable, range-checked, and mutually disjoint.

Each entry occupies 128 bytes and sorts by canonical asset GUID bytes: asset GUID at 0, schema GUID at 16, schema major/minor unsigned 32-bit values at 32/36, codec byte at 40 (`None = 0`, `Brotli = 1`), zero entry flags byte at 41, six reserved zeros at 42, payload offset/compressed/decoded lengths as unsigned 64-bit values at 48/56/64, dependency-first/count unsigned 32-bit values at 72/76, decoded SHA-256 at 80, profile-table index unsigned 32-bit value at 112, and twelve reserved zeros at 116. None requires equal compressed/decoded lengths. Profile index is strictly below profile count; nonempty packages therefore require at least one profile.

Each dependency occupies 64 bytes: asset GUID at 0, required content digest at 16, required schema-major unsigned 32-bit value at 48, unsigned 32-bit flags zero at 52, and eight reserved zeros at 56. Each entry's dependency slice is sorted by GUID/digest, with no duplicates. The canonical table concatenates those slices in entry order; a dependency may recur in another entry's slice. External dependencies resolve by exact ID/digest/schema through the store, never by diagnostic names.

Profile-table version 2 has 128-byte records: coordinate/geometry/spatial unsigned 32-bit IDs at 0/4/8, signed Int32 `MetersPerUnitExponent` at 12, chunk exponent byte at 16, three reserved zeros at 17, settings SHA-256 at 20, schema-registry SHA-256 at 52, numerical-kernel SHA-256 at 84, and twelve reserved zeros at 116. Built-in IDs are coordinate `LeftHandedYUpForwardZ1 = 1`, geometry `EngineGeometryBinary32Rows2 = 2` (1 remains reserved), spatial `FloatChunk32V2 = 2` (1 remains reserved for `FloatChunk32V1`). The container remains EPKG2/EIDX1; its explicit profile-table discriminator changes, not the byte extent of entries or profiles. Version 1's binary32 factor at 12 is legacy migration input, never an Int32 exponent under version 2. Records sort lexicographically by complete bytes and cannot duplicate. IDs/digests resolve through the locked manifest; unknown profiles reject rather than adopting matching storage layouts. The table deduplicates semantic manifests, not application GUID aliases. After decoding an entry, its major-2 EBIN `foundation/profile` including `ProfileId` must agree with the indexed manifest and registered identity before activation.

Canonical index layout is header, entry table, dependency table, profile table, then diagnostic strings, aligned to 16 with zero padding and exact total length. A diagnostic name record is asset GUID (16 bytes), UTF-8 byte count (unsigned 32-bit), four zero reserved bytes, then bytes and zero alignment padding. Records sort by asset GUID, reference an existing entry at most once, and collectively occupy exactly the declared string-byte extent. Names are optional and never runtime paths. Empty tables have zero extent and the current aligned cursor as their offset.

Payload ranges are positive, nonoverlapping, in bounds, and disjoint from header/index. A legal zero-length schema may have an empty payload. Index and decoded payload digests both verify before activation. Reserved bytes/unknown mandatory flags/duplicate IDs/dependency cycles/incompatible profiles reject.

Default package limit remains 8 GiB; index limit is 64 MiB, with at most 131072 entries and 524288 dependency records. Byte budgets include every header/table/string/padding allocation, not just payload. Tighter untrusted-content limits in [11](11-assets-import-streaming-and-packages.md) remain mandatory and do not rise automatically with trusted package capacities.

## 4. Streaming, compression, saves, and migration

Large readers validate extents in integer arithmetic and consume owned bounded windows. A schema's fixed-stride arrays may be lazily mapped only after exact alignment/stride/lease/profile proof; otherwise readers copy/convert. Public consumers do not receive a byte pointer, BCL sequence position, or view into a disposed package.

Brotli output cannot exceed declared decoded length or aggregate decode budget. Short output, excess output, truncated/trailing invalid frames, mismatched digest, or canceled incomplete output never becomes a ready asset. Independent blocks are used only by schemas that declare their block table/digests and random-access semantics; an implementer cannot split a whole-stream codec arbitrarily.

Binary save/checkpoint/replay records reuse EBIN schemas and canonical field ordering, with [27](27-state-serialization-replay-and-recovery.md)'s generation, durable flush, atomic replacement, backup, and lineage rules. Text exports are tooling features, not the canonical strict-checkpoint hash. A JSON/XML save importer stages a new binary schema lineage and cannot promise StrictLocal continuation from unspecified textual float/order data.

Migrations are registered pure bounded semantic conversions. Legacy EPKG1 JSON indices and old text input maps validate under their original schema first, then cook EPKG2/native EBIN outputs with fresh digests. Unsupported old layouts reject explicitly. Engine public types, source formats, native binary schema versions, and package-container versions are independent compatibility dimensions.

## 5. Acceptance obligations

| ID | Required observation |
|---|---|
| BN01 | Golden EBIN/header/table/geometry/GUID/UTF-8 byte fixtures round-trip under separate readers/writers; no CLR padding/native alias assumptions |
| BN02 | Exact field tags/kinds/strides/defaults/schema versions are frozen before reader implementation; unknown required cases reject |
| BN03 | JSON/XML/native authoring of equivalent semantics cooks equivalent canonical data without format-dependent runtime APIs |
| BN04 | EPKG2 sorted binary tables, reserved bytes, range overlap/overflow, counts, digests, dependencies, and profile identity validate before allocation/activation |
| BN05 | Compression bombs, truncation, trailing invalid data, cancellation, invalid UTF-8, duplicate fields, and malicious external XML resolution fail observably |
| BN06 | Large windowed reads/mappings retain storage until actual completion and respect aggregate package/decode budgets |
| BN07 | Text/EPKG1/schema migrations create validated new versions/lineages without silently relabeling strict replay |
| BN08 | Representative content measures load latency, allocations, cooked/compressed bytes, and edit/cook round trips against the text baseline |
| BN09 | Independent UUID recipe fixtures cover exact RFC namespace byte order, case/length/domain separation, all built-in keys, version 8/variant bits and the frozen defaults. Invalid names/colliding assignments/refused unknown recipes fail before publication. Encode signed exponent endpoints/tag 10/major 2 and profile-table version 2; inject old factor/tag/discriminator/identity bytes | SHA-256 UUIDv8 matches locked golden bytes across implementations/cultures; evolving a recipe never silently regenerates assigned IDs. UUIDs are not full content digests/authenticators. Legacy UUIDv5/profile manifests require explicit bounded registry/content migration and new lineage; no reused tag 4, inferred exponent, mixed profile interpretation or relabeled StrictLocal state |
| BN10 | Degree/turn/radian scalar and Euler source envelopes survive JSON/XML edit/save/cook, signed zero and untouched-display/other-property edits. Reject duplicate/stale bindings, wrong unit/arity/version and changed canonical digest; migrate old source interpretation only explicitly. Runtime bytes contain existing canonical fields, never raw Angle/editor envelopes |
