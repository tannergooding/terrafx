# M03-B Assets operation-profile capacity decision

**Status: tentative blocker disposition for review. No numerical `C_op` or complete admitted-operation count is established. Keep `PreparedScene.QualifiedFootprint` null.**

This supplements [pre-lease recovery ownership](m03b-assets-prelease-recovery-owner.md), committed as `8cadc4d3`. Its original-Scene-root ownership candidate remains unchanged. The current ceilings cannot supply its missing operation profile or code-ready issuer boundary without an allocation-authority decision.

## What the existing limits establish

Let `d` be an admitted entry's `DecodedLength`, and `A(n,s)` the existing `WorldAdmissionDomain.ArrayCharge` accounting rule: `64 + align16(n*s)`, with checked arithmetic.

| Input | Derivable bound | Why it does not close `C_op` |
|---|---|---|
| Entry/decoded-asset limits | `AssetPayloadReader.ReadAsync` creates one `byte[d]`, giving the partial accounting term `A(d,1)`. Brotli additionally creates `byte[1]`. | These are byte-array terms, not semantic values, validation/scanner builders, decoder workspace, invocation controls or cleanup/publication peaks. They are not measured runtime allocation evidence. |
| Brotli input request | The current rent requests 65,536 bytes. | A rent request is not a reviewed upper bound on the returned backing or the native decoder's complete workspace. No such bound is supplied by the Store options. |
| Concurrent decode bytes | Bounds the sum of reserved declared decode lengths. | `LoadAssetAsync` returns its initial decode reservation before reference validation and lease construction. It is not a whole-operation reservation. |
| CPU asset bytes / live leases | Charge each decoded value's declared wire length once and cap counted handles/admitted typed loads. | Neither charges codec-owned managed allocations. Temporary references precede typed-load admission; package inspection does not reserve a live-lease credit. |
| Scene/entity/occurrence/depth and directory limits | Bound their semantic walks and distinct package identities. | They do not establish bytes per codec invocation or join controls whose producer/context suffix remains live after an outcome. |
| Requested W / original P | `MaximumPreparationBytes` selects the exact funded whole-preparation purse; it is not extra World capacity. | A purse bounds permitted claims, not allocations in a lower method that never receives or checks that provision. Reserving all remaining P is not proof that the operation fits it. |

Consequently, neither `C_op = A(d,1) + constant` nor `N_op = MaximumLiveAssetLeases` is justified. No fraction of P, guessed codec multiplier, or duplicate charge of the wire-byte budget repairs the missing terms.

## Where the attempted closure stops

The actual callers include the planner's root Scene/Prefab loads and the bundle's Atlas, Mesh/Skeleton, Material and texture dependency loads. They converge through:

`AssetStore.AcquireLoad<T>` → `LoadAssetAsync<T>` → `AssetPayloadReader.ReadAsync` / `AssetEnvelopeDecoder.Decode<T>` → `AssetStoreRegistration.IAssetCodec<T>.Read`.

`AssetStoreRegistrationBuilder` accepts supplied `IBinarySchemaCodec<T>` or `IContextualBinarySchemaCodec<T>` objects. Its adapters forward `Read`; admission checks the CLR type, native-asset role and exact schema tuple, not a byte-bounded implementation profile. `SceneSupport.RegisterAssetCodecs` offers the built-in Scene/Prefab codecs, but `SceneLoader` does not require those concrete implementations.

Thus the same accepted type/schema tuple does not identify one allocation closure. Neither codec interface nor `AssetCodecContext` supplies workspace ownership, a debit operation, or an allocation-bound declaration. Chapter 30 explicitly excludes codec-owned managed allocations from the two Assets byte budgets. This is a contract/source limitation, not an experiment with a fabricated codec.

Even a built-in-only candidate needs its transitive reader/workspace and runtime/native envelope catalogue; the original 50 files were a recovery-root review set, not that complete closure. The proposed operation-control types also have no implemented final layout. No complete numerical carrier, coexistence, fault-capture or return profile follows from their proposed names.

The live-lease counter cannot bootstrap a pre-work owner: an anchored call already holds its temporary reference when admission can reject, and inspection has no corresponding lease slot. Finite Scene demand is not disputed; it does not establish a complete, funded count of these owners through their actual final return.

## Decision required before implementation admission

Chapters 11/16/30's owners must select the qualified Scene operation's **allocation authority**:

- Either bind admission to an explicitly closed compiled codec/read/decompression/runtime profile with complete peak-storage functions, or introduce a bounded workspace-grant contract that every admitted allocating participant respects.
- Define the original-P subclaim's opaque Assets-side issuer/acceptance protocol: construction provision, atomic debit before work, result/debt transfer, and one return after actual last access. A numeric W field alone supplies neither authority nor enforcement. Assets must not acquire a Runtime callback/dependency.

This need not introduce another World quota. It does require a reviewed compatibility/refusal decision for registrations currently accepted without such a contract. No independent Store pool, default slot count, or capacity amount is selected here.

Only after that decision and the complete fixed/workspace/cleanup/return terms close can disjoint original-P claims bound simultaneous owners. Until then, ordinary Scene admission remains unavailable before lower acquisition/decode/construct; do not lower W, construct an unfunded control, or permanently quarantine normal successful calls as a substitute.

FileStream/SafeFileHandle ownership-preserving close, native/Task/context return, and finite fault-capture gates remain separately unproved. Reconsider when the admitted allocation contract and exact profile inputs exist, not by repeating unchanged blocked probes. No source, tests, synthetic producers, runtime/native observations, or milestone acceptance are supplied by this addendum.

## Current input freeze

UTC `2026-11-18T15:51:55.8758773Z`; HEAD `8cadc4d3bba4dafdda5b6e1479ecb471b12c62f0`.

All 50 paths in the parent proposal's frozen set were rehashed and are byte-identical, including its four dirty inputs. The following 12 additional inputs close the specific dispatch/issuer assessment above:

```text
docs\proposals\m03b-assets-prelease-recovery-owner.md
sources\TerraFX.Assets\AssetCodecContext.cs
sources\TerraFX.Assets\AssetEnvelopeDecoder.cs
sources\TerraFX.Assets\AssetStoreRegistration.cs
sources\TerraFX.Assets\AssetStoreRegistrationBuilder.cs
sources\TerraFX.Assets\IBinarySchemaCodec.cs
sources\TerraFX.Assets\IContextualBinarySchemaCodec.cs
sources\TerraFX.Runtime\Entities\WorldAdmissionDomain.cs
sources\TerraFX.Runtime\Scenes\SceneAssetCodec.cs
sources\TerraFX.Runtime\Scenes\SceneLoader.cs
sources\TerraFX.Runtime\Scenes\ScenePrefabAssetCodec.cs
sources\TerraFX.Runtime\Scenes\SceneSupport.cs
```

Using the parent's raw-file/ordinal-path/LF hash recipe:

| Set | Files | SHA-256 |
|---|---:|---|
| Original inputs | 50 | `ec28c815b4e478ed0ba714a2ca46883bdffaf54dcf9f04b3482d3f77f215748e` |
| Additional inputs | 12 | `78a20b7c546f0122ae8840472e91ba27ce417f7462f875cca09982536faf1b2d` |
| Union | 62 | `5f61960e52145954552567e0b3b312b891f248ad76e0d67507c4ce6b569a3340` |

The parent proposal's raw SHA-256 is `f947e6e040cf6dfa5a5a04de1c84c257536a9a37b3794c0cefcd5555cd55c18c`. Recheck identities and dirty state before disposition; changed inputs require reassessment. No schema/fixture version, transitive runtime freeze or native qualification is asserted.
