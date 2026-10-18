# Physics acceptance, numerical evidence, and primary references

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

[22](22-custom-physics-world-and-solver.md) and [23](23-custom-physics-shapes-queries-and-ccd.md) are the normative custom-physics unit; shared [00](00-overview-and-scope.md), [03](03-shared-conventions-and-clocks.md), [02](02-dotnet-api-memory-and-ownership.md) and parent integration remain authoritative. All PH cases below are **unexecuted implementation obligations**; reference helpers do not establish their engine results.

## 1. Fixtures, observations, and pass rules

Run each "D" case in both 2D and 3D; embed XY input at Z = 0 where meaningful, using circles/spheres and boxes with full unit sizes. 3D meshes use triangles, 2D surfaces segments; never replace a 3D case with a 2D result. Unless overridden: rate 60, Substeps4, 12 velocity/4 position iterations, gravity 0, damping 0, friction 0.5, restitution 0, enabled/awake dynamic unit cube/square of mass 1 and center of mass (COM) 0, no rotation lock, binary32/compensated spatial internal profile. Dynamic fixtures supply explicit mass 1 and the matching analytic shape inertia, including small projectiles whose density 1 computed mass would fall below admission; computed-mass cases opt in explicitly. Tests create bodies at a final barrier, then begin counting simulated ticks. A "floor" is a static box spanning x +/-10 m, upper face y = 0, half-height 0.5; in 3D z +/-10. Gravity-enabled tests set `(0,-9.81[,0])`.

Default fixtures construct standalone `PhysicsWorld` with `Core` owner context, explicit rate/origin and `Guid.Empty` `BindingId`, prepare/commit an idle initialization batch, then call public `Step`; no entity-component-system (ECS) `World` or scene is needed. PH04/05/06/48's bound-entity portions are explicitly `TerraFX.Integration` fixtures, with raw `Physics` events plus optional `Entity`-enriched wrappers. They exercise the adapter, not an `Entity`/scene reference inside `TerraFX.Physics`. "Final barrier" for a standalone case means its explicit idle structural boundary.

PH09's separately selectable `M03Primitive2D`/`M03Primitive3D` observations use the pure typed validators and geometry-only contexts under [23 section 1.2](23-custom-physics-shapes-queries-and-ccd.md#12-inert-primitive-validation-and-statistics). They create no PhysicsWorld, owner, ShapeHandle, body/material or simulated tick; the world/rate/density defaults above do not apply. Exact identity, measure-pair and bit-rounding checks have no solver tolerance. Remaining PH09 full-profile/handle/material/nonprimitive observations retain M06 ownership.

Public observation tolerances: position/witness <= 0.001 m per component; unit normal length error <= 1e-5; velocity <= 1e-4 m/s and angular velocity <= 1e-4 rad/s unless a workload states a looser physical convergence threshold. Exact handle/count/status/order/state checks have no numeric tolerance. Small direct equation checks use their stated analytic result or residual inequality; they do not replace acceptance tolerances for rounded binary32 solver arithmetic. Native kernel/solver qualification uses [08](08-binary32-kernels-and-predicates.md)'s predicate and rounding contracts plus these physical observation/residual budgets. Sweep/ray `Distance` tolerance 1e-5 m near origin and Fraction tolerance 1e-6; at operational-coordinate limits use position/distance 0.001 m. Compare quaternion orientation by shortest angle, not sign or component difference.

Collect local physics snapshots/outputs/raw events, optional `Integration`-enriched events, witness/feature traces, statistics and faults. An unexpected fault during `Physics` `Step` throws `PhysicsSimulationException` with `PhysicsFault` after local staging rollback/Status Faulted/`LastFault` update; `NextTick` and all locally committed contacts/impulses/sleep/state/cursor equal their pre-step copy, with no successful output or generic result union. Do not generalize this to a later integrated tick fault: valid stage 4 physics and earlier ECS writes can already have committed locally. After a `Late`/`Structural`/`Extract` fault, external stage 9 events/receipts/snapshots are absent and the encompassing `World` must restore its prior committed whole-world checkpoint before resuming; physics is not automatically undone. Read-only query `Complete`/`BufferTooSmall`/`Indeterminate` uses the existing domain status/count/fault contract and failed queries leave sentinel outputs unchanged. Variants use isolated worlds.

The thresholds qualify the chosen profile; they are not already measured. If the eventual engine cannot meet them, fix the implementation or seek a recorded specification revision with affected acceptance review. Do not silently relax tolerances or substitute an external engine. No implementation-time wall budget is claimed without named hardware/runtime/build/workload measurements.

### Exported standalone physics capability contract

`GetCapabilities()` returns a frozen `TerraFX.Physics` `PhysicsCapabilities` named data contract: Dimension (`TwoD`/`ThreeD`), `ImmutableArray<PhysicsShapeKind>` `SupportedShapes`, `ImmutableArray<PhysicsJointKind>` `SupportedJoints`, `SupportsTranslationalCcd` = true, `SupportsRotationalCcd` = false, `SupportsContinuousTriggers` = false, `SupportsCapsules` = false, `ProvidesCharacterController` = false. `PhysicsShapeKind` is an enum Circle/Sphere/Box/`ConvexPolygon`/`ConvexPolyhedron`/`StaticSegmentMesh`/`StaticTriangleMesh`; `PhysicsJointKind` is an enum `Distance`/Ball/Hinge/Fixed. A 2D world reports Circle/Box/`ConvexPolygon`/`StaticSegmentMesh` and `Distance`/Hinge/Fixed; a 3D world reports Sphere/Box/`ConvexPolyhedron`/`StaticTriangleMesh` and `Distance`/Ball/Hinge/Fixed. Conditional body/mesh/rotation eligibility remains exactly chapter [23](23-custom-physics-shapes-queries-and-ccd.md)'s contract, not an unconditional continuous collision detection (CCD) promise.

