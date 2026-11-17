# M05 Seek and particle draw-order addendum

## Status and scope

**Proposed; a tentative unblocker under the current user authorization, retained for later review.** This addendum resolves only the two accepted blocking findings against `m05-owner-contract-closure-rectification.md`, SHA-256 `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632`: selector-less multi-clip Seek, and cosmetic particle draw identity/order. The recommendations below supplement that exact proposal; they are not descriptions of implemented behavior or amendments to maintained design chapters.

Only this new proposal is changed. No public signature, schema tuple/tag, acceptance ID, production source, test, milestone status, commit, or remote publication is changed. No build, test, native qualification, or implementation acceptance is claimed. The rectification's section 6 declaration holdpoints and actual-producer gates remain in force.

### Existing evidence, not proposed behavior

| Fact | Frozen evidence |
|---|---|
| Seek selects an Entity, absolute `TimeSpan`, and explicit event policy, not a clip slot or layer. | `docs\design\30-public-api-review-surface.md:3672–3698`. |
| The owner permits eight layers/four active clips per layer, quotient-based seeks, reverse events, and once-per-play completion. | `docs\design\20-animation-and-deformation.md:13–17,31–47`; rectification sections 2.2–2.4 supply the proposed state fields and transitions. |
| Existing sampling accepts a selected Clip and integral local time; it does not select which player instances a command affects. | `AnimationClipAsset.cs:13–98`; `AnimationClipSamplingKernels.cs:16–168`. |
| Ordinary Sprite identity is `NameUuid(entityId, "sprite/0")`; sorting uses signed layer/order, descending camera-view Z, then RFC draw GUID. | `docs\design\21-2d-and-3d-rendering.md:29,49`; `Renderer.Extract.cs:389–423,529–545,848–849`. |
| Snapshot validation rejects duplicate Sprite draw GUIDs and requires each camera order to be a complete permutation. The current Sprite row and camera-order carriers are internal. | `RenderSnapshot.cs:184–243`; `RenderSpriteSnapshot.cs:10–30`; `RenderSpriteCameraOrder.cs:8`. |
| Name UUIDs use the fixed SHA-256/RFC-byte recipe, not platform hash codes or mixed-endian GUID memory. Hash-derived identities still require collision validation. | `docs\design\10-binary-data-and-authoring-formats.md:115–121`; `FoundationNameUuid.cs:14–53`. |

Source basenames above refer to the complete paths in section 4. The following sections are the new recommendations.

## 1. Selector-less Seek

### 1.1 Unchanged operation and selected instances

Keep the existing member on `TerraFX.Animation.AnimationController` exactly:

```csharp
public Task<AnimationCommandReceipt> SeekClipAsync(
    Entity entity, TimeSpan position, SeekEventPolicy eventPolicy,
    CancellationToken cancellationToken = default);
```

Recommend **broadcast absolute-time Seek**. At the command's stage-2 application point, after all preceding accepted commands, select every active clip instance in every layer of that player's current state. Include transition sources/target, zero-weight active instances, zero-weight layers, and masked-out instances. A clip active in two layers is two selected instances. Do not select inactive allowed slots, infer a primary clip, normalize time to a target's duration, or add an API/control selector.

Validate the exact receiving Entity/Scene/controller binding under the rectification's command contract. Negative `position.Ticks`, an undefined policy, and invalid argument shape are synchronous usage errors. A live admitted player with no active instances yields `Rejected`, with an Error diagnostic and no applied tick; it does not initialize its definition's initial clips. Application-time binding or capacity refusal also yields the existing `Rejected` receipt. Rejection installs no partial player state or provisional event and does not suppress that player's otherwise due ordinary advancement. It does not rewind a command sequence already consumed by accepted admission.

### 1.2 Absolute clocks and complete state transformation

Let `p = position.Ticks`, an integer in `[0,long.MaxValue]`, and let `D > 0` be each selected instance's own retained Clip duration. Use its pre-command state as the source:

