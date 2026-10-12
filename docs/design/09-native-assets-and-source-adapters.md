# Native asset contracts and source adapters

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

The engine-native schema is authoritative. A source format is an adapter into that schema, not an engine scene model, preferred coordinate system, runtime dependency, or reason to inherit source API naming. [11](11-assets-import-streaming-and-packages.md) owns import transactions/packages; [07](07-spatial-coordinates-and-indexing.md) owns spatial units and cells.

## 1. Native semantic records

Every native record has a registered schema GUID/version, stable asset GUID, target numerical/unit/coordinate profile, dependency versions, and bounded validated payload. The cook format encodes named scalar fields and explicit component/row order; CLR/BCL struct layout is not the wire format.

| Native record | Required semantics / owner |
|---|---|
| `MeshAsset` | Vertex attribute descriptors, explicit streams/strides, indexed primitive groups, material slots, conservative local bounds, optional skin binding and authored LOD partitions; [21](21-2d-and-3d-rendering.md), [18](18-shaders-and-material-pipeline.md) |
| `MaterialAsset` | Registered shader/pass/layout identity, typed linear/color/texture/sampler values, alpha/depth/cull policy and required capabilities; core metallic-roughness is one engine material profile |
| `SkeletonAsset` | Stable joint IDs, topological parents, bind-local poses, inverse-bind row matrices and declared root-motion binding; [20](20-animation-and-deformation.md) |
| `AnimationClipAsset` | Native `TimeSpan` key times, named target tracks, interpolation kind, tangent units, event records, loop/root-motion policy and bounds |
| `CameraDefinition` | Projection kind, engine unit distances, radians, optional aspect override, viewport/layers/clear/order; no source `xmag`, `ymag`, or extension JSON |
| `SceneAsset` / `PrefabAsset` | Stable entity IDs, schema components, root spatial positions, child-local hierarchy, references/overrides and required assets; [16](16-scenes-transforms-and-cameras.md) |
| `CollisionShapeAsset` | Explicit Physics-owned definition, selected spatial profile/settings, exact validator/statistics-formula identity and version, and dimensional `ShapeStatistics2D/3D` with canonical `ShapeMeasure` pairs; not an automatic collision interpretation of a visual mesh |
| `TextureAsset`, `AudioClipAsset`, `FontAsset` | Explicit color/channel/sample/encoding/rate/licensing metadata and bounded payload; their subsystem authorities retain validation |

Native formats can be authored directly, generated procedurally, or produced by any admitted adapter. Engine-only scenes/components/events/materials/root spatial positions need no glTF representation. Runtime asset loading accepts cooked native versions, not source JSON object models.

M03 collision content uses [10's exact version-1 primitive identity inventory](10-binary-data-and-authoring-formats.md#m03-collision-validation-identity-inventory) and [23's geometry/statistics contract](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics). Area/volume is a canonical significand-plus-binary-exponent pair, not a single binary32 measure. A geometry context establishes neither native provenance nor PhysicsWorld/body/material admission. Native loading verifies the selected profile/settings and complete foundation binding, revalidates copied definitions, recomputes statistics and requires canonical equality before publication. Unknown or mismatched validator/formula identities refuse even if their numbers happen to agree. Changing settings requires explicit registered migration/re-cook with new revision/digests; unchanged bits under different settings are reinterpretation, never implicit conversion. Assets remains Core-only transport; Physics-aware typed adapters are supplied by their permitted higher-layer owners.

M03's complete records are the normative [10 M03 3-8 catalogue](10-binary-data-and-authoring-formats.md#22-m03-content-schema-and-package-binding). A collision primitive requires Definition, Statistics, Profile and all four validation/statistics identity members; Settings and Identity are required nested statistics records. Circle/sphere topology fields are absent; Box2 vertex/edge are 4/4 with FaceCount absent; Box3 is 8/12/6. Mesh-processing fields are absent for every primitive. Required analytic integers, single-float SI area/volume and an additional exact-SI cutoff are not this contract. Native loading checks exact profile/settings/presence and recomputed canonical values; runtime verification uses the Assets-owned common provenance kernel without original source bytes or Content.Tools.

## 2. Adapter boundary

An import request selects a registered importer GUID/version and typed options. It supplies the bounded source/dependency leases and declared source conventions: basis, unit scale, vector/matrix convention, winding, texture orientation, time domain, and required extensions. Importers produce a staged native semantic graph plus conversion/approximation diagnostics and provenance. They do not create live entities, native GPU resources, physics proxies, or voices.

The common cooker validates that graph using the same native validators as direct native authoring, then emits immutable platform/profile outputs and atomically publishes the manifest. A failed conversion/cook keeps the prior version with `ImportFailed`; it never relabels source bytes as compatible native data. Source names and optional inert metadata may be retained within budget but cannot change runtime behavior implicitly.

Cache identity includes source/dependency bytes, importer/tool/options versions, source conventions, conversion algorithm, target native schemas, unit/spatial/numerical profiles, and any approximation policy. Conversion is applied exactly once at this boundary. Loading a native asset does not repeat reflection, unit scaling, or source-camera adaptation.

Unsupported source features produce specific admission diagnostics. An explicit approximation is permitted only where the native owning subsystem admits it, with retained settings, affected counts, recomputed bounds, and a new version. Importer convenience cannot expand a physics shape set, material capability, animation influence budget, or executable-content trust boundary.

## 3. glTF adapter

glTF 2.0/GLB are admitted source choices, not native record definitions. [11](11-assets-import-streaming-and-packages.md#2-concrete-cooking) owns the supported subset and complete source validation/conversion: source meters per unit, basis/axial signs, winding/tangents, hierarchy/skins, key times/interpolation, and camera restrictions. All conversion occurs once at that adapter boundary; native loading applies none of it again.

Direct native import and the glTF adapter must produce equivalent canonical geometry/camera/animation behavior for equivalent authored content. Their provenance/digests may differ; behavioral equivalence does not require byte-identical source files or erase the importer identity.

## 4. Acceptance obligations

| ID | Required observation |
|---|---|
| AS01 | A native 2D/3D scene, material, clip, collision asset, and spatial root can be authored/loaded without glTF types or an installed glTF importer |
| AS02 | Two source adapters map equivalent content to the same native semantics; runtime schemas contain no required source accessor/extension objects |
| AS03 | Reflection, unit scaling, winding, tangents, inverse binds, camera axes/planes/aspect and cubic derivatives are converted exactly once |
| AS04 | Native direct data bypasses source conversion but passes the same cook validators and package bounds |
| AS05 | Unsupported required source features and unapproved approximation reject before atomic publication; old versions remain intentionally pinned |
| AS06 | Cache/version identity includes importer/conversion/source and target profiles; stale incompatible caches cannot be relabeled |
| AS07 | Native time resolution collisions, sheared unsupported poses, invalid cameras, influence limits and degenerate geometry have explicit diagnostics |
| AS08 | Native schema migration and inert optional metadata preserve the API/version/trust boundary without imposing a source format on consumers |