The engine exports no capsule collider or built-in character-controller witness. Application-authored circle/sphere motors can consume `TerraFX.Animation`'s typed root-motion record through `TerraFX.Integration` and emit typed forces/impulses/kinematic targets using chapter [22](22-custom-physics-world-and-solver.md)'s command keys. Root validation, entity mapping and event enrichment belong to `Integration`; motor policy/gameplay belongs to the application. This clarification does not expand shape scope, add GJK/EPA or rotational CCD, or make `Physics` depend on `Animation`/`Scenes`/Entities.

## 2. Uniquely identified required cases

### PH01 - Fixed time and integration (D)
With gravity, body y = 10 and velocity 0, simulate 60 ticks (240 microsteps). COM velocity y = -9.81 within 1e-4, y = 5.0745625 within 0.001 by semi-implicit summation. Tick boundary `TimeSpan` ticks at n = 1/2/3 are 166666/333333/500000. Different presentation rates do not change these outcomes.

### PH02 - Orientation conventions (D)
Gravity 0, angular velocity + pi/2 around Z, simulate 60 ticks; transformed local +X is world +Y within 0.001, angle + pi/2 within 1e-4. In left-handed (LH) 3D, + Y90 deg rotates +Z to +X and + X90 deg rotates +Y to +Z; force-at-point `(r =  + X,force =  + Y)` produces axial torque +Z. Right-handed (RH) reflection maps polar Z/axial X-Y signs per [03](03-shared-conventions-and-clocks.md); no blanket cross negation. Noncommuting X/Z integration agrees with active `Vector3.Transform` within 1e-5 using left world-delta multiplication, not row-matrix quaternion order.

### PH03 - Forces, impulses, and consumption (D)
Explicit mass 2, force + X1 N once in `Early`, simulate 1 tick: vx = 1/120 m/s within 1e-6; a second tick without force keeps that velocity. An impulse + X2 N s then adds exactly 1 m/s within 1e-6. Force-at-point and torque order must match the copied command journal, and a rejected NaN command changes neither state nor cursor.

`Integration` ordering variant, mass 1: `Stage` 1 with `SystemOrder = 100` calls `SetVelocity` with `+X` at magnitude 2 before `Stage` 2 with `SystemOrder = 1` calls `ApplyImpulse` along `+X` with magnitude 1, producing `vx = 3` within `1e-6` despite the inverse system-order values. Partition 0 then 1 at one stage/system uses local `Sequence`, not worker arrival. A `Late` `Stage` 6 call to `SetVelocity` with `+X` at magnitude 4 emitted at `N` is admitted at `N + 1` with its key retained; it follows the current `Early` `Stage` 1 `SetVelocity` with `+X` at magnitude 2, giving `vx = 4`. Standalone keys use `Stage` 0, a stable-producer `SystemOrder`, and `Partition` 0.

### PH04 - Final-barrier creation and admission (D)
`Integration` fixture: create a body vx1 at stage 7 N; its tick-local proxy exists but it has not simulated at N. At N + 1 pose x advances 1/60 within 0.001. Invalid same-batch collider/token dependency rejects physics/ECS/binding reservations without partial creation. Standalone initialization creates the same unbound body before its first `Step`; `Step` immediately returns the full local result. Equal Sequence0 in two partitions yields distinct request tokens.

Post-physics fault variant: valid stage 4 advances x by1/60, then inject a `Late` stage 6 failure. Locally committed physics still has that solved pose/cursor; encompassing `TickIndex` does not advance and stage 9 publishes zero tick events/receipts/snapshots. Prior external snapshot remains unchanged. Restore the prior whole-world checkpoint (physics/ECS/bindings/commands/origin) before another tick; do not assert automatic rollback.

### PH05 - Destruction and contact tombstones (D)
Rest a body touching the floor and observe one `Begin`. `Integration` destroys it at stage 7 N after tick-local `Late` delivery but before external stage 9 publication. Queries no longer see it; next successful encompassing tick publishes one End `BodyDestroyed` at `FinalPrevious` with `ObservedTick` = N. Raw event retains `BindingId`; `Integration` wrapper retains the historical `Entity` captured before unmapping. Standalone raw tombstones require no `Entity`. No dangling joint, missing end, Stay-after-End or solver-time callback.

### PH06 - Teleport and dynamic pose authority (D)
Touch the floor, set velocity + X1, teleport + X2 on the same floor. Clear makes velocity 0; Preserve keeps 1 within 1e-4. Old End Teleported precedes new `Begin`. `Integration` variant: ordinary scene-pose write, non-root binding, scale (2,1[,1]), reflection and shear reject as `Integration` `UnsupportedPhysicsTransform` before solver admission, without partial overwrite. Standalone definitions contain rigid poses/`BindingId` only and never inspect a scene.

### PH07 - Collision filters and sensors (D)
Overlapping dynamic pairs: category 1/mask 2 versus category 2/mask 1 permits; mask 0 denies; equal positive group overrides denying masks, equal negative group overrides permitting masks. `Set` one collider sensor: `BeginTrigger` with zero impulses and no velocity change. Filter/disable then produces End Filtered/Disabled; same-body, static-static, and `CollideConnected` = false pairs never emit begins.

### PH08 - Enable, body-type, and mass edits (D)
Disable an awake moving body for 10 ticks: pose/velocity stored but no query/contact/integration. Re-enable wakes and resumes next admitted solve. Replace a computed mass with a shifted COM: origin pose and origin-point velocity remain within 0.001/1e-4 while COM velocity changes by omega cross `deltaCenter`. Static replacement has zero velocity/inverse mass and clears old warm starts.