| Clip mode | Source integral unwrapped time | Destination |
|---|---|---|
| Loop | `oldLoopOrdinal * D + oldLocalTicks`, with `0 <= oldLocalTicks < D` | `newLoopOrdinal = p / D`; `newLocalTicks = p % D`. |
| Clamp | `oldLocalTicks`, with `0 <= oldLocalTicks <= D` and ordinal zero | `newLoopOrdinal = 0`; `newLocalTicks = min(p,D)`. |

Use checked widened integer arithmetic, including for negative source loop ordinals, comparisons and crossed-event counts; do not construct a global floating time or iterate every crossed loop. These products and differences fit signed 128-bit arithmetic for the declared signed-64-bit operands. Sub-tick `Fraction` is not sampled time and does not enter a crossed-event interval.

Prepare all selected instances, poses, event records and ownership before installing any of this command's changes:

| Existing field/state | Successful Seek disposition |
|---|---|
| `LocalTicks`, `LoopOrdinal` | Install the independently mapped destination above for each instance. |
| `PreviousTicks`, `PreviousLoopOrdinal` | Set equal to that instance's new local time/ordinal. Retain the old values only in bounded provisional traversal work, not as the next tick's motion history. |
| Active-clip `Fraction` | Set all 24 bytes to zero, including instances whose integral time did not change. |
| `ForwardCursor` | Count of native marker records with `TimeTicks <= newLocalTicks`: the first record strictly after the destination. |
| `ReverseCursor` | Count of native marker records with `TimeTicks < newLocalTicks`: reverse traversal starts by decrementing this count. Both cursors lie in `[0, Events.Count]`. |
| `LastDirection` | Forward for positive integral unwrapped displacement, Reverse for negative displacement; preserve its admitted value for zero displacement. Direction is not inferred from player Speed. |
| `StartDelivered` | Preserve. Seek never rearms or substitutes for explicit Play's initial-zero delivery. Crossed loop-zero occurrences remain ordinary interval occurrences. |
| `Completed` | Preserve true; never rearm completion. A first nonzero Clamp traversal reaching its directional terminal consumes completion as specified in section 1.3. Loop instances do not acquire completion. |
| Player `LastSampleTick` | Set to the executing tick for the successfully prepared sample. |
| Clip slots, per-instance `PlaySequence`, player `NextPlaySequence`, active instance membership, instance weights | Preserve exactly. Seek starts no new play and does not add, remove or merge instances. |
| Layer weights, modes, masks, reference poses, target bindings/baselines and player Speed, including signed-zero bits | Preserve exactly. |
| Every existing `animation/transition-state` field | Preserve `TransitionId`, `ElapsedTicks`, its separate U16 `Fraction`, `SourceWeights`, and `TargetClipSlotId` exactly. Seeking past the fade duration does not finish/restart the fade. |
| World clock/rate/time scale | No command-specific change or hidden tick. |

Recompute both event cursors from the retained native marker array even for PoseOnly, None-policy, masked or zero-weight instances. The forward cursor excludes destination-time markers from the next forward interval; the reverse cursor excludes them from the next reverse interval. At loop boundaries the traversal operator also observes the paired end/zero rules below; a local cursor alone is not the complete boundary state. Do not rewrite the native event array.

Evaluate the pose at the new integral times with the existing kernels and unchanged blend/transition weights. A successful Seek, including a zero-distance Seek, marks the existing render-history discontinuity. It suppresses **all ordinary clip-time and fade-elapsed advancement for that player in this stage**, not just the base-layer target. Other players advance normally. Next ordinary tick resumes from the new local times, zero clip remainders and preserved fade clock/remainder.

Seek itself evaluates no transition action and does not bypass an interrupt policy. Later explicit commands still execute in accepted order. The rectification's normal automatic-transition choice pass follows the commands, using the resulting pre-transition state and manual-request precedence. It may make an independently eligible transition; it does not treat Seek's position as fade elapsed time. The preservation table describes Seek's own transformation, not immunity from a later command or that normal choice pass.

No motion is extracted across the Seek discontinuity. For an Extract-mode player, this stage's request is valid identity motion for its mapped Actor/current tick, with zero Translation, identity Rotation and `SourceCount=0`; Ignore-mode players have no extraction request. A later command cannot turn the discarded Seek jump into physical travel. Subsequent ordinary root motion starts from the new sample. Existing dynamic-root ownership refusals still apply to Scene tracks.

