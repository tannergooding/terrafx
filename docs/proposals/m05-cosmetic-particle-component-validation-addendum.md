# M05 cosmetic-particle component validation addendum

## Status and decision

**Tentative implementation guidance under the requested blocker policy.** This supplements the first component-binding batch in [the cosmetic-particle pool proposal](m05-cosmetic-particle-pool-owner-closure.md). It selects only the failure policy for the required positive Definition AssetId; it does not amend maintained contracts or authorize source/test changes in this documentation task.

**Select the existing exception channel for an invalid in-memory private component, and the existing typed Read-rejection channel for invalid serialized input.** No new emitter diagnostic code, public member, result type, schema version, or acceptance ID is necessary.

The earlier stop correctly avoided inventing a diagnostic, but the absence of an emitter-specific diagnostic does not establish that M03 requires one. Its frozen validation dispatcher already permits exceptions, revokes bindings on exceptional exit, and routes writable-scope failure through existing World fault handling. This addendum explicitly selects that existing behavior for this new private component.

## 1. Evidence and contract fit

Unqualified source paths below are under `sources\TerraFX.Runtime`.

| Input | Observed contract or behavior |
|---|---|
| Chapter 02 section 1; Chapter 03 section 4; Chapter 28 section 1 | Conventional exceptions cover argument misuse and internal invariant violations. Caller-visible failure is independent of telemetry. These chapters do not require a new named diagnostic for every private invariant. |
| Chapter 12, “M03 frozen component storage and typed codec bindings”; Chapter 30, `IComponentStateCodec<T,TAuthor>` | The captured family includes actual Read/Validate/Count/Write/default/Restore roots. Read has its own engine-issued Complete/Reject protocol; callback exceptions are not malformed-input rejections. Default construction cannot repair missing required serialized fields. |
| ``Entities\ComponentFactoryBinding`1.cs:354-394,493-550,657-684`` | Validate invokes the captured codec inside `try/finally`, checks returned diagnostic arrays, and always revokes its binding/context. It does not catch or convert a codec exception into success. Its current `ValidationContext.ValidateFields` checks the root type but otherwise returns `[]`; it does not enforce a positive asset identity. Read separately preserves issued rejection results and never calls Restore after a rejected Read. |
| `Entities\ComponentValidationBinding.cs:25-42` | `ValidateFields` checks binding lifetime/thread and the returned array. Calling it is not evidence that the current context validated Definition's numeric domain. |
| `Rendering\RenderMeshComponentDefinitions.cs:135-158,253-260,393-408` | Mesh uses the Mesh-specific `RenderMeshInvalid` diagnostic. Its default provider instead throws `InvalidOperationException` when the required Mesh asset was not explicitly selected. Neither establishes an emitter diagnostic assignment. |
| `Entities\World.cs:1082-1188,1562-1594` | Create validates the uncommitted component row before publishing an Entity/committing its identity; its cleanup clears an uncommitted row. Set invokes registered validation before changing the live value or mutation revision. An escaped codec exception already follows these operations' exceptional paths. |
| `Entities\World.Queries.cs:307-360,500-565`; `Entities\World.cs:2279-2314` | Writable-scope validation exceptions are caught outside the component. Idle query failure faults the World through existing `FaultWorld(null, exception)`, whose existing fallback is `WORLD_COMPONENT_VALIDATION_FAILED` / `World.ComponentValidation`. Callback/protocol failure precedence is already handled there. |
| `Entities\World.Ticks.cs:37-196`; Chapter 13 sections 2 and 4 | An uncured tick-stage validation failure withholds the publication and follows existing Step fault routing. Earlier writes are not promised rollback. The component must not replace that routing with a cosmetic-pool rejection. |
| ``Entities\ComponentReadBinding`1.cs:143-183`` | Reject with `ComponentReadFailureKind.InvalidValue` produces the existing `ComponentRead.InvalidValue` diagnostic and a rejected result with no author. The binding owns source bounds, field-path context and one-result issuance. |

The component's already selected data is unchanged: required Definition referencing `native/cosmetic-particle-emitter` 1.0, required Enabled Bool with authoring default true, persistent component 1.0. Positive means the existing signed-32-bit AssetId domain, `[1, int.MaxValue]`. Positivity is required even when Enabled is false. Existence and compatibility of the referenced asset in a retained package remain the later Assets/Scene binding responsibility; component validation performs no asset load.

## 2. Selected failure policy