### PH09 - Shape validation and refusal (dimension-specific)
Reject clockwise/collinear/self-crossing 2D polygons; a LH 3D cube with reversed/missing clockwise-outside face, duplicate vertex, concavity, coplanar split face or width 0.01; degenerate/non-manifold/inconsistently wound/self-intersecting meshes. Each names indices and creates no shape. Capsule/dynamic mesh/sheared collider requests are unsupported, not boxes. `GetCapabilities` exactly matches the dimension's exported table, including translational-only CCD/no capsule/no controller; serialized unsupported kinds cannot silently expand it. A valid unit cube has outward numerical cross normals/positive volume 1/inertia 1/6 per axis at density 1. Reflect RH cube coordinates and reverse loops once: same mass/converted COM/inertia, not negative volume or abs repair. Repeat computed-mass square/cube fixtures with physical meter and millimeter settings and omitted material: the dimensional world default density resolves to the corresponding engine units and preserves physical mass. Explicitly supplied density is not rescaled; explicit numeric density 1 outside the millimeter world's converted range rejects before commit.

#### M03 primitive observations and retained M06 parity

| Selection | Required independent observation |
|---|---|
| M03Primitive2D/3D thresholds | Exercise selected unit exponents `-30, 0, 3, 30`; distinguish exact `1/40` from `0.025f` at exponent 3 (`0x37D1B717` versus `0x37D1B718`), admit the published endpoint and exercise adjacent values. Both Core overloads prove nearest-even signed underflow, float signed-zero preservation, exact rational zero `+0`, parameter bounds and overflow exceptions. |
| M03Primitive2D/3D geometry | All four immutable definitions, finite-bit copying, API null/nonfinite/default-settings exceptions versus finite-content rejection, radius/X/Y/Z fault precedence, exact corner needles and overflowing float-square cases, minimum enclosing-radius inequalities, and exact nullable topology/processing applicability. |
| M03Primitive2D/3D scaled measure | Reviewed literal normalized pairs and scalar bits derived from the declared formulas and pi bits `0x40490FDB`; cover 24-bit-significand normalization/carry/ties, bounded exponent range, canonical zero, successful normals/subnormals, and false/default output for nonzero underflow/overflow. A valid one-meter sphere at unit exponent -14 retains volume `(0x3FC056FF, 141)` despite single-float overflow; float materialization failure never rejects the geometry. |
| M03Primitive3D stored-pair precision | Unit exponent 12, radius `0x28E12FB1`, stored volume `(0x3FB67680, -134)`: independently prove the exact subnormal midpoint and successful nearest-even `TryGetSingle` result `0x0000B676`; direct formula rounding is `0x0000B677`. Do not merge these rounding boundaries. |
| M03 native bindings | Verify the selected `physics/primitive2` or `physics/primitive3` schema version, spatial profile/settings, canonical measure pair/applicability and statistics recomputation. Independently valid enclosing digests must reach schema-version/profile/settings/statistics mismatches; API NaN throws while equivalent wire input gives contextual content rejection before constructor invocation. Attribute root cross-invariants to the primitive schema/tag 0 and physical occurrence path; child-local failures retain child attribution. Preserve required-context/local ordering and Core's actual first-provable structural scanner failure. No validator/formula IDs, versions, per-use indices or unknown-identity precedence cases are part of the content contract. |
| M03 reinterpretation/conversion | Unchanged `32f` radius under meters versus millimeters has different physical meaning even when numeric measure bits agree. Explicit conversion produces `32000f` in millimeters and a new validated definition/statistics/revision/digests; changed settings cannot implicitly revalidate/rebind old assets or pass StrictLocal. |
| M06 geometry/full-profile boundary | Kilometer context validates suitable local geometry while full fixed-spacing admission refuses under [22](22-custom-physics-world-and-solver.md). Admitted worlds reuse the same primitive outcome/statistics; independent storage/capacity refusal is distinct. Robust mass/inertia consumes copied geometry/density, not rounded statistics or `TryGetSingle`. |
| M06 surface/statistics projection | Every 2D face count is null; 3D facets/mesh triangles and internal indexed edges have exact counts. Welded count is final retained count, patch count is groups, and actual adjustment includes projection with null versus zero applicability. Valid closed surfaces retain canonical zero filled measure; degenerate/inverted solids reject without absolute-value repair. |

These observations also qualify the native AS01/AS04/AS06-AS08 and BN/AR/EN slices selected by [35](35-implementation-roadmap.md), not replacement IDs or completion claims. Expected UUIDs, rational thresholds, measure bits, wire bytes and enclosing hashes come from the frozen contracts and reviewed literal facts; production rounding and packers are tested against those facts. Collision readers/writers remain gated on their actual schema/API and consumer implementations, not a second descriptor or fixture-approval pipeline. Running these primitive observations does not execute the remaining PH09 world/handle/mesh/solver cases.

### PH10 - Full convex containment (D)
`Distance` between centered inner box half 0.5 and outer box half 2 yields signed depth -2.5 m within 1e-5, not the interval-intersection width -1. Normal is defined/unit with stable tie; 2D manifolds contain an integer point count in `[1, 2]` and 3D manifolds in `[1, 4]`, consistent with witness gaps. Move inner center to x4: Euclidean distance 1.5 within 1e-5 and no empty successful penetration manifold.

### PH11 - Coincident centers and sphere-inside-hull (D)
Two radius 0.5 circles/spheres at identical centers report depth -1 and +X normal without cache, plus `CoincidentCenters` diagnostic. Radius 0.25 at center of box half 1 reports depth -1.25 within 1e-5 and the lowest-index nearest exit face tie. Repeated calls have identical feature keys; no NaN, zero purported unit normal, or iteration loop.

### PH12 - Separating axes despite overlapping AABBs (D)
Two boxes half (1,0.05[,0.1]), both rotated + pi/4 around Z, centers (0,0[,0]) and (0.2,-0.2[,0]), have overlapping AABBs but separated shapes. Overlap returns zero hits; internal separating projection is approximately 0.182842712 m within 1e-5. No broad-phase-only false `Begin` is allowed.

