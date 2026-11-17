# M05 Mesh LOD native-authoring closure

**Tentative proposal for later owner review, not an approved contract or acceptance result.** Close only the missing native Mesh 2.0 authoring/package producer. Preserve the selected data in the [owner proposal](m05-owner-contract-closure.md#4-authored-mesh-lod) and [rectification section 4.1](m05-owner-contract-closure-rectification.md#41-exact-dataversioned-producer), its [finite envelopes](m05-owner-contract-closure-rectification.md#14-finite-owner-envelopes), and [Chapter 35's producer sequencing](../design/35-implementation-roadmap.md#m04m05-capability-and-observation-sequencing). Selection/history, Scene preparation, native rendering, and milestone qualification are not delivered here.

## 1. Missing production path

Code inspection finds no Mesh 2.0 producer:

| Boundary | Actual implementation |
|---|---|
| CLI/build entry | [`Program.cs`](../../sources/TerraFX.Content.Tools/Program.cs) dispatches only `cook-static-mesh` for Mesh cooking. [`ContentCooking.targets`](../../sources/TerraFX.Content.Tools/ContentCooking.targets) accepts only GLB/OBJ items. Neither accepts native Mesh records. |
| Mesh authoring | [`StaticMeshSourceAdapters.CreateMeshRecord`](../../sources/TerraFX.Content.Tools/StaticMeshSourceAdapters.cs) emits Mesh 1.0 with one index/group set and one Material 1.0 slot. |
| Package writer | [`ContentPackageWriter.CreatePackage`](../../sources/TerraFX.Content.Tools/ContentPackageWriter.cs) validates **every asset payload as major 1, minor 0** before creating its envelope. Its structural `EncodeRecord` helper can encode other headers, but that is not a Mesh 2.0 cooker. |
| Runtime/schema | [`RenderingSchemaCatalogue`](../../sources/TerraFX.Graphics/RenderingSchemaCatalogue.cs) constructs 1.0 tuples; [`RenderMeshAssetCodec`](../../sources/TerraFX.Runtime/Rendering/RenderMeshAssetCodec.cs) selects 1.0 and [`RenderMeshAsset`](../../sources/TerraFX.Runtime/Rendering/RenderMeshAsset.cs) holds one index/group set. Codec `Write` copies an already-decoded record; it does not author LODs. |

These are source observations, not executed tests. Existing Scene tests supply no evidence of the missing producer.

## 2. Selected native input/output route

Add a sibling Content.Tools command, not another source adapter:

```text
TerraFX.Content.Tools cook-native-mesh --source Assets\lod-mesh.ebin --profile-record Assets\profile.ebin --mesh-id 100 --asset 101=Assets\material.ebin --output artifacts\content\lod.epkg
```

- `--source` is one complete canonical EBIN1 `native/mesh` **2.0** record. Native authors supply explicit streams, all index/group sets, bounds, and thresholds using the existing Core EBIN writing primitives. No new JSON/geometry format or test-only author API is required. Native data is already in the selected engine profile; apply no GLB/OBJ coordinate conversion.
- `--asset <id>=<record-path>` is repeatable and supplies the same-package typed reference closure: Material 1.0/2.0, optional Skeleton 1.0, and any referenced Texture 1.0 records. Split at the first `=`. Root and dependency IDs are caller-supplied stable positive signed-32-bit IDs; duplicate IDs, missing/wrong-version targets, undeclared dependencies, unknown options, and output/input aliasing reject. Resolve paths from the invoking project directory; paths are build inputs, never runtime identities.
- First deliver a static, textureless Material **1.0** package, with no Skeleton. This exercises the full LOD data path without waiting for Animation or Material 2.0. A supplied Material 2.0 reference requires its separately completed exact codec; otherwise reject explicitly, never downgrade or reinterpret it. Skeleton/texture references likewise require their real owner codecs and package-binding validation.
- Add `TerraFXNativeMesh` items to the existing `ContentCooking.targets`, with source in `Include` and `ProfileRecord`, `MeshAssetId`, `PackagePath` metadata. Associated `TerraFXNativeMeshAsset` items name dependency files, their `AssetId`, and a `MeshSource` matching the root's resolved path. A sibling `TerraFXCookNativeMeshes` target uses the existing tool-assembly/project-reference and `TerraFXSkipContentCooking` conventions. Declare every root/profile/dependency file, item options through `MSBuildAllProjects`, target, and tool assembly as inputs; package paths are outputs. Reject ambiguous associations and duplicate output paths.

Reuse `ContentPackageWriter`'s EPKG2 layout and atomic publication. Replace its blanket payload-1.0 assumption with exact compiled **NativeAsset** tuple validation for the explicitly selected producer; do not replace it with unrestricted version pass-through. Keep existing callers' 1.0 selection unchanged. Profile 2.0, envelope 2.0, unsigned AssetId directory ordering, alignment, and externally supplied whole-package SHA-256 remain unchanged. No cook graph, dependency table, per-level AssetId, or publication receipt is added.

## 3. Validation, ownership, and budgets

The author/cooker and new exact Runtime codec must enforce the same selected Mesh 2.0 predicates:

- Tags 1–7 retain their existing shapes. Tags 3/4 are finest level 0; required tag 8 contains zero through seven `native/mesh-lod-level` 1.0 records in authored fine-to-coarse order; required tag 9 contains the same number of finite, positive, strictly decreasing binary32 thresholds. A one-level 2.0 record still encodes both empty arrays.
- All levels share vertex streams, MaterialSlots, and optional Skeleton. Preserve each material reference's selected major, not just its integer ID. Every level has the same logical group count and group-index-to-material-slot mapping as level 0; index width is independently UInt16/UInt32. Do not sort levels by triangle count or rewrite group identity.
- Apply existing stream rank/uniqueness, stride/count, finite/unit/color/tangent, and skin predicates. Joint/weight/Skeleton presence is all-or-none; weights are not normalized during load, and joint indices must fit the actual retained Skeleton.
- Each level independently has in-range indices, triangle-divisible counts, nonzero exact-dyadic triangle area, and nonempty contiguous triangle-aligned groups covering its complete index array. Aggregate indices are at most **1048576** and aggregate groups at most **65536**, including level 0. Existing vertex, stream, and material-slot limits remain.
- Bounds conservatively contain **all supplied positions**, including vertices unused by every level, with the existing sphere predicate. No per-level tighter bound, silent vertex removal, geometry repair, simplifier, or synthesized LOD is permitted.

Preflight nested counts, checked extents, aggregate totals, and complete byte requirements before allocating level storage. Keep the existing 268435456-byte complete-record limit and all structural/decode/live-lease limits; count maxima do not guarantee simultaneous byte fit. The current writer's tighter **2147483584-byte** package ceiling and 131072-entry ceiling remain; the format's larger package ceiling grants no additional memory.

Content.Tools owns bounded input, author/encoding scratch, envelopes, candidate package, and staging storage through validation/publication. Account their simultaneous peak separately from AssetStore's validation decode/lease charges; reserve/check the complete candidate layout before bulk copies. Open the staged candidate through a real AssetStore and the new exact Mesh codec, validate every material/texture/Skeleton binding through that retained backing, and finish validation/lease cleanup before atomic publication. Failure or cancellation leaves the previous output unchanged and cannot release storage still used by a decoder.

Runtime decoded levels and references use ordinary Assets ownership; old leases retain their exact package through replacement. Graphics owns Core-only descriptors, Runtime Rendering owns Mesh semantics/codecs, and Content.Tools orchestrates cooking; Assets gains no Mesh dependency and Runtime gains no Tools dependency. The later World-owned LOD history/output reservations in rectification section 1.4 are **not** cooker workspace credits or a consequence of successfully loading this package.

## 4. First producer and later witnesses

| Sequence | Deliverable and focused evidence to implement |
|---|---|
| Versioned author/reader closure | Add the selected Mesh 2.0 and helper 1.0 metadata, distinct exact Mesh 2.0 immutable value/codec, and package-bound reference validation. Use reviewed literal inputs/expectations for one/eight levels, required empty arrays, threshold count/order/nonfinite failures, invalid coarse triangles/ranges/mapping, aggregate limits, skin domains, and all-position bounds. Mesh 1.0 bytes and behavior stay unchanged and reject LOD fields. |
| First real package producer | Deliver the native CLI, exact-tuple package writer selection, and ordinary build target together. Add focused `TerraFX.Content.Tools.Tests` and Runtime Rendering codec tests that consume native EBIN files and reload the actual emitted EPKG2; no Scene preparation is needed. Verify retained level/index/group/reference membership, deterministic bytes under reordered dependency arguments, exact envelope/schema versions, failed/canceled cook preserving the previous output, missing/wrong references, byte refusal, and declared-input/missing-output rebuild behavior. Use existing owning-project Debug then Release commands when implemented, not a separate qualification runner. |
| Later Scene/camera and native consumers | After the actual version-2 component/reference and static Scene/camera extraction producers exist, consume this package for rectification sections 4.2–4.3's projection/hysteresis, two-camera history, repeat extraction, refusal, rebase, replacement, and retained-geometry observations. Then use genuine World-produced snapshots for fine/coarse silhouettes and fence-retained old geometry on D3D12/Vulkan. Public/AOT and applicable platform lanes remain separate observations. |

**Distinguishing first input:** supply positions `(-1,-1,0)`, `(1,-1,0)`, `(1,1,0)`, `(-1,1,0)`, `(2,0,0)` with matching +Z normals; level 0 UInt16 indices `[0,1,2,0,2,3]`, level 1 UInt32 indices `[0,1,2]`, one group per level mapped to material slot 0, and threshold `[0.625]`. Bounds `[-1,-1,0]..[2,1,0]`, sphere center zero/radius 3, include the unused fifth vertex. Reload must preserve both topologies and widths rather than flatten to one set; shrinking the bounds to referenced vertices must reject. The quad/triangle difference later supplies an independently authored silhouette witness, not a current pixel result.

The fail-closed Scene-preparation candidate is not executable evidence and does not block this standalone package producer. Retry Scene/history observations only when their actual producers are available; do not fabricate snapshots or reuse Scene tests as LOD evidence. Static authored LOD does not require live Animation; animated coarsening still requires its real conservative-bounds/palette producer, and current qualification-zero bounds retain finest-level fallback. GLB/OBJ continue producing only Mesh 1.0; no imported LOD, automatic simplification, new acceptance ID, or acceptance claim follows.
