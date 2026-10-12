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

### M03 2 Preserved foundation and reader boundary

Core Major/Minor remain `int`. Wire major 0, major above `2147483647`, or minor above that value rejects with `InvalidRecord`, category `signed-projection-range`, consumed zero. This is recursive and prefix-aware: major is at bytes48..51 and minor52..55; an otherwise legal 52-byte prefix proving major0/high bit, or 56-byte prefix proving minor high bit, already rejects without a complete header/body. Earlier incomplete legal bits still NeedMoreData. Representable unregistered tuples are a later semantic unsupported-schema result. Public scanning does not register schemas or impose finite geometry semantics.

`ReadHeader.Complete` consumes 64, even without the body; it does not validate nested records or a semantic value. Full Read/Validate consumes one complete prefix, not trailing bytes. Enclosing owners require exact length before publication. Non-Complete outputs are unusable and consume zero. Read's action executes only after recursive structural validation; an action exception propagates, not a fabricated successful receipt. Nested views borrow validated storage and do not reset budgets.

Root depth is one; child records increase it; array containers do not. Count every physical record, field and FixedArray/RecordArray element once through one shared scan state. Root byte length already includes child bytes; do not double-charge an alleged aggregate-record-byte sum.

New typed Content models that contain a Foundation record retain its complete admitted canonical record bytes alongside the parsed Foundation value, including inert optional fields. A native reader/writer, hash projection or restore cannot silently drop those fields because the existing Foundation value type has no place for them. Programmatic authoring emits the known M02 fields; a read-derived model preserves its admitted optional field tags/kinds/flags/counts/payloads. An explicit frame/fork transformation replaces only its named known fields and repacks canonically while preserving those inert fields. Their bytes affect the containing envelope/checkpoint hash, not M02 RegistryDigest, KernelDigest, settings identity or Default. This is a Content ownership wrapper, not a change to existing M02 value types, semantic rules or wire assignments; optional inert fields acquire no reference or executable authority.

The explicit owner limits are:

| Use | Record bytes | Fields | Depth | Individual array elements | UTF-8 bytes | Aggregate records / fields / elements |
|---|---:|---:|---:|---:|---:|---:|
| Existing Foundation | 65536 | 64 | 64 | 1048576 | 1048576 | 1048576 / 1048576 / 1048576 |
| Content descriptors | 16777216 | 256 | 64 | 1048576 | 1048576 | 262144 / 1048576 / 4194304 |
| Trusted native asset / cook metadata | 268435456 | 256 | 64 | 1048576 | 1048576 | 4194304 / 8388608 / 16777216 |
| Complete save file | 2147483584 | 256 | 64 | 2147483584 | 1048576 | 33554432 / 134217728 / 2147483584 |

These are additionally bounded by actual owner allocations/reservations. The save limit covers the complete `state/file`, not a World payload plus uncounted headers. It is the current chapter-27 2-GiB-minus-64 limit, representable by Core's signed record-size contract; the initial structural scan is noninterruptible. Check cancellation before it and on semantic entry, retain ownership until actual unwind, and measure scan/semantic/cancellation latency separately. Fixed foundation records still use the selected M02 semantic contract inside a larger caller.

### M03 3. Complete descriptor notation and expansion