### 1.3 Events, completion and atomic refusal

**PoseOnly** emits no marker or Completed records, regardless of distance, wraps or event policy. **EmitCrossedEvents** traverses independently for every selected instance from its old integral unwrapped time to the destination: `(old,new]` forward, `[new,old)` reverse, and empty for equality. Clamp uses its clamped destination, not excess requested time.

For Loop, a native marker at local time `e` in loop `k` occurs at unwrapped time `k*D+e`. Thus end-of-loop `k` and zero-of-loop `k+1` share one unwrapped boundary. If the interval includes that boundary, forward emits preceding end before new zero; reverse emits new zero before preceding end. If the boundary is the excluded source endpoint, neither is replayed. Payload `LocalTime` and `LoopOrdinal` identify the actual native occurrence, not merely the destination's wrapped pose. Same-time marker IDs follow the rectification's traversal RFC ordering.

Use the event policy already selected by the retained definition: an active transition's EventPolicy while that transition is present, otherwise the layer's EventPolicy. Both permits each selected instance; TargetOnly permits only the active transition target, or all active instances outside a transition; None permits none. These policies filter emission, not clock/cursor updates. Weight/mask zero is not another event filter. Policy is resolved from the pre-command state, so this Seek's newly reached completion cannot retroactively select a different event source.

For Clamp, a nonzero traversal reaches its directional terminal when it moves forward to `D` or backward to zero. If `Completed` was false, set it true once for that play:

- PoseOnly consumes this completion silently and supplies no new Completed condition edge, now or on a later tick.
- EmitCrossedEvents creates one new internal completion edge; emit Completed only if the selected event policy permits it, after terminal markers. An emission-suppressing policy does not disable transition conditions.
- A previously true latch stays true after rewind and emits no second Completed. Only a new Play rearms it. Equality creates no completion edge.

New internal edges from EmitCrossedEvents are available to the normal same-stage automatic-transition pass. PoseOnly does not leave a deferred edge to synthesize after restore. A Completed condition is never evaluated by treating the stored true latch as a fresh edge.

Pre-count the **aggregate emitted** markers and Completed records over all selected instances. Apply the rectification's 4096/player/stage and 65536/World/tick envelopes, including records from earlier commands, plus its existing byte/control/pin limits. None-policy records cost no event slots; their state transformations still require admission. A demand beyond the remaining envelope rejects the whole Seek before any time, latch, cursor, pose or ownership transfer changes. In particular, 4096 is not a separate allowance for each clip/layer. Use bounded arithmetic/counting, not materialization of an over-limit traversal.

Invalid/nonfinite sampled poses, invalid additive ratios, or inadmissible sampled spatial arithmetic retain their owning sampling/tick-failure contract, before installing this Seek. They are not capacity refusals, identity-pose repairs or successful no-ops.

Append each successful command's occurrences in accepted-command order within its player/layer/clip-slot stream, then apply the rectification's existing player/layer/clip-slot merge and dense publication sequencing. Never re-sort successive Seek traversals by their destination time or ordinal. Earlier command occurrences are not erased by a later Seek. Events remain provisional until stage 9; the stage-2 Applied receipt still does not promise a successful encompassing tick.

### 1.4 Pins, persistence and distinguishing cases

Seek admits no asset load, new clip slot, replacement backing or playback instance. Its existing independent-use/command envelope and any command-owned retained binding stay owned until actual application/cleanup. It must not release another command's or an active instance's playback pin. Selected instances remain actual sampling/held-pose/transition consumers; keep their separate playback pins through that use, including a completed Clamp still held by the player. A completion notification alone is not proof of retirement. Ordinary Stop, transition removal, completed-instance retirement and World retirement release only the ownership whose actual consumers ended. Definition/prepared-source and old snapshot pins remain independent.

Record one applied `animation/control` Seek using the existing PlayerId/CommandSequence/PositionTicks/SeekPolicy fields; add no selector or schema field. Standard Branch capture preserves the resulting per-instance times, both cursors, latches, LastDirection, clip fractions and unchanged transition state exactly. The post-restore join rebuilds the destination pose without a Seek traversal, motion jump, callback or event replay. RecordedContinuation remains unsupported as in the rectification.