### PH13 - Face manifold and persistence (D)
Unit body centered y = 0.5 on the floor: exactly 2 (2D) or4 (3D) reduced face points, all gaps within 1e-5 of 0 and normal floor-to-body +Y when floor is canonical A. Move x by0.001 between accepted poses: points persist with valid features and warm-start eligibility; no more than the documented point count or coordinate-hash keys.

### PH14 - Closest-feature degeneracies (kernel, both dimensions)
3D segment from `(-1,0,0)` to `(1,0,0)` and segment from `(0,-1,1)` to `(0,1,1)` yield witnesses (0,0,0)/(0,0,1), distance 1 within 1e-10. Parallel overlapping projections and zero-length intermediate features reduce safely to endpoint tests. Triangle edge/vertex barycentric cases return the closed feature, and near-parallel separating axis theorem (SAT) edges skip redundant axes without losing a separating face.

### PH15 - Coplanar mesh seam (D)
Mesh floor spans x in `[-2, +2]`, split at 0; in 3D use a square spanning z in `[-2, +2]` with two triangles sharing its diagonal. Gravity, friction 0, body y = 0.5/x = -0.5 and vx = 0.5 for 120 ticks. COM ends x = 0.5 within 0.01, y drift <= 0.01, no seam normal with spurious horizontal component > 1e-4, and one continuing collider-pair episode rather than seam End/`Begin`.

### PH16 - Real mesh corners and creases (D)
Static floor/wall surfaces at y = 0/x = 0; add z = 0 in 3D. Body center (0.5,0.5[,0.5]) touches 2/3 distinct patches with independent axis normals, not one averaged diagonal plane. Contact count and impulses remain within caps; an open boundary retains edge contact rather than being discarded as an internal seam.

### PH17 - Exterior ray and nearest surface (D)
Circle/sphere radius 1 at origin, ray origin (-3,0[,0]), direction +X, distance 6. First hit `Distance` 2, Fraction 1/3, Point(-1,0[,0]), Normal(-1,0[,0]) within declared query tolerances. Reversing creation order with farther colliders cannot change nearest-first delivery.

### PH18 - Inside, boundary, and zero ray (D)
Ray from center of radius 1: `ReportInitial` gives `InitialOverlap`/`UndefinedInside`/distance 0; `IgnoreInitial` gives zero hits; Exit gives +X surface at 1. From boundary x = -1: outward Exit 0, inward Exit 2. `Zero` `MaxDistance` only reports an initial state, never a future exit. `Zero`/nonunit/NaN direction is an argument failure, not Miss.

### PH19 - Coplanar and shared-edge mesh ray (D)
2D segment from `(0,0)` to `(1,0)`, ray (-2,0) along +X: hit distance 2. 3D triangle (0,0,-1),(0,0,1),(1,0,0), ray (-2,0,0) along +X: same entry. Normal is defined canonical surface normal at tangent; a ray through a welded shared edge delivers one merged mesh hit, not zero or duplicates.

### PH20 - Certified miss and coordinate-edge roots (D)
Ray parallel outside a box half 1 at y = 2 is `Complete` with zero hits, not `Indeterminate`. Sphere/circle center x = 4090/radius 0.5, ray start 4088/ +X/distance 3 hits at 1.5 within 0.001. Endpoints outside an admitted calculation/residency neighborhood, stale explicit frame revisions, and nonfinite discriminant inputs refuse without output writes; near-tangent valid roots do not use an unstable subtractive formula.

### PH21 - Query sorting, buffers, callbacks (D)
Radius 0.5 colliders centered x = 0,2,4; ray from -3/ +X/length 10 produces distances 2.5,4.5,6.5. Buffer length 2 returns `BufferTooSmall`/Required 3/Written 0 and unchanged sentinels. Full buffer returns all; coincident-distance ties order by handle. Visitor `Stop` delivers one but preserves validated world state; thrown/reentrant callback follows the explicit exception/refusal policy.

### PH22 - Translational convex sweep (D)
Radius 0.5 query shape at x = -3 sweeps + X6 toward static box half 0.5 at origin: first center x = -1, Fraction 1/3, `Distance` 2, obstacle witness x = -0.5, obstacle-to-query normal -X. `Zero` translation inside gives `InitialOverlap` or ignored-collider Miss according to policy, with no solver/event side effect.

### PH23 - Rounded edge/vertex cast roots (dimension-specific)
3D triangle (-1,-1,0),(1,-1,0),(0,1,0); sphere radius 0.625 at (0,-1.375,-2) casts + Z4: edge entry `Distance` 1.5/Fraction 0.375, witness (0,-1,0), normal (0,-0.6,-0.8). `Start(-1.375, -1.5, -2)` instead: vertex tangent at `Distance` 2/Fraction 0.5, normal (-0.6,-0.8,0). 2D radius 0.625 from (-2,1.375) against vertical segment ending (0,1) enters at 1.5 with normal (-0.8,0.6).

### PH24 - Mesh surface is not filled solid (D)
`Query` point at center of closed segment loop/triangle cube half 1: mesh-only overlap is empty, distance 1, interior-start ray hits the sheet at 1 as Surface, not `InitialOverlap`. A convex box with the same boundary is an inside overlap. Mesh mass computation/dynamic attachment refuses; no silent parity fill or unit-mass fallback.

### PH25 - Coulomb friction (D)
Gravity floor/body friction 0 and vx1: after 60 ticks x displacement 1 within 0.01. Repeat both friction 0.5: stops within 0.25 seconds, displacement approximately 0.10194 within 0.015, final |vx| <= 0.01. In 3D diagonal tangential input verifies impulse-vector norm <= mu * `normalImpulse` + 1e-8, not independent-axis square friction.

