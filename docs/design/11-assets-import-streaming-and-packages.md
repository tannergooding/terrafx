# Asset import, streaming, and inert packages

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

## 1. Identities and build graph

`AssetId` is stable `Guid`; `AssetVersion` is content SHA256 + schema/import-recipe version. File path is authoring location, not identity. Import manifest contains source logical path/digest, importer `Guid`/version, typed settings, dependency `AssetIds` + versions, outputs per platform/profile, coordinate profile `LeftHandedYUpForwardZ1`, license metadata and provenance. Source rename preserves globally unique identifier (GUID) via sidecar; duplicate/missing GUID is error.

Native scene/prefab authoring assigns entity, placement and external-reference IDs once and persists them in the native document or an explicit importer identity map. Source formats without stable node identity require a persisted mapping; ambiguous reconciliation rejects or records a deliberate identity change, never silently deriving identity from path, row index, timestamp, geometry or enumeration order. Cooking consumes these IDs and pins the expansion recipe and exact dependency versions in its key. Asset/version/dependency digests validate and identify the cooked inputs but do not become runtime occurrence-UUID namespace operands. A change to IDs, placement targets, expansion semantics or overrides changes the recipe/content result; runtime World/Entity IDs never enter inert native cooking.

Import manifest/cache/cook identity also records `EngineGeometryBinary32Rows2`. Public semantic vectors/quaternions/matrices use chapter [06](06-owned-geometry-and-numerics-interchange.md)'s `TerraFX.Numerics` types; wire components/rows and graphics processing unit (GPU) formats remain explicitly encoded, not raw Common Language Runtime (CLR)/base class library (BCL) layout. Old same-left-handed (LH) component data needs a registered schema/profile migration, not silent cache relabeling. A nominal type migration performs no H * M * H reflection; the following glTF source conversion still occurs exactly once.