| Entry or condition | Selected behavior |
|---|---|
| Private value constructor supplied a nonpositive Definition integer | Conventional `ArgumentOutOfRangeException` naming that constructor's Definition argument. This is an optional early guard, never the only guard: `default(T)` and writable storage bypass constructors. |
| Codec Validate observes Definition <= 0, including the language-default component or a value left by a writable scope | Throw `InvalidOperationException` identifying the invalid Definition/positive-AssetId requirement. This is a private invalid-state/provider-validation failure, **not** a normal returned diagnostic rejection. Do not construct a new Diagnostic or catch this exception inside the component. |
| Codec Validate observes an admissible value | Preserve ordinary cancellation/binding checks and return the normal initialized diagnostic array through `binding.ValidateFields(in value)`. Enforce positivity explicitly; do not infer it from that currently empty array. |
| Count/Write reaches an invalid Definition | Refuse through the same `InvalidOperationException` guard before encoding, narrowing or copying to the destination. Normal World capture still performs its existing validation; this does not add a different capture authority or repair path. Count/Write remain observational. |
| A structurally readable Definition reference contains unsigned AssetId 0 or greater than `int.MaxValue` | During the actual codec Read, issue `binding.Reject(ComponentReadFailureKind.InvalidValue, ...)` before conversion, successful author completion or Restore. Use the existing Definition field path and an in-range source offset. This uses the existing `ComponentRead.InvalidValue` code; it does not introduce a diagnostic ID. |
| Required Definition/Enabled is missing or has the wrong wire shape | Use the existing Read rejection protocol for invalid fields, or preserve the actual structural EBIN rejection. Do not fill missing encoded tags from defaults. Preserve exact root/reference tuple validation and full source consumption. |
| Whole-component Defaults requested without an explicit Definition | Throw `InvalidOperationException`, following the Mesh default-provider pattern. Enabled's authoring default cannot manufacture a valid Definition, and AssetId 1 is not an implicit substitute. |

An exception message should identify Definition and its required domain; no exact message text or new diagnostic context-key contract is selected. Binding/lifetime/thread/cancellation failures retain their existing precedence and channels. Do not catch broad exceptions in Read to disguise provider, scope or execution faults as bad input.

This is deliberately not a generic validation-framework change. For this component, an invalid in-memory Definition follows the existing provider-exception path. Invalid authored bytes follow the established typed Read-result path. Conflating those two paths is unnecessary.

### Effects at actual World boundaries

- **Idle Create:** invalid initialization publishes no Entity or identity assignment. Existing candidate-row/storage cleanup applies; do not claim that validation necessarily precedes all preparation allocations or leaves every backing capacity unchanged.
- **Idle Set:** validation throws before the replacement is stored or the canonical mutation revision advances. The existing method does not fault the World merely because this pre-write call threw; do not add such a side effect to the component.
- **Writable query:** a caller may already have modified storage. Existing scope-close handling faults the idle World rather than allowing later ticks/capture of the invalid state. No component-side rollback, clamp, default replacement or swallowed exception is permitted.
- **Inside Step:** existing invocation/callback cause selection and Step fault wrapping apply, with no new publication. Preserve original causes and existing cleanup precedence; do not add a component-specific wrapper or override an earlier sticky protocol failure.
- **State2 Read/Restore:** malformed numeric input returns the issued rejected result; Restore is not invoked. A genuine provider exception remains an execution failure. Do not use a normal rejection to claim a failed candidate was restored.

The existing World-owned fallback fault diagnostic is reused only when its existing caller actually faults the World. The component must not emit that code itself, force telemetry on an idle Set refusal, or promise that every invalid constructor/Set call creates `LastFault`.

## 3. Alternatives

| Alternative | Disposition |
|---|---|
| Add `RenderParticleEmitterInvalid` or another emitter diagnostic | Not selected: it would create the unassigned diagnostic identity that prompted this blocker. No such ID is needed for the selected exception path. |
| Reuse `RenderMeshInvalid` or another component's diagnostic helper | Not supported: it misidentifies the failing component/owning contract. |
| Construct `ComponentRead.InvalidValue` in Validate, or manufacture a Read binding to obtain it | Not supported: that diagnostic/result belongs to an actual Read invocation. Reuse its legitimate producer during Read only. |
| Rely on `ValidateFields`, constructor checks, or successful decoding alone | Insufficient: the current generic context does not check the range, and default values/writable storage bypass construction and decoding. |
| Throw on every malformed serialized value | Not selected: expected bad input has the existing typed Read rejection channel. A callback exception instead denotes protocol/execution failure under M03. |
| Use `ArgumentOutOfRangeException` for runtime Validate too | A conventional exception is available, but this addendum selects `InvalidOperationException` for an invalid stored/private component invariant. This avoids creating a new compound-value parameter-name promise and distinguishes it from the optional constructor argument guard. |
| Extend generic field validation, invent a generic result, or translate the exception at every World call site | Unnecessary and broader than the component. Existing dispatch/revocation, pre-write checks and writable-scope fault handling already support the selected channel. |

## 4. Small implementation/test batch unlocked

Implement only:

1. **`sources\TerraFX.Runtime\Rendering\RenderParticleEmitterComponentDefinitions.cs`:** the production-private typed component/immutable author, exact two-field storage inventory and bindings, internal registration helper, default provider, persistent Restore and complete State2 codec. Follow the Mesh component's existing registration/layout/factory recipe and captured-root pattern. Use the existing particle metadata; do not change schema tags/versions, select pool ownership or unconditionally enlist the helper in existing CPU support.
2. **Focused tests under `tests\TerraFX.Runtime.Tests\Rendering`, normally `RenderParticleEmitterComponentTests.cs`:** use that real helper with `WorldRegistrationBuilder` and actual World-owned component storage, with no Scene or CPU-extraction support selected.