### PH26 - Restitution threshold (D)
Continuous centered radius 0.5 body y = 1.5/vy = -2 approaches floor, gravity 0, restitution 1 on both. First impact changes vy to + 2 within 0.005 without speculative pre-impact bounce. Repeat vy = -0.5: impact leaves |vy| <= 0.001 because threshold 1 suppresses bounce. Impact impulse is not multiplied during CCD warm-start scaling.

### PH27 - Ten-body stack and sleep (D)
Gravity, ten unit boxes at y = 0.5 + i, zero initial velocities, floor, simulate 1200 ticks. By tick 600 all may satisfy the required sleep gate and must sleep by tick 1200; no explosion/NaN. Final max penetration <= 0.02 m, top horizontal drift <= 0.05 m (2D)/0.1 m (3D), vertical height error <= 0.1 m, no repeated contact `Begin` churn. Record counts/residuals and energy, do not infer stability from a screenshot.

### PH28 - Position correction does not inject velocity (D)
Gravity 0, friction 0, locked-rotation dynamic box penetrates static box by0.02 m with zero velocity. After 20 ticks depth <= 0.006 m and |v| <= 0.001 m/s; position-only impulses never appear as restitution/velocity impulses. Initial correction respects 0.05 m/0.05 rad per-row limits and discovers any third-body overlap it creates.

### PH29 - Lever arms and angular impulse (D)
Unit cube/square mass 1, inertia 1/6, COM 0; impulse + X1 at world (0,1[,0]) gives vx = 1 and `omegaZ` = -6 within 1e-4 before integration. Origin/COM distinction and shifted collider local transform produce the same world-point torque. `RotationLocked` instead retains omega 0, not an invalid inverse-inertia divide.

### PH30 - Damping and finite integration (D)
Initial `vx = 10`, `LinearDamping = 3`, and gravity 0: after 1 tick, `vx = 10/(1+3/240)^4` within `1e-4`. Damping 0 preserves velocity. Scalar angular damping follows the same formula. Enqueueing speed `500.01` throws `ArgumentOutOfRangeException` without mutation; admitted angular speed `60.24` at default `h` would travel `0.251` rad and faults `MotionLimitExceeded` without a partial tick. Stored internal quaternions meet the binary32 norm budget `2e-6`, not a binary64-sized `1e-10` obligation. The normalized fixture bits `[0, 0, 0x3f3504f3, 0x3f3504f3]` remain valid; direct equation checks do not constrain stored engine floats beyond their stated contract.

### PH31 - Island sleep and wake (D)
Gravity 0/free zero-velocity body: sleeps after 30 complete quiet ticks (0.5 seconds), velocities zero and query-visible. Impulse at next `Begin` wakes with cause and expected velocity; `EnableSleep` = false prevents this transition. Two joint-connected dynamics sleep/wake together; a shared static floor alone cannot merge unrelated islands.

### PH32 - Kinematic target and waking support (D)
A sleeping unit body on a kinematic floor receives one floor target + X0.1 over a tick. Floor velocity is + X6 during that tick, arrives at target within 0.001, wakes the body and never receives collision-induced velocity. No target next tick means zero floor velocity; target conversion uses the outer dt even if a CCD segment splits the tick.

### PH33 - Hard distance joint (D)
Static anchor 0, dynamic anchor at (1,0[,0]), length 1, tangential velocity + Y1, gravity 0: after 120 ticks length error <= 0.01 m and radial velocity <= 0.02 m/s. Coincident-anchor internal fixture has deterministic stored/ +X axis and named diagnostic, not NaN. `CollideConnected` = false suppresses otherwise overlapping endpoint colliders.

### PH34 - Range joint and bound signs (D)
`Length` starts 1 with range[0.5,1.5], gravity 0/v0: no rest-length force or drift > 0.001 for 60 ticks. `Set` outward radial velocity 1: after 60 ticks length <= 1.505; inward trial length >= 0.495. Lower impulse >= 0, upper <= 0; interior drops stale limit accumulation. Equal limits use a single bilateral row.

### PH35 - Soft distance and frequency limit (D)
`Length` starts 1.1, rest 1, frequency 5 Hz, damping ratio 1, mass 1/static partner. At tick 600 |error| <= 0.005 and speed <= 0.01. Reference coefficient computation has positive finite gamma/b and equals the documented mass-spring-damper formula. Frequency 61 at default h = 1/240 is refused ( > 0.25/h = 60); frequency 0 selects hard joint without division by zero.

### PH36 - Hinge motor, swing, and limits (D)
Coincident anchors/static partner, axis +Z/reference +X in 3D, motor speed 1/`MaxTorque` 10, gravity 0. Within 60 ticks relative angular speed reaches 1 within 0.01; anchor error <= 0.005, 3D swing <= 0.002 rad, reaction torque <= 10 + 1e-4. `Add` limits[-0.25,0.25]: angle never exceeds 0.252, motor into upper limit is disabled while away motion is permitted.

### PH37 - Fixed-joint frames (D)
Valid initial relative angle pi/2 in 2D / quaternion + pi/2 around Y in 3D. Apply impulse and torque to dynamic endpoint with static partner. After 60 ticks anchors differ <= 0.005 m, relative orientation error <= 0.002 rad, no unexpected frame multiplication order; reaction wrench is finite with its microstep duration recorded.

### PH38 - Ball freedom and unsatisfiable locked rows (3D plus 2D refusal)
3D Ball with coincident COM anchors/static partner and omega (1,2,3) keeps anchor error <= 0.005 while freely changing orientation by > 0.1 rad; it is not a hinge. Fixed joint on `RotationLocked` dynamic with angular error 0.1 rad is rejected `UnsatisfiableJoint` before creation; both-static endpoints and antiparallel/invalid hinge frames refuse explicitly in their applicable dimensions.