Collision cache/recipe identity additionally includes [10's exact dimension-specific version-1 validator/statistics pair](10-binary-data-and-authoring-formats.md#m03-collision-validation-identity-inventory), selected spatial profile/settings and complete foundation binding. Statistics retain canonical `ShapeMeasure` significand/exponent pairs under [23](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics), not materialized binary32 area/volume. A settings or formula change requires explicit registered migration/re-cook, new canonical statistics and fresh recipe/content digests and asset revision. Convert copied geometric components through public `SpatialMath.ConvertQuantity`; never rescale old rounded statistics or accept changed settings as implicit revalidation/rebinding. Ordinary loading verifies the bindings and recomputes canonical statistics rather than treating a stored result as an admission certificate.

The import dependency directed acyclic graph (DAG) rejects cycles except explicitly non-owning scene-entity references handled in [Chapter 16](16-scenes-transforms-and-cameras.md). Topological tasks write new temporary output, validate complete bytes/digest/schema, then publish the verified immutable version directory's manifest under the scoped transfer protocol below. A `Failed` import leaves the old version intentionally active in the editor with `ImportFailed`; it never represents a successful new version. Failure of a first required version blocks scene admission. The cache key includes source/dependency/tool/settings/platform digests; timestamps alone do not invalidate or certify content.

Native engine schemas are authoritative under [09](09-native-assets-and-source-adapters.md). Required adapters include PNG8/JPEG, the supported glTF 2.0/GLB subset, PCM WAV/Ogg Vorbis, TrueType/OpenType, and JSON/XML authoring for engine scene/prefab/material/clip/tile records plus HLSL. They cook to [10](10-binary-data-and-authoring-formats.md)'s versioned binary runtime data; no textual/source object model is a runtime requirement. Image decoding reuses WIC or libpng/libjpeg-turbo; font/audio rules remain in [25](25-ui-text-and-editor-tools.md) and [24](24-custom-audio-mixing-and-spatialization.md). Record decoder/importer versions. Unsupported required features fail explicitly; optional inert extensions need an admitted policy and diagnostic.

#### M03 4.3 Exact public asset identities, version bindings and digest scope

##### M03 4.3.1 Complete public member shapes

The public member shapes belong to chapter 30 section 6; declaration notation omits bodies:

The exact public declarations are in [30](30-public-api-review-surface.md#m03-asset-identities-and-contextual-codecs).

AssetId privately stores exactly one Guid; IsValid is derived, not an additional flag. It is a nominal Guid value, not an alias, path, UUID generator, operational handle or authority token; its16-byte RFC wire encoding is independent of CLR layout. Constructor Guid.Empty throws ArgumentException naming value. Default has Value=Guid.Empty/IsValid=false and remains usable for pure comparison/diagnostics but never asset admission. Every nonempty Guid, including UUIDv8, is intrinsically valid; World's explicit-assignment reserved-domain rule does not apply. Equality/hash/format delegate to the stored Guid; ToString() is Guid's default D format, the explicit overload preserves its format/provider contract. Text formatting is not the wire encoding. No implicit/explicit Guid conversions, public Empty/automatic issuance or additional AssetGuid type are introduced. This follows the current Core SpatialProfileId value pattern without changing it.

AssetVersion is a sealed immutable publication-binding value object, not a lease/service or a fabricated Ready result. The full tuple is too large and digest-bearing to make an accidentally defaultable small-handle struct; AssetVersion? uses null for absence. It has no public parameterless constructor, mutable/init members, IDisposable or implied ownership of asset bytes. Its constructor copies each complete32-byte digest before returning and retains no span or writable caller alias. Empty/31/33-byte digests throw ArgumentException naming the corresponding argument. All256-bit patterns are intrinsically representable, including all-zero bytes; only independent verification can establish an actual digest, and no pending receipt may use zero as a fabricated approval hash.

Constructor validation is source-independent: assetId.IsValid/profileId.IsValid, nonempty importRecipeId, positive int importRecipeVersion, and valid nonempty positive-major/nonnegative-minor Content-scope RootSchema/PayloadSchema tuples. Invalid value arguments throw ArgumentException; nonpositive importRecipeVersion throws ArgumentOutOfRangeException. No registry lookup, I/O, encoder or source verification occurs in this constructor. A positive unregistered schema/recipe/profile remains a nominal value but is unsupported at its actual operation boundary, never implicitly registered or defaulted. Unsigned wire versions must fit positive/nonnegative signed int before constructing the tuple, as Core already requires.

Equality compares all nine public properties: AssetId, ContentSha256Digest bytes, complete RootSchema/PayloadSchema tuples, ContentSchemaDigest bytes, ProfileId, NativeKeyDigest bytes and ImportRecipeId/version. Digest comparison is byte-sequence equality, not ImmutableArray backing-array identity, reference equality or formatted hex. Independently allocated equal versions compare equal; null==null is true and nonnull==null is false. Equal values have equal GetHashCode within the process; that hash is not a persisted/canonical digest or a cross-process promise. No sort/latest relation is defined for versions.

The additional AssetId/catalogue/profile/native-key operands are an explicit normative completion of the verified version binding, not a claim that chapter11 already enumerates them. They prevent two different cook/provenance contracts with identical output bytes from being collapsed into one version. ImportRecipeId/version names the invoked common native cook recipe in Context.Recipe, not merely the source decoder's IAssetImporter ID. Decoder/conversion/tool/settings identities remain complete context/key provenance operands; package compression is not that native recipe.

##### M03 4.3.2 Canonical wire and digest projection

No new C0 schema/tag is allocated by the public classes. Their exact projections use the nine-field version records, with explicit mappings rather than CLR member discovery:

| Public property | cook/dependency and state/asset-version tag | cook/published-output tag | Meaning |
|---|---:|---:|---|
| AssetId.Value | 1 | 1 | RFC16-byte stable asset GUID. |
| ContentSha256Digest | 2 | 2 | EnvelopeDigest, exactly32 raw SHA256 bytes. |
| RootSchema | 3 | 3 | Complete Content schema GUID/positive major/nonnegative minor/scope tuple; M03 native/envelope2.0. |
| PayloadSchema | 4 | 4 | Exact active NativeAsset semantic tuple; independent of root2.0, including its own minor. |
| ContentSchemaDigest | 5 | 5 | SHA256 of exact canonical source content/schema-manifest bytes, not M02 RegistryDigest. |
| ProfileId | 6 Profile.ProfileId | 7 Profile.ProfileId | Resolve the explicit registered M02 binding; the wire carries complete foundation/profile, not Guid-only settings inference. |
| NativeKeyDigest | 7 | 6 | Domain-separated SHA256 of the complete native/cook-key from [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records). |
| ImportRecipeId | 8 | 8 | Context.Recipe.Id, checked against verified publication. |
| ImportRecipeVersion | 9 | 9 | Context.Recipe.Version, UInt32LE wire with positive signed-int admission. |

Native asset-version admission verifies the complete retained canonical foundation/profile bytes/binding and envelope/hash/publication joins; the public ProfileId alone does not synthesize optional inert Foundation bytes. Source registrations retain those bytes under [M03 2](10-binary-data-and-authoring-formats.md#m03-2-preserved-foundation-and-reader-boundary). The four-field native/dependency is a deliberately narrower content requirement, not another full AssetVersion encoding: its ContentDigest is the target envelope hash and its SchemaId/Major names the target's semantic payload. Resolve that requirement through the exact trusted full-version publication/source context; never invent missing recipe/key/minor/profile facts from EIDX or a four-field dependency.

| Digest | Exact scope and exclusions |
|---|---|
| AssetVersion.ContentSha256Digest / EnvelopeDigest / EIDX decoded-root SHA256 / native/dependency.ContentDigest | SHA256 of every byte of one complete admitted canonical decoded native/envelope2.0, from its EBIN header through its field table, all nested records and final zero padding. Includes AssetId, complete Profile, dependencies, Payload and ContentSchemaDigest. No domain prefix, omitted/zeroed field, payload-only hash, decoded CLR-value hash or extra trailing bytes. |
| PayloadInputDigest | SHA256 of the complete canonical input semantic Payload record alone. It is a cook-key operand, not AssetVersion.ContentSha256Digest. |
| ContentSchemaDigest | SHA256 of the complete canonical content/schema-manifest record, including declarations/imports/selections/rule/default/profile operands. M02 foundation registry/kernel/settings/Default remain unchanged. |
| NativeKeyDigest | SHA256 of `TerraFX.NativeCookKey.v1`, NUL and complete canonical native/cook-key, as [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records) specifies. Binds effective output ID, graph/context/source/tool/validator/settings/recipe/profile/catalogue/dependency facts and optional metadata digest without containing its own output-envelope digest. |
| PackageDigest / PackageKeyDigest / PublicationDigest | Distinct transport/publication scopes from [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records). Codec/packing/PackageId/diagnostic-name choices affect package identity, not decoded native-envelope content or its native key solely for those choices. |

Hash admitted exact decoded bytes, not a parse/reencode that discards inert fields. Canonical/declared-length validation precedes semantic publication; a trailing decoded record/byte sequence is not admitted even if Core's prefix reader can consume the first record. None versus Brotli, package offsets or physical source location never substitutes compressed/package bytes for content SHA256. Root and payload versions remain separate: a valid envelope2.0 with payload1.0 must not be rejected by a false equal-major predicate.

Changing an envelope operand requires recomputing its content hash. Changing only source/tool/recipe/node metadata can change NativeKeyDigest and therefore the full AssetVersion while a preserve-canonical producer emits the same envelope bytes/content hash. A publication's transport-only metadata does not change that native key. Repacking those bytes under None/Brotli or a different PackageId/name changes package/publication digests, not the full native AssetVersion when its nine operands remain unchanged. Equal source behavior is not equal provenance; equal content SHA256 is not equal full version or authenticity. No hash proves publisher trust, executable admission or Ready state.

##### M03 4.3.3 Load/source/lease/receipt joins

AssetLoadOptions' digest is empty or32 bytes; its constructor copies it to a nondefault immutable array. If RequiredVersion and a digest are both present, compare that digest to RequiredVersion.ContentSha256Digest and throw ArgumentException naming requiredSha256Digest on disagreement. Empty means no additional constraint, not a zero digest. LoadAsync validates id.IsValid (ArgumentException naming id on failure) and, when supplied, RequiredVersion.AssetId==id synchronously before I/O/queue/operation reservation; disagreement is ArgumentException naming options.

RequiredVersion selects its complete exact tuple, not a content-only alias. Without it, select the registration's one explicit current full version for that AssetId; RequiredSha256Digest additionally constrains that selected version's envelope digest. A digest alone does not choose arbitrarily among distinct recipe/key versions with identical content. Missing current/exact source faults with contextual load diagnostics rather than guessing latest, a path or a default version. RegisterSource and actual bounded load verify full publication/EIDX/envelope/root/payload/profile/catalogue/recipe/key/dependency joins before returning an AssetLease. The nominal constructor supplies no verification authority.

AssetLease.Version is the exact admitted binding and may outlive store-selection changes through its independent credits. PreparedScene/GPU residency/audio lease and checkpoint version fields refer to that full version; CPU readiness does not imply GPU/audio readiness. A World still admits one active full version per AssetId, while stores/retained leases may hold multiple. Exact-version pin deduplication is only an ownership optimization: all surviving canonical requirements in one World must agree on the complete nine-operand AssetVersion binding for each AssetId, including when content digests match. A conflict refuses the whole operation until explicit unload/rebind or whole replacement; another World or retained historical snapshot may independently keep a different version. Cooked receipt.AssetId==PublishedVersion.AssetId==the request root and ManifestSha256Digest==PublicationDigest; Rejected has null PublishedVersion, empty manifest digest and nonempty diagnostics. Manifest digest is not the envelope/content hash. Existing I/O/allocation/cancellation/retirement task semantics remain intact.

##### M03 4.3.4 Preserved native request-root/key invariant

The native-graph variant freezes a unique explicit graph root and requires `request.AssetId == graph.RootAssetId == uniqueRootNode.AssetId` before native cooking admission. Mismatch throws ArgumentException naming assetId synchronously; there is no request-only rename, automatic root selection/relabel or fallback. CookAsync retains that frozen binding and obtains independent credits before work. Source-byte variants can only compare after the importer produces its graph; they must bind the same request ID before admitting the native cook/publication phase, not pretend an unavailable graph was synchronously checked at source-request construction.

The root output's native/cook-key tag1 OutputAssetId is that effective ID; nonroot output keys use their own exact graph-node IDs. RequestRootAssetId also enters graph/package-key/publication as already specified. Never put the root's eventual ContentSha256Digest in its own key. Bind all corresponding envelope/EIDX/publication IDs on output before visibility. Consistent explicit graph-root/owned-reference rename requires revalidation and fresh derived keys; request-only rename rejects without publishing or consuming a cook operation.

##### M03 4.3.5 Acceptance and implementation ownership

| Owner / existing acceptance scope | Exact M03 responsibility and current status |
|---|---|
| TerraFX.Assets; AR06/AR07, BN01/02/04/05/06/09, native portions AS01/04/05/06/08 and EN098/103-108 | Implement IDs/versions, copied digest/equality contracts, contextual base codecs, exact source selection, common graph/context/key/package/publication models and verification, and transitive lease/store ownership. AR01/AR02/EN152 retain dependency observations; EN024 includes the no-Tools runtime publication lane. Current source/test directories are empty scaffolds; no implementation/test/acceptance result exists from them. |
| TerraFX.Content.Tools; native portions EN097/102/104 and AS04/05/06, BN01/02/04/06/08 | Implement source-adapter-disabled native orchestration, actual source/producer checks, independent cook credits and exact version/manifest publication using the shared Assets verifier/models. Native rename/key-change/atomic-failure counterparts execute here; original source-sidecar/importer/editor observations remain incomplete until their real M05/M08 consumers. Current Program returns1, not a callable/working cooker. |
| Runtime Scenes/Rendering/Hosting plus permitted Integration bridge; AR06/AR07, EN024/049-052/097-108/121-132 CPU slices | Use the same full version through real standalone load/scene/Extract/save/restore, pinned source/dependency closure and replacement retirement. Entities/Core do not depend on AssetVersion or Assets codecs. Integration supplies Physics-aware asset bindings without changing Physics's Core-only direction. |
| Descriptor/generated-binding and two public NativeAOT consumers; BN01/02/09 and EN024 M03 slices | Require approved independent descriptors/fixtures plus actual compiled ID/version/member/equality/codec roots and executed native-author/runtime consumers on their supported targets. A project file, generated assembly attribute, empty test selection or normative signature is not that evidence. |

M05 retains source adapters/equivalence, source rename/sidecar and importer-specific AS02/03/05-07/BN03/EN097/099-102 observations; M08 retains editor/unit-authoring BN10, current-version/restart/release portions. Actual supported native pure migrations retain BN07 and their implementation owner; this contract does not manufacture EPKG1 readers, source converters or completed migration cases. All parent IDs and M02 evidence remain unchanged.

The full immutable binding permits content-addressed blob deduplication without erasing distinct provenance/recipe versions; transport-only repacking does not create a fake new native version. A Guid alias, payload-only hash, recipe/key-free equality, implicit latest/digest-only selection or Runtime-owned ID/version is not a conforming alternative.

## 2. Concrete cooking

Images decode into straight-alpha RGBA8, preserve authored sRGB/linear semantic flag; reject unsupported ICC/high dynamic range (HDR) profiles rather than approximate unknown transforms. Normal maps linear. JPEG decode differences can produce different cooked digests across platforms; distribute one cooked content set for strict replay, not a false universal deterministic-import promise.

Sprite atlas max 4096 square/default 2048, padding 2 texels with extruded border, rectangle order by descending padded height, descending padded width, then canonical `AssetGuid` bytes; no rotations by default. Pack deterministic skyline at the lowest fitting Y then X, considering pages in creation order. `Add` pages up to 16, overflow error; atlas pages have only mip 0 as specified below. Premultiply in linear space after sRGB decode; encode stored color accordingly, mark material premultiplication. No gamma-space premultiply.

Mip dimensions match native floor-halving: output `W = max(1, floor(inputW / 2))`, and likewise for `H`. An output texel `(x, y)` is the area-box average over [`x * inputW / W`, `(x + 1) * inputW / W`] by [`y * inputH / H`, `(y + 1) * inputH / H`]; weight each source texel by its rectangle-overlap area and divide by the total area. Even dimensions reduce to the ordinary 2 x 2 average; odd dimensions include the entire source rather than inventing a ceil-sized native mip or dropping its final row. A 3-by-3 source reduced to 1-by-1 averages all 9 texels. Color samples decode `sRGB` if flagged, premultiply RGB by alpha, average RGBA in linear space, then encode stored RGB only when the target is sRGB. A straight-alpha target divides averaged premultiplied RGB by alpha when `alpha > 0`; otherwise RGB is zero, the mathematically empty transparent state, not a load-error fallback. Normal maps decode `signedXYZ`, average with the same area weights and renormalize; a near-zero vector with `lengthSquared < 1e-12` uses the declared flat normal `(0, 0, 1)`, with a `NormalMipDegenerate` warning/count in the cook manifest. Mask materials use the averaged alpha and authored cutoff; no alpha-coverage-preserving mip claim is made in 0.1.

Atlas pages use only mip 0 in 0.1 to avoid unspecified cross-sprite bleeding at coarse levels; general textures use this mip chain. Sprite atlas sampler cannot request unavailable mip levels. Default 3D sampler is linear min/mag/mip with Repeat,2D pixel-art Nearest/Clamp, user interface (UI) Linear/Clamp; application may declare supported alternatives in material schema.

glTF subset: triangle lists/strips/fans expanded into indexed triangle lists, POSITION/NORMAL/TANGENT/UV0/UV1/COLOR0, JOINTS0/`WEIGHTS0`, metallic-roughness, nodes/skins/animations/cameras. Sparse accessors expand with checked bounds; external URIs remain under import root, no network/data-URI > 64 MiB. glTF is right-handed (RH)/Y-up; imported engine data is LH/Y-up/ +Z-forward. Reject unsupported decoder/extensions/morphs, > four weights without recorded approximation, unsupported interpolation or ambiguous sheared local matrix.

Apply [Chapter 03](03-shared-conventions-and-clocks.md)'s Z reflection exactly once, before publishing cooked engine assets. Positions/translations/normals/`tangentXYZ` map `(x,y,z) -> (x,y,-z)`; node/track quaternions map `(-x,-y,z,w)` and scale components stay unchanged. Column matrices become `H4 * transpose(source) * H4`, including node transforms and every inverse-bind matrix. The exact distance conversion is the declared source meters-per-unit definition divided by `10^targetMetersPerUnitExponent`, multiplied by the exact dyadic value of the finite positive optional asset scale (default 1); evaluate the complete rational product before one final binary32 rounding. Source glTF uses exactly one meter per unit. Scale positions and every affine translation, including inverse binds, once, not directions/normals or quaternion components; no pre-rounded SI factor or second runtime conversion. Engine camera nodes use local +Z forward instead of glTF camera -Z; converted orientation/translation therefore preserve the camera's actual source view.

For the complete distance conversion scale `s`, imported camera `Near = s * znear`, finite `Far = s * zfar` exactly once; omitted perspective `zfar` remains `Infinite`. Perspective `VerticalFieldOfView = yfov` and optional `AspectRatioOverride = aspectRatio` are dimensionless and unchanged; if source `aspectRatio` is omitted, derive it from the viewport. Orthographic glTF `xmag`/`ymag` are half-extents: `OrthographicHeight = 2 * s * ymag`, `AspectRatioOverride = xmag / ymag`, giving `width = 2 * s * xmag`; finite clipping planes scale by `s` too. The glTF orthographic source requires `znear >= 0` and `zfar > znear`; negative source `znear` rejects before activation even though engine-native orthographic `Near` may be negative. This importer subset also requires positive finite `xmag`/`ymag`; source-permitted negative magnification is `UnsupportedSourceCamera`, not silently flipped. Perspective source `near` stays positive. Validate the source and all derived dimensions/ranges before activation; do not truncate/clip a scaled source camera. Native camera distances already use the frozen target unit profile and are not rescaled by loading.

Expand strip/fan winding first, then swap indices 1/2 of each triangle exactly once: RH source CCW fronts become engine clockwise fronts with numerical cross normals agreeing with the reflected source normal. Flip imported `tangentW` once; reconstructed `B = tangentW * cross(N,T)` equals reflected source bitangent. UVs retain explicit texture-origin handling, but handedness alone does not flip normal-map green/UV V. Generate missing normals/tangents from final canonical geometry/UVs; do not apply the imported-`tangentW` flip again to newly generated engine tangents.

Convert every animation translation/rotation key and cubic tangent with the corresponding linear component map. A `Rotation` derivative/tangent `(x,y,z,w)` maps to `(-x,-y,z,w)` without normalization; only the key/sample quaternion rules in [20](20-animation-and-deformation.md) normalize. Carry the conversion through node hierarchy, bind/current bone matrices, skinning, root-motion extraction, and bounds, not just vertex Z. Skin weights/joint indices/material scalars are unchanged. An authored negative scale keeps its determinant sign after H4 conjugation, so the instance parity defined in [Chapter 21](21-2d-and-3d-rendering.md) still applies once. Applying the reflection/index/`tangentW` conversion twice restores source values and is not a valid second importer pass.

Convert source key times once to native `TimeSpan` ticks with round-to-nearest, ties-to-even and checked extent. Coincident keys produced by conversion reject as `SourceTimeResolutionExceeded`; do not reorder/drop them. Native cubic Hermite tangents are derivatives per second, multiplied by segment duration during evaluation. Quaternion derivative tangents remain raw; only key/sample rotation admission normalizes. Runtime interpolation consumes native tracks, not glTF accessors.

Recipe/version/cache identity includes source basis, this conversion algorithm and target coordinate profile. Prior RH cooked assets cannot be marked LH or reused under their old digest; recook/migrate with validated normals/winding/skins and new version. Engine-native LH scenes/clips are already canonical and must not be reflected again. glTF remains an interoperable authoring/import choice, not a required runtime representation.

Generate missing normals area-weighted per smooth indexed adjacency with explicit angle (default 60 degrees); hard edge split based on smoothing settings. Missing required tangents generated by version-pinned MikkTSpace convention, recorded digest/license. Degenerate triangles rejected by default; optional recorded `StripDegenerate` policy gives exact removed count and recomputed bounds. Central processing unit (CPU) physics shape cook is distinct from visual mesh; static triangle mesh never silently becomes dynamic convex.

`Audio` decodes to chapter [24](24-custom-audio-mixing-and-spatialization.md)'s canonical PCM metadata/blocks. Font asset includes original licensed bytes/fallback chain/raster settings, not baked text strings only. Scene/clip schemas source-generated, unknown required fields/unsupported major versions reject; unknown optional extension properties preserved as inert data within explicit size budget.

### Offline mesh conditioning

Cook order is checked source expansion/index validation, one basis/unit conversion, declared degenerate removal and seam/hard-edge splits, missing-normal generation, version-pinned MikkTSpace tangent generation, then optional triangle-cache/vertex-fetch reordering, followed by final bounds/submesh/stream validation and serialization. Every index is checked against actual vertex count before native tool invocation. Reordering is None by default; an enabled recipe pins tool/algorithm/version/digest, settings and host numerical profile. DirectXMesh is a private offline candidate, not a runtime dependency or replacement for MikkTSpace.

An enabled reorder cannot cross material/submesh boundaries or weld vertices merely because positions coincide. Its vertex remap covers position/normal/tangent/UV0/UV1/color/joint/weight streams together; update all dependent indices and preserve source seams, skin association, winding, tangent parity and declared stable primitive mappings. Record remap/removal counts and algorithm identity in manifest/cache keys. Repeat identical locked cooking gives identical bytes; changing recipe invalidates outputs instead of relabeling old digests. LOD generation, meshlets and dynamic geometry simplification remain outside baseline.

## 3. Runtime tickets and leases

`AssetStoreOptions(absoluteCacheDirectory, registration)` supplies a trusted absolute cache root and an immutable `AssetStoreRegistration`, never an asset-controlled path or a World. The registration explicitly binds the permitted content catalogues, trusted package/source catalogues, and typed payload-codec factories before store creation; it does not discover codecs from a World or CLR reflection. `AssetStoreOptions` also supplies this chapter's frozen CPU/decoded/worker/decode capacities. `AssetStore.CreateAsync(options, cancellationToken)` creates an independently owned store; the host uses the same factory and owns its instance. A standalone caller supplies all registrations before creation. There is no public partially initialized store constructor. Shutdown closes admissions and joins consumers before releasing leased backing data.

`TerraFX.Assets` uses Core's public structural EBIN reader and applies its own registered semantic codecs only after bounded recursive validation. The registration carries executable typed codecs; a content-schema digest or descriptor alone never installs one. `AssetStore` does not depend on Entities, Scenes, Runtime, Physics, or Content.Tools. A codec implemented by an owning higher layer is supplied as a typed registration through this interface, not referenced by the Assets assembly.

Offline `TerraFX.Content.Tools.AssetCooker.CookAsync(AssetCookerAdmissionDomain, AssetCookRequest, CancellationToken)` returns `Task<AssetCookReceipt>`. A typed native-graph request names the output `AssetId`, retained `StagedAssetGraph`, exact source/dependency versions and credits, selected content catalogue/profile, recipe/tool/validator identities and Assets-owned resource scope with its trusted output directory. The graph has a unique explicit root whose publishable ID must equal the request ID; disagreement rejects synchronously before admission. The canonical cook key includes that effective output ID. Source-byte/import requests remain tooling inputs and produce the same typed graph before native validation. Admission takes independent operation credits; caller disposal cannot revoke accepted cooking. A `Cooked` receipt names the atomically published version/manifest, while expected invalid/unsupported content returns `Rejected` with specific diagnostics and no published version. Unexpected I/O/allocation/invariant failures fault the task; cancellation becomes terminal only after owned consumers and subprocesses stop and staging is reclaimed, or faults with retained/quarantined cleanup ownership. Acquisition-helper Tasks instead end at their own actual phase quiescence; they never wait for their awaiting worker's whole cleanup. Runtime assemblies do not reference this tooling entry point.

Cook publication stages immutable content-addressed outputs and a complete manifest privately under the explicit trusted destination. It validates the complete envelopes/package before atomically publishing the version manifest on the supported same-filesystem path; this manifest visibility point, not a sequence of file writes, is the publication boundary. Preserve prior pinned versions, clean only this operation's named staging, and never derive extraction paths from source names. Dependency identity includes supplied dependency content hashes and exact root/payload versions; the output's own digest is excluded from its key to avoid a digest cycle.

`AssetTicket<T>` states `Pending` -> `Ready` or `Failed`/`Canceled`, and includes version/result/reason; `Task` observes terminal state. `AssetStore` owns source/cache and strong leases; ordinary callers hold `AssetLease<T>` disposable pins. Dependency leases are retained transitively until the last version consumer releases them. Unload means no new leases plus retirement, not destruction of data still referenced by GPU/audio/scene.

The ticket is a domain lifecycle/ownership object, not `Task<Result<T>>` or a C# union. `LoadAsync<T>` returns `Task<AssetLease<T>>`: required-file/digest/decode/device faults fault the `Task` with contextual exceptions, cancellation cancels only after its borrowed/owned consumption lease is safe to release. Ticket Status/last diagnostic mirrors that outcome for the editor; it does not convert an unexpected failure into a successful `Task` carrying `Failed`.

Read/verify/decompress/decode worker stages use bounded `Channels(default 128 tasks, four I/O workers, two cook workers)`, `CancellationToken`. Required version activated only at `Begin` boundary, recorded tick. Async completion cannot mutate live entity-component-system (ECS) mid-query. Before activation cancellation disposes staged owners; after activation ticket result/lease exists and cancellation does not revoke other consumers.

Importer inputs use [05](05-owned-buffers-and-internal-collections.md)'s retained buffer/chain leases and bounded windows. A private decoder adapter retains exact extents/access until actual completion, without a prohibited memory-type bridge or implicit flattening of an unbounded sequence. BCL UTF-8/UTF-16 validation remains separate from [25](25-ui-text-and-editor-tools.md)'s shaping/grapheme/bidi rules.

Tool process orchestration uses `Process` lifecycle/streamed output application programming interfaces (APIs) with owned handles, cancellation and exact recorded command/tool identity. Read stdout/stderr concurrently in fixed <= 16 KiB chunks; the aggregate captured raw-byte limit is 8 MiB per process, enforced before decoding/retaining bytes. Incremental decoding uses the locked tool's declared encoding and bounded scratch; never use `ReadLineAsync` or unbounded capture that can allocate an arbitrary unterminated line before checking the limit. Overflow reports `ToolOutputLimitExceeded` and stops/fails only that invocation; exit/pipe cleanup must be confirmed before releasing ownership or activating output. Waiting cancellation is not process termination and no cooker/compiler is abandoned fire-and-forget. A stuck native child retains ownership and surfaces chapter [02](02-dotnet-api-memory-and-ownership.md)'s bounded shutdown failure, not fake successful cleanup.

Streaming priority explicit integer plus request sequence; memory budget CPU assets 512 MiB default, decoded asset 256 MiB max, aggregate concurrent decode 256 MiB. Reserve decoded budget before decode based on validated header; malicious claimed compression ratio/count overflow rejects. Least recently used (LRU) evicts only unpinned `Ready` versions; if no evictable budget fail `AssetBudgetExceeded`, no borrowed pooled storage returned early.

GPU residency/voice streaming allocations count separately; "`Ready` CPU" is not "`Ready` GPU". `Rendering` requires a fulfilled GPU residency ticket or explicit editor error-visualization policy. Critical audio streaming errors surface typed playback state; no quiet fake `Ready`.

#### M03 4.4 Runtime-owned provenance model and common verification kernel

This replaces the original Tools-owned verifier seam; it defines descriptor/recipe Owner operands, not the assembly DAG or any M02 identity. The `cook/` key prefix describes inert provenance, not a permission to reference the Tools assembly.

##### M03 4.4.1 Exact owner and rule projection

| Keys / surface | Revised owner, role and permitted implementation |
|---|---|
| cook/graph, cook/node, cook/dependency, cook/source, cook/source-convention, cook/implementation, cook/validator-use, cook/native-context | Assets=1, Cook=10. Public immutable data models and canonical Read/Validate/Count/Write/hash projections in Assets; no source decoder/tool process or World reference. |
| native/output-policy, native/cook-key, cook/native-key-record, cook/published-output, cook/package-entry-policy, cook/package-key, cook/package-name, cook/output-metadata, cook/publication | Assets=1, Cook=10, with the same field tags/kinds/cardinality/order/presence in [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records). Package policies describe a producer; reading them never invokes that producer. |
| cook/native-settings, cook/empty-settings, cook/brotli-settings | Assets=1, TypedSettings=13. The exact zero/native expansion operands, empty settings and quality/window ranges remain unchanged. |
| native/envelope, native/dependency, native/asset-reference, native/metadata, native/metadata-entry | Assets=1, Auxiliary=11, unchanged. |
| Rule17 / Rule18 / Rule19 / Rule49 | Assets=1. IDs, version1, algorithms17/18/19/49, parameters-none, Record scope4 and contexts11/11/11/2 respectively remain. Their recipe tag7 Owner and the matching content/rule tag7 are both1. No extra rule/default/use is inserted. |
| Core metadata/parameter/literal/approval/structural primitives | Core=0, unchanged. Core owns generic descriptor/recipe evaluation scaffolding and M02 validation, never an Assets/Tools publication model. |
| Native family semantics | Scenes/Rendering/Animation/Physics owners remain their [M03 3](10-binary-data-and-authoring-formats.md#m03-3-complete-descriptor-notation-and-expansion)-8 values. Assets invokes only explicitly supplied typed codec/binding registrations; Physics's Assets adapter stays in Integration. |
| AssetCookRequest, AssetCookReceipt, AssetCooker, NativeCookSpecification, NativePackageSpecification, importer adapters and subprocess/filesystem orchestration | Content.Tools. Specifications refer to the Assets-owned inert implementation/settings/policy models. Tools validates original source bytes and actual invoked producer artifacts, calls the common Assets kernels/writers, and stages/atomically publishes. No reverse edge or Tools-owned registration is required by a player. |

The Assets implementation has one common pure kernel for the graph/context/key/package/publication predicates, not separate offline and runtime algorithms. Its inputs are the exact [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records) models/canonical bytes, bounded registered catalogue/profile bindings, exact envelope/dependency records and an explicit validation phase. Tools uses it after forming its checked input graph; AssetStore uses it after bounded decode and reconstructed-graph preparation. [M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff) supplies the missing public operation-native-input/VerifyPublicationAsync route over exact sealed resources and explicit frozen registration. The kernel has no I/O; lower-owned orchestration reads/decodes bounded resources and invokes it outside every gate. No arbitrary verifier callback, Tools payload inspection or package-selected executable is added.

Runtime reconstruction produces the immutable inert `cook/graph` data model, not a StagedAssetGraph owner containing original-source leases. StagedAssetGraph is the author/cook-side credit carrier; its checked data projection feeds the same kernel. This distinction keeps recorded source fingerprints verifiable as representation without inventing absent original-byte credits at runtime.

Rule17 validates the pure rooted graph and direct owning edges, supplied dependency set, profile/catalogue/convention correspondence and the *representation* of Sources. It does not open/hash original source files. Rule18 validates complete contexts, key operands and their pure hash projections. Rule19 validates output/package/metadata/set/policy/dependency joins, reconstructs the graph and verifies the hash DAG. Rule49 validates nonempty producer Id, positive signed version,32-byte artifact digest, active declared TypedSettings selection and exact applicable native/package/compressor settings. An implementation Id/artifact digest is provenance data, not an executable lookup key.

Rule49's exact record dispatch is: cook/implementation checks tags1-4's intrinsic tuple/settings selection; cook/native-settings checks its four zero policies and fixed expansion ID/version; cook/brotli-settings checks its quality/window ranges; cook/validator-use checks Scope/RuleId/version/RecipeDigest against the selected Content or preserved Foundation recipe and its complete implementation/settings record. Empty settings has no RR. Native-context/package-policy/publication rules18/19 enforce which settings/recipe kind is applicable to each recorded use. These are the existing listed RR sites, not a new implicit validator. Context2 is the already validated catalogue/recipe selection; root source pin and full version bindings are supplied to rules17/18/19 through context11.

The native and package recipe/conversion/expansion identities and supported versions still select the exact known algorithms stated in [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records). Unknown required algorithm/schema/rule/profile versions reject as UnsupportedSchema; known mismatched tuple/settings joins are InvalidRecord. Arbitrary nonempty offline tool/validator implementation labels may be represented in an explicitly approved pinned publication without those tools being installed. Their RuleId/version/RecipeDigest/use ownership and complete recorded settings still must match the selected approved semantic closure. Thus a missing required *runtime codec/kernel registration* refuses, while absence of an offline producer binary is not a load failure.

##### M03 4.4.2 Source pin, semantic verification and producer execution are different evidence

AssetSourceRegistration pins full AssetVersion plus exact package/publication SHA256 and trusted absolute locations. It is the explicit caller-approved receipt for those exact inert provenance bytes; it is not executable publisher authority. No additional serialized approval hash, self-hash or catalogue row is introduced. The source selection freezes before load; actual package/publication bytes must match those external pins and all independent semantic joins must pass. Pinning invalid bytes cannot waive a known rule failure. Bare package bytes without the required full-version/publication context still refuse.

Runtime verification performs, under the selected budgets:

1. Validate complete canonical publication bytes and exact source pin, registered content/profile/recipe selections, package bytes/hash/extent and EPKG2/EIDX structures. Retain operation credits before asynchronous consumption.
2. Decode each declared output envelope with exact-prefix refusal at its enclosing boundary; verify content hashes, complete profile/catalogue/semantic tuple and registered owner validators, plus every exact supplied dependency closure.
3. Check Keys/Outputs/Policies/Metadata sets and all IDs/versions; reconstruct the pure Graph from those admitted outputs/dependencies and recorded Context fields. Sources stays recorded fingerprint data. Verify rooted reachability and recomputed GraphDigest.
4. Verify native ContextDigest, each PayloadInputDigest/MetadataDigest/NativeKeyDigest and full published-version joins. Verify PackageKeyDigest, index dependency/root/profile joins, PackageId/digest/byte count and PublicationDigest. None/Brotli consumption is verified through the runtime decoder, not the compressor tool.
5. Publish a CPU lease only after complete success. No World/host/device/executable admission, original-source fetch, tool installation, importer, code generation or producer execution occurs. All bytes retained by temporary verification are budgeted and retire after actual consumers finish.

Tools additionally recomputes original retained source lengths/SHA256, checks exact producer/module/tool binaries and canonical settings against its trusted build specification, proves the actual invocation closure, runs the selected producers and validates their result before atomic manifest visibility. These cook-only checks are explicit orchestration preconditions/evidence, not extra hidden C0 rule algorithms. A runtime cannot prove which historic executable ran from a fingerprint; a cook receipt/test/build manifest supplies that separate evidence. Changing a SourceId/digest/byte count consistently and rebuilding every key creates different provenance under a separately approved source pin, not intrinsically malformed content. A disagreement with an independently supplied source/producer expectation is a different negative.

For fixture positives, recorded offline artifact fingerprints may be inert literal operands and originals/tools intentionally absent; label them provenance-model vectors, not producer-execution evidence. For rehashed semantic negatives, pin the candidate publication/package hashes in the fixture's source context to reach the target join, while retaining the independent catalogue/profile/version or derived equality being violated. Hash pinning then succeeds but rule17/18/19 still rejects. [M03 10.6](34-engine-acceptance-cases.md#m03-106-independent-correction-coverage-and-exact-acceptance-effects) specifies the exact invalid joins and no-Tools public-player observation.

No namespace/assembly friendship is added. Compile-time closure and actual public NativeAOT execution must prove Assets transitively uses only Core, and that no-Tools player source/generated/reference/runtime-deployment closure contains neither Content.Tools nor Editor/generators-as-runtime dependencies. An offline producer hash inside data is not a binary dependency.

#### M03 4.5 Native node, graph, request and operation credits

This is the normative replacement for the original node-credit omission. Copied metadata is immutable; storage owners are explicit disposable reference objects. No new C0 schema/tag/hash field records a credit, disposal state or operational owner.

##### M03 4.5.1 Required Core retention boundary

Add these ordinary safe-facing public members in TerraFX.Buffers, subject to implementation and public-consumer qualification; [M03 4.6.4](05-owned-buffers-and-internal-collections.md#m03-464-required-core-bridge-and-public-operational-closure) adds the quota-aware boundary required for engine-retained use:

```csharp
// Additional members on the existing sealed types; not new types or constructors.
public BufferReadLease<T> Retain();
public BufferChain<T> Retain();
```

The first is a member of BufferReadLease<T>, the second of BufferChain<T>, retaining their existing `where T : unmanaged` constraints. Each call on an open receiver atomically takes independent read credits against Dispose, covering the exact same allocation/range or ordered canonical nonempty chain segments. Success returns a distinct disposable wrapper, never `this`; no payload copy/flattening or writable alias. The parent may then close, and an already closed allocation-owner is permissible when that active receiver still holds a valid credit. A closed receiver throws ObjectDisposedException before acquisition. Empty chain retention returns a distinct open empty chain.

For a chain, all segment clones and copied descriptor extents succeed before return; a failure releases every acquired clone and propagates the original exception, without changing the input chain. Checked extent/count/reservation/accounting preflight occurs before publishing a new credit and cannot wrap. [M03 4.6](04-capacity-and-workload-profiles.md#m03-46-finite-operational-limits-admission-domains-and-charging) fixes the missing numeric bounds and quota scope; [M03 4.7](11-assets-import-streaming-and-packages.md#m03-47-credit-release-versus-staging-cleanup-failure) establishes nonthrowing valid credit release. Total length/window/overlap/generation/accounting semantics remain chapter05's. Closing the receiver first forbids retention; retention winning first creates a valid independent child. Core implements retention with its existing gated lifetime machinery and the new quota-aware registration storage, no public pointer and no new product friendship. Internal Clone is not a substitute for the required public retention boundary. They are additive M03 prerequisites, not revisions or execution claims for accepted M02 bytes/fixtures.

##### M03 4.5.2 Exact node surface and failure behavior

```csharp
public sealed class NativeAssetNode : IDisposable
{
    public NativeAssetNode(
        AssetId assetId,
        BufferReadLease<byte> canonicalPayload,
        ImmutableArray<AssetId> dependencyIds,
        NativeMetadata? metadata);

    public AssetId AssetId { get; }
    public ContentSchemaReference PayloadSchema { get; }
    public long PayloadByteCount { get; }
    public ImmutableArray<byte> PayloadSha256Digest { get; }
    public ImmutableArray<AssetId> DependencyIds { get; }
    public NativeMetadata? Metadata { get; }
    public BufferReadLease<byte> AcquirePayloadReadLease();
    public void Dispose();
}
```

All are in Assets. Validate null/invalid AssetId/nondefault dependency-array, distinct RFC-ordered valid IDs and copied metadata representation; take `canonicalPayload.Retain()` and validate its actual retained extent and complete canonical structural EBIN record under [M03 2](10-binary-data-and-authoring-formats.md#m03-2-preserved-foundation-and-reader-boundary)'s native caps. Parse the signed header projection into PayloadSchema(Content); verify exact record consumption, compute PayloadByteCount and PayloadSha256Digest from those actual bytes, and freeze all metadata before returning. Registration/profile/semantic graph checks remain the later owning boundary. Non-Complete/extra bytes/budget-invalid structural input throws ArgumentException naming canonicalPayload; a disposed input throws ObjectDisposedException; actual allocation/invariant exceptions propagate.

If anything after successful retention fails, release that retained credit and any temporary credits/reservations in reverse acquisition order before propagating the failure. Never dispose the caller's lease, mutate its bytes or leak a half-returned node. DependencyIds remains exactly the declared edge list; constructor does not guess/add/remove semantic edges or normalize geometry. Input native-buffer read protections preserve those bytes while any retained credit exists.

AcquirePayloadReadLease takes a fresh independent full-range read credit atomically against node close; disposing its result cannot dispose the node's private credit. Node.Dispose is synchronous/idempotent: close new acquisitions, then release only its own retained payload credit outside the node gate. Metadata getters above remain cheap frozen facts after close; byte acquisition and graph admission through that wrapper then throw ObjectDisposedException. Core active-use/child credits delay physical free as already specified. No node finalizer traverses managed owners, waits or substitutes for deterministic Dispose; an unadmitted/abandoned node must be disposed by its creator, ordinarily using/finally.

##### M03 4.5.3 Graph/request acquisition and disposal linearization

StagedAssetGraph is sealed IDisposable, with the [chapter 30 section 6 input constructor](30-public-api-review-surface.md#m03-public-construction-and-binding-contracts) and public `StagedAssetGraph Retain()` member. A retained graph is another independent owner over identical frozen graph facts/bytes. Its public RootAssetId/ProfileId/Catalogue/Convention and immutable NodeIds/source/dependency metadata are copied observations, never shared disposable node/lease owners. Payload access is through `BufferReadLease<byte> AcquireNodePayloadReadLease(AssetId assetId)`; source access through `BufferChain<byte> AcquireSourceBytes(Guid sourceId)`; supplied dependency access through `BufferReadLease<byte> AcquireDependencyEnvelopeReadLease(AssetId assetId)` and `AcquireDependencyPublicationReadLease(AssetId assetId)`. Each returns a new independent credit and requires an open graph and valid present input identity; invalid IDs throw ArgumentException, absent IDs throw KeyNotFoundException. The graph's exact supplied AssetVersion and source/node metadata are the immutable [M03 4.2](10-binary-data-and-authoring-formats.md#m03-42-transportprovenance-records) facts, not reconstructed latest values. No public property hands out a graph-owned disposable child.

Input source/dependency descriptors borrow caller-owned leases/chains and contain immutable metadata; those input descriptors acquire no ownership themselves. Graph construction freezes/validates its scalar/array/ID/root inputs, then captures each node's copied metadata plus an independent payload credit under that node's admission gate, in canonical RFC node order. It independently retains source chains and dependency envelope/publication credits through the same safe Core Retain boundary. A node must be open at that node-snapshot acquisition point. A concurrent Dispose winning first causes ObjectDisposedException; capture winning first succeeds and later disposal cannot invalidate it. There is no unchecked `IsDisposed` pretest, transfer of caller ownership or reliance on a strong reference to the wrapper alone.

All graph acquisitions are provisional until the entire structural/root/uniqueness/extent/storage preflight completes. Any later failure releases all acquired node/source/dependency credits and reservations in reverse acquisition order. Existing caller nodes/leases and previously built graphs remain intact. Duplicate node AssetIds/repeated nodes within one graph reject; the same open node may independently supply different graphs. Retain clones the graph's *private* credits, not original caller nodes, so it remains legal when those caller nodes have since closed.

The unqualified constructors/Retain here create ordinary detached caller owners within [M03 4.6](04-capacity-and-workload-profiles.md#m03-46-finite-operational-limits-admission-domains-and-charging)'s per-object bounds, not engine work or an implicit cooker. Retain on an already domain-bound graph stays in that same domain; explicit Retain(domain) creates another independently charged bound snapshot. A bound graph/request/byte credit cannot shed its domain by calling a no-argument Retain. Entering a different explicitly supplied domain is a new fully charged acquisition, never a transfer or source-credit consumption.

Graph.Dispose closes new acquisition/Retain/admission and releases only its graph-owned credits. Graph Retain/acquire and close linearize under the graph gate; partial child acquisition on failure is unwound. Already returned read credits/retained graphs/accepted operations survive. Tools' native AssetCookRequest constructor takes its own retained graph snapshot, validates request AssetId against that snapshot's unique root and [M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff)'s explicit Cook ResourceScope before admission, and unwinds on mismatch/specification/scope/path failure. All three native/source constructor signatures are [M03 4.6.9.6](11-assets-import-streaming-and-packages.md#m03-4696-complete-public-only-route-and-api-replacement-consequences)'s; no string-only directory overload supplies missing registry/destination authority. It owns and disposes only that request snapshot. Native versus source-byte input is an explicit variant; source-only access on a native request is InvalidOperationException, not null/empty fake input.

CookAsync(domain,request,token) validates the open explicit domain, request/arguments/mode, then pre-cancellation before operation retention/reservation. A valid pre-canceled call returns a canceled Task without an operation credit. [M03 4.6](04-capacity-and-workload-profiles.md#m03-46-finite-operational-limits-admission-domains-and-charging) reserves all count/queue/byte/metadata/cleanup capacity before any independent operation credit or staging allocation. Normal admission synchronously acquires its independent graph/source/dependency and selected-specification credits before returning the accepted operation Task. The native request snapshot is copied through the quota-aware Assets/Core boundary; Tools needs no internal Core access. Request Dispose and admission synchronize: close first throws ObjectDisposedException with no admission; acquisition first leaves a valid operation even if the caller immediately disposes request, graph, nodes and original buffers. A failure before operation publication releases provisional operation credits/reservations; graph/request credits are never consumed/transferred.

Nodes/graphs/requests do not join accepted consumers on synchronous Dispose. The operation holds its own credits until all parser/codec/window/decoder/file/subprocess consumers actually stop; finally releases operation temporaries/graph credits/reservations and attempts reclamation of only its named uncommitted staging. Cancellation is terminal only after successful unwind/reclamation. [M03 4.7](11-assets-import-streaming-and-packages.md#m03-47-credit-release-versus-staging-cleanup-failure) governs failed staging cleanup: fault plus ownership-retaining quarantine, never false cancellation/reclamation. Cancellation cannot retract atomic manifest visibility; if unexpected post-commit cleanup fails, the fault explicitly carries the already committed version/publication digest rather than pretending rollback. A separate canceled observer cannot revoke the original Task or its credits.

At controlled stable checkpoints for a one-node graph with no sources/dependencies, after the original caller lease/owner close, independent retained read credits are `N + sum(G_i) + sum(R_i) + sum(O_i) + sum(E_i)` for open node, each graph/request/operation snapshot, and explicit acquired read leases; active callback/use credits are additional temporary protections. Storage is physically reclaimed exactly once only when all allocation/lease/use protections finish. Count actual wrapper/registration bookkeeping separately; do not multiply one backing allocation by reference count or claim zero resident bytes merely because one wrapper closed. Multi-node/source/dependency cases apply per actual range/segment and include every provisional clone.

A stuck consumer leaves the original operation nonterminal, retains storage/reservation/staging and exposes the owner's bounded shutdown/observer failure diagnostics; it does not fabricate Canceled/Cooked, free live bytes or report quiescent reclamation. Once that consumer really exits, cleanup can complete. Tests use controlled actual-use gates and observed accounting, not sleep, GC or Task.IsCanceled as a proxy. [M03 10.6](34-engine-acceptance-cases.md#m03-106-independent-correction-coverage-and-exact-acceptance-effects) defines the exact disposal-order/failure/cancellation matrix.

##### M03 4.6.3 Exact charging and reservation protocol

For a domain D, track backing by an unforgeable Core allocation identity, not address, AssetId, payload digest, range, envelope version or wrapper reference. Its charge is the complete allocator/alignment-rounded allocation byte extent. Two overlapping/disjoint leases from the same live allocation charge backing once while D has any pin/actual-use/retirement interest. A one-byte view into a larger allocation charges that entire allocation. Empty allocations charge0 backing but still charge identity/wrapper metadata. Separate allocations with identical bytes charge separately. No ticket/credit can outlive its charge; physical free is separately observed and can be delayed by caller/other-domain ownership.

Every independent wrapper/reference/registration/source segment costs metadata and one reference slot, including each provisional clone, chain traversal clone and nested independent operation. Shared immutable metadata is charged once per actually shared private storage object/array identity plus every holder/index; equal contents in two copies are two charges. Do not charge shared data131072 times, and do not treat131072 independent owners as one. Qualified maps/arrays include capacity/resize/old+new transition peak and reserved retirement. Caller-held detached graphs/requests/original leases remain in a separately reported caller-owned category; the budget is engine-retained ownership, not the entire CLR heap or all unrelated caller Core registrations.

A shared immutable-metadata entry has its own domain-held metadata-only Core reservation and provisional/live interest count. Its charge is not attached solely to the first graph/operation's disposable reservation: rolling back/disposing that first holder cannot refund shared storage still held by another. Reserve the entry/index/header before first capture; later holders reserve their own reference/holder bookkeeping; release the shared entry only after its final actual interest. The same rule applies to result metadata retained by multiple AssetLease pins.

The trusted build/runtime charge profile fixes conservative all-in reserved byte coefficients with floors `Header>=4096`, `ReadReference>=256`, `DistinctBackingEntry>=256` bytes. Include actual extra extents/typed arrays/shared immutable metadata/canonical copies/scratch; these floors are accounting quanta, not assertions of measured CLR object sizes. No unmeasured smaller coefficient, serialized-length proxy, reflection guess or success-shaped fallback is permitted. The enabled NativeAOT build must establish that the plan covers actual allocations/peak/retained storage on each target; if not established, enabling fails with ResourceAccountingUnavailable and no work. This required future measurement/registry is pending, not evidence currently supplied by the contract. Exact admission is against the frozen profile's conservative demand; separately measure actual allocations and reserved-versus-used differences.

Reservation order is owner/open/arguments/mode, pre-cancellation, immutable per-object/format claims, then aggregate queue/count/backing/metadata/decode/process/capture/staging/cleanup demand. Under the domain admission gate book the complete *peak*, including temporary snapshots and rollback owner, before any independent credit/copy/array/process/file/resource acquisition. Duplicate storage entries in concurrent provisional plans share one backing entry with provisional-interest counts; neither may remove the other's charge on rollback. Book count/metadata for each clone even though backing is shared. Freeze claimed input plan; source/node/request close racing later capture can still cause conventional ObjectDisposedException, followed by full reservation unwind.

Allocate/acquire only inside that reservation; update the pre-existing private operation/recovery record immediately after each success, before the next fallible step. A plan exceeds no limit temporarily: unknown next-stage header demand is first bounded/validated using reserved fixed windows, then additional capacity is reserved before larger allocation. A valid later capacity refusal returns expected execution Rejected only after actual consumers/credits/staging unwind. Writers pre-reserve Count/Write scratch, decoded material, index/package/publication and atomic-publication retirement peaks. Named output roots and<=256-byte GUID/hash-addressed relative stage names are trusted; data-provided source names do not become paths.

The acceptance point assigns a checked monotonic positive domain-local long OperationId and queue sequence only after the full snapshot/reservation succeeds. Exhausted long domains close new admissions rather than wrap. No refusal/constructor failure consumes input ownership or operation sequence; admitted execution Rejected/cancellation/fault consumes its accepted ID. Queue slots release on dequeue; running worker slots release after actual worker use; operation/snapshot/reference/backing/metadata/staging/cleanup charges release only when their own last actual consumer/ownership is gone. A completed task with quarantined cleanup is not an available operation/cleanup slot. Committed on-disk cache retention has its existing cache owner; temporary staging charge transfers to that named committed owner at the marker, not to nowhere.

Synchronous detached-to-domain graph/request Retain books its counts/metadata/bytes before copying; pressure throws Assets AssetAdmissionException with limit/claim/available and no returned owner. It never waits secretly or consumes a detached input. Prepared bound graph/request Dispose releases its own reservation after actual Core users; an admitted operation is another complete reservation. AssetStore publication transfers its ready backing/metadata/pin reservation to the version/lease family before returning; disposing one public pin cannot discharge other children. New result-child read credits remain bounded and inherit that domain. Closing the store/domain forbids new loads/preparations, not destruction of already admitted valid read children; their actual lifetimes continue to determine drain.

##### M03 4.6.5 Queue-full, execution refusal and retry

CookAsync(domain,request,token) is non-Try: expected admission refusal returns a completed Rejected receipt, no PublishedVersion, empty manifest digest and at least one specific admission diagnostic (`CookQueueFull`, `CookBudgetExceeded` or `CookInputLimitExceeded`, with exact limit/claim/available). It is not a synchronous usage error, acceptance, hidden wait or faulted success wrapper. An idle running slot may accept directly; otherwise a pending slot is required. To prove QueueFull, block all configured workers and fill the128 pending records, then submit another otherwise admissible input. Metadata/operation/cleanup headroom must be available so a different limit cannot mask the queue witness.

After acceptance, invalid/unsupported semantic content or later legitimate demand beyond the frozen quota returns execution Rejected with its specific content/admission diagnostic *after* complete safe unwind and successful staging reclamation. Unexpected allocation/I/O/invariant failures fault; cleanup failure supersedes a planned Rejected/canceled task as [M03 4.7](11-assets-import-streaming-and-packages.md#m03-47-credit-release-versus-staging-cleanup-failure) specifies, preserving that planned outcome. Root mismatch, disposed domain/request or bad options remain synchronous usage errors before pre-cancellation; valid pre-cancellation returns canceled Task/no reservation.

AssetStore.LoadAsync's established non-Try failure channel is a contextual faulted Task: AssetBudgetExceeded/AssetQueueFull carries demand/limit; no partial CPU lease or successful Rejected asset. Unpinned Ready eviction may be attempted under the documented policy, never pinned backing/quarantine eviction. Synchronous byte-child acquisition has conventional capacity exception with no usable child, not a fake empty view.

Both admission and execution Rejected leave all caller graph/request/source credits and bits intact. A capacity retry may use the same still-open input after charges are actually released, or an explicitly supplied larger admissible domain. A domain-bound preparation continues to occupy its own charges until disposed; retries cannot assume it was consumed. A deterministic semantic rejection needs a corrected new immutable input. Staging/retirement pressure cannot retry successfully by forgetting a quarantined entry; cleanup must actually succeed or an explicitly independent domain must reserve its own backing. No retry republishes an ambiguous committed operation without inspecting its recorded committed version/key.

##### M03 4.6.6 Callable Assets admission, participation and retirement protocol

This is the normative completion of the operational API, not an optional implementation sketch. All types here are in `TerraFX.Assets`, depend only on Core/BCL, and are normative additions. Content.Tools implements the participant-side request builder, worker and resource drivers against these public types. Assets never names AssetCookRequest, a Tools type, a Runtime type or a participant's private field. The domain owns the queue and retention/recovery records; Tools owns scheduling/execution, not ledger mutation.

**Authority and trusted registration.** Tickets below are sealed reference types with internal constructors. Their domain/entry/generation association is private; copying a reference creates no new credit. Null, wrong domain, wrong operation, wrong stage, already consumed slot and stale ticket throw conventional exceptions before invocation/acquisition. No public ID, counter setter or caller-supplied ordinal mints authority. Ordinals/OperationId are copied observations only. Repeated close/abort/disposal is idempotent, not repeated release. Slots retire rather than wrap. Descriptor approval, executable registration and physical release do not follow from possessing a ticket.

Each domain freezes one `AssetRetentionParticipantRegistration`: the Assets built-in registration for an ordinary two-argument domain, or an explicit trusted compiled registration for a store/cooker. Registration contains the qualified charge-profile ID/version/32-byte digest and explicitly rooted payload/resource-driver type entries. `AssetRetentionTypeEntry.Payload<T>(AssetRetentionTypeProfile profile)`, T:class,IAssetRetentionPayload, and `ResourceDriver<T>(AssetRetentionTypeProfile profile)`, T:class,IAssetRetainedResourceDriver, create opaque entries. `AssetRetentionParticipantRegistration.Create(AssetRetentionChargeProfile profile,ImmutableArray<AssetRetentionTypeEntry> entries)` validates initialization, uniqueness and complete rooted/qualified profiles. AssetRetentionTypeProfile is immutable Guid ProfileId/int Version/32-byte Sha256Digest plus the profile's fixed/variable/peak/retirement charge coefficients. AssetRetentionChargeProfile is the immutable build/runtime manifest of those typed profiles and the Core/Assets charge quanta of4.6.3. Their construction/admission requires the explicit externally qualified manifest; an unmeasured coefficient cannot enable operation. The entries use known closed generic type identity, not reflection discovery, arbitrary activation, delegates or source-provided type names. All initialization metadata is preflighted under unchanged limits. Positive claims are obligations of this trusted compiled participant, not a sandbox for arbitrary code; Assets still validates arithmetic, capacities, registered types and every ticket transition. Data cannot supply a registration.

**Factory-bound public installation.** The overload `AssetStoreRegistrationBuilder(catalogue, participantRegistration)` freezes that exact complete participant closure into the resulting `AssetStoreRegistration.ParticipantRegistration`; the existing constructor selects the compiled Assets built-in closure. `AssetRetentionParticipantRegistration.CreateForStore(chargeProfile, extensionEntries)` adds the statically rooted Assets framework payload/resource-driver entries to the explicit extension entries, then validates the same complete closure without callbacks. Content.Tools' `AssetCookerAdmissionDomain.CreateParticipantRegistration(chargeProfile, extensionEntries)` composes its own statically rooted request/resource entries through that Assets helper; Assets never references Tools. These helpers construct bounded immutable declarations only. They create no domain, query participant getters, invoke code, bind domain state or self-approve qualification receipts.

`AssetStore.CreateAsync` and `AssetCookerAdmissionDomain.CreateAsync` install registrations only into their own actual factory-created domains. The Assets-owned lower initializer `AssetRetentionDomain.CreateAsync(limits, maximumRetainedBackingBytes, registration, executionOptions, token)` creates a fresh domain using the existing four-argument domain constructor, books initialization demand, binds the registration's immutable templates to that exact domain, and returns only after installation succeeds. The store/cooker factory starts workers only after this managed initialization. `AssetRetentionDomain.ParticipantRegistration` observes the frozen operand used by the domain; `FactoryRegistration` observes the exact detached registration installed by the new initializer and is null for directly constructed domains without that installation. These are read-only observations, not setters, transfer of close authority, or commands.

The cooker overload `AssetCookerOptions(limits, verificationRegistration)` freezes a complete `AssetStoreRegistration` for verification dispatch. Its existing one-argument constructor selects the compiled Content.Tools built-in registration. A runtime-only store instead carries only its qualified inspector/materializer closure; it does not load or reference the producer/Tools module. Tools-side producer refusal and Runtime-side producer-free inspection are distinct consumer claims. No caller-created separate domain, matching profile digest alone, registration receipt or `RetentionDomain` observation substitutes for factory installation into the exact domain.

Before invoking any registration accessor, each factory validates the entire trusted closure: selected catalogue and imported descriptor/rule/settings set; exact charge-profile identity, digest and complete coefficient bodies; all required Assets/Tools entries; typed schema, role and factory bindings; module/artifact/runtime-target qualification; and receipt/source correspondence. This is all-or-nothing across the closure: a well-formed first entry's `Schema` getter is not called if a later required entry is missing, foreign, stale or mismatched. After full validation, each qualified getter is queried exactly once per registered accessor key, outside gates, and the resulting immutable binding cache is local to that new domain. Receipts attest an external qualification run; they are not proof by constructor, and no active callback is executed to discover or repair a registration.

The lower `CreateAsync` is non-Try: null or intrinsically malformed options throw usage exceptions synchronously; a valid pre-canceled call returns a canceled task without creating a domain or invoking a getter/worker. After cancellation or failure once a getter/initialization callback begins, its reservation remains charged until that actual callback or worker stops; only then may the private domain drain and release service roots. Unavailable, stale, foreign or mismatched deployment qualification faults the task with contextual `AssetAdmissionException` carrying the existing `ResourceAccountingUnavailable` refusal. Capacity failure carries its exact existing admission refusal. Store/cooker factories retain their established initialization-task fault/cancellation channels and add owner context when propagating lower failures. Unexpected allocation/invariant failures preserve the primary exception through actual cleanup; observer cancellation or task completion never substitutes for callback/worker quiescence.

For public supporting-shape closure, AssetRetentionTypeProfile's full constructor/properties are Guid ProfileId, int Version, ImmutableArray<byte> Sha256Digest, long FixedMetadataBytes, long ConstructionPeakMetadataBytes, long RetirementMetadataBytes, ImmutableArray<AssetRetentionStorageSite> StorageSites. A site is the readonly tuple uint SiteTag, int ElementByteCount, int ElementAlignment, long HeaderByteCount; tags are positive/unique/sorted, sizes positive/alignment power-of-two/header nonnegative. All bytes include allocator/CLR padding and actual capacity, not serialized length. A literal per-instance site demand is uint SiteTag, long ElementCapacity, int ConcurrentCopies; its charge is ConcurrentCopies times HeaderByteCount plus aligned ElementCapacity*ElementByteCount, checked, plus the profile's fixed/peak/retirement entries. The parentheses encompass the whole per-copy storage. Maximum retained demand and simultaneous old+new/constructor demand are separate literal vectors in the qualified plan; a resizable site records both capacities/copies before resize. AssetRetentionChargeProfile's constructor/properties are Guid ProfileId, int Version, ImmutableArray<byte> Sha256Digest, string RuntimeTarget, ImmutableArray<byte> CoreArtifactSha256Digest, ImmutableArray<byte> AssetsArtifactSha256Digest, long HeaderReserveBytes, long ReadReferenceReserveBytes, long DistinctBackingEntryReserveBytes, ImmutableArray<AssetRetentionTypeProfile> TypeProfiles, ImmutableArray<byte> QualificationReceiptSha256Digest. Target is an explicitly pinned architecture/runtime/AOT lane; profile digest/receipt refer to literal external manifests, not self-approval through constructors. Type/site coefficients and receipt bytes are not fabricated here. Generated trusted request/resource plans provide their site-demand vectors with claimed totals; qualification independently checks those totals and actual allocation/resize/lifetime peaks. No callback under a gate computes them and no public participant claim can substitute for that prerequisite.

`AssetRetentionExecutionOptions(int queueCapacity,int ioWorkerCount,int cookWorkerCount,long maximumDecodedAssetBytes,long maximumConcurrentDecodeBytes)` is sealed/immutable with get-only properties in that order. Queue is1..128. Existing configured worker options remain1..64; zero in this lower configuration means that *absent work class is disabled*, not zero-worker inline execution. A cooker has IoWorkerCount0/CookWorkerCount2, a store4/2. At least one class is required for operational use; a retention-only domain has null execution options and cannot admit operations. Decode ceilings/defaults remain256MiB. Add a domain constructor overload `(AssetRetentionLimits limits,long maximumRetainedBackingBytes,AssetRetentionParticipantRegistration registration,AssetRetentionExecutionOptions? executionOptions)`. The two-argument constructor means built-in registration/retention only. Store/cooker factories use this overload and start their own workers only after managed initialization. No late registration/configuration.

Supporting immutable public operands are complete as follows. Constructors take listed fields in order; arrays must be initialized, enums known, digests32 bytes where required, scalar claims checked/nonnegative. They contain bounded inert facts, not new byte credits/resources. Planning facts are cached during bounded detached request/graph construction; CookAsync does not create an uncharged private copied plan.

| Type | Exact fields/factory and validation |
|---|---|
| `AssetCaptureKind` | Graph0, NativeRequest1, SourceRequest2, RuntimeLoad3, ReadyAsset4; operational only. |
| `AssetWorkClass` | Io0, Cook1; operational only, class must be enabled. |
| `AssetRetentionStorageDemand` | Readonly uint SiteTag, long ElementCapacity, int ConcurrentCopies; known positive site, nonnegative capacity/copies, checked charge by registered site. Retained and simultaneous peak vectors are initialized immutable arrays, do not mix the same allocation's ownership sites or omit old+new resize storage. |
| `AssetRequestRetentionFacts` | long MetadataBytes, long CapturePeakMetadataBytes, Guid ChargeProfileId, int ChargeProfileVersion, ImmutableArray<byte> ChargeProfileSha256Digest, ImmutableArray<AssetRetentionStorageDemand> RetainedStorage, ImmutableArray<AssetRetentionStorageDemand> CapturePeakStorage, AssetResourceScope ResourceScope. Required scope carries Assets-visible requested ID, destination/read locations and frozen verification registration before every NativeRequest/SourceRequest/RuntimeLoad admission. Registered totals include request storage; Assets derives its copied scope/path/capability sites as4.6.9 specifies. No opaque payload getter establishes scope. |
| `AssetRetentionPlan` | Get-only Kind, ImmutableArray<BufferRetentionInfo> Reads, long RetainedMetadataBytes, long CapturePeakMetadataBytes, AssetRequestRetentionFacts? RequestFacts, plus copied exact native/source shape/limit claims from4.6.1. Factories `FromGraph(StagedAssetGraph source,AssetRequestRetentionFacts? requestFacts)` choose Graph versus NativeRequest; `FromSources(ImmutableArray<BufferChain<byte>> sources,NativeGraphLimits limits,AssetRequestRetentionFacts requestFacts)` chooses SourceRequest; `ForRuntimeLoad(AssetRequestRetentionFacts requestFacts)` chooses RuntimeLoad with empty initial Reads; `ForReadyAsset(ImmutableArray<BufferRetentionInfo> reads,long retainedMetadataBytes,long capturePeakMetadataBytes)` chooses ReadyAsset. No factory retains payload or invokes a visitor under a gate. Reads retain canonical descriptor order. Domain revalidates full local shape, registered profile, peak and source identity/range. |
| Plan-derived counters | Graph reserves1 graph/0 requests; NativeRequest1 graph/1 request; SourceRequest0 graph/1 request; RuntimeLoad0 graph/1 request initially; ReadyAsset0 graph/0 requests/1 CPU pin. Reconstructed inert/importer graphs need explicit growth reserving1 graph before construction. An operation accepts only NativeRequest/SourceRequest/RuntimeLoad and adds1 outstanding operation. No participant can request native request with graph count0. |
| `AssetAdmissionRefusal` | Readonly value: bool HasValue, string Code, string LimitName, long Claim, long Limit, long Available. Internal construction; successful Try has default/HasValue=false; expected false has exact nonempty contextual refusal. Closed/invalid calls throw. Codes map to4.6.5's cooker/store channels, not new statuses. |
| `AssetRetentionGrowthClaim` | ImmutableArray<BufferRetentionInfo> Reads, ImmutableArray<BufferAllocationInfo> Allocations, long AdditionalMetadataBytes, int AdditionalInterestSlots, int AdditionalGraphSnapshots, long ConcurrentDecodeBytes, long CapturedProcessBytes, ImmutableArray<AssetResourceDemand> Resources, ImmutableArray<AssetFileGrowthDemand> FileGrowth, ImmutableArray<AssetRetentionStorageDemand> PeakStorage. Metadata includes new plan/copy/old+new peak/ticket/descriptor/index/cleanup state and agrees with registered site demand. AdditionalInterestSlots are metadata/actual-use interests without new byte access, counted in the Core reference quota; no fabricated read-info. Additional graphs0..remaining ceiling; no request/operation/queue privilege. Decode/capture use validated logical/raw bytes; Core backing is rounded. Every claim fits per-operation/aggregate limits and registered footprint rules. |
| `AssetCleanupResourceKind` / `AssetResourceDemand` | File0/Directory1/Stream2/Process3/Handle4 remains4.7.3's operational kind. Constructor/properties are Kind, AssetResourceLocation Location, AssetResourceAccess Access, long MaximumNamedBytes, ImmutableArray<int> PrerequisiteResourceOrdinals. [M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff) replaces unbound TrustedRelativeName with typed stage-root/name, registered-package/publication and nonpath locations, deriving exact root/path/ownership. Caller final-output paths cannot bypass verified transfer. File/Directory still use0 handle/process slots; Stream/Handle1 handle; Process1 handle/1 process; I/O handles are separate declared stream/handle tickets, not hidden in file cells. Same-operation earlier prerequisites/no cycles and full entry/byte/metadata reservation remain. |
| `AssetFileGrowthDemand` | AssetResourceTicket Resource, long NewMaximumNamedBytes. Resource is this operation's recorded file; new maximum >= its existing reserved extent. Domain atomically reserves the difference, not the entire old size again; growth Commit installs the new maximum before write. No increment/decrement by a bare name. |
| `AssetOperationResult` | AssetOperationOutcome Outcome, Exception? PrimaryException, ImmutableArray<Diagnostic> Diagnostics, AssetVersion? PublishedVersion, ImmutableArray<byte> PublicationSha256Digest. Rejected/Canceled/Faulted/Committed only; Committed requires version/32-byte digest, others null/initialized empty. Faulted requires primary; Canceled retains original OCE/token; Rejected retains specific diagnostics. This is terminal intent, not actual completion/cleanup success. |
| `AssetOperationCompletion` | Same immutable outcome/primary/diagnostics/version/digest plus long OperationId, Exception? CleanupException, bool HadRetainedOwnershipAtFailure. Domain-produced/frozen only. Completion Task follows4.7: canceled on safe Canceled, faulted with original/AssetCleanupException on faults, successful value only for safe Rejected/Committed. No successful Task carries Failed/default fault. |
| `IAssetRetentionPayload` | Marker for a registered immutable bounded participant payload. Assets stores its strong reference without calling properties/methods. No undisclosed native/read/thread/finalizer ownership may hide in it; owning bytes/resources are issued by tickets. Tools' private native/source request/work payloads are statically rooted implementations, fully charged while domain-held. |

Metadata ownership has no double attribution: plan/request/growth scalar metadata and site vectors describe Assets/participant-managed storage only, excluding Core's automatically charged registration/identity/reservation/owner/descriptor/interest sites. Core derives its own charge from Reads/Allocations/extra slots and distinct/shared identity state. Domain preflight checks the complete *all-in* graph/chain/request/operation peak against local limits, then books participant-only bytes through Core additionalMetadataBytes; the sum is the one aggregate metadata ledger. Assets-known shared immutable storage has its own interest-counted metadata reservation and is not charged again per holder; each holder/ticket/copy is. All coefficient/site manifests label one physical allocation owner so Core storage is neither omitted nor included twice. Immutable privately copied Tools metadata is per-copy, not caller-declared sharing by content digest.

The mandatory public command surface is:

```csharp
// Additional commands on AssetRetentionDomain; observation/cleanup members remain.
public bool TryBeginSnapshot(AssetRetentionPlan plan,
    out AssetAdmissionTicket? admission, out AssetAdmissionRefusal refusal);
public bool TryBeginOperation(AssetRetentionPlan plan, AssetWorkClass workClass,
    int priority, out AssetAdmissionTicket? admission,
    out AssetAdmissionRefusal refusal, CancellationToken cancellationToken = default);
public AssetWorkerTicket AcquireWorker(AssetWorkClass workClass, int workerIndex);
public CancellationToken CloseRequested { get; }
public Task WhenDrained { get; }

public sealed class AssetAdmissionTicket : IDisposable
{
    public BufferReadLease<T> CaptureRead<T>(BufferReadLease<T> source, int readIndex)
        where T : unmanaged;
    public BufferChain<T> CaptureChain<T>(BufferChain<T> source, int firstReadIndex)
        where T : unmanaged;
    public StagedAssetGraph CaptureGraph(StagedAssetGraph source);
    public AssetSnapshotTicket CommitSnapshot(IAssetRetentionPayload payload);
    public AssetOperationTicket CommitOperation(IAssetRetentionPayload payload);
    public Task WhenReleased { get; }
    public void Abort();
    public void Dispose();
}

public sealed class AssetSnapshotTicket : IDisposable
{
    public AssetCaptureKind Kind { get; }
    public Task WhenReleased { get; }
    public void Dispose();
}

public sealed class AssetOperationTicket
{
    public long OperationId { get; }
    public long QueueSequence { get; }
    public AssetWorkClass WorkClass { get; }
    public IAssetRetentionPayload Payload { get; }
    public Task<AssetOperationCompletion> Completion { get; }
    public bool TryReserveGrowth(AssetRetentionGrowthClaim claim,
        out AssetGrowthTicket? growth, out AssetAdmissionRefusal refusal);
    public AssetUseTicket BeginUse(ImmutableArray<AssetResourceTicket> resources);
    public Task<AssetOperationCompletion> RequestRetirement(AssetOperationResult result);
}

public sealed class AssetWorkerTicket : IDisposable
{
    public bool TryTake(out AssetOperationTicket? operation);
    public Task<AssetOperationTicket?> TakeAsync(
        CancellationToken cancellationToken = default);
    public void EndExecution(AssetOperationTicket operation);
    public void Dispose();
}

public sealed class AssetGrowthTicket : IDisposable
{
    public BufferReadLease<T> CaptureRead<T>(BufferReadLease<T> source, int readIndex)
        where T : unmanaged;
    public StagedAssetGraph CaptureGraph(StagedAssetGraph source);
    public BufferAllocationTicket GetAllocationTicket(int allocationIndex);
    public AssetDecodeTicket GetDecodeTicket();
    public AssetResourceTicket GetResourceTicket(int resourceIndex);
    public void Commit();
    public Task WhenReleased { get; }
    public void Dispose();
}

public sealed class AssetUseTicket : IDisposable
{
    public void Dispose();
}

public sealed class AssetDecodeTicket : IDisposable
{
    public long ByteCount { get; }
    public AssetUseTicket BeginUse(ImmutableArray<AssetResourceTicket> resources);
    public Task WhenReleased { get; }
    public void Dispose();
}
```

CaptureChain/CaptureGraph capture every mandatory slot exactly once, verify source-open/full-plan equality and build a private bound carrier from pre-reserved storage. Input close racing capture may throw ObjectDisposedException and abort without consumption. Provisional carriers cannot escape as admitted owners before Commit. CaptureRead returns only the distinct planned registration; Core roots are attached to the ticket reservation even if a later graph/request helper throws. StagedAssetGraph additionally implements the built-in IAssetRetentionPayload; a captured graph's owning roots are disclosed ticket-owned slots, not hidden owners. Commit binds its preinstalled carrier anchor to the snapshot/operation before public use; no second caller binding/setter is needed. Tools source/native request payloads similarly hold the issued carrier and returned snapshot ticket. AssetCookRequest.Retain(domain) uses this protocol; Assets need not name AssetCookRequest. CaptureGraph preserves exact frozen native/source/dependency facts, not semantic recomputation under a domain gate.

**Atomic admission and queue.** TryBegin validates usage, immutable shape/registered footprint and, for operation Try, pre-cancellation (synchronous OCE under chapter02). Under the Assets gate it reserves *together* implicit graph/request/operation counts, shared/private metadata/peak, Core reads, cleanup/failure headers and one direct-worker handoff or pending slot. Only Core's callback-free inert kernel is called under this gate. Any refusal rolls back every provisional interest before false/null/refusal; no graph-only partial grant, input clone, OperationId or accepted sequence. Concurrent plans see all provisional claims. Ticket/private records allocate only from booked metadata; allocation failure aborts the entire transaction.

No source/node/graph/request gate spans an Assets call. Capture admits a bounded source interest, leaves that gate before participant work, and rechecks source/range/identity in Core. Commit verifies all mandatory slots and exact registered payload, atomically converts provisional to prepared/accepted counts and consumes the admission once. Snapshot commit assigns no ID. Operation commit assigns checked positive monotonic OperationId/QueueSequence and installs payload/queue/direct handoff before exposure. Overflow closes rather than wraps. The admission saves its token; after all potentially failing validation, under the commit gate it reads IsCancellationRequested and domain-open state once before the indivisible ID/install transition. Observed cancellation/close aborts without ID; a false token read at that commit linearization makes a subsequent concurrent token signal admitted cancellation. There is no user cancellation callback under gate. Commit has no callback/allocation/wait under gate.

The pending limit128 is aggregate across classes. Each idle configured worker has at most one pre-reserved direct handoff, charged as its running-slot reservation, not another queue. Pending selection is higher signed priority first, then ascending QueueSequence within class; default0 is FIFO. TryTake/TakeAsync converts pending/direct to executing atomically, releases only pending capacity and binds exact worker/operation. Counts/recovery/bytes/metadata remain. Wrong worker/class/double-take/EndExecution misuse throws. EndExecution is legal only after the real worker body stops using borrows; at most one executing operation per worker. Worker disposal is its loop's finally after actual exit, never wait cancellation. Tools retains/joins actual worker tasks during shutdown. TakeAsync null is only permanent closed-and-empty end marker, not pressure/default work; cancellation is canceled Task. Signals/continuations run outside gates.

The worker may await phase helpers and its safe resource-local RequestCleanup Tasks, not its own operation Completion/RequestRetirement Task or domain WhenDrained/DisposeAsync/whole RetryCleanupAsync. After observing helper cancellation/fault it leaves actual resource/Core borrows, records the exact retirement intent nonblockingly and calls EndExecution in its real body's finally. The worker loop/EndExecution does not await whole-operation cleanup; a separate cooker/store observer maps Completion to the original public operation Task. EndExecution releases the executing-worker interest only, signals eligibility outside gates and cannot refund still-owned files/backing/operation metadata or publish a false terminal result. [M03 4.6.10](11-assets-import-streaming-and-packages.md#m03-4610-helper-completion-worker-retirement-and-cleanup-progress) gives exact boundaries, failure precedence and progress.

An abandoned uncommitted ticket retains finite provisional charges until Abort/Dispose/domain close, never GC. Abort closes capture and recorded Core roots, waits for in-progress captures/actual users and refunds only ended interests. Domain close aborts all uncommitted admissions and cancels queued work without calling payload methods; executing work receives CloseRequested outside gate and quiesces. Snapshot Dispose closes only its roots; operation retirement uses its worker/use/resource tickets, not dropping its facade. Retired tickets retain only cheap immutable caller observations; clear domain-held payload/large arrays when actual ownership ends, without silently stranding charged registration capacity.

Admission state distinguishes Reserved/Capturing, CommittedSnapshot/CommittedOperation and Aborting/Released. Commit transfers its recorded ownership to the issued snapshot/operation. Abort/Dispose after commit is an idempotent facade cleanup, not disposal of that transferred reservation; the usual using/finally pattern cannot revoke accepted work. Snapshot Dispose or operation RequestRetirement is the corresponding owner's close. Growth similarly distinguishes materializing from committed: Dispose after Commit does not close operation-owned allocations/resources, while aborting partial materialization retains its entries under operation cleanup. Core allocation-ticket Dispose still has its explicit4.6.7 bound-owner semantics; it is not a temporary using token that may be dropped while that owner must remain open.

**Incremental growth.** TryReserveGrowth checks complete additional peak against the same Assets/Core ledgers and per-operation ceilings before graph construction/allocation/open/write. false has exact refusal/no acquisition. FileGrowth reserves delta and installs maximum before write. Old source/output/scratch coexist with new claims. Commit consumes materialized subslots and transfers ownership into the operation; unused reservations refund. Abort/Dispose of partially materialized growth cannot refund acquired resources/storage: they remain in the operation's actual-use/cleanup record. Concurrent growth sees all provisional claims.

ConcurrentDecodeBytes additionally issues one pre-reserved AssetDecodeTicket through GetDecodeTicket, consumed once and valid only for a positive decode claim. Its ByteCount is the exact reserved logical decode total. Its BeginUse reserves the ordinary operation/use slot and also associates that real consumer with this activity; Dispose closes future activity uses, and only after existing activity uses actually end does it refund the logical decode counter/complete WhenReleased. It does not dispose the decoded output/backing. After growth commit this activity is operation-owned until explicit activity disposal/operation retirement; discarded growth closes an unused activity or retains an active one to true quiescence. This is the callable decode-counter retirement boundary, not an unexposed ledger decrement or a Task-state guess.

BeginUse reserves1 additional Core interest slot and its qualified metadata over this operation/exact resource set before exposing real consumption; members/prerequisites are validated. Empty resources still reserve the operation-use slot. Expected synchronous pressure throws AssetAdmissionException with no usable token, not a hidden wait. Dispose occurs only after actual native/file/process/callback completion, not observer cancellation. Core windows/children retain their separate quota/access checks. No public handle/pointer. Retirement/close refuses new work uses/growth; existing users survive. Ready/result pins are captured into a *separate* prepared snapshot/reservation before publication, sharing backing and owning references/metadata. Closing the old operation cannot close that new family or join its independent consumer as old work. This preserves4.6.3's ready-version and caller/operation independence.

The resource/cleanup protocol is:

```csharp
public interface IAssetRetainedResourceDriver
{
    ValueTask AcquireAsync(AssetResourceTicket ticket, CancellationToken cancellationToken);
    AssetResourceOwnership GetOwnership();
    ValueTask RequestStopAsync(AssetResourceTicket ticket);
    ValueTask<AssetResourceCleanupResult> CleanupAsync(AssetCleanupAttempt attempt);
}

public sealed class AssetResourceTicket
{
    public long OperationId { get; }
    public int ResourceOrdinal { get; }
    public AssetResourceDemand Demand { get; }
    public AssetOperationResourceScopeTicket ResourceScope { get; }
    public string TrustedAbsoluteRoot { get; }
    public string? ResolvedAbsolutePath { get; }
    public AssetNamedResourceDisposition NamedDisposition { get; }
    public Task AcquireAsync(IAssetRetainedResourceDriver driver,
        CancellationToken cancellationToken = default);
    public void RecordAcquired();
    public Task RequestCleanup();
}

public sealed class AssetCleanupAttempt
{
    public AssetResourceTicket Resource { get; }
    public bool MayDeleteNamedObject { get; }
    public string? OwnedAbsoluteRoot { get; }
    public void RecordActualRelease();
}
```

AssetResourceOwnership is NeverAcquired0/Owned1/ActuallyReleased2, operational only. AssetResourceCleanupResult is readonly bool Released/Exception? Failure; factories ActualRelease() (true/null) and Retained(Exception failure) (false/non-null). Released also requires RecordActualRelease on this exact attempt. It is not a receipt a caller submits to a public decrement. Failed release retains state/charge even beyond the16 detailed samples.

That driver ownership state refers to its installed resource carrier, not permission to delete a named path. [M03 4.6.9.2](11-assets-import-streaming-and-packages.md#m03-4692-public-resource-locations-access-and-ownership)'s separate NamedDisposition and pass-issued MayDeleteNamedObject/OwnedAbsoluteRoot state distinguish temporary physical ownership from borrowed/published identity interests. A borrowed or published file carrier can be actually released without deleting the file. Publication transfers only its named temporary-byte/entry claims; actual driver/stream/use/metadata interests remain until their own confirmed retirement.

**Acquire-before-return is owned.** GetResourceTicket retrieves a reserved entry/dependencies/ordinal and opens nothing. AcquireAsync verifies exact registered driver type/unique driver object and books its planned cell storage, installs a strong reference/sentinel and an active inspection/acquisition interest in the domain recovery record before any driver callback. Driver constructors are resource-free; initial GetOwnership runs outside gates and must report NeverAcquired. A populated/reused driver is invalid input before new OS invocation; after inspection actually exits, detach it without cleanup/adopting its caller-owned pre-existing resource. Close waits an in-flight inspection as well as acquisition. A reliable empty result plus valid open/token check permits Acquiring; pre-cancel returns canceled Task/no Acquire callback and the unused reservation retires through its ticket/operation. driver.AcquireAsync runs outside every gate, writes successful OS resource into this pre-existing cell immediately, calls RecordAcquired, then performs the next fallible helper work. No unrecorded local owner escapes. Valid RecordAcquired has no callback/allocation and cannot reject an in-flight acquisition because close raced. Close waits its actual callback/finally.

The domain owns the cell even if a helper throws between field installation and RecordAcquired or before owner return. After the acquisition callback exits it invokes GetOwnership outside gates to reconcile sentinel/state. Owned-but-unrecorded is an invariant fault with recovery, not refund/orphan; reliable NeverAcquired permits refund. Throwing/invalid ownership inspection retains the full uncertain acquisition and surfaces failure, never defaults to NeverAcquired. Running acquisition/native work remains RetainedLive/nonterminal. Successful callback return without actual recorded acquisition is an invariant failure, not fake usable data. Primary/partial cleanup follows4.7.

AcquireAsync and AcquireWithAccessAsync are inner phase Tasks. After their own actual callbacks, inspections and active uses have ended, cancellation/primary-fault observation completes independently of worker EndExecution and whole cleanup. Partial acquired/uncertain cells remain domain-owned/charged; the helper drops only ended phase interests and reliably unused provisional claims. No automatic whole-operation unwind is awaited inside it. The worker can then perform its finally; original operation Completion still waits that worker and actual cleanup, including any later primary-plus-cleanup failure. An actual blocked callback/inspection keeps its helper nonterminal; a mere retained stopped file does not.

**Assets actually performs cleanup.** RequestRetirement freezes intended outcome and schedules whole-operation quiescence without joining the caller worker in that command. For whole retirement Assets issues installed RequestStopAsync calls outside gates and observes actual acquisition/worker/use quiescence before general resource teardown. Those waits are parked eligibility conditions, never possession of the cleanup executor, selection lease or a cursor that excludes resource-local work. A resource's public Task-returning RequestCleanup also admits *phase-local* close during a live worker and after domain/operation close: atomically close that resource's future uses/acquisitions, then join only its actual acquisition/uses and required dependent resources, not the whole calling worker/operation. The caller must leave its own uses before awaiting that close. Unaffected operation work can continue; this is the callable writer/read-stream close used before seal/move, not a hidden stop or self-join. Domain close still joins the real worker for original operation completion.

Phase close uses the same domain-owned cleanup coordinator/driver/cursor, with pre-reserved per-resource Task/failure header/sample sites; no callbacks or continuations under gates. Its Task is scoped to that resource's close generation, not the currently selected pass. Success requires its own actual release and callback/inspection exit (and required dependent release), never shared-pass success. Cleanup failure faults explicitly with retained ownership. A still-consuming/blocked native callback keeps that close nonterminal with separate bounded observation. Concurrent/repeated close while that generation is pending or faulted shares its frozen Task and starts no implicit retry. Explicit domain RetryCleanupAsync can later release retained ownership without rewriting earlier faulted Tasks; calls after confirmed release observe idempotent completed release.

The coordinator scans pre-existing records with bounded cursor/marks from the1-MiB reserve, not an unbounded copied selection. It skips worker/use/dependent-ineligible entries, records their wait flags and remains available for other eligible phase closes. No whole-worker join may hold its dispatch authority. A late RequestCleanup marks its existing resource cell and wakes/rechecks selection under the gate even when another pass has selected work; it neither creates an unbounded queue nor adopts that pass's completion. Each ready entry has at most one issued AssetCleanupAttempt/callback in flight. No duplicate attempt, missed late-close signal or head-of-line worker wait is permitted. [M03 4.6.10](11-assets-import-streaming-and-packages.md#m03-4610-helper-completion-worker-retirement-and-cleanup-progress) fixes completion, ordering and lost-wakeup rules.

Only the domain coordinator invokes installed drivers. CleanupAsync runs outside every gate on a permitted control context; an affine driver dispatches to its real owner and completes after actual cleanup, never awaiting an executing worker that is itself awaiting that phase close. Stop/inspection/cleanup driver contracts cannot require worker EndExecution or the containing pass/domain drain in order to complete that worker's helper/close. A genuinely blocked native callback remains retained failure, not an excuse to insert an otherwise avoidable protocol cycle.

RecordActualRelease is legal only for this in-flight attempt after authoritative close/delete/exit and recorded acquisition/dependent/use stop. It marks intended confirmation without refund while the callback remains active. After callback exit, Assets observes driver ownership outside gates and commits an actually confirmed Released/refunds exact resource/growth charges under gate, publishing outside it. Retained/throw without actual-release confirmation leaves Quarantined/RetainedLive and safe siblings continue. If the driver records actual release then unexpectedly throws, a reliable ActuallyReleased observation permits that entry's refund while retaining/reporting the thrown cleanup/invariant failure; it does not quarantine an already released handle or re-close a reused PID. Uncertain/throwing inspection instead retains conservative ownership until explicit reconciliation. Stale/different attempts cannot retire reused resources. Separate tickets expose partial stream/file/process/pipe cleanup; no undeclared bundled handles. Drivers may reenter snapshots/request close with no locks held; overlapping same-resource acquisition/cleanup refuses before callback.

RetryCleanupAsync/DisposeAsync/RequestCleanup drive those same installed cells, including after helper failure. One domain coordinator owns dispatch; concurrent observers of the same retry pass share its Task, not every resource-close Task. A retry's frozen selected set is its observation scope, not an exclusive executor lease; newly requested phase-close work remains independently eligible/observed. Pre-cancel admits no retry and post-admission token is observation-only. No join waits under gate or retains exclusive selection authority. The coordinator owns selection/failure headers, not a Tools ledger. All4.7 state/failure/wait/repeated-close rules remain. Original operation Completion publishes only after actual worker/consumers/credit cleanup; stopped-unreleased staging faults with AssetCleanupException and stays owned. Stuck acquisition/consumer/pass remains nonterminal; only separate bounded observation may fault.

Committed transfer is exactly [M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff)'s public scope -> native-input binding -> sealed-resource verification -> Assets-issued AssetVerifiedPublicationTicket -> PrepareCommittedTransfer(operation,verified) -> prepared PublishAsync/actual relocation-marker protocol. Remove the abbreviated PrepareCommittedTransfer(operation,version,digest,files) and unrestricted RecordPublicationCommitted(target): neither establishes a checked association. A valid marker attempt records actual relocation/visibility through its pre-issued Assets capability, atomically performing the same nonthrowing/allocation-free committed ownership transition. Quiescence, irreversible-marker outcome, failed cleanup and independent ready-Core capture remain; version/hash/file/path facts now have a callable lower-owned origin.

**Public-only Tools trace.** Factory freezes registration/options/workers. Request explicitly supplies AssetResourceScope as4.6.9 declares; CookAsync validates ID/scope/graph/profile/registration before precancel and uses the same atomic TryBegin/capture/commit/rollback. Worker uses growth/resources, binds native graph/context through Assets, seals actual package/publication resources and invokes public bounded verification. Only its operation-bound result can prepare target publication. It observes helper outcomes, ends real borrows, requests retirement without awaiting it and calls EndExecution in finally. A separate observer maps Assets Completion to typed receipt/fault/cancellation after whole cleanup. No hidden path/result in a payload, gate callback, fabricated success or reverse edge. Runtime uses registered read scope and the same verifier without Tools;4.6.9 gives the full sequence.

##### M03 4.6.9 Destination, sealed resources and verified publication handoff

This section owns the complete trusted-root/common-result public boundary. All types/members below are in `TerraFX.Assets`, using only Core/BCL. [M03 4.6.6](11-assets-import-streaming-and-packages.md#m03-466-callable-assets-admission-participation-and-retirement-protocol) still owns atomic accounting, opaque tickets, acquisition-before-helper-return, driver invocation outside gates, growth and actual cleanup; sections4.6.7-8 are unchanged. The new operands close their public input/output routes rather than granting Tools private access.

###### M03 4.6.9.1 Caller declaration, bounded copy and operation binding

AssetResourceScope is a sealed immutable *detached declaration*, not an owning filesystem handle, publication permission or proof that a directory exists. Its factories validate/copy all operands; no I/O, code discovery, worker, native handle or user callback occurs. AssetSourceRegistration is the nominal immutable full-version/source pin, completed with the exact public constructor below. AssetStoreRegistration gains the read-only initialized `ImmutableArray<AssetSourceRegistration> Sources` observation of its frozen RegisterSource table; registration remains an explicit typed codec/source set, not a descriptor-discovered executable.

```csharp
public enum AssetResourceScopeKind
{
    Cook = 0,
    RuntimeLoad = 1
}

public sealed class AssetSourceRegistration
{
    public AssetSourceRegistration(AssetVersion version,
        string absolutePackagePath, string absolutePublicationPath,
        scoped ReadOnlySpan<byte> packageSha256Digest,
        scoped ReadOnlySpan<byte> publicationSha256Digest);
    public AssetVersion Version { get; }
    public string AbsolutePackagePath { get; }
    public string AbsolutePublicationPath { get; }
    public ImmutableArray<byte> PackageSha256Digest { get; }
    public ImmutableArray<byte> PublicationSha256Digest { get; }
}

public sealed class AssetResourceScope
{
    public static AssetResourceScope ForCook(AssetId requestedAssetId,
        string absoluteOutputDirectory, AssetStoreRegistration verificationRegistration,
        ImmutableArray<AssetSourceRegistration> suppliedSources);
    public static AssetResourceScope ForRuntimeLoad(AssetSourceRegistration source,
        string absoluteCacheDirectory, AssetStoreRegistration verificationRegistration);
    public AssetResourceScopeKind Kind { get; }
    public AssetId RequestedAssetId { get; }
    public string AbsoluteStagingParent { get; }
    public string? AbsolutePublicationDestination { get; }
    public AssetStoreRegistration VerificationRegistration { get; }
    public ImmutableArray<AssetSourceRegistration> Sources { get; }
}

public sealed class AssetOperationResourceScopeTicket
{
    public long OperationId { get; }
    public AssetResourceScope Scope { get; }
    public string StagingAbsoluteRoot { get; }
}

// Additional member on AssetOperationTicket.
public AssetOperationResourceScopeTicket ResourceScope { get; }
```

Source constructor requires a nonnull intrinsically valid full AssetVersion, two distinct trusted fully qualified file paths, and exactly32 bytes per digest; it copies the digests. All256-bit values are representable, not approval sentinels. There is no resource acquisition/disposal or authority inferred from a pin. Existing RegisterSource(fullVersion,makeCurrent), publication/EIDX/envelope joins and independent source selection remain. Nonempty rooted path syntax/4096-byte UTF8 extent and known usable OS path forms are validated; no URI, relative/drive-relative path, device namespace or null character. `Path.GetFullPath` is used only on an already fully qualified input, with no ambient base; ending directory separators are trimmed except the filesystem root. The normalized returned path is the public observation. This normalization is filesystem metadata only, never content/provenance canonicalization. Trusted caller ownership of these locations is explicit; a path string/hash is not a sandbox or publisher authentication.

ForCook requires valid requestedAssetId, nonnull frozen verificationRegistration and initialized unique/conflict-free suppliedSources, each matching one exact full-value/pin/path entry in that registration's Sources. The selected catalogue/typed codec registration is explicit, not supplied by payload bytes. Sources retains the declared subset order, giving0-based source indices; they are permitted trusted external dependencies, not extra semantic Graph.SuppliedDependencies. Unused scope registrations do not enter graph/context hashes. RuntimeLoad requires source to match one exact value/pin/path entry in verificationRegistration.Sources; Sources has that selected entry first, then the other distinct frozen registered entries in registration order. RequestedAssetId is selected Version.AssetId; a selected nonroot output inside a publication is allowed. Cook sets publication destination and staging parent to its normalized output directory; RuntimeLoad has null publication destination and its explicit cache directory as staging parent. Every source file path is independently explicit; no graph name/import filename/diagnostic name resolves another OS path.

AssetRequestRetentionFacts' required ResourceScope makes these facts visible to Assets before TryBegin. NativeRequest/SourceRequest require Cook scope and exact request ID; NativeRequest additionally checks that ID against captured unique graph root. RuntimeLoad requires its matching selected source/RuntimeLoad scope. A graph-only/ready snapshot uses no RequestFacts/resource scope and cannot open resources/publish. A bound request snapshot retains the scope facts but gets no active filesystem namespace or transfer authority. No root is taken from IAssetRetentionPayload. Scope cannot be replaced after a plan/admission/operation is made; a different destination/source/registry needs a new declaration/request/admission.

The domain copies/fixes scope, registry/source pins and all path/namespace facts under the complete pre-acquisition reservation. Scope metadata has Assets-known typed sites and checked char/UTF8/array capacities, included in MaximumRequestMetadataBytes/MaximumOperationMetadataBytes/aggregate metadata and capture/cleanup peak. Participant request-site totals exclude these automatically charged Assets sites and all automatic Core sites; the all-in local checks still include them. Sharing uses actual private immutable storage interests plus per-holder metadata, never a claim that equal path strings/free wrappers cost nothing. Domain tables/target keys exist only for active/prepared/retiring/quarantined work, bounded by the existing work/entry/metadata limits, not an unbounded permanent destination registry.

The public scope-ticket Scope getter observes the original immutable detached declaration, not the engine's private copied lookup/authorization arrays or native resource carrier. Internal operation-owned copy/index interests stay charged until true retirement and can then be cleared without mutating that caller declaration. Retained public inert scope/version/digest/path observations are caller-owned metadata after handoff, not a process-heap bound or a reason to keep an uncharged cooker registry. No public property exposes engine-owned mutable/pool-backed storage or makes retired scope authority valid again.

A provisional admission gets a pre-reserved private staging name `.tfx-stage-<domainNonceN>-<admissionNonceN>` beneath AbsoluteStagingParent. Nonces are opaque operational128-bit values generated once before the atomic installation, with lowercase32-hex N spelling; they are not public identity generators or digest operands. Their allocation/path/nonce peak is booked before constructing the domain-owned copy. The name/descendant paths fit the existing relative256-byte and complete trusted4096-byte extents. No ID/accepted sequence is consumed by refusal; the staging name is independent of the later OperationId. CommitOperation binds that frozen namespace to the issued scope ticket without allocation/callback under the gate. Actual staging-root creation is a later recorded resource acquisition using create-new: an existing path is a fault/conflict, never adopted, erased or silently retried under another name. The parent is caller/cache-owned and is never recursively cleaned. Two requests can bind two output directories in one domain; each scope ticket and staging root remains independent while backing sharing/accounting is unchanged.

###### M03 4.6.9.2 Public resource locations, access and ownership

Replace AssetResourceDemand's unbound relative-name field with these complete operational values:

```csharp
public enum AssetResourceLocationKind
{
    StagingRoot = 0,
    StagingRelative = 1,
    RegisteredPackage = 2,
    RegisteredPublication = 3,
    NonPath = 4,
    PreparedVersionDirectory = 5,
    PreparedPackage = 6,
    PreparedManifest = 7
}

public enum AssetResourceAccess
{
    Metadata = 0,
    Read = 1,
    Write = 2,
    Execute = 3
}

public enum AssetNamedResourceDisposition
{
    None = 0,
    TemporaryOwned = 1,
    BorrowedSource = 2,
    PublishedDestination = 3
}

public readonly struct AssetResourceLocation
{
    public static AssetResourceLocation StagingRoot();
    public static AssetResourceLocation StagingRelative(string relativeName);
    public static AssetResourceLocation RegisteredPackage(int sourceIndex);
    public static AssetResourceLocation RegisteredPublication(int sourceIndex);
    public static AssetResourceLocation NonPath();
    public bool IsValid { get; }
    public AssetResourceLocationKind Kind { get; }
    public string? RelativeName { get; }
    public int SourceIndex { get; }
}
```

Internal construction makes default invalid even though Kind's default bits are0. Root/nonpath have null name/source index-1; relative has copied <=256-byte trusted ASCII segments/no root/`.`/`..`/separator ambiguity/drive/stream suffix/invalid or reserved OS name, source index-1. Package/publication has nonnegative index and no caller filename; its full path is exactly that operation scope's source entry. All path segments/full combined extent are validated before reservation/acquisition. Case/path aliasing never mints a new owning resource: duplicate resolved file/directory cells in one operation reject before acquisition, with the supported provider's exact OS identity checks where necessary. Several registered full versions sharing the exact same pinned physical publication/package reuse the same recorded read-only file/seal pair; they do not acquire duplicate named cells. Separate actual stream/handle interests retain their individual charges.

PreparedVersionDirectory/Package/Manifest locations have only internal issuance tied to a completed verified/prepared target, not public factories. Ordinary TryReserveGrowth rejects them even if a caller copies a location observation from another ticket. The prepared target resource and successful owned-file relocation update this exact typed location plus ResolvedAbsolutePath under their existing scope; they never reinterpret an ordinary staging-relative name as a final path. TargetDirectoryDriver receives the issued resolved ticket in AcquireAsync, so public-only code can obtain its authoritative path without a private Assets ledger or unbound destination argument.

Growth resolves every demand against its *own* operation-bound scope and records the path/ownership before issuing a resource ticket. File/Directory under staging are operation-owned named temporary entries/bytes, NamedDisposition=TemporaryOwned; their TrustedAbsoluteRoot is the frozen staging parent and their initial ResolvedAbsolutePath is beneath the issued private staging root. StagingRoot itself is only a Directory/Metadata create-new demand. Files/directories beneath it have the recorded owned directory ancestry as earlier prerequisites. Registered package/publication location is File/Read, has0 MaximumNamedBytes, NamedDisposition=BorrowedSource, and is borrowed existing disk data; TrustedAbsoluteRoot is its explicit registered parent directory and ResolvedAbsolutePath its exact full registered file. Cleanup never deletes/truncates/renames that borrowed path. Its separate owned Stream/Read or Handle/Read ticket closes the actual I/O handle. All source-index lookup remains bounded/immutable; an out-of-range or wrong-scope/operation ticket is usage failure, not fallback to output root.

None applies to nonnamed streams/handles/processes; their actual carrier/use/handle charges still apply. A successful marker changes only its exact prepared directory/package/manifest to PublishedDestination. Domain cleanup attempts for named resources expose MayDeleteNamedObject=true only for still TemporaryOwned exact acquired objects, and OwnedAbsoluteRoot is their issued private staging root or prepared version directory after partial relocation. Otherwise MayDeleteNamedObject=false/OwnedAbsoluteRoot=null; cleanup drops the borrowed/published identity carrier after its actual declared dependents stop and must not delete that path. TrustedAbsoluteRoot is immutable admission-origin metadata, not a cleanup authorization for a relocated object; the pass's current root/path/disposition and qualified object identity are authoritative. Thus public drivers need no private ledger lookup or inference to distinguish a partially moved temporary from a committed file. A misleading driver cannot manufacture a published transition/cleanup attempt; driver conformance remains required.

Streams/handles are NonPath/Read or Write with a declared same-operation file prerequisite; their root is that resolved parent resource's exact trusted root. A write stream cannot target borrowed registered data, and a sealed file cannot gain another writer. A transient directory-metadata handle is Handle/Metadata/NonPath with the issued owned-directory prerequisites and staging scope, not an undeclared handle inside a Directory cell. Fresh file creation that returns a handle uses the joint public acquisition below, not that directory-metadata exception. Process/Execute is NonPath, rooted in the operation's staging scope with declared files/pipes; the executable/command remains a trusted compiled producer registration, not a scope path or package field. Each Stream/Handle consumes the existing handle slot, Process its existing handle/process slots; driver object/callback/actual-use metadata and reference interest remain separately charged. No new numeric ceiling or bundled undeclared handle is introduced.

Fresh staged File creation reserves its File and separate Stream/Write or Handle/Write cells together. The access demand has that earlier file as prerequisite, retaining the exact cleanup edge (access closes before file deletion). Use [M03 4.6.9.3](11-assets-import-streaming-and-packages.md#m03-4693-quiescent-file-sealing-and-exact-bounded-read-protocol)'s AcquireWithAccessAsync to admit/record both atomically instead of acquiring either through a cyclic ordinary prerequisite. Ordinary File.AcquireAsync handles borrowed existing identity, not fresh file creation with an uncharged transient handle. Supported providers/footprint profiles must qualify all returned transient handles too, not only handles surviving acquisition; no private ledger update, handle bundle or quota waiver is permitted.

ResourceTicket.ResourceScope is the Assets-issued scope ticket; its immutable root observation derives only from this resolved declaration. ResolvedAbsolutePath is a cheap copied current-location observation: initially stage/registered path, then only an authorized prepared publication relocation may change an owned file location. Consumers cannot set paths/roots, move through a caller-written driver field, or supply a final publication destination in ordinary growth. Acquiring validates existing trusted parent/OS capability outside gates. Registered driver conformance must establish that actual handle/object belongs to the issued path/identity, cannot follow an unapproved reparse/alias to unrelated cleanup, and uses same-filesystem atomic publication where promised. Failure/unsupported capability is explicit; no normalization/hash alone establishes native ownership.

Generic BeginUse(resources) remains for admitted actual consumers, but a use containing an owned file/write-stream is writer-class until that file seals. Seal closes its new external uses/writers and joins all recorded writing/native consumers; verification gets only issued sealed-read interests. Existing active uses are not revoked. Dispose/close never waits under a gate and cannot refund a live writer. A borrowed file's only engine permission is read; none of this grants exclusive authority over unrelated caller-owned files.

###### M03 4.6.9.3 Quiescent file sealing and exact bounded read protocol

Memory bytes checked before writing are insufficient to authorize a different final file. Publication verification consumes the actual recorded package/publication files through issued sealed-file tickets. File identity and every read handle have separate existing resource/actual-use charges; the following Assets interfaces extend explicitly registered concrete drivers, not hidden Tool-side authority:

```csharp
public interface IAssetPublicationFileAccessDriver : IAssetRetainedResourceDriver
{
    ValueTask AcquireFileAccessAsync(AssetFileAccessAttempt attempt,
        CancellationToken cancellationToken);
}

public interface IAssetPublicationFileDriver : IAssetRetainedResourceDriver
{
    ValueTask SealAsync(AssetFileSealAttempt attempt, CancellationToken cancellationToken);
    ValueTask RelocateAsync(AssetFileRelocationAttempt attempt, CancellationToken cancellationToken);
    AssetPublicationFilePosition GetPublicationPosition(AssetFileRelocationAttempt attempt);
}

public interface IAssetPublicationReadDriver : IAssetRetainedResourceDriver
{
    ValueTask<int> ReadAsync(AssetFileReadAttempt attempt,
        BufferWriteLease<byte> destination, CancellationToken cancellationToken);
}

// Additional members on AssetResourceTicket (File only).
public Task AcquireWithAccessAsync(AssetResourceTicket access,
    IAssetPublicationFileDriver fileDriver,
    IAssetPublicationFileAccessDriver accessDriver,
    CancellationToken cancellationToken = default);
public Task<AssetSealedFileTicket> SealForVerificationAsync(
    AssetResourceTicket readStream, CancellationToken cancellationToken = default);

public sealed class AssetFileAccessAttempt
{
    public AssetResourceTicket File { get; }
    public AssetResourceTicket Access { get; }
    public void RecordAcquired();
}

public sealed class AssetFileSealAttempt
{
    public AssetResourceTicket File { get; }
    public AssetResourceTicket ReadStream { get; }
    public void RecordSealed(long byteCount);
}

public sealed class AssetSealedFileTicket
{
    public long OperationId { get; }
    public AssetResourceTicket File { get; }
    public AssetResourceTicket ReadStream { get; }
    public long ByteCount { get; }
}

public sealed class AssetFileReadAttempt
{
    public AssetSealedFileTicket File { get; }
    public long SourceOffset { get; }
    public int ByteCount { get; }
}
```

All attempt/sealed constructors are internal with domain/operation/slot/seal-generation association. AcquireWithAccessAsync requires a fresh reserved TemporaryOwned staging File and its exact later Stream/Write or Handle/Write cell, same operation/growth/owned-directory prerequisites, two registered resource-free unique drivers and complete already booked peaks. It installs both driver/recovery roots and inspection/actual-acquisition interests before invoking either driver; ownership inspection is outside gates, validates both NeverAcquired and joins actual inspections on close. The final open/token check and admission atomically mark the pair Acquiring, exceptionally allowing that one not-yet-acquired file prerequisite within this issued pair only. It invokes accessDriver.AcquireFileAccessAsync outside gates; the File driver's ordinary AcquireAsync is not also called.

The actual create-new call installs returned named-file identity in the pre-owned file-driver cell and the returned handle in the pre-owned access-driver cell immediately. The attempt's valid RecordAcquired atomically confirms both after those installations, without allocation/callback or rejection of admitted actual ownership on close. Neither independently acquired slot nor another file/path can substitute. Actual callback exit and both outside-gate ownership observations precede usable completion; helper failure before/after record preserves/reconciles each acquired/uncertain cell and original primary. Reliable absence never adopts/deletes a pre-existing file, while actual partial acquisition stays owned. Both active interests prevent cleanup racing the native call. After callback quiescence, the existing access -> file -> directory dependency orders real handle close before file deletion, without an acquisition cycle or opaque driver-side ledger. Pre-cancel admits no pair. Admitted cancellation waits only this phase's actual callback/inspection/use quiescence, then completes the acquisition helper Canceled while partial resources remain domain-owned/charged. It never waits worker EndExecution or original-operation cleanup; the worker can now unwind/EndExecution and the separate original operation later completes after whole cleanup. Unexpected acquisition/inspection faults follow the same phase boundary with the original primary, not canceled success or resource refund. Duplicate or ordinary independent acquisition of either pair member refuses before callback. Generic cleanup remains the installed per-cell protocol, not a new public release command.

readStream must be this operation's acquired Stream/Read with the file as prerequisite and the registered read-driver type; it is not an uncharged hidden FileStream. Seal installs its active attempt/interest before driver invocation, closes new file/write-stream admission and waits actual previous writers/closing handles outside gates. It uses the public phase-local RequestCleanup Tasks for affected write streams/handles after their writing consumers stop; it does not wait the whole worker whose next step is verification/publication. The participant must leave its own writer uses before awaiting seal; no owner self-join. Phase cleanup failure surfaces with its pre-owned retained failure context rather than minting a seal. All flush/version-snapshot/native operations have their declared resource/use interest. The File driver records stable length/content identity through RecordSealed only after actual writes stop; method does not compute/approve a hash and cannot grant a seal before quiescence. Valid record is callback-free/nonallocating even when close races an admitted attempt. Actual attempt exit and state validation precede exposing the sealed ticket.

No later operation write/acquisition or file substitution is permitted while seal/verification/transfer is live. The actual provider must preserve immutable byte identity through those reads and prepared moves, using a valid private immutable file/snapshot or its qualified versioned read ownership; merely sampling mtime/length is insufficient. For registered external sources whose provider cannot supply that contract, the public bounded read/growth/write path creates an operation-owned staging snapshot, then seals it against the same explicit source pin/index. It does not invent another source or relax expected digests. Such copy/source/destination/handles coexist within unchanged byte/entry/metadata limits. If no supported stable snapshot exists, report explicit unsupported snapshot capability, not successful verification of mutable storage. This is a qualification prerequisite, not a claim current BCL/driver implementations prove arbitrary external-writer exclusion.

File-seal identity/immutable ownership and read-stream lifetime are separate. Verification retains both through actual reads; joined read streams may then close before publication relocation without invalidating the still-owned immutable file seal/certificate. The issued seal cannot perform new reads through its closed stream or rebind another stream; an independent later load acquires its own file/read/seal family. If that provider's immutability depends on a read handle it cannot close before relocation, it must preserve the qualified identity/handle through the move or supply the pre-reserved immutable snapshot; it cannot claim a seal survives a lost protection. Source and final-file cleanup always waits each actual handle/callback; no last-stream close changes a borrowed source into an owned temporary file.

Identical concurrent seal calls for the same file/readStream observe one charged attempt/Task; a different stream or stale generation refuses, not silent rebinding. Pre-cancel after usage validation yields canceled Task/no attempt. Close first refuses new seal; close after attempt admission waits actual driver use and retires the resource only afterward. If close wins the final exposure check, no usable seal is returned and the phase faults with ObjectDisposedException after its actual consumer stops (or follows its already-admitted token cancellation); an already exposed ticket becomes stale for new use. A throwing helper/failed ownership inspection retains its preinstalled resource record and4.7 failure context. A blocked seal remains an actual owned use/nonterminal; shutdown's separate wait may fault.

Assets reads sealed resources only through issued AssetFileReadAttempts into its own prospective-budgeted byte buffer/write lease, with bounded <=16384-byte chunks (reusing the existing I/O chunk envelope, not a new capacity profile). ReadAsync returns0..attempt.ByteCount; invalid count is driver invariant fault. Exact hash/parse loops use checked long offsets/remaining extents and actual returned count. Premature EOF/truncation is explicit invalid input, never zero-filled completion. The driver can retain this issued destination only through its actual call; Task/ValueTask completion must mean I/O ceased and the write lease is safe to release. Cancellation/observer timeout does not free storage while I/O lives. Every driver/registered codec call, hash/parse work, wait and continuation is outside all domain/storage/chain gates. No public Memory<T>/raw handle/pointer bridge.

Package length is128..8GiB from the preserved publication/EPKG ceiling; canonical publication/each decoded envelope still fits its existing256-MiB range and all schema/reader caps. Bundle/transitive-dependency/read-descriptor/metadata/decoded/concurrent-decode/scratch/capture peaks obey existing per-object/domain limits, including MaximumSuppliedDependencies and MaximumReadDescriptors; the package chain is transport, not one256-MiB original-source record. Verified streaming does not promise the8-GiB endpoint fits default logical/backing/staging limits or all maxima simultaneously. Reserve each next validated demand via existing growth/prospective/decode tickets before allocation, retain sources until real decode/read end, and classify shadowed maxima honestly.

###### M03 4.6.9.4 Public native-input binding, common verification and attachment

The semantic kernel still validates sections4.2/4.4's exact unchanged rules/hash DAG. The operation now has callable lower-owned input/result association. NativeRequest's initial captured graph is its admitted input; SourceRequest explicitly reserves/captures its importer-produced graph using the added AssetGrowthTicket.CaptureGraph(source), then binds it. Capture consumes a planned graph/read/metadata reservation and gives it this operation's provenance anchor; it never consumes the detached original or shares an arbitrary other-operation graph.

```csharp
// Additional members on AssetOperationTicket.
public Task<AssetNativeCookBindingResult> BindNativeCookInputAsync(
    StagedAssetGraph graph, BufferReadLease<byte> canonicalNativeContext,
    CancellationToken cancellationToken = default);
public Task<AssetPublicationVerificationResult> VerifyPublicationAsync(
    AssetPublicationVerificationRequest request,
    CancellationToken cancellationToken = default);

public sealed class AssetNativeCookBindingResult
{
    public BinaryRecordStatus Status { get; }
    public AssetNativeCookBinding? Binding { get; }
    public BinarySchemaDiagnostic? Diagnostic { get; }
    public AssetAdmissionRefusal Refusal { get; }
}

public sealed class AssetNativeCookBinding
{
    public long OperationId { get; }
    public AssetOperationResourceScopeTicket ResourceScope { get; }
    public AssetId RootAssetId { get; }
    public ImmutableArray<byte> GraphSha256Digest { get; }
    public ImmutableArray<byte> ContextSha256Digest { get; }
}

public sealed class AssetPublicationInput
{
    public AssetPublicationInput(AssetVersion requiredVersion,
        AssetSealedFileTicket publicationFile, AssetSealedFileTicket packageFile,
        scoped ReadOnlySpan<byte> expectedPublicationSha256Digest,
        scoped ReadOnlySpan<byte> expectedPackageSha256Digest, int sourceIndex);
    public AssetVersion RequiredVersion { get; }
    public AssetSealedFileTicket PublicationFile { get; }
    public AssetSealedFileTicket PackageFile { get; }
    public ImmutableArray<byte> ExpectedPublicationSha256Digest { get; }
    public ImmutableArray<byte> ExpectedPackageSha256Digest { get; }
    public int SourceIndex { get; }
}

public sealed class AssetPublicationVerificationRequest
{
    public AssetPublicationVerificationRequest(AssetPublicationInput primary,
        ImmutableArray<AssetPublicationInput> dependencyPublications,
        AssetNativeCookBinding? nativeInput);
    public AssetPublicationInput Primary { get; }
    public ImmutableArray<AssetPublicationInput> DependencyPublications { get; }
    public AssetNativeCookBinding? NativeInput { get; }
}

public sealed class AssetPublicationVerificationResult
{
    public BinaryRecordStatus Status { get; }
    public AssetVerifiedPublicationTicket? Publication { get; }
    public BinarySchemaDiagnostic? Diagnostic { get; }
    public AssetAdmissionRefusal Refusal { get; }
}

public sealed class AssetVerifiedPublicationTicket
{
    public long OperationId { get; }
    public AssetOperationResourceScopeTicket ResourceScope { get; }
    public AssetVersion RequestedVersion { get; }
    public ImmutableArray<byte> PublicationSha256Digest { get; }
    public ImmutableArray<byte> PackageSha256Digest { get; }
    public long PackageByteCount { get; }
}
```

Result/binding/verified constructors are internal. Request/Input constructors copy bounded immutable nominal metadata but acquire no credits or confer validity. Nonnull values, initialized arrays, distinct full-Version/source-pin dependency requirements, two distinct same-operation sealed file tickets per input, intrinsic full Version and exact32-byte pins are required. Dependency uniqueness is by full required Version and exact allowed source binding, not physical file pair: multiple outputs in one approved package can use the same sealed pair, while every required full Version checks independently. Required semantic closure/ordering/conflicts remain the existing common-kernel graph rules; no extra requirement or array order changes those bytes.

SourceIndex=-1 means fresh owned cook output; nonnegative selects an exact allowed source in this scope. Runtime primary uses selected index0/full source.Version/pins; cook primary uses-1 and its RequestedVersion.AssetId must equal scope.RequestedAssetId. Dependency inputs select approved source entries and contain no extras. A registered-path file must match that selected entry's exact normalized location and pins, allowing a shared file/seal pair where another source index has those same physical paths/pins. An operation-owned staging snapshot is accepted against the selected entry only after its actual sealed bytes/full Version match both pinned hashes and every common semantic join; SourceIndex declares the expected pin, not private copy-origin history. No hidden snapshot-result field is required or trusted. The registered driver/BeginUse/bounded growth/write path supplies the actual snapshot; identical approved bytes do not claim original-source execution. Same physical publication is decoded/hashed once through actual shared file/seal identity. A runtime-requested output may be nonroot in that publication; do not falsely require its AssetId equal Publication.RequestRootAssetId.

BindNativeCookInputAsync requires Cook scope, that exact captured graph anchor and root/request equality before pre-cancel/retention. It books full canonical-context/copy/graph validation/diagnostic/active-use demand before independent context retention and invokes registered catalogue/owner semantics outside gates. The context is exactly canonical `cook/native-context`, with actual graph/profile/catalogue/convention/source/recipe/use/settings/hash joins from4.2; known mismatch/invalid bits reject and unknown active required selection is UnsupportedSchema. Original-source fingerprint recomputation and actual producer/tool invocation remain the separate trusted Tools preconditions/evidence; the common binding does not pretend history is proven by labels. On complete, Assets atomically attaches one frozen context/graph binding to this operation after rechecking open/anchor state; source wrappers may then close independently. No other graph/context can replace it.

VerifyPublicationAsync first validates operation/scope/ticket/registration/mode associations synchronously before pre-cancel or work. Cook requires its exact completed NativeInput binding; RuntimeLoad forbids a cook binding and requires its exact registered source pins. No caller certificate, successful codec status, nominal Version or payload field is accepted as verification authority. It reserves request/result/header/file-use/scratch/decoded/dependency/graph-reconstruction peak before capture, reads the actual sealed bytes and runs all4.4.2 steps. Cook also compares reconstructed graph/context/keys to its bound native input, not just a self-consistent forged output. It verifies complete publication consumption, EPKG layout/ranges/index/codec consumption/hash, every output/envelope/root/payload/profile/catalogue/dependency/full-version/metadata/key/package join, and complete required transitive supplied-version closure. All actual current schemas/typed kernels come from the immutable scope registration. Runtime never invokes a producer/importer, fetches absent original sources or references Tools.

Full-file truncation/trailing data returns InvalidRecord/its exact diagnostic, not a usable prefix. Expected invalid/unsupported semantic data or capacity returns a frozen result with respectively InvalidRecord/UnsupportedSchema (capacity InvalidRecord plus HasValue refusal), null binding/ticket and owned contextual diagnostic; caller maps it to existing Cook Rejected or load fault channels. Complete alone has a nonnull Assets-issued binding/ticket and no diagnostic/refusal. NeedMoreData/DestinationTooSmall are not outcomes of these complete-file/read commands. Unexpected I/O/allocation/invariant failure faults; valid pre-cancel returns canceled Task/no new reservation; admitted cancellation becomes terminal only after actual reads/codecs/subcredits finish. No successful Task carrying Failed/default hides unexpected faults.

Before returning Complete, Assets attaches the full bounded common-verifier model result and exact primary/dependency seal set, registry/catalogue/profile/native-input/full-version pins to the operation and scope; the public ticket exposes only cheap immutable scalar/digest metadata. Private file/registry/input generations and authority are unforgeable. There is no second public AttachResult accepting a caller object. It retains seal/file/dependency interests and all required model/target-validation metadata until prepared ownership/retirement; never release those charges just because the result Task completed. Exact duplicate pending/completed requests observe the same charged operation/task/result; a different request while one is pending or after successful attachment refuses, not replacement/rebinding. After a failed attempt with no certificate, a new corrected immutable file/seal set may retry only after that attempt's actual release.

Close/retirement winning the final attachment check yields no usable capability; retire captured inputs after actual consumers. Attachment first does not allow a later closed operation to start target preparation, though cheap frozen observations remain readable. Cleanup/actual-release invalidates new use of a file/seal generation. No target ticket may be minted from a different operation/registry/destination even if all hashes and path text match. Mutation/writer reacquisition after seal is refused, not repaired by clearing a verified flag. A provider violating the immutable identity promise is an invariant fault and retained failure, not legitimate verification or a target for blind retries.

###### M03 4.6.9.5 Prepared destination ownership, relocation and visibility

PrepareCommittedTransfer no longer takes caller version/hash/file operands:

```csharp
// Additional/replacement member on AssetRetentionDomain.
public AssetCommittedStorageTicket PrepareCommittedTransfer(
    AssetOperationTicket operation, AssetVerifiedPublicationTicket verified);

public sealed class AssetCommittedStorageTicket
{
    public long OperationId { get; }
    public AssetOperationResourceScopeTicket ResourceScope { get; }
    public AssetVersion Version { get; }
    public ImmutableArray<byte> PublicationSha256Digest { get; }
    public ImmutableArray<byte> PackageSha256Digest { get; }
    public string FinalVersionDirectory { get; }
    public string FinalPackagePath { get; }
    public string FinalPublicationPath { get; }
    public Task<AssetPublicationCommitResult> PublishAsync(
        IAssetRetainedResourceDriver targetDirectoryDriver,
        CancellationToken cancellationToken = default);
}

public enum AssetPublicationCommitStatus
{
    Published = 0,
    Rejected = 1
}

public sealed class AssetPublicationCommitResult
{
    public AssetPublicationCommitStatus Status { get; }
    public AssetVersion? PublishedVersion { get; }
    public ImmutableArray<byte> PublicationSha256Digest { get; }
    public ImmutableArray<byte> PackageSha256Digest { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
    public AssetAdmissionRefusal Refusal { get; }
}

public enum AssetPublicationFilePosition
{
    Original = 0,
    Relocated = 1,
    Uncertain = 2
}

public sealed class AssetFileRelocationAttempt
{
    public AssetResourceTicket File { get; }
    public string DestinationAbsolutePath { get; }
    public bool IsManifestVisibilityPoint { get; }
    public void RecordRelocated();
}
```

All ticket/result/attempt constructors are internal. Prepare validates open domain/operation, Cook destination, exact completed verified ticket's domain/op/scope/registration/native-input/model/file/seal generations and no retirement/other target. It uses the *stored checked result*, not another I/O/semantic callback under its gate. Version/hashes/package count/full file set come from that result; a load/read scope cannot issue a publication. Exact repeated prepare for the same successful certificate returns its same charged ticket; wrong op/root/scope/digest/certificate or changed resource is usage failure before resource acquisition. Expected preparation metadata/entry/target-slot pressure throws contextual AssetAdmissionException (maps to execution Rejected before marker), not an uncharged attempt.

AssetAdmissionException is the previously public sealed Exception with immutable AssetAdmissionRefusal Refusal and its exact contextual code/limit/claim/available; its constructor is internal. Same-active-target refusal uses Code=PublicationTargetBusy, LimitName=PublicationTargetOwnership, Claim=1/Limit=1/Available=0 (one create-new ownership slot for this key), not a fabricated byte-budget failure. An OS occupied target yields PublishAsync Rejected/null version/initialized empty digests/nonempty diagnostics and refusal with Code=PublicationTargetExists and the same slot operands. Ordinary preparation capacity reports its actual existing limit. Published has both exact32-byte hashes/full version/no diagnostics/default refusal. Usage failures are synchronous before pre-cancel; after admission an expected refusal returns only after the actual phase consumers stop. Tools maps these refusals to Rejected only after original operation cleanup; unexpected primary/cleanup follows4.7.

The fixed physical edition-1 destination layout is `AbsolutePublicationDestination\v-<PublicationDigestLowercase64Hex>\data.epkg` and `...\manifest.ebin`; the parent version directory is that `v-...` path. These are OS paths composed through the validated provider, not serialized fields. Its exact digest is SHA256 of canonical `cook/publication` bytes already computed, not a random filename/PackageId/current selector. Scope path/operation namespace differences do not change canonical outputs/native version. The manifest is the sole visibility marker. No automatic mutable latest/current pointer or writer-order-dependent winner is added; caller source registration explicitly selects a full version. Byte-identical occupied targets are not silently adopted as this operation's owned files.

Prepare reserves/copies all final paths/verified-model references, target-directory resource cell, directory-driver storage, active publication/relocation/reconciliation/cleanup entries, exact transfer receipt, failure/sample/TCS headers and target-key index before returning. Counts/bytes/site peaks stay within unchanged limits. Under the gate it claims one active lexical key `(normalized scope destination path, publication digest)`, using OrdinalIgnoreCase on Windows and Ordinal on other supported hosts; another live prepare of that key has explicit PublicationTargetBusy refusal. This is the already declared/copied trusted path, not an asserted native directory identity from an uninspected payload or I/O under the gate. A physical alias outside this conservative lexical equality still encounters qualified identity/no-overwrite/create-new checks outside gates; the actual target slot cannot be overwritten/adopted. The key stays while preparing/relocating/quarantined/consuming and is not an unbounded committed cache dictionary. All string/derived-path/4096/relative256 checks and target bookkeeping happen before marker. No new semantic rule/hash approval follows from key admission.

PublishAsync validates its live prepared ticket and registered resource-free targetDirectoryDriver, then pre-cancel. The directory driver is preinstalled in the reserved resource/recovery cell before any actual creation, exactly like4.6.6; target path is the pre-issued final directory, not an ordinary growth location. It must create-new and record acquisition, never claim/delete an existing directory. Existing target in another domain/process yields explicit PublicationTargetExists Rejected after safe cleanup; same-domain busy is refused earlier. The existing target/old version remains untouched even if bytes might be equal. A subsequent application may explicitly load/verify that named version, but this refusal is not Cooked and consumes its already accepted operation ID. This is a deliberate physical publication policy, not nondeterministic byte output.

The target directory and *exact* verified package/publication files remain operation-owned pending resources until marker. Dependencies of relocation/cleanup are fixed and reserved: await phase-local actual verification read-stream RequestCleanup first (not whole-worker retirement); create recorded target directory; relocate package first; relocate publication last. Driver.RelocateAsync receives only the issued attempt/target path and performs a same-filesystem atomic no-overwrite move of that immutable identified file. RecordRelocated installs its actual new location/parent relationship immediately after OS success, before any next fallible step. It cannot attach a different file or copy changed bytes. Partial package/final-directory creation before manifest visibility remains charged named temporary ownership; ordinary cleanup can delete only the exact objects this operation acquired/moved. Separate directory/file/stream records preserve attempt-all cleanup order; no hidden native handle bundle.

All provider/driver calls, identity/volume/no-overwrite checks and joins occur outside gates. Unsupported stable/atomic provider capability is explicit refusal before marker, not a copy/delete fallback presented as atomic success. Package relocation failures stop marker admission; already moved resources retain their new-location records for safe cleanup. GetPublicationPosition after an actual callback exit is outside gates and verifies original/target position of that same sealed file identity, not existence of a filename. Successful move then helper throw before recording can therefore reconcile ownership. Uncertain/throwing inspection retains all candidate location/entry/byte/target claims and reports the failure; never delete unrelated existing targets or refund by guessing.

Immediately before the last relocation, Assets atomically checks operation-open/cancel/verified-seal/target state and admits one noncancelable atomic marker attempt. Close/cancel winning first admits no marker and triggers pre-commit cleanup. Marker admission winning makes subsequent observation cancellation/close unable to revoke that real invocation; the underlying atomic move receives no rollback token. On actual successful manifest relocation, its valid RecordRelocated atomically records visibility, transfers the prepared final directory/package/manifest ownership into the named immutable committed version owner, and freezes the exact version/publication metadata. It is nonthrowing/nonallocating/idempotent for the valid admitted attempt even after close, performs no callback/I/O/wait, and uses only pre-reserved cells. No capacity check, hashing, path parsing, model validation, new owner construction or fallible helper intervenes after OS success. The former unrestricted RecordPublicationCommitted(target) is removed; this exact in-flight actual-location confirmation is its lower-owned replacement.

If the marker driver exits exceptionally after actual move but before RecordRelocated, reliable `GetPublicationPosition==Relocated` for the exact sealed manifest commits the same already prepared owner/metadata; no rollback of a visible manifest. Uncertain marker position retains ownership and keeps the publication phase/original operation nonterminal pending authoritative reconciliation; a separate shutdown observation may fault. It is not Rejected/Canceled or permission to destroy a possibly committed version. No new successful marker starts while that uncertainty/target claim exists. Explicit RetryCleanupAsync also reconciles such entries before choosing cleanup; actual committed state routes final files to their immutable owner, original location routes them to pre-commit cleanup, continuing uncertainty stays retained.

Published result contains exact full Version/32-byte publication digest/no diagnostics, but is only phase completion; original CookAsync remains governed by all consumer/credit/staging cleanup. Cancellation alone after actual marker returns Published/Cooked when cleanup succeeds. Unexpected post-marker execution/reporting failure or cleanup failure must preserve Committed/version/digest in the shared failure envelope; counts reflect only real cleanup failures, possibly0 for a primary post-marker fault, and the original primary/stack is retained. Before marker the unchanged primary/failure/cancellation channels apply. A visible publication cannot be downgraded through RequestRetirement(Rejected/Canceled/Faulted); operation-held committed metadata wins and any fault keeps that context. No successful Rejected/default hides such failure.

At the marker, the exact directory/package/manifest become caller-owned persistent artifacts under the explicitly selected Cook destination. The scope factory/request declaration authorizes that ownership recipient; the pre-issued committed ticket plus phase result is its exact public immutable receipt, including both verified digests/full Version/final paths. No unspecified private cache manager acquires them. Atomically set NamedDisposition=PublishedDestination and remove only the transferred NamedStagingBytes/NamedStagingEntries; the existing directory/file recovery cells, driver/use/handle/model/target metadata remain charged until their own actual retirement. Safe cleanup of those carriers must obey MayDeleteNamedObject=false and cannot remove the published objects. Final files persist as caller data, not an unbounded cooker-held disk/metadata registry or a system-wide free-space guarantee.

Domain/cook Dispose therefore never deletes committed versions or invalidates independently registered/read pins. Native read handles/use/driver cells must actually close/release, old stage/scratch/verification/root claims retire normally and leftover cleanup faults retain the operation/quarantine. A separately elected AssetStore/cache owner may retain independently budgeted state/pins under its documented policy; merely registering a source does not transfer caller-owned disk deletion authority. Public retained receipts are ordinary bounded per-object caller metadata. Independent Core ready-version snapshots still hold their own quota/metadata/backing as4.6.6-7 requires. Until actual marker confirmation, the exact prepared directory and moved files remain TemporaryOwned with their named claims, including during uncertain relocation; no speculative transfer/refund occurs.

###### M03 4.6.9.6 Complete public-only route and API replacement consequences

The Tools native request and both source-byte variants now require the explicit scope operand instead of an unscoped final directory string:

```csharp
// Members on Content.Tools.AssetCookRequest; these remain Tools-owned.
public AssetCookRequest(AssetId assetId, StagedAssetGraph graph,
    NativeCookSpecification nativeSpecification, NativePackageSpecification packageSpecification,
    AssetResourceScope resourceScope);
public AssetCookRequest(AssetId assetId, BufferChain<byte> sourceBytes,
    ImportRecipe recipe, SourceConvention sourceConvention,
    ImmutableArray<BufferChain<byte>> dependencyBytes, AssetResourceScope resourceScope);
public AssetCookRequest(AssetId assetId, BufferReadLease<byte> sourceBytes,
    ImportRecipe recipe, SourceConvention sourceConvention,
    ImmutableArray<BufferReadLease<byte>> dependencyBytes, AssetResourceScope resourceScope);
public AssetResourceScope ResourceScope { get; }
public string OutputDirectory { get; }
```

OutputDirectory is the normalized Cook scope destination. Constructors require Kind Cook/RequestedAssetId==assetId, explicit frozen compatible catalogue/registration and native graph-root equality when present; mode/owner/argument checks precede cancellation at CookAsync. Existing source/native/specification/credit/default semantics remain. String-only overloads are not part of this contract: they cannot create the verification/source/destination association. A caller constructs ForCook(id,directory,registry,sources) first; there is no guessed registry/global-domain/implicit path fallback. This is a justified pre-freeze API correction over unimplemented scaffolds, not a runtime compatibility rewrite.

Native public-only sequence is: freeze typed codecs/sources; create scope; construct request; use request facts including scope -> atomic TryBeginOperation -> CaptureGraph -> CommitOperation. Source route captures source chains, runs its actual registered importer later, reserves/captures its graph through growth and binds the same requested root. Both invoke BindNativeCookInputAsync against canonical context; stage bytes through resolved growth/resource/write-use tickets and public joint File/access creation; close actual writers, acquire charged read streams and seal package/publication. Build explicit pinned primary/dependency verification request from those sealed tickets and invoke VerifyPublicationAsync. Complete yields only an Assets-issued operation-bound result; expected failure maps to established execution rejection after safe cleanup. Prepare(operation,result.Publication), then PublishAsync(installed directory driver) performs scoped recorded moves/marker. On success/cancellation/fault, the worker ends actual helper/resource/Core uses, requests retirement nonblockingly and calls EndExecution in its real body's finally; it never awaits its own original Completion. The separate cooker observer awaits Completion/whole cleanup and produces the original receipt/fault/cancellation. The caller never supplies an Assets root setter, verifier callback, private ledger update or fabricated committed receipt.

The returned committed target paths/full Version/package digest/publication digest let caller construct the exact public AssetSourceRegistration and RegisterSource(makeCurrent) for a new frozen AssetStoreRegistration. No content URI/path field is needed. Runtime creates its explicit cache/load scope from the chosen registered source and invokes the same ticket/read/seal/verification pipeline (or a qualified bounded source snapshot), with no cook binding/transfer permission and no Tools dependency. It retains the requested dependency closure into an independent ready snapshot before old operation retirement; actual World/scene/persistence consumers remain their existing later acceptance observations.

Public-only fixtures must compile every constructor/interface/member/type root used by this sequence against reference assemblies without friendship/reflection/generated test-only setters. NativeAOT must root the actual registered concrete file/read/directory drivers and common-codec generic closures. Design coherence has received independent ACCEPT — design only. Actual provider identity/immutable seal/atomic move, charged peaks/quota release, executable pipeline/fixture bytes and AOT evidence remain pending.

##### M03 4.6.10 Helper completion, worker retirement and cleanup progress

This operational completion contract adds no schema, API signature family, queue or admission domain. Phase-local close remains runnable even when whole close came first. All4.6.9 destination/source/seal/native-input/verifier/transfer authority and actual-marker rules remain. A true native-use or uncertain-marker hold is not declared complete merely to make a test pass.

**Completion scopes and ownership.**

| Existing public observation | Actual completion boundary and forbidden dependency |
|---|---|
| AssetResourceTicket.AcquireAsync / AcquireWithAccessAsync | Its admitted acquisition callback, ownership inspections and scoped active uses have actually exited. Successful state may expose acquired tickets; cancellation/fault exposes no successful acquisition result. Retained physical/uncertain cells remain charged and reachable through the domain. The helper never awaits the original operation's worker, Completion, whole cleanup or domain drain. |
| SealForVerificationAsync / BindNativeCookInputAsync / VerifyPublicationAsync / PublishAsync | Their own admitted callbacks/reads/codecs/inspections and required safe resource-local closes have finished before a phase result/fault is exposed. No phase awaits its own executing-worker interest or whole-operation cleanup. Existing attachment/marker/real-uncertainty rules still decide success and authority; helper fault/cancellation cannot retract a marker or refund live storage. |
| AssetResourceTicket.RequestCleanup | Only its own closed resource and required dependent closure, actual callback/inspection exits and confirmed release/failure. It stays legal after domain/operation close. Waiting for the requesting worker EndExecution or returning some other selected pass's result is forbidden. |
| AssetWorkerTicket.EndExecution | Called once in the worker's actual body's finally after every admitted helper/native use and its borrows end. Ends its execution interest/signals eligibility, not its resource or operation ownership. No cleanup wait occurs in this call. |
| AssetOperationTicket.RequestRetirement / Completion, original CookAsync/load Task | RequestRetirement records intent and returns the original completion observation without joining. Only that separate completion observer awaits real worker exit, remaining consumers and whole resource/credit cleanup. Quarantine faults and stuck-consumer nonterminal ownership remain4.7's; no helper terminal implies Cooked/Rejected/Canceled. |
| Domain RetryCleanupAsync / DisposeAsync / WhenDrained | Whole pass/drain observations can await the selected real ownership, but cannot possess the dispatch authority while waiting a worker/use. Workers never await their own whole pass/drain before EndExecution. Existing close/retry/timeout channels remain. |

Before admission, usage/owner/mode/pre-cancel precedence remains unchanged. After admission, an unexpected callback/ownership-inspection primary fault retains its original exception/stack and wins over a simultaneously signaled cancellation. If actual callback/inspection completed without that fault, the helper's final gate check observes its admitted token: observed cancellation returns a canceled helper Task with the original token after ended phase-use release; otherwise close winning exposure faults with ObjectDisposedException, otherwise confirmed acquisition succeeds. This check does not discard acquired cells on cancel/close. A callback OCE is cancellation only for the admitted requested token; unrelated unexpected OCE retains the established fault channel. Helper observers are published asynchronously outside gates after the phase's last real use. Inspection failure/unknown acquired state retains conservative ownership; it is not changed to NeverAcquired.

The canceled/faulted acquisition helper does not synchronously delete/close the acquired File/access pair or invoke a whole unwind before returning. It leaves those already installed cells and their named-byte/entry/handle/metadata claims with the domain. Only ended helper use/inspection credits and reliably unused provisional reservations can release at that boundary. A later phase close may release safe resources before EndExecution, or general cleanup may release them afterward; either path requires actual confirmation. The original operation's failure envelope later combines that primary/cancellation intent with any real cleanup failures. A phase fault and a whole-operation cleanup fault are distinct observations, not duplicate invented cleanup samples. Owned input-node/graph/request/operation credit independence remains exact.

The worker's executable sequence is: await helper phase; observe/record its actual outcome; leave its resource/Core use scopes; record the exact AssetOperationResult through nonblocking RequestRetirement; invoke EndExecution in finally after its last use; return to the worker loop. A separate pre-reserved cooker/store completion observer handles the returned Completion Task. Domain close records stop/close intent for an executing body, not a prematurely frozen successful/canceled body result. That worker's one final RequestRetirement report supplies its actual primary/cancellation or committed state before EndExecution; the pre-existing closed flag cannot erase a late observed primary. Queued work never taken by a worker can retire as canceled directly. An explicit final RequestRetirement result is still immutable, exact repeated reporting is idempotent and conflicting reports are usage failure; valid execution does not report a final result and then run more body work. Cleanup failures are later separate owned observations, not mutable replacement of that body result. No second accepted operation or lossy canceled fallback is introduced. No EndExecution is fabricated merely because a token/observer became canceled. Existing valid-state bookkeeping is callback-free/nonthrowing; unexpected driver/invariant faults still surface. Worker reuse after real EndExecution does not consume another pending slot for the old cleanup; old operation/resource counts remain until true retirement.

**Shared cleanup progress.** One domain coordinator and the already bounded records/marks/attempt/header storage perform cleanup; no second unbounded cleanup queue or numeric ceiling is introduced. Whole close records all work and parks each operation's WaitingForWorker/use eligibility in its existing cell. A parked operation has no cleanup-executor/selection-lease/cursor ownership. Stop dispatch and quiescence observation occur outside gates and cannot await that worker inside the sole selection quantum. Eligibility changes wake the coordinator through pre-reserved state; no callback, native inspection or user continuation runs under its gate.

Every explicit RequestCleanup marks its existing resource CloseRequested generation, closes future use admission and enables resource-local eligibility even while its operation is WaitingForWorker. Eligibility requires that resource's actual acquisition/inspection/use counts and required dependent resources stop, independent of the whole worker interest. Resource access is permitted only through the existing admitted resource/use/helper interests; a mere worker assignment is not authority for an untracked raw-handle use after a phase close. The coordinator skips ineligible entries, services eligible requested entries in the established reverse dependency/acquisition order, and parks without holding dispatch when none is eligible. It never waits worker EndExecution before servicing an otherwise safe writer/read-stream close needed by that worker's finally.

Close/wake/generation/attempt/phase-TCS/failure/continuation sites and parked-worker/body-result observations are pre-reserved qualified Assets storage, included in the same local/aggregate metadata/operation limits and protected cleanup reserve. No late close allocates an uncharged task/header/queue or computes an unqualified footprint under a gate. The exact compiled storage-site profile/producer hashes and their measured cost must later be captured/requalified; no candidate coefficient/digest or sustaining-capacity result is invented here. Pending Task state alone is not actual callback/use liveness and cannot substitute for either real-use retention or an eligible dispatch decision.

Late close during selection/cleanup marks the same pre-existing cell under the gate. If that resource already has an attempt in flight, its close Task observes that one actual attempt; no second callback is installed. If it was not selected, the dirty/wake flag forces eligibility rescan in the current or next dispatch quantum, regardless of the earlier pass's frozen observation set. Selection readiness, clearing the wake flag and deciding to park are one gated transition: a close/use-exit/worker-exit either precedes that recheck and is seen, or follows it and schedules another quantum. An exhausted scan cannot erase a concurrent late close. Do not allocate/copy an uncharged pending resource list. Expected eligibility waiting is not a running driver callback or an executor lease.

Per-resource close Tasks belong to their recorded resource/close generation, not a global pass. After callback exit and outside-gate authoritative inspection, the release/refund state installs under gate; only then is that resource's Task completed outside gates. Required dependent release must also have occurred. Another resource/pass succeeding or failing cannot complete/fault this Task by proxy. A failed cleanup/uncertain release faults that resource's close with its already reserved exact contextual failure and retains its charges; unselected or still-ineligible requested resources remain pending. An unexpected reporting failure after confirmed physical release is still that resource's fault observation, not permission to release another resource. Original operation/pass observers separately aggregate their selected actual failures in the existing deterministic sample order.

Concurrent/repeated RequestCleanup before resolution returns the same per-resource Task; after its stopped-retained failure, repeated calls observe the frozen fault and perform no automatic retry. Explicit RetryCleanupAsync owns a new qualified retry observation; successful later actual release updates the cell and subsequent close calls observe idempotent completed release, while the earlier faulted Task remains faulted. A first close arriving after confirmed release succeeds immediately; one arriving while the selected attempt is still active must wait that resource's actual exit/confirmation. A retry/pass's frozen selected set is its result scope, not permission to exclude a newly requested eligible phase close or to declare it successful on pass completion.

Registered stop/cleanup/inspection drivers must not await worker EndExecution, the containing phase Task, whole-operation cleanup or domain drain when that wait would be required for their own actual exit. Affine cleanup must run on a permitted live control/owner context not blocked by the phase caller. The built-in coordinator is independent of the cook worker's execution token; no task-completion options/ConfigureAwait(false)/thread-count increase substitutes for these ownership/order rules. Real blocked native calls still retain their original Tasks/ownership and only a separate bounded observer may fault. This correction proves removal of the protocol cycle, not eventual completion of an unresponsive native API.

Rejected alternatives: awaiting whole cleanup inside the helper preserves the demonstrated cycle; marking EndExecution before actual use ends falsifies safety; fire-and-forget ownership or cancel-time refund loses acquired resources; mandatory inline phase acquisition cleanup delays primary observation behind unrelated cleanup and is unnecessary with pre-owned cells; adding another worker/executor merely hides the cycle under a different saturation schedule. The selected retained-owner phase boundary plus eligibility-driven shared dispatch is sufficient without new public methods, limits, schemas or acceptance IDs.

#### M03 4.7 Credit release versus staging cleanup failure

##### M03 4.7.1 Valid Core releases are nonthrowing

For valid privately created registrations under the built-in allocator, BufferReadLease/BufferWriteLease/BufferChain/OwnedBuffer/UnmanagedBuffer Dispose and their actual-use-finally credit decrements are guaranteed nonthrowing/idempotent. Admission/Retain/Use validation and user callbacks may throw; credit release does not invoke user code, allocate, wait, publish a task under lock or perform checked arithmetic that can overflow on a valid decrement. The lifetime gate verifies the exact owner once, decrements each credit at most once and frees only at closed/no-owner/no-lease/no-active-use. NativeMemory.Free versus AlignedFree is fixed at allocation and called once with that valid address.

This is the valid-release contract for the built-in public allocator and lifetime gates. It does not assert the internal INativeMemoryOperations interface prevents a deliberately throwing fake Free, prove invalid native pointers safe, cover process-corrupting failures, or permit a plugin/native-resource releaser in the buffer path. Such fakes/invariant violations are outside valid buffer conformance and cannot be used as evidence that ordinary NativeMemory release throws. Quota bookkeeping is also valid-state nonthrowing, fixed-capacity and callback-free. Conformance must establish it before enabling domain admission; no broad catch may turn an unexpected library invariant into release success.

Thus node/graph/request constructor failure needs no orphan-credit fallback owner: attempt all acquired valid Core releases in reverse acquisition order, release every reservation, then propagate the primary exception with its original stack. A normal callback failure plus valid deferred credit cleanup propagates the callback exception, not an invented AggregateException. Repeated Dispose never re-releases a slot or silently retries other cleanup. Active children delay WhenReleased/physical free and the charge remains. Graph/request constructors and domain initialization acquire no files/processes, so they cannot produce the filesystem cleanup problem without a prior domain recovery record.

##### M03 4.7.2 Files/processes have a retained failure owner

Before an admitted load/cook opens/creates a file, pipe, process or staging directory, its explicit existing domain owns a reserved operation/recovery record and a complete worst-case cleanup-entry/metadata/staging plan. Each successful acquisition is installed in that record before the next fallible operation. A local helper constructor failing before it returns an owner therefore leaves an accessible domain-owned record, not an orphan local handle. All resource IDs are domain-local operation/resource ordinals with [M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff)'s typed scope/name/registered/prepared location and disposition; no raw handle/pointer is public.

The callable installation/inspection/actual-release/pass authority is exactly [M03 4.6.6](11-assets-import-streaming-and-packages.md#m03-466-callable-assets-admission-participation-and-retirement-protocol)'s registered resource-driver and opaque-ticket protocol. Domain callbacks are outside every engine gate. The nonthrowing valid-release direction in4.7.1 is an obligation for the new ticket/quota bookkeeping, not source qualification of machinery that does not yet exist.

States are Reserved -> Acquiring/Running -> Quiescing -> Cleaning -> Released, with Quarantined for stopped-but-unreleased resources and RetainedLive for still-consuming resources. Only actual release removes its remaining carrier/use/handle/metadata charges. [M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff)'s actual marker may separately transfer named temporary-byte/entry ownership to the declared caller destination; it does not release carriers or permit final-path deletion. On cleanup failure the owner closes new load/cook/preparation admission, retains all failed/live entries, and exposes exact retained bytes/entries/state through its snapshot and contextual failure. Safe Core payload credits can release after their actual users finish even when an unrelated file deletion still fails; filesystem staging bytes/handle/operation/metadata charges remain. A live process/pipe/native/file-I/O consumer keeps every range/path/handle it may access; do not delete its staging or make its original task terminal merely because a timeout expires.

Cleanup first requests stop/cancellation of only this owned invocation and observes actual-consumer eligibility. Whole worker/use joins are parked outside dispatch as4.6.10 specifies; safe explicit phase-local resource close can progress before worker EndExecution. Then attempt all independently safe selected resources in reverse dependency/acquisition order: streams/handles, named uncommitted files, then empty owned directories. Failure of one does not stop cleanup of safe siblings. A resource whose prerequisite is still live remains RetainedLive, not falsely attempted/released. Delete/close/exit confirmation is authoritative; disappearance by a successful idempotent missing-file check may complete that named item. Retrying a completed item does not re-close a reused handle, repeat process termination against a different PID or delete another operation's path.

Failed entries retain ownership and charge in the original domain until an explicit cleanup pass succeeds. Closing that domain never clears its ledger to report zero. RetryCleanupAsync remains legal after close, takes a frozen selected set of all safe retained entries for its observation, attempts all, and retains each failed item. One domain coordinator owns dispatch; concurrent callers of that same retry observe its same Task. The frozen retry set holds no executor lease while joining worker/users and excludes no late eligible phase-close work; every resource RequestCleanup keeps its own actual-release Task per4.6.10. Once a pass is admitted its token is observation-only; pre-canceled valid calls return canceled Task with no pass. A pass completes successfully only when its selected entries and actual consumers are released; otherwise it faults with the retained failure details. A blocked underlying cleanup call leaves that pass/operation nonterminal and owned; an independent bounded shutdown observation can fault, not free it.

DisposeAsync closes once, joins/cleans and observes the configured wait. Timeout/failure faults its drain observation with retained ownership; active original operations keep running/nonterminal as necessary. Repeated DisposeAsync observes the same close/drain outcome and performs no automatic retry. Explicit successful RetryCleanupAsync updates the drain state; a later DisposeAsync may then report completed drain while earlier faulted observations remain faulted. No finalizer walks staging/processes or waits; outstanding tasks/recovery records retain the domain until real cleanup, and a caller still must explicitly close/retry it.

##### M03 4.7.3 Exact failure channel and low-memory ownership

Assets owns `AssetCleanupException : Exception`, shared by runtime and Tools without an Assets -> Tools edge. Its read-only public observations are: `long OperationId`, `AssetOperationOutcome OriginalOutcome` (None0/Rejected1/Canceled2/Faulted3/Committed4; operational only), `Exception? PrimaryException`, `ImmutableArray<Diagnostic> PrimaryDiagnostics`, `int CleanupFailureCount`, `int CapturedCleanupFailureCount`, `AssetCleanupFailure GetCleanupFailure(int index)`, `bool HadRetainedOwnershipAtFailure`, `AssetVersion? PublishedVersion`, and `ImmutableArray<byte> PublicationSha256Digest`. AssetCleanupFailure is a copied operational record of resource ordinal/kind and the original Exception; kind is File0/Directory1/Stream2/Process3/Handle4, not a wire enum. Bounds checks on GetCleanupFailure are conventional; copied fault observations do not grant disposal authority. OperationId0 identifies a domain-wide shutdown observation; accepted operation faults use their positive ID. PrimaryDiagnostics reuses the already charged immutable bounded validation/refusal report, not another unbounded formatted copy.

The operation's cleanup exception header, separate shutdown-observer header and fixed16-sample storage are reserved/created before resource acquisition. During private cleanup fill primary/outcome, exact total failure count and the first16 original cleanup exceptions in deterministic selected-resource order; freeze before publication. An observer failure never freezes/reuses the original operation's still-private mutable failure header. There is no allocator/user formatting in the release loop or loss of a primary OutOfMemoryException while trying to construct an aggregate. One primary + one cleanup exposes both original exceptions/stacks. For more failures, attempt all, retain each exact failed-resource state, count all, expose first16 with explicit captured/total counts and preserve the failed set for retry; do not claim all raw exception objects fit an unbounded AggregateException. Extra temporary I/O exception objects become unreachable after their exact state/count is recorded. Caller-authored exception object graphs are ordinary caller data, not a claimed bounded engine-created message graph; engine diagnostic/header allocations themselves stay charged.

For original operation Completion/CookAsync before irreversible publication, if there is no cleanup failure an unexpected primary fault is rethrown unchanged, normal expected semantic refusal returns Rejected and cancellation returns canceled Task only after successful actual cleanup. [M03 4.6.10](11-assets-import-streaming-and-packages.md#m03-4610-helper-completion-worker-retirement-and-cleanup-progress)'s inner acquisition helper has a different boundary: its primary/cancellation observation follows its own actual callback/inspection/use quiescence, with partial acquired resources still domain-owned/charged; it does not await the worker/whole cleanup. If cleanup later fails after consumers stop, the original task is Faulted with AssetCleanupException, never Rejected/Canceled: OriginalOutcome preserves the planned refusal/cancellation/fault and PrimaryException preserves its exception where applicable. Successful partial cleanup releases those items only; remaining entries/quarantine stay charged. If a consumer remains live, the original operation stays nonterminal and a separate shutdown/observer timeout reports the pre-owned failure context; no aggregate can authorize freeing it.

After the manifest marker, cancellation alone cannot retract publication and successful cleanup returns Cooked. Unexpected post-commit cleanup failure instead faults explicitly with OriginalOutcome=Committed, the already PublishedVersion and32-byte PublicationSha256Digest; the committed version remains visible, and no Rejected/Canceled or rollback is claimed. Pre-commit cleanup faults have PublishedVersion=null/empty digest and preserve the old visible version. This deliberate exception detail reconciles the irreversible commit with chapter02/11's unexpected-I/O fault channel, rather than silently returning success for failed cleanup.

[M03 4.6.9](11-assets-import-streaming-and-packages.md#m03-469-destination-sealed-resources-and-verified-publication-handoff) additionally makes post-marker execution/reporting faults explicit: use the same pre-reserved AssetCleanupException envelope with OriginalOutcome=Committed/exact version/digest/PrimaryException retaining the original fault and stack. CleanupFailureCount/CapturedCleanupFailureCount are0 if there was no cleanup failure; never invent a cleanup exception merely to fill samples. This is the narrowly required irreversible-publication context exception to ordinary primary rethrow, so a caller cannot mistake an unexpected post-marker fault for an unpublished operation and blindly retry. The original operation becomes terminal only after its actual consumers/credit cleanup; an uncertain real marker remains retained/nonterminal until reconciled, with separate bounded observer failure.

RetryCleanupAsync reports a fresh pass failure header reserved before the pass; it does not mutate the frozen original operation exception/task. A constructor cannot acquire stage resources until that pre-owned header/record exists. Metadata/quarantine exhaustion therefore refuses *before* acquisition. Core failure injection is confined to acquisition/metadata stages; the primary-plus-cleanup traces use filesystem/process failures, not a claim that valid Core free throws.

## 4. Native binary package format EPKG2

### Bounded accelerated-read boundary

CPU file I/O plus validated None/Brotli decode remains required on both OS targets and for writes. DirectStorage is an optional private Windows read adapter, not a public ticket model, mandatory package codec or implied Linux/Vulkan parity. Selected accelerated providers report implemented capability/version/license and obey the same asset admission, integrity, cancellation and readiness rules.

Reserve queue slots, source/destination bytes and retirement credits before provider enqueue; a full provider may block, so enqueue/waits run only on bounded I/O workers, never host/render/audio callbacks. Request IDs preserve association under out-of-order completion; queue submission/order is not completion or publication. Independently compressed request regions require an explicitly versioned chunk format with bounded decoded extents and integrity, not arbitrary splits of a Brotli stream. The baseline codecs/container remain unchanged.

A GPU-destination path must specify validated extents/layout/format, decompression capability, final decoded-integrity verification and exact copy/transition completion before GPU Ready. If it cannot verify that content, choose an explicit CPU verification/staging path or report Unsupported, never weaken admission. Corruption, cancellation and device loss retain consumers until actual completion and cannot publish partial assets. Writes and unsupported accelerated cases use the selected ordinary CPU path only through explicit capability policy, not an unreported failed-read success.

Load `EPKG2` through [10](10-binary-data-and-authoring-formats.md#3-epkg2-binary-index)'s validated 128-byte package header, binary `EIDX` tables, and typed `EBIN1` payloads. That chapter owns canonical ordering, extents/counts, trusted package budgets, digests/profiles, None/Brotli codecs, bounded decompression, and `EPKG1` migration. Validate each extent/count before using it to reserve a bounded I/O/decompression/staging buffer; complete cross-record/digest/schema validation before publishing a semantic asset or activating it in a World. Cancellation leaves no admitted half-asset and releases operation-owned staging only after consumers stop. Authoring JSON/XML is not reparsed as the runtime index.

Untrusted downloaded packages are inert bounded asset/data bundles: separate validation profile (max 16 MiB package,1024 entries,4 MiB decoded/entry,32 MiB aggregate, depth 32), no C# assembly/HLSL/native DLL/raw pointers/arbitrary file paths/network URI or renderer compute program. Trusted author shader/engine build outputs use distinct signed/local admission channel. Certificate is evidence of validator version/result, not executable trust.

Import extraction never writes attacker-named paths; GUID-addressed private cache only. Logical resource names are normalized identifiers and are not concatenated into host filesystem paths. SHA256 ensures content integrity, not provenance/authenticity; publisher signatures/trust policy are application-owned and explicit.

## 5. Validation

Cases cover deterministic cache keys/version pinning, missing dependency/cycle/rename, sparse accessor overflow, invalid mesh/normal/weight/color metadata, atomically failed import, canceled load, pool/lease survival, pressure without evictable bytes, package ranges/digests/decompression bombs/path injection and trusted-versus-inert policy. `Editor` and CLI use the same validators; preview success cannot override player admission.