The following chapter-10 tables and rules, together with exactly the seven existing declarations in [chapter 27 M03 8.5](27-state-serialization-replay-and-recovery.md#m03-85-explicit-restore-intent-and-finite-standalone-continuation), are the entire normative `C0` source. Chapter 27 remains the sole authoritative table for those declarations, their fields, assignments and continuation/command semantics; the existing order-rule uses are expanded once in M03 3.3.1 below. No unlisted audio/font/UI/shader-bytecode/input/solver/playback state is selected. Their real consumers require their own approved catalogue extension before implementation. Mesh/material/texture/atlas/skeleton/clip data, scenes/prefabs/cameras, primitive collision data, CPU render components and standalone committed persistence are included.

#### M03 3.1 Identity, kinds, cardinalities and annotations

For every key `k`, `S(k)` is exactly the frozen chapter-10 name UUID algorithm with namespace `2ed8b750-e1a0-4b46-9000-000000000001`, exact ASCII `k`, length-prefix/domain separator, first 16 SHA-256 bytes and specified UUIDv8/RFC variant bits. No case/Unicode/path normalization. The four Physics assignments in [M03 6](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory) override no algorithm: they are the existing exact names/outputs.

Every schema below is 1.0 except the `native/envelope`, `native/scene`, `native/prefab`, `native/hierarchy` and `native/hierarchy-node`, which are 2.0. The occurrence-qualified native scene/prefab major follows the current chapter-16 owner, not the old unaccepted 1.0 rows. A reference is `(S(key), major, minor, Scope)`; Scope is Content=0 or Foundation=1. Foundation references name the unchanged M02 tuple, not a local content declaration.

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
O(keys;sorted;unique) = one ordered rule use, as specified below
RR(n,...) = ordered record rules; omitted means empty
```

Scalar/geometry/Record cardinality is always [1,1]. T/B counts are byte counts. Numeric bounds are encoded as same-kind literal records, inclusive on both sides for `[a..b]`; a stated strict inequality uses the corresponding false inclusivity flag. F/geometry endpoints are exact bit values where specified. Integer endpoints are exact integers. `F>0` uses positive-zero lower bound, exclusive, and no upper bound.

Constant notation is not left to an emitter's choice. `U8/U16/U32/U64=c` and `Bool=false/true` produce rule 5 with the singleton UInt64 value c or 0/1. `I8/I16/I32/I64=c` and `F=c` produce rule 4 with identical inclusive same-kind lower/upper literals. Braces with ranges, such as `{0,1..11,14..21}`, enumerate every integer inclusively, sort and deduplicate; they do not produce rule 4. `F>=0` has lower positive zero inclusive and no upper; `Vn>=0`/`Vn>0` do the same componentwise with a Vn positive-zero literal. A scalar bound on Vn broadcasts to all n lanes in its Vn literal. `abs[...]` selects RangeDomain 1; otherwise domain 0. `pi32` is bits `0x40490FDB`, and `pi32/2` is `0x3FC90FDB`. Simple decimal defaults in these tables are exact rational numbers rounded once to binary32 nearest-even; `D(0)` means positive zero, while explicitly negative zero means bits `0x80000000`. No bound/default uses host-language double intermediates.

Every field gets primary `ValidatorId = S("validator/content/field-contract/v1")`, primary version 1. It checks its intrinsic presence/flags/kind/count/stride/nested tuple. The ordered extra rules are generated as:

1. G: rule 2; ordinary F/V2/V3/V4/Q/M32/M4/P2/P3: rule 3.
2. Q adds rule 7; P2/P3 adds rule 9; declared unit-vector3 adds rule 8.
3. Declared numeric range adds rule 4; enum adds rule 5; mask adds rule 6. No undeclared range is invented.
4. Linear HDR color annotations add rule 51 where RGB>=0/alpha<=1 is not already the complete stated [0,1] range.
5. T marked ASCII adds rule 10.
6. Each O annotation adds rule 11, in written order. Mesh streams and clip tracks instead add rules 44/45.

FA applies these extra rules to each actual element as ElementRules, not to its descriptor; its outer Rules contains only outer ordering if stated. RA/R has no element scalar rules. Literals are the sole raw-bit exception: their tag-1 value gets only the primary structural field rule, no finite/nonempty/canonical value rule. Their value is checked against its destination when used as a bound/default.

Field metadata expands to all 17 tags in [M03 3.2](10-binary-data-and-authoring-formats.md#m03-32-metadata-records): exact count pair, ordered Rules, optional Default, exact sorted nested set or role mask, ElementKind/Rules/Stride, explicit unit/reference/flags and primary version. Unused nested/element metadata is required-present empty/zero, never omitted. There is no registration-order contribution.

Record rules have one `content/rule-use` each in RR order; its version is 1 and parameters are the literal parameter record defined for that rule. The four Physics uses carry their fixed dimensional parameter records. Rule-use lists are sequences, not GUID-sorted sets; repeated IDs with different parameters are allowed, duplicate byte-identical uses reject.

All C0 descriptors have ReservedTags empty except native/prefab 2.0's reserved former single-root tag 2; storage NotComponent/restore NotApplicable unless marked Component Persistent, and the stated owner/role. All nine built-in components are Persistent and restore NotApplicable. C0 has no EntityTag or application-owned SystemState/ResourceState/AttachmentState row. Those roles have concrete fixture/application consumers, not dummy built-ins.

An array's Unit/Reference/AuthoringFlags annotation describes its admitted leaves, not its count/container. A Record annotation is meaningful only for the explicitly named semantic wrapper: Asset on `native/asset-reference` is the same owning reference as its tag 1, never a second edge; EngineArea/EngineVolume on `physics/shape-measure` names the pair's dimension. Entity remapping never infers a leaf from an unannotated Guid, a record's kind or a numerical match. Units have no implicit arithmetic validator or extra rule use. Record-rule predicates supply the declared discriminated meaning where a shared key record serves Position versus Scale.

#### M03 3.2 Metadata records

All metadata is Core/Descriptor; metadata fields have no authoring defaults or flags unless a destination field is explicitly annotated in the payload tables. Tags 1-6 of `content/field` retain their current meanings; ordered rule uses are appended, not substituted for tag 6.

| Key | Complete tags | RR |
|---|---|---|
| `content/schema-manifest` 2.0 | 1 Schemas RA{content/schema}[0..4096]; 2 TagSchemas RA{content/tag-schema}[0..256]; 3 Rules RA{content/rule}[0..256]; 4 Imports RA{content/import}[0..64]; 5 CurrentSchemas RA{content/schema-ref}[1..4096]; 6 Migrations RA{content/migration}[0..4096]; 7 Foundation R{content/foundation-binding}; 8 CompositionVersion U32=1; 9 SemanticProfiles RA{content/semantic-profile}[0..64] | 13 |
| `content/schema` 2.0 | 1 SchemaId G/Schema; 2 Major U32[1..I]; 3 Minor U32[0..I]; 4 Fields RA{content/field}[0..256]; 5 Role U8{1..13}; 6 Assignment R{content/identity}; 7 ReservedTags FA(U32)[0..256]; 8 RecordRules RA{content/rule-use}[0..16]; 9 Storage U8{0,1,2}; 10 TransientRestore U8{0,1,2}; 11 Owner U8{0..9} | 12 |
| `content/tag-schema` 2.0 | 1 SchemaId G/Schema; 2 Major U32[1..I]; 3 Minor U32[0..I]; 4 Owner U8{0..9}; 5 Assignment R{content/identity} | 12 |
| `content/field` 2.0 | 1 Tag U32[1..4294967295]; 2 WireKind U16{1..24}; 3 Required Bool; 4 MinimumCount U64[0..L]; 5 MaximumCount U64[0..L]; 6 ValidatorId G/Recipe; 7 Rules RA{content/rule-use}[0..16]; 8 Default? R{DefaultSet}; 9 NestedSchemas RA{content/schema-ref}[0..256]; 10 NestedRoleMask U32 mask(0x1FFF); 11 ElementKind U16{0,1..11,14..21}; 12 ElementRules RA{content/rule-use}[0..16]; 13 ElementStride U32[0..64]; 14 Unit U8{0..12}; 15 Reference U8{0..19}; 16 AuthoringFlags U8 mask(3); 17 ValidatorVersion U32[1..I] | 12 |
| `content/schema-ref` | 1 SchemaId G/Schema; 2 Major U32[1..I]; 3 Minor U32[0..I]; 4 Scope U8{0,1} | 12 |
| `content/identity` | 1 Id G; 2 Kind U8{1,2}; 3 Namespace? G; 4 Key? T ASCII[1..1024]; 5 NameRecipe? U8=1; 6 LiteralAssignmentDigest? B[32..32] | 14 |
| `content/rule` | 1 RuleId G/Recipe; 2 Version U32[1..I]; 3 Assignment R{content/identity}; 4 ParameterSchema R{content/schema-ref}; 5 ApplicableScopes U8 mask(7); 6 RecipeDigest B[32..32]; 7 Owner U8{0..9} | 15 |
| `content/rule-recipe` | 1 RuleId G/Recipe; 2 Version U32[1..I]; 3 Algorithm U16[1..51]; 4 ParameterSchema R{content/schema-ref}; 5 RequiredContexts U8 mask(15); 6 ApplicableScopes U8 mask(7); 7 Owner U8{0..9} | 15 |
| `content/rule-use` | 1 RuleId G/Recipe; 2 Version U32[1..I]; 3 Parameters R{RuleParameterSet} | 15 |
| `content/import` | 1 CatalogueDigest B[32..32]; 2 Purpose U8{0,1} | 13 |
| `content/migration` | 1 SourceCatalogueDigest B[32..32]; 2 SourceSchema R{content/schema-ref}; 3 TargetSchema R{content/schema-ref}; 4 RecipeId G/Recipe; 5 RecipeVersion U32[1..I]; 6 SemanticRecipeDigest B[32..32]; 7 ParameterSchema R{content/schema-ref}; 8 Parameters R@TypedSettings | 13 |
| `content/foundation-binding` | 1 RegistryDigest B[32..32]; 2 KernelDigest B[32..32]; 3 CoordinateCode U32=1; 4 GeometryCode U32=2; 5 SpatialCode U32=2 | 13 |
| `content/semantic-profile` | 1 ProfileId G/Profile; 2 Assignment R{content/identity}; 3 ParameterSchema R{content/schema-ref}; 4 Bindings RA{content/profile-binding}[3..3]; 5 RequiredCapabilities U64 mask(0x1FF); 6 DefaultAlphaMode U8{0,1,2}; 7 DefaultDepthTest Bool; 8 DepthWriteAlphaMask U8 mask(7); 9 DefaultCullMode U8{0,1}; 10 RequiredVertexAttributes U16 mask(0xFF); 11 AllowedAlphaMask U8 mask(7) | 46 |
| `content/profile-binding` | 1 Slot U8{1,2,3}; 2 Assignment R{content/identity} | 46 |
| `content/default-meters` | 1 Numerators FA(I64)[1..3]; 2 Denominator U64[1..L] | 12 |
| `content/default-material` | 1 Property U8{0..5} | 12 |
| `content/parameters-none` | no fields | none |
| `content/parameters-range` | 1 Lower? R{NumericLiteralSet}; 2 Upper? R{NumericLiteralSet}; 3 LowerInclusive Bool; 4 UpperInclusive Bool; 5 Domain U8{0,1} | 47 |
| `content/parameters-enum` | 1 Values FA(U64)[1..256] O(value;true;true) | 47 |
| `content/parameters-mask` | 1 AllowedBits U64 | 47 |
| `content/parameters-order` | 1 Keys RA{content/order-key}[1..8]; 2 RequireSorted Bool; 3 RequireUnique Bool | 47 |
| `content/order-key` | 1 Source U8{0..3}; 2 Path FA(U32)[0..16]; 3 Comparison U8{0..6}; 4 Direction U8{0,1}; 5 Missing U8{0,1,2} | 47 |
| `content/property-segment` | 1 Kind U8{1,2}; 2 Value U32 | 47 |
| `content/parameters-primitive-validation` | 1 Dimension U8{2,3}; 2 MinimumNumerator I64=1; 3 MinimumDenominator U64=40; 4 MaximumNumerator I64=64; 5 MaximumDenominator U64=1; 6 LengthDimension I32=1; 7 LimitPolicy U8=0; 8 CornerPolicy U8=0 | 15 |
| `content/parameters-primitive-statistics` | 1 Dimension U8{2,3}; 2 PiBits U32=0x40490FDB; 3 Precision U8=24; 4 MinimumExponent I32=-512; 5 MaximumExponent I32=512; 6 MeasureRounding U8=0; 7 PairConversion U8=0; 8 RadiusRounding U8=0 | 15 |

`R@TypedSettings` is a Record, not an array: use the TypedSettings role bit and cardinality [1,1]. The same notation is used in cook implementation settings below.

Literal schemas are exactly `content/literal/<suffix>`, tag 1 Value of the following permanent kind, raw bits, no record rule:

```text
u8=1, u16=2, u32=3, u64=4, i8=5, i16=6, i32=7, i64=8,
bool=9, f32=10, guid=11, utf8=12, bytes=13,
vector2=14, vector3=15, vector4=16, quaternion=17,
matrix3x2=18, matrix4x4=19, position2=20, position3=21
```

T/B literal counts are [0..1048576]; other literal counts are [1,1]. NumericLiteralSet is suffixes u8/u16/u32/u64/i8/i16/i32/i64/f32/vector2/vector3/vector4. FixedLiteralSet is all 21 except utf8/bytes. DefaultSet is all 21 plus default-meters/default-material. RuleParameterSet is parameters-none/range/enum/mask/order/primitive-validation/primitive-statistics. Expand sets by these exact keys/versions and sort references by RFC GUID/major/minor/scope.

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
Scopes: Field=1 Element=2 Record=4
Contexts: Profile=1 Catalogue=2 WorldBinding=4 AssetBindings=8
Import purpose: Composition=0 HistoricalSource=1
Assignment: NameRecipe=1 Literal=2; NameRecipe code 1 is the frozen UUIDv8 recipe.
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
Range domain: Numeric=0 AbsoluteComponentMagnitude=1
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

NameRecipe identity requires tags 3/4/5 and forbids tag 6; its Id equals the exact name recipe. Literal identity requires tag 6 and forbids 3/4/5. Its immutable, independently approved assignment receipt has this exact digest preimage:

```text
ASCII("TerraFX.LiteralAssignment.v1") || 00 || RFC(Id) ||
UInt8(Purpose) || UInt32LE(MajorOrVersion) || UInt32LE(Minor) ||
UInt8(Owner) || UInt32LE(specificationByteLength) || exact UTF8 specification
Purpose: DataSchema=1 EntityTag=2 Rule=3 MaterialProfile=4
         Shader=5 Pass=6 Layout=7; non-schema Minor is zero.
```

The receipt is an external immutable approval operand, not a digest of its containing catalogue or a claim of authority made by package data. C0 uses NameRecipe only. Literal-assignment fixtures pin literal specification bytes and the receipt independently; a changed receipt/context cannot authorize an unregistered literal ID.

#### M03 3.3.1 Exact ordering-parameter expansion

This table replaces property-name inference in every O annotation. `G(p)`, `U(p)`, `S(p)`, `T(p)`, `B(p)`, `Tuple(p)` and `Props(p)` mean Source=FieldPath and Comparison=RFCGuid/Unsigned/Signed/UTF8Bytes/DigestBytes/UInt32OrGuidTuple/PropertySegments respectively. A dotted numeric p is the exact tag sequence, and `[]` is the empty path. `H1/H2/H3` mean HeaderSchemaId/HeaderMajor/HeaderMinor with empty paths and RFCGuid/Unsigned/Unsigned comparisons. Every key has Direction=Ascending and Missing=Error, except a written `Before`, which sets Missing=Before. These are encoded key records, in the listed order. The last column gives `(RequireSorted,RequireUnique)`; each row produces one rule-11 use. A field listed twice keeps that use order. Header schema tuples have no fabricated Scope field; nested schema references include their real tag 4.

| Containing schema / field tag | Exact keys | Flags |
|---|---|---|
| content/parameters-enum / 1 | U([]) | true,true |
| native/envelope / 3 | G(1) | true,true |
| native/metadata / 1 | T(1) | true,true |
| cook/graph / 2, 3 | G(1) | true,true |
| cook/graph / 4 | G(1) | true,true |
| cook/node / 3 | G([]) | true,true |
| cook/native-context / 6 | G(1),U(2) | true,true |
| cook/native-context / 7 | U(1),G(2),U(3) | true,true |
| cook/native-context / 9 | G(1) | true,true |
| native/cook-key / 4 | G(1) | true,true |
| cook/package-key / 2, 4 | G(1) | true,true |
| cook/package-key / 6 | G(1),U(2) | true,true |
| cook/package-key / 7 | G(1) | true,true |
| cook/publication / 3 | G(2.1) | true,true |
| cook/publication / 4, 10 | G(1) | true,true |
| native/scene / 2 | G(1) | true,true |
| native/scene / 4; native/prefab / 5 | G(1) | true,true |
| native/scene / 5 | G(1) | true,true |
| native/scene / 6; native/prefab / 6 | U(1.1),Tuple(1.2),G(1.3),G(2.1),U(2.2),U(2.3),U(2.4),Props(3) | true,true |
| native/scene / 7; native/prefab-placement / 5 | U(1.1),Tuple(1.2),G(1.3),G(2.1),U(2.2),U(2.3),U(2.4),Props(3) | true,true |
| native/scene / 8; native/prefab / 8 | G(1) | true,true |
| native/prefab / 3 | G(1) | true,true |
| native/prefab / 7 | G(1) | true,true |
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
| native/clip / 3, use 0 | G(1) | false,true |
| native/clip / 3, use 1 | S(2),G(1) | true,true |
| native/transform-track / 5; native/sprite-track / 2 | S(1) | true,true |
| state/application / 4; state/runtime-profile / 9 | G(1) | true,true |
| state/world / 7 | G(1) | true,true |
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
| state/recorded-tick / 5 | G(1) | true,true |
| state/recorded-tick / 6 | G(1) | true,true |
| state/command-stream / 1 | U(1.1),S(1.2),U(1.3),S(1.4) | true,true |
| state/entity-description / 2, 3 | H1,H2,H3 | true,true |

RequireUnique compares the complete declared key, even when RequireSorted=false. A missing key is never replaced with an empty Guid, zero or empty tuple. Before/After is a distinct comparison sentinel; two missing values compare equal. Array lists with no O annotation retain their declared semantic order and have no inferred ordered-rule use.

Metadata collection sorting is checked by its stated record rule, not an unlisted O use: manifest tags 1/2 use G(1),U(2),U(3), tag 3 G(1),U(2), tag 4 B(1), tag 5 G(1),U(2),U(3),U(4), tag 6 B(1),G(2.1),U(2.2),U(2.3),U(2.4), and tag 9 G(1); schema Fields/ReservedTags use U(1)/U([]); field NestedSchemas uses the four schema-reference keys; profile Bindings uses U(1). All are ascending and unique. RecordRules, field Rules/ElementRules, order Keys, DM Numerators and literal vectors/matrices remain sequences.

#### M03 3.4 Every rule recipe

For generic rule n, ID is S(`validator/content/<name>/v1`), version 1. The four Physics rows use their already assigned keys/IDs. Parameter abbreviations: Z=parameters-none, R=parameters-range, E=parameters-enum, M=parameters-mask, O=parameters-order, V=parameters-primitive-validation, S=parameters-primitive-statistics.

| n / exact name | Parameters | Scopes | Contexts | Owner | Meaning |
|---|---|---:|---:|---:|---|
| 1 field-contract | Z | 1 | 2 | 0 | Intrinsic descriptor, presence/flag/kind/count/stride/nested selection. |
| 2 nonempty-guid | Z | 3 | 0 | 0 | Reject Guid.Empty, no general UUIDv8 restriction. |
| 3 finite | Z | 3 | 0 | 0 | Every declared binary32 lane finite, no hidden fourth Vector3 lane. |
| 4 range | R | 3 | 0 | 0 | Exact bounds/inclusivity; Domain 1 uses exact absolute component magnitude. |
| 5 enum | E | 3 | 0 | 0 | Exact sorted unsigned members, destination width checked; Bool's domain is 0/1. |
| 6 mask | M | 3 | 0 | 0 | Reject bits outside mask and masks wider than destination. |
| 7 canonical-quaternion | Z | 3 | 0 | 0 | Finite copied dyadics; abs(sum(q*q)-1)<=1/100000, no normalization. |
| 8 unit-vector3 | Z | 3 | 0 | 0 | Same exact squared-norm tolerance for three lanes. |
| 9 canonical-position | Z | 3 | 1 | 0 | M02's selected persistent cell/offset form under explicit settings. |
| 10 ascii | Z | 3 | 0 | 0 | Exact ASCII bytes; count bounds remain field metadata. |
| 11 ordered | O | 1 | 0 | 0 | Declared composite keys, sorted/unique independently. |
| 12 descriptor | Z | 4 | 2 | 0 | [M03 3](10-binary-data-and-authoring-formats.md#m03-3-complete-descriptor-notation-and-expansion) complete metadata/default/role/ownership constraints. |
| 13 catalogue | Z | 4 | 2 | 0 | [M03 identity and provenance](10-binary-data-and-authoring-formats.md#22-m03-content-schema-and-package-binding) collection, imports, active selection and migration closure. |
| 14 identity | Z | 4 | 2 | 0 | Name/literal presence and exact assignment source. |
| 15 rule-binding | Z | 4 | 2 | 0 | Rule/version/recipe/parameters/scope/owner correspondence. |
| 16 envelope2 | Z | 4 | 11 | 1 | [M03 identity and provenance](10-binary-data-and-authoring-formats.md#22-m03-content-schema-and-package-binding) envelope/index/version/dependency/profile bindings. |
| 17 native-graph | Z | 4 | 11 | 1 | Pure rooted nominal graph, direct owning edge set and recorded source-fingerprint representation; no original-source I/O. |
| 18 native-cook-key | Z | 4 | 11 | 1 | Pure context/output/payload/dependency/metadata operands and reconstructed graph/context/key hashes. |
| 19 publication | Z | 4 | 11 | 1 | Runtime-verifiable key/output/package/provenance joins and codec-policy branches under exact trusted source pins. |
| 20 scene | Z | 4 | 11 | 3 | [M03 5](10-binary-data-and-authoring-formats.md#m03-5-complete-sceneprefab-catalogue-and-expansion) scene/hierarchy/reference invariants. |
| 21 prefab | Z | 4 | 11 | 3 | [M03 5](10-binary-data-and-authoring-formats.md#m03-5-complete-sceneprefab-catalogue-and-expansion) occurrence/graft/override/limit invariants. |
| 22 camera | Z | 4 | 1 | 3 | [M03 7](10-binary-data-and-authoring-formats.md#m03-7-complete-cpu-meshmaterialtextureanimationrender-data) camera fields/discriminators/default meanings. |
| 23 mesh | Z | 4 | 11 | 4 | Streams, counts, topology, material slots, bounds and static skin. |
| 24 material | Z | 4 | 11 | 4 | Profile/binding/parameter/texture/state/capability correspondence. |
| 25 texture | Z | 4 | 0 | 4 | Mip extents/order/format/data size and declared semantics. |
| 26 atlas | Z | 4 | 8 | 4 | Page/frame/padding/overlap/extruded-byte constraints. |
| 27 skeleton | Z | 4 | 1 | 5 | Ordered topology, bind/inverse-bind and root-motion binding. |
| 28 clip | Z | 4 | 11 | 5 | Typed channel/keys/tangents/events/reference/bounds branches. |
| 29 light | Z | 4 | 1 | 4 | [M03 7](10-binary-data-and-authoring-formats.md#m03-7-complete-cpu-meshmaterialtextureanimationrender-data) dimensional light fields and cone/range relationships. |
| 30 primitive-binding2 | Z | 4 | 11 | 6 | Complete selected 2D profile/settings/identity correspondence. |
| 31 primitive-binding3 | Z | 4 | 11 | 6 | Complete selected 3D correspondence. |
| 32 existing validator/physics/primitives/2d/v1 | V(dimension=2) | 4 | 1 | 6 | [M03 6](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory) copied 2D definition admission. |
| 33 existing validator/physics/primitives/3d/v1 | V(dimension=3) | 4 | 1 | 6 | [M03 6](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory) copied 3D definition admission. |
| 34 existing statistics/physics/primitives/2d/scaled32/v1 | S(dimension=2) | 4 | 1 | 6 | [M03 6](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory) complete 2D recomputation/equality. |
| 35 existing statistics/physics/primitives/3d/scaled32/v1 | S(dimension=3) | 4 | 1 | 6 | [M03 6](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory) complete 3D recomputation/equality. |
| 36 shape-measure | Z | 4 | 0 | 6 | Canonical zero or positive normalized bounded pair. |
| 37 checkpoint | Z | 4 | 15 | 8 | [M03 8](10-binary-data-and-authoring-formats.md#m03-8-complete-standalone-checkpoint-catalogue-and-equalities) root/header/participant/asset/canonical binding. |
| 38 ecs-state | Z | 4 | 7 | 2 | Registration/history/membership/reference/state/capacity constraints. |
| 39 strict-layout | Z | 4 | 7 | 2 | Exact rows/slots/layout/cursors/allocator joins. |
| 40 clock | Z | 4 | 0 | 2 | Exact tick/rate/publication/scale-segment/control relationships. |
| 41 controls | Z | 4 | 7 | 2 | Common control/clock/cursor/frame/paused-step relationships; Scenes rule 50 owns scene cases. |
| 42 pcg | Z | 4 | 0 | 2 | Odd increment and exact PCG32 contract. |
| 43 metadata | Z | 4 | 0 | 1 | Bounded inert ASCII keys and uninterpreted UTF-8 values. |
| 44 mesh-stream-order | Z | 1 | 0 | 4 | [M03 7](10-binary-data-and-authoring-formats.md#m03-7-complete-cpu-meshmaterialtextureanimationrender-data) fixed semantic-rank key. |
| 45 clip-track-order | Z | 1 | 0 | 5 | [M03 7](10-binary-data-and-authoring-formats.md#m03-7-complete-cpu-meshmaterialtextureanimationrender-data) domain/target/channel key. |
| 46 material-profile | Z | 4 | 2 | 0 | Exact [M03 7](10-binary-data-and-authoring-formats.md#m03-7-complete-cpu-meshmaterialtextureanimationrender-data) profile slots/masks/identities. |
| 47 parameters | Z | 4 | 2 | 0 | Type/arity/bounds/order-path/unused-bit applicability. |
| 48 factory-registration | Z | 4 | 2 | 2 | Exact descriptor/factory/state/ownership/cold-create metadata. |
| 49 cook-implementation | Z | 4 | 2 | 1 | Intrinsic producer ID/version/artifact/settings tuple and catalogue-selected settings/policies; no producer loading or executable registration. |
| 50 scene-state | Z | 4 | 15 | 3 | Active occurrence/provenance/hierarchy/external binding joins. |
| 51 hdr-color | Z | 3 | 0 | 0 | V3 channels>=0; V4 RGB>=0 and alpha in [0,1]. |

Primary rule 1 is represented by field tags 6/17, not repeated in Rules. A rule requiring context cannot be run against unchecked context: wire structure and local inputs first, then required binding, then RR order. Root Physics rules are binding, geometry, statistics in that order after local values; geometry fault precedence is exactly [M03 6](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory). No undisclosed record validator exists.

Invocation has an intrinsic, nonserialized execution context: exact descriptor/use index, Field versus Element versus Record site, complete containing root, physical ancestor tag/array-index path and current record. This is not a lookup by CLR type or an additional digest operand. Context bits request separately validated Profile/Catalogue/WorldBinding/AssetBindings. Reused record rules dispatch only on the explicitly listed schema key and execute that key's applicable clauses; a support record is not forced to possess its parent's header fields. A support record requiring its containing root cannot be admitted alone as a usable owner value.

For semantic rule diagnostics, a primary field-contract rule has `UseIndex = -1`; an extra field rule, element rule or record rule reports its zero-based position in that rule-use sequence. Preserve the actual invocation site and physical path; do not shift extra-rule indices to make room for the primary rule. A Core structural scanner failure keeps its actual structural diagnostic and does not acquire a fabricated semantic rule/use index. These conventions affect diagnostics only, not wire fields, rule assignments, recipe digests or first-provable structural scanning.

For binding rules 30/31 on the M03 primitive roots only, after structural/local validation and required-context admission, resolve the root identity members in tag order (including supported versions), then statistics identity members in tag order, dimensional/validator-versus-formula correspondence, root/envelope/trusted profile-tuple agreement, statistics settings agreement, and root/statistics identity-copy agreement. Only then run geometry and statistics recomputation. This ordering is scoped to predicates within rules 30/31; it does not create a global malformed-before-unsupported rule or reorder local validation, required-context admission, other rules, RR sequencing or Core's first-provable scanner. The compound unknown nonempty validator identity plus valid statistics/profile settings mismatch therefore reports `unsupported-identity` before `InvalidRecord` settings mismatch. Numeric version-domain, nonfinite and malformed nested-measure failures remain earlier local failures. Attribute root cross-invariants to the current primitive schema, tag 0, its RR index and physical path to that primitive occurrence; local field failures retain the child schema/tag/path.

Rule-parameter applicability is closed: nonempty-guid accepts Guid; finite accepts F and the actual lanes/offsets of V2/V3/V4/Q/M32/M4/P2/P3; range accepts the eight integer kinds/F/V2/V3/V4, with domain 1 restricted to F/V2/V3/V4 and nonnegative bounds; enum accepts U8/U16/U32/U64/Bool; mask accepts U8/U16/U32/U64; quaternion accepts Q; unit-vector3 accepts V3; position accepts P2/P3; ascii accepts UTF8; hdr-color accepts V3/V4; ordered accepts FA/RA containers. Their advertised Field/Element scopes do not legalize an incompatible destination kind. RR-only rules accept just the records listing them. Parameter record identity/version, scope, destination kind, bound arity and context all validate before values. Bounds must be finite, same-kind, ordered and nonempty after inclusivity; domain 1 bounds are absolute magnitudes. At least one endpoint is present; an omitted endpoint has Inclusivity=false. Enum Values is a nonempty set; mask unused high bits reject. Tangent XYZ uses the unit-vector3 *predicate inside mesh RR23*, not an illegal rule-8 use on V4.

Context phase is Author=0/Cook=1/Load=2/Restore=3, an invocation fact, not a serialized default or rule-recipe operand. Author AssetBindings contains the exact declared nominal AssetId/semantic-schema graph and any supplied exact versions; it admits only canonical input records, never an asset publication. Cook first validates the complete nominal graph, then resolves exact ready dependency versions topologically before output-key/envelope publication. Load/Restore requires exact trusted versions and retained dependency closure. No phase supplies invented future output digests. Catalogue context includes trusted bootstrap descriptors and explicit executable binding/recipe registrations; data cannot add a registration. WorldBinding is required only at the actual World/candidate boundary, never supplied by Assets or a guessed WorldGuid.

RecipeDigest is SHA-256 of the complete canonical `content/rule-recipe` with tags 1-7 above. For each row encode its exact ID/version/algorithm/parameter-schema/context/scope/owner, including required zero values. It contains no catalogue digest, binary artifact hash or self-hash. A local content/rule stores that digest and the matching assignment/parameter/scopes/owner. Semantic changes require a new identity/version and owner-reviewed catalogue transition, not editing the algorithm behind the existing ID.

C0 `parameters-none` is exactly a complete 64-byte zero-field EBIN record. R lower/upper kinds must match the destination; omitted bound has Inclusivity=false. E values strictly ascend and fit the unsigned field. O includes each key in declared sequence, Ascending unless stated, Missing=Error unless stated. These rules also pin the otherwise byte-affecting zero flags.

#### M03 4.1 Exact C0 and collection rules

C0 is `content/schema-manifest` 2.0 with:

```text
Schemas = every non-EntityTag schema in sections 3-8, including literal family,
          plus exactly the seven declarations in chapter 27 M03 8.5;
          complete expanded descriptor rows, sorted RFC SchemaId/major/minor.
TagSchemas = empty.
Rules = all 51 rows above, sorted RFC RuleId/version.
Imports = empty.
CurrentSchemas = exactly one Content-scope tuple for every local schema GUID,
                 sorted RFC GUID/major/minor; all table-selected versions.
Migrations = empty.
Foundation = the unchanged M02 Registry/Kernel digests and codes 1/2/2.
CompositionVersion = 1.
SemanticProfiles = the two complete rows in section 7, sorted RFC ProfileId.
ContentSchemaDigest = SHA256(all complete canonical manifest bytes).
```

C0 has exactly 198 DataSchema rows with 851 field rows, zero TagSchema rows, 51 rule rows, 198 active Content tuples and two semantic-profile rows. The 198 includes 170 individually tabulated Chapter 10 schemas with 790 fields, the seven authoritative Chapter 27 schemas with 40 fields, and 21 literal-family schemas with one field each. The Chapter 10 M03 3.3.1 ordering table already contains the seven records' existing rule-11 uses; do not add or infer duplicate uses. Fixture CApp1 has 14 local DataSchema rows, one local EntityTag and no local rules/profiles; its effective set is 212 data schemas plus that tag, and its complete CurrentSchemas has 213 tuples. These are source-inventory counts, not accepted descriptor lengths/hashes.

The digest is not a signature, an executable grant or semantic-equivalence hash. A flattened declaration set and an imported composition can have different digests even with identical effective types.

Local declarations are local, not a misleading partly-effective list. Each DataSchema, TagSchema, rule, profile and active selection has an exact collection/key scope. Imports sort by digest bytes; duplicate digests reject even if purposes differ. Fields and reserved tags sort numerically; all disjointness/duplicate constraints reject.

Composition imports resolve exact approved manifest bytes by digest and inherit declarations, rules, profiles and active selections. Byte-identical diamond declarations coalesce; conflicting bytes, owners, recipes, profiles or selections reject. A local declaration of an imported identity/version rejects even if identical. A schema GUID cannot be both tag and data or have conflicting roles/owners. Content declarations cannot reuse any of the eight frozen Foundation schema GUIDs or alias its rule assignments; Scope does not authorize a second meaning for a preserved identity.

The complete Composition/HistoricalSource closure is bounded too: at most64 distinct manifest digests including the root,4096 import edges and16777216 aggregate canonical manifest bytes, counting each digest once. Effective declarations are at most4096 data tuples/256 tag tuples/256 rule tuples/64 profiles and4096 active GUID selections. Historical closures obey the same admission rather than becoming an unbounded archive. Validate the closure, counts, conflicts and actual owned/reserved storage before copying/expanding descriptors. These catalogue-specific ceilings do not alter Core's per-scan counters or M02's registry.

CurrentSchemas is the complete effective active table: every inherited selection must appear identically; no omission/deactivation or imported-active replacement in CompositionVersion 1. Local multiple historical versions may be declared, but exactly one tuple per effective schema GUID is active. HistoricalSource imports retain their own source closure/readers and do not merge active selections into the destination.

Schema/value references resolve only in their declared Content/Foundation scope. R/RA union members must be active, except an explicitly registered historical migration source. Component/Tag API SchemaVersion selects major, minor zero. No implicit latest/additive-minor read. Chapter 26's general compatible-minor rule applies only where a schema owner admits that explicit migration; it does not bypass C0 or the component/tag API.

CApp imports exact C0, adds explicit application declarations/tags using the approved rule recipes, and writes the complete inherited-plus-local CurrentSchemas table. Its digest changes, not M02 RegistryDigest/Default. The finite fixture CApp adds no rule algorithms or local rule aliases; a real custom algorithm needs its own approved descriptor/recipe extension and rooted implementation, not permission inferred from Owner=Application. An asset catalogue is permitted in a World only if it is the World catalogue or reachable via Composition imports with the same inherited active tuples. Historical imports do not authorize ordinary activation.

There is one next migration edge for each exact `(source catalogue digest, source schema tuple)`, no cycles, <=16 steps. Its target must be current in the declaring destination; that destination's digest is implicit, not a self-hash field. Registered pure code must match its ID/version, parameter schema/values and semantic digest. Ordinary loading does not migrate; Standard invokes an explicitly requested chain, validates output and starts a new lineage; StrictLocal refuses.

The semantic migration operand is:

```text
ASCII("TerraFX.PureMigration.v1") || 00 ||
RFC(recipeId) || UInt32LE(version) ||
EBIN(sourceRef) || EBIN(targetRef) || EBIN(parameterSchemaRef) ||
UInt32LE(specificationByteLength) || exact immutable UTF8 specification bytes
```

The immutable specification is approved build input, not package-executed text. C0 contains no migration edges, including no promise that EPKG1/old envelope/profile data is already supported.

#### M03 4.2 Transport/provenance records

Owners/roles: native envelope/dependency/reference/metadata are Assets/Auxiliary. The envelope is an explicitly selected transport root, deliberately not a semantic NativeAsset role member. Native output-policy/cook-key and every cook/ record below are Assets/Cook, except cook/native-settings, cook/empty-settings and cook/brotli-settings, which are Assets/TypedSettings. Cook is a data-role code, not an assembly grant. These immutable facts, canonical codecs/hash projections and rules17/18/19/49 are in Assets and use only Core/BCL. Tools constructs/consumes those public facts and owns offline orchestration, not a second publication verifier; [M03 4.4](11-assets-import-streaming-and-packages.md#m03-44-runtime-owned-provenance-model-and-common-verification-kernel) gives the exact owner map.

| Key | Complete tags | RR |
|---|---|---|
| `native/envelope` 2.0 | 1 AssetId G/Asset; 2 Profile R{foundation/profile 2.0}; 3 Dependencies RA{native/dependency}[0..N] O(AssetId;true;true); 4 Payload R@NativeAsset; 5 ContentSchemaDigest B[32..32] | 16 |
| `native/dependency` | 1 AssetId G/Asset; 2 ContentDigest B[32..32]; 3 SchemaId G/Schema; 4 SchemaMajor U32[1..I] | none |
| `native/asset-reference` | 1 AssetId G/Asset ^pa; 2 SchemaId G/Schema; 3 SchemaMajor U32[1..I] | none |
| `native/metadata` | 1 Entries RA{native/metadata-entry}[0..256] O(Key/UTF8;true;true) | 43 |
| `native/metadata-entry` | 1 Key T ASCII[1..128]; 2 Value T[0..4096] | 43 |
| `cook/graph` | 1 RootAssetId G/Asset; 2 Nodes RA{cook/node}[1..131072] O(AssetId;true;true); 3 SuppliedDependencies RA{cook/dependency}[0..524288] O(AssetId;true;true); 4 Sources RA{cook/source}[0..65536] O(SourceId;true;true); 5 Profile R{foundation/profile}; 6 CatalogueDigest B[32..32]; 7 Convention R{cook/source-convention} | 17 |
| `cook/node` | 1 AssetId G/Asset; 2 Payload R@NativeAsset; 3 DependencyIds FA(G)[0..N] O(value/RFC;true;true); 4 Metadata? R{native/metadata} | none |
| `cook/dependency` | 1 AssetId G/Asset; 2 EnvelopeDigest B[32..32]; 3 RootSchema R{content/schema-ref}; 4 PayloadSchema R{content/schema-ref}; 5 CatalogueDigest B[32..32]; 6 Profile R{foundation/profile}; 7 NativeKeyDigest B[32..32]; 8 RecipeId G/Recipe; 9 RecipeVersion U32[1..I] | none |
| `cook/source` | 1 SourceId G; 2 ContentDigest B[32..32]; 3 ByteCount U64[0..268435456]; 4 LogicalName? T[1..1024]; 5 LicenseExpression? T[1..1024] | none |
| `cook/source-convention` | 1 Basis U8=0; 2 UnitExponent I32[-30..30]; 3 Matrix U8=0; 4 TextureOrigin U8=0; 5 TimeDomain U8=0; 6 AlgorithmId G/Recipe; 7 AlgorithmVersion U32[1..I] | 18 |
| `cook/implementation` | 1 Id G/Recipe; 2 Version U32[1..I]; 3 ArtifactDigest B[32..32]; 4 Settings R@TypedSettings | 49 |
| `cook/native-settings` | 1 Validation U8=0; 2 Atlas U8=0; 3 MeshConditioning U8=0; 4 AnimatedBounds U8=0; 5 SceneExpansionRecipeId G/Recipe; 6 SceneExpansionRecipeVersion U32=2 | 49 |
| `cook/empty-settings` | no fields | none |
| `cook/brotli-settings` | 1 Quality U8[0..11]; 2 Window U8[10..24] | 49 |
| `cook/validator-use` | 1 Scope U8{0,1}; 2 RuleId G/Recipe; 3 Version U32[1..I]; 4 RecipeDigest B[32..32]; 5 Implementation R{cook/implementation} | 49 |
| `cook/native-context` | 1 GraphDigest B[32..32]; 2 Profile R{foundation/profile}; 3 CatalogueDigest B[32..32]; 4 Convention R{cook/source-convention}; 5 Recipe R{cook/implementation}; 6 Tools RA{cook/implementation}[0..64] O(Id,Version;true;true); 7 Validators RA{cook/validator-use}[1..256] O(Scope,RuleId,Version;true;true); 8 OutputPolicy R{native/output-policy}; 9 Sources RA{cook/source}[0..65536] O(SourceId;true;true) | 18 |
| `native/output-policy` | 1 Format U8=0; 2 SchemaPolicy U8=0; 3 Padding U8=0; 4 AssetIdPolicy U8=0 | 18 |
| `native/cook-key` | 1 OutputAssetId G/Asset; 2 ContextDigest B[32..32]; 3 PayloadInputDigest B[32..32]; 4 Dependencies RA{cook/dependency}[0..N] O(AssetId;true;true); 5 MetadataDigest? B[32..32] | 18 |
| `cook/native-key-record` | 1 KeyDigest B[32..32]; 2 Key R{native/cook-key} | 19 |
| `cook/published-output` | 1 AssetId G/Asset; 2 EnvelopeDigest B[32..32]; 3 RootSchema R{content/schema-ref}; 4 PayloadSchema R{content/schema-ref}; 5 CatalogueDigest B[32..32]; 6 NativeKeyDigest B[32..32]; 7 Profile R{foundation/profile}; 8 RecipeId G/Recipe; 9 RecipeVersion U32[1..I] | 19 |
| `cook/package-entry-policy` | 1 AssetId G/Asset; 2 Codec U8{0,1}; 3 Compressor? R{cook/implementation} | 19 |
| `cook/package-key` | 1 RequestRootAssetId G/Asset; 2 Outputs RA{cook/published-output}[1..131072] O(AssetId;true;true); 3 PackageId G; 4 Policies RA{cook/package-entry-policy}[1..131072] O(AssetId;true;true); 5 Recipe R{cook/implementation}; 6 Tools RA{cook/implementation}[0..64] O(Id,Version;true;true); 7 DiagnosticNames RA{cook/package-name}[0..131072] O(AssetId;true;true) | 19 |
| `cook/package-name` | 1 AssetId G/Asset; 2 Name T[0..1024] | 19 |
| `cook/output-metadata` | 1 AssetId G/Asset; 2 Metadata R{native/metadata} | 19 |
| `cook/publication` | 1 RequestRootAssetId G/Asset; 2 Context R{cook/native-context}; 3 Keys RA{cook/native-key-record}[1..131072] O(Key.OutputAssetId;true;true); 4 Outputs RA{cook/published-output}[1..131072] O(AssetId;true;true); 5 PackageKey R{cook/package-key}; 6 PackageKeyDigest B[32..32]; 7 PackageId G; 8 PackageDigest B[32..32]; 9 PackageByteCount U64[128..8589934592]; 10 Metadata RA{cook/output-metadata}[0..131072] O(AssetId;true;true) | 19 |

R@NativeAsset admits active, explicitly registered semantic roots: the C0 roots in sections 5-7 and any independently approved/rooted CApp NativeAsset, including the fixture literal carrier. It cannot admit `native/envelope`, whose role is Auxiliary. Role metadata never supplies an executable codec; missing typed registration is unsupported, not dynamic dispatch. This removes the old recursive-envelope loophole without a hidden exception to role-mask encoding.

Metadata keys are ASCII letters/digits/`.`/`_`/`-`; values are uninterpreted user UTF-8. Aggregate encoded key/value bytes <=65536 per metadata record. No path/resolver/code authority. Node metadata absent means no output-metadata row/key MetadataDigest; present-empty means one row and its actual empty-record digest.

Convention codes mean canonical LH/Y-up/+Z, row-vector matrices, top-left texture coordinates, native TimeSpan ticks; UnitExponent equals the selected profile. Algorithm is S(`conversion/native-preserve-canonical/v1`), version 1. No source conversion runs on native data. Native settings mean PreserveCanonical/ValidateSupplied/None/UnqualifiedAuthoredBounds; their complete scene expansion operand is S(`expansion/native-scene-occurrences/v2`), version 2, with [M03 5](10-binary-data-and-authoring-formats.md#m03-5-complete-sceneprefab-catalogue-and-expansion)'s exact immutable algorithm. Its artifact is pinned through the invoked scene-validator implementation closure. Output policy means canonical EBIN/exact selection/minimal zero padding/preserve graph IDs.

The root is unique and all submitted nodes must be reachable from it through direct owning asset-reference/RequiredAssets edges. Root request ID equals graph RootAssetId and that node's publishable ID synchronously before admission; disagreement is ArgumentException naming assetId. Node DependencyIds equals the deduplicated direct owning edge set; prefab/texture/mesh references are owning, symbolic scene-entity bindings are not. SuppliedDependencies is exactly the distinct direct dependency IDs outside Nodes, with their full versions; extras/missing rows/internal-ID conflicts reject. Each supplied version independently pins/verifies its own transitive closure, not extra unreferenced Graph rows. This makes the later Graph reconstruction exact. Cycles/unreachable nodes/missing versions reject.

At cook admission, graph sources refer to retained exact byte credits; Tools recomputes their lengths/hashes, not caller claims or timestamps, before forming the admitted graph/context. At runtime, Sources is bounded inert fingerprint/provenance data and graph reconstruction copies those exact recorded values; it does not claim the absent originals were verified. Implementation facts are exact ID/version/artifact/settings tuples, sorted as above. Validator-use scope0 references content rule recipes; scope1 references unchanged M02 rules. Include the complete reachable validation closure for the selected envelope/payload descriptors, all declared nested branches, defaults and catalogue/profile admission. Tools proves actual invocation/artifact/settings against its trusted build registration; runtime checks the approved publication's recorded tuple/use/recipe joins and executes its own rooted semantic kernels. No label installs code or proves historical execution. [M03 4.4](11-assets-import-streaming-and-packages.md#m03-44-runtime-owned-provenance-model-and-common-verification-kernel) distinguishes these evidence levels without changing the hash preimages.

Native recipe ID is S(`recipe/native-preserve-canonical/v1`), version1, with exactly `cook/native-settings`' four zero policies and complete expansion identity/version; package recipe is S(`recipe/package-canonical-epkg2/v1`), version1, with `cook/empty-settings`. Their actual implementation artifact digests are mandatory producer-vector inputs when production executes, not values derivable from this contract. Runtime consumes their exact inert digests under the trusted publication/source pin and does not need those offline artifacts installed. Context profile/catalogue/convention/sources equal graph values exactly, and Context.GraphDigest hashes that exact graph. Tools/validators record the exact approved invoked closure: catalogue/descriptor admission validates its declared possible branches, while payload checks use actual present branches; neither padding unused IDs nor omitting that declared closure is allowed. Runtime checks that complete declarative closure and each rule/version/recipe/settings join through Assets-owned kernels and registered owner codecs, not by invoking the producer labels. Native context excludes compressors/package-only tools. The preserve-canonical native producer validates all input semantic bits and emits them unchanged; material defaults/Physics statistics are explicit admitted input, not hidden output repairs.

For a Foundation rule Ri, its provenance-only cook binding digest is:

```text
SHA256(ASCII("TerraFX.M02RuleBinding.v1") || 00 ||
       RFC(Ri) || UInt32LE(1) || UInt8(i) ||
       M02RegistryDigest || M02KernelDigest)
```

This does not modify Ri, M02 recipes, or their accepted operands.

The exact DAG of hashes is:

```text
GraphDigest = SHA256(EBIN(cook/graph))
ContextDigest = SHA256(ASCII("TerraFX.NativeCookContext.v1") || 00 || EBIN(context))
PayloadInputDigest = SHA256(the complete canonical input semantic record)
MetadataDigest = SHA256(EBIN(metadata)), only when present
NativeKeyDigest = SHA256(ASCII("TerraFX.NativeCookKey.v1") || 00 || EBIN(key))
EnvelopeDigest = SHA256(all complete canonical native/envelope bytes)
PackageKeyDigest = SHA256(ASCII("TerraFX.PackageCookKey.v1") || 00 || EBIN(package-key))
PackageDigest = SHA256(all package bytes)
PublicationDigest = SHA256(EBIN(cook/publication))
```

Internal outputs cook in deterministic topological order: dependencies first, ready-node ties by RFC AssetId. An output key includes already-produced dependency envelope/version facts, but not its own envelope/package/publication hash. Native input graphs contain nominal references, not future output hashes. No self-hash cycle.

None=0 forbids Compressor. Brotli=1 requires it with exact `cook/brotli-settings`; empty settings are not a default substitute. Policies and outputs have identical AssetId sets. Compression/repacking changes package identity, not NativeKey/envelope bytes. Do not demand cross-implementation canonical Brotli bytes; a specifically pinned deterministic compressor is a distinct measured producer.

Keep EPKG2/EIDX layouts/profile-table version 2 unchanged. Entry/root tuple is envelope 2.0; entry decoded digest is EnvelopeDigest; dependency table major is target root major 2. Envelope dependency SchemaId/Major is the target semantic payload. C0 payload minors are zero, additionally checked from the actual target and publication; do not invent EIDX minor/GUID fields.

The edition-1 package producer uses one deterministic layout: index starts at offset 128; its exact canonical header/entry/dependency/profile/string extent is 16-aligned; payloads then follow in RFC AssetId order at successive 16-aligned offsets; every gap and final alignment byte is zero; TotalBytes is the final aligned cursor. DiagnosticNames is the exact optional EIDX string-record set, including present-empty names, references only Outputs, and is included in PackageKey. It is display text, not a logical-resource lookup identifier/path; preserve exact user UTF8 bytes without guessed normalization. It is not inferred from source paths or native metadata. This layout is a writer promise, not a retroactive narrowing of the existing structural package reader's legal disjoint-range admission. Codec/settings/PackageId/names/tool changes alter the package key; native context and envelope bytes do not change solely for those choices.

Bind every output/join before publication: graph/key OutputAssetId, envelope/EIDX AssetId/root, actual payload tuple, exact digest, profile tuple/registered ProfileId, source catalogue and dependency slice. Known mismatches are InvalidRecord; unknown admitted-version/schema/recipe/profile selections are UnsupportedSchema; invalid integer domains are InvalidRecord before lookup.

Assets-owned publication verification uniquely reconstructs the inert Graph: Nodes are exactly Outputs with each decoded canonical payload, direct Key.Dependency AssetIds and matching present/absent Metadata row; SuppliedDependencies is the deduplicated full version set in Keys outside Outputs; Sources/Profile/Catalogue/Convention are Context's recorded values; Root is RequestRootAssetId. Reencode it and verify Context.GraphDigest and rooted reachability. Validate each payload input digest and every version/metadata/context/key join. A bounded pass may stream the exact output-envelope set then retain only the requested dependency closure. An unverified claimed GraphDigest is not a proven join, but no original source graph/source bytes/tool binary is a runtime prerequisite: recorded source fingerprints are integrity-bound claims, not independently re-proved history or authority to fetch/execute anything. [M03 4.4](11-assets-import-streaming-and-packages.md#m03-44-runtime-owned-provenance-model-and-common-verification-kernel) defines the exact trusted approval and executable-kernel boundary.

`AssetVersion` must carry the content digest plus exact root/payload schema and import recipe ID/version; the full version also carries NativeKeyDigest. Trusted source registration binds each EIDX ID/digest/root tuple to one full publication version. Missing/conflicting provenance refuses; never invent a recipe/version because bare package bytes lack it. Publication metadata is copied/pinned with the version and exposed as inert metadata, not folded into the five-field envelope. Stores may pin many versions; a World admits one active canonical version per AssetId. Conflicts need explicit unload/rebind or whole replacement, not implicit field retargeting.

Assets remains Core-only transport. Scenes/Rendering/Animation supply their typed codecs through the existing registry interface. Physics remains Core-only; a permitted higher-layer `TerraFX.Integration` adapter supplies the native collision codec using public Physics definitions/validators. Do not add an Assets reference or private-constructor friendship to Physics.

### M03 5. Complete scene/prefab catalogue and expansion

All rows here are Scenes/Auxiliary except scene/prefab NativeAsset and marked Persistent Components.

| Key | Complete tags | RR |
|---|---|---|
| `native/scene` 2.0 | 1 SceneId G/Asset; 2 Entities RA{native/entity}[0..N] O(EntityId;true;true); 3 Hierarchy R{native/hierarchy}; 4 Placements RA{native/prefab-placement}[0..N] O(PlacementId;true;true); 5 RequiredAssets RA{native/asset-reference}[0..N] O(AssetId;true;true); 6 References RA{native/entity-reference-binding}[0..N] O(source composite below;true;true); 7 Overrides RA{native/prefab-override}[0..65536] O(target composite below;true;true); 8 ExternalSlots RA{native/external-slot}[0..65536] O(SlotId;true;true) | 20 |
| `native/prefab` 2.0 | Tag 2 reserved/forbidden. 1 PrefabId G/Asset; 3 Entities RA{native/entity}[0..65536] O(EntityId;true;true); 4 Hierarchy R{native/hierarchy}; 5 Placements RA{native/prefab-placement}[0..N] O(PlacementId;true;true); 6 References RA{native/entity-reference-binding}[0..N] O(source composite;true;true); 7 RequiredAssets RA{native/asset-reference}[0..N] O(AssetId;true;true); 8 ExternalSlots RA{native/external-slot}[0..65536] O(SlotId;true;true) | 21 |
| `native/entity` | 1 EntityId G/Entity; 2 Components RA@mask(0x0000000C)[0..256] O(header SchemaId,Major,Minor;true;true) | none |
| `native/hierarchy` 2.0 | 1 Nodes RA{native/hierarchy-node}[0..N] O(EntityId;true;true); 2 Roots RA{native/hierarchy-entry}[0..N] O(Kind,Id;false;true) | 20 |
| `native/hierarchy-node` 2.0 | 1 EntityId G/Entity; 2 ParentId? G/Entity; 3 Children RA{native/hierarchy-entry}[0..N] O(Kind,Id;false;true) | none |
| `native/hierarchy-entry` | 1 Kind U8{1,2}; 2 Id G | 20 |
| `native/prefab-placement` | 1 PlacementId G; 2 Prefab R{native/asset-reference}; 3 ParentEntityId? G/Entity; 4 RootPoses RA{native/prefab-root-pose}[0..65536] O(Target selector;true;true); 5 Overrides RA{native/prefab-override}[0..65536] O(target composite;true;true); 6 ExternalBindings RA{native/placement-slot-binding}[0..65536] O(SlotId;true;true) | 21 |
| `native/prefab-root-pose` | 1 Target R{native/entity-selector}; 2 Local R{scene/local-transform}; 3 RootPosition? P3 @EngineDistance | 21 |
| `native/entity-selector` | 1 Scope U8{1,2}; 2 PlacementPath FA(G)[0..32]; 3 AuthoredEntityId G/Entity | 20 |
| `native/prefab-override` | 1 Target R{native/entity-selector}; 2 ComponentSchema R{content/schema-ref}; 3 PropertyPath RA{content/property-segment}[1..64]; 4 Value? R{FixedLiteralSet}; 5 Operation U8{0,1} D(0); 6 ReferenceValue? R{native/reference-target} | 21 |
| `native/entity-reference-binding` | 1 Source R{native/entity-selector}; 2 ComponentSchema R{content/schema-ref}; 3 PropertyPath RA{content/property-segment}[1..64]; 4 Target R{native/reference-target} | 20 |
| `native/reference-target` | 1 Kind U8{1,2}; 2 Entity? R{native/entity-selector}; 3 ExternalSlotId? G/ExternalSceneBinding | 20 |
| `native/external-slot` | 1 SlotId G/ExternalSceneBinding; 2 Required Bool; 3 AllowNull Bool; 4 RequiredComponents RA{content/schema-ref}[0..256] O(schema tuple;true;true) | 21 |
| `native/placement-slot-binding` | 1 SlotId G/ExternalSceneBinding; 2 Target? R{native/reference-target} | 21 |
| `scene/local-transform` Component | 1 Position V3 @EngineDistance ^pa D(0,0,0); 2 Rotation Q ^pa D(0,0,0,1); 3 Scale V3 abs[0x38D1B717..0x447A0000] ^pa D(1,1,1) | none |
| `scene/root-position` Component | 1 Position P3 @EngineDistance ^p D(cells 0/offsets positive zero) | none |
| `scene/camera` Component | 1 Projection U8{0,1} ^p D(0); 2 VerticalFov F(0..pi32) exclusive @Radians ^p D(0x3F860A92); 3 OrthographicHeight F>0 @EngineDistance ^p DM(10;1); 4 Near F @EngineDistance ^p DM(1;10); 5 FarMode U8{0,1} ^p D(0); 6 Far? F @EngineDistance ^p DM(1000;1); 7 Aspect? F>0 ^p; 8 Viewport V4 ^p D(0,0,1,1); 9 ClearFlags U8 mask(3) ^p D(3); 10 ClearColor V4 HDR @LinearColor ^p D(0,0,0,1); 11 LayerMask U64 mask(0xFFFFFFFFFFFFFFFF) ^p D(all); 12 Order I32 ^p D(0) | 22 |
| `scene/collision-reference` Component | 1 Shape R{native/asset-reference} /Asset ^p | none |

Every native entity has LocalTransform. Entity and PrefabPlacement entry kinds are exactly 1/2. Each authored entity and placement occurs once across root/child entries; an entity's ParentId or a placement's ParentEntityId agrees with its entry location. Parent names a directly authored entity in that containing asset; no nested/ambient match. Roots have RootPosition and numerically zero Local.Position; children have no RootPosition. Parent/child projection is acyclic and final entity hierarchy depth <=256. SceneId/PrefabId equals envelope AssetId. Prefab root lists may contain multiple entities/placements or be empty; the old single-root tag is reserved, not silently reinterpreted. Entity tags are complete zero-field schema records. One schema GUID per entity, irrespective of major.

Authored EntityIds and PlacementIds are each unique within their containing document; equal bits across those two domains are disambiguated by the explicit entry kind. They are persisted author identities and may be UUIDv8; the reserved-domain restriction applies to explicit *World assignments*, not these IDs. The Placements and Entities maps are RFC-sorted, while hierarchy entries retain semantic order. No gap-index, map order or async completion establishes a second placement order.

### M03 collision validation identity inventory

The primitive descriptor/recipe inventory uses these exact assignments under the existing `TerraFXNameUuidSha256V1` namespace and recipe. Every listed version is 1; these are the authoritative primitive assignments, not aliases for provisional identities.

| Exact key | UUID | Version |
|---|---|---:|
| `validator/physics/primitives/2d/v1` | `bf235ec6-b588-8267-b521-b727ba965fb8` | 1 |
| `validator/physics/primitives/3d/v1` | `0b55d5d0-b90b-889a-afa6-fba0216aba19` | 1 |
| `statistics/physics/primitives/2d/scaled32/v1` | `db1e465b-5b3d-849e-82fa-4bfd84f66344` | 1 |
| `statistics/physics/primitives/3d/scaled32/v1` | `a1cebb74-c652-8c94-a2cf-c164e2af2707` | 1 |

The validator identity pins exact-literal Core conversion, published converted-limit interpretation, exact corner predicates and admission/fault classification. The statistics identity pins binary32 pi bits `0x40490FDB`, complete formula evaluation, normalized 24-bit-significand measure rounding, stored-pair nearest-even conversion, bounds/radius rounding and nullable applicability under [23](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics). The complete dimension-specific validator/statistics ID and version pair is retained in the collision descriptor and cook recipe/key alongside selected `SpatialProfileId`/settings and the unchanged coordinate/geometry/spatial/kernel foundation binding. Numerically identical results never authorize an unknown identity or implicit revision upgrade.

### M03 6. Complete replacement Physics wire inventory

Physics owns the semantic meaning; Integration supplies the Assets-interface bridge. Primitive roots NativeAsset; all support rows Auxiliary. The following field catalogues are normative; expanded descriptor and fixture-byte approval remain separate gates.

| Key | Complete tags | RR |
|---|---|---|
| `physics/primitive2` | 1 Definition R{physics/circle,physics/box2}; 2 Statistics R{physics/statistics2}; 3 Profile R{foundation/profile 2.0}; 4 Identity R{physics/validation-identity} | 30,32,34 |
| `physics/primitive3` | 1 Definition R{physics/sphere,physics/box3}; 2 Statistics R{physics/statistics3}; 3 Profile R{foundation/profile 2.0}; 4 Identity R{physics/validation-identity} | 31,33,35 |
| `physics/circle` | 1 Radius F @EngineDistance | none |
| `physics/box2` | 1 HalfExtents V2 @EngineDistance | none |
| `physics/sphere` | 1 Radius F @EngineDistance | none |
| `physics/box3` | 1 HalfExtents V3 @EngineDistance | none |
| `physics/shape-measure` | 1 Significand F; 2 BinaryExponent I32[-512..512] | 36 |
| `physics/spatial-settings` | 1 ChunkSizeExponent U8[8..16]; 2 MetersPerUnitExponent I32[-30..30] | none |
| `physics/validation-identity` | 1 ValidatorId G/Recipe; 2 ValidatorVersion U32[1..I]; 3 StatisticsFormulaId G/Recipe; 4 StatisticsFormulaVersion U32[1..I] | none |
| `physics/statistics2` | 1 VertexCount? U32[0..262144]; 2 EdgeCount? U32[0..N]; 3 FaceCount? U32[0..N]; 4 SignedArea R{physics/shape-measure} @EngineArea; 5 Minimum V2 @EngineDistance; 6 Maximum V2 @EngineDistance; 7 EnclosingRadius F>=0 @EngineDistance; 8 ConnectedComponents U32[1..N]; 9 WeldedVertexCount? U32[0..262144]; 10 PatchCount? U32[0..N]; 11 MaximumSnapDisplacement? F>=0 @EngineDistance; 12 Settings R{physics/spatial-settings}; 13 Identity R{physics/validation-identity} | none |
| `physics/statistics3` | Identical tags to statistics2, except 4 SignedVolume @EngineVolume and 5/6 V3 | none |

Stats local fields do not pretend to establish geometry. Their applicability and exact values are validated by the containing primitive root; the shared support meaning leaves later indexed/surface consumers to their separately registered owner algorithms, not a competing statistics type.

The version-1 assignments are the unchanged collision validation identity inventory immediately above.

Envelope Profile, primitive Profile and trusted selected binding agree on registered ProfileId, codes, both exponents and all three digests. Stats.Settings equals those two exponents; stats Identity equals root Identity in all four members; the dimension-specific pair is selected exactly. Equal numeric settings do not substitute a different ProfileId. Foundation's existing inert optional preservation is unchanged; compare the complete admitted foundation binding tuple, not a guessed profile string or a new foundation digest.

For known IDs, positive representable unassigned versions are unsupported; zero/high-bit versions are invalid before lookup. Known other-dimension or internally crossed ID pairs are invalid dimensional bindings. Unknown IDs are unsupported. No numerical equality waives identity.

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

Bounds are copied positive extents/negations; analytic radius is copied; box radius is the least binary32 r with `N(r)^2>=sum(N(h_i)^2)`. Compare every recomputed field/presence/identity/settings/bit exactly; do not replace stored statistics with corrected success. Known mismatches return contextual InvalidRecord.

`TryGetSingle` rounds the stored pair once: normals/subnormals/+0 succeed; nonzero rounds-to-zero/infinity returns false with +0 output and does not invalidate geometry. Preserve the independent e=12 sphere (`radius 0x28E12FB1`, pair `0x3FB67680,-134`, stored conversion `0x0000B676`, direct formula `0x0000B677`) and e=-14 one-meter sphere (`0x3FC056FF,141`, float overflow).

All 61 exponents are metadata/geometry cases, not complete Physics profile eligibility. Explicit settings change converts copied dimensions once with Core, recomputes stats and produces new key/content/version/lineage; chunk-only changes still rebind/recook explicitly. No scaling old rounded measures, relabeling, or automatic StrictLocal upgrade.

### M03 7. Complete CPU mesh/material/texture/animation/render data

Rendering owner unless noted. Native roots are mesh/material/texture/atlas; support Auxiliary; render components Persistent. Skeleton/clip and supports are Animation, with native roots and EventPayload marker.

| Key | Complete tags | RR |
|---|---|---|
| `native/mesh` | 1 VertexCount U32[1..262144]; 2 Streams RA{position,normal,tangent,uv,color,joints,weights wrappers below}[1..8] rule 44; 3 Indices R{native/mesh-indices16,native/mesh-indices32}; 4 Groups RA{native/mesh-group}[1..65536] O(FirstIndex;true;true); 5 MaterialSlots RA{native/asset-reference}[1..256] semantic order; 6 Bounds R{native/bounds3}; 7 Skeleton? R{native/asset-reference} | 23 |
| `native/bounds3` | 1 Minimum V3 @EngineDistance; 2 Maximum V3 @EngineDistance; 3 SphereCenter V3 @EngineDistance; 4 SphereRadius F>=0 @EngineDistance | none |
| `native/mesh-position` | 1 Stride U32=12; 2 Values FA(V3)[1..262144] @EngineDistance | none |
| `native/mesh-normal` | 1 Stride U32=12; 2 Values FA(V3 unit)[1..262144] | none |
| `native/mesh-tangent` | 1 Stride U32=16; 2 Values FA(V4)[1..262144] | none |
| `native/mesh-uv` | 1 Set U8{0,1}; 2 Stride U32=8; 3 Values FA(V2)[1..262144] | none |
| `native/mesh-color` | 1 Stride U32=16; 2 Values FA(V4)[1..262144] range [0..1] @LinearColor | none |
| `native/mesh-joints` | 1 StridePerVertex U32=8; 2 Values FA(U16)[4..N] | none |
| `native/mesh-weights` | 1 Stride U32=16; 2 Values FA(V4)[1..262144] range [0..1] | none |
| `native/mesh-indices16` | 1 Values FA(U16)[3..N] | none |
| `native/mesh-indices32` | 1 Values FA(U32)[3..N] | none |
| `native/mesh-group` | 1 FirstIndex U32[0..N]; 2 IndexCount U32[1..N]; 3 MaterialSlot U32[0..255] | none |
| `native/material` | 1 ProfileId G/Profile; 2 ShaderId G; 3 PassId G; 4 LayoutId G; 5 Parameters R{native/material-sprite-parameters,native/material-pbr-parameters}; 6 AlphaMode U8{0,1,2} DP(0); 7 AlphaCutoff F[0..1] D(0.5); 8 DoubleSided Bool D(false); 9 DepthTest Bool DP(1); 10 DepthWrite Bool DP(2); 11 CullMode U8{0,1} DP(3); 12 OutputAlpha U8{0,1} DP(4); 13 RequiredCapabilities U64 mask(0x1FF) DP(5) | 24 |
| `native/material-sprite-parameters` | 1 Color V4[0..1] @LinearColor D(1,1,1,1); 2 Texture? R{native/texture-binding} | none |
| `native/material-pbr-parameters` | 1 BaseColor V4[0..1] @LinearColor D(1,1,1,1); 2 Metallic F[0..1] D(0); 3 Roughness F[0x3D3851EC..1] D(1); 4 NormalScale F[0..2] D(1); 5 Emissive V3>=0 @LinearColor D(0,0,0); 6 BaseTexture? R{native/texture-binding}; 7 PackedMetalRoughOcclusion? same; 8 NormalTexture? same; 9 EmissiveTexture? same | none |
| `native/texture-binding` | 1 Texture R{native/asset-reference}; 2 Sampler R{native/sampler}; 3 UVSet U8{0,1} D(0) | none |
| `native/sampler` | 1 Min U8{0,1}; 2 Mag U8{0,1}; 3 Mip U8{0,1,2}; 4 AddressU U8{0,1}; 5 AddressV U8{0,1} | none |
| `native/texture` | 1 Width U32[1..4096]; 2 Height U32[1..4096]; 3 Format U8=0; 4 ColorSpace U8{0,1}; 5 Alpha U8{0,1}; 6 Semantic U8{0,1,2}; 7 MipPolicy U8{0,1}; 8 Mips RA{native/texture-mip}[1..13] O(Level;true;true) | 25 |
| `native/texture-mip` | 1 Level U32[0..12]; 2 Width U32[1..4096]; 3 Height U32[1..4096]; 4 Pixels B[4..67108864] | none |
| `native/atlas` | 1 Pages RA{native/asset-reference}[1..16] semantic order; 2 Frames RA{native/atlas-frame}[1..N] O(FrameId;true;true) | 26 |
| `native/atlas-frame` | 1 FrameId G; 2 Page U32[0..15]; 3 X U32[0..4095]; 4 Y U32[0..4095]; 5 Width U32[1..4096]; 6 Height U32[1..4096]; 7 Padding U8=2 | none |
| `render/sprite` Component | 1 Atlas R{native/asset-reference} /Asset ^pa; 2 FrameId G ^pa; 3 Pivot V2[0..1] ^p D(0.5,0.5); 4 Size V2>0 @EngineDistance ^p DM(1,1;1); 5 Tint V4 HDR @LinearColor ^p D(1,1,1,1); 6 FlipMask U8 mask(3) ^p D(0); 7 SortingLayer I32 ^p D(0); 8 SortingOrder I32 ^p D(0); 9 Material? R{native/asset-reference} /Asset ^p; 10 PixelSnap Bool ^p D(false) | 24 |
| `render/mesh` Component | 1 Mesh R{native/asset-reference} /Asset ^p; 2 LayerMask U64 mask(all) ^p D(all); 3 PoseMode U8=0 D(0) | 23 |
| `render/directional-light` Component | 1 Color V3>=0 @LinearColor ^p D(1,1,1); 2 Illuminance F>=0 @Lux ^p D(10000); 3 Direction V3 unit ^p D(0,-1,0); 4 LayerMask U64 mask(all) ^p D(all); 5 ShadowRequested Bool ^p D(false); 6 ShadowPriority I32 ^p D(0) | 29 |
| `render/point-light` Component | 1 Color V3>=0 @LinearColor ^p D(1,1,1); 2 Flux F>=0 @Lumens ^p D(1000); 3 Range F>0 @EngineDistance ^p DM(10;1); 4 LayerMask U64 mask(all) ^p D(all) | 29 |
| `render/spot-light` Component | 1 Color V3>=0 @LinearColor ^p D(1,1,1); 2 Flux F>=0 @Lumens ^p D(1000); 3 Range F>0 @EngineDistance ^p DM(10;1); 4 Direction V3 unit ^p D(0,0,1); 5 InnerHalfAngle F(0..pi32/2) exclusive @Radians ^p D(0x3F060A92); 6 OuterHalfAngle F(0..pi32/2) exclusive @Radians ^p D(0x3F490FDB); 7 LayerMask U64 mask(all) ^p D(all); 8 ShadowRequested Bool ^p D(false); 9 ShadowPriority I32 ^p D(0) | 29 |
| `native/skeleton` | 1 Joints RA{native/joint}[1..256] O(JointId;false;true); 2 RootMotionJoint? G/Joint | 27 |
| `native/joint` | 1 JointId G/Joint; 2 ParentIndex I32[-1..255]; 3 BindLocal R{scene/local-transform}; 4 InverseBind M4 | none |
| `native/clip` | 1 DurationTicks I64[1..L] @TimeSpanTicks; 2 Tracks RA{native/transform-track,native/sprite-track}[0..4096] rule 45; 3 Events RA{native/clip-event}[0..N] O(EventId;false;true) then O(TimeTicks,EventId;true;true); 4 LoopMode U8{0,1} D(0); 5 Skeleton? R{native/asset-reference}; 6 RootMotion? R{native/root-motion}; 7 Bounds? R{native/bounds3}; 8 BoundsQualification U8=0; 9 BoundsMargin F>=0 @EngineDistance D(0); 10 Scene? R{native/asset-reference} | 28 |
| `native/transform-track` | 1 TargetId G/EntityOrJoint; 2 TargetDomain U8{0,1}; 3 Channel U8{0,1,2}; 4 Interpolation U8{0,1,2}; 5 Keys RA{native/key-vector3,native/key-quaternion}[1..N] O(TimeTicks;true;true) | none |
| `native/sprite-track` | 1 TargetEntityId G/Entity; 2 Keys RA{native/key-sprite}[1..N] O(TimeTicks;true;true) | none |
| `native/key-vector3` | 1 TimeTicks I64[0..L] @TimeSpanTicks; 2 Value V3; 3 InTangent? V3; 4 OutTangent? V3 | none |
| `native/key-quaternion` | 1 TimeTicks I64[0..L] @TimeSpanTicks; 2 Value Q; 3 InTangent? V4; 4 OutTangent? V4 | none |
| `native/key-sprite` | 1 TimeTicks I64[0..L] @TimeSpanTicks; 2 Atlas R{native/asset-reference}; 3 FrameId G | none |
| `native/clip-event` | 1 EventId G/Event; 2 TimeTicks I64[0..L] @TimeSpanTicks; 3 Payload R@EventPayload | none |
| `native/event-marker` EventPayload | 1 Name T[1..128], user text not normalized | none |
| `native/root-motion` | 1 RootJointId G/Joint; 2 Policy U8=0 | none |

Shorthands `same` above expand to the exact `R{native/texture-binding}`. `all` is the literal `0xFFFFFFFFFFFFFFFF`. V2>0 means both finite components strictly positive. V3>=0 means all finite components nonnegative. They generate same-kind vector range literals, not scalar or padded bounds.

Mesh-stream key is exactly semantic rank Position0/Normal1/Tangent2/UV0=3/UV1=4/Color5/Joints6/Weights7; ascending and unique. No arbitrary Header GUID sort. Each non-joint stream count=VertexCount; joints count=4*VertexCount. Position is mandatory. Tangent XYZ obeys rule 8's exact predicate inside RR23, and W is bits +1/-1; weights finite nonnegative, exact dyadic sum within 1/100000 of one, no load normalization; joints in the actual skeleton domain. Joint/weight/skeleton presence is all-or-none. Indices are in range, their count divisible by 3, and each exact dyadic triangle cross is nonzero. Groups begin at 0, have FirstIndex/IndexCount divisible by 3, contiguous nonempty ranges and final end=Indices.Count; every MaterialSlot is in the actual slot domain. Unreferenced vertices are not silently removed or made invalid; conservative bounds include all supplied positions. Material-slot order is semantic and may repeat the same asset.

Bounds are finite, ordered and conservative over the admitted vertices; sphere uses exact dyadic squared-distance inclusion. No minimal-sphere requirement. This edition admits supplied explicit noninterleaved streams and no authored LOD/morph/meshlet/packed formats; their actual M05 owners must extend the catalogue. It does not silently reinterpret opaque stride bytes.

### M03 8. Complete standalone checkpoint catalogue and equalities

This edition is standalone CPU committed persistence. Hosted wall pacing/input inbox/device/physics/animation/audio state belongs to its actual later owner; this replaces the old speculative `state/pacing` and opaque world-edit operation table rather than pretending those consumers exist. Later hosted formats need explicit catalogue/version extensions and exact pending-state contracts. Current M03 save does not silently serialize future host delegates or action state.

Hosting/Checkpoint owns file/checkpoint/header/canonical/runtime identities and world composition. Entities/Auxiliary owns ECS/configuration/registration/clock/layout/PCG; Scenes/Auxiliary owns scene state. Asset-version is Assets/Auxiliary. Control payload owners are given below.

| Key | Complete tags | RR |
|---|---|---|
| `state/file` | 1 TransactionGeneration I64[1..L]; 2 Checkpoint R{state/checkpoint}; 3 CheckpointDigest B[32..32]; 4 FileContentDigest B[32..32] | 37 |
| `state/file-content` | 1 TransactionGeneration I64[1..L]; 2 Checkpoint R{state/checkpoint} | 37 |
| `state/checkpoint` | 1 Header R{state/header}; 2 World R{state/world}; 3 CanonicalHash B[32..32] | 37 |
| `state/header` | 1 CatalogueDigest B[32..32]; 2 Application R{state/application}; 3 WorldGuid G/World; 4 Profile R{foundation/profile}; 5 NextTick I64[1..L]; 6 Mode U8{0,1}; 7 LineageGuid G; 8 RuntimeProfile R{state/runtime-profile}; 9 SerializationMajor U32=1; 10 SerializationMinor U32=0; 11 CaptureWorkerCount U8[0..64] | 37 |
| `state/application` | 1 ApplicationId G; 2 Major U32[1..I]; 3 Minor U32[0..I]; 4 Modules RA{state/module-id}[0..256] O(ModuleId;true;true) | 37 |
| `state/module-id` | 1 ModuleId G/Module; 2 Version U32[1..I] | none |
| `state/module` | 1 ModuleId G/Module; 2 Version U32[1..I]; 3 ArtifactDigest B[32..32] | none |
| `state/runtime-profile` | 1 EngineArtifact B[32..32]; 2 RuntimeArtifact B[32..32]; 3 CompilerArtifact B[32..32]; 4 OS U8{0,1}; 5 Architecture U8=0; 6 Codegen U8{0,1}; 7 ISAFlags U64 mask(15); 8 Numerics R{state/numerics}; 9 Modules RA{state/module}[0..256] O(ModuleId;true;true) | 37 |
| `state/numerics` | 1 KernelDigest B[32..32]; 2 FmaPolicy U8{0,1}; 3 MaximumSimdBits U16{0,128,256,512}; 4 Rounding U8=0; 5 FlushSubnormals Bool=false; 6 Reassociation Bool=false | 37 |
| `state/canonical` | 1 Mode U8{0,1}; 2 CatalogueDigest B[32..32]; 3 Application R{state/application}; 4 WorldGuid G/World; 5 Profile R{foundation/profile}; 6 World R{state/world}; 7 RuntimeProfile? R{state/runtime-profile} | 37 |
| `state/world` | 1 Configuration R{state/configuration}; 2 Clock R{state/clock}; 3 Frame2? R{foundation/frame2}; 4 Frame3 R{foundation/frame3}; 5 ECS R{state/ecs}; 6 Scenes R{state/scenes}; 7 Assets RA{state/asset-version}[0..N] O(AssetId;true;true); 8 Controls RA{state/control}[0..65536] O(Sequence;true;true); 9 Cursors R{state/cursors}; 10 RNG RA{state/pcg}[0..65536] O(StreamId;true;true); 11 StrictLayout? R{state/strict-layout}; 12 Registration R{state/registration} | 37 |
| `state/configuration` | 1 MaximumLiveEntities U32[1..N]; 2 MaximumEntitySlots U32[1..2097152]; 3 MaximumArchetypes U32[1..4096]; 4 MaximumDeferredCommands U32[1..65536]; 5 MaximumComponentTypes U32[1..256]; 6 MaximumOwnerRequests U32[1..128]; 7 MaximumRecordedControls U32[1..65536]; 8 MaximumIdentityAssignments U32[1..33554432]; 9 Scheduling R{state/scheduling-limits}; 10 ExecutionOwner U8=0; 11 MaximumIdentityBranches U16[1..1024]; 12 MaximumEntityReferencePositions U32[1..4194304]; 13 MaximumActiveSceneInstances U32[1..65536]; 14 MaximumSceneOccurrences U32[1..N]; 15 MaximumSnapshotBytes I64[64..2147483584]; 16 MaximumPersistenceAdmissionBytes I64[4096..8589934592] | 38 |
| `state/scheduling-limits` | 1 RegistrationBytes I64[4096..16777216]; 2 GraphBytes I64[4096..8388608]; 3 WorkPlanBytes I64[4096..67108864]; 4 CommandStagingBytes I64[4096..33554432]; 5 OwnedStateBytes I64[4096..2147483648]; 6 DiagnosticBytes I64[4096..8388608]; 7 ConflictComparisons U32[0..16777216] | 38 |
| `state/registration` | 1 Components RA{state/component-registration}[0..256] O(schema tuple;true;true); 2 Tags RA{content/schema-ref}[0..256] O(schema tuple;true;true); 3 Systems RA{state/system-registration}[0..4096] O(Order signed,SystemId;true;true); 4 Resources RA{state/resource-registration}[0..4096] O(ResourceId;true;true) | 38 |
| `state/component-registration` | 1 Schema R{content/schema-ref}; 2 Storage U8{1,2}; 3 Restore U8{0,1,2}; 4 FactoryId G/Factory; 5 FactoryVersion U32[1..I] | 48 |
| `state/system-registration` | 1 SystemId G/System; 2 Order I32; 3 Stage U8{0..8}; 4 ThreadMode U8{1,2}; 5 Access RA{state/access}[0..256] O(Kind,ResourceId;true;true); 6 Before FA(G)[0..4095] O(value/RFC;true;true); 7 After same; 8 FactoryId G/Factory; 9 FactoryVersion U32[1..I]; 10 StateSchema? R{content/schema-ref} | 48 |
| `state/access` | 1 Kind U8{1,2,3,4}; 2 ResourceId G/ResourceOrSchema; 3 Mode U8{1,2}; 4 Range? R{state/partition-range} | 38 |
| `state/partition-range` | 1 Start U32[0..I]; 2 Length U32[1..I] | 38 |
| `state/resource-registration` | 1 ResourceId G; 2 PartitionCount U32[1..I]; 3 AllowParallelAccess Bool; 4 StateSchema R{content/schema-ref}; 5 FactoryId G/Factory; 6 FactoryVersion U32[1..I] | 48 |
| `state/ecs` | 1 Entities RA{state/entity}[0..N] O(EntityId;true;true); 2 Systems RA{state/system-state}[0..4096] O(SystemId;true;true); 3 Resources RA{state/resource-state}[0..4096] O(ResourceId;true;true); 4 Attachments RA{state/attachment}[0..N] O(owner missing-before,schema tuple;true;true); 5 IdentityHistory R{state/identity-history} | 38 |
| `state/entity` | 1 EntityId G/Entity; 2 Components RA@Component[0..256] O(header tuple;true;true); 3 Tags RA@EntityTag[0..256] O(header tuple;true;true); 4 TransientSchemas RA{content/schema-ref}[0..256] O(schema tuple;true;true); 5 AssignmentOrdinal I64[1..33554432]; 6 SceneInstanceId? G/SceneInstance | 38 |
| `state/identity-history` | 1 WorldGuid G/World; 2 ReplayLineageId G; 3 ActiveBranchId G; 4 IssuanceNamespace G; 5 NextAssignmentOrdinal I64[1..33554433]; 6 Branches RA{state/identity-branch}[1..1024] O(DirectoryIndex;true;true); 7 Pages RA{state/identity-page}[0..8192] O(FirstOrdinal;true;true) | 38 |
| `state/identity-branch` | 1 DirectoryIndex U16[0..1023]; 2 BranchId G; 3 CreationLineageId G; 4 IssuanceNamespace G; 5 ParentBranchId? G; 6 CreatedAtNextTick I64[0..L]; 7 SourceCheckpointDigest? B[32..32]; 8 ForkSource? R{state/fork-source}; 9 Kind U8{1,2}; 10 FirstAssignmentOrdinal I64[1..33554433] | 38 |
| `state/fork-source` | 1 SourceWorldGuid G; 2 SourceBranchId G; 3 SourceCreationLineageId G; 4 SourceNamespace G; 5 SourceCheckpointDigest B[32..32]; 6 SourceIdentityDigest B[32..32]; 7 Prior? R{state/fork-source} | 38 |
| `state/identity-page` | 1 FirstOrdinal I64[1..33554432]; 2 Ids FA(G)[1..4096]; 3 Metadata FA(U32)[1..4096] mask(0x7FFF) | 38 |
| `state/system-state` | 1 SystemId G/System; 2 Payload R@SystemState | 38 |
| `state/resource-state` | 1 ResourceId G; 2 Payload R@ResourceState | 38 |
| `state/attachment` | 1 Schema R{content/schema-ref}; 2 OwnerEntityId? G/Entity; 3 Payload R@AttachmentState | 38 |
| `state/asset-version` | 1 AssetId G/Asset; 2 EnvelopeDigest B[32..32]; 3 RootSchema R{content/schema-ref}; 4 PayloadSchema R{content/schema-ref}; 5 CatalogueDigest B[32..32]; 6 Profile R{foundation/profile}; 7 NativeKeyDigest B[32..32]; 8 RecipeId G/Recipe; 9 RecipeVersion U32[1..I] | 16 |
| `state/pcg` | 1 StreamId G/Stream; 2 State U64; 3 Increment U64 | 42 |
| `state/clock` | 1 NextTick I64[1..L]; 2 TickRate U32[1..1000]; 3 TimeScale F[0..4]; 4 NextPublicationSequence I64[1..L]; 5 Segments RA{state/time-segment}[1..65537] O(Sequence;true;true); 6 ReplayComplete Bool=false; 7 CurrentSegmentSequence I64[1..L]; 8 InitialFrame2? R{foundation/frame2}; 9 InitialFrame3 R{foundation/frame3}; 10 HistoryBaseNextTick I64=0 | 40 |
| `state/time-segment` | 1 Sequence I64[1..L]; 2 StartNextTick I64[0..L]; 3 TimeScale F[0..4] | 40 |
| `state/control` | 1 Sequence I64[1..L]; 2 AppliedAtNextTick I64[0..L]; 3 Payload R@ControlPayload | 41 |
| `state/control-timescale` Entities/ControlPayload | 1 Value F[0..4]; 2 SegmentSequence I64[1..L] | 41 |
| `state/control-frame2` Entities/ControlPayload | 1 Frame R{foundation/frame2} | 41 |
| `state/control-frame3` Entities/ControlPayload | 1 Frame R{foundation/frame3} | 41 |
| `state/control-step` Entities/ControlPayload | 1 ExecutedTick I64[0..L] | 41 |
| `state/control-branch` Entities/ControlPayload | 1 BranchId G; 2 LineageId G; 3 DirectoryIndex U16[1..1023] | 41 |
| `state/control-scene` Scenes/ControlPayload | 1 Operation U8{0,1,2}; 2 SceneInstanceId G/HistoricalSceneInstance; 3 Source? R{state/asset-version}; 4 Intents? RA{scene/intent-reparent,scene/intent-root-position}[1..65536]; 5 DestroyMode? U8{0,1}; 6 ExternalBindings? RA{state/external-scene-binding}[0..65536] O(BindingId;true;true); 7 RequestedSceneId? G; 8 OriginalSceneInstanceId? G | 50 |
| `scene/intent-reparent` Scenes/Auxiliary | 1 ChildId G/HistoricalEntity; 2 ParentId? G/HistoricalEntity; 3 Mode U8{0,1} | none |
| `scene/intent-root-position` Scenes/Auxiliary | 1 EntityId G/HistoricalEntity; 2 Position P3 @EngineDistance | none |
| `state/cursors` | 1 NextControlSequence I64[1..L]; 2 NextOwnerRequestSequence? I64[1..L]; 3 NextBarrierEpoch? I64[1..L]; 4 PendingCommands RA@ControlPayload[0..0]; 5 PendingOwnerRequests RA@ControlPayload[0..0]; 6 PendingWorldEvents RA@EventPayload[0..0]; 7 AcceptedOwnerRequests? I64[0..9223372036854775806] | 39 |
| `state/strict-layout` | 1 Archetypes RA{state/archetype}[0..4096] O(SchemaIds/RFC tuple;true;true); 2 Slots RA{state/slot}[0..2097152] O(Index;true;true); 3 FreeSlots FA(U32)[0..2097152] O(value;false;true); 4 NextChunkOrdinal I64[0..L]; 5 ComponentLayouts RA{state/component-layout}[0..256] O(schema tuple;true;true) | 39 |
| `state/archetype` | 1 SchemaIds FA(G)[0..256] O(value/RFC;true;true); 2 Chunks RA{state/chunk}[0..N] O(Ordinal;true;true) | 39 |
| `state/chunk` | 1 Ordinal I64[0..L]; 2 Rows FA(G)[0..256] O(value/RFC;false;true); 3 Capacity U16[1..256]; 4 LogicalRowBytes U32[12..32768] | 39 |
| `state/slot` | 1 Index U32[1..2097152]; 2 Generation U32[1..4294967295]; 3 Status U8{0,1,2}; 4 EntityId? G/Entity; 5 ArchetypeKeyDigest? B[32..32]; 6 ChunkOrdinal? I64[0..L]; 7 Row? U16[0..255] | 39 |
| `state/component-layout` | 1 Schema R{content/schema-ref}; 2 LogicalBytes U32[1..32756]; 3 LayoutId G | 39 |
| `state/scenes` Scenes/Auxiliary | 1 Instances RA{state/scene-instance}[0..65536] O(SceneInstanceId;true;true); 2 Hierarchy R{scene/world-hierarchy} | 50 |
| `state/scene-instance` Scenes/Auxiliary | 1 SceneInstanceId G/SceneInstance; 2 Source R{state/asset-version}; 3 EntityIds FA(G)[0..N] O(value/RFC;false;true); 4 Bindings RA{state/scene-entity-binding}[0..N] O(OccurrencePath,TemplateEntityId;true;true); 5 ExternalBindings RA{state/external-scene-binding}[0..65536] O(BindingId;true;true); 6 AssignmentOrdinal I64[1..33554432]; 7 Occurrences RA{state/scene-occurrence}[1..N] O(OccurrencePath;true;true) | 50 |
| `state/scene-entity-binding` Scenes/Auxiliary | 1 OccurrencePath FA(G)[0..32]; 2 TemplateAsset R{state/asset-version}; 3 TemplateEntityId G; 4 EntityId G/Entity; 5 EffectiveOccurrenceId G; 6 ExpandedEntityKey G | 50 |
| `state/scene-occurrence` Scenes/Auxiliary | 1 OccurrencePath FA(G)[0..32]; 2 OccurrenceId G; 3 Source R{state/asset-version}; 4 ParentOccurrenceId? G; 5 PlacementId? G | 50 |
| `state/external-scene-binding` Scenes/Auxiliary | 1 BindingId G/ExternalSceneBinding; 2 TargetEntityId? G/HistoricalEntity | 50 |
| `scene/world-hierarchy` Scenes/Auxiliary | 1 Nodes RA{scene/world-hierarchy-node}[0..N] O(EntityId;true;true); 2 Roots FA(G)[0..N] O(value/RFC;false;true) | 50 |
| `scene/world-hierarchy-node` Scenes/Auxiliary | 1 EntityId G/Entity; 2 ParentId? G/Entity; 3 Children FA(G)[0..N] O(value/RFC;false;true) | 50 |

Schema-tuple O expands keys SchemaId RFC/Major unsigned/Minor unsigned/Scope unsigned. `same` After is FA(G)[0..4095] with the same ordered-unique rule. Qualified Payload roles must match the exact factory/participant schema, not any arbitrary record of that role.

Mode Standard=0/StrictLocal=1. OS Windows0/Linux1, Architecture x64=0, Codegen AOT0/JIT1. ISA flags are recorded admitted V2=1/AVX2=2/FMA=4/AVX512=8; FMA policy Automatic0/Disabled1, rounding NearestEven0. These identify actual qualified execution, not guessed hardware support. Modules in application/runtime tables have identical IDs/versions; StrictLocal additionally matches artifact/OS/ISA/runtime/compiler/codegen/numerical values exactly. Worker count is audit-only, deliberately excluded from the canonical projection; 0/1/4 must not change the hash solely by configuration.

EngineArtifact/RuntimeArtifact/CompilerArtifact are SHA256 of externally captured immutable canonical J build/profile manifests, not a hash of one arbitrarily selected DLL. Each has a sorted complete admitted file inventory `(relative path UTF8 bytes, byte length, ordinary file SHA256)` and explicit exact release/toolchain version bytes. Runtime additionally contains OSFamily, exact OS version/build/distribution identity bytes and runtime version; Compiler includes selected SDK/compiler/AOT-or-JIT tool identities. OS0/1 is only the family discriminator: a changed exact platform manifest cannot pass StrictLocal just because both machines are Windows. Pin these manifest bytes and all actual file hashes as literal inputs to both oracles before execution vectors; missing/unverifiable actual profile rejects continuation. Wire-only literal artifact cases remain nonexecution cases.

### M03 9. Independent descriptor and fixture derivation

This section is a required plan, not an assertion that accepted fixture bytes or reviewer revisions exist. No fixture binary need be generated during this contract task.

#### M03 9.1 Required descriptor output and provenance receipt

From this exact source, materialize:

1. A complete assignment table: exact key, namespace/recipe, UUID RFC hex, schema/rule/profile role and version; collision check across the full table.
2. Complete expanded schema/field/tag/rule-use/rule-recipe/default/profile tables; no DSL shorthand, symbolic set, omitted zero or inferred rule.
3. Full canonical C0 manifest hex, byte length and SHA-256; complete imported CApp manifests for the specified fixture consumers.
4. Rule recipe hex/length/hash and every default/parameter/profile operand; exact full references and ordered uses.
5. A descriptor coverage ledger with exactly one row per selected schema/tag/rule-use/default/context/role/ordering branch and one registration row per actual producer/reader/binding.

The normative contract alone, without these independently approved outputs, cannot unlock codecs. Approval receipt fields are mandatory:

```text
specification path + SHA256 + evidence/base-worktree pins
expanded descriptor-source SHA256
assignment/table/manifest lengths + SHA256
oracle A repository/source revision + source-tree SHA256
oracle B independent repository/source revision + source-tree SHA256
their toolchain/runtime/library artifact versions + SHA256
fixture input-source SHA256 + complete fixture-ledger SHA256
all output-vector lengths + SHA256 + ordered corpus aggregate
independent reviewer identity/model exact version + review revision + report SHA256
reviewed exact descriptor/fixture/corpus hashes + disposition + exclusions
```

Until each real field exists, status is `Proposed`, `PendingDescriptorOutput`, `PendingOracleAgreement` or `PendingIndependentReview`. No placeholder zero digest, invented commit/hash or inherited ACCEPT from a different scope. The user-appointed independent GPT-6.1 Sol reviewer reviews those exact immutable revisions; this task does not ask it.

#### M03 9.2 Independence

Oracle A: independent offset-first reference assembler; arbitrary-precision integer/rational arithmetic; manual RFC UUID bytes and fixed widths; quotient/remainder nearest-even rounding and exact scalar/measure derivation; explicit arithmetic field offsets/gaps.

Oracle B: separately authored C#/.NET record-tree emitter; independently derived cursor/alignment/offset-table logic; rational comparisons with adjacent IEEE encodings/binary-search selection instead of A's quotient algorithm; independent persistent-tree scene expansion and state projection. Use BCL crypto/UTF-8, not TerraFX libraries.

They share only approved literal inputs/specification. Neither imports/calls/copies production EBIN layout, foundation/native codecs, UUID helper, SpatialMath, Physics validator/measure converter, generated bindings, scene expander, state hash/projection or the other oracle's helpers. Pin ordinary crypto with standard known-answer vectors, including namespace/domain/length/variant fixtures. Verify RFC-versus-mixed-endian ordering independently.

Produce bytes and semantic facts independently before comparing. Differences are failures investigated from source arithmetic, not resolved by copying one output. Freeze both outputs and reviewer facts before production. A production-generated expectation update cannot repair a disagreement. Every later change invalidates affected approvals and needs a reviewed source/corpus revision.

#### M03 9.3 Literal source bases

Source uses an exact typed record tree (schema key/version, ordered tagged values, presence, raw float bits, RFC GUID bytes), not public CLR property enumeration. Input JSON is tooling data with explicit tags/types/ordered arrays; no JSON order determines EBIN tags. Expected facts/status/mutations are independent source, not a production round trip.

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

Every selected schema gets [M03 9.5](10-binary-data-and-authoring-formats.md#m03-95-deterministic-schema-to-carrier-closure)'s `schema/<RFC-id-N>/<major>/<minor>/base` source case, either a standalone root or an exact path within the contextually complete family base below. Raw literal/default/parameter/identity rows get their own metadata carrier if not exercised by C0 itself; unused support is not silently omitted.

Family bases:

| Base | Exact input construction |
|---|---|
| Metadata | C0 expansion itself; CApp imports C0 and adds the fixture types below. Name assignment and external literal-assignment branches have separate immutable assignment receipts. All E/R/M/O/default/helper variants occur explicitly, not fabricated from production. |
| Literal carrier | Fixture NativeAsset with tags1..21 for the permanent literal family: integers 1/0x0203/0x04050607/0x0102030405060708/-1/-257/-65537/-1099511627777; true; f32 bits0x80000000; Guid A; UTF-8 bytes C3 A9; Bytes 00 7F 80 FF; V2(1,-0), V3(1,2,3), V4(1,2,3,4), Q identity; M32 rows(1,0),(0,1),(2,3); M4 identity; P2 cells(-1,2) offsets(+0,+0); P3 cells(1,-2,3) offsets(+0,1,2). All required tags materialized from explicit default metadata. |
| Texture/material | 2x2 RGBA8 top-left red/green/blue/transparent-black literals; BaseOnly and supplied FullChain1x1. Distinct Linear/sRGB, Straight/Premultiplied, Color/Normal/Data, sampler min/mag/mip/address combinations. PBR defaults plus every texture-slot presence combination; sprite defaults plus absent/present texture. Alpha0/1/2 and double-sided0/1 crossed with valid/invalid depth/cull/output/capability predicates. |
| Atlas | 8x8 premultiplied Linear BaseOnly page; one 2x2 frame at(2,2), padding2. Padded coordinates0..5 equal the clamped 2x2 source texel; unused pixels transparent-black. Second-frame variant uses 16x16 page with second frame at(10,2), disjoint padding. |
| Mesh | vertices(0,0,0),(0,1,0),(1,0,0); CW -Z normals; tangent(1,0,0,+1); UV0(0,1),(0,0),(1,1); UV1 distinct finite values; white colors; indices0,1,2 in both wrapper kinds; group(0,3,0); bounds min(0,0,0)/max(1,1,0), sphere(.5,.5,0), radius1. Skin variant one identity joint, four indices0/vertex, weights(1,0,0,0). Exercise every admitted stream subset, typed required-attribute/skin predicates and exact rank. |
| Skeleton/clip | one/two identity-bind joints, parent -1/0, inverse bind identity; duration10000000; keys0/duration; position zero->(1,0,0), rotation identity->negative identity, scale ones. All domain/channel/interpolation unions, raw nonunit quaternion derivative tangents, events0/duration, sprite frames, root-motion absent/present, optional bounds absent/present. Root-motion scale keys/tangents obey its stricter rule. |
| Primitive | For every e=-30..30 register an independently assigned fixture profile/settings(e,12), plus Default and equal-settings aliases. Derive L_e/H_e and four definitions at published endpoints, adjacent values, exact corner needles, measure/bounds/identity/settings facts. Add the two prescribed extreme/tie witnesses and all measure zero/normal/subnormal/carry/underflow/overflow cases. |
| Scene/prefab | Exact current-owner A/B/C/P/Q/R/S occurrence witness plus the small camera/sprite/mesh/collision hierarchy below. Ordered Entity/Placement entries, multi/empty-root placements and per-root final poses, nested override layers, composite typed paths/array indices, relative/absolute anchors and explicit external slots. RFC-sort maps independently of hierarchy; World assignment uses expanded-key order, not caller occurrence IDs. |
| State | One committed tick, n1/pub2/base0/segment(1,0,1), exact profile/frames, automatic ordinal1 plus 4096-page and retired/scene/branch variants, strict rows/free list, participant/PCG/asset/scene joins. Cross Standard/Strict and explicit Branch/IndependentFork/RecordedContinuation intents, optional Frame2, all controls/same-tick/signedzero branches, source membership, transformed target bits, actual workers0/1/4 and complete file-generation integrity. |
| Continuation | Exact StrictLocal base checkpoint and one-/two-tick deterministic standalone records; no Input consumer. Empty/full command variants, canceled Create, every command/target/presence case, complete control/version/PRNG admission, actual preassignment trace and before-stage9 expected hash; unrecorded edit/source exhaustion and explicit branch exit. |
| Package | None package from fixed envelope bytes, exact entry/dependency/profile tables and strings; independent literal Brotli decoder corpus, then separately pinned compressor producer. Publication and every key/dependency/metadata presence join covered. |

#### M03 9.3.1 Exact fixture CApp descriptors and binding inputs

All keys in the next table have prefix `fixture/m03/schema/` and suffix `/v1`, use S(full key), version 1.0, Owner=Application, ReservedTags empty and the exact [M03 3](10-binary-data-and-authoring-formats.md#m03-3-complete-descriptor-notation-and-expansion) field/rule expansion. Each name is a full assignment key, not an ordinal runtime identity. Component storage/restore and roles are explicit. The zero-field tag is a `content/tag-schema` row, not a DataSchema row. CApp's local Rules/Migrations/SemanticProfiles are empty; its sole import is exact C0/Composition; CurrentSchemas is the complete inherited table plus these local tuples. Foundation/CompositionVersion remain C0's values.

| Middle key / role / storage / restore | Complete field tags and RR |
|---|---|
| persistent / Component / Persistent / NotApplicable | 1 Counter U64[0..L] ^p D(0); 2 Target? G/Entity ^p; 3 Vector V3 D(0,0,0); 4 OpaqueId G D(A); RR empty. |
| editable / Component / Persistent / NotApplicable | 1 Settings R{fixture/m03/schema/editable-values/v1} ^p; 2 Other R{same} ^p; 3 Asset? R{native/asset-reference} /Asset ^p; RR empty. |
| editable-values / Auxiliary / NotComponent / NotApplicable | 1 Value F ^p D(0); 2 Optional? F ^p D(negative zero); RR empty. Here `same` above means this exact schema reference. |
| empty-tag / EntityTag | Zero fields; owner/assignment/version only in the tag descriptor. |
| system-counter / SystemState / NotComponent / NotApplicable | 1 Counter I64[0..L] D(0); 2 Target? G/Entity; RR empty. Derived lookup/cache fields are not serialized. |
| resource-counter / ResourceState / NotComponent / NotApplicable | 1 Values FA(U64)[1..64]; RR empty. Values.Count equals the actual registered PartitionCount, never worker count. |
| attachment-counter / AttachmentState / NotComponent / NotApplicable | 1 Counter U64[0..L] D(0); 2 Target? G/Entity; 3 OpaqueId G D(A); RR empty. Transferred authoritative ownership. |
| settings-empty / TypedSettings / NotComponent / NotApplicable | Zero fields; RR empty. |
| settings-nonempty / TypedSettings / NotComponent / NotApplicable | 1 Flag Bool D(false); 2 Threshold F[0..1] D(0.5); RR empty. |
| reset-transient / Component / Transient / ResetDeclaredDefaults | 1 Value U32 D(0); 2 OpaqueId G D(A); RR empty. Standard reset positive; StrictLocal admission negative. |
| derived-transient / Component / Transient / ExactDerivedRehydration | 1 Cache U64 D(0); RR empty. Factory rehydrates from the same entity's persistent.Counter, exactly, without Execute or modifying authoritative state. |
| row24 / Component / Persistent / NotApplicable | 1 Data FA(U8)[24..24] ^p; RR empty. Concrete fixed inline 24-byte backing, with declared fixed-array element editing. |
| row32756 / Component / Persistent / NotApplicable | 1 Data FA(U8)[32756..32756]; RR empty. Concrete fixed inline 32756-byte backing. |
| row32757 / Component / Persistent / NotApplicable | 1 Data FA(U8)[32757..32757]; RR empty. Descriptor itself is valid; a World column/row registration is rejected as oversized. |
| literal-carrier / NativeAsset / NotComponent / NotApplicable | Tags 1..21 have respectively the 21 literal-family *value kinds*, ordinary field rules and explicit D values from the Literal carrier base. Tag 12 UTF8 has count [0..N] with D(bytes C3 A9); tag 13 Bytes has [0..N] with D(00 7F 80 FF); all other tags have [1,1]. No literal-record wrappers around these payload fields; RR empty. |

Fixed factory/system/resource IDs use S(`fixture/m03/factory/<middle-key>/1/v1`), version 1. The stateful system ID is S(`fixture/m03/system/counter/1/v1`), Order=-1, Begin/OwnerThread, read access to persistent and write access to its declared own authoritative state; the stateless system is S(`fixture/m03/system/stateless/1/v1`), Order=0, Begin/OwnerThread with empty accesses and no state row. The resource ID is S(`fixture/m03/resource/counter/1/v1`), PartitionCount=1 in base (variants 2/64), AllowParallelAccess=true, exact resource-counter StateSchema/factory. Its generated private UInt64 partition storage has no hidden shared counter or caller array. Binding variants add explicit read/write/disjoint-range system accesses without changing these schema identities.

The persistent/system/attachment Target wire field is Guid, but its runtime field is nullable `Entity`; its symbolic authoring representation is nullable stable Guid. OpaqueId remains Guid even when its bits equal Target. Generated bindings map only the declared Target through the complete receiving map, preserve optional presence and never call the Entity constructor. System membership-observation variants add the exact explicit kind4 Read/no-range grant; access to the system's own engine-owned authoritative state is its State2 contract, not a fabricated public Resource registration. Reset/derived transients have no payload in state/entity; only membership is serialized. Fixed inline row types pin source/layout and later verify actual registered size; no `sizeof(Vector3)`/padding estimate substitutes for those concrete byte types.

Owned versus unowned attachment and reference-present versus absent are explicit fixture cases with the same descriptor. Their factory/codec consumers are independent pinned build inputs, not generated from production round trips. A poisoned system-owned cache is rehydrated inside Restore and observationally validated afterward, preserving the previously recorded S2-State2 correction. Never add these fixture declarations to C0 to create fake consumers.

Use a real captured execution-profile manifest as frozen input for public/AOT execution vectors, obtained from artifact identity data, not from an engine encoder. Literal nonexecution runtime-profile vectors test wire/metadata only and are labeled as such. They do not qualify a fake OS/runtime profile for StrictLocal.

#### M03 9.3.2 Deterministic scene/state source construction

Let F(role,i)=S(`fixture/m03/` + exact ASCII role + `/` + canonical decimal i + `/v1`). Exponent-profile IDs are S(`fixture/m03/profile/exponent/` + canonical signed decimal e + `/v1`); aliases replace `exponent` with `alias`. Both use independently recomputed M02 settings/kernel/registry bindings, with chunk exponent 12. No runtime-generated Guid or production profile object supplies an expectation.

The small hierarchy/override/reference base is exact:

| Document | Authored construction |
|---|---|
| Leaf prefab, AssetId F(asset-prefab,1) | Root F(entity,101), child F(entity,102), Roots=[Entity101], children=[Entity102]. Root Local=(zero, Q(0,0,1,0), scale(2,1,1)), RootPosition canonical zero; child Local=(0,2,0)/identity/ones. Root carries initialized persistent/editable/row24 (24 zero bytes), Target=102 and OpaqueId=102. Reference Source=Relative([],101), property [FieldTag2], Target=Internal Relative([],102). No placements/external slots; RequiredAssets empty. |
| Container prefab, AssetId F(asset-prefab,2) | Root F(entity,201), ordered entries [Placement11,Placement12,Entity203,Entity202]; child Locals (1,0,0)/(0,0,1), identity/ones. Placements11/12 target Leaf with parent201 and one Relative([],101) RootPose, final Locals (0,3,0)/(0,4,0), identity/ones, RootPosition absent. Placement11 Sets editable [FieldTag1,FieldTag1] to F32 1 and Omits [FieldTag1,FieldTag2]; placement12 Sets [1,1] to negative zero. All override targets Relative([],101); no reference values. RequiredAssets=[Leaf]. |
| Main scene, AssetId F(asset-scene,1) | Ordered roots/children are the Main-placement row below. Root1 is default camera at canonical zero; root2 at offsets(0,0,1) has mesh/collision references. Literal RFC witnesses B/A carry initialized persistent; A additionally sprite/atlas and symbolic Target=leaf101; B symbolic Target=external-slot ID below. Child Locals (1,0,0)/(0,1,0), identity/ones. OpaqueIds equal those symbolic tokens and remain unmapped. |
| Main placements | Second-root entries=[Entity B,Placement1,Placement2,Entity A]; placements1/2 target Leaf with one Relative([],101) RootPose, final Local translations(1,0,0)/(-1,0,0), identity/ones. Main Roots=[Entity1,Placement3,Entity2]; placement3 targets Container with one Relative([],201) RootPose, RootPosition offsets(0,0,1), Local zero/identity/ones. Its outer overrides target Relative([placement11],101): editable [1,1]=F32 2, persistent [1]=U64 42. Both paths are explicitly PrefabEditable, including the revised Counter flag1. These share target context but not component identity. |
| Main bindings | Source Relative([],A), persistent [FieldTag2], Target Internal InstanceAbsolute([placement3,placement11],101). Source Relative([],B), same field, Target ExternalSlot F(external-binding,1); symbolic Value equals that slot ID. Declare that required/non-null slot with RequiredComponents empty. There are no other direct Entity leaves. Nested Leaf bindings remain their own occurrence-relative bindings. |
| External scene, AssetId F(asset-scene,2) | One root F(entity,900), canonical zero RootPosition/initialized Local; no other data/references/placements/assets. First activation uses automatic World scene ordinal1/entity ordinal2. Obtain its actual Entity with root-authored lookup and pass the typed slot binding to Main; Main activation reserves scene ordinal3 then its entities in ExpandedEntityKey order. It is a separate graph/root, not an unreachable node in Main's owning graph. |

Numeric authored Entity/Placement abbreviations mean the explicit F inputs, never runtime assignment IDs or IDs derived by a real importer from row order. All entity/hierarchy/placement/override/reference maps use their declared wire keys; ordered entries retain the lists above. Main's RequiredAssets is the RFC-sorted distinct union of direct Leaf/Container/mesh/atlas/primitive references; descendant mesh/material/texture versions form their own closure. Other fields use exact [M03 3](10-binary-data-and-authoring-formats.md#m03-3-complete-descriptor-notation-and-expansion) initializer values. Main B.OpaqueId equals the external-slot symbolic token and is not remapped. Asset inputs use F(asset-material,1), F(asset-atlas,1), F(asset-mesh,1), F(asset-primitive3,1). The next successful tick makes both activations extraction/capture eligible. Mutations vary one entry/pose/override key/property-segment/reference anchor at a time. Domain0 clip bases pin this exact Scene and use separately derived ExpandedEntityKeys; joint-only bases omit Scene.

The non-scene state base uses WorldGuid above, lineage F(lineage,1), Default profile, n1/rate60/pub2, scale1/segment(1,0,1), ReplayComplete=false/HistoryBase0 and exact revision1 zero-origin +/-4096 frames. Configuration uses explicit default ceilings. One automatic live Entity at ordinal1 is the published EN025 value `9a2b3839-8a40-8fb7-aee5-740dacac1a2c`; its persistent Counter0/Target absent/Vector positive zeros/OpaqueId A, no tags/transients. Identity directory index0 is the independently derived root branch with lineage F(lineage,1), WorldGuid namespace, tick0/first ordinal1 and absent parent/source/fork; page FirstOrdinal1 has that ID and metadata `0x00005400` (Entity/Automatic/Live/index0), next ordinal2. Stateful system Counter1 after one Execute; stateless no row; resource Values=[0]; attachment Counter0/Target absent/OpaqueId A. PCG42/54 is exactly initialized and unconsumed. Scenes/Hierarchy/Assets empty. Strict slot1 Live/gen1/row0, exact archetype/chunk0/capacity, NextChunkOrdinal1, barrier2 and accepted requests0/next1; Standard omits strict layout/cursors. Frame2-absent omits both current/initial Frame2 and forbids its controls.

Branch fixture inputs are explicit F(branch,1) and optional fresh migration lineage F(lineage,2); independent fork uses F(world-fork,1)/F(lineage,3), subject only to nonempty/fresh active-GUID admission, not Entity explicit-domain restrictions. Independently transform full history/declared references/directory and derive the new future branch; retired and source-provenance/opaque-equal-Guid variants are separate. Add one non-v8 explicit top-level instance request A: its original activation fields both equal A; after fork only current SceneInstanceId transforms to the new World/ordinal UUIDv8, while the original request/result stay A. Cross automatic, explicit, retired and twice-forked provenance plus direct-control versus source-bound-load branch rows. A self-consistent alternative checkpoint with new valid identity/hash facts is positive under its matching context; the same bytes against independently pinned wrong source/target context reject. Do not call a harmless changed digest a malformed-history fixture.

Continuation base starts from the exact StrictLocal n1 source. Tick1 has no controls/commands, exact before-PCG/assets/scale, Counter becomes2 and n2/pub3; tick2 Counter3/n3/pub4. Both expected canonical hashes are independently computed from those state facts. Command/control variants substitute explicit generated fixture systems with deterministic effects, all op/target/presence/cancellation branches, and declared source inputs; they never take expected hashes from production recordings. Real execution profiles replace inert artifact facts before both oracles derive execution vectors.

Module ID is F(module,1)/version1 in both application/runtime tables. Nonexecution wire bases explicitly choose Windows/x64/AOT, scalar MaximumSimdBits0, FmaDisabled, ISAFlags0, nearest-even/no flush/no reassociation, and digest operands from immutable literal strings `fixture/m03/<artifact-name>/1/v1` via SHA256. They are wire/model contexts only. Public StrictLocal vectors instead substitute the independently captured *actual* compiled profile/artifact facts before both oracles run; they never execute a fake profile or compare production output to an expectation copied from it.

#### M03 9.3.3 Exact correction inputs and derivation effects

The following rows are mandatory additions to the independently derived source/ledger, not existing fixtures/tests or accepted execution. Every binary and output digest remains PendingOracleAgreement until actual derivation; the numeric field/rule operands below are selected normative inputs.

| Correction source row | Exact operands and expected facts |
|---|---|
| `correction/counter/editable-positive` | CApp1 persistent1.0 tag1 keeps U64/range[0..L]/required/default+0 and receives AuthoringFlags=1 at its content/field tag16, not Animatable2. Target/Vector/OpaqueId retain flags1/0/0. Main placement F(placement,3) retains target Scope1, Path=[F(placement,11)], AuthoredEntityId=F(entity,101), component S(fixture/m03/schema/persistent/v1)/1/0/Content0, PropertyPath=[(FieldTag1,1)], Value=content/literal/u64 tag1 UInt64LE42, Operation0, ReferenceValue absent. Runtime Counter at root-relative [placement3,placement11]/101 is42; all otherwise initialized persistent Counters remain0. |
| `correction/override/noneditable-valid-path` | Starting from that fully corrected positive, add one Set in Main placement3 for the same relative target/component but PropertyPath=[(FieldTag1,3)], Value=content/literal/vector3 with bits3F800000/40000000/40400000, Operation0, ReferenceValue absent. Preserve the legal Counter42 Set and other overrides; independently sort the complete composite keys. Tag3 exists and is finite V3, but its AuthoringFlags=0; reject under prefab rule21, version1, RR useIndex0, SchemaId=S(native/prefab-override), diagnostic Tag3(PropertyPath), path of that record's PropertyPath element0. Core remains Canonical Complete; owner consumed/written0, unusable value/unchanged destination, no native publication or World activation. |
| `correction/precedence/retained-state` | For that same target, editable.Settings.Value is F32 bits40000000 after the outer Main2 beats inner Container1; editable.Settings.Optional remains absent from the inner Omit. Counter is42, and any separately selected derived-transient membership rehydrates Cache42 inside Restore. Other Leaf occurrences and default Counters remain0. OpaqueId/reference/source-occurrence/hierarchy/assignment facts remain [M03 9.3.2](10-binary-data-and-authoring-formats.md#m03-932-deterministic-scenestate-source-construction)'s, with no silent remap or normalization. Execute save/restore/continuation variants only after the corrected carrier is actually admitted; the old invalid Main never supplies execution evidence. |
| `correction/provenance/runtime-without-originals` | Use the [M03 9.3](10-binary-data-and-authoring-formats.md#m03-93-literal-source-bases) full rooted graph/output/publication/package under revised C0/CApp. Record exact source fingerprints and pinned inert producer facts, but do not deploy original sources/importers/offline producer binaries. Load in phase2, rederive every runtime-verifiable graph/context/key/package/version join through Assets and explicit owner codecs. Successful bytes/model checks do not claim original-source or producer execution. |
| `correction/provenance/offline-source-mismatch` | Cook-only input retains actual source bytes whose length/hash disagrees with supplied source facts; refuse before producer/publication. Runtime receives no such retained source input. A consistently changed source claim and completely rederived publication under its separately chosen source pin is positive provenance, not the same negative. |
| `correction/credits/one-node` | One canonical literal-carrier payload and a node with AssetId=F(asset-credit-node,1), dependencies initialized-empty, Metadata absent. Use a one-node rooted graph, empty supplied dependencies/sources, registered Default/CApp1 and the exact native convention/specification. Let node N, graph G, request R and accepted operation O each own one separate read credit to that range; close original owner/lease. Descriptor/corpus facts describe this byte input; lifecycle traces, not an invented binary schema, cover subsequent disposal/cancellation. |
| `correction/credits/shared-two-graphs` | Reuse the same open node for G1 and G2, each with that node as its unique root and exact independent retained credits; build R1/R2 and operations as selected by [M03 10.6](34-engine-acceptance-cases.md#m03-106-independent-correction-coverage-and-exact-acceptance-effects). Same node twice within one graph is the duplicate-ID negative. Credits/disposal state never enter keys/canonical bytes. |

In the noneditable negative, compute `i` as placement3's independent zero-based position in Main.tag4's RFC-sorted placement table and `j` as the new Vector Set's position in that placement.tag5's full override composite order. The physical envelope path is tag4(Payload)/tag4(Placements)[i]/tag5(Overrides)[j]/tag3(PropertyPath)[0]; graph/raw-payload carriers prefix their own containing path. These deterministically derived positions, row/source SHA and exact first diagnostic are materialized by each oracle, never guessed from authored hierarchy order. It is not a wrong-type, missing-path, alias or count negative.

Adding `^p` is selected because Main is an intended positive, component-qualified outer-layer witness. Removing Counter42 would erase that branch and change its intended state; widening all fields or bypassing segment editability would violate the owner. Vector remains noneditable, providing a legal typed path that fails only the required flag predicate. No Counter default/range/storage/identity/layout or ordinary runtime mutation permission changes.

Retaining candidate schema/rule version1 while correcting these operands is permitted only because the original C0/CApp/recipe set was unaccepted and has no admitted reader/fixture revision. This is a pre-freeze candidate replacement. Chapter10's new-major/migration rule still applies to a change after actual admission; no reader may reinterpret an accepted old version using these revised flags/owners.

All affected byte/digest operands must be rederived as follows:

| Operand | Exact revised input / propagation; what remains unchanged |
|---|---|
| C0 schema descriptors | Each of the20 [M03 4.4](11-assets-import-streaming-and-packages.md#m03-44-runtime-owned-provenance-model-and-common-verification-kernel) common provenance/settings schemas has content/schema tag11 Owner7 -> 1; roles/tags/fields/RR/versions unchanged. NameRecipe schema IDs/keys do not include Owner and remain unchanged. Other C0 descriptors and all M02 identities remain unchanged. |
| Four recipe/rule rows | content/rule-recipe tag7 and matching content/rule tag7 for rules17/18/19/49 become1. Recompute those four RecipeDigests and all places carrying them. IDs/version/Algorithm/ParameterSchema/RequiredContexts/ApplicableScopes remain exactly [M03 4.4.1](11-assets-import-streaming-and-packages.md#m03-441-exact-owner-and-rule-projection)'s. The changed candidate meanings are this revision of unaccepted recipes, not a silent change to an approved algorithm. |
| C0 manifest | Reencode changed schema/rule rows, all canonical ordered collections and the manifest; recompute ContentSchemaDigest. Counts198/851/51, active tuples, semantic profiles and Foundation binding operands remain. No final hash/byte length is asserted. |
| CApp1/CApp2 | persistent field tag1 AuthoringFlags at descriptor tag16 becomes1. CApp1's Composition import uses revised C0 digest; complete active tuples stay the same. CApp2 inherits revised C0 and HistoricalSource-imports revised CApp1; its copied persistent2.0 tag1 also has flag1, and migration SourceCatalogueDigest uses revised CApp1. The header-copy specification/source-target reference/parameter bytes are unchanged, so its standalone semantic-recipe preimage remains unchanged; the containing migration/manifest bytes still require reencoding. |
| Initial native payloads/source fingerprints | Main's existing literal Counter42 override bytes are unchanged by flag/owner amendments; other unchanged payload trees keep their PayloadInputDigest. The noneditable negative adds its explicit record and therefore requires its own payload digest. Source.ContentDigest/ByteCount changes only if the actual literal source bytes change; do not blindly rewrite unrelated source fingerprints. Initial Counter default bytes remain+0. |
| Graph/context/key/native versions | Rebind graph catalogue/profile selection to the revised manifest, rederive GraphDigest/ContextDigest, four changed recipe-use digests and registered model closure, every affected NativeKeyDigest, envelope tag5 catalogue digest and EnvelopeDigest. Dependency full versions/digests and publication RootSchema/PayloadSchema/catalogue/profile/recipe/key joins update topologically. Raw profile/settings/M02 digest operands and AssetIds remain unchanged. |
| Package/publication/source registration | Rebuild EIDX decoded hashes/dependency slices/index hash, compression outputs and alignment/lengths as necessary, PackageKeyDigest/PackageDigest, published outputs and PublicationDigest. Source registrations/load RequiredVersion/required digest pins use the newly derived full version and package/publication hashes. A byte/layout that is genuinely unchanged is independently checked rather than assumed changed. Producer fingerprints for inert nonexecution vectors remain their literal facts; future real producer pins must be recaptured after compiled ownership/binding changes. |
| Scene/state/continuation | Active AssetVersion, scene/occurrence/control source-version facts and Header.CatalogueDigest use the revised full versions/catalogue. Recompute Counter42/derived-cache facts at the selected instance, complete canonical projection/hash, CheckpointDigest and generation-bound FileContentDigest; continuation source checkpoint/payload/ReplayDigest, command payload hashes and affected before/after canonical expectations are independently rederived. World/author/occurrence IDs, assignment ordinals, tick/control/segment cursor algorithms, layout sizes and opaque-reference behavior do not change. The non-scene Counter0 base keeps value bits but still rebinds catalogue/runtime profile as applicable. |
| Generated/producer/execution approval | Schema helpers for the20 models and four kernels move to Assets; Counter initialization/override/binding metadata changes. Recapture actual owner/source/compiler/producer artifact registries after implementation and derive their producer-vector extension independently. Source/ledger/corpus/reviewer hashes and selected/executed counts need a new reviewed revision; the original artifact/approval evidence is retained, never patched in place. |

The Core Retain and node/disposal changes add no serialized field or rule; lifecycle/evidence and compiled artifact pins change, not schema counts/M02 bytes. Static C0/CApp identity keys are unaltered. Candidate old/new catalogue digests may not be treated as equivalent or implicitly aliased by readers; only the separately reviewed revised set is a future supported registration.

#### M03 9.4 Exhaustive branch generation

For every schema/tag generate legal positive values/presence and isolated negative mutations. For every reachable optional/discriminator combination *within one record's shape and required containing context* enumerate it, not pairwise sampling. This is not a claim to enumerate all numeric values or all million-element array permutations. All material texture slots therefore include all 16 PBR presence combinations; all primitive topology/processing-field presence combinations (64 per statistics dimension) are classified against each of its two definition discriminators. An inapplicable branch gets a correctly rehashed rejection, not an impossible claimed positive. For arrays, exercise every admitted union at first/last positions, empty/nonempty/count endpoints and each ordering key independently; recursive paths get explicit depth/context coverage rather than an infinite Cartesian tree expansion.

For every default: independently encode the initialized required value; absence rejects, except a declared optional omission branch; explicit nondefault values preserve bits. DM covers all61 exponents/both scalar overload distinctions; DP crosses both profiles/alpha/double-sided/normal-texture conditions. Record/array defaults, bad literal kind/arity, NaN finite default, unregistered contextual default and digest-dependent default reject.

Asset ID/version cases use [M03 4.3](11-assets-import-streaming-and-packages.md#m03-43-exact-public-asset-identities-version-bindings-and-digest-scope)'s public/semantic contracts explicitly: default/empty/nonempty/UUIDv8 Guid, all constructor digest lengths and signed version domains, default schema/profile, independent equal digest arrays and caller-array mutation after construction, value-versus-reference equality/null, same-content/different-recipe-or-key versions, exact/current/digest-constrained selection, conflicting required ID/digest, and each version-to-wire field join. Compare envelope versus payload/package hashes, complete canonical padding and inert optional preservation, root2/payload1 and every isolated profile/catalogue/recipe/key mismatch. Native request-only rename throws before admission; consistent graph rename enters tag1 and changes derived keys. API-constructor rows mark Core reader/writer status NotApplicable rather than pretending a constructor test executes a codec; companion carrier rows exercise actual version records/envelope/EIDX/publication/readers/writers and consumers.

The concrete fixed-array override positive Sets Leaf row24 at `[FieldTag1,FixedArrayIndex0]` to literal U8 7; negative indices24/uintMax and wrong segment-kind/tag reject. Entity-reference override positives target its declared persistent.Target through Internal/ExternalSlot with the correct anchor; raw-Guid spoof and inappropriate null reject. These are real fixture component branches, not invented built-in components.

Counter's corrected flag1 positive and Vector's existing valid-path flag0 negative are mandatory independent branches under [M03 9.3.3](10-binary-data-and-authoring-formats.md#m03-933-exact-correction-inputs-and-derivation-effects). Common provenance verification runs at both offline and runtime boundaries, with absent-originals positives and rehashed rule17/18/19/49 negatives. Node/graph/request/Core Retain constructors, failure and lifecycle traces are mandatory nonbinary rows with reader/writer status NotApplicable; companion canonical payload/graph/publication carriers retain all byte assertions.

Numeric fields cover endpoints and nearest representable outside; integers zero/high bit/max/one-beyond where representable; finite floats positive/negative zero, subnormal, maxfinite, NaN/+/-infinity as admissible or rejected by their exact contract. Kind/count/stride flags and every nested union/role/identity/version get a branch.

Ordering covers each key independently, equal prefixes, missing-before option, full composite duplicates versus legitimate shared partial keys, source selectors that alias one effective override target, property FieldTag/FixedArrayIndex pairs, semantic unsorted-but-unique hierarchy/page-column lists, reversed tuples and RFC/mixed-endian/culture witnesses. Include all selector anchors, multi/empty roots, root/child Entry kinds, exact current-owner nested vectors, automatic/explicit/fork-imported metadata, 4095/4096/4097 page boundaries and branch intervals. No empty filter counts as execution.

Where a nominal count maximum is shadowed by a stronger byte/cross-field limit, the ledger records the exact arithmetic/reachable maximum and a stronger-limit witness. It never marks an infeasible maximum as Complete, silently drops a branch, or uses a malformed array to claim a contextual count test.

#### M03 9.5 Deterministic schema-to-carrier closure

Build the ledger from the expanded catalogues, not a handpicked scenario list. Every selected tuple gets a `schema/<RFC-id-N>/<major>/<minor>/base` row. Indices below are the independently computed zero-based positions in the relevant canonical sorted table; default/rule-use lists retain their declared sequence. Duplicate reachable paths get separate consumer/context rows, not only the first match.

| Selected records | Exact carrier construction |
|---|---|
| manifest/schema/field/tag-schema | Complete C0 or CApp manifest; Schema=tag1[i], Field=Schema.tag4[j], TagSchema=CApp.tag2[empty-tag index]. Every field's counts/flags/default/nested sets/element rules are explicitly projected. |
| schema-ref/identity/rule/rule-use | CurrentSchemas tag5 and each real nested-reference path; schema Assignment tag6, rule tag3, profile tag2 and binding tag2; Rules tag3[i]; schema RR tag8/field Rules7/ElementRules12. Cover every ordered use/parameter digest, not a representative rule ID. |
| rule-recipe | Separate complete canonical recipe row for each of all51 rules, exact binding context and expected RecipeDigest; those bytes are not falsely described as embedded in the manifest. |
| foundation/profile/profile-binding/default/literal | Manifest Foundation7/SemanticProfiles9 and profile Bindings4; declared field Default8 for all literal-family CApp carrier defaults, camera DM and material DP. Raw literals additionally have standalone intrinsic carriers; contextual destinations remain explicit. |
| parameters/order-key/property-segment | Every recipe/default/use plus standalone intrinsic parameter cases bound to an explicit destination-kind/scope context. Enumerate Source0..3, Comparison0..6, Direction0/1, Missing0..2, RangeDomain0/1 and endpoint presence; valid metadata parameters do not imply that a particular fixed C0 field uses that order. The row24 override supplies real FieldTag/FixedArrayIndex consumption. |
| import/migration | CApp Composition import plus the exact migration pair below/HistoricalSource import. No fake positive migration is assigned to C0's required empty Migrations. |
| transport/cook/package | Full rooted graph and reconstructed-publication example, complete envelopes/dependency closure/EIDX/profile/string records, None/Brotli policy branches, metadata absent/empty/value, all implementation/settings/key/publication records. Explicit public AssetId/AssetVersion cases project their nine properties into every version carrier, with copied-array/equality/selection and root/key admission assertions from [M03 4.3](11-assets-import-streaming-and-packages.md#m03-43-exact-public-asset-identities-version-bindings-and-digest-scope). Hash-only projections get their exact producer root too. |
| scene/prefab | Each root document in [M03 9.3.2](10-binary-data-and-authoring-formats.md#m03-932-deterministic-scenestate-source-construction) and the current-owner seven-entity witness; physical hierarchy/placement/root-pose/selector/override/external-slot/reference paths are enumerated. Multi/empty-root and optional/null external variants have complete explicit source graphs. |
| Physics | Four primitive roots, each statistics/settings/identity/measure path across all61 exponent contexts, dimension/discriminator/count/processing cases. Never admit a support Statistics row as a usable shape without its definition/profile. |
| rendering/animation | Exact texture/material/atlas/mesh/skeleton/clip bases and their containing semantic roots; every stream/key/discriminator/slot combination has a source/value/context row. Domain0 keys require the selected source Scene's expanded map. |
| checkpoint/identity/layout/scene/controls | Exact Standard/Strict source checkpoint, mode/participant/control/branch/fork variants, paged identity/directory/source-provenance records, strict allocated slots/chunks and complete registry. File-content/canonical projections are actual hash-writer cases; source/target records remain distinguished. |
| continuation/commands | One-/two-tick complete standalone record; op1..8 presence/target branches, exact command keys and real generated systems; header/runtime/controls/PRNG/assets context and preassignment/before-commit expectations. |
| fixture components/tags/state/settings | All explicit CApp rows through native symbolic writer, actual World typed registration/defaults/queries/state bindings and consumer output, including declared-versus-opaque reference and transient reset/derived branches. |

Each component fixture's expected record/value is independently pinned to the frozen `ComponentFactoryDescriptor`: exact schema tuple, FactoryId/FactoryVersion, Storage/Restore/flags, context-qualified LayoutId and physical `Unsafe.SizeOf<T>()` stride. Layout/source-leaf audits are separate from EBIN canonical bytes: stride includes padding, while expected encodings/hash projections exclude padding contents. Exercise actual default, typed getters/presence, declared Entity-reference mapping, opaque-Guid preservation, restore, fresh Count/Write and saved-layout admission with distinguishing nondefault values. A same-size wrong accessor or equally swapped encoder/decoder must fail against independent expected values/bytes, not pass by round trip. Metadata/build declarations cannot certify the selected source layout or emitted provider code.

The mandatory handwritten witness is a separate nonfriend public-only test executable for an already selected CApp component, with component binding generation disabled for that type. It creates the catalogue-derived schema, storage/path declaration, concrete accessors and `ComponentFactoryBinding<T>.Create<TAuthor>` using ordinary public APIs; during actual decode it invokes the engine-issued `ComponentReadBinding<TAuthor>.Complete` and `Reject` ports. Compile with warnings-as-errors in ordinary and NativeAOT lanes, execute actual creation/default/query/capture/restore paths, and compare to the same independent pinned fixtures as the selected generated-provider variant. Use neither InternalsVisibleTo nor a private issuer. A result factory invoked outside its exact callback, null/foreign/duplicate result, absent-as-default synthesis, bad consumed extent, missing reference map and an opaque Guid matching a live Entity are distinguishing negatives. This witness adds no C0 schema, field, rule, wire kind, selector or acceptance ID.

The migration fixture uses source CApp1 exactly as above and destination CApp2 that Composition-imports C0, HistoricalSource-imports exact CApp1, retains its other local fixture rows and declares the same persistent schema GUID at major2/minor0 as its active target. The one migration maps CApp1 persistent1.0 to target2.0 with S(`fixture/m03/migration/persistent-header/1/v1`), version1, parameter schema/value settings-empty, and exact specification bytes ASCII `Copy tags 1-4, presence and bits unchanged; emit target schema major 2 minor 0`. Compute its semantic digest by [M03 identity and provenance](10-binary-data-and-authoring-formats.md#22-m03-content-schema-and-package-binding)'s exact preimage. Register the bounded pure typed header migration separately; validate both schemas/contexts. Cycle/duplicate-next/missing source/>16 and changed bytes are negative. This changes no C0/M02 identity.

The only selected executable migration positive is the Standard transition of the non-scene/no-assets base: `Assets=[]`, `Scenes=[]`, `Hierarchy=[]`, and `Controls=[]`, with the persistent1.0-to-2.0 edge above. Preserve entity identity/history/creation provenance, clock, RNG, frames and participants; change only the active catalogue and persistent header/registration, using a distinct `PersistentV2` type and factory `S("fixture/m03/factory/persistent-v2/1/v1")`, version1. It performs no hidden tick. A Branch variant additionally uses fresh lineage `F(lineage,2)` and `F(branch,1)`, appends the exact source-bound Branch2 directory row at index1, and preserves the original entity creation lineage. Reject absent/nonfresh migration lineage and any attempt to retain the old lineage. HistoricalSource authorizes decoding CApp1 only for this registered pure transition: active CApp1 scene/prefab/assets remain unauthorized by CApp2, and rehashing them under CApp2 is not a positive. StrictLocal schema/profile migration, RecordedContinuation after migration, and Standard continuation are negative; recooking/rebinding scene/assets requires a separately reviewed destination-compatible transition and is not selected here. A C0-published asset with no CApp1-only dependency is a distinct possible lane, not general CApp1 activation authority. Selected pure migration belongs to BN07 and applicable AS08/EN125/126 CPU persistence observations; this does not close their parent cases or create an EPKG1/legacy-container positive.

Foundation retention carriers add optional tag250, kind UTF8, flags0, count1 and payload byte78 to a base foundation/profile and to each base frame dimension. Preserve exact canonical bytes through containing native/state Read/Write and hash projection; fork changes only the frame's known WorldGuid and retains tag250. Required-flag variants retain the unchanged M02 unknown-required failure. Reserved profile tag4 and other M02-invalid fields remain invalid; an inert extension positive does not bypass their rules or alter Default.

Literal-assignment intrinsic vectors use fixed distinct IDs `90000000-0000-4000-8000-000000000001` through `...000007` for Purpose1..7, version1/minor0, Owner Application9 for schema/tag/rule and Core0 for material-profile, Rendering4 for shader/pass/layout. Exact specification is ASCII `fixture/m03/literal-assignment/<purpose-decimal>/v1`. Their independently approved receipts bind those complete operands. They test identity/local metadata verification only; they do not install active literal rule/profile implementations or override C0. An active literal-schema variant is a separately approved CApp declaration with the literal carrier's exact fields; its producer/registration/hash joins are mandatory before use.

For every declared optional/default/discriminator/union/context branch derive its admissible or rejected status, root carrier, exact physical path and first diagnostic from the written rule predicates. A branch without a legal positive is a labeled semantic negative with its invariant and unrelated hash repairs. A source case missing a carrier, producer/binding registration or explicit unreachable/stronger-limit proof blocks fixture approval; no generation from production defaults/encoders or empty selection fills the gap.

### M03 10. Mutation/status/coverage contract

#### M03 10.1 Complete coverage-ledger shape

Each row has:

```text
rowId; acceptanceIds; milestone/selector; exact schema tuple; tag/path;
ruleId/version/useIndex/parameterDigest; role/context/default/discriminator branch;
sourceInputId/sourceSHA256; rootCaseId/rootPath; positiveBaseId;
mutationId/operations/targetInvariant/derivedRepairs; policy/limits/contextSHA256;
expectedByteLength/SHA256; expectedSemanticFactsSHA256;
Core.ReadHeader/Validate/Read status+consumed+callback+diagnostic;
owner.Read status+consumed+valueUsable+diagnostic schema/tag/rule/path/category;
owner.Write input/expected status+written+unchanged-destination assertion;
generated binding/default/query/reference/presence assertion IDs;
actual production reader/writer qualified symbol+source revision;
consumer boundary assertions; oracle A/B revision/output hashes;
approval disposition/review SHA256; actual selected/executed results when later run.
```

Unique join keys are `(schema tuple,tag/path,rule use,branch,producer/binding)`. Require total joins from all expanded descriptors and all actual producer/reader registrations. Missing rows, unexpanded role unions, optional/default/discriminator branches, zero selected tests, unreviewed hashes or orphan implementation symbols fail the gate.

Producer registry distinguishes direct authoring/default creation, generic/native schema writer, envelope/package/publication writer, component/state/scene participant writer, save/canonical projector and generated binding adapter. A shared helper may have multiple roots, but each actual writer and each shape-changing binding needs a coverage row. Prospective later producers are explicitly Pending at their owner milestone, not reported as M03 executed writers.

Prospective generated symbol naming is deterministic: for schema tuple `(id,M,m)` emit `C_<lowercase RFC id N>_V<M>_<m>` under the schema owner's permitted namespace, with `Read`, `Write`, `Validate`, `Count` and (only when defaults exist) `InitializeDefaults`. Header/raw/symbolic versus World-bound component helpers are distinct `Symbolic` and `WorldBound` bindings; Entity remapping cannot hide inside the same opaque-Guid helper. Tags get membership bindings, not mutable-column writers. Helpers for support records are exercised through every actual containing root as well as their own local value checks.

The registry's complete M03 roots are Core descriptor/parameter/default/literal/profile codecs; Assets envelope/dependency/version/package/index/source codecs plus the20 shared graph/context/key/settings/package/publication model writers and common verification kernels; Content.Tools native orchestration and its invocations of those Assets writers/kernels; Scenes scene/prefab/hierarchy/override/reference preparation; Rendering mesh/stream/index/material/texture/atlas/component writers and Extract; Animation skeleton/clip/key/event data writers; Integration primitive native adapters; Entities component/tag/system/resource/attachment/clock/control/layout/identity state participants; Hosting file/checkpoint/canonical/capture/replace coordination; and the fixture application's explicit state/query/default/reference factories. Generated Core/Entities code uses Core metadata/bindings, never an Assets or higher-layer type reference. Package/header/index producers have layout cases in addition to EBIN descriptor rows. No C0 helper may be orphaned by merely declaring a prospective writer; pure common models do not remain Tools-owned aliases.

Include actual continuation/recorded-tick and structural-command trace readers/writers/projectors explicitly in those Hosting/Entities producer roots. Their prefix selection, source digest, opcode/presence/token/partition and expected-state comparisons require ledger rows; a save-writer row does not implicitly cover them.

Pre-codec descriptor/oracle approval records deterministic prospective symbols as PendingImplementation, not Covered; it does not require an artifact that cannot yet be built. After implementation, replace them in a separately hashed execution-ledger extension with actual compiled symbols/source/artifact pins. Total descriptor/branch joins and independent byte expectations are the pre-codec gate; executed writer/binding/public-consumer joins and producer-specific vector extensions are the later implementation gate. Neither inherits acceptance from the other.

Tooling source/ledger/fact documents use one pinned canonical JSON encoding J: UTF8 without BOM/newline, no whitespace, ASCII object keys sorted by ordinal bytes, arrays in declared order, integers represented as canonical signed/unsigned decimal strings, booleans literal true/false, float bits exactly eight lowercase hex digits, byte strings lowercase hex, GUIDs lowercase RFC hyphen form. Strings contain ASCII only: escape quote/backslash as `\"`/`\\`, controls as lowercase `\u00xx`, and emit all other ASCII bytes literally; no alternative slash/short-control escapes. User UTF8 text is represented by its exact hex bytes, not escaped/NFC-normalized text. No JSON float or provider-dependent dictionary serialization. SHA256(J(document)) defines source/ledger/semantic-fact hashes. A document never embeds its own hash.

Source-tree/build-file inventory is a J array sorted by unsigned decoded relative-path UTF8 bytes. Each entry is exactly `{"byteCount":<canonical decimal string>,"pathUtf8":<lowercase hex>,"sha256":<64 lowercase hex>}`. Logical paths use backslashes and exact source spelling, are nonempty relative paths with no empty/dot/dot-dot segment, and reject duplicate/case-alias paths. Hash file contents including actual line endings/BOM; exclude timestamps, absolute checkout paths and unlisted generated output. SourceTreeDigest=SHA256(J(inventory)); the receipt pins the complete enumerated file set, so omission is not an alternate valid inventory. Build/profile manifests use `files`, `kind`, `versionUtf8`, plus `osFamily`/`osIdentityUtf8`/`runtimeVersionUtf8` for Runtime and `sdkVersionUtf8`/`codegen` for Compiler, exactly these fields and actual literal identity bytes; kind is engine/runtime/compiler. Thus their ordinary manifest-file hashes are independently reproducible, not an unspecified ZIP/tree hash.

Ordered corpus digest has this exact preimage; only vector-producing rows participate, sorted by ordinal ASCII rowId:

```text
ASCII("TerraFX.M03FixtureCorpus.v1") || 00 || UInt32LE(vectorCount) ||
for each vector:
  UInt32LE(rowIdByteLength) || ASCII(rowId) ||
  UInt64LE(vectorByteLength) || SHA256(exact vector bytes) ||
  SHA256(J(expected semantic facts))
```

Approval/report hashes are separate receipt operands, not fields inside the source/corpus they approve. Recomputed source/ledger/corpus revisions are staged as a new set; never edit an accepted expectation in place.

#### M03 10.2 Negative matrix

| Class | Mutations and expected boundary |
|---|---|
| Core header/prefix | Magic/reserved/extent/table flags/count/overflow and major0/high-bit major/high-bit minor: InvalidRecord. Unsupported container version and unknown required wire kind: UnsupportedSchema. Both consumed0/no callback; valid but incomplete prefixes NeedMoreData0. ReadHeader root may still Complete64 when only a nested body is bad. |
| Structure | Tag0/duplicates/order, flags, known kind/count/stride, overlapping/outside fields, bad zero padding, nonminimal canonical packing, record lengths/offset tables, nested aggregates/depth; Structural versus Canonical differentiated. |
| Semantic selection | Representable unregistered schema/version, role, validator/recipe/profile/source catalogue and missing typed registration: Core may Complete, owner UnsupportedSchema, consumed0/unusable. Known conflicting descriptor/default/identity/profile/required field: InvalidRecord. |
| Defaults/presence | Every required omission, invalid declared default/literal kind/arity, optional absent/empty/value branch, forbidden inactive fields, signedzero/NaN and record-array default refusal. A valid explicit nondefault is positive and retains bits; defaults are initialization, not equality constraints. |
| C0/composition | Omitted inherited selection, conflicting diamond/local duplicates, tag/data GUID collision, wrong owner/role bits/scope/parameter version, unsorted rules/profiles/refs, missing import, self/cycle migration/>16, fake recipe digest or self-hash. Exercise exact distinct-manifest/aggregate-byte/effective-tuple/profile/selection ceilings and nearest representable overrun. The4096-edge ceiling is shadowed by the64-node acyclic distinct-edge maximum2016; ledger proof and node/duplicate/cycle negatives replace an impossible4096-edge positive. |
| Envelope/package | AssetId/root/payload/version/digest/catalogue/profile/dependency slice mismatch; missing/cyclic exact dependency; bad index/profile/string ranges/reserved bytes; None length mismatch; corrupt/truncated/short/excess/trailing Brotli; inert policy overrun. |
| Cook identity | Request-only rename must synchronously throw before admission; consistent graph/root rename changes keys. Change sources/options/artifacts/validator/profile/catalogue/dependencies/metadata independently. Wrong graph/key/output/context/publication joins reject even with valid enclosing hashes. Repack changes package key, not native content/full version solely for packing choices. Equal content SHA256 with different valid recipe/native-key facts remains a distinct full version; independently pin both publications and test exact versus current/digest-constrained selection. |
| Physics | Nonfinite-before-constructor, wrong dimension/ID/version/settings/profile, exact versus pre-rounded min, min/max/corner precedence, old SI float kind, malformed/zero/negativezero pair, every count/processing presence, one-bit measure/bounds/radius mismatch, scaled old stats or chunk-only relabel. Known values InvalidRecord; unknown identity/version UnsupportedSchema. |
| Scene | Duplicate/aliasing/prefix effective override keys, optional omission/path/array-index/flags/kind, ordered Entry projection/parent/root-pose coverage, multi/empty roots, 33 nesting/65537 subtree/256+1 hierarchy/cycle/entity/occurrence/bytes, source UUID collisions, every relative/absolute anchor/external-slot presence, root-only shorthand ambiguity, stale queued intent and sequential recomputation. |
| Mesh/material/texture/clip | Wrong stream rank/duplicate UV/mismatched count/joint/weight/stride/topology/group/bounds; profile binding/capability/attribute/alpha mask/depth/output mismatch; mip size/order/data/color/atlas-border/overlap; track composite order/channel/key type/tangent presence/time/domain/events/root-motion/bind predicates. Spot inner/outer equality is a positive hard cutoff; nearest inner>outer, zero and pi32/2 endpoints reject. Native/restore quaternion-bit preservation is distinct from normalized gameplay assignment. |
| Checkpoint | Header.NextTick alone; pub/control/segment cursor alone; scale/segment time/sequence; current/initial/control frame binding/revision; slots/free/retired/live fields/rows/capacity/layout; paged history lengths/ordinal gaps/full-page rules/metadata bits/origin recipes/branch intervals/kind-live joins/reuse; scene-activation/history bijection, original request/result versus transformed ID and recursive namespace; control-branch/directory rewrite; participant/asset/occurrence/source mappings; mode/intent; branch/fork source/target transforms; canonical/checkpoint/file-generation integrity; counts/bytes/coexistence/pending/stale target/post-swap retirement. |
| Continuation | Standard/default intent; missing or source-incompatible record/profile/catalogue/lineage/branch/input policy; tick holes/extent/exhaustion; omitted controls/versions/randomness; each command discriminator/optional/target/writer/token/path case; preassignment stream mismatch, before-stage9 hash mismatch, unrecorded live edit/control and explicit branch exit. Exercise every legal nonempty selected prefix/end, source-too-short and invalid endpoint; full source ReplayDigest remains fixed when the selected prefix changes. Change exact OS identity/runtime/compiler manifest bytes while retaining OS family and reject against the pinned source execution profile. Correctly rehash record integrity while retaining the independently pinned violated expectation. |
| Runtime binding/cancellation | Stale/foreign/default Entity, declared versus opaque Guid mapping, revoked/thread/phase binding, poisoned cache, Restore throw/cancel/short consumption, post-Restore mutation, output scratch cancellation and source disposal. No fake successful receipt or early credit release. |
| Correction: publication dependency | Execute the Assets-owned common kernel from Tools and a no-Tools player; absent originals/offline binaries is positive under exact source pins. Rehashed wrong graph/context/payload/metadata/output/package/recipe joins still reject. Source/tool fact representation is not producer execution or runtime executable registration. |
| Correction: node/graph credits | Exact [M03 4.5](11-assets-import-streaming-and-packages.md#m03-45-native-node-graph-request-and-operation-credits) acquisition/close orders, constructor/Retain partial failures, unadmitted-node disposal, shared nodes, request/operation independence, cancellation/observer cancellation and quiescent/stuck reclamation. Lifecycle rows have no fabricated EBIN bytes or codec execution. |

Known-data negatives must reach the target owner/rule, not merely fail an old hash or structural count. Record each target invariant and exact expected first diagnostic; multi-fault precedence vectors are separate.

All new Content/C0 schemas are closed: unknown required tags/kinds are UnsupportedSchema (`unsupported-field`/the Core kind category); unknown optional tags are InvalidRecord under field-contract rule 1. A header Guid.Empty is contextual InvalidRecord (`invalid-schema`); a nonempty unregistered tuple is UnsupportedSchema. Do not weaken or replace M02's expressly preserved optional-inert-field behavior inside Foundation records. A typed immutable model whose constructor makes an invalid state unreachable is recorded as such, with its constructor/reader-negative proof and legal mismatched-context writer test; never use reflection/unsafe corruption to manufacture a writer input.

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

This inventory is of scanner clauses, not a claim that every category is independently reachable through every public entry point. The ledger marks a shadowed/unreachable clause with its actual earlier guard and proof; for example a malformed scalar width can be diagnosed by field-entry scalar-width before ScanPayload's scalar-empty branch. Never call that a separately executed scalar-empty test. Preserve the structural diagnostic's actual populated SchemaId/Tag/offset/claim/limit; do not manufacture ancestor/rule/path details that Core did not report.

For contextual semantic codecs, a known semantic rule failure has status InvalidRecord, consumed/written0, category `rule/<algorithm-number>` in canonical decimal (1..51), and exact SchemaId, field Tag (0 for record cross-invariants), RuleId/version/use index and physical tag/array-index path. Validation visits fields in ascending tag order, each field's primary then written extra-use order, elements in declared order, then record RR order after required bindings. Unsupported schema/rule/version/profile/identity registration uses category `unsupported-schema`/`unsupported-rule`/`unsupported-version`/`unsupported-profile`/`unsupported-identity`, respectively, and UnsupportedSchema0; context misuse throws before reading. NeedMoreData has no semantic value/diagnostic; DestinationTooSmall follows complete input validation and gives `destination-too-small`, written0/unchanged destination. The existing context-free IBinarySchemaCodec status interface is not falsely said to expose diagnostics it lacks; [30](30-public-api-review-surface.md#m03-asset-identities-and-contextual-codecs) declares the contextual interface needed by these owner consumers.

For semantic negatives, Core.Validate/Read under Canonical is Complete with exact full record-prefix consumption and one callback; Core.ReadHeader is Complete64. Owner.Read rejects with the above zero consumption and unusable output. Structural negatives never invoke owner callbacks; their first failure remains the Core category/claim/limit. I/O, actual allocation failures, visitor/codec exceptions and cancellation retain their owner exception/task contracts, not converted successful status objects.

#### M03 10.3 Exact rehash dependency graph

Mutate only the declared semantic target plus mechanically derived bytes/hashes:

1. Rebuild each enclosing canonical EBIN header/table/count/offset/zero padding, without fixing the targeted semantic equality.
2. For asset mutations recompute input payload hash, changed graph/context/key operands and affected dependency version facts; do not change the trusted C0/identity selection to authorize bad data.
3. Recompute changed envelope hash, every referencing envelope dependency/hash/version fact, EIDX decoded hashes/slices, index hash, package hash, output/package-key/publication hashes. Process parent dependencies topologically.
4. For checkpoint mutations recompute projection from the mutated actual World/header subset, CanonicalHash, CheckpointDigest, FileContentDigest (including exact TransactionGeneration) and file bytes. Header.NextTick alone is deliberately not copied into Clock. For branch/fork mutations derive all declared target-reference repairs but leave the named failed source/interval/presence join broken; pin the unmutated external source context. Continuation mutations rehash command-stream/record bytes but never repair the deliberately wrong independently expected execution.
5. A fixture intended to test a digest failure omits only that repair. A semantic fixture repairs all unrelated integrity layers.

For example, a shape-pair bit mutation must have a valid new envelope/package/index/publication/key chain but unchanged selected formula/C0 and deliberately wrong recomputation. A frame-WorldGuid mutation must have valid new file/canonical/checkpoint hashes but remain different from Header.WorldGuid. Otherwise neither tests the promised semantic invariant.

#### M03 10.4 Core exact-size corpus

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

For compact valid records, enumerate every proper prefix with independently derived expected status/consumption/diagnostic. For large vectors, ranges are explicit with base hash, every selected index and an ordered aggregate; no sampled prefix is reported as exhaustive. Append trailing bytes and verify Core's one-prefix consumption versus enclosing exact-consumption refusal. Malformed already-provable prefixes need not return NeedMoreData.

Every successful writer compares exact full canonical bytes to both approved oracles, including nested records/empty arrays/padding. Every failed writer checks invalid-input-before-too-small, written0 and byte-for-byte unchanged nonempty 0xA5 destination; invalid models use legal raw/mutable or finite-invalid construction paths, not unauthorized reflection/unsafe fabrication. Every reader failure checks consumed0/unusable value/no publication.

#### M03 10.5 Actual consumer/threshold macros

EN095: source scene contains one camera then d root renderables, d=131072/131073; 2D sprites and 3D one-group meshes separately. IDs use exact fixture names, root order is the source sequence, entity maps RFC-sort independently. Place objects at admitted z1 with layers selected. Lights l=256/257 separately, plus multiple-limit precedence. Bone count2048/2049 uses eight256-joint bind-pose instances plus a one-joint instance. Run real Renderer.Extract, not a count helper.

PH09: explicit selectors `M03Primitive2D`, `M03Primitive3D` and `PH09PrimitiveBoundary.M03` cross all61 exponents, API/geometry/native binding boundaries and both prescribed precision witnesses. `PH09PrimitiveBoundary.M06` adds actual full-profile/owner/handle/storage/mass boundary when that consumer exists.

EN028/029: exact logical sizes produce row36/cap256 and row32768/cap1 versus32769 refusal; use concrete fixed/inline schemas, not padding estimates. EN026 strict fixtures cover live/free/retired generation uintMax/no-wrap issuance. Near-terminal generation/owner storage uses an isolated declared test seam; it is not a reachable billion-creation lifetime under the33554432 cap. Exercise exact configured thresholds/+1 and admitted owner maxima where reachable; failed structural issuance preserves root/assignment cursor/history and permits retirement. It does not refund an already admitted owner-request cursor or restore capture eligibility after a canonical write grant.

M03EntityIdentityBudget follows current chapter28 exactly: uninterrupted 10000 initial plus10000*(120+3000)=31210000 assignments, with no history reset or World replacement; separate live/retired scene lane totals33297153 assignments including its top-level instance and leaves257279. Measure all five processes/early-middle-late/full windows, actual index/page/name-format allocation and whole preflight cost, complete checkpoint sizes, shared/coexistence/candidate/remap/quarantine peaks. These are planned observations, not inferred memory/latency passes.

Scheduler comparison endpoint uses the same4096 uniquely ordered systems across four full-overlap resource groups: all4096 write groups1/2, systems0..90 write group3, system0 writes group4/system1 reads it. Charge16773120+4095+1=16777216; adding system2 Read to group4 gives16777217. Keep fresh PartitionCount1 resources and exact metadata/artifacts. The full-overlap hazard ordering can be represented by sufficient adjacent chains without retaining millions of redundant edges, but the charge remains every pair. Real preflight and actual graph/registration reservations must reach the target; failure at another budget is not a comparison-ceiling pass.

Run the two separate public NativeAOT consumers through native tooling then headless World/store/scene/Step/Extract/save/prepare/replace/retirement, on both supported x64 targets when implemented. Public construction/closed registration, generated roots, actual source-adapter/device exclusion and executed counts are distinct from unit fixture agreement.

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

Each entry occupies 128 bytes and sorts by canonical asset GUID bytes: asset GUID at 0, decoded-root schema GUID at 16, decoded-root schema major/minor unsigned 32-bit values at 32/36, codec byte at 40 (`None = 0`, `Brotli = 1`), zero entry flags byte at 41, six reserved zeros at 42, payload offset/compressed/decoded lengths as unsigned 64-bit values at 48/56/64, dependency-first/count unsigned 32-bit values at 72/76, decoded-root SHA-256 at 80, profile-table index unsigned 32-bit value at 112, and twelve reserved zeros at 116. For M03 native assets, the decoded root is `native/envelope` 2.0; its nested payload has an independent semantic schema identity/version. None requires equal compressed/decoded lengths. Profile index is strictly below profile count; nonempty packages therefore require at least one profile.

Each dependency occupies 64 bytes: asset GUID at 0, required content digest at 16, required decoded-root schema-major unsigned 32-bit value at 48, unsigned 32-bit flags zero at 52, and eight reserved zeros at 56. Each entry's dependency slice is sorted by GUID/digest/root major, with no duplicates. The canonical table concatenates those slices in entry order; a dependency may recur in another entry's slice. For M03, this major names the target `native/envelope` root (2); the target's nested semantic payload schema GUID/major is separately bound by the source envelope's `native/dependency` record. External dependencies resolve by exact ID/digest/root schema through the store, never by diagnostic names.

Profile-table version 2 has 128-byte records: coordinate/geometry/spatial unsigned 32-bit IDs at 0/4/8, signed Int32 `MetersPerUnitExponent` at 12, chunk exponent byte at 16, three reserved zeros at 17, settings SHA-256 at 20, schema-registry SHA-256 at 52, numerical-kernel SHA-256 at 84, and twelve reserved zeros at 116. Built-in IDs are coordinate `LeftHandedYUpForwardZ1 = 1`, geometry `EngineGeometryBinary32Rows2 = 2` (1 remains reserved), spatial `FloatChunk32V2 = 2` (1 remains reserved for `FloatChunk32V1`). The container remains EPKG2/EIDX1; its explicit profile-table discriminator changes, not the byte extent of entries or profiles. Version 1's binary32 factor at 12 is legacy migration input, never an Int32 exponent under version 2. Records sort lexicographically by complete bytes and cannot duplicate. IDs/digests resolve through the locked manifest; unknown profiles reject rather than adopting matching storage layouts. The table deduplicates semantic manifests, not application GUID aliases. After decoding an entry, its major-2 EBIN `foundation/profile` including `ProfileId` must agree with the indexed manifest and registered identity before activation.

Canonical index layout is header, entry table, dependency table, profile table, then diagnostic strings, aligned to 16 with zero padding and exact total length. A diagnostic name record is asset GUID (16 bytes), UTF-8 byte count (unsigned 32-bit), four zero reserved bytes, then bytes and zero alignment padding. Records sort by asset GUID, reference an existing entry at most once, and collectively occupy exactly the declared string-byte extent. Names are optional and never runtime paths. Empty tables have zero extent and the current aligned cursor as their offset.

Payload ranges are positive, nonoverlapping, in bounds, and disjoint from header/index. A legal zero-length schema may have an empty payload. The complete index and decoded-envelope digests verify before semantic asset publication or World activation. Reserved bytes/unknown mandatory flags/duplicate IDs/dependency cycles/incompatible profiles reject. Bounded package/index/decompression staging may be allocated after its claims pass the selected budget; invalid semantic cross-bindings never publish a partial asset or allocate live World state.

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
| BN04 | EPKG2 sorted binary tables, reserved bytes, range overlap/overflow, counts, digests, dependencies, and profile identity validate before semantic publication/activation; every staging allocation is independently bounded |
| BN05 | Compression bombs, truncation, trailing invalid data, cancellation, invalid UTF-8, duplicate fields, and malicious external XML resolution fail observably |
| BN06 | Large windowed reads/mappings retain storage until actual completion and respect aggregate package/decode budgets |
| BN07 | Text/EPKG1/schema migrations create validated new versions/lineages without silently relabeling strict replay |
| BN08 | Representative content measures load latency, allocations, cooked/compressed bytes, and edit/cook round trips against the text baseline |
| BN09 | Independent UUID recipe fixtures cover exact RFC namespace byte order, case/length/domain separation, all built-in keys, version 8/variant bits and the frozen defaults. Invalid names/colliding assignments/refused unknown recipes fail before publication. Encode signed exponent endpoints/tag 10/major 2 and profile-table version 2; inject old factor/tag/discriminator/identity bytes | SHA-256 UUIDv8 matches locked golden bytes across implementations/cultures; evolving a recipe never silently regenerates assigned IDs. UUIDs are not full content digests/authenticators. Legacy UUIDv5/profile manifests require explicit bounded registry/content migration and new lineage; no reused tag 4, inferred exponent, mixed profile interpretation or relabeled StrictLocal state |
| BN10 | Degree/turn/radian scalar and Euler source envelopes survive JSON/XML edit/save/cook, signed zero and untouched-display/other-property edits. Reject duplicate/stale bindings, wrong unit/arity/version and changed canonical digest; migrate old source interpretation only explicitly. Runtime bytes contain existing canonical fields, never raw Angle/editor envelopes |
