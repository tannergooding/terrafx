# M05 skinned-normal diagnostic budget-evidence palette-bound addendum

## 1. Tentative scope and production bound

**Tentative correction, pending review; not a contract amendment, implementation change, admission guarantee, or acceptance decision. The budget gate remains NO-GO.** This supplements only the count domain and command arithmetic in [budget-evidence rectification](m05-skinned-normal-diagnostic-budget-evidence-rectification.md), sections 3 and 7. The existing proposal is unchanged.

Here `D` counts diagnostic draw groups and `M` counts their distinct geometry resource identities. Source references in this section are relative to `sources\TerraFX.Runtime\Rendering\`:

- `Renderer.Extract.cs:650–657` counts `mesh.Groups.Length` draws but adds `skeleton.Joints.Length` only once per selected Mesh entity. Its shared Sprite/Mesh draw ceiling is 131,072; its bone-palette-entry ceiling is 2,048 (`19–23,1066–1097`).
- Extraction creates one palette per skinned entity and reuses its index across every group (`Renderer.Extract.cs:240–273`). `RenderSnapshot3D.cs:83–103,125–169` requires nonempty matching palettes, not a distinct palette per draw.
- Camera selection is unioned without multiplying rows; geometry is deduplicated by `(SceneInstanceId, MeshAssetId)` (`Renderer.MeshC0Graph.cs:561–580,905–917`). Diagnostic preparation counts each selected skinned group (`RenderMeshC0NormalDiagnosticWork.cs:117–160`). Thus each distinct diagnostic geometry needs at least one captured palette, but each draw does not need a distinct palette entry.
- `RenderMeshAssetCodec.cs:225–268` permits up to 65,536 contiguous nonempty triangle groups without requiring different vertices, materials, or bones. One one-joint entity with 2,049 groups is therefore a source-permitted counterexample to `D <= 2,048`, not an observed or byte-qualified workload.

For positive `D`, the production count domain is:

```text
D <= combined selected Sprite/Mesh draw count <= 131,072
1 <= M <= min(D, 2,048)
```

Other admission limits can reduce the reachable workload. The 131,072 ceiling is not a promise that a diagnostic-bearing graph can be constructed or recorded there.

## 2. Command-only consequences

Retain `N = 6D - M - 2` from `RenderMeshC0NormalDiagnosticGraphPass.cs:149–165` and the existing proposal's measured-profile command stride of 280 bytes. This addendum does not remeasure that profile.

The `D=M=131,072` all-distinct row is unreachable through production extraction. Its arithmetic remains a valid **relaxed algebraic lower bound**, not the production-compatible minimum schedule. At `D=131,072`, maximal count-compatible `M=2,048` instead gives `N=784,382`: final payload **219,626,960 bytes**, two payloads **439,253,920 bytes**, and two conservative array charges **439,254,048 bytes**.

Using the existing `Q(c,s) = 64 + roundUp16(c*s)` rule and the optimistic mutable capacity `C=N`, `2Q(N,280) <= 33,554,432` requires `N <= 59,918`:

| Command-only family | Last fitting D | First failing D | First-failing `2Q(N,280)`, bytes |
|---|---:|---:|---:|
| Repeated geometry, `M=1` | 9,986 | **9,987** | **33,554,784** |
| Maximum production-compatible `M=min(D,2,048)` | 10,328 | **10,329** | **33,557,568** |
| Relaxed `M=D` arithmetic, not production-reachable above D=2,048 | 11,984 | 11,985 | 33,557,024 |

For the maximum-M family, D=10,328 charges 33,554,208 bytes; the next row has `N=59,924`. These are derived command-only thresholds, **not safe admitted draw limits or demonstrated workloads**. Actual mutable capacities and every other simultaneous charge still apply.

## 3. Disposition and unchanged gate

Use `M <= min(D,2,048)` for subsequent production-domain lower bounds and boundary derivations. Keep larger `M=D` cases explicitly relaxed/scalar; do not present them as production snapshot or end-to-end admission cases. This correction does not require lowering the shared renderer draw ceiling. The conservative pre-index command bound `N <= 6D-3` remains valid.

Current preparation already refuses against five preparation arrays plus worst-case command arrays before allocating diagnostic arrays (`RenderMeshC0NormalDiagnosticWork.cs:57–83,168–192`); it can refuse earlier than these command-only thresholds. It is not the complete ledger.

Retain the joint **33,554,432-byte preparation/recording ceiling** and independent **16,777,216-byte completion ceiling**. Complete profile-qualified phase ledgers, pre-admitted whole-graph refusal, ownership/root-release and quarantine accounting, and completion-budget enforcement remain **NO-GO** under the existing proposal. No budget or qualification credit follows from this correction.

## 4. Narrow input hash manifest

SHA-256 hashes identify the exact dirty-workspace inputs for this count/arithmetic correction, based on HEAD `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`; HEAD alone does not identify their bytes. This is not a full-ledger or measurement-qualification manifest. Recheck these hashes before applying a review disposition; changed inputs require reconsidering affected claims.

The proposal row is relative to `docs\proposals\`; all source rows are relative to `sources\TerraFX.Runtime\Rendering\`.

| Input | SHA-256 |
|---|---|
| `m05-skinned-normal-diagnostic-budget-evidence-rectification.md` | `A8DBD9EE4EA443245391A3B8AE17EDE03E3D71DA3F824E9128641E1C94F4DB36` |
| `Renderer.Extract.cs` | `274298BFDCEE321141D4138D417807951FCC0EF6BCAC1DFCE2726D5F36829F2D` |
| `RenderSnapshot3D.cs` | `5C6044F77B45C89A6C6C00F2B4D393C12FB426B7CEDAA019D449B0E45D5191DC` |
| `Renderer.MeshC0Graph.cs` | `C1D61709BC3B9AD7325B2B4C47B702C02E8A11A279C6AE3290554EEA4B142E20` |
| `RenderMeshAssetCodec.cs` | `F863186F77B07AAD63606D43A3F74CC887D0A6C3A52B3BD1777EDAD7899CEB16` |
| `RenderMeshC0NormalDiagnosticWork.cs` | `4C3D87B4DF7B2BCA950D7200A8DB801D083AE075AA0171707403235CC77A2CFD` |
| `RenderMeshC0NormalDiagnosticGraphPass.cs` | `14E3198F43B96725BE5509EA5611EE1E9D1204E5111AB7327E7C2B61B5DAA380` |
