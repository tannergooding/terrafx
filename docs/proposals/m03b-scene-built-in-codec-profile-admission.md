# M03-B closed built-in Scene codec admission

**Status: tentative owner-contract decision for review. Select option B below; no numerical profile is enabled. Keep `PreparedScene.QualifiedFootprint` null.**

This resolves the allocation-authority choice left by [37a83455](m03b-assets-prelease-operation-profile-capacity-addendum.md). The [pre-lease owner](m03b-assets-prelease-recovery-owner.md) remains the recovery mechanism; issued-lease release is unchanged.

## Decision and first slice

| Choice | Disposition |
|---|---|
| A: require every Scene-selected codec to implement a workspace-profile contract | Not selected. This adds a compatibility/ABI obligation to application codecs and still needs either review of each implementation or allocation-grant enforcement. A self-declared byte count does not establish a bound. |
| B: qualify a closed built-in implementation set | Selected. Preserve public Assets codec interfaces and admit the Scene-owned route only through factory-bound identities with reviewed profiles. This removes arbitrary codec bodies from the first allocation closure. |

The first candidate is **ordinary Scene/Prefab preparation, package codec None, with no `ISceneExpansionResourcePreparer` participant**. Its typed reads are exactly `SceneAssetCodec.Read` and `ScenePrefabAssetCodec.Read`, schema versions `native/scene` 2.0 and `native/prefab` 2.0. Their real transitive readers, constructors, diagnostics and unwind belong to those profiles.

Common membership includes the existing typed-load/anchored-admission path, None payload reading, envelope decode/reference scanning, sequential `ValidateAssetReferencesAsync`/envelope inspection, and handle/lifetime construction and release. Reference inspection is part of the owning load, not another owner slot; it executes no target codec `Read`. Each dependency must pass its own None/inspection checks before reading it.

Rendering participation, including a selected preparer that would return null, is refused before the first root load in this slice. Mesh/Material/Atlas/Texture/Skeleton profiles, Brotli, animation playback, package-pin/Persistence and restore consumers remain unselected. Unrelated unused codec registrations do not make a Store unsupported.

## Factory identity and exact enforcement

These are proposed internal boundary members, not existing APIs:

1. `SceneSupport.RegisterAssetCodecs` uses an internal Assets `RegisterProfiledCodec` path when it constructs each exact private built-in codec. Assets seals an immutable registration receipt binding **codec object, CLR type, schema tuple and versioned opaque origin** into the actual adapter. Ordinary `RegisterCodec` creates no qualified receipt. Runtime owns the origin/profile catalogue; Assets stores opaque identities without referencing Runtime types or calling a Runtime sizing/funding callback.
2. Assets `DescribeOwnedRead<T>` returns a non-owning value descriptor containing the exact Store/source identity, backing token, registration receipt, entry codec/lengths and applicable immutable limits/schema identities. It acquires no backing/lease/decode credit and invokes no codec. Descriptor, registration-receipt and cold-metadata provision must be covered by their real construction owner/original preparation bootstrap, never treated as free.
3. Runtime performs an exact-origin/profile lookup and computes the complete up-front requirement from descriptor inputs. A receipt proves origin, **not sizing approval**. Unknown implementation, missing profile term or changed profile inputs refuse; no schema/type-name/reflection match, guessed multiplier or fallback codec qualifies.
4. The only Scene grant issuer is a proposed `PreparedScene.TryInstallAssetRead` construction actor. Under the existing preparation gate it requires Building/Running state and the empty operation slot, debits the exact disjoint claim through `TryClaimProducerWorkspace`, and elects construction. It constructs the empty Assets owner outside gates and installs that exact owner before authorizing start. The original use retains the claim and construction/cleanup obligation throughout. A number or receipt alone conveys no grant.
5. On start, Assets revalidates Store/source admission, exact backing token, adapter receipt, entry and profile inputs under its own gates, then records acquired credits/references in the installed owner before asynchronous escape. Replacement, disposal or identity mismatch refuses before acquisition; it does not silently rebind or retry. Existing usage/argument/pre-cancellation precedence remains required.

Profile calculations run on Runtime's side of this seam. Assets never receives `ScenePreparationFunding`, a World capability, or an upward callback. Registration/profile metadata must have explicit byte attribution; this decision establishes no free process-global registry.

## Operation count and byte ownership

**Choose one operation-owner slot per `PreparedScene`, with no queue.** This is an explicit admission rule for the qualified slice, not an inference from today's awaits or `MaximumLiveAssetLeases`, and not a new Store-wide quota.

The slot progresses `Empty → Constructing → Installed/Executing → CleanupAndReturn → Empty`, or `FailedRetained`. Nested reference validation uses the same owner. No next operation or reentrant acquisition is admitted until the prior owner has actually ended/transferred every obligation and joined its real return. Failure keeps the slot, root and original charge without retry. Independent preparations still need their own existing World/domain admission.

This bounds **operation owners**, not internal Tasks, registrations or native invokers. Their complete coexistence must still fit the profile; sequential awaits do not prove their tails joined.

Each profile must supply both:

- the complete operation peak, including construction, workspace, helper/invoker controls and protected cleanup/fault/publication;
- the retained-result charge and transfer attribution.

Independently proved Assets provision stays with Assets and is not double-charged to P. Any retained result/control/storage actually funded by P remains a disjoint original-X partition with the real plan/helper after exact installation acknowledgment; it is not refunded when the operation slot empties. Later settlement uses the existing exact plan/bundle receiver transfer. Only temporary/unused provision returns after actual last access. Retained aliases share an already funded value. An origin receipt or wire counter alone does not establish independent managed-storage provision: the profile must identify its complete bound, issuer and lifetime before taking that exemption. Backing/shared-storage attribution must likewise be explicit.

