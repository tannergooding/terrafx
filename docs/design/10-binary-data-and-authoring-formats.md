# Versioned binary data and authoring formats

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

Runtime content, input maps, scenes, clips, save data, and package directories use explicit versioned binary schemas. JSON, XML, and other admitted text formats are editor/import adapters into the same semantic model; none is the permanent runtime representation. [09](09-native-assets-and-source-adapters.md) owns native semantics, [05](05-owned-buffers-and-internal-collections.md) owns bytes and leases, and [27](27-state-serialization-replay-and-recovery.md) owns transactional save behavior. Deterministic replay is a separate optional feature, not a requirement of ordinary saves.

## 1. Format boundary

Authoring adapters parse bounded input into registered typed semantic records. JSON rejects duplicate properties; XML disables DTDs and external entity/network resolution. Unknown fields/schema versions reject unless the selected schema explicitly declares that field optional. An adapter cannot execute constructors/plugins, resolve arbitrary paths, or bypass common semantic validators.

The content build validates records, canonicalizes their declared field semantics, and emits the deterministic package. Editor open/edit/save maintains the selected authoring format and stable IDs; runtime loading uses the selected package and verifies its single expected SHA-256. Build inputs belong to incremental build state, not a persisted runtime recipe/digest chain. Switching an authoring format does not change stable entity IDs, AssetIds, or schema GUIDs. Unknown, undeclared fields reject; no generic unknown-field preservation is promised.