Required focused cases are recommendations, not executed results:

- With a manual-only A→B fade, A Loop duration 1 s, B Loop duration 2 s, and a layer-1 Clamp duration 0.75 s, all initially at 0.25 s: Seek 1.5 s maps A to `(loop1,0.5 s)`, B to `(loop0,1.5 s)`, Clamp to `(loop0,0.75 s)`. Current A/B weights 0.75/0.25 and fade elapsed 0.05 s stay unchanged, even though 1.5 s exceeds its 0.2-s duration. All clip fractions reset; an admitted nonzero fade fraction is preserved. Repeat with the layer masked/weighted zero.
- For a 1-s Loop with markers at 0/end, EmitCrossedEvents 0.25→1.0 s emits end(loop0), zero(loop1); 1.25→1.0 s emits zero(loop1), end(loop0). Both leave local time zero, forward cursor 1 and reverse cursor 0. PoseOnly emits neither. Seek to the current integral position still clears its clip remainder without inventing a marker.
- A first Clamp seek to its terminal consumes completion; PoseOnly does so silently. Rewind then seek there again: terminal markers may recur under EmitCrossedEvents, Completed does not. A new Play rearms it. Separately distinguish a Completion-conditioned transition from a manual-only fade.
- Put the same Loop Clip in two layers, with 2048 selected markers per traversal: the aggregate is 4096, not 2048. A 2048+2049-marker pair demands 4097 and wholly refuses. Test remaining-stage/World limits separately; refusal preserves every selected instance and pin. Test opposite per-instance Seek directions despite one unchanged player Speed.
- Save/restore after a multi-layer mid-fade Seek and compare literal next-clock/cursor/pose expectations through the real owner path. Old snapshots retain old poses/assets; the Seek tick supplies identity desired root motion. Cancellation, later tick failure and cleanup retain the rectification's existing channels.

## 2. Cosmetic particle draw identity and merged Sprite order

### 2.1 Scope, ordinals and reset

Define a particle's full live identity as **(exact World construction-bound pool, Generation, ParticleId)**. These are cosmetic facts, not Entity membership. The copied qualified Emitter and EmitterGeneration continue to select the retained birth definition/resources; an emitter lookup against the current Scene is never needed to identify an old particle.

Make the existing public `Generation` value a **pool-owner-local generation ordinal**, not a process-global counter or a value comparable across Worlds:

- A freshly constructed pool starts at Generation=1 and NextParticleId=1. Zero/default is unissued. The selected surface has no in-place reset/restart operation; a Faulted pool does not silently restart on content change.
- Restore/replacement constructs a new exact World/pool binding, starts its empty generation at ordinal 1, and marks lost history/discontinuity. The new **qualified generation** differs even when its numeric ordinal, WorldGuid and restored emitter identities equal the old ones. Do not carry omitted cosmetic counters/PCG state through a checkpoint merely to make numeric ordinals globally monotonic.
- Within that owner, keep ParticleIds positive, contiguous per wholly accepted request and never reused, including after expiration or pool-slot reuse. Use the rectification's emitter-RFC/producer-sequence admission order. Refused spawns and failed enclosing ticks publish no issued IDs or RNG consumption. Checked exhaustion faults the pool; it never wraps.
- Emitter definition/backing/seed/stream changes use the rectification's new EmitterGeneration/fresh stream, **not** a reset of pool Generation or NextParticleId. Existing particles keep their birth row, ID and assets. Enable/disable, movement, Scene unload, rebase, recording rotation, extraction/render frequency and caller output disposal also do not reset those ordinals.

There is no new issuer service, global dictionary, canonical RNG/state registration, reset API or persistence field. Restoring cosmetic state is still not supported. Old outputs remain attached to their original owner scope, copied values and retained backing.

### 2.2 Exact draw-GUID recipe

For each particle, select the existing `TerraFXNameUuidSha256V1` recipe:

```text
namespace = the pool's captured WorldGuid
name      = "particle/v1/" + Hex16(Generation) + "/" + Hex16(ParticleId)
DrawId    = NameUuid(namespace, name)
```

