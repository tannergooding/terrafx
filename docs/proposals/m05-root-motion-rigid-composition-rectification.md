# M05 root-motion rigid composition rectification

## Status and scope

**Tentative implementation recommendation for owner review; not an approved contract, implementation, or acceptance result.** Preserve Chapter 20 section 3's root-motion semantics. The tentative M05 owner proposal section 2.6 is context only. No public API, schema, dependency, or maintained design change is proposed.

The live Animation/World root-motion producer remains unimplemented. This numerical proposal does not establish playback, actor application, or actual MotionFeedback.

## Defect and recommended representation

The reviewed matrix-power revision amplifies binary32 rotation-matrix roundoff into scale loss: its quarter-turn fixture returns a finite result rejected by `Matrix4x4.TryInvert` after one billion loops and zero X/Y linear rows after one trillion. Reverse traversal then fails. The reviewed billion-loop assertion checks only finiteness.

Recommend one private immutable rigid value: a finite `TerraFX.Numerics.Vector3` translation and a normalized `TerraFX.Numerics.Quaternion` rotation. This is seven scalar components with constant storage, no accumulated scale/shear or per-loop allocation. Identity is explicitly zero translation and `Quaternion.Identity`, never the raw default.

For row-vector transforms $M(q,t)=R(q)T(t)$, define product $C(L,R)$ to represent $M_L M_R$, applying left then right:

- Rotation: $q=N(\operatorname{Compose}(q_L,q_R))$.
- Translation: $t=t_L R(q_R)+t_R$.
- Inverse: $q_i=N(\operatorname{Conjugate}(q))$, $t_i=-tR(q_i)$.

Here `Compose` is the existing `Quaternion.Compose(first, second)` application-order operation; `N` is checked normalization. Rotate translations as directions, not points. Preserve existing root binding, unit-scale, and quaternion-sample admission. Reject nonfinite translations or failed normalization; never repair failure with identity.

Use these operators for endpoint deltas, cycle deltas, chronological `tail * repeatedCycles * head`, and reverse inversion. Keep checked endpoint normalization and the widened loop-count calculation. Exponentiate by squaring in at most 64 iterations for a `ulong` exponent, normalizing **every** quaternion product, including squares and accumulator updates. Do not exponentiate matrices or use general matrix inversion for reverse traversal. Materialize the existing matrix result only at the boundary, retaining finite-result faults.

## Distinguishing regression

Reuse the existing duration-1000 fixture: identity bind/start, translation from zero to unit X, and rotation from identity to `(0, 0, sqrt(0.5), sqrt(0.5))`. Independently, four cycles visit XY positions `(1,0)`, `(1,1)`, `(0,1)`, `(0,0)` and restore orientation. Therefore one billion cycles have the literal expected delta `Matrix4x4.Identity`.

Assert that forward and reverse billion-cycle deltas equal that identity within the existing `1e-5` component tolerance, that `Matrix4x4.TryInvert` succeeds, and that both inverse products are near identity. Do not derive the oracle from the production one-cycle result. Retain the noncommuting crossing regression and existing endpoint/overflow/binding behavior. These are proposed CPU regressions, not executed acceptance evidence.

## Exact input manifest

SHA-256 of raw bytes captured for the defect review and recommendation. The kernel and test entries identify the **earlier reviewed matrix-power revision and finite-only regression**, not concurrently edited candidates. This proposal reviews or accepts no subsequent implementation or test revision.

| Input | SHA-256 |
|---|---|
| `docs\design\20-animation-and-deformation.md` | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\proposals\m05-owner-contract-closure-rectification.md` (root-motion context) | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| `sources\TerraFX.Runtime\Animation\AnimationRootMotionKernels.cs` (reviewed matrix-power revision) | `4F5A8C9FC2CE53DC441132093B97B59A8E0ACDE1A8DF6FCFF0952DAB43B6DECD` |
| `sources\TerraFX.Core\Numerics\Quaternion.cs` | `772F08C233BE46CBC9B590BA9433F36A866E535840D63D0324966AF9192EBA0B` |
| `tests\TerraFX.Runtime.Tests\Animation\AnimationRootMotionKernelsTests.cs` (reviewed finite-only regression) | `3056273966E1CD18C5296A5621188630F60CBD1EA8C956A82B269B67BE94973D` |