### PH39 - CCD thin zero-thickness wall (D)
Centered circle/sphere radius 0.05 at x = -0.5, vx240, `ContinuousLinear`, static surface x = 0 spanning remaining axes +/-3. `One` tick ends center x in[-0.056,-0.044], vx <= 0.001; `Begin` fraction is 0.1125 within 1e-4. Discrete trial can traverse between samples and has no CCD guarantee; neither mode inflates the wall into an undisclosed box.

### PH40 - CCD faceted shape and continuous SAT (D)
`RotationLocked` box half 0.05 starts x = -0.5/vx240 against static wall box half-width 0.025. `One` tick must not cross far side; final center x <= -0.069 and contact gap >= -0.006. A parallel sweep separated by a perpendicular SAT axis is a certified Miss; fixed-orientation tangent entry is a Hit.

### PH41 - Relative dynamic CCD (D)
Centered radius 0.05 bodies at x = -0.5/ + 0.5, vx + 120/-120, restitution 1, both `ContinuousLinear`. First impact fraction 0.225 within 1e-4; velocities exchange within 0.005, momentum 0 within 0.001, kinetic energy relative error <= 0.001. No bullet-bullet exclusion, endpoint-only test, or freeze/drop of the remaining tick is permitted.

### PH42 - Rotating CCD-pair refusal (D)
Continuous sphere projectile swept bounds intersect a rotating, unlocked dynamic box (`omegaZ = 1`). The tick faults `UnsupportedRotatingCcdPair` before positional integration, even if exact paths might miss. All committed states, contacts/events/cursor remain unchanged; the message identifies both endpoints and prescribed rotation. A static/fixed-orientation obstacle trial succeeds.

### PH43 - CCD impact budget and rollback (D)
Radius 0.025 body at 0/vx500, restitution 1, surfaces at x +/-0.1; configure `CcdMaxImpactsPerSubstep` = 1. Second impact in the same microstep faults `CcdBudgetExceeded`; no partial bounce poses, `Begin` batch, force consumption or successful tick. With default 8 the intentionally excessive ping-pong still must fault if it needs a ninth, never advance unchecked.

### PH44 - Initial CCD overlap and continuous-sensor exclusion (D)
Radius 0.05 centered on wall x = 0 exceeds 5 mm depth: `CcdInitialOverlap` and entire local physics staging discarded, not automatic ECS rollback. Shallow <= 0.005 contact needs its certificate or faults. Separate fixture: radius 0.025 CCD body x = -0.03, hard distance to static anchor x0.42/length 0.05, wall x0, velocity 0. Its first + 0.01 correction hits at fraction 0.5 and faults `CcdPositionCorrection`; no corrected pose publishes. Sensor-only projectile with explicit mass may traverse between samples without events; zero impulses/no continuous trigger claim.

### PH45 - Range, non-finite, and arithmetic overflow (D)
Body at chunk zero/offset x4095, half-extent 0.5 and vx240 crosses the cell seam successfully when required neighbors are resident; final spatial position is chunk X1/offset near 3 after a 60-Hz tick. Missing required neighbor residency refuses/faults before advancement, not passage through absent collision. Repeat at the outermost int32 cell edge: an overflowing required cell/pose carry refuses with old state/version/cursor unchanged. Explicit calculation-frame requests beyond their extent likewise refuse, independently of total world addressability. NaN/infinity and checked allocation/count overflow reject; no default pose, clamp or Miss.

### PH46 - Capacity and no silent truncation (D)
Body capacity 2, final batch requests 3: all rejected, no partial creation/binding. Candidate capacity 1 with three mutually overlapping enabled dynamics faults when deduplicated pair count is 3. Contact-point capacity 1 with a2/4-point face patch, mesh/query candidate budget exhaustion, and query output-hit cap each produce their explicit failure and unchanged committed/output state.

### PH47 - Explicit rebase and old versions (D)
Touching/sleeping bodies near canonical cell zero/offset x4000.125: prepare a dimensional frame origin at x4000, then commit. Persistent cell/high/low positions, spatial witnesses, velocities, anchors, features, impulses and sleep bits remain unchanged. Derived frame coordinates are near 0.125; frame/structural revision increments once with no contact/wake transition. No mutation before commit or after rejected/disposed plans; active queries/jobs and stale/overflowed requests refuse. Pending/returned spatial witnesses retain their identity; old outputs retain their original frame descriptors. Integration publishes only after stage 9.

Combined structural/frame variant creates an unbound body at canonical x4001/y2 and removes one touching body in `PrepareStructural(N, rebase)`. Commit leaves the new spatial position x4001 unchanged but produces frame-relative x1, preserves removed spatial end witnesses, increments revisions once and first simulates at N + 1. Invalid expected revision rejects all participants. Separate proxy fixture has actual `maxX = 4095.96` and fat `maxX = 4096.06`; it is conservatively represented across shared cell boundaries, and a cache-frame origin change to -0.02 neither drops the padded proxy nor invents an operational world-edge refusal.

### PH48 - Ownership, generations, unbinding, closure (D)
Wrong-physics-owner/dimension, stale-generation and disposed handles fail; index reuse increments generation, and generation exhaustion retires the slot instead of reviving an old handle. Standalone body `BindingId` `Guid.Empty` and a nonempty stable `Guid` round-trip unchanged through raw states/hits/events/tombstones without entity lookup. Assembly/application programming interface (API) check finds only `Core`/base class library (BCL) dependencies, no Entities/`Scenes`/`Hosting` or mandatory `World`. `Integration` unbind enqueues explicit Unbound removal and enriches historical `Entity` values; collider removal ends `ColliderRemoved`. Close drains each remaining/pending episode once before release without a further `Step` or solver callback; repeated Close/`Dispose` does not duplicate ends. Integrated fault/closure variant: after a published episode, a later valid local step ends it and begins a replacement but `Late` faults. External closure ends exactly the previously published episode using retained historical data, never the unpublished replacement.