`Hex16` is exactly sixteen lowercase ASCII hexadecimal digits, most significant first, with leading zeros, no sign/prefix/separator/culture conversion. Inputs are admitted positive signed-64-bit values. The name is exactly 45 bytes. The namespace uses canonical RFC bytes; use Chapter 10's existing domain prefix, UInt32LE name-byte length, SHA-256 truncation and UUIDv8/variant rules. This is a derived render identity, not a new schema/key assignment in the content catalogue.

Pool-wide ParticleId uniqueness makes EmitterGeneration unnecessary in this render key; neither two emitters nor an emitter rebind may restart that counter. Keep the qualified Emitter/EmitterGeneration separately for retained content, recordings and diagnostics. Do not use the emitter's ordinary `"sprite/0"` ID for its billboards. Ordinary Sprite and Mesh-group recipes remain unchanged.

The ID is camera-independent and stays unchanged for that particle through motion, age/curve changes, rebase, culling, batching and retained snapshots. A PlaneXY particle eligible in both dimensional extraction paths has the same draw GUID in those separate snapshots. CameraFacing3D changes its copied basis per camera, not its logical identity.

As with an ordinary Sprite drawn in several snapshots, DrawId alone is not globally unique across output lifetimes. New same-GUID World bindings may derive equal GUIDs after reset. SnapshotId/exact captured binding and independently retained roots distinguish those outputs; equality cannot authorize cross-binding reuse, asset lookup, reconciliation or particle continuation. Particles remain omitted from the rectification's entity-picking table.

### 2.3 Collision and publication behavior

Domain-separated names do not prove that truncated hashes cannot collide. Before installing a snapshot, validate the complete eligible logical draw set across selected cameras, before frustum culling/batching:

1. Each live particle contributes one logical draw key/ID, not one ID per camera. Duplicate particle keys or two distinct particle keys with equal DrawId are invalid.
2. A particle's DrawId must differ from every other logical draw ID in that snapshot, including ordinary Sprite and present Mesh-group IDs. Preserve existing ordinary-draw checks; this adds only checks involving the new particle domain.
3. An animated ordinary Sprite's intentionally related weighted subdraws remain one original logical-ID block under the rectification; they are not distinct particles or permission to accept unrelated duplicate IDs.

Invalid keys/mapping/collision fail the whole extraction with `InvalidDataException`, before snapshot publication or native work. They are not CapacityExceeded, RejectSpawn, a partial output, or a reason to drop a draw, add salt, choose another hash, consume RNG/IDs, advance Generation, or retry until a convenient identity appears. Existing committed pool state and independently retained old outputs remain unchanged. Preserve existing limit-check precedence and reserve collision/merge scratch under the rectification's existing output/World envelopes.

The current C0 constructor's unique-ID and camera-permutation checks remain evidence/invariants to preserve, not a particle implementation port. Carry actual copied particle IDs in the extended immutable billboard roots and validate their merged order. Do not fabricate live Scene/Entity rows or weaken the old composition's checks to squeeze particles through its current internal ordinary-Sprite carrier.

### 2.4 Exact mixed ordering

Capture the union of ordinary Sprite logical blocks and particle billboard blocks eligible for at least one selected camera, under the existing dimensional/layer rules. For each camera in the existing signed-Order/RFC-camera-ID selection order, form **one merged sequence over that complete captured set**, not a separately compacted per-camera subset. Apply the same lexicographic comparison to both kinds:

1. Signed `SortingLayer`, ascending.
2. Signed `SortingOrder`, ascending.
3. Finite captured `CameraViewZ`, descending, using the existing comparison; positive/negative zero compare equal. No epsilon, Euclidean-distance key or global world-Z substitution.
4. DrawId, ascending unsigned canonical RFC bytes.

For an ordinary Sprite, keep its existing pivot and sorting fields. A particle uses its current copied center/pivot position and its **retained birth definition's** SortingLayer/SortingOrder/LayerMask, not a newer emitter definition. Compute view Z through that same snapshot camera/frame path. Camera-facing orientation does not change the center's depth key.

At equal first three keys, the RFC draw GUID alone decides interleaving: no ordinary-first/particle-first flag, emitter order, ParticleId numeric order, birth time, pool slot, worker arrival, material, Atlas or batching key precedes it. Distinct logical blocks cannot tie on the final key after successful collision validation.