Required focused observations:

- Valid Definition values 1 and `int.MaxValue`, with Enabled true/false, register and survive Create/Set/readback; Count/Write encode the exact selected fields rather than raw managed bytes.
- Whole-component Defaults fails without a Definition. A default component supplied to Create fails without publishing the requested Entity; a default replacement supplied to Set leaves the previous value and mutation marker unchanged.
- A real writable query writes a default component and returns normally. Scope-close validation throws the selected exception and leaves the World faulted with its **existing** fallback code; later tick/capture admission refuses. This distinguishes a constructor-only guard from complete storage validation.
- Through the real legal State2 capture/candidate paths, invalid serialized Definition IDs 0 and values above `int.MaxValue` produce the existing typed Read rejection, not overflow, Restore invocation or a success-null author. Missing required fields are not repaired. Use independently authored literal input/expected fields.
- Binding revocation and exception propagation retain the actual M03 lifetime rules. Do not instantiate test callback bindings/contexts to label an otherwise unavailable persistence observation as legal execution. Compiled provider closure, actual World storage tests and B-owned persistence execution remain distinct evidence.

Prerequisites for this batch are the existing compiled M05 particle metadata, accepted M03 typed factory/storage contracts, the earlier tentative emitter schema selection, and this narrow failure-policy selection. It needs no new public declaration, generic validator, maintained design edit, Scene preparation, pool/phase dispatch or native device.

Use the owning Runtime test project and the new fixture's nonempty selection, Debug before Release, retaining fresh results under ignored `artifacts`. If a particular State2 observation lacks its real B-owned operation/producer, identify that exact unexecuted observation; do not fabricate an issuer or call it passed. This does not defer the actual compiled Read/Count/Write/Restore roots.

The controller affinity/refusal policies, pool/operation budgets, Scene preparation `QualifiedFootprint`/J/K/supplier/receiver gates, cohort/restore joins and all rendering/native/AOT/milestone qualification remain unchanged. No source/test files were edited and no tests or Scene preparation were run for this addendum.

## Evidence boundary

The decision is based on the following working-tree inputs, including dirty bytes. These hashes bind the inspected evidence, not an accepted API baseline or executable qualification result. Reconcile changed inputs before applying the selection.

| Input | SHA-256 |
|---|---|
| Parent cosmetic-particle pool proposal | `888D97BA6BC9AC2F06295D725162074F85C275658CDB3D0DDF31B8DA89904A95` |
| `m05-owner-contract-closure-rectification.md` | `67134F02B92D316524848ECB8E0C917EF868DA3D2EDA5FC581DBAA25B1825632` |
| Chapter 02 | `40F913A50434209E09941C22625A6D0722202BAB97EBAC0E153C1EA3368E2291` |
| Chapter 03 | `B4208D3037C8C90EED56627F63DC04ACFFA80FF16B5FF3FF6936CD89615C08C5` |
| Chapter 12 | `174B2465746DA79CEF560C8506216EF4F99F9D1AABAC184F872C9E6CB0B94E0B` |
| Chapter 13 | `0D7806B03A1D019215EB5C3912F10200A078F9B00A71A206FB979E93BBD18CDE` |
| Chapter 28 | `C9260CD18A905E087DFC47D07AF67BD2477996232DA773B69A0E4C3B7A64D90F` |
| Chapter 30 | `15848C82F3858A77A87A0D3EA6FBCC07046EB6E77CA5B21748C72B90EFAB598B` |
| Chapter 35 | `24EEC6E92B7AD904D552F290EF748FFC8FCE13B6B94A89EBD9F39487D4584D87` |
| `sources\TerraFX.Graphics\RenderingSchemaCatalogue.cs` | `DE25A8C0E249FF9AB5B592FE848C85429C004FE629694C64EF1AFEF3BA731614` |
| ``Entities\ComponentFactoryBinding`1.cs`` | `96191847427574AB6E33A24203569B0958D9BEA8345411125CFC625FDB4D6284` |
| ``Entities\ComponentReadBinding`1.cs`` | `80134BC6808263ABB07309B1559C3D682775CCD517BC40B18D6274439E12D037` |
| `Entities\ComponentValidationBinding.cs` | `6B2698B82494D61DF87AE5F0694EE8873D13C597CC28867E7A17D1F0193B5B94` |
| `Entities\World.cs` | `692F721BEAD29E5EC258BF7B1844EC785E2D1548E9F53DFA9BDB57A22A01F09F` |
| `Entities\World.Queries.cs` | `519239A7D692851B573ACEF2C61C2C453D503A3D5F991AC643DD9B7B8CF2D13F` |
| `Entities\World.Ticks.cs` | `5DAAC62FA95D6E2FC665576C5DCFC6D1CDFCFAE0F345E60A5F06098FDBCF7F05` |
| `Rendering\RenderMeshComponentDefinitions.cs` | `167D1ADF373643E47C6F489A61D35F51FE6F4829349C82FDBC2131B0E772A20F` |