Same-thread ownership variant: reuse one `PhysicsOwnerContext(currentThreadId)` to construct two separate `PhysicsWorld3D` services with identical options; repeat in 2D. `Create` each service's first body/shape/collider/joint so matching kinds have equal indices/generations. `OwnerIds` are nonzero/distinct; A's body handle is refused by B's `GetBodyState`, `ApplyImpulse` admission and query `IgnoreBody` validation before state/cursor/output mutation. Foreign shape/joint handles, request IDs and prepared plans are likewise refused. B's own handles remain valid after A closes; a third service never reuses either identity. Context's public fields contain no caller `OwnerId`.

`StrictLocal` replay/restore variant (standalone D and bound-ECS `Integration` fixture): capture after a committed tick with touching awake bodies, a sleeping contact island, a motor joint, a pending `FinalPrevious` End and next-tick commands. `Create` physics identities in non-globally unique identifier (GUID) order, remove/reuse slots, retain a retired slot and at least two ordered free slots per populated arena; use deterministic allocator fixtures for retirement. Include a canonical nontrivial quaternion in 3D, canonical 2D angle, permitted signed-zero velocity and distinct previous/current snapshots. Integrated fixture additionally has non-GUID entity row order, chunk ordinal gaps, next ordinals and stable command partitions. `StrictLocal` restores under new process owner IDs with exact saved slot indices/generations/retirement/free-list/allocation/traversal order, feature/impulse/sleep state, pending keys/cursors and ECS layout. Compare all canonical scalar bits and order metadata exactly before any `Step`; saved GUID relationships match, old source handles refuse, and activation emits zero physics transitions/tick advancement.

Continue original and restored branches for 120 ticks with the same recorded inputs/profile, including new body/shape/collider/joint allocations/removals and recorded stable GUID assignments. Per-tick canonical checksums, GUID-normalized event/order traces, allocation indices/generations, command consumption and logical outcomes match exactly; exclude process owners and profiling time. No authoring renormalization, hidden warm-up tick or GUID-sort recreation is permitted. Mutating saved order metadata must fail compatibility/integrity or appear in the canonical checksum, not be ignored. Incompatible strict profile/layout or noncanonical/non-finite quaternion rejects before activation; the old service/world remains unchanged. `Standard` restore may repack with correct GUID relationships but cannot claim this strict result.

**Boundary-admission obligation (existing PH IDs; unexecuted).** Exercise both endpoints of every finite closed bound in [22](22-custom-physics-world-and-solver.md) and [23](23-custom-physics-shapes-queries-and-ccd.md), accepting the endpoint values and rejecting the nearest values outside them under the owning PH category: PH01 covers `Substeps` `[1, 16]`, `VelocityIterations` `[1, 64]`, and `PositionIterations` `[1, 16]`; PH03/PH08 cover mass `[0.001, 100000]` kg, inertia eigenvalues `[1e-7, 1e9]` kg m^2, and gravity scale `[-4, +4]`; PH09 covers density `[0.001, 100000]` in each dimensional unit, polygon vertex count `[3, 16]`, polyhedron face-loop index count `[3, 64]`, and `SnapTolerance` `[0, GeometryTolerance]`; PH20/PH45 cover body origins, actual collider AABBs, centers of mass, and swept bounds on every active operational-coordinate axis `[-4096, +4096]` m; PH18/PH20 cover ray `MaxDistance` `[0, 8192]` m; PH25 covers friction `[0, 2]`; PH30 covers damping `[0, 1000]` /s; PH34/PH35 cover joint length `[0.05, 128]` m and damping ratio `[0, 2]`; PH36 covers `MaxTorque` `[0, 1e9]` N m. For integer counts, test one integer below and above each endpoint; for scalar limits, use the nearest finite representable input values outside each endpoint in that field's declared type. Rejected world updates, shape cooks, and queries leave committed state, published shapes, and output buffers unchanged. These are additions to existing PH cases, not new IDs or evidence of execution.

PH09's existing M03Primitive2D/M03Primitive3D observations additionally bind the complete [10 M03 6 wire inventory](10-binary-data-and-authoring-formats.md#m03-6-complete-replacement-physics-wire-inventory) to independent descriptor/reader/writer/generated-binding tests. Select exact/nearest-outside converted limits, analytic nullable presence, nested measure normalization, schema-version/profile/settings joins and correctly rehashed negatives under the existing AS/BN/AR/EN/PH IDs. A separately selectable primitive-boundary observation covers all 61 exponents and stored-pair rounding without implying full M06 Physics profile/handle/solver qualification; no new PH ID is created.

## 3. Minimum implementation suites and workloads

| Suite / reproducible workload | Mandatory evidence; still unexecuted |
|---|---|
| Pure geometry | Named supported-pair cases for containment, tangency, coplanarity, witness normals, winding and degeneracy, with direct analytic expectations and declared error budgets |
| Broad-phase behavior and workload | 10000 colliders across insertion/removal/enable/teleport/sleep/rebase workloads; explicit expected candidate-pair cases, zero missed declared overlaps, and measured tree height/storage growth |
| Stack solver | PH27 plus 2D 20-box stacks and 3D 10x10 columns of 10 boxes for 3600 ticks; report residuals, energy drift, sleep/churn, allocations and counts; larger workload gates cannot waive PH27 |
| Joints | Each joint, mass ratios 1:1/1:100, 20-link distance/hinge chains, limits/motor/softness, conflicting loops and locked rows; max/RMS errors and reaction impulses for 3600 ticks |
| Queries | 10000 mixed shapes, 262144-triangle tiled scene within local bounds, 10000 recorded rays/sweeps/overlaps; exact results, inside policies, distributions of feature tests, buffer/budget failures |
| CCD | PH39..44 plus explicit translation-only sphere/polyhedron cases; fast thin surfaces/dynamic pairs, tangent misses/hits, simultaneous contacts, impact budget and rollback |
| Transactions/lifetime | Local physics staging failures versus valid-local-commit/later-`Late`/`Structural`/`Extract` world faults; stage 9 suppression/whole-world checkpoint recovery, raw/enriched tombstones, no leaked pin/stale handle/solver callback |
| Standalone/DAG/integration | `Core`-only public assembly/API baseline; standalone construction/`Step`/structural/rebase without ECS; stage/system/partition/sequence ordering, `BindingId` passthrough and `Integration`-only entity/root validation |
| Platform/replay | PH48's `StrictLocal` capture/restore/120-tick continuation with non-GUID physics/ECS order, canonical bits, allocator gaps/retirement/free lists and pending cursors; separately qualify Windows/Linux x64 and scalar/accelerated paths. Same-profile hashes certify only that profile, not cross-platform identity |
| Performance | `Headless` steady-state scenes above, warmup and named hardware, elapsed median/p95/max, allocation bytes/tick, memory peak, narrow-phase tests and CCD segments; no unmeasured throughput promise |