Keep an animated ordinary Sprite's weighted subdraws contiguous at its original logical key, in the rectification's existing clip-slot order. Each particle is a single billboard block. This does not reduce weighted-subdraw or billboard counts under the 131072 draw ceiling. The per-camera order is a complete permutation of the captured merged logical set; ordinary camera-layer/frustum filtering preserves the surviving relative order. Batch only adjacent compatible entries; separate particle/Sprite passes must not reorder that sequence. Existing Mesh pass ordering is unchanged.

All IDs, keys, per-camera orders, billboard bases and resource pins are captured through the genuine extended World extraction cohort. Render/native work follows no live pool/emitter references. Private tagged indices/column layout are implementation details; this contract adds no public snapshot member or changes the existing no-feature composition.

### 2.5 Literal distinguishing example and focused evidence

Use WorldGuid `00000000-0000-4000-8000-000000000001`, ordinary Sprite EntityId `00000000-0000-4000-8000-000000000004`, Generation=1, and particle IDs 1/2. These are scalar recipe vectors, not fabricated issued World/snapshot evidence:

| Logical block | Namespace | Exact ASCII name | Expected draw GUID |
|---|---|---|---|
| S | Sprite EntityId above | `sprite/0` | `c15502f2-4af1-81f8-9b6a-05cfd8ade993` |
| P1 | WorldGuid above | `particle/v1/0000000000000001/0000000000000001` | `a6091218-0ca7-8814-b396-62b8d76521ac` |
| P2 | WorldGuid above | `particle/v1/0000000000000001/0000000000000002` | `d711ff7d-da52-85bd-b573-708e47575242` |

With equal layer/order/view Z, the exact order is **P1, S, P2**, neither particles-first nor ordinary-first. Give P1 red, S blue, P2 green, each alpha 0.5 over transparent black with the existing premultiplied SourceOver rule: the linear accumulator before tone mapping is `(0.125,0.5,0.25,0.875)`. This is not a claim about final encoded native pixels. The UUID literals were independently derived from Chapter 10's byte recipe, not generated by calling production output as the expected-value source.

Required focused cases, not results:

- Use the real emitter/Scene/fixed-tick/Headless producer to check stable IDs and mixed order; vary extraction frequency, camera rotation, selected camera order, emitter motion and rebase. Two equal-depth particles from one emitter must not share `"sprite/0"`. Exercise PlaneXY/CameraFacing3D only in their selected dimensional paths.
- Replace definition/backing or seed/stream while old particles remain, then unload the emitter. Old IDs/order inputs/assets remain their birth values; new births continue the pool-wide ordinal. Expiration/slot reuse and recording rotation do not recycle IDs.
- Restore the same checkpoint into new exact bindings: empty pools/new discontinuities, owner-local Generation=1 and fresh ID streams, not resumed old particles. Equal derived GUIDs in separately retained snapshots do not alias their roots or assets.
- Feed a literal duplicate candidate ID through the owning prepared-draw validation path to distinguish collision refusal from capacity and from a legal weighted-subdraw relation. This qualifies that validation only, not a real cryptographic collision, public producer, or native observation; add no hash override/test-only issuer.
- Retain actual old outputs through replacement/close and actual GPU use when that producer exists. Native overlapping pixels and fence retention remain separate observations; a helper sort or the scalar recipe vectors cannot establish them.

## 3. Remaining gates

These recommendations close only the two named semantic decisions for the rectification's Animation and cosmetic-particle units. They do not close its private phase/post-restore ports, multi-major component providers or ResourceState aggregate providers. Existing budgets, schema versions, Standard Branch versus RecordedContinuation boundary, M03-B use/retention, M04 native producers and M06/M07 feedback/noninterference prerequisites are unchanged. No new acceptance ID or qualification program is selected.

Implementation remains responsible for the directly affected owner documentation and focused existing Animation/particle acceptance slices already assigned by the rectification. This addendum supplies no execution result or permission to bypass another declaration holdpoint.

## 4. Frozen inputs

