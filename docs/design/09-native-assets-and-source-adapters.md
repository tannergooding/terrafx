# Native asset contracts and source adapters

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

The engine-native schema is authoritative. A source format is an adapter into that schema, not an engine scene model, preferred coordinate system, runtime dependency, or reason to inherit source API naming. [11](11-assets-import-streaming-and-packages.md) owns import/package behavior; [07](07-spatial-coordinates-and-indexing.md) owns spatial units and cells.

## 1. Native semantic records

Every native record has a registered schema GUID/version, a positive signed 32-bit AssetId unique within its selected content set, and a bounded validated payload. The content set supplies the coordinate/unit/numerical profile. The cook format encodes named scalar fields and explicit component/row order; CLR/BCL struct layout is not the wire format.

| Native record | Required semantics / owner |
|---|---|
| `MeshAsset` | Vertex attribute descriptors, explicit streams/strides, indexed primitive groups, material slots, conservative local bounds, optional skin binding and authored LOD partitions; [21](21-2d-and-3d-rendering.md), [18](18-shaders-and-material-pipeline.md) |
| `MaterialAsset` | Registered shader/pass/layout identity, typed linear/color/texture/sampler values, alpha/depth/cull policy and required capabilities; core metallic-roughness is one engine material profile |
| `SkeletonAsset` | Stable joint IDs, topological parents, bind-local poses, inverse-bind row matrices and declared root-motion binding; [20](20-animation-and-deformation.md) |
| `AnimationClipAsset` | Native `TimeSpan` key times, named target tracks, interpolation kind, tangent units, event records, loop/root-motion policy and bounds |
| `CameraDefinition` | Projection kind, engine unit distances, radians, optional aspect override, viewport/layers/clear/order; no source `xmag`, `ymag`, or extension JSON |
| `SceneAsset` / `PrefabAsset` | Stable entity IDs, schema components, root spatial positions, child-local hierarchy, and typed AssetId references/overrides; [16](16-scenes-transforms-and-cameras.md) |
| `CollisionShapeAsset` | Explicit Physics-owned definition, primitive schema version, selected spatial profile/settings, and dimensional `ShapeStatistics2D/3D` with canonical `ShapeMeasure` pairs; not an automatic collision interpretation of a visual mesh |
| `TextureAsset`, `AudioClipAsset`, `FontAsset` | Explicit color/channel/sample/encoding/rate/licensing metadata and bounded payload; their subsystem authorities retain validation |

Native formats can be authored directly, generated procedurally, or produced by any admitted adapter. Engine-only scenes/components/events/materials/root spatial positions need no glTF representation. Runtime asset loading accepts registered cooked native schema versions, not source JSON object models.

M03 collision content selects the fixed `physics/primitive2` or `physics/primitive3` schema version and [23's geometry/statistics contract](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics). Area/volume is a canonical significand-plus-binary-exponent pair, not a single binary32 measure. A geometry context establishes neither PhysicsWorld/body/material admission nor source history. Loading validates the selected content-set profile/settings, revalidates copied definitions, recomputes statistics and requires canonical equality before use. Changing settings is handled by the application/content-build workflow; the runtime does not retain a generic migration catalogue. Assets remains Core-only transport; Physics-aware typed adapters are supplied by their permitted higher-layer owners.

M03's selected native records are specified in [10](10-binary-data-and-authoring-formats.md). A collision primitive requires Definition, Statistics and Profile; statistics include the selected Settings but no copied algorithm identity. Circle/sphere topology fields are absent; Box2 vertex/edge are 4/4 with FaceCount absent; Box3 is 8/12/6. Mesh-processing fields are absent for every primitive. Required analytic integers, single-float SI area/volume and an additional exact-SI cutoff are not this contract. Loading checks the registered schema version, selected profile/settings and recomputed canonical values; runtime loading does not require original source bytes, cook receipts or Content.Tools.

## 2. Adapter boundary

An import request selects an available source adapter and typed options. It supplies bounded source bytes and declared source conventions: basis, unit scale, vector/matrix convention, winding, texture orientation, time domain, and required extensions. Importers produce validated native assets plus conversion/approximation diagnostics. They do not create live entities, native GPU resources, physics proxies, or voices.

The content build validates adapter output using the same schema-version-owned behavior as direct native authoring, then emits the deterministic package for the selected content set/profile. A failed conversion/cook does not replace the previous package. Source names and optional inert metadata may be retained within budget but cannot change runtime behavior implicitly.

The build declares the source files, relevant options, adapter/tool version and target profile as inputs to the outputs they affect. Up-to-date checks invalidate the affected cook outputs when an input changes; those build inputs are not serialized into the runtime package. Conversion is applied exactly once at this boundary. Loading a native asset does not repeat reflection, unit scaling, or source-camera adaptation.

Unsupported source features produce specific admission diagnostics. An explicit approximation is permitted only where the native owning subsystem admits it, with retained settings, affected counts, and recomputed bounds. Importer convenience cannot expand a physics shape set, material capability, animation influence budget, or executable-content trust boundary.

## 3. glTF adapter

glTF 2.0/GLB are admitted source choices, not native record definitions. [11](11-assets-import-streaming-and-packages.md#2-concrete-cooking) owns the supported subset and complete source validation/conversion: source meters per unit, basis/axial signs, winding/tangents, hierarchy/skins, key times/interpolation, and camera restrictions. All conversion occurs once at that adapter boundary; native loading applies none of it again.

Direct native authoring and the glTF adapter must produce equivalent canonical geometry/camera/animation behavior for equivalent authored content. Their package bytes may differ when their effective inputs differ; behavioral equivalence does not require byte-identical source files. Importer/tool identity is a declared build input, not runtime asset identity.

## 4. Acceptance obligations

| ID | Required observation |
|---|---|
| AS01 | A native 2D/3D scene, material, clip, collision asset, and spatial root can be authored/loaded without glTF types or an installed glTF importer |
| AS02 | Two source adapters map equivalent content to the same native semantics; runtime schemas contain no required source accessor/extension objects |
| AS03 | Reflection, unit scaling, winding, tangents, inverse binds, camera axes/planes/aspect and cubic derivatives are converted exactly once |
| AS04 | Native direct data bypasses source conversion but passes the same cook validators and package bounds |
| AS05 | Unsupported required source features and unapproved approximation reject before package replacement; the previous package remains available |
| AS06 | Declared build inputs invalidate affected cook outputs; runtime package identity is the combined package checksum, not a source/tool receipt chain |
| AS07 | Native time resolution collisions, sheared unsupported poses, invalid cameras, influence limits and degenerate geometry have explicit diagnostics |
| AS08 | Native schema-version selection and inert optional metadata preserve the API/trust boundary without imposing a source format on consumers |