The slot fields belong in the whole preparation-carrier catalogue. Close eligibility and result installation use the original owner's existing fence; no second preparation root or estimated receiver carve-out is introduced.

## Numerical profiles: still NO-GO

No byte constant is invented. Using the existing `A(n,s) = WorldAdmissionDomain.ArrayCharge(n,s)` and `s_p = Unsafe.SizeOf<SceneHierarchyPlacement>()`, the hierarchy workspace exposes this partial term:

`H(e,p) = O_workspace + A(e,sizeof(Guid)) + A(p,s_p) + 2*A(e,sizeof(byte)) + A(p,sizeof(byte)) + 2*A(e,sizeof(int)) + A(e,sizeof(ushort))`.

That follows its eight actual arrays, but `O_workspace`, whole-type charges and the complete read coexistence are not qualified here. Its current `GetRequiredByteReservation` uses a different array-rounding rule; copying that returned number does not establish the owning Chapter 04 profile.

| Candidate | Missing fact before it can become Ready |
|---|---|
| Scene/Prefab None | A reviewed pre-read bound from entry length, limits and admitted schema facts to the peak of `SceneAssetReader`, hierarchy workspace, entity/component builders, selectors/overrides/references and their copies. Counts discovered during decoding cannot fund allocations already made. `SceneAuthoredRecord`, `SceneAuthoredComponentRecord` and the final asset each copy encoded bytes; include their actual coexistence and retained-result ownership. |
| Common envelope/None/inspection | Complete scanner/validation/helper and operation-carrier catalogue, reference-validation overlap with the decoded result, cold state, and preserved-fault/cleanup/publication costs. The operation controls do not yet have final implemented layouts. |
| Return/release | Actual enclosing Task/context/native return and the FileStream/SafeFileHandle release contract, not outcome readiness or a second Dispose. |

Thus every complete profile remains **NO-GO**. A formula may conservatively refuse a large valid input when it cannot fit the requested P, but must not silently lower P or derive its inputs through unfunded decoding.

## Bounded implementation batch and compatibility

Implement registration provenance, metadata-only descriptor/preflight checks, the single-slot issuer/installation protocol and retained-result partition bookkeeping behind the existing guard. These now have one selected owner policy; numerical profiles stay unavailable until their listed terms close. Do not add a fake Ready profile or expose placeholder public APIs.

The qualified Scene route intentionally refuses unreceipted/custom implementations even with matching schema/type; public Assets loading remains unchanged. This is a new future Scene admission restriction, not a claim that arbitrary codecs were already forbidden. Use existing contextual unsupported/capacity refusal channels; no new public exception family or implicit fallback.

Initial behavior checks can use real registrations/packages to distinguish factory receipts from an otherwise identical unreceipted built-in registration, missing-profile refusal and stale descriptors. Actual capacity/slot-return checks require a genuinely admitted profile; do not forge return acknowledgments or physical failure. Preserve the existing Rendering/pixel-17 consumer as a later composite gate rather than weakening it to fit this slice.

The decision permits this implementation batch, **not execution of ordinary Scene preparation**. FileStream/native retirement, caller-context/AsyncLocal behavior, stock async suffixes and finite native fault capture remain independent gates. Do not suppress context flow, replace exception identity, permanently hold normal operations, or add an observer chain to declare those gates closed. Managed, native/AOT and public-consumer acceptance remain separate.

## Frozen decision inputs

UTC `2026-11-18T16:09:28.7300162Z`; HEAD `37a834550be6c2a6fd65b550fd8b5163fa1dcf97`.

The prior addendum's 62 inputs were rehashed unchanged, including its four dirty inputs. Additional inputs:

```text
docs\proposals\m03b-assets-prelease-operation-profile-capacity-addendum.md
sources\TerraFX.Assets\AssetPackageBackingToken.cs
sources\TerraFX.Core\EbinStructuralReader.cs
sources\TerraFX.Runtime\Animation\AnimationSupport.cs
sources\TerraFX.Runtime\Rendering\RenderMeshAssetBinding.cs
sources\TerraFX.Runtime\Scenes\SceneAssetReader.cs
sources\TerraFX.Runtime\Scenes\SceneHierarchyValidationWorkspace.cs
sources\TerraFX.Runtime\Scenes\SceneAuthoredRecord.cs
sources\TerraFX.Runtime\Scenes\SceneAuthoredComponentRecord.cs
sources\TerraFX.Runtime\Scenes\SceneEntityReader.cs
```

Using the parent's raw-file/ordinal-path/LF hash recipe:

| Set | Files | SHA-256 |
|---|---:|---|
| Prior | 62 | `5f61960e52145954552567e0b3b312b891f248ad76e0d67507c4ce6b569a3340` |
| Additional | 10 | `2aa49e19a9dc2c4802740805020993d13d31f152dac427250680290beb2e657c` |
| Union | 72 | `51808257835c74846a458ca2c6952f5e94030b9bf54a721990060355ab0092be` |

The prior addendum's raw SHA-256 is `fb07baef70412a2ce33f56be8efdb3909046ad64a4fd5227e4731fa188e5a868`. Changed inputs require reassessment before disposition. This is a bounded decision freeze, not a complete allocation/runtime attestation. Only this proposal was authored; no source/tests/design chapters, schema/fixture version, execution evidence, commit or remote state was changed.
