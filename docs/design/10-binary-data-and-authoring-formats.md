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

## 2.1 M02 foundation-codec contract

> **Status:** This section is the accepted normative M02 foundation-codec contract. The descriptor, validator, kernel, digest-scope and persistence-binding decisions below are fixed. Candidate vectors in P7 remain candidates until their literal bytes and semantic facts are independently derived and reviewed; that review accepts fixture facts, not a separate implementation. This contract does not establish M02 completion. The existing EBIN1 layout/wire kinds, built-in keys/UUID recipe, foundation profile major 2/tags, frame tags and default profile assignment remain unchanged.

Fixed fields use EBIN's 32-bit tags. Optional components use the reserved extension set and signed 64-bit IDs: positive IDs are reserved for built-ins, negative IDs for user-defined extensions, and zero is invalid. The owning schema version fixes semantics and any required-ID set; optional instances carry no requiredness bit, and a later version may promote an ID to required. Unknown optional payloads remain inert and preservable. This is not permission for an implementation to invent a new tag order for fixed built-ins or ship a payload codec without its milestone's locked schema contract.

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

These foundation schemas are closed. Every admitted field is in the selected schema-version descriptor; unknown tags reject rather than entering a generic extension container. Profile tag 4 remains reserved and forbidden in major 2. Fixed scalar/geometry widths, array descriptors and field flags remain as defined by EBIN1. RecordArray element schemas are explicitly `registry/schema`, `registry/field` and `kernel/recipe` for their respective fields, never an inferred heterogeneous CLR collection.

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

Frame records are closed under their selected schema version. Read-derived state retains the exact admitted frame value needed for capture; unknown tags are unsupported rather than opaque sidecar state. A newly authored frame contains exactly its declared fields. No public sidecar or borrowed package view is introduced.

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