For workloads exceeding a version-one admission cap, partition/tile explicitly or expect refusal; never shrink a shape/count secretly. No allocation-free guarantee is inferred from pooled internals: immutable event/public snapshot output requires a measured budget during implementation qualification. Any later acceleration must preserve this specification's geometry and failure outcomes; a slower duplicate engine is not a prerequisite for that comparison.

For a Physics acceptance claim, report every PH ID as passed, failed, or not run, including required and executed dimension/variant counts and the tested revision, using ordinary NUnit/workload results; no separate acceptance runner or persistent qualification ledger is required. Running a reference formula helper is not running PH27, the contact solver, collision code, the C# API, or cross-platform tests. Acceptance covers the integrated revision and consequential contracts, not isolated worker summaries.

## 4. Analytic evidence limits (informative)

Small direct scalar checks can establish finite equation behavior for mass/moments, damping/fixed-time sums, impulse signs, friction projection, soft coefficients, interval/root arithmetic, quaternion composition and ULP/rebase behavior. They do not execute a contact solver, scene loop or stability workload and cannot establish PH acceptance. Qualify the implemented cases and their dimension/variant observations through the repository's NUnit workflow; no duplicate implementation or private run record is a prerequisite.

## 5. Primary sources and departures (informative)

Catto's [official publications index](https://box2d.org/publications/) lists the titles/years below. These published references inform the stated algorithms and contracts; publication availability or equation review does not prove the custom 3D solver or its implemented acceptance cases.

| ID / primary reference | Used for; not a borrowed implementation promise |
|---|---|
| P1 Erin Catto, Sequential Impulses, GDC2006: https://box2d.org/files/ErinCatto_SequentialImpulses_GDC2006.pdf | Accumulated normal/friction impulses, warm starting; this unit specifies its own iterations/tolerances |
| P2 Erin Catto, Contact Manifolds, GDC2007: https://box2d.org/files/ErinCatto_ContactManifolds_GDC2007.pdf | Reference/incident features, clipping and persistence concepts; 3D manifold reduction here is an engine decision |
| P3 Erin Catto, Understanding Constraints, GDC2014: https://box2d.org/files/ErinCatto_UnderstandingConstraints_GDC2014.pdf | Jacobian/effective-mass constraint framework; exact joint set/failure policy here is narrower |
| P4 Erin Catto, Soft Constraints, GDC2011: https://box2d.org/files/ErinCatto_SoftConstraints_GDC2011.pdf | Mass-spring-damper motivation; gamma/b equations here are explicitly algebra-derived, not a quoted slide |
| P5 Erin Catto, Continuous Collision, GDC2013: https://box2d.org/files/ErinCatto_ContinuousCollision_GDC2013.pdf | TOI concepts and tunneling problem; this unit instead requires complete fixed-orientation SAT/feature sweeps and refuses rotating CCD pairs |
| P6 Erin Catto, Dynamic Bounding Volume Hierarchies, GDC2019: https://box2d.org/files/ErinCatto_DynamicBVH_GDC2019.pdf | Dynamic-tree acceleration; the specified balancing/tie/rebuild policy is this engine's own bounded choice |
| P7 Erin Catto, Numerical Methods, GDC2015: https://box2d.org/files/ErinCatto_NumericalMethods_GDC2015.pdf | Numerical-methods reference; no claim that fixed stepping establishes portable bitwise determinism |
| P8 Erin Catto, Solver2D,2024-02: https://box2d.org/posts/2024/02/solver2d/ | Directly verifies accumulated-impulse clamping, separate NGS pose correction, block contacts, soft constraints and substep tradeoffs; article distinguishes algorithm experimentation from performance validation |
| P9 Official Box2D collision documentation: https://box2d.org/documentation/md_collision.html | Primitives/surfaces, contact manifolds, tree, inside-ray and TOI limitations; our `ReportInitial` default intentionally differs from its inside-ray Miss default |
| P10 Official Box2D simulation documentation: https://box2d.org/documentation/md_simulation.html | COM versus origin, body types, damping approximation, fixed/substep and sleep concepts; our transactions/structural timing/CCD pair rules are not Box2D's |
| P11 Microsoft `Quaternion.Concatenate`: https://learn.microsoft.com/en-us/dotnet/api/system.numerics.quaternion.concatenate?view=net-10.0 | BCL active rotation order; concatenation value 1 then value 2, equivalent to multiply (value 2, value 1), not CPU row-matrix multiplication order |

No source is an external physics dependency or copied engine code. SAT/feature enumeration, tetrahedron integrals and translational root equations are specified and independently derived here; no invented "Catto3D solver" attribution. Published2D ideas do not validate our3D contacts, mesh patch culling, numerical bounds, or stability thresholds. Full rotating CCD, gyro-correct 3D tops, capsule support and arbitrary scaled hierarchy binding are honestly excluded rather than deferred unspecified branches in version-one implementation.