Source literals use the units/references declared by their adapter/schema and convert into canonical typed records before cooking; a runtime loader does not apply the authoring conversion again. Inspector display preferences are not source-field interpretation or build inputs. Untouched typed values remain authoritative when another field is saved; viewing rounded degrees or another length unit cannot rewrite those values. [25's authoring contract](25-ui-text-and-editor-tools.md#authoring-presentation-and-conversion) owns display/commit isolation and shared typed conversion; this adds no wire kind, tag or implicit Euler representation.

Generated readers/writers come from explicit schema descriptors and stable numeric field tags. Public .NET property names are not byte offsets. Tags are never reused for another meaning. Schema-major changes require migration; schema-minor changes may add only optional tagged fields with declared defaults. Removing/changing a required field, scalar interpretation, units, or profile is not an additive minor change.

No binary codec dumps CLR padding, object graphs, process pointers, method names, or public collection backing storage. Native/GPU layouts remain separate explicit packers. Binary format efficiency and compression are workload measurements, not guaranteed merely by avoiding JSON.

Signed semantic APIs follow [02's integer policy](02-dotnet-api-memory-and-ownership.md#integer-types-and-boundaries); that policy does not itself alter container integers, permanent wire-kind IDs or established tag meanings. Decode an unsigned field in its wire domain, validate its declared budget and semantic range, then perform checked conversion to `int`, `nint` or `long`. Values above the target's signed maximum reject before allocation/activation; they never wrap, clamp or become a successful negative value. Opaque identities/masks retain their declared full unsigned domain. Encode validated semantic values through the corresponding explicit wire packer. Each codec's prerequisites under [35](35-implementation-roadmap.md) record these admission rules in its accepted descriptors/golden rejection fixtures before its reader/writer is implemented; narrowing a previously shipped schema's valid range requires explicit compatibility review/versioning/migration, not an unannounced same-profile reinterpretation. Specification-level checks are not shipped-codec qualification.

### Definition-preserving authoring angles

The source-only `authoringDefinitions` extension has version 1 and bounded entries keyed by the owning document/entity GUID plus stable registered property-binding GUID. Each AuthoringAngleDefinition1 entry contains `value` (one scalar or exactly three X/Y/Z Euler values), `unit` (matching one or three exact ASCII Degrees/Turns/Radians names), and `canonicalDigest` (SHA-256 of that target's registered canonical field bytes). JSON numbers parse to finite binary32 with nearest-even rounding and serialize with round-trip scalar formatting; XML uses the same invariant scalar tokens and fields. Preserve negative zero explicitly. Unknown version/unit, duplicate binding, wrong arity/nonfinite value, unsupported property mapping or mismatched digest rejects before activation. The existing source/document and aggregate extension budgets apply; no executable expressions, global reference inference or arbitrary precision literal language is introduced.

JSON envelope fields are `version` and `entries`; each entry has `ownerId`, `bindingId`, `value`, `unit`, `canonicalDigest`. IDs are canonical GUID strings; digest is exactly 64 lowercase hex digits. Scalar value/unit are a number/string; Euler value/unit are exactly three-element arrays in X/Y/Z order. XML root `<authoringDefinitions version="1">` contains `<entry ownerId="..." bindingId="..." canonicalDigest="...">`; scalar child is `<value unit="Degrees">-0</value>`, and Euler child is `<value><X unit="Degrees">0</X><Y unit="Degrees">0</Y><Z unit="Degrees">90</Z></value>`. Mixed arity, repeated/missing axes or duplicate attributes reject. No display precision string replaces the round-trip scalar.

Registered binding metadata fixes scalar/Euler/bearing kind, axes/order/reference/sign, snap policy, canonical target fields and converter recipe version. A real edit validates definition and canonical target together and writes the entry/digest atomically; a recognized external canonical edit removes the superseded binding in that same transaction. Old tooling that preserves an inert entry but changes its target produces a digest mismatch, not permission to overwrite either side silently. Documents with no entry retain their original source-schema unit interpretation; loading them never infers authored degrees from a cooked quaternion/radian value.

Source cooking retains the definition until the explicitly selected conversion/evaluation boundary, uses [06](06-owned-geometry-and-numerics-interchange.md#definition-preserving-angles), and validates the resulting canonical fields. Scalar definitions must reproduce the canonical float under the selected converter, and Euler definitions must reproduce the orientation within [16](16-scenes-transforms-and-cameras.md)'s existing recomposition budget. Inconsistent bindings reject, not silently overwrite either side. A validated unchanged binding preserves its existing canonical bytes, including quaternion sign; a real edit generates both target and binding atomically. Untouched source saves preserve definitions and target bits. A changed field interpretation requires a new schema version; conversion of older data is an explicit application/build action.

This is editor/source metadata, not EBIN runtime data: geometry wire kinds, profile identities and schema assignments remain explicit. No runtime Angle kind or implicit encoding of its 8 CLR bytes is added. Explicit typed/generated converters are rooted for selected authoring/build artifacts; player loading consumes only canonical package data.

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

Unsupported required wire kinds reject; optional unknown kinds may be retained as inert bytes within budget. There is no inferred language union or arbitrary type activation. Each registered schema supplies permanent field tags, kinds, cardinalities, defaults, validation, and ordering; readers do not invent them from reflection. The frozen descriptors below govern container/foundation/input records. Subsystem payload descriptors are build-time source compiled into the application's closed schema/codec registrations, not package manifests or reflection; [35](35-implementation-roadmap.md) requires those declarations and independent golden tests before each subsystem's reader/writer is implemented. Adding a field appends a permanent tag rather than renumbering existing fields.

Profile-bearing records explicitly encode coordinate, numerical, unit/chunk, and schema identity. `SpatialPosition3` contains three signed 32-bit cells and three binary32 offsets in X/Y/Z order; canonical offset validation/carries follow [07](07-spatial-coordinates-and-indexing.md). A matrix encodes documented rows, not an implicit transpose. Runtime data with an incompatible profile rejects or undergoes an explicit bounded migration.

### M03 structural and payload writing

The Core structural writer accepts one borrowed `EbinRecordWriteView`; its `Measure`, `Prepare`, `Commit` and `Write` operations share the recursive canonical scanner and explicit `EbinReadLimits`. `Prepare` packs into private caller scratch, then validates the complete candidate; `Commit` validates exactly one complete canonical candidate before overlap/capacity checks and copies only the record prefix; `Write` composes both phases. Failure reports zero bytes and does not mutate final destination. `Prepare` scratch may contain partial output only after packing begins; it is not a rollback-safe final destination.

`EbinPayloadWriter` scalar and array methods write payload bytes only: no field tag/flags/count, record header, outer alignment padding or schema admission. The 19 scalar overloads use permanent wire kinds 1–11 and 14–21, exact component/row order and RFC-order GUID bytes; the closed 19 typed fixed-array overloads emit kind-23's exact 32-byte descriptor followed by tightly packed wire elements. Empty typed arrays still carry their actual kind/stride descriptor. The raw fixed-array overload accepts only the closed fixed-size wire-kind set, exact `count * stride` bytes and Boolean values 00/01; unsupported kinds are `InvalidRecord`/`fixedarray-stride`, not opaque extensions. None of these paths normalizes geometry, finite values, quaternion signs, signed zero, spatial carries or caller bytes.

Record-array `MeasureRecordArray`/`WriteRecordArray` accept borrowed slices into caller storage; each consumed slice must contain exactly one complete canonical child with declared total equal to slice length. One fresh shared scan state counts the outer elements and every emitted child occurrence (including repeated slices), with unsupported-required-kind detection. The first child offset is `AlignUp(8 + 8 * (count + 1), 16)`; emit count, offsets, zero table padding and contiguous child records. Empty output is exactly 16 bytes with count 0 and final offset 16.

A standalone payload has no fictitious parent record or field: scan at depth zero, direct child records at depth one, count outer elements and all nested physical records/fields/elements, and bound payload length by `MaximumRecordBytes`. This does not reserve parent counters/depth/header bytes. Helper success is not proof that a later containing record fits; structural `Measure`/`Prepare`/`Commit` independently validate the complete actual root under fresh shared root accounting.

Payload helpers complete full deterministic input/budget preflight before rejecting overlap or short workspace. Overlap uses the whole supplied destination span, physical typed-source extents, the record-array slice-table bytes and every consumed child range; unused child storage need not be disjoint. On any returned failure, output bytes are zero and the entire helper destination is unchanged. Scanner failures preserve encoded schema/tag/category/offset/claim/limit and source; incomplete encoded values use `incomplete-encoded-value` only when the scanner has no earlier malformed/unsupported diagnostic, without inventing offset zero. Description faults remain `Construction`, encoded-byte faults `EncodedInput`, and candidate faults `Candidate`. Capacity diagnostics use `workspace-too-small` or structural final `destination-too-small`, with actual capacity as Claim and required extent as Limit. Helpers never return `NeedMoreData` or `DestinationTooSmall`; overlap throws `ArgumentException` after complete content preflight and before capacity refusal. Preserve scanner first-provable ordering and M02 precedence; do not impose a global malformed-before-unsupported rule.

All span backing follows [05](05-owned-buffers-and-internal-collections.md)'s borrowing rule: inputs stay unchanged and the destination remains live/exclusively borrowed through the synchronous call. `scoped` does not establish those alias/lifetime properties. These helpers retain no bytes or charges and provide no native producer metadata-only planning; an admitted/native encoder must first capture inputs under its independent read interests, then use the actual complete-root writer within its separately reserved simultaneous workspace/output budget.

### Built-in schema registry

Built-in schema IDs use the frozen UUIDv8 recipe below, namespace `2ed8b750-e1a0-4b46-9000-000000000001` and exact ASCII keys in this table, independent of CLR type names. `foundation/profile` starts at schema major 2/minor 0; other built-ins start at major 1/minor 0. Numeric tags are explicit; `?` means optional/omitted for null, never an empty scalar. Required fields are encoded even when an authoring default supplies their value. Legacy UUIDv5/SHA-1 identities are migration input, not newly generated active built-ins.

| Stable key | Frozen tag: field / wire kind |
|---|---|
| `foundation/profile` | 1 CoordinateId/UInt32; 2 GeometryId/UInt32; 3 SpatialId/UInt32; 4 reserved legacy MetersPerUnit/Binary32, absent in major 2; 5 ChunkSizeExponent/UInt8; 6 SettingsDigest/Bytes; 7 RegistryDigest/Bytes; 8 KernelDigest/Bytes; 9 ProfileId/Guid; 10 MetersPerUnitExponent/Int32 |
| `foundation/frame2` | 1 WorldGuid/Guid; 2 Profile/Record; 3 Origin/SpatialPosition2; 4 Revision/UInt64; 5 Minimum/Vector2; 6 Maximum/Vector2 |
| `foundation/frame3` | Same tags as frame2, with SpatialPosition3/Vector3 |
| `native/envelope` | 1 AssetId/UInt32; 2 Payload/Record |
| `native/asset-reference` | 1 AssetId/UInt32; 2 SchemaId/Guid; 3 SchemaMajor/UInt32 |
| `registry/manifest` | 1 Schemas/RecordArray |
| `registry/schema` | 1 SchemaId/Guid; 2 Major/UInt32; 3 Minor/UInt32; 4 Fields/RecordArray |
| `registry/field` | 1 Tag/UInt32; 2 WireKind/UInt16; 3 Required/Boolean; 4 MinimumCount/UInt64; 5 MaximumCount/UInt64 |
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

Each functional descriptor row contains the namespace, exact key and `TerraFXNameUuidSha256V1` recipe for its assigned identity. Generated lookup derives the ID from that row and never selects a "latest hash algorithm"; do not maintain a second assignment list that repeats keys and metadata. A future algorithm gets a new immutable recipe ID/domain separation and its own golden vectors. Existing assigned UUIDs keep their original recipe and bytes; an algorithm upgrade does not recalculate them. Intentional reassignment requires an explicit registry/schema migration covering dependent references, fresh digests and new lineage. UUIDv8 itself does not encode which hashing algorithm was chosen or supply automatic version negotiation.

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

Profile digests and content digests are exactly 32 bytes. `ProfileId` is the nonempty GUID in `SpatialProfileId`; registration/restoration rejects that identity with different locked manifest fields. Default uses chapter [07](07-spatial-coordinates-and-indexing.md)'s fixed GUID/settings/kernel manifest; custom unit/chunk settings must use their explicitly registered different identity. Decoding never silently overwrites a registered manifest. Frame records persist world GUID/profile, not process owner ID; restore assigns a new runtime owner. Frame extents are ordered finite displacement bounds. Envelope payloads name their registered native schema in their EBIN header; no arbitrary type activation follows. Dependencies sort by RFC GUID bytes/digest. Registry schemas sort by RFC GUID bytes, fields by tag; the registry digest covers the exact canonical `registry/manifest` EBIN bytes. A schema version owns its fixed semantics and required set; registry data does not select or execute validator identities.

The major-2 foundation profile requires tag 10 within `[-30,30]` and forbids the reserved legacy tag 4. Its settings digest is SHA-256 over ASCII `TerraFX.SpatialSettings.v2` plus one NUL byte, Int32 little-endian `MetersPerUnitExponent`, and one UInt8 `ChunkSizeExponent`; no approximate floating factor or presentation label participates. Exponents and locked kernel/profile/registry identities agree with the package profile table before activation. A legacy binary32 factor is not automatically converted by rounding a logarithm or recognized as an intended SI prefix; an explicit migration supplies the target exponent and any required content conversion, records rounded results, and cannot certify unchanged StrictLocal continuation.

Input action kinds are Digital=1, Axis1=2, Axis2=3. Binding kinds are Button=1, Chord=2, Axis1Keys=3, Axis2Keys=4, GamepadAxis1=5, GamepadStick2=6, GamepadTrigger=7, AxisThreshold=8. Their `Source` schema must match exactly. Remaining closed enum choices use their displayed order in [14](14-custom-input-actions-and-text.md), starting at zero; explicit flags retain that chapter's values. Control schema GUID identifies the control kind, with no extra union representation. Null thresholds/window omit their optional fields; nonnull/defaulted thresholds and processor fields use that chapter's finite/bounded validators. Arrays retain authored order except where semantic canonical ordering is explicitly prescribed; reordering bindings cannot be used to change their ID/priority reducer.

Fixed fields use EBIN's 32-bit tags. Optional components use the reserved extension set and signed 64-bit IDs: positive IDs are reserved for built-ins, negative IDs for user-defined extensions, and zero is invalid. The owning schema version fixes semantics and any required-ID set; optional instances carry no requiredness bit, and a later version may promote an ID to required. Unknown optional payloads remain inert and preservable. This is not permission for an implementation to invent a new tag order for fixed built-ins or ship a payload codec without its milestone's locked schema contract.

## 2.1 M02 foundation-codec contract

> **Status:** This section is the accepted normative M02 foundation-codec contract. The static schema, kernel, digest-scope and persistence-binding decisions below are fixed. Candidate vectors in P7 remain candidates until their literal bytes and semantic facts are independently derived and reviewed; that review accepts fixture facts, not a separate implementation. This contract does not establish M02 completion. The EBIN1 layout/wire kinds, built-in keys/UUID recipe, foundation profile major 2/tags, frame tags and default profile assignment remain unchanged.

### P1. Selected unit and boundaries

The M02 unit is the EBIN1 bounded scalar/geometry packers and record/table/array machinery, together with the eight-schema foundation closure below. It includes canonical registry/kernel manifest construction, major-2 profile records, dimensional frame records, profile admission and private persistence-binding checks. Primitive support covers all permanent kinds 1 through 24; schema admission remains separate from structural packing. In particular, raw Binary32/geometry packing preserves component bits, including NaN payloads and signed zero, where the schema version admits them. Frame positions/extents are finite-domain fields, not that raw interchange exemption.

Excluded are native asset/envelope and input payload codecs; application/component schemas; EPKG2 readers and package activation; text/source adapters and Angle definition envelopes; scenes, physics/audio state and strict-checkpoint codecs; and live-world save/restore/rebase transactions. Primitive GUID and geometry coverage does not qualify those consumers. Angle's CLR representation, owner IDs, clocks, buffer leases, pool state, callables, spatial bounds/poses/rays and arbitrary managed structures acquire no new schema or implicit byte encoding. Package compatibility remains an M03 integration obligation, not evidence that M02 implements packages.

The M02 admission profile limits one outer record, including all contained headers/tables/payload/padding, to 65536 bytes and each record to 64 field entries. [27](27-state-serialization-replay-and-recovery.md#5-savereplay-admission-limits)'s nesting limit 64, array-count limit 1048576 and UTF-8 length limit 1 MiB remain upper bounds; the smaller outer-record budget also applies. Copies and retained storage charge the caller's frozen storage budget under [05](05-owned-buffers-and-internal-collections.md). These are explicit selected-unit limits, not replacements for general asset/package budgets or promises to admit every structurally representable EBIN record. Reject an over-budget declared extent before waiting for its payload or allocating from its counts. Check unsigned products, sums, alignment and ranges first, then signed/native representability before conversion or views.

This contract adds no public API. Private wire records and frozen codec context carry fields that Core values do not contain. Any use of [30](30-public-api-review-surface.md#6-assets-schema-codecs-and-cooking)'s existing `IBinarySchemaCodec<T>` follows its atomic output/status and scoped-span contract; this does not add a Core dependency on `TerraFX.Assets` or a context-free `IBinarySchemaCodec<SpatialFrame2/3>`. Internal packers supply the foundation without a new public facade.

### P2. Static schema catalogue and extension representation

The selected immutable descriptor catalogue is `M02FoundationDescriptors1`: eight schema rows and thirty-eight fixed-field rows. Schema records use minor zero; `foundation/profile` uses major 2, the other seven schemas use major 1. The schema GUIDs and the existing `TerraFXNameUuidSha256V1` assignments remain as shown. A schema version fixes its complete required fixed-field set and its semantics; the registry records only wire shape and cardinality, not validator identities, rule recipes or defaults.

| Schema key | Schema GUID | Assignment status |
|---|---|---|
| `registry/manifest` | `a842b722-20a3-8e3e-9824-39b812becd31` | Frozen |
| `registry/schema` | `07902135-1633-8b5e-86e4-436850ca1149` | Frozen assignment |
| `registry/field` | `4ec03903-a0b1-8425-8f43-0e905f7f0764` | Frozen assignment |
| `foundation/profile` | `3b9760ce-468a-8aab-bb89-f38db9e3bc00` | Frozen |
| `foundation/frame2` | `ae928678-285e-82be-b250-cea4bb2cb7c3` | Frozen |
| `foundation/frame3` | `42626aff-fc5c-864d-867c-43c666699777` | Frozen |
| `kernel/manifest` | `b4bbf3fb-503c-8a6d-8687-878cbc3cc9d6` | Accepted assignment |
| `kernel/recipe` | `f7a613f1-1c19-8c64-9448-31be7d694db2` | Accepted assignment |

The fixed-field inventory is literal. Count intervals describe a present field's element count, not tag multiplicity. Each fixed tag occurs at most once; `Required` describes only that schema version's fixed field. Bytes counts are byte counts.

| Schema | Tag / field | Wire kind / ID | Count |
|---|---|---|---|
| `registry/manifest` | 1 Schemas | RecordArray / 24 | [1, 1048576] |
| `registry/schema` | 1 SchemaId | Guid / 11 | [1, 1] |
| `registry/schema` | 2 Major | UInt32 / 3 | [1, 1] |
| `registry/schema` | 3 Minor | UInt32 / 3 | [1, 1] |
| `registry/schema` | 4 Fields | RecordArray / 24 | [1, 1048576] |
| `registry/field` | 1 Tag | UInt32 / 3 | [1, 1] |
| `registry/field` | 2 WireKind | UInt16 / 2 | [1, 1] |
| `registry/field` | 3 Required | Boolean / 9 | [1, 1] |
| `registry/field` | 4 MinimumCount | UInt64 / 4 | [1, 1] |
| `registry/field` | 5 MaximumCount | UInt64 / 4 | [1, 1] |
| `foundation/profile` | 1 CoordinateId | UInt32 / 3 | [1, 1] |
| `foundation/profile` | 2 GeometryId | UInt32 / 3 | [1, 1] |
| `foundation/profile` | 3 SpatialId | UInt32 / 3 | [1, 1] |
| `foundation/profile` | 5 ChunkSizeExponent | UInt8 / 1 | [1, 1] |
| `foundation/profile` | 6 SettingsDigest | Bytes / 13 | [32, 32] |
| `foundation/profile` | 7 RegistryDigest | Bytes / 13 | [32, 32] |
| `foundation/profile` | 8 KernelDigest | Bytes / 13 | [32, 32] |
| `foundation/profile` | 9 ProfileId | Guid / 11 | [1, 1] |
| `foundation/profile` | 10 MetersPerUnitExponent | Int32 / 7 | [1, 1] |
| `foundation/frame2` | 1 WorldGuid | Guid / 11 | [1, 1] |
| `foundation/frame2` | 2 Profile | Record / 22 | [1, 1] |
| `foundation/frame2` | 3 Origin | SpatialPosition2 / 20 | [1, 1] |
| `foundation/frame2` | 4 Revision | UInt64 / 4 | [1, 1] |
| `foundation/frame2` | 5 Minimum | Vector2 / 14 | [1, 1] |
| `foundation/frame2` | 6 Maximum | Vector2 / 14 | [1, 1] |
| `foundation/frame3` | 1 WorldGuid | Guid / 11 | [1, 1] |
| `foundation/frame3` | 2 Profile | Record / 22 | [1, 1] |
| `foundation/frame3` | 3 Origin | SpatialPosition3 / 21 | [1, 1] |
| `foundation/frame3` | 4 Revision | UInt64 / 4 | [1, 1] |
| `foundation/frame3` | 5 Minimum | Vector3 / 15 | [1, 1] |
| `foundation/frame3` | 6 Maximum | Vector3 / 15 | [1, 1] |
| `kernel/manifest` | 1 CoordinateId | UInt32 / 3 | [1, 1] |
| `kernel/manifest` | 2 GeometryId | UInt32 / 3 | [1, 1] |
| `kernel/manifest` | 3 SpatialId | UInt32 / 3 | [1, 1] |
| `kernel/manifest` | 4 Recipes | RecordArray / 24 | [1, 1048576] |
| `kernel/recipe` | 1 RecipeId | Guid / 11 | [1, 1] |
| `kernel/recipe` | 2 Version | UInt32 / 3 | [1, 1] |
| `kernel/recipe` | 3 Parameters | Bytes / 13 | [0, 1024] |

These foundation schemas have closed fixed-field sets. Every fixed field must match the selected schema-version descriptor, and unknown fixed tags reject. The reserved EBIN extension field is separately admitted on `foundation/profile`, `foundation/frame2` and `foundation/frame3`. When present, it is a nonempty array of fixed `ebin/extension` records containing a nonzero signed 64-bit identifier and opaque byte payload; identifiers are unique and strictly increasing under signed comparison. These entries do not alter the fixed descriptor catalogue or make an unknown fixed tag admissible. The profile/frame staged values preserve validated extension payloads for recapture. Profile tag 4 remains reserved and forbidden in major 2. Fixed scalar/geometry widths, array descriptors and field flags remain as defined by EBIN1. RecordArray element schemas are explicitly `registry/schema`, `registry/field` and `kernel/recipe` for their respective fields, never an inferred heterogeneous CLR collection.

**Defaults:** this closure has no serialized defaults or `DefaultValue` descriptor. Required profile fields are written even when configuration used defaults: coordinate/geometry/spatial codes `(1,2,2)`, exponents `(0,12)` and the existing Default profile GUID. Digests are derived/verified, never empty/zero placeholders. Frames have no implicit world, origin, revision or extent defaults; a new owner's initial revision 1 is an owner operation, not a decoder repair.

**Bootstrap:** seed the EBIN structural reader, all eight fixed schema descriptors and the P4 kernel recipe catalogue from trusted immutable inputs. No external registry can install schema behavior, authorize fixed schema IDs, or select the catalogue used to validate itself. Descriptors contain schema/field identities, versions and cardinalities, but no profile record, actual registry/kernel/settings digest, runtime owner or executable validator. Fixed schema semantics belong to the owning schema version.

### P3. Schema-version admission and semantics

First validate EBIN structure, bounded extents, field-table ordering, fixed field presence/flags/kinds/counts and record-array element structure. EBIN's structural reader enforces Boolean payloads as exactly 0 or 1. Then validate known nested records and local values in tag order. Finally perform cross-field/context checks only after their inputs are admitted. This ordering is a fixed behavior of the schema version, not a serialized validator graph.

- `registry/manifest` and its nested `registry/schema` / `registry/field` records must be canonical, RFC-GUID/tag sorted, unique and an exact match for the seeded eight-schema/38-field catalogue. Fixed tags must be nonzero and cannot use the reserved extension tag. Field kinds are permanent EBIN IDs 1 through 24; count bounds are ordered and fixed-width scalar/geometry/Record fields have count one. Missing, added or reinterpreted catalogue fields are invalid.
- `foundation/profile` admits only coordinate/geometry/spatial codes `(1,2,2)`, chunk exponent [8,16], unit exponent [-30,30], three 32-byte digests and a nonempty registered ProfileId. The full tuple must match the immutable binding and any enclosing package tuple. Unknown profile codes or identities are unsupported; malformed ranges, mismatched registered bindings and a use of reserved tag 4 are invalid. Decoding never registers or rebinds an identity.
- `foundation/frame2` and `foundation/frame3` require a nonempty WorldGuid matching the explicit persistence binding, a complete nested profile matching that binding, a positive revision representable by `long`, finite ordered extents and dimension-correct canonical positions. Owner-specific extent policy is checked before projection. Reads do not normalize offsets, allocate an OwnerId, register profiles or activate a world.
- `kernel/manifest` requires codes `(1,2,2)` and exactly the nine P4 recipes in RFC RecipeId order. A known recipe with an unsupported positive version is unsupported; zero/out-of-range versions and mismatched parameters are invalid. Recipe IDs and parameter bytes remain meaningful kernel semantics, distinct from field validators.

An unknown schema/version, unknown fixed tag, unknown profile identity/code or unsupported kernel recipe/version is `UnsupportedSchema`; malformed known data is `InvalidRecord`. Diagnostics identify the schema, physical tag, category and relevant offset/range; they do not carry a per-field RuleId. Usage/ownership misuse throws under [02](02-dotnet-api-memory-and-ownership.md)/[03](03-shared-conventions-and-clocks.md), not a successful default. Writes validate ownership, the complete schema/context and checked output extent before destination capacity; an invalid value is not hidden by DestinationTooSmall. No output write or externally visible admission precedes those checks.
### P4. Kernel semantic manifest

The selected semantic kernel manifest is `M02FoundationKernel1`. Its canonical `kernel/manifest` 1.0 has required codes `(1,2,2)` and nine `kernel/recipe` 1.0 records. Each row defines an immutable semantic recipe, not a hash of a CLR method name. IDs use the same frozen name recipe/namespace; all listed IDs and parameter vectors are assigned by this contract. Parameter payloads are the exact hexadecimal byte strings shown, without an internal header, count, terminator or padding. Their EBIN Bytes fields and containing records still receive normal EBIN alignment padding.

| Exact recipe key / RecipeId | Parameter bytes | Meaning and owner contract |
|---|---|---|
| `kernel/foundation/binary32/v1` / `93ec20f5-b161-8d79-aafb-8ec1438c794f` | `0000` | Two UInt8 codes: rounding 0 = nearest-even; subnormal mode 0 = gradual underflow. [08](08-binary32-kernels-and-predicates.md) requires the subnormal-preserving baseline, not flush-to-zero. |
| `kernel/foundation/geometry-rows2/v1` / `36bf491c-f7b6-88fe-a931-31b03a8b40cf` | Empty | [03](03-shared-conventions-and-clocks.md#transform-notation-and-order)/[06](06-owned-geometry-and-numerics-interchange.md): X/Y/Z/W component and row order, LH algebra, row transforms, Hamilton Multiply/Compose order, explicit bit-preserving BCL interchange and raw IEEE/equality semantics. No transpose, basis reflection or quaternion-sign canonicalization. |
| `kernel/foundation/normalize/v1` / `fe722d22-1099-8438-8531-eb330c3b3589` | `0100000020a10700` | UInt32 numerator 1, denominator 500000: exact unit-error ceiling 2e-6. [08 section 2](08-binary32-kernels-and-predicates.md#2-finite-normalization-and-inverses)'s max-component scaling, stable component-order Binary32 reduction/sqrt/division and signed-zero preservation; zero/nonfinite source refuses. |
| `kernel/foundation/quaternion-inverse/v1` / `c8af393c-d701-8332-bee5-ab6cb228a60e` | `2c02aa010004` | UInt16 denominator/numerator magnitude-bit bounds 556/426 and workspace cap 1024 bits. [08 section 2](08-binary32-kernels-and-predicates.md#2-finite-normalization-and-inverses)'s certified candidate or exact rational inverse, final nearest-even representability and conjugated zero signs; a nonfinite candidate alone is not proof of failure. |
| `kernel/foundation/matrix-inverse/v1` / `dab554c7-5762-8b00-9ba3-910a68157c96` | `a086010020a10700` | UInt32 exact residual multipliers 100000 and 500000. [08 section 2](08-binary32-kernels-and-predicates.md#2-finite-normalization-and-inverses): qualified finite BCL candidate; both exact-dyadic product checks; homogeneous 3x3 for Matrix3x2. An uncertain predicate needs bounded exact evaluation, not guessed success. |
| `kernel/foundation/view-frame/v1` / `408ce437-ab29-85de-93b6-6ef24c8ea1e3` | `0100000020a10700` | UInt32 error ratio 1/500000. [08 section 3](08-binary32-kernels-and-predicates.md#3-proper-camera-frames)'s exact source D/cross degeneracy, Scaled32/normalization, row basis and once-rounded exact translation dot; no angular epsilon or required FP64. |
| `kernel/foundation/spatial-canonical/v1` / `fbfbcfda-0497-8115-8e79-ed024525aa80` | `00000080ffffff7f01` | Int32 cell endpoints -2147483648/2147483647 and UInt8 positive-zero policy 1. [07](07-spatial-coordinates-and-indexing.md#closed-geometry-and-unique-ownership)'s exact dyadic floor/carry, [0,C) persistent offsets, checked rounded carry and refusal outside the cell domain; closed geometry remains [0,C]. |
| `kernel/foundation/spatial-decimal/v1` / `aa5cd1e7-05a4-8630-b91b-bf83420da8f6` | `000403e2ffffff1e000000` | UInt16 workspace 1024 bits, UInt8 maximum absolute length degree 3, Int32 unit-exponent endpoints -30/30. [07](07-spatial-coordinates-and-indexing.md#1-physical-units-and-coordinate-values)'s exact rational decimal scaling (maximum power 180), one nearest-even destination rounding and checked position carries. No approximate floating unit factor. This records the shared semantic rule, not completed physics/density adapters. |
| `kernel/foundation/nearby-displacement/v1` / `fbe9d96e-0119-8d57-b9ff-903e3b0fcc2c` | `4000` | UInt16 signed cell-difference width 64. [07 section 2](07-spatial-coordinates-and-indexing.md#2-precision-statements-and-nearby-arithmetic)'s integer comparison and prescribed same/adjacent-cell expression order, then an explicitly admitted nearby frame for wider neighborhoods; no world-wide float subtraction or inferred provenance. |

The recipe/version identifies the full stated owner algorithm and refusal rules, not merely the parameters or a tolerance. Parameters make its important bounds exact digest inputs. A change to an assigned algorithm/order/zero rule cannot keep its recipe ID while updating prose. An implementation may use only the alternatives its locked owner recipe permits, and must separately qualify them.

**Proposed digest scope:** KernelDigest commits this coordinate/geometry/spatial **semantic** manifest, including its recipe identities/versions/constants. Unit/chunk selections are separate SettingsDigest inputs. It contains no profile GUID, registry/settings/kernel digest, SDK/runtime/ISA string, binary image, physics solver/audio graph manifest or application recipe. [08](08-binary32-kernels-and-predicates.md#1-algebra-and-naming) records kernels separately from runtime identity; [06](06-owned-geometry-and-numerics-interchange.md#4-arithmetic-equality-and-formatting)/[27](27-state-serialization-replay-and-recovery.md#3-replay-profile) still require exact BCL/compiler/FMA/runtime/ISA/execution profiles for StrictLocal. Matching this semantic digest does not certify native execution or bit-identical cross-runtime replay. A different semantic recipe, unsupported FMA/reduction substitution, flush-to-zero or optional binary64 profile needs a different kernel manifest/digest and profile assignment, not a relabeled Default.

The broader qualified profile identity described in [30](30-public-api-review-surface.md#6-assets-schema-codecs-and-cooking) still includes the separately recorded execution/runtime profile in its enclosing build/save/replay manifest. This contract defines only the component carried at foundation/profile tag 8; the spatial GUID/codes/settings/three-digest tuple alone is never a complete StrictLocal identity. It adds no runtime field or new interpretation to the frozen foundation tags.

### P5. Exact canonical bytes, digests and profile binding

Construct the registry operand mechanically from P2/P3, without runtime registration order:

1. For each of the thirty-eight fixed-field rows, emit `registry/field` 1.0 with tags 1 through 5 holding the literal tag, wire-kind ID, required Boolean and count bounds. All five fields have field flag Required and count one.
2. For each schema, sort those field records by numeric tag and encode its Fields RecordArray using EBIN's count/offset table. Emit `registry/schema` 1.0 with SchemaId, selected major, minor zero and that array. The profile **descriptor's** Major value is 2; its containing registry/schema record is still schema 1.0.
3. Sort schema records by unsigned lexicographic comparison of the sixteen RFC SchemaId bytes, not `Guid.CompareTo`, Guid strings, dictionary insertion or CLR member order. Emit `registry/manifest` 1.0 with that Schemas RecordArray.
4. Use the exact EBIN header/table/field flags/counts, tag-ordered payload cursor, minimum alignment and zero padding in section 2 at every level. All record lengths include final alignment. `RegistryDigest = SHA-256(canonical registry/manifest EBIN bytes)`, with **no** additional domain prefix, text conversion, omitted padding, digest slot or zeroed self-reference.

For KernelDigest, encode each P4 `kernel/recipe` 1.0 with RecipeId RFC bytes, Version UInt32 1 and its exact required Parameters Bytes field (count equals payload length). Sort records by unsigned RFC RecipeId bytes. Emit `kernel/manifest` 1.0 with codes 1/2/2 and the resulting RecordArray, using the same canonical EBIN construction. `KernelDigest = SHA-256(canonical kernel/manifest EBIN bytes)`, again without an additional prefix or mutable/runtime inputs. The new schema GUID/version supplies the operand's format identity. Neither operand contains the other operand's digest.

SettingsDigest retains the already frozen formula exactly: `SHA-256(ASCII("TerraFX.SpatialSettings.v2") || 0x00 || Int32LE(MetersPerUnitExponent) || UInt8(ChunkSizeExponent))`. Validate exponents first. Write each digest's full raw 32 bytes at profile tags 6/7/8; lowercase hex is only fixture/document display. ProfileId tag 9 is the independently assigned identity, **not** a truncation of any digest or a new UUID recipe. No digest uses the profile record itself as its operand.

Profile records embed these digests, not the manifest records themselves. Full manifest operands come from trusted immutable build/registration inputs or separately staged data validated against those inputs; tag 7/8 Bytes never contain a nested manifest or authorize one merely by its advertised digest.

The dependency order is the fixed schema contract and kernel recipe catalogue, then independent registry/kernel bytes and settings preimage, then digests, then the full profile binding, then frame bytes. Bootstrap validation may compare actual manifests to the seeded canonical operands, but may not manufacture trust by accepting a digest advertised by untrusted data.

**Registry scope and profile extension policy:** the profile's RegistryDigest identifies its selected closed schema catalogue; M02 selects exactly P2, including the two kernel descriptor schemas. It is not a hash of Core's `SpatialProfiles` dictionary or an automatically growing process/application registry. Default is bound to this exact foundation closure and the P4 semantic manifest. Registering unrelated schemas must not silently change its digest or rebind its GUID. A later selected catalogue composition with additional native/input/application descriptors produces a different RegistryDigest and needs an explicitly assigned matching profile identity and compatibility review; it cannot overwrite Default. A different catalogue is not admitted merely because all currently decoded fields happen to match. This scoped closure/extension policy is the accepted interpretation of the general registry for M02.

For other explicitly registered IDs, the codec-side immutable binding contains `(ProfileId, CoordinateId, GeometryId, SpatialId, MetersPerUnitExponent, ChunkSizeExponent, SettingsDigest, RegistryDigest, KernelDigest)`. The M02 selection admits the same baseline codes/catalogues with the registered settings; application GUID aliases are not deduplicated into Default. Before decode/projection, obtain that binding from trusted registration/build data, require Core `SpatialProfiles.TryGetSettings` to agree, and never call `Register` from the record. Core's existing ID/settings registration is necessary but does not itself prove that an external digest/catalogue was accepted. This adds no public member to `SpatialProfiles`.

The EPKG2 version-2 profile-table projection, when implemented, is precisely the same codes, exponents and three digests; it omits ProfileId as already specified. Its semantic-manifest deduplication does not license GUID rebinding. Persisted profile/frame fields must agree with both the selected registered identity and any enclosing package tuple before activation.

### P6. WorldGuid source and runtime-owner boundary

The current `sources\TerraFX.Core\SpatialFrame2.cs`/`SpatialFrame3.cs` public values have OwnerId, ProfileId, Settings, Origin, positive `long` Revision and extents, but no WorldGuid. `SpatialPosition2/3` have no owner/profile metadata, and `SpatialProfiles` registers ID/settings only. These are evidence of a consumer gap, not authorization to add a public WorldGuid property or infer one from an OwnerId.

The source is [27 section 1](27-state-serialization-replay-and-recovery.md#1-save-schema-and-committed-snapshots)'s enclosing **stable world GUID**. A private immutable persistence binding supplies that nonempty GUID and the accepted profile binding to frame reading/writing. World creation/persistence ownership assigns and retains it; subsequent captures of that world use the same value. M02 fixtures explicitly supply this binding without constructing a live World. A bare Core SpatialFrame is insufficient serialization input. No process-ID lookup guess, default GUID, per-write random GUID, host ID, profile GUID or document-name hash substitutes for the world identity.

Capture pairs a Core frame with the owning world's committed persistence binding on the legal owner boundary. Check that its OwnerId equals that owner's current runtime ID and that profile/settings/revision/extents are admitted; only then copy its semantic fields and the binding's WorldGuid into the private wire record. The codec neither grants authority from a publicly fabricated frame nor retains a mutable World/borrow across I/O. A foreign/stale capture binding is ownership misuse, not a serialization fallback.

Read requires the explicitly supplied enclosing world GUID, validates tag 1 against it, and retains that field in the staged wire value. It requires the entire nested profile, including ProfileId, to match the enclosing persistence binding, and validates canonical origin, UInt64-to-positive-long revision and ordered extents before successful output. It does not normalize source offsets, allocate an OwnerId, register a profile, create a World, publish caches, or replace a live frame. `Complete` at this boundary means a usable staged persistence value, not an activated Core frame/world.

Frame fixed-field sets are closed under their selected schema version; unknown fixed tags are unsupported. The reserved extension field, when present on a frame or nested profile, is validated and retained in read-derived state so capture can preserve its opaque payloads without exposing a public extension API. A newly authored frame contains its declared fixed fields and no optional extensions unless they are retained from an admitted prior value. No public sidecar or borrowed package view is introduced.

Restore's World-owned transaction validates staged data, then obtains exactly one fresh process OwnerId from [03](03-shared-conventions-and-clocks.md#3-identities-bounds-and-numeric-safety)'s internal issuer through the reviewed Core-to-Runtime assembly route. It binds that ID to the preserved stable world GUID and projects validated frame data through the existing dimensional SpatialFrame constructor; individual frames do not allocate owners. The candidate's new runtime owner does not enter serialized bytes, while positive revision and canonical component bits survive. The transaction also applies its subsystem's extent/precision policy before activation. Reusing an already active world or applying a rebase remains that owner's coordinated transaction, not the codec's authority. Matching a world GUID alone cannot validate a foreign runtime handle. No public persistence context or owner-ID fabrication API is added.

For M02, acceptance covers the private wire codec and explicit-binding projection/capture checks with controlled issuer/binding fixtures. Actual committed capture, staged-world replacement and strict restore remain M03 and [27](27-state-serialization-replay-and-recovery.md) obligations. Failed byte/profile/registration admission before owner issuance leaves registries, owner allocation and live state untouched. Failure after a validated replacement candidate has received its owner cleans up candidate resources, burns the ID, and leaves the current World unchanged. Changing only the valid runtime OwnerId on two corresponding bound captures must produce identical frame bytes.

### P7. Canonical vectors and focused tests

The Core NUnit suite keeps five canonical SHA-256 values as compact wire-format regression checks. Tests construct the canonical records in C# and assert their exact lengths and hashes; additional tests exercise the reader, writer, profile/frame binding, UUID recipe, extension retention, malformed input, budgets and atomic-write behavior directly.

| Vector | Contract | Byte count / SHA-256 |
|---|---|---|
| G01 Registry | P2/P3, eight schemas and thirty-eight fixed fields | 14112 / `f21833596ed8bedeaa7e366505a2ade8ad349ab35f7bf0554ad2d8ce5aeda43f` |
| G02 Kernel | P4, nine version-1 recipes and exact parameters | 2192 / `494f69b9559de1c609b2c51a11454fcdc3a0be8b108fdc91f242d89b38d18105` |
| G03 Default profile | Codes (1,2,2), exponents (0,12), P5 digests and Default GUID | 544 / `5fe3ae0fbae08e4a21b064b1449934a25911176e50def5c6f3bac8239595652f` |
| G04 Frame2 | Bound world, default profile, signed-zero extent and subnormal offset | 880 / `ef8d0cf21410ebcd8926dfb52155f437a7324ff5f0dce507bb3fa565989551fe` |
| G05 Frame3 | Bound world, default profile and signed-boundary coordinates | 896 / `b72d5e5735a6586a9774ef608426d443ebf135bf678a44f2d42ff2290d72f357` |

These hashes detect canonical wire drift without pinning every generated mutation or the contents of test metadata. Prefix tests check representative incomplete inputs and their observable status/output directly; they do not hash a sequence of assertions they already perform. Add a case when it covers a distinct contract, not to preserve corpus counts or duplicate a behavior already tested end to end.

### P8. Prerequisite handoff

The M02 implementation uses ordinary .NET tests with C# test data. The five canonical hashes above guard byte-level drift; focused NUnit cases cover the behavior that hashes cannot express, including validation outcomes, binding rules, extension retention and destination atomicity. Future readers and writers add only the cases needed to exercise distinct contracts at their owning implementation boundary.

## 2.2 M03 content-schema and package binding

This section separates the M02 spatial/foundation profile from the M03 asset schema and package. `foundation/profile.RegistryDigest` and `SpatialProfileId` retain their identities; the M02 RegistryDigest and dependent profile/frame bytes reflect the fixed-field descriptor contract. M03 schema registrations are compiled into the selected application; packages do not carry or compose an executable schema catalogue and do not require a separate `ContentSchemaDigest`. The package SHA-256 covers the complete package bytes and is supplied by the application's deployment configuration. It detects a byte mismatch but does not authenticate the publisher. Adding a compiled application schema requires its statically rooted codec and a schema version; it does not change the foundation binding.

The finite M03 contract below describes the selected native payload schemas and content-set package. Schema registrations are build inputs and trusted runtime registrations, not package-provided executable metadata. Adoption does not approve production bytes, implementation, capacity/provider qualification, NativeAOT/BF12 or runtime execution. M02 section 2.1 keeps its schema identities and kernel recipes; its RegistryDigest and dependent profile/frame bytes reflect the fixed-field contract. Native envelope 2.0 contains only its scoped AssetId and payload.

M03-A registers only the compiled schema codecs needed by its selected features; there is no runtime schema-manifest import/composition or migration-chain admission. M03-A implements each real callable provider root required by its selected registrations; M03-B owns higher-owner native payload and persistence operations and their dependent observations. Tests distinguish schema metadata and package parsing from actual provider/persistence behavior; no receipt or catalogue-approval package is required.

M03 clause numbers are retained across owner chapters so every byte-affecting operand has one authoritative location; they are not new acceptance IDs. Public member construction/binding contracts are in [30](30-public-api-review-surface.md). Existing owner semantics still apply; these clauses replace incomplete/stale projections, not matching reviewed contracts.

| M03 clause | Normative owner |
|---|---|
| 2, 3, 4.1-4.2, 5-8 record inventories, 9, 10.1-10.5 | This chapter: reader boundary, functional C0/recipe/wire operands and focused C# conformance tests |
| Package identity, deterministic build, bounds, and leases | [11](11-assets-import-streaming-and-packages.md): content-set AssetIds, EPKG2/EIDX1, package checksum, loading, and replacement |
| Package and asset-loading capacities | [04](04-capacity-and-workload-profiles.md): checked file, directory, decode, and retained-backing bounds |
| 5.1-5.2 | [16](16-scenes-transforms-and-cameras.md): final root poses and typed override/reference binding |
| 7.1 / 7.2 / 7.3 | [18](18-shaders-and-material-pipeline.md) / [20](20-animation-and-deformation.md) / [21](21-2d-and-3d-rendering.md): material operands, ordered clip data and CPU extraction |
| 8.1-8.5 | [27](27-state-serialization-replay-and-recovery.md): executable checkpoint/identity/control/layout/continuation invariants |
| 10.6 | [34](34-engine-acceptance-cases.md): public lifetime/admission/resource/cleanup observations |

### M03 2 Preserved foundation and reader boundary

Core Major/Minor remain `int`. Wire major 0, major above `2147483647`, or minor above that value rejects with `InvalidRecord`, category `signed-projection-range`, consumed zero. This is recursive and prefix-aware: major is at bytes48..51 and minor52..55; an otherwise legal 52-byte prefix proving major0/high bit, or 56-byte prefix proving minor high bit, already rejects without a complete header/body. Earlier incomplete legal bits still NeedMoreData. Representable unregistered tuples are a later semantic unsupported-schema result. Public scanning does not register schemas or impose finite geometry semantics.

`ReadHeader.Complete` consumes 64, even without the body; it does not validate nested records or a semantic value. Full Read/Validate consumes one complete prefix, not trailing bytes. Enclosing owners require exact length before publication. Non-Complete outputs are unusable and consume zero. Read's action executes only after recursive structural validation; an action exception propagates, not a fabricated successful receipt. Nested views borrow validated storage and do not reset budgets.

Root depth is one; child records increase it; array containers do not. Count every physical record, field and FixedArray/RecordArray element once through one shared scan state. Root byte length already includes child bytes; do not double-charge an alleged aggregate-record-byte sum.

New typed Content models that contain a Foundation record retain its parsed Foundation value and any complete canonical bytes required by the owning record contract. M03 Content records have no extension container of their own: readers reject undeclared tags and writers emit the selected schema version's known fields. This does not discard the reserved optional-extension bytes inside an embedded M02 Foundation value. An explicit frame/fork transformation updates only its declared values and repacks canonically. This is a Content ownership wrapper, not a change to existing M02 value types or base semantics.

The explicit owner limits are:

| Use | Record bytes | Fields | Depth | Individual array elements | UTF-8 bytes | Aggregate records / fields / elements |
|---|---:|---:|---:|---:|---:|---:|
| Existing Foundation | 65536 | 64 | 64 | 1048576 | 1048576 | 1048576 / 1048576 / 1048576 |
| Content descriptors | 16777216 | 256 | 64 | 1048576 | 1048576 | 262144 / 1048576 / 4194304 |
| Trusted native asset / cook metadata | 268435456 | 256 | 64 | 1048576 | 1048576 | 4194304 / 8388608 / 16777216 |
| Complete save file | 2147483584 | 256 | 64 | 2147483584 | 1048576 | 33554432 / 134217728 / 2147483584 |

These are additionally bounded by actual owner allocations/reservations. The save limit covers the complete `state/file`, not a World payload plus uncounted headers. It is the current chapter-27 2-GiB-minus-64 limit, representable by Core's signed record-size contract; the initial structural scan is noninterruptible. Check cancellation before it and on semantic entry, retain ownership until actual unwind, and measure scan/semantic/cancellation latency separately. Fixed foundation records still use the selected M02 semantic contract inside a larger caller.

### M03 3. Complete descriptor notation and expansion

The following chapter-10 schema tables, together with exactly the seven existing declarations in [chapter 27 M03 8.5](27-state-serialization-replay-and-recovery.md#m03-85-explicit-restore-intent-and-finite-standalone-continuation), are the entire normative `C0` source. Chapter 27 remains the sole authoritative table for those declarations, their fields, assignments and continuation/command semantics; the version-owned order constraints are stated directly in M03 3.3.1. No unlisted audio/font/UI/shader-bytecode/input/solver/playback state is selected. Their real consumers require their own approved catalogue extension before implementation. Mesh/material/texture/atlas/skeleton/clip data, scenes/prefabs/cameras, primitive collision data, CPU render components and standalone committed persistence are included.

#### M03 3.1 Identity, kinds, cardinalities and annotations

For every key `k`, `S(k)` is exactly the frozen chapter-10 name UUID algorithm with namespace `2ed8b750-e1a0-4b46-9000-000000000001`, exact ASCII `k`, length-prefix/domain separator, first 16 SHA-256 bytes and specified UUIDv8/RFC variant bits. No case/Unicode/path normalization. Schema identities use this recipe; Physics validation/statistics behavior is selected by the owning schema version and has no separate algorithm assignment.

Every schema below is 1.0 except `native/envelope`, `native/scene`, `native/prefab`, `native/hierarchy` and `native/hierarchy-node`, which are 2.0. The occurrence-qualified native scene/prefab major follows the current chapter-16 owner, not the old unaccepted 1.0 rows. A reference is `(S(key), major, minor, Scope)`; Scope is Content=0 or Foundation=1. Foundation references name the unchanged M02 tuple, not a local content declaration.

Notation:

```text
N = 1048576; I = 2147483647; L = 9223372036854775807
U8/U16/U32/U64 = permanent kinds 1/2/3/4
I8/I16/I32/I64 = kinds 5/6/7/8
Bool/F/G/T/B = kinds 9/10/11/12/13
V2/V3/V4/Q/M32/M4/P2/P3 = kinds 14/15/16/17/18/19/20/21
R{...} = Record with the exact listed nested schema set
RA{...}[m..M] = RecordArray with that set and count bounds
RA@Role[m..M] = RecordArray admitting the active role bit
RA@mask(x)[m..M] = RecordArray admitting the exact literal role mask x
FA(K)[m..M] = FixedArray of permanent fixed kind K
? = optional tag, flag 0 when present; no ? means required, flag 1
[a..b] on a scalar = numeric range, not element count
{a,b,...} on an unsigned scalar = enum membership
mask(x) = allowed unsigned bits
/Ref and @Unit = explicit reference/unit metadata; omitted means None
^p/^a/^pa = PrefabEditable/Animatable/both; omitted flags zero
D(v), DM(numerators;denominator), DP(property) = authoring initialization
O(keys;sorted;unique) = fixed ordering behavior for the selected schema version
```

Scalar/geometry/Record cardinality is always [1,1]. T/B counts are byte counts. Numeric bounds are encoded as same-kind literal records, inclusive on both sides for `[a..b]`; a stated strict inequality uses the corresponding false inclusivity flag. F/geometry endpoints are exact bit values where specified. Integer endpoints are exact integers. `F>0` uses positive-zero lower bound, exclusive, and no upper bound.

Constant notation is part of the selected schema-version contract. `U8/U16/U32/U64=c` and `Bool=false/true` admit exactly that value. `I8/I16/I32/I64=c` and `F=c` require exact equality; braces with ranges enumerate the closed set. `F>=0` has positive-zero lower bound and no upper; `Vn>=0`/`Vn>0` do the same componentwise. A scalar bound on Vn applies to all lanes. `abs[...]` is an absolute-component bound. `pi32` is bits `0x40490FDB`, and `pi32/2` is `0x3FC90FDB`. Decimal defaults are exact rationals rounded once to binary32 nearest-even; `D(0)` is positive zero, while explicitly negative zero is bits `0x80000000`. No bound/default uses host-language double intermediates.

The field descriptor in [M03 3.2](10-binary-data-and-authoring-formats.md#m03-32-metadata-records) contains only fixed wire shape, cardinality, nested schema selection, authoring defaults and semantic annotations. EBIN owns structural kind/layout checks, including Boolean 0/1. The schema version and its owner contract define domain constraints such as finiteness, ranges, enums, masks, canonical geometry, ordering and record-level relationships; no package-supplied validator identity or parameter record selects them. FixedArray value constraints apply to each actual element. RecordArray elements are interpreted by their declared schema/role. Literal schemas preserve raw bits; the destination schema applies its own semantic contract when a literal is used as a bound/default.

Reserved tags are empty except `native/envelope` tags 3-5, `native/scene` tag 5, and `native/prefab` tags 2 and 7. Storage is NotComponent/restore NotApplicable unless marked Component Persistent, with the stated owner/role. All nine built-in components are Persistent and restore NotApplicable. C0 has no EntityTag or application-owned SystemState/ResourceState/AttachmentState row. Those roles have concrete fixture/application consumers, not dummy built-ins.

An array's Unit/Reference/AuthoringFlags annotation describes its admitted leaves, not its count/container. A Record annotation is meaningful only for the explicitly named semantic wrapper: Asset on `native/asset-reference` is the same owning reference as its tag 1, never a second edge; EngineArea/EngineVolume on `physics/shape-measure` names the pair's dimension. Entity remapping never infers a leaf from an unannotated Guid, a record's kind or a numerical match. Units imply no arithmetic. The owning schema version defines discriminated meaning where a shared key record serves Position versus Scale.

#### M03 3.2 Static schema registrations and wire references

Schema declarations are build-time source inputs compiled into the application's trusted codec/owner registrations. They are not serialized into every content package. A schema version defines its fixed fields and behavior; package data cannot introduce codecs, validators, imports, migrations, profile assignments, or executable code. A new application schema is available only when its owning application build registers that schema version and its codec.

The following are functional wire records because asset and authoring payloads use them directly. Other descriptor facts—schema membership, owner, role, field shape, and semantic-profile assignments—remain in the selected build's static registrations and are not an on-wire catalogue:

| Key | Complete tags |
|---|---|
| `content/schema-ref` | 1 SchemaId G/Schema; 2 Major U32[1..I]; 3 Minor U32[0..I]; 4 Scope U8{0,1} |
| `content/property-segment` | 1 Kind U8{1,2}; 2 Value U32 |
| `content/default-meters` | 1 Numerators FA(I64)[1..3]; 2 Denominator U64[1..L] |
| `content/default-material` | 1 Property U8{0..5} |

Literal schemas are exactly `content/literal/<suffix>`, tag 1 Value of the following permanent kind, raw bits. The schema-version and owning type determine any semantic domain; no field-level validator identity is serialized.

```text
u8=1, u16=2, u32=3, u64=4, i8=5, i16=6, i32=7, i64=8,
bool=9, f32=10, guid=11, utf8=12, bytes=13,
vector2=14, vector3=15, vector4=16, quaternion=17,
matrix3x2=18, matrix4x4=19, position2=20, position3=21
```

T/B literal counts are [0..1048576]; other literal counts are [1,1]. NumericLiteralSet is suffixes u8/u16/u32/u64/i8/i16/i32/i64/f32/vector2/vector3/vector4. FixedLiteralSet is all 21 except utf8/bytes. DefaultSet is all 21 plus default-meters/default-material.
#### M03 3.3 All masks and enums

```text
Role: Descriptor=1 NativeAsset=2 Component=3 EntityTag=4 SystemState=5
      AttachmentState=6 Checkpoint=7 EventPayload=8 ControlPayload=9
      Cook=10 Auxiliary=11 ResourceState=12 TypedSettings=13
RoleBit(r) = 1U << (r-1); no other bits.
Storage: NotComponent=0 Persistent=1 Transient=2
TransientRestore: NotApplicable=0 ResetDeclaredDefaults=1 ExactDerivedRehydration=2
Owner: Core=0 Assets=1 Entities=2 Scenes=3 Rendering=4 Animation=5
       Physics=6 Content.Tools=7 Hosting=8 Application=9
Unit: None=0 EngineDistance=1 Meters=2 Seconds=3 Radians=4 LinearColor=5
      SquareMeters=6 CubicMeters=7 TimeSpanTicks=8 EngineArea=9 EngineVolume=10
      Lux=11 Lumens=12
Reference: None=0 Schema=1 Asset=2 Entity=3 Joint=4 World=5 SceneInstance=6
           Recipe=7 System=8 Profile=9 Event=10 Stream=11 Module=12 Material=13
           Factory=14 ExternalSceneBinding=15 EntityOrJoint=16
           HistoricalEntity=17 HistoricalSceneInstance=18 ResourceOrSchema=19
Authoring flags: PrefabEditable=1 Animatable=2
Order source: FieldPath=0 HeaderSchemaId=1 HeaderMajor=2 HeaderMinor=3
Order comparison: Unsigned=0 Signed=1 RFCGuid=2 UTF8Bytes=3 DigestBytes=4 UInt32OrGuidTuple=5
                  PropertySegments=6
Direction: Ascending=0 Descending=1
Missing: Error=0 Before=1 After=2
Material slots: Shader=1 Pass=2 Layout=3
Material default: AlphaMode=0 DepthTest=1 DepthWrite=2 CullMode=3 OutputAlpha=4 Capabilities=5
```

Tuple comparison is lexicographic with a shorter equal prefix first; tuple element type follows the field's declared FA kind. PropertySegments compares an RA of content/property-segment lexicographically by each unsigned `(Kind,Value)` pair, with a shorter equal prefix first. Kind 1 is FieldTag and requires Value>0; kind 2 is FixedArrayIndex and permits zero, checked against the actual fixed-array extent. It is not a CLR/member-name path or a RecordArray index. Other scalar comparators are not usable for FA/RA. Header sources require empty Path and Missing=Error. Field paths use nonzero tags; an empty path means the scalar FA element itself. Byte comparisons are unsigned lexicographic, not culture-sensitive strings. GUID comparisons are RFC bytes, never mixed-endian raw memory.

R/RA uses a nonempty exact nested set or one nonzero role mask, never both. Scalar fields have neither. FA ElementKind must be a permanent fixed kind and ElementStride exactly:

```text
1..11: 1,2,4,8,1,2,4,8,1,4,16
14..21: 8,12,16,16,24,64,16,24
```

Default records never fill missing required wire tags. Required arrays, including empty ones, are present. Optional absent/present-empty/present-value remains distinct. No Record/FA/RA defaults in this edition. DM applies only to F/V2/V3 @EngineDistance with matching numerator count, one exact rational decimal conversion and final binary32 nearest-even rounding. DP applies only to native/material tags 6/9/10/11/12/13 and evaluates admitted profile/parameters/alpha/double-sided inputs in that order; no arbitrary expression facility.

An authoring initializer is separate from Read/Write. It first retains explicitly supplied tags and explicit optional-omission requests, establishes provided/defaulted discriminators, then materializes D/DM/DP only for unspecified applicable fields. An optional field with a declared default is initialized present unless explicitly omitted or forbidden by the selected discriminator; an optional field without one is absent unless supplied. A forbidden *explicitly supplied* field rejects rather than disappearing. Required fields without defaults must be supplied; a constant constraint is not an invented default. Thus the default finite camera materializes Far, Infinite omits only an unspecified Far, and the editable-values optional negative-zero field initializes present until an explicit Omit. Validate the complete initialized value afterward; ordinary readers never apply this procedure.

Entity-reference leaves are Guid, never owner/index/generation bytes. Only declared Reference=Entity leaves are mapped by typed component/system bindings. HistoricalEntity is an ID fact, not a live Entity. ResourceState admits no runtime Entity/private-handle leaves; an explicitly declared stable-Guid reference is permitted only with its closed stable-value codec/contextual target validator, never a returned Entity or membership grant. Fork transforms its declared stable reference, not opaque Guid data. A PrefabEditable Entity leaf may be overridden only through [M03 5](10-binary-data-and-authoring-formats.md#m03-5-complete-sceneprefab-catalogue-and-expansion)'s explicit selector/external-slot value binding, never by treating arbitrary Guid bits as a live target. Joint/HistoricalEntity editing requires its separately declared owner predicate.

Built-in schema IDs use the existing deterministic name-UUID recipe and exact registered keys. Application-defined schema IDs and codecs are compiled/registered by their owning application. The package carries neither identity-assignment metadata nor an approval receipt; an unknown schema/version is rejected against the runtime's selected registrations.

#### M03 3.3.1 Exact schema ordering contracts

This table replaces property-name inference in every O annotation. `G(p)`, `U(p)`, `S(p)`, `T(p)`, `B(p)`, `Tuple(p)` and `Props(p)` mean Source=FieldPath and Comparison=RFCGuid/Unsigned/Signed/UTF8Bytes/DigestBytes/UInt32OrGuidTuple/PropertySegments respectively. A dotted numeric p is the exact tag sequence, and `[]` is the empty path. `H1/H2/H3` mean HeaderSchemaId/HeaderMajor/HeaderMinor with empty paths and RFCGuid/Unsigned/Unsigned comparisons. Every key has Direction=Ascending and Missing=Error, except a written `Before`, which sets Missing=Before. These are documentation notation for fixed schema-version behavior, not serialized parameter records or validator uses. The last column gives `(RequireSorted,RequireUnique)`. Each listed `(containing schema, field tag)` directly specifies its ordering predicates; comma-separated tags and semicolon-separated schema/field groups apply independently. Repeated targets are separate predicates over the same field values. Header schema tuples have no fabricated Scope field; nested schema references include their real tag 4.

| Containing schema / field tag | Exact keys | Flags |
|---|---|---|
| native/metadata / 1 | T(1) | true,true |
| native/scene / 2 | G(1) | true,true |
| native/scene / 4; native/prefab / 5 | G(1) | true,true |
| native/scene / 6; native/prefab / 6 | U(1.1),Tuple(1.2),G(1.3),G(2.1),U(2.2),U(2.3),U(2.4),Props(3) | true,true |
| native/scene / 7; native/prefab-placement / 5 | U(1.1),Tuple(1.2),G(1.3),G(2.1),U(2.2),U(2.3),U(2.4),Props(3) | true,true |
| native/scene / 8; native/prefab / 8 | G(1) | true,true |
| native/prefab / 3 | G(1) | true,true |
| native/entity / 2 | H1,H2,H3 | true,true |
| native/hierarchy / 1 | G(1) | true,true |
| native/hierarchy / 2; native/hierarchy-node / 3 | U(1),G(2) | false,true |
| native/prefab-placement / 4 | U(1.1),Tuple(1.2),G(1.3) | true,true |
| native/prefab-placement / 6 | G(1) | true,true |
| native/external-slot / 4 | G(1),U(2),U(3),U(4) | true,true |
| native/mesh / 4 | U(1) | true,true |
| native/texture / 8 | U(1) | true,true |
| native/atlas / 2 | G(1) | true,true |
| native/skeleton / 1 | G(1) | false,true |
| native/clip / 3, predicate 1 | G(1) | false,true |
| native/clip / 3, predicate 2 | S(2),G(1) | true,true |
| native/transform-track / 5; native/sprite-track / 2 | S(1) | true,true |
| state/application / 4; state/runtime-profile / 9 | G(1) | true,true |
| state/world / 7 | U(1) | true,true |
| state/world / 8 | S(1) | true,true |
| state/world / 10 | G(1) | true,true |
| state/registration / 1 | G(1.1),U(1.2),U(1.3),U(1.4) | true,true |
| state/registration / 2 | G(1),U(2),U(3),U(4) | true,true |
| state/registration / 3 | S(2),G(1) | true,true |
| state/registration / 4 | G(1) | true,true |
| state/system-registration / 5 | U(1),G(2) | true,true |
| state/system-registration / 6, 7 | G([]) | true,true |
| state/ecs / 1, 2, 3 | G(1) | true,true |
| state/ecs / 4 | G(2;Before),G(1.1),U(1.2),U(1.3),U(1.4) | true,true |
| state/entity / 2, 3 | H1,H2,H3 | true,true |
| state/entity / 4 | G(1),U(2),U(3),U(4) | true,true |
| state/identity-history / 6 | U(1) | true,true |
| state/identity-history / 7 | S(1) | true,true |
| state/clock / 5 | S(1) | true,true |
| state/strict-layout / 1 | Tuple(1) | true,true |
| state/strict-layout / 2 | U(1) | true,true |
| state/strict-layout / 3 | U([]) | false,true |
| state/strict-layout / 5 | G(1.1),U(1.2),U(1.3),U(1.4) | true,true |
| state/archetype / 1 | G([]) | true,true |
| state/archetype / 2 | S(1) | true,true |
| state/chunk / 2 | G([]) | false,true |
| state/scenes / 1 | G(1) | true,true |
| state/scene-instance / 3 | G([]) | false,true |
| state/scene-instance / 4 | Tuple(1),G(3) | true,true |
| state/scene-instance / 5 | G(1) | true,true |
| state/scene-instance / 7 | Tuple(1) | true,true |
| state/control-scene / 6 | G(1) | true,true |
| scene/world-hierarchy / 1 | G(1) | true,true |
| scene/world-hierarchy / 2; scene/world-hierarchy-node / 3 | G([]) | false,true |
| state/continuation / 9 | S(1) | true,true |
| state/recorded-tick / 2 | S(1) | true,true |
| state/recorded-tick / 5 | U(1) | true,true |
| state/recorded-tick / 6 | G(1) | true,true |
| state/command-stream / 1 | U(1.1),S(1.2),U(1.3),S(1.4) | true,true |
| state/entity-description / 2, 3 | H1,H2,H3 | true,true |

RequireUnique compares the complete declared key, even when RequireSorted=false. A missing key is never replaced with an empty Guid, zero or empty tuple. Before/After is a distinct comparison sentinel; two missing values compare equal. Array lists with no O annotation retain their declared semantic order and have no inferred ordering constraint.


#### M03 3.4 Schema-version-owned validation

The application has a statically compiled schema/codec registration set, not a package-provided content catalogue. A trusted `(SchemaId, Major, Minor)` selects the owning fixed contract; package data cannot choose validator IDs, recipes, contexts or executable code. Every schema version defines its fixed fields and semantic behavior. A later schema version can add fields or change requirements; v1 does not define a universal extension-ID container or dynamic schema imports.

Read order is fixed: validate EBIN extents and structure; require the selected version's fixed fields and validate their wire kinds/counts in tag order; validate declared nested records; obtain the context required by the owning schema; then evaluate the schema's contextual and cross-field invariants. Shared implementation helpers are internal code, not independently registered algorithms. Owning schema tables and chapters 5-8, 11, 23 and 27 define those predicates and their expected observations.

Unknown schema versions, unsupported owner registrations and unknown required extensions return UnsupportedSchema. Malformed known fields, structural violations and failed schema-version predicates return InvalidRecord. Diagnostics identify the schema, physical tag/path and failure category; no RuleId, validator version or serialized use index is required. Structural scanner failures retain their actual EBIN diagnostic. No M03 engine feature is implied by these metadata contracts.
#### M03 4.1 Static schema registrations

The application build selects a closed set of compiled schema versions/codecs. A package does not carry a schema catalogue, import table, active-schema manifest, profile approval list, or migration chain. Each payload record identifies its schema GUID and major/minor; the runtime resolves that tuple against its statically registered codecs. Unknown versions reject. Adding a custom schema requires compiling/registering its codec in the application, not authoring executable or identity-approval data into a package.

AssetIds are nonzero signed 32-bit integers scoped to one content set. The content-set allocator assigns each ID once and keeps only its next/high-water value; native source records or a moved-with-source sidecar retain the assignment across rename and recook. Deleted IDs are not reused, but no tombstones are retained. The final package directory contains the live AssetId-to-payload ranges. AssetId is not derived from source path or content bytes.

Authoring asset references are typed: `native/asset-reference` carries the target AssetId, schema GUID and schema major. All target IDs must resolve in the selected content set. The build traverses actual direct references to collect/validate package contents; it does not serialize a general dependency graph or dependency history. Scene/prefab expansion owners still reject cycles where their own semantics require acyclic input.

#### M03 4.2 Native assets, references, and content-set packages

An `AssetId` is a positive signed 32-bit value stored as EBIN UInt32 in the range 1..2147483647. It is unique within one selected content set, not globally. The content-set allocator assigns it once and persists the next/high-water value; native sources or a moved-with-source sidecar retain IDs across rename and recook. Deleted IDs are not reused, exhaustion rejects without wrap, and published IDs are never compacted. The allocation and parallel finalization contract is in [11](11-assets-import-streaming-and-packages.md).

| Key | Complete tags |
|---|---|
| `native/envelope` 2.0 | 1 AssetId U32[1..2147483647]; 2 Payload R@NativeAsset; tags 3-5 reserved/forbidden |
| `native/asset-reference` | 1 AssetId U32[1..2147483647]; 2 SchemaId G/Schema; 3 SchemaMajor U32[1..I] |
| `native/metadata` | 1 Entries RA{native/metadata-entry}[0..256] O(Key/UTF8;true;true) |
| `native/metadata-entry` | 1 Key T ASCII[1..128]; 2 Value T[0..4096] |

Every native envelope repeats its content-set AssetId and carries one semantic payload. An asset reference carries the expected registered schema ID and major; resolution against another content set is invalid. The package directory is the authoritative live ID-to-payload map. Direct typed references in payloads define the required set; `native/scene.RequiredAssets` and `native/prefab.RequiredAssets` are not serialized duplicate lists. The build rejects zero, duplicate, missing, or wrongly typed IDs before publishing a package.

The package format is EPKG2/EIDX1 as defined in section 3 and [11](11-assets-import-streaming-and-packages.md). It contains one selected `foundation/profile`, one 64-byte EIDX header, and 32-byte directory entries sorted by AssetId. One external SHA-256 covers the complete package. There is no dependency, profile-index, name, per-asset digest, cook-key, publication, or schema-manifest table in the package. Schema codecs are selected from the application's compiled registration set; package data cannot compose or install codecs.
### M03 5. Complete scene/prefab catalogue and expansion

All rows here are Scenes/Auxiliary except scene/prefab NativeAsset and marked Persistent Components.

| Key | Complete tags |
|---|---|
| `native/scene` 2.0 | 1 SceneId U32[1..2147483647]; 2 Entities RA{native/entity}[0..N] O(EntityId;true;true); 3 Hierarchy R{native/hierarchy}; 4 Placements RA{native/prefab-placement}[0..N] O(PlacementId;true;true); 5 reserved/forbidden; 6 References RA{native/entity-reference-binding}[0..N] O(source composite below;true;true); 7 Overrides RA{native/prefab-override}[0..65536] O(target composite below;true;true); 8 ExternalSlots RA{native/external-slot}[0..65536] O(SlotId;true;true) |
| `native/prefab` 2.0 | Tags 2 and 7 reserved/forbidden. 1 PrefabId U32[1..2147483647]; 3 Entities RA{native/entity}[0..65536] O(EntityId;true;true); 4 Hierarchy R{native/hierarchy}; 5 Placements RA{native/prefab-placement}[0..N] O(PlacementId;true;true); 6 References RA{native/entity-reference-binding}[0..N] O(source composite;true;true); 8 ExternalSlots RA{native/external-slot}[0..65536] O(SlotId;true;true) |
| `native/entity` | 1 EntityId G/Entity; 2 Components RA@mask(0x0000000C)[0..256] O(header SchemaId,Major,Minor;true;true) |
| `native/hierarchy` 2.0 | 1 Nodes RA{native/hierarchy-node}[0..N] O(EntityId;true;true); 2 Roots RA{native/hierarchy-entry}[0..N] O(Kind,Id;false;true) |
| `native/hierarchy-node` 2.0 | 1 EntityId G/Entity; 2 ParentId? G/Entity; 3 Children RA{native/hierarchy-entry}[0..N] O(Kind,Id;false;true) |
| `native/hierarchy-entry` | 1 Kind U8{1,2}; 2 Id G |
| `native/prefab-placement` | 1 PlacementId G; 2 Prefab R{native/asset-reference}; 3 ParentEntityId? G/Entity; 4 RootPoses RA{native/prefab-root-pose}[0..65536] O(Target selector;true;true); 5 Overrides RA{native/prefab-override}[0..65536] O(target composite;true;true); 6 ExternalBindings RA{native/placement-slot-binding}[0..65536] O(SlotId;true;true) |
| `native/prefab-root-pose` | 1 Target R{native/entity-selector}; 2 Local R{scene/local-transform}; 3 RootPosition? P3 @EngineDistance |
| `native/entity-selector` | 1 Scope U8{1,2}; 2 PlacementPath FA(G)[0..32]; 3 AuthoredEntityId G/Entity |
| `native/prefab-override` | 1 Target R{native/entity-selector}; 2 ComponentSchema R{content/schema-ref}; 3 PropertyPath RA{content/property-segment}[1..64]; 4 Value? R{FixedLiteralSet}; 5 Operation U8{0,1} D(0); 6 ReferenceValue? R{native/reference-target} |
| `native/entity-reference-binding` | 1 Source R{native/entity-selector}; 2 ComponentSchema R{content/schema-ref}; 3 PropertyPath RA{content/property-segment}[1..64]; 4 Target R{native/reference-target} |
| `native/reference-target` | 1 Kind U8{1,2}; 2 Entity? R{native/entity-selector}; 3 ExternalSlotId? G/ExternalSceneBinding |
| `native/external-slot` | 1 SlotId G/ExternalSceneBinding; 2 Required Bool; 3 AllowNull Bool; 4 RequiredComponents RA{content/schema-ref}[0..256] O(schema tuple;true;true) |
| `native/placement-slot-binding` | 1 SlotId G/ExternalSceneBinding; 2 Target? R{native/reference-target} |
| `scene/local-transform` Component | 1 Position V3 @EngineDistance ^pa D(0,0,0); 2 Rotation Q ^pa D(0,0,0,1); 3 Scale V3 abs[0x38D1B717..0x447A0000] ^pa D(1,1,1) |
| `scene/root-position` Component | 1 Position P3 @EngineDistance ^p D(cells 0/offsets positive zero) |
| `scene/camera` Component | 1 Projection U8{0,1} ^p D(0); 2 VerticalFov F(0..pi32) exclusive @Radians ^p D(0x3F860A92); 3 OrthographicHeight F>0 @EngineDistance ^p DM(10;1); 4 Near F @EngineDistance ^p DM(1;10); 5 FarMode U8{0,1} ^p D(0); 6 Far? F @EngineDistance ^p DM(1000;1); 7 Aspect? F>0 ^p; 8 Viewport V4 ^p D(0,0,1,1); 9 ClearFlags U8 mask(3) ^p D(3); 10 ClearColor V4 HDR @LinearColor ^p D(0,0,0,1); 11 LayerMask U64 mask(0xFFFFFFFFFFFFFFFF) ^p D(all); 12 Order I32 ^p D(0) |
| `scene/collision-reference` Component | 1 Shape R{native/asset-reference} /Asset ^p |

Every native entity has LocalTransform. Entity and PrefabPlacement entry kinds are exactly 1/2. Each authored entity and placement occurs once across root/child entries; an entity's ParentId or a placement's ParentEntityId agrees with its entry location. Parent names a directly authored entity in that containing asset; no nested/ambient match. Roots have RootPosition and numerically zero Local.Position; children have no RootPosition. Parent/child projection is acyclic and final entity hierarchy depth <=256. SceneId/PrefabId equals envelope AssetId. Prefab root lists may contain multiple entities/placements or be empty; the old single-root tag is reserved, not silently reinterpreted. Entity tags are complete zero-field schema records. One schema GUID per entity, irrespective of major.

Authored EntityIds and PlacementIds are each unique within their containing document; equal bits across those two domains are disambiguated by the explicit entry kind. They are persisted author identities and may be UUIDv8; the reserved-domain restriction applies to explicit *World assignments*, not these IDs. The Placements and Entities maps are RFC-sorted, while hierarchy entries retain semantic order. No gap-index, map order or async completion establishes a second placement order.

### M03 primitive schema behavior

The fixed `physics/primitive2` and `physics/primitive3` schema versions select their dimension-specific geometry admission and statistics behavior. The owner implementation is statically rooted; primitive records and statistics carry no validator/formula GUID or version. Exact-literal Core conversion, converted-limit interpretation, corner predicates and fault classification are defined in this inventory and [23](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics). The schema version and selected spatial profile/settings determine the behavior; equal numeric settings never authorize a different schema or profile identity.

### M03 6. Complete replacement Physics wire inventory

Physics owns the semantic meaning; Integration supplies the Assets-interface bridge. Primitive roots NativeAsset; all support rows Auxiliary. The following field catalogues are normative; expanded descriptor and fixture-byte approval remain separate gates.

| Key | Complete tags |
|---|---|
| `physics/primitive2` | 1 Definition R{physics/circle,physics/box2}; 2 Statistics R{physics/statistics2}; 3 Profile R{foundation/profile 2.0} |
| `physics/primitive3` | 1 Definition R{physics/sphere,physics/box3}; 2 Statistics R{physics/statistics3}; 3 Profile R{foundation/profile 2.0} |
| `physics/circle` | 1 Radius F @EngineDistance |
| `physics/box2` | 1 HalfExtents V2 @EngineDistance |
| `physics/sphere` | 1 Radius F @EngineDistance |
| `physics/box3` | 1 HalfExtents V3 @EngineDistance |
| `physics/shape-measure` | 1 Significand F; 2 BinaryExponent I32[-512..512] |
| `physics/spatial-settings` | 1 ChunkSizeExponent U8[8..16]; 2 MetersPerUnitExponent I32[-30..30] |
| `physics/statistics2` | 1 VertexCount? U32[0..262144]; 2 EdgeCount? U32[0..N]; 3 FaceCount? U32[0..N]; 4 SignedArea R{physics/shape-measure} @EngineArea; 5 Minimum V2 @EngineDistance; 6 Maximum V2 @EngineDistance; 7 EnclosingRadius F>=0 @EngineDistance; 8 ConnectedComponents U32[1..N]; 9 WeldedVertexCount? U32[0..262144]; 10 PatchCount? U32[0..N]; 11 MaximumSnapDisplacement? F>=0 @EngineDistance; 12 Settings R{physics/spatial-settings} |
| `physics/statistics3` | Identical tags to statistics2, except 4 SignedVolume @EngineVolume and 5/6 V3 |

Stats local fields do not pretend to establish geometry. Their applicability and exact values are validated by the containing primitive root; the shared support meaning leaves later indexed/surface consumers to their separately registered owner algorithms, not a competing statistics type.

Envelope Profile, primitive Profile and trusted selected binding agree on registered ProfileId, codes, both exponents and all three digests. Stats.Settings equals those two exponents. The primitive schema dimension determines which definition and statistics record apply. Foundation's existing inert optional preservation is unchanged; compare the complete admitted foundation binding tuple, not a guessed profile string or a new foundation digest.

Let e be selected MetersPerUnitExponent. Publish L_e=RNE32((1/40)/10^e) and H_e=RNE32(64/10^e), using the exact rational Core overload. Admit radius/each half-extent against these binary32 endpoints, not an additional exact-SI predicate. At e=3 admit `0x37D1B717`; `0.025f` first would produce the wrong `0x37D1B718`.

Definition wire nonfinite values reject contextually before invoking finite-only constructors. Finite definitions copy bits. Fault order is minimum radius/X/Y/Z, maximum radius/X/Y/Z, then exact corner. With `N(x)` the signed integer x*2^149, box corner admission is `sum(N(h_i)^2)<=N(H_e)^2`. Observe an outward-rounded finite radius on a corner fault. No overflowing float-square predicate.

Pi is the exact dyadic `0x40490FDB`. Compute complete exact expressions, then normalized 24-bit-significand RNE and carry:

```text
Circle area = pi32*r*r
Box2 area = 4*hx*hy
Sphere volume = 4*pi32*r*r*r/3
Box3 volume = 8*hx*hy*hz
```

These are engine-unit squared/cubed, not SI single-float fields. A positive pair has finite 1<=Significand<2 and exponent [-512,512]. Zero is exactly positive-zero significand/exponent zero; negative-zero or a zero/nonzero-exponent pair rejects. Primitive output is positive, so valid zero pairs still fail primitive recomputation.

Presence/values:

```text
Circle: vertex absent, edge absent, face absent.
Box2: vertex 4, edge 4, face absent.
Sphere: vertex absent, edge absent, face absent.
Box3: vertex 8, edge 12, face 6.
All: ConnectedComponents=1.
All: WeldedVertexCount/PatchCount/MaximumSnapDisplacement absent,
     not zero-present.
```

Bounds are copied positive extents/negations; analytic radius is copied; box radius is the least binary32 r with `N(r)^2>=sum(N(h_i)^2)`. Compare every recomputed field/presence/setting/bit exactly; do not replace stored statistics with corrected success. Known mismatches return contextual InvalidRecord.

`TryGetSingle` rounds the stored pair once: normals/subnormals/+0 succeed; nonzero rounds-to-zero/infinity returns false with +0 output and does not invalidate geometry. Preserve the independent e=12 sphere (`radius 0x28E12FB1`, pair `0x3FB67680,-134`, stored conversion `0x0000B676`, direct formula `0x0000B677`) and e=-14 one-meter sphere (`0x3FC056FF,141`, float overflow).

All 61 exponents are metadata/geometry cases, not complete Physics profile eligibility. An explicit settings change converts copied dimensions once with Core, recomputes statistics, and requires rebuilding the affected package. The AssetId remains stable while the whole-package SHA-256 changes; a StrictLocal recording pinned to the prior package rejects it. Chunk-only changes still require explicit profile rebinding/rebuild. No scaling old rounded measures or relabeling is permitted.

### M03 7. Complete CPU mesh/material/texture/animation/render data

Rendering owner unless noted. Native roots are mesh/material/texture/atlas; support Auxiliary; render components Persistent. Skeleton/clip and supports are Animation, with native roots and EventPayload marker.

| Key | Complete tags |
|---|---|
| `native/mesh` | 1 VertexCount U32[1..262144]; 2 Streams RA{position,normal,tangent,uv,color,joints,weights wrappers below}[1..8]; 3 Indices R{native/mesh-indices16,native/mesh-indices32}; 4 Groups RA{native/mesh-group}[1..65536] O(FirstIndex;true;true); 5 MaterialSlots RA{native/asset-reference}[1..256] semantic order; 6 Bounds R{native/bounds3}; 7 Skeleton? R{native/asset-reference} |
| `native/bounds3` | 1 Minimum V3 @EngineDistance; 2 Maximum V3 @EngineDistance; 3 SphereCenter V3 @EngineDistance; 4 SphereRadius F>=0 @EngineDistance |
| `native/mesh-position` | 1 Stride U32=12; 2 Values FA(V3)[1..262144] @EngineDistance |
| `native/mesh-normal` | 1 Stride U32=12; 2 Values FA(V3 unit)[1..262144] |
| `native/mesh-tangent` | 1 Stride U32=16; 2 Values FA(V4)[1..262144] |
| `native/mesh-uv` | 1 Set U8{0,1}; 2 Stride U32=8; 3 Values FA(V2)[1..262144] |
| `native/mesh-color` | 1 Stride U32=16; 2 Values FA(V4)[1..262144] range [0..1] @LinearColor |
| `native/mesh-joints` | 1 StridePerVertex U32=8; 2 Values FA(U16)[4..N] |
| `native/mesh-weights` | 1 Stride U32=16; 2 Values FA(V4)[1..262144] range [0..1] |
| `native/mesh-indices16` | 1 Values FA(U16)[3..N] |
| `native/mesh-indices32` | 1 Values FA(U32)[3..N] |
| `native/mesh-group` | 1 FirstIndex U32[0..N]; 2 IndexCount U32[1..N]; 3 MaterialSlot U32[0..255] |
| `native/material` | 1 ProfileId G/Profile; 2 ShaderId G; 3 PassId G; 4 LayoutId G; 5 Parameters R{native/material-sprite-parameters,native/material-pbr-parameters}; 6 AlphaMode U8{0,1,2} DP(0); 7 AlphaCutoff F[0..1] D(0.5); 8 DoubleSided Bool D(false); 9 DepthTest Bool DP(1); 10 DepthWrite Bool DP(2); 11 CullMode U8{0,1} DP(3); 12 OutputAlpha U8{0,1} DP(4); 13 RequiredCapabilities U64 mask(0x1FF) DP(5) |
| `native/material-sprite-parameters` | 1 Color V4[0..1] @LinearColor D(1,1,1,1); 2 Texture? R{native/texture-binding} |
| `native/material-pbr-parameters` | 1 BaseColor V4[0..1] @LinearColor D(1,1,1,1); 2 Metallic F[0..1] D(0); 3 Roughness F[0x3D3851EC..1] D(1); 4 NormalScale F[0..2] D(1); 5 Emissive V3>=0 @LinearColor D(0,0,0); 6 BaseTexture? R{native/texture-binding}; 7 PackedMetalRoughOcclusion? same; 8 NormalTexture? same; 9 EmissiveTexture? same |
| `native/texture-binding` | 1 Texture R{native/asset-reference}; 2 Sampler R{native/sampler}; 3 UVSet U8{0,1} D(0) |
| `native/sampler` | 1 Min U8{0,1}; 2 Mag U8{0,1}; 3 Mip U8{0,1,2}; 4 AddressU U8{0,1}; 5 AddressV U8{0,1} |
| `native/texture` | 1 Width U32[1..4096]; 2 Height U32[1..4096]; 3 Format U8=0; 4 ColorSpace U8{0,1}; 5 Alpha U8{0,1}; 6 Semantic U8{0,1,2}; 7 MipPolicy U8{0,1}; 8 Mips RA{native/texture-mip}[1..13] O(Level;true;true) |
| `native/texture-mip` | 1 Level U32[0..12]; 2 Width U32[1..4096]; 3 Height U32[1..4096]; 4 Pixels B[4..67108864] |
| `native/atlas` | 1 Pages RA{native/asset-reference}[1..16] semantic order; 2 Frames RA{native/atlas-frame}[1..N] O(FrameId;true;true) |
| `native/atlas-frame` | 1 FrameId G; 2 Page U32[0..15]; 3 X U32[0..4095]; 4 Y U32[0..4095]; 5 Width U32[1..4096]; 6 Height U32[1..4096]; 7 Padding U8=2 |
| `render/sprite` Component | 1 Atlas R{native/asset-reference} /Asset ^pa; 2 FrameId G ^pa; 3 Pivot V2[0..1] ^p D(0.5,0.5); 4 Size V2>0 @EngineDistance ^p DM(1,1;1); 5 Tint V4 HDR @LinearColor ^p D(1,1,1,1); 6 FlipMask U8 mask(3) ^p D(0); 7 SortingLayer I32 ^p D(0); 8 SortingOrder I32 ^p D(0); 9 Material? R{native/asset-reference} /Asset ^p; 10 PixelSnap Bool ^p D(false) |
| `render/mesh` Component | 1 Mesh R{native/asset-reference} /Asset ^p; 2 LayerMask U64 mask(all) ^p D(all); 3 PoseMode U8=0 D(0) |
| `render/directional-light` Component | 1 Color V3>=0 @LinearColor ^p D(1,1,1); 2 Illuminance F>=0 @Lux ^p D(10000); 3 Direction V3 unit ^p D(0,-1,0); 4 LayerMask U64 mask(all) ^p D(all); 5 ShadowRequested Bool ^p D(false); 6 ShadowPriority I32 ^p D(0) |
| `render/point-light` Component | 1 Color V3>=0 @LinearColor ^p D(1,1,1); 2 Flux F>=0 @Lumens ^p D(1000); 3 Range F>0 @EngineDistance ^p DM(10;1); 4 LayerMask U64 mask(all) ^p D(all) |
| `render/spot-light` Component | 1 Color V3>=0 @LinearColor ^p D(1,1,1); 2 Flux F>=0 @Lumens ^p D(1000); 3 Range F>0 @EngineDistance ^p DM(10;1); 4 Direction V3 unit ^p D(0,0,1); 5 InnerHalfAngle F(0..pi32/2) exclusive @Radians ^p D(0x3F060A92); 6 OuterHalfAngle F(0..pi32/2) exclusive @Radians ^p D(0x3F490FDB); 7 LayerMask U64 mask(all) ^p D(all); 8 ShadowRequested Bool ^p D(false); 9 ShadowPriority I32 ^p D(0) |
| `native/skeleton` | 1 Joints RA{native/joint}[1..256] O(JointId;false;true); 2 RootMotionJoint? G/Joint |
| `native/joint` | 1 JointId G/Joint; 2 ParentIndex I32[-1..255]; 3 BindLocal R{scene/local-transform}; 4 InverseBind M4 |
| `native/clip` | 1 DurationTicks I64[1..L] @TimeSpanTicks; 2 Tracks RA{native/transform-track,native/sprite-track}[0..4096]; 3 Events RA{native/clip-event}[0..N] O(EventId;false;true) then O(TimeTicks,EventId;true;true); 4 LoopMode U8{0,1} D(0); 5 Skeleton? R{native/asset-reference}; 6 RootMotion? R{native/root-motion}; 7 Bounds? R{native/bounds3}; 8 BoundsQualification U8=0; 9 BoundsMargin F>=0 @EngineDistance D(0); 10 Scene? R{native/asset-reference} |
| `native/transform-track` | 1 TargetId G/EntityOrJoint; 2 TargetDomain U8{0,1}; 3 Channel U8{0,1,2}; 4 Interpolation U8{0,1,2}; 5 Keys RA{native/key-vector3,native/key-quaternion}[1..N] O(TimeTicks;true;true) |
| `native/sprite-track` | 1 TargetEntityId G/Entity; 2 Keys RA{native/key-sprite}[1..N] O(TimeTicks;true;true) |
| `native/key-vector3` | 1 TimeTicks I64[0..L] @TimeSpanTicks; 2 Value V3; 3 InTangent? V3; 4 OutTangent? V3 |
| `native/key-quaternion` | 1 TimeTicks I64[0..L] @TimeSpanTicks; 2 Value Q; 3 InTangent? V4; 4 OutTangent? V4 |
| `native/key-sprite` | 1 TimeTicks I64[0..L] @TimeSpanTicks; 2 Atlas R{native/asset-reference}; 3 FrameId G |
| `native/clip-event` | 1 EventId G/Event; 2 TimeTicks I64[0..L] @TimeSpanTicks; 3 Payload R@EventPayload |
| `native/event-marker` EventPayload | 1 Name T[1..128], user text not normalized |
| `native/root-motion` | 1 RootJointId G/Joint; 2 Policy U8=0 |

Shorthands `same` above expand to the exact `R{native/texture-binding}`. `all` is the literal `0xFFFFFFFFFFFFFFFF`. V2>0 means both finite components strictly positive. V3>=0 means all finite components nonnegative. They generate same-kind vector range literals, not scalar or padded bounds.

Mesh-stream key is exactly semantic rank Position0/Normal1/Tangent2/UV0=3/UV1=4/Color5/Joints6/Weights7; ascending and unique. No arbitrary Header GUID sort. Each non-joint stream count=VertexCount; joints count=4*VertexCount. Position is mandatory. Tangent XYZ is a unit vector under the fixed `native/mesh` schema contract, W is bits +1/-1; weights are finite and nonnegative with exact dyadic sum within 1/100000 of one, without load normalization; joints are in the actual skeleton domain. Joint/weight/skeleton presence is all-or-none. Indices are in range, their count divisible by 3, and each exact dyadic triangle cross is nonzero. Groups begin at 0, have FirstIndex/IndexCount divisible by 3, contiguous nonempty ranges and final end=Indices.Count; every MaterialSlot is in the actual slot domain. Unreferenced vertices are not silently removed or made invalid; conservative bounds include all supplied positions. Material-slot order is semantic and may repeat the same asset.

Bounds are finite, ordered and conservative over the admitted vertices; sphere uses exact dyadic squared-distance inclusion. No minimal-sphere requirement. This edition admits supplied explicit noninterleaved streams and no authored LOD/morph/meshlet/packed formats; their actual M05 owners must extend the catalogue. It does not silently reinterpret opaque stride bytes.

### M03 8. Complete standalone checkpoint catalogue and equalities

This edition is standalone CPU committed persistence. Hosted wall pacing/input inbox/device/physics/animation/audio state belongs to its actual later owner; this replaces the old speculative `state/pacing` and opaque world-edit operation table rather than pretending those consumers exist. Later hosted formats need explicit catalogue/version extensions and exact pending-state contracts. Current M03 save does not silently serialize future host delegates or action state.

Hosting/Checkpoint owns file/checkpoint/header/canonical/runtime identities and world composition. Entities/Auxiliary owns ECS/configuration/registration/clock/layout/PCG; Scenes/Auxiliary owns scene state. Control payload owners are given below.

| Key | Complete tags |
|---|---|
| `state/file` | 1 TransactionGeneration I64[1..L]; 2 Checkpoint R{state/checkpoint}; 3 CheckpointDigest B[32..32]; 4 FileContentDigest B[32..32] |
| `state/file-content` | 1 TransactionGeneration I64[1..L]; 2 Checkpoint R{state/checkpoint} |
| `state/checkpoint` | 1 Header R{state/header}; 2 World R{state/world}; 3 CanonicalHash B[32..32] |
| `state/header` | 1 ContentPackageSha256? B[32..32]; 2 Application R{state/application}; 3 WorldGuid G/World; 4 Profile R{foundation/profile}; 5 NextTick I64[1..L]; 6 Mode U8{0,1}; 7 LineageGuid G; 8 RuntimeProfile R{state/runtime-profile}; 9 SerializationMajor U32=1; 10 SerializationMinor U32=0; 11 CaptureWorkerCount U8[0..64] |
| `state/application` | 1 ApplicationId G; 2 Major U32[1..I]; 3 Minor U32[0..I]; 4 Modules RA{state/module-id}[0..256] O(ModuleId;true;true) |
| `state/module-id` | 1 ModuleId G/Module; 2 Version U32[1..I] |
| `state/module` | 1 ModuleId G/Module; 2 Version U32[1..I]; 3 ArtifactDigest B[32..32] |
| `state/runtime-profile` | 1 EngineArtifact B[32..32]; 2 RuntimeArtifact B[32..32]; 3 CompilerArtifact B[32..32]; 4 OS U8{0,1}; 5 Architecture U8=0; 6 Codegen U8{0,1}; 7 ISAFlags U64 mask(15); 8 Numerics R{state/numerics}; 9 Modules RA{state/module}[0..256] O(ModuleId;true;true) |
| `state/numerics` | 1 KernelDigest B[32..32]; 2 FmaPolicy U8{0,1}; 3 MaximumSimdBits U16{0,128,256,512}; 4 Rounding U8=0; 5 FlushSubnormals Bool=false; 6 Reassociation Bool=false |
| `state/canonical` | 1 Mode U8{0,1}; 2 ContentPackageSha256? B[32..32]; 3 Application R{state/application}; 4 WorldGuid G/World; 5 Profile R{foundation/profile}; 6 World R{state/world}; 7 RuntimeProfile? R{state/runtime-profile} |
| `state/world` | 1 Configuration R{state/configuration}; 2 Clock R{state/clock}; 3 Frame2? R{foundation/frame2}; 4 Frame3 R{foundation/frame3}; 5 ECS R{state/ecs}; 6 Scenes R{state/scenes}; 7 AssetIds FA(U32)[0..N] O(value;true;true); 8 Controls RA{state/control}[0..65536] O(Sequence;true;true); 9 Cursors R{state/cursors}; 10 RNG RA{state/pcg}[0..65536] O(StreamId;true;true); 11 StrictLayout? R{state/strict-layout}; 12 Registration R{state/registration} |
| `state/configuration` | 1 MaximumLiveEntities U32[1..N]; 2 MaximumEntitySlots U32[1..2097152]; 3 MaximumArchetypes U32[1..4096]; 4 MaximumDeferredCommands U32[1..65536]; 5 MaximumComponentTypes U32[1..256]; 6 MaximumOwnerRequests U32[1..128]; 7 MaximumRecordedControls U32[1..65536]; 8 MaximumIdentityAssignments U32[1..33554432]; 9 Scheduling R{state/scheduling-limits}; 10 ExecutionOwner U8=0; 11 MaximumIdentityBranches U16[1..1024]; 12 MaximumEntityReferencePositions U32[1..4194304]; 13 MaximumActiveSceneInstances U32[1..65536]; 14 MaximumSceneOccurrences U32[1..N]; 15 MaximumSnapshotBytes I64[64..2147483584]; 16 MaximumPersistenceAdmissionBytes I64[4096..8589934592] |
| `state/scheduling-limits` | 1 RegistrationBytes I64[4096..16777216]; 2 GraphBytes I64[4096..8388608]; 3 WorkPlanBytes I64[4096..67108864]; 4 CommandStagingBytes I64[4096..33554432]; 5 OwnedStateBytes I64[4096..2147483648]; 6 DiagnosticBytes I64[4096..8388608]; 7 ConflictComparisons U32[0..16777216] |
| `state/registration` | 1 Components RA{state/component-registration}[0..256] O(schema tuple;true;true); 2 Tags RA{content/schema-ref}[0..256] O(schema tuple;true;true); 3 Systems RA{state/system-registration}[0..4096] O(Order signed,SystemId;true;true); 4 Resources RA{state/resource-registration}[0..4096] O(ResourceId;true;true) |
| `state/component-registration` | 1 Schema R{content/schema-ref}; 2 Storage U8{1,2}; 3 Restore U8{0,1,2}; 4 FactoryId G/Factory; 5 FactoryVersion U32[1..I] |
| `state/system-registration` | 1 SystemId G/System; 2 Order I32; 3 Stage U8{0..8}; 4 ThreadMode U8{1,2}; 5 Access RA{state/access}[0..256] O(Kind,ResourceId;true;true); 6 Before FA(G)[0..4095] O(value/RFC;true;true); 7 After same; 8 FactoryId G/Factory; 9 FactoryVersion U32[1..I]; 10 StateSchema? R{content/schema-ref} |
| `state/access` | 1 Kind U8{1,2,3,4}; 2 ResourceId G/ResourceOrSchema; 3 Mode U8{1,2}; 4 Range? R{state/partition-range} |
| `state/partition-range` | 1 Start U32[0..I]; 2 Length U32[1..I] |
| `state/resource-registration` | 1 ResourceId G; 2 PartitionCount U32[1..I]; 3 AllowParallelAccess Bool; 4 StateSchema R{content/schema-ref}; 5 FactoryId G/Factory; 6 FactoryVersion U32[1..I] |
| `state/ecs` | 1 Entities RA{state/entity}[0..N] O(EntityId;true;true); 2 Systems RA{state/system-state}[0..4096] O(SystemId;true;true); 3 Resources RA{state/resource-state}[0..4096] O(ResourceId;true;true); 4 Attachments RA{state/attachment}[0..N] O(owner missing-before,schema tuple;true;true); 5 IdentityHistory R{state/identity-history} |
| `state/entity` | 1 EntityId G/Entity; 2 Components RA@Component[0..256] O(header tuple;true;true); 3 Tags RA@EntityTag[0..256] O(header tuple;true;true); 4 TransientSchemas RA{content/schema-ref}[0..256] O(schema tuple;true;true); 5 AssignmentOrdinal I64[1..33554432]; 6 SceneInstanceId? G/SceneInstance |
| `state/identity-history` | 1 WorldGuid G/World; 2 ReplayLineageId G; 3 ActiveBranchId G; 4 IssuanceNamespace G; 5 NextAssignmentOrdinal I64[1..33554433]; 6 Branches RA{state/identity-branch}[1..1024] O(DirectoryIndex;true;true); 7 Pages RA{state/identity-page}[0..8192] O(FirstOrdinal;true;true) |
| `state/identity-branch` | 1 DirectoryIndex U16[0..1023]; 2 BranchId G; 3 CreationLineageId G; 4 IssuanceNamespace G; 5 ParentBranchId? G; 6 CreatedAtNextTick I64[0..L]; 7 SourceCheckpointDigest? B[32..32]; 8 ForkSource? R{state/fork-source}; 9 Kind U8{1,2}; 10 FirstAssignmentOrdinal I64[1..33554433] |
| `state/fork-source` | 1 SourceWorldGuid G; 2 SourceBranchId G; 3 SourceCreationLineageId G; 4 SourceNamespace G; 5 SourceCheckpointDigest B[32..32]; 6 SourceIdentityDigest B[32..32]; 7 Prior? R{state/fork-source} |
| `state/identity-page` | 1 FirstOrdinal I64[1..33554432]; 2 Ids FA(G)[1..4096]; 3 Metadata FA(U32)[1..4096] mask(0x7FFF) |
| `state/system-state` | 1 SystemId G/System; 2 Payload R@SystemState |
| `state/resource-state` | 1 ResourceId G; 2 Payload R@ResourceState |
| `state/attachment` | 1 Schema R{content/schema-ref}; 2 OwnerEntityId? G/Entity; 3 Payload R@AttachmentState |
| `state/pcg` | 1 StreamId G/Stream; 2 State U64; 3 Increment U64 |
| `state/clock` | 1 NextTick I64[1..L]; 2 TickRate U32[1..1000]; 3 TimeScale F[0..4]; 4 NextPublicationSequence I64[1..L]; 5 Segments RA{state/time-segment}[1..65537] O(Sequence;true;true); 6 ReplayComplete Bool=false; 7 CurrentSegmentSequence I64[1..L]; 8 InitialFrame2? R{foundation/frame2}; 9 InitialFrame3 R{foundation/frame3}; 10 HistoryBaseNextTick I64=0 |
| `state/time-segment` | 1 Sequence I64[1..L]; 2 StartNextTick I64[0..L]; 3 TimeScale F[0..4] |
| `state/control` | 1 Sequence I64[1..L]; 2 AppliedAtNextTick I64[0..L]; 3 Payload R@ControlPayload |
| `state/control-timescale` Entities/ControlPayload | 1 Value F[0..4]; 2 SegmentSequence I64[1..L] |
| `state/control-frame2` Entities/ControlPayload | 1 Frame R{foundation/frame2} |
| `state/control-frame3` Entities/ControlPayload | 1 Frame R{foundation/frame3} |
| `state/control-step` Entities/ControlPayload | 1 ExecutedTick I64[0..L] |
| `state/control-branch` Entities/ControlPayload | 1 BranchId G; 2 LineageId G; 3 DirectoryIndex U16[1..1023] |
| `state/control-scene` Scenes/ControlPayload | 1 Operation U8{0,1,2}; 2 SceneInstanceId G/HistoricalSceneInstance; 3 Source? R{native/asset-reference}; 4 Intents? RA{scene/intent-reparent,scene/intent-root-position}[1..65536]; 5 DestroyMode? U8{0,1}; 6 ExternalBindings? RA{state/external-scene-binding}[0..65536] O(BindingId;true;true); 7 RequestedSceneId? G; 8 OriginalSceneInstanceId? G |
| `scene/intent-reparent` Scenes/Auxiliary | 1 ChildId G/HistoricalEntity; 2 ParentId? G/HistoricalEntity; 3 Mode U8{0,1} |
| `scene/intent-root-position` Scenes/Auxiliary | 1 EntityId G/HistoricalEntity; 2 Position P3 @EngineDistance |
| `state/cursors` | 1 NextControlSequence I64[1..L]; 2 NextOwnerRequestSequence? I64[1..L]; 3 NextBarrierEpoch? I64[1..L]; 4 PendingCommands RA@ControlPayload[0..0]; 5 PendingOwnerRequests RA@ControlPayload[0..0]; 6 PendingWorldEvents RA@EventPayload[0..0]; 7 AcceptedOwnerRequests? I64[0..9223372036854775806] |
| `state/strict-layout` | 1 Archetypes RA{state/archetype}[0..4096] O(SchemaIds/RFC tuple;true;true); 2 Slots RA{state/slot}[0..2097152] O(Index;true;true); 3 FreeSlots FA(U32)[0..2097152] O(value;false;true); 4 NextChunkOrdinal I64[0..L]; 5 ComponentLayouts RA{state/component-layout}[0..256] O(schema tuple;true;true) |
| `state/archetype` | 1 SchemaIds FA(G)[0..256] O(value/RFC;true;true); 2 Chunks RA{state/chunk}[0..N] O(Ordinal;true;true) |
| `state/chunk` | 1 Ordinal I64[0..L]; 2 Rows FA(G)[0..256] /Entity O(value/RFC;false;true); 3 Capacity U16[1..256]; 4 LogicalRowBytes U32[12..32768] |
| `state/slot` | 1 Index U32[1..2097152]; 2 Generation U32[1..4294967295]; 3 Status U8{0,1,2}; 4 EntityId? G/Entity; 5 ArchetypeKeyDigest? B[32..32]; 6 ChunkOrdinal? I64[0..L]; 7 Row? U16[0..255] |
| `state/component-layout` | 1 Schema R{content/schema-ref}; 2 LogicalBytes U32[1..32756]; 3 LayoutId G |
| `state/scenes` Scenes/Auxiliary | 1 Instances RA{state/scene-instance}[0..65536] O(SceneInstanceId;true;true); 2 Hierarchy R{scene/world-hierarchy} |
| `state/scene-instance` Scenes/Auxiliary | 1 SceneInstanceId G/SceneInstance; 2 Source R{native/asset-reference}; 3 EntityIds FA(G)[0..N] /Entity O(value/RFC;false;true); 4 Bindings RA{state/scene-entity-binding}[0..N] O(OccurrencePath,TemplateEntityId;true;true); 5 ExternalBindings RA{state/external-scene-binding}[0..65536] O(BindingId;true;true); 6 AssignmentOrdinal I64[1..33554432]; 7 Occurrences RA{state/scene-occurrence}[1..N] O(OccurrencePath;true;true) |
| `state/scene-entity-binding` Scenes/Auxiliary | 1 OccurrencePath FA(G)[0..32]; 2 TemplateAsset R{native/asset-reference}; 3 TemplateEntityId G; 4 EntityId G; 5 EffectiveOccurrenceId G; 6 ExpandedEntityKey G |
| `state/scene-occurrence` Scenes/Auxiliary | 1 OccurrencePath FA(G)[0..32]; 2 OccurrenceId G; 3 Source R{native/asset-reference}; 4 ParentOccurrenceId? G; 5 PlacementId? G |
| `state/external-scene-binding` Scenes/Auxiliary | 1 BindingId G/ExternalSceneBinding; 2 TargetEntityId? G/HistoricalEntity |
| `scene/world-hierarchy` Scenes/Auxiliary | 1 Nodes RA{scene/world-hierarchy-node}[0..N] O(EntityId;true;true); 2 Roots FA(G)[0..N] /Entity O(value/RFC;false;true) |
| `scene/world-hierarchy-node` Scenes/Auxiliary | 1 EntityId G/Entity; 2 ParentId? G/Entity; 3 Children FA(G)[0..N] /Entity O(value/RFC;false;true) |

Schema-tuple O expands keys SchemaId RFC/Major unsigned/Minor unsigned/Scope unsigned. `same` After is FA(G)[0..4095] with the same ordered-unique rule. Qualified Payload roles must match the exact factory/participant schema, not any arbitrary record of that role.

These design scenarios do not define Standard/StrictLocal execution-profile
manifests or host/build inventories. Managed tests run through the supported
.NET SDK and ordinary CI workflow; machine identity, toolchain file hashes,
and worker counts are not fixture identity.

### M03 9. Production behavior tests and scenario inputs

This section defines representative inputs and expected observations for the features described by this contract. It does not claim that their M03 production consumers or fixtures exist.

#### M03 9.1 Tests of implemented behavior

Add focused C# tests to the project that owns each implemented behavior. Assert exact bytes against reviewed literals where the wire contract fixes them; otherwise assert direct values, statuses, diagnostics, ownership, failure ordering, and cleanup. Exercise public consumer and integration paths when they exist. A round trip alone does not establish the expected wire representation.

Use ordinary `dotnet restore`, `dotnet build`, and NUnit `dotnet test` workflows. Do not maintain a second complete encoder, converter, geometry engine, schema interpreter, owner schedule, lifecycle simulator, or qualification framework merely to compare it with production.

M02 canonical byte outputs remain guarded by the five hashes in P7, with functional semantics covered by focused C# NUnit cases. For unfinished M03 behavior, add tests alongside its owning implementation; do not retain obsolete schema branches or implementation archives.

#### M03 9.2 Expected values

Derive expected values from the owning contract and direct mathematics, then record them as readable literals or small bounded assertions in the production behavior tests. Do not bless production output or another implementation's output as an expected value. Use standard cryptographic known-answer vectors and explicit RFC-versus-mixed-endian cases where those distinctions are part of the production contract.

#### M03 9.3 Literal source bases

When an owning implementation exists, construct typed schema inputs through its actual registrations and APIs rather than reflecting public CLR properties. No current M03 application-schema compiler or registration consumer is established by these scenario examples. Build only the small scenario values and mutations needed to distinguish a behavior; do not add a persisted matrix of expected cases, statuses, or hashes.

Fixed fixture identity seeds:

```text
WorldGuid = 00112233-4455-4677-8899-aabbccddeeff
RFC-order pair:
 A = 00000001-0000-4000-8000-000000000001
 B = 00000100-0000-4000-8000-000000000001
RFC A<B; mixed-endian ToByteArray ordering is the contrary witness.
All other fixture identities = S("fixture/m03/<named-role>/<ordinal>/v1"),
 ordinal decimal without leading zeros unless a specific hexadecimal draw name is stated.
```

The source bases below are representative functional constructions, not a mandate for one carrier or expected-result row per schema/tag/field. C0 and application schema descriptors remain separate build-time sources; C# tests select the smallest record paths needed to distinguish the behavior under test. These cases do not automatically select a normalization `SchemaAddress`, `SchemaMember`, `SchemaRecord`, or `TypedRecordRoot`.

Family bases:

| Base | Exact input construction |
|---|---|
| Metadata | Introduce application schema registrations with their production owner. No candidate descriptor set, compiled application catalogue, or projection pipeline is required before that implementation exists. |
| Literal carrier | Select only the primitive kinds actually exercised by an implemented behavior. Keep the owner contract and production registration authoritative; do not maintain a duplicate schema catalog as test data. |
| Texture/material | Use minimal assets to distinguish color space, alpha, sampler, material defaults and required/optional slots when those owners are implemented. Avoid the full cross product of texture/material settings. |
| Atlas | Test one padded/clamped edge and one disjoint frame only when atlas sampling is implemented. Construct the pixels in C#; do not check in a generated pixel matrix for each combination. |
| Mesh | Use one asymmetric triangle and add a stream only to test a distinct required/optional-attribute or skinning rule. Do not enumerate every stream subset. |
| Skeleton/clip | Test endpoint keys and the interpolation/channel distinctions that change validation or output; do not build a row for every channel combination. |
| Primitive | Test settings endpoints, nearest outside values and representative rounding/carry/underflow cases. Do not create a profile fixture for every exponent unless a production operation actually consumes that complete set. |
| Scene/prefab | Use the smallest hierarchy that distinguishes nested placement, override precedence or reference mapping. Vary one source, owner or path condition at a time. |
| State | Start with one committed state and add separate branch, strict-layout or control cases only for distinct restore behavior; avoid crossing all modes and controls into a fixture matrix. |
| Continuation | Test one-/two-tick behavior and command classes that change validation or execution. Do not enumerate every command/target/presence tuple in a persisted source file. |
| Package | Test AssetId ordering, the selected foundation profile, the external whole-package checksum, direct typed references, and bounded decoding with minimal packages. Add a codec or metadata variant only when it distinguishes a supported contract. |

#### M03 9.3.1 Static application schema inputs

Application-owned schemas and semantic material profiles are selected by their owning implementation, not imported from package data. The current repository does not establish a production consumer for the former candidate JSON descriptors or profile declarations; tests must not describe those candidates as compiled registrations. Add the actual schema inputs and focused production tests together when the owning codec or runtime admission path is implemented. No runtime schema-import or migration catalogue is implied.

The `CApp1` label in the focused scene/state examples refers only to the fixture's compiled application schema set. It does not identify a runtime catalogue, manifest digest, or import edge. A distinct two-schema application fixture is compiled independently for row-pair tests and has no import relationship to CApp1.

##### M03 9.3.1.1 EN031/EN032 tag-case source

When the EN031 query and EN032 tag/storage paths are implemented, exercise them
through the public World and query owners using ordinary C# NUnit tests. Use
the actual registered component types and build only the small worlds needed
to distinguish the behavior.

Cover registration-limit admission and atomic refusal; required/excluded tag
matching and canonical entity order; HasTag for default, live-present,
live-absent, foreign, stale, and unknown-schema inputs; entity/token add and
remove; tag-only and component archetype transitions; storage growth and
capacity refusal; and cleanup or pool reuse when those change observable
behavior. Keep the 32,756-byte row boundary only where it distinguishes the
storage limit. Assert expected membership, IDs, status, and diagnostics
directly in tests. Do not persist a JSON case matrix or one source row per
operation/target combination.

#### M03 9.3.1.2 EN033–EN036 structural-command case source

Structural-command staging belongs to the command and lifecycle contracts in
chapters 13 and 27. When implemented, test staging, validation and first-failure
behavior, cancellation, ownership, cleanup, and failure atomicity through the
actual public owner path with small C# test builders. Add a target/presence
variant only when it changes the observable result or diagnostic; do not
introduce a serialized command-case matrix. These design cases do not claim
engine execution for unimplemented paths.

#### M03 9.3.2 Deterministic scene/state source construction

The examples in this section describe semantic inputs, not checked-in scene/state fixture files. Build a minimal source with C# test helpers when an implemented owner needs it; add variants only for distinct behavior rather than persisting the full state/branch cross product.

Let F(role,i)=S(`fixture/m03/` + exact ASCII role + `/` + canonical decimal i + `/v1`). Exponent-profile IDs are S(`fixture/m03/profile/exponent/` + canonical signed decimal e + `/v1`); aliases replace `exponent` with `alias`. Both use independently recomputed M02 settings/kernel/registry bindings, with chunk exponent 12. No runtime-generated Guid or production profile object supplies an expectation.

The small hierarchy/override/reference base is exact:

The fixture explicitly assigns AssetIds: Leaf prefab 1, Container prefab 2, Main scene 3, External scene 4, material 5, atlas 6, mesh 7, and primitive 8. These literal values are fixture inputs, not IDs derived from `F`, document order, paths, or content hashes.

| Document | Authored construction |
|---|---|
| Leaf prefab, AssetId 1 | Root F(entity,101), child F(entity,102), Roots=[Entity101], children=[Entity102]. Root Local=(zero, Q(0,0,1,0), scale(2,1,1)), RootPosition canonical zero; child Local=(0,2,0)/identity/ones. Root carries initialized persistent/editable/row24 (24 zero bytes), Target=102 and OpaqueId=102. Reference Source=Relative([],101), property [FieldTag2], Target=Internal Relative([],102). No placements, external slots, or direct asset references. |
| Container prefab, AssetId 2 | Root F(entity,201), ordered entries [Placement11,Placement12,Entity203,Entity202]; child Locals (1,0,0)/(0,0,1), identity/ones. Placements11/12 target Leaf with parent201 and one Relative([],101) RootPose, final Locals (0,3,0)/(0,4,0), identity/ones, RootPosition absent. Placement11 Sets editable [FieldTag1,FieldTag1] to F32 1 and Omits [FieldTag1,FieldTag2]; placement12 Sets [1,1] to negative zero. All override targets Relative([],101); no reference values. The placement directly references the Leaf AssetId. |
| Main scene, AssetId 3 | Ordered roots/children are the Main-placement row below. Root1 is default camera at canonical zero; root2 at offsets(0,0,1) has mesh/collision references. Literal RFC witnesses B/A carry initialized persistent; A additionally sprite/atlas and symbolic Target=leaf101; B symbolic Target=external-slot ID below. Child Locals (1,0,0)/(0,1,0), identity/ones. OpaqueIds equal those symbolic tokens and remain unmapped. |
| Main placements | Second-root entries=[Entity B,Placement1,Placement2,Entity A]; placements1/2 target Leaf with one Relative([],101) RootPose, final Local translations(1,0,0)/(-1,0,0), identity/ones. Main Roots=[Entity1,Placement3,Entity2]; placement3 targets Container with one Relative([],201) RootPose, RootPosition offsets(0,0,1), Local zero/identity/ones. Its outer overrides target Relative([placement11],101): editable [1,1]=F32 2, persistent [1]=U64 42. Both paths are explicitly PrefabEditable, including the revised Counter flag1. These share target context but not component identity. |
| Main bindings | Source Relative([],A), persistent [FieldTag2], Target Internal InstanceAbsolute([placement3,placement11],101). Source Relative([],B), same field, Target ExternalSlot F(external-binding,1); symbolic Value equals that slot ID. Declare that required/non-null slot with RequiredComponents empty. There are no other direct Entity leaves. Nested Leaf bindings remain their own occurrence-relative bindings. |
| External scene, AssetId 4 | One root F(entity,900), canonical zero RootPosition/initialized Local; no other data/references/placements/assets. First activation uses automatic World scene ordinal1/entity ordinal2. Obtain its actual Entity with root-authored lookup and pass the typed slot binding to Main; Main activation reserves scene ordinal3 then its entities in ExpandedEntityKey order. It is a separate graph/root, not an unreachable node in Main's owning graph. |

Numeric authored Entity/Placement abbreviations mean the explicit F inputs, never runtime assignment IDs or IDs derived by a real importer from row order. All entity/hierarchy/placement/override/reference maps use their declared wire keys; ordered entries retain the lists above. Main's package closure is derived from direct typed references; nested prefab/material references resolve within the same content set. Other fields use exact [M03 3](10-binary-data-and-authoring-formats.md#m03-3-complete-descriptor-notation-and-expansion) initializer values. Main B.OpaqueId equals the external-slot symbolic token and is not remapped. Asset inputs use 5, 6, 7, 8. The next successful tick makes both activations extraction/capture eligible. Mutations vary one entry/pose/override key/property-segment/reference anchor at a time. Domain0 clip bases pin this exact Scene and use separately derived ExpandedEntityKeys; joint-only bases omit Scene.

The non-scene state base uses WorldGuid above, lineage F(lineage,1), Default profile, n1/rate60/pub2, scale1/segment(1,0,1), ReplayComplete=false/HistoryBase0 and exact revision1 zero-origin +/-4096 frames. Configuration uses explicit default ceilings. One automatic live Entity at ordinal1 is the published EN025 value `9a2b3839-8a40-8fb7-aee5-740dacac1a2c`; its persistent Counter0/Target absent/Vector positive zeros/OpaqueId A, no tags/transients. Identity directory index0 is the independently derived root branch with lineage F(lineage,1), WorldGuid namespace, tick0/first ordinal1 and absent parent/source/fork; page FirstOrdinal1 has that ID and metadata `0x00005400` (Entity/Automatic/Live/index0), next ordinal2. Stateful system Counter1 after one Execute; stateless no row; resource Values=[0]; attachment Counter0/Target absent/OpaqueId A. PCG42/54 is exactly initialized and unconsumed. Scenes/Hierarchy/Assets empty. Strict slot1 Live/gen1/row0, exact archetype/chunk0/capacity, NextChunkOrdinal1, barrier2 and accepted requests0/next1; Standard omits strict layout/cursors. Frame2-absent omits both current/initial Frame2 and forbids its controls.

Branch fixture inputs are explicit F(branch,1) and optional fresh migration lineage F(lineage,2); independent fork uses F(world-fork,1)/F(lineage,3), subject only to nonempty/fresh active-GUID admission, not Entity explicit-domain restrictions. Independently transform full history/declared references/directory and derive the new future branch; retired and source-provenance/opaque-equal-Guid variants are separate. Add one non-v8 explicit top-level instance request A: its original activation fields both equal A; after fork only current SceneInstanceId transforms to the new World/ordinal UUIDv8, while the original request/result stay A. Cross automatic, explicit, retired and twice-forked provenance plus direct-control versus source-bound-load branch rows. A self-consistent alternative checkpoint with new valid identity/hash facts is positive under its matching context; the same bytes against independently pinned wrong source/target context reject. Do not call a harmless changed digest a malformed-history fixture.

Continuation base starts from the exact StrictLocal n1 source. Tick1 has no controls/commands, exact before-PCG/assets/scale, Counter becomes2 and n2/pub3; tick2 Counter3/n3/pub4. Expected canonical hashes are derived from those declared state facts, not copied from production recordings. Command/control variants use small real fixture systems with deterministic effects and only the operation/target/presence/cancellation branches that distinguish behavior.

Module ID is F(module,1)/version1 in both application/runtime tables. StrictLocal behavior uses the actual profile and state facts required by its owner contract; synthetic toolchain manifests and captured build inventories are not substitutes for executing a real consumer.

#### M03 9.3.3 Asset identity and package inputs

Asset identity and package tests use explicit positive integer IDs in one content-set fixture and literal payload bytes. They do not derive expected IDs from source paths, hashes, document order, runtime-generated values, or production output. The required allocation, reference-relocation, package-ordering, encoded-width, checksum, failure-atomicity, and lease cases are listed in [11](11-assets-import-streaming-and-packages.md#5-focused-acceptance). No source fingerprint, cook graph, publication record, dependency catalogue, or historical output is an input to those tests.

Scene and persistence owners use the explicit AssetIds from M03 9.3.2. Their typed references resolve only against the selected content set; the required package closure is derived from those references. Schema/profile and World/Entity identities remain separate domains.
#### M03 9.4 Focused branch coverage

For each behavioral contract, choose the smallest C# test input that distinguishes the required outcomes. Cover boundaries and error precedence when they change observable behavior; do not generate a Cartesian product of unrelated schema, presence, identity, profile and context variants. Use representative numeric and array boundaries, signed-zero/raw-bit cases, RFC-versus-mixed-endian identity ordering, registered-versus-unregistered bindings, and valid-versus-invalid owner cases where those distinctions affect observable behavior. Exhaustive enumeration is appropriate only when the production contract itself requires visiting every value in a finite domain. Keep test construction and expected outcomes in focused NUnit cases/builders, not a persisted fixture or coverage ledger.

The cases above are design examples, not compiled inputs or projection outputs. When production registrations exist, test their observable behavior directly with a small number of independently reviewed literals. Do not add a second descriptor catalog, owner schedule, or per-case coverage record.

Canonical wire bytes remain protected by a small number of static length/SHA-256 assertions. Test reader outcomes at representative valid-prefix boundaries (header, field table, payload and final byte), asserting status, consumption, diagnostic and output usability. Do not retain one expected row or hash per prefix or mutation when the assertion already tests that behavior.

#### M03 9.5 Focused source and integration tests

Schema descriptors, application registrations, and profile selection are introduced with their owning production features. They are not current build inputs or test projections. Add focused tests against those features when implemented, using direct literal outcomes where appropriate rather than a second projection pipeline.

When component providers or persistence paths are implemented, exercise their actual public owner boundary with small C#-constructed inputs. Compare exact outputs where bytes are contractual; otherwise assert the observable value, status, diagnostic, ownership and cleanup result. A test should isolate one changed rule or binding at a time rather than cross-producting unrelated dimensions.

Application schema versions are selected by their owning application and codec registrations when those features exist. There is no runtime schema import, historical catalogue, migration recipe, or package-provided approval path. Actual engine migrations remain unimplemented and outside this chapter's asset/package design.

### M03 10. Mutation/status/coverage contract

#### M03 10.1 Current testing approach

Add focused NUnit tests to the project that owns each implemented behavior. Use reviewed literal bytes and semantic values from the owner contract, small direct mathematical assertions, and real public-consumer or integration tests when those behaviors exist. Test production readers, writers, conversions, validators, and runtime operations directly; round-trip agreement alone is not an expected-value source.

Do not maintain a second format implementation, numerical converter, geometry engine, schema interpreter, owner schedule, lifecycle simulator, fixture compiler, test-case ledger, or source/provenance capture pipeline. Current M02 codec tests retain their five canonical length/SHA-256 locks and semantic witnesses. Existing tests of implemented Core, Physics, and Runtime behavior do not establish acceptance of unimplemented M03 schema, package, World, persistence, native, or AOT features.

Build and run tests through the ordinary .NET solution and owning-project workflows. Keep test inputs close to their production behavior tests; do not create a replacement qualification runner or persisted coverage manifest.

#### M03 10.2 Negative matrix

| Class | Mutations and expected boundary |
|---|---|
| Core header/prefix | Magic/reserved/extent/table flags/count/overflow and major0/high-bit major/high-bit minor: InvalidRecord. Unsupported container version and unknown required wire kind: UnsupportedSchema. Both consumed0/no callback; valid but incomplete prefixes NeedMoreData0. ReadHeader root may still Complete64 when only a nested body is bad. |
| Structure | Tag0/duplicates/order, flags, known kind/count/stride, overlapping/outside fields, bad zero padding, nonminimal canonical packing, record lengths/offset tables, nested aggregates/depth; Structural versus Canonical differentiated. |
| Semantic selection | A representable unregistered schema/version or missing typed codec registration returns UnsupportedSchema; malformed fields or a failed fixed schema-version predicate return InvalidRecord. There is no runtime validator/recipe lookup or package catalogue selection. |
| Defaults/presence | Every required omission, invalid declared default/literal kind/arity, optional absent/empty/value branch, forbidden inactive fields, signedzero/NaN and record-array default refusal. A valid explicit nondefault is positive and retains bits; defaults are initialization, not equality constraints. |
| Envelope/package | Zero, duplicate, missing, or wrongly typed AssetId; invalid directory ordering/extent/alignment/overflow/reserved bytes; whole-package checksum mismatch; None length mismatch; corrupt/truncated/short/excess/trailing Brotli; unsupported registered schema version. No per-asset digest, dependency slice, cook key, or publication join exists. |
| Physics | Nonfinite-before-constructor, wrong dimension/ID/version/settings/profile, exact versus pre-rounded min, min/max/corner precedence, old SI float kind, malformed/zero/negativezero pair, every count/processing presence, one-bit measure/bounds/radius mismatch, scaled old stats or chunk-only relabel. Known values InvalidRecord; unknown identity/version UnsupportedSchema. |
| Scene | Duplicate/aliasing/prefix effective override keys, optional omission/path/array-index/flags/kind, ordered Entry projection/parent/root-pose coverage, multi/empty roots, 33 nesting/65537 subtree/256+1 hierarchy/cycle/entity/occurrence/bytes, source UUID collisions, every relative/absolute anchor/external-slot presence, root-only shorthand ambiguity, stale queued intent and sequential recomputation. |
| Mesh/material/texture/clip | Wrong stream rank/duplicate UV/mismatched count/joint/weight/stride/topology/group/bounds; profile binding/capability/attribute/alpha mask/depth/output mismatch; mip size/order/data/color/atlas-border/overlap; track composite order/channel/key type/tangent presence/time/domain/events/root-motion/bind predicates. Spot inner/outer equality is a positive hard cutoff; nearest inner>outer, zero and pi32/2 endpoints reject. Native/restore quaternion-bit preservation is distinct from normalized gameplay assignment. |
| Checkpoint | Header.NextTick alone; pub/control/segment cursor alone; scale/segment time/sequence; current/initial/control frame binding/revision; slots/free/retired/live fields/rows/capacity/layout; paged history lengths/ordinal gaps/full-page rules/metadata bits/origin recipes/branch intervals/kind-live joins/reuse; scene-activation/history bijection, original request/result versus transformed ID and recursive namespace; control-branch/directory rewrite; participant/asset/occurrence/source mappings; mode/intent; branch/fork source/target transforms; canonical/checkpoint/file-generation integrity; counts/bytes/coexistence/pending/stale target/post-swap retirement. |
| Continuation | Standard/default intent; missing or source-incompatible record/profile/catalogue/lineage/branch/input policy; tick holes/extent/exhaustion; omitted controls/versions/randomness; each command discriminator/optional/target/writer/token/path case; preassignment stream mismatch, before-stage9 hash mismatch, unrecorded live edit/control and explicit branch exit. Exercise every legal nonempty selected prefix/end, source-too-short and invalid endpoint; full source ReplayDigest remains fixed when the selected prefix changes. Recompute enclosing record integrity while retaining the independently specified violated behavior. |
| Runtime binding/cancellation | Stale/foreign/default Entity, declared versus opaque Guid mapping, revoked/thread/phase binding, poisoned cache, Restore throw/cancel/short consumption, post-Restore mutation, output scratch cancellation and source disposal. No synthetic success or early credit release. |

Known-data negatives must reach the target owner/version contract, not merely fail an old hash or structural count. Record each target invariant and exact expected first diagnostic; multi-fault precedence vectors are separate.

All selected Content schemas are closed over their declared fields; unknown tags/kinds and unregistered schema versions are UnsupportedSchema. There is no common `0xFFFFFFFF` extension field. A header Guid.Empty is contextual InvalidRecord (`invalid-schema`); a nonempty unregistered tuple is UnsupportedSchema. A typed immutable model whose constructor makes an invalid state unreachable is recorded as such, with its constructor/reader-negative proof and legal mismatched-context writer test; never use reflection/unsafe corruption to manufacture a writer input.

Core scanner mutation rows pin its complete category inventory, not a renamed semantic category:

```text
header-magic; unsupported-container-version; header-extent; field-table-extent;
signed-projection-range; header-flags; header-reserved;
field-count-budget; aggregate-field-budget; record-byte-budget;
nesting-depth-budget; aggregate-record-budget;
field-tag-zero; field-tag-duplicate; field-tag-order; field-kind-zero;
field-flags; unsupported-required-kind; scalar-empty; scalar-width; scalar-count;
boolean-domain; byte-count; utf8-byte-budget; utf8-invalid;
range-arithmetic-overflow; field-range-alignment; field-range-in-header;
field-range-in-header-table; field-range-past-end; field-range-overlap;
padding-nonzero; noncanonical-record-layout;
nested-record-extent; nested-record-total;
fixedarray-descriptor; fixedarray-reserved; fixedarray-stride;
array-product-overflow; array-count-budget; aggregate-array-budget; fixedarray-extent;
recordarray-extent; recordarray-nested-total; recordarray-count;
recordarray-first-offset; recordarray-offset-order; recordarray-final-offset;
noncanonical-recordarray-layout
```

Each row pins the corresponding cited EbinCodec source clause/path/prefix and claim/limit/offset. Unsupported container/required kind is UnsupportedSchema; other listed failures InvalidRecord; all consumed0/no visitor. A partial proper UTF8 sequence with room remaining is NeedMoreData, but an impossible declared tail or illegal sequence is utf8-invalid before full arrival. Optional unknown kinds may structurally Complete; an exact closed owner schema does not thereby admit them. Retained-storage-budget and destination-too-small belong to their separate legacy owned-field/writer paths, not a claimed public structural-reader counter.

This inventory describes scanner clauses; it does not claim that every category is independently reachable through every public entry point. Document a shadowed clause by its actual earlier guard rather than creating an unreachable test row; for example, field-entry scalar-width may diagnose malformed width before ScanPayload's scalar-empty branch. Preserve the structural diagnostic's actual populated SchemaId/Tag/offset/claim/limit; do not manufacture ancestor/rule/path details that Core did not report.

For contextual semantic codecs, malformed data under a known schema version returns InvalidRecord, consumed/written0, with the owning SchemaId, physical Tag (0 for record cross-invariants), path and category. Validation visits fixed fields in ascending tag order, declared array elements in order and owner cross-field checks after their required context. Unsupported schema versions or missing typed owner registrations return UnsupportedSchema; context misuse throws before reading. NeedMoreData has no semantic value/diagnostic; DestinationTooSmall follows complete input validation and gives `destination-too-small`, written0/unchanged destination. The context-free codec status interface does not claim to expose diagnostics it lacks; [30](30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) also defines the contextual interface used by owner consumers.

For semantic negatives, Core.Validate/Read under Canonical is Complete with exact full record-prefix consumption and one callback; Core.ReadHeader is Complete64. Owner.Read rejects with the above zero consumption and unusable output. Structural negatives never invoke owner callbacks; their first failure remains the Core category/claim/limit. I/O, actual allocation failures, visitor/codec exceptions and cancellation retain their owner exception/task contracts, not converted successful status objects.

#### M03 10.3 Exact rehash dependency graph

Mutate only the declared semantic target plus mechanically derived bytes/hashes:

1. Rebuild each enclosing canonical EBIN header/table/count/offset/zero padding, without fixing the targeted semantic equality.
2. For an asset mutation, update every declared typed reference affected by the change and preserve the independent expected semantic fact being tested.
3. Re-encode changed payload/envelope bytes, sort the EIDX AssetId entries, check all layout extents, and recompute the single external whole-package SHA-256. There are no per-asset digests, dependency slices, or publication hashes.
4. For checkpoint mutations recompute projection from the mutated actual World/header subset, CanonicalHash, CheckpointDigest, FileContentDigest (including exact TransactionGeneration) and file bytes. Header.NextTick alone is deliberately not copied into Clock. For branch/fork mutations derive all declared target-reference repairs but leave the named failed source/interval/presence join broken; pin the unmutated external source context. Continuation mutations rehash command-stream/record bytes but never repair the deliberately wrong independently expected execution.
5. A fixture intended to test a digest failure omits only that repair. A semantic fixture repairs all unrelated integrity layers.

For example, a shape-pair bit mutation must have a correctly rebuilt package and updated external checksum while retaining the deliberately wrong recomputed statistic. A frame-WorldGuid mutation must have valid new file/canonical/checkpoint hashes but remain different from Header.WorldGuid. Otherwise neither tests the promised semantic invariant.

#### M03 10.4 Core size-boundary tests

Independent arithmetic sources, Core-only where their schema is not a production native root:

```text
Bytes payload65440 -> complete leaf65536; +1 byte ->65552.
Record parent of65552 leaf ->65648.
RA of two65552 leaves: array131136; root131232.
Eight zero-field children -> root688; records9.
Eight empty-Bytes children -> root944; fields9.
Eight children FA(UInt8,count8) -> root1328; aggregate elements72.
Depth64 chain ->6112; depth65 ->6208.
```

Exercise exact/lower budget pairs, total records8/9, fields8/9, elements71/72, depth63/64/65, UTF8/individual-array zero/exact/+1, legal constructor limits and rejected limits. Larger explicit budgets must not alter Foundation. A real 128x128 RGBA8 native texture/envelope additionally demonstrates the production >65536 path; a schema-agnostic leaf is not substituted for it.

Use focused C# tests for valid prefixes at the header, field-table, payload and final-byte boundaries, and assert status, consumption, diagnostics and output usability directly. Test trailing bytes against Core's one-record-prefix contract and enclosing exact-consumption refusal. A sampled prefix is not exhaustive evidence, but no per-prefix expected manifest or ordered aggregate hash is needed. Malformed already-provable prefixes need not return NeedMoreData.

Compare byte-producing production writers directly with reviewed canonical literals where the format is specified, including nested records, empty arrays and padding. For a status-returning writer whose owner contract promises returned-failure atomicity, test invalid-input-before-too-small, its exact zero-extent result and byte-for-byte unchanged nonempty 0xA5 destination. Do not extend that guarantee to Prepare scratch after packing begins, native-sized state-provider Write results, or exceptional exits whose owner contract does not promise rollback. Invalid models use legal raw/mutable or finite-invalid construction paths, not unauthorized reflection/unsafe fabrication. Each reader failure follows its exact result-contract status, consumption, diagnostic and value-usability rule; do not normalize distinct API families to consumed0 when their owner contract says otherwise.

#### M03 10.5 Actual consumer/threshold macros

EN095: source scene contains one camera then d root renderables, d=131072/131073; 2D sprites and 3D one-group meshes separately. IDs use exact fixture names, root order is the source sequence, entity maps RFC-sort independently. Place objects at admitted z1 with layers selected. Lights l=256/257 separately, plus multiple-limit precedence. Bone count2048/2049 uses eight256-joint bind-pose instances plus a one-joint instance. Run real Renderer.Extract, not a count helper.

PH09: explicit selectors `M03Primitive2D` and `M03Primitive3D` cover selected unit exponents `-30, 0, 3, 30`, API/geometry boundaries and the specified precision witnesses. Add native-binding cases only when that production consumer exists. `PH09PrimitiveBoundary.M06` adds actual full-profile/owner/handle/storage/mass boundaries when that consumer exists.

EN028/029: exact logical sizes produce row36/cap256 and row32768/cap1 versus32769 refusal; use concrete fixed/inline schemas, not padding estimates. EN026 strict fixtures cover live/free/retired generation uintMax/no-wrap issuance. Near-terminal generation/owner storage uses an isolated declared test seam; it is not a reachable billion-creation lifetime under the33554432 cap. Exercise exact configured thresholds/+1 and admitted owner maxima where reachable; failed structural issuance preserves root/assignment cursor/history and permits retirement. It does not refund an already admitted owner-request cursor or restore capture eligibility after a canonical write grant.

M03EntityIdentityBudget follows current chapter28 exactly: uninterrupted 10000 initial plus10000*(120+3000)=31210000 assignments, with no history reset or World replacement; separate live/retired scene lane totals33297153 assignments including its top-level instance and leaves257279. Measure all five processes/early-middle-late/full windows, actual index/page/name-format allocation and whole preflight cost, complete checkpoint sizes, shared/coexistence/candidate/remap/quarantine peaks. These are planned observations, not inferred memory/latency passes.

Scheduler comparison endpoint uses the same4096 uniquely ordered systems across four full-overlap resource groups: all4096 write groups1/2, systems0..90 write group3, system0 writes group4/system1 reads it. Charge16773120+4095+1=16777216; adding system2 Read to group4 gives16777217. Keep fresh PartitionCount1 resources and exact metadata/artifacts. The full-overlap hazard ordering can be represented by sufficient adjacent chains without retaining millions of redundant edges, but the charge remains every pair. Real preflight and actual graph/registration reservations must reach the target; failure at another budget is not a comparison-ceiling pass.

Run the two separate public NativeAOT consumers through native tooling then headless World/store/scene/Step/Extract/save/prepare/replace/retirement, on both supported x64 targets when implemented. Public construction/closed registration, generated roots, actual source-adapter/device exclusion and executed counts are distinct from unit fixture agreement.

## 3. EPKG2 content-set package

EPKG2 is one deterministic container for one content set and selected profile. AssetIds are positive 32-bit values unique within that content set. The package directory is the live AssetId-to-payload map; it is not a dependency graph or history record. The whole-package SHA-256 is supplied in the application's deployment configuration and covers every package byte. It is not stored inside the package and is not a signature.

The package header is 64 bytes:

| Offset / bytes | Field |
|---|---|
| 0 / 4 | ASCII `EPKG` |
| 4 / 2, 6 / 2 | Major `2`, minor `0` |
| 8 / 8 | Total package bytes |
| 16 / 8, 24 / 8 | Profile-record offset and byte length |
| 32 / 8, 40 / 8 | EIDX directory offset and byte length |
| 48 / 4 | Asset entry count |
| 52 / 4 | Flags, zero in v1 |
| 56 / 8 | Reserved zeros |

The single profile record is one canonical `foundation/profile` EBIN record and applies to the package content set. Packaging separate selected profiles produces separate packages.

The EIDX directory starts with a 64-byte header: magic at 0; directory version `1` and payload alignment `16` as UInt16 at 4/6; entry count and flags as UInt32 at 8/12; entry-table offset and byte length as UInt64 at 16/24; total directory bytes as UInt64 at 32; reserved zeros at 40..63. The only table in v1 is the entry table. Each entry is 32 bytes:

| Offset / bytes | Field |
|---|---|
| 0 / 4 | AssetId UInt32, range 1..2147483647 |
| 4 / 1 | Codec (`None = 0`, `Brotli = 1`) |
| 5 / 1, 6 / 2 | Entry flags and reserved bytes, all zero |
| 8 / 8, 16 / 8, 24 / 8 | Payload offset, stored length, decoded length |

Entries sort by unsigned AssetId with no duplicates. Each payload is exactly one complete canonical `native/envelope` 2.0 record; the envelope repeats its AssetId at tag 1 and stores the semantic payload record at tag 2. The payload's EBIN header supplies the registered schema ID and version. `None` requires equal stored and decoded lengths. No entry stores a root-schema copy, profile index, dependency slice, per-asset digest, or diagnostic name.

There is no dependency table. Direct owning AssetId references remain in their semantic payloads and resolve only within this content set. The content build checks the required closure it packages; runtime owners validate the references they actually load or activate. A general transitive graph and per-edge version/hash records are not serialized.

One canonical `foundation/profile` record applies to every asset in this package. The package has no per-entry profile index or profile alias table. A different selected profile is built and deployed as a separate package.

The package layout is header, profile record, EIDX directory, then payloads in ascending AssetId order. Each region is aligned to 16 with zero padding.

Payload ranges are positive, nonoverlapping, and in bounds. All arithmetic, reserved bytes, IDs and entry extents are validated before allocation or publication. The store verifies the expected whole-package SHA-256 before making the content set available; a correctly checksummed malformed package still fails structural/schema validation.

Default package limit remains 8 GiB; the directory is `64 + 32 * entryCount` bytes and has at most 131072 entries (4194368 bytes maximum). Directory, decompression, and retained-memory budgets are separate and checked before allocation; the total package limit does not raise them. Random-access chunk hashes, multiple content packages, per-entry digests, signatures, and partial package replacement are not part of v1.

## 4. Streaming, compression, saves, and migration

Large readers validate extents in integer arithmetic and consume owned bounded windows. A schema's fixed-stride arrays may be lazily mapped only after exact alignment/stride/lease/profile proof; otherwise readers copy/convert. Public consumers do not receive a byte pointer, BCL sequence position, or view into a disposed package.

Brotli output cannot exceed declared decoded length or aggregate decode budget. Short output, excess output, truncated/trailing invalid frames, mismatched digest, or canceled incomplete output never becomes a ready asset. Independent blocks are used only by schemas that declare their block table/digests and random-access semantics; an implementer cannot split a whole-stream codec arbitrarily.

Binary save/checkpoint/replay records reuse EBIN schemas and canonical field ordering, with [27](27-state-serialization-replay-and-recovery.md)'s generation, durable flush, atomic replacement, backup, and lineage rules. Text exports are tooling features, not the canonical strict-checkpoint hash. A JSON/XML save importer stages a new binary schema lineage and cannot promise StrictLocal continuation from unspecified textual float/order data.

The current package path accepts EPKG2 only. It has no EPKG1/JSON index reader or schema migration catalogue; unsupported package/container/schema versions reject. Text input adapters remain owned by their source-format consumers and do not create an alternate runtime package path.

## 5. Acceptance obligations

| ID | Required observation |
|---|---|
| BN01 | Golden EBIN/header/table/geometry/GUID/UTF-8 byte fixtures round-trip under separate readers/writers; no CLR padding/native alias assumptions |
| BN02 | Exact field tags/kinds/strides/defaults/schema versions are frozen before reader implementation; unknown required cases reject |
| BN03 | JSON/XML/native authoring of equivalent semantics cooks equivalent canonical data without format-dependent runtime APIs |
| BN04 | EPKG2/EIDX1 has a single AssetId-sorted directory; bounds, ordering, overflow, reserved bytes, codec lengths, compiled profile, and the external whole-package SHA-256 are validated before activation. Package, directory, decode, and retained-memory limits are checked independently |
| BN05 | Compression bombs, truncation, trailing invalid data, cancellation, invalid UTF-8, duplicate fields, and malicious external XML resolution fail observably |
| BN06 | Large windowed reads/mappings retain storage until actual completion and respect aggregate package/decode budgets |
| BN07 | The selected package contract is EPKG2/EIDX1 with one current foundation profile. No legacy EPKG1 reader or schema migration catalogue is part of this package path |
| BN08 | Representative content measures load latency, allocations, cooked/compressed bytes, and edit/cook round trips against the text baseline |
| BN09 | Independent UUID recipe fixtures cover exact RFC namespace byte order, case/length/domain separation, all built-in keys, version 8/variant bits and the frozen defaults. Invalid names/colliding assignments/refused unknown recipes fail before publication. Encode signed exponent endpoints/tag 10/major 2 in `foundation/profile`; EPKG2 has one package-level profile record and no profile table. Legacy UUIDv5 identities are never silently regenerated or reinterpreted as current assignments |
| BN10 | Degree/turn/radian scalar and Euler source envelopes survive JSON/XML edit/save/cook, signed zero and untouched-display/other-property edits. Reject duplicate/stale bindings, wrong unit/arity/version and changed canonical digest; migrate old source interpretation only explicitly. Runtime bytes contain existing canonical fields, never raw Angle/editor envelopes |