Baseline HEAD: `31f156a51ccc462ae9c5cc8d4e87e00c0054dec4`. Freeze date: **2026-11-16 UTC**. The following **18 complete files** are this addendum's direct input set, including files read only in applicable ranges. Hashes are raw-file SHA-256. `C`=unchanged at baseline; `M`=modified; `U`=untracked. Modified/untracked bytes are intentional inputs, not replaced by HEAD.

The rectification's own 73-file manifest was also reverified before using that proposal and again after authoring. This smaller table records the addendum's actual direct inputs, not a second broad inventory or a recursive copy of that manifest. Filename/symbol discovery and Git status are context, not additional byte-reviewed inputs. Other dirty files were not dispositioned. This new output excludes its own recursive self-hash.

| Input | State | SHA-256 |
|---|---|---|
| `docs\proposals\m05-owner-contract-closure-rectification.md` | U | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| `docs\project\Roadmap.md` | C | `C4FBE82155A6D82FFD2D13AD2F18056D938738C4FA5A66760C4877E24ED001C7` |
| `docs\design\10-binary-data-and-authoring-formats.md` | C | `56721136AECAE18C20EB3E9A1BECEB527CC0676DD6B43CB506538AD63DECBB5F` |
| `docs\design\13-jobs-and-frame-pipeline.md` | C | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| `docs\design\16-scenes-transforms-and-cameras.md` | M | `B012F42B23CEDE9C7CDB71203D8357CD19E1626823EA25EF3119961292D3698A` |
| `docs\design\20-animation-and-deformation.md` | C | `BBEF0A36E0832C2BD0D54A07E479917DB923671B64F7176369F8F57A8F1618C5` |
| `docs\design\21-2d-and-3d-rendering.md` | C | `989A6183186592F165FA9614CC19A8FED63826E2A51170CE55FBFFFCA8116ED2` |
| `docs\design\27-state-serialization-replay-and-recovery.md` | C | `B9B6324C5B1B1543CA1D2287ECE579597A7E7AA62D885031C4FF1BB629F08777` |
| `docs\design\30-public-api-review-surface.md` | M | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| `docs\design\35-implementation-roadmap.md` | C | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `sources\TerraFX.Core\FoundationNameUuid.cs` | C | `D11F4E31287C9CA9BE7FFAC6E0008033F16E33706E3E59CD7B814C99DD593AE2` |
| `sources\TerraFX.Runtime\Entities\WorldEntityIdentity.cs` | C | `09B2A54CC3ADDEE27331A9BB5D4B6486C27CA506B5B3A8F2C2D022BA49AC4A2C` |
| `sources\TerraFX.Runtime\Animation\AnimationClipAsset.cs` | C | `AFB62F4EC29D3CEFCFB46D529699BC04FFB19F3E2D79D22BD4353350B91B0539` |
| `sources\TerraFX.Runtime\Animation\AnimationClipSamplingKernels.cs` | C | `E2ECEFF51F175038833C1883D7EDF522F679BA0F6D31CEF8EC5F9E62F037AC3D` |
| `sources\TerraFX.Runtime\Rendering\Renderer.Extract.cs` | C | `5D3B4C9D739316542629EA7A40B60D8558A54C50B6347441985A77E51F8CA048` |
| `sources\TerraFX.Runtime\Rendering\RenderSnapshot.cs` | C | `733C8A58053977A349742540639FC55AF0CD299DB33C7394D1DC999A17D30AF0` |
| `sources\TerraFX.Runtime\Rendering\RenderSpriteSnapshot.cs` | C | `28DA4B8894D2F2A49EAFAEE91243C295ED7F01498CD9EB920535A6FD6D365B26` |
| `sources\TerraFX.Runtime\Rendering\RenderSpriteCameraOrder.cs` | C | `E2D32AB4FDDA54DCA76845EB4F55A52AD09BF0C518D3C7A3F92ACAA0E22837FA` |

**Verification record:** all 18 direct input hashes/states, the rectification's exact hash and 73 frozen input hashes/states, and baseline HEAD matched before and after authoring. Recheck these bytes before a later review/disposition; acceptance cannot transfer to changed inputs. Verification is file/hash/state inspection only, not a build, test run, or native qualification.
