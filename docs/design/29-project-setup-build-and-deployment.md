# Project setup, dependencies, build, and deployment

[Terminology and reading conventions](01-terminology-and-reading-guide.md).

This chapter owns repository layering, qualified tool profiles, build/test commands and deployment requirements. [00](00-overview-and-scope.md) defines product scope; the target repository informs infrastructure, not engine architecture.

## 1. Repository and assembly DAG

Use `TerraFX.slnx`, `Directory.Build.props`/targets, `Directory.Packages.props`, `NuGet.config`, `global.json`, sources/tests/tools/samples/artifacts, and docs with these specs. All thirteen production projects, including content tools, editor and generators, live under sources; tools is reserved for repository-local utilities. Runtime libraries target `net11.0`/C# 15 with `IsAotCompatible = true`, with nullable enabled, implicit usings disabled, warnings as errors, .NET analyzers/API documentation enabled, and deterministic/source-link builds. The shared `TargetFramework` is `net11.0`; compiler-hosted generators override it with `netstandard2.0`. `LangVersion` is `latest` so SDK updates do not require a separate language-version change; preview features remain opt-in. Keep shared defaults in root props/targets and folder-specific settings or exceptions in sources/tests/tools/samples. `AllowUnsafeBlocks` is enabled centrally, but unsafe implementations still require reviewed native/allocator/storage boundaries and lifetime proofs. Individual caller-obligation members are marked unsafe and other accesses use method-body unsafe blocks as specified in [Chapter 02](02-dotnet-api-memory-and-ownership.md), not unsafe classes/files by default.

Namespaces are logical subsystem boundaries, not an instruction to create one project/package per namespace. The physical assembly DAG is the following thirteen product targets: ten runtime/backend libraries, separate content tools/editor, and the compiler-hosted generator. `TerraFX.Runtime` contains the coupled world-to-host path; the remaining library boundaries have an independent-use or native-dependency purpose.

The `TerraFX.Core` assembly/package name is not a public namespace: shared foundation values/callables use `TerraFX`, with semantic child namespaces `TerraFX.Numerics`, `TerraFX.Buffers`, `TerraFX.Pooling` and `TerraFX.Diagnostics`. Runtime likewise keeps semantic subsystem namespaces rather than introducing `TerraFX.Runtime` as their public prefix. Angle and bounded object pools remain in Core; they do not add product assemblies/packages or native dependencies.

| Assembly | Public namespaces | Allowed dependencies / responsibility | Reason for physical boundary |
|---|---|---|---|
| `TerraFX.Core` | `TerraFX`, `TerraFX.Numerics`, `TerraFX.Buffers`, `TerraFX.Pooling`, `TerraFX.Diagnostics` | BCL only; geometry/Angle/interchange, shared callables, bounded object pools, IDs/profiles/options/clocks/diagnostics/registration, safe-facing buffers | Small reusable foundation; no engine or native backend dependency |
| `TerraFX.Assets` | `TerraFX.Assets` | `Core`; store/package/base codecs, all 20 common provenance/settings models and rules 17/18/19/49, runtime join verifier and retention/resource-scope/ticket protocol; generic importer interfaces, not concrete scene/render imports | CPU asset ownership/loading shared by audio and offline tools without a world/host |
| `TerraFX.Input` | `TerraFX.Input` | `Core`; normalized events/actions/text | Portable input independent of native adapters, ECS and hosting |
| `TerraFX.Platform` | `TerraFX.Platform` | `Core`, `Input`; native windows/pump/input adapters | OS boundary depends on portable input, never on Runtime |
| `TerraFX.Graphics` | `TerraFX.Graphics` | `Core`; portable RHI/resources/graph APIs | Independent graphics consumers and backend contract without ECS/scene/host |
| `TerraFX.Graphics.D3D12` | `TerraFX.Graphics.D3D12` | `Core`, `Graphics`; private Windows/D3D12 bindings | Explicitly selected native backend and Windows-only dependency |
| `TerraFX.Graphics.Vulkan` | `TerraFX.Graphics.Vulkan` | `Core`, `Graphics`; private Vulkan bindings | Explicitly selected native backend and its loader/bindings |
| `TerraFX.Physics` | `TerraFX.Physics` | `Core`; standalone dimensional solver/query/shape state | Physics consumers need no ECS, scene, graphics, assets or host |
| `TerraFX.Audio` | `TerraFX.Audio` | `Core`, `Assets`; mixer/DSP/endpoints | Independent audio service and native endpoint boundary, no ECS/scene dependency |
| `TerraFX.Runtime` | `TerraFX.Entities`, `TerraFX.Scenes`, `TerraFX.Animation`, `TerraFX.Rendering`, `TerraFX.UI`, `TerraFX.Integration`, `TerraFX.Hosting` | `Core`, `Assets`, `Input`, `Platform`, `Graphics`, `Physics`, `Audio`; integrated world/scene/animation/render/UI/host lifecycle | One coherent runtime library instead of seven coupled subsystem assemblies |
| `TerraFX.Content.Tools` | `TerraFX.Content.Tools` | `Core`, `Assets`, `Input`, `Runtime`, `Graphics`, `Physics`, `Audio`; offline decoders/DXC/cookers and registered schema validators | Offline authoring dependencies never become runtime references |
| `TerraFX.Editor` | `TerraFX.Editor` | `Runtime`, `Content.Tools`; trusted editor operations | Editor application and content tools stay out of player libraries |
| `TerraFX.Generators` | Generated consumer namespaces | Compiler only; Roslyn-compatible `netstandard2.0` analyzer/generator | Compiler-hosted tooling, not a runtime dependency |

The table lists allowed dependencies. Keep direct `ProjectReference` items minimal; SDK-style project references supply transitive dependencies.

The Assets common provenance verifier reconstructs the bounded inert native graph and validates graph/context/key/package/publication/source/version joins without Content.Tools or original source files. Tools uses the same Core-only models/kernel, independently checks original bytes and actual producer execution, and participates through public Assets admission/resource/verifier/transfer tickets; it cannot mutate a private Assets ledger. Runtime participant codecs are explicit higher-owner registrations, not reverse assembly references. Scope/path/source pins and operation-bound verification results are lower-owned authority, never opaque payload claims or callback-under-gate validation. EN024's public headless NativeAOT player must verify publication without Tools; AR01/AR02/EN152 check the preserved DAG and ordinary reference-assembly access.

### Logical layering inside Runtime

The merged assembly preserves the existing subsystem dependency direction. Internal accessibility is not permission to cross a logical boundary, fabricate another owner's handles or weaken lifecycle validation. CI's repository-only semantic dependency analyzer checks handwritten and generated type/member references, including nonpublic access and issuer paths; a project-DAG check alone cannot enforce these rules. Introduce this check with the first Runtime implementation through the ordinary build/test workflow. It belongs under tools, not in the consumer-facing `TerraFX.Generators` package.

| Runtime namespace | Allowed subsystem dependencies |
|---|---|
| `TerraFX.Entities` | `Core` |
| `TerraFX.Scenes` | `Core`, `Entities`, `Assets` |
| `TerraFX.Animation` | `Core`, `Entities`, `Scenes`, `Assets` |
| `TerraFX.Rendering` | `Core`, `Entities`, `Scenes`, `Assets`, `Graphics`, `Animation` |
| `TerraFX.UI` | `Core`, `Input`, `Assets`, `Graphics` |
| `TerraFX.Integration` | `Core`, `Entities`, `Assets`, `Scenes`, `Input`, `Platform`, `Graphics`, `Animation`, `Physics`, `Audio`, `Rendering`, `UI` |
| `TerraFX.Hosting` | `Core`, `Entities`, `Assets`, `Scenes`, `Input`, `Platform`, `Graphics`, `Animation`, `Physics`, `Audio`, `Rendering`, `UI`, `Integration` |

No lower logical layer references `TerraFX.Hosting`, and no runtime layer references `TerraFX.Editor`, `TerraFX.Content.Tools` or compiler tooling. Generic asset-store/input/audio/physics/graphics implementations retain their independently usable contracts. Concrete scene/render source import stays in Content.Tools; the namespace graph still forbids an `Assets <-> Scenes` cycle. `Integration` is an optional adapter namespace in Runtime, not a second solver or a separate package.

#### Entities-owned World identity admission

`TerraFX.Entities` alone owns the `WorldHistoryRegistrar`,
`ValidatedWorldIdentityHistory`, registration permits, history interests,
new-lineage reservations, domain-operation interests, retirement owner/anchor,
default-domain owner/handoff and `WorldConstructionPlan`. These are engine-private
capabilities bound to exact domain/root/operation generations, never public
GUID-based permissions. The validator creates `ValidatedWorldIdentityHistory`
only after complete descriptor, identity, ancestry, assignment and structural
validation. `WorldAdmissionDomain` owns one registrar; an external owner may close
its own domain, while an engine-default domain has one current World close owner.
Hosting supplies its admitted worker-pool lease and exact domain but cannot
resolve/register lineages or issue World owners. Persistence consumes and retains
the exact lower-owned plan/leases; it cannot manufacture a lineage list or
retarget a candidate by readable GUID metadata.

The registrar's only state-changing protocol is the closed set:
`PrepareRetainedRegistration`, `PrepareStartNewRegistration`,
`CommitRegistration`, `RetainHistory`, `TransferHistory`, `PublishStartNew`,
`EndConsumerAfterActualUse`, `EndOwnerAfterActualHistoryRelease`,
`QuarantineUnreleasedHistory` and `AbandonPreparedRegistration`. Preparation
charges the complete prospective root/key/edge/index/vector/interest/control and
growth peak before allocation; gated commit revalidates exact generations and
installs all membership changes or none. Transfer moves an existing credit
without minting one or changing domains. End operations are nonblocking and legal
after closure; actual consumers, storage and failed-cleanup quarantine retain
their existing charges until release. Metadata copies, wrapper reachability,
cancellation, and GC do not end or create operational interests.

For an engine-default domain, the prepared single-use handoff binds the exact old
owner generation and candidate. The old World keeps close authority until
successful publication; one allocation/callback-free swap transfers its existing
owner credit and revokes the old authority. Pre-swap rejection leaves ownership
unchanged, and old-World retirement never closes or waits for the successor's
domain. Caller/host domains stay externally owned. This does not add a lock,
queue, global service locator or persistent lineage tombstone; [12](12-entities-components-and-queries.md)
owns the detailed admission, count and release invariants.

#### Core owner-ID issuer

`TerraFX.Core` retains the sole private process counter and its internal
`TerraFX.OwnerIdAllocator.Allocate(): uint` issuer. Only `TerraFX.Core.csproj` grants
`TerraFX.Runtime` a strong-name-qualified `InternalsVisibleTo` entry using
`$(AssemblyOriginatorPublicKey)`; do not put the grant in shared source properties.
This is deliberately assembly-wide friendship: it gives Runtime access to Core's
assembly-accessible internals and is not a per-type/member/caller restriction, a
security boundary, a reverse project dependency, or a transitive grant. Runtime.Tests
does not gain Core friendship through Runtime. Grant no other product friend until an
actual owner is implemented; then review the implementing assembly and exact issuer
path separately. Keep the private counter, BCL-only Core runtime closure, ordinary
reference-assembly/package flow, and selected single-Core identity requirement.

Semantic issuance is narrower than CLR access. `World..ctor(WorldConstructionPlan)` is
the only Runtime caller of `Allocate`; it consumes the exact Entities-owned admitted
plan, validates its final target/domain/root/reservation/handoff grants before issuing
once, then initializes live storage. A later initialization failure burns the ID.
`World.Create(WorldOptions)`, `World.CreateHosted(...)` and
`World.CreateRestoreCandidate(...)` prepare fresh or restored plans through the same
private constructor; the World factory is the only fresh-lineage selector, and a
restore candidate consumes only source/transformation identity already admitted by
the plan. The WorldAdmissionDomain's registrar, reservations and interests do not
issue owners or accept identity from GUID-only metadata. A default-domain replacement
transfers its existing close-owner credit exactly once at successful publication;
ordinary interests do not grant close authority, and retiring the former World
releases only its own interests. The only
Entity-constructor caller is `World.IssueEntity(uint,uint)`, which uses its receiver's
owner and admitted slot/generation metadata. `RestoreEntities()` issues on the
replacement candidate. `CommitStructuralTransactions()` is World-owned preflight and
commit, not an authority delegated to producers: `ExecuteTick()` and
`PumpControlBoundary()` invoke it for applicable structural work; the control boundary
also processes queued scene intent and candidate replacement. CommandWriter/scene
producers return tokens or queued intent, never live Entity values. `World.Step()`
uses the common `ExecuteTick()` only after standalone admission. The host owner loop
uses `PumpControlBoundary()` and `ExecuteTick()` after host admission; it never calls
public `Step()` on a host-managed World.

The reviewed policy semantics distinguish exact constructor Construction permission,
exact sensitive-call permission, and callable acquisition. Only `World.IssueEntity`
may construct Entity; only declared exact callers may invoke the allocator/issuer and
World lifecycle helpers; no callable-acquisition permission follows from direct-call
permission. These are semantic actor requirements, not a claim that the still-blocked
serialized policy grammar or analyzer enforcement is implemented. Exact keys include
declaring assembly, metadata type, method kind/name, staticness, generic arity,
parameter/ref/scoped forms, and return/ref form. Unlisted/generated helpers receive no
exemption. Owner/slot provenance, transaction preflight, exactly-once activation and
candidate binding remain World implementation invariants verified by behavior, not
proofs inferred from an opaque return value.

The deferred hierarchy-destruction surface preserves intent without transferring structural authority:

| Exact actor | Permitted effect and boundary |
|---|---|
| `CommandWriter.Destroy(Entity, HierarchyDestroyMode): void` | Capture one checked live-target request in its active writer domain; preserve mode. No live installation, Entity issuance or expansion authority. |
| `CommandWriter.Destroy(EntityToken, HierarchyDestroyMode): void` | Capture one checked same-writer temporary-target request; preserve mode and cancellation rules. No cross-writer application-token grant. |
| `World.DestroyEntity(Entity, HierarchyDestroyMode): void` | Idle owner-boundary request through the common coordinator; no deferred sequence or receipt. |
| `WorldStructuralPlanWriter.Destroy(int, WorldEntityProjectionReference, HierarchyDestroyMode): WorldStructuralCommandAnchor` | Validate the Destroy union and supplied mode under the existing exact `DestroyEntity` permission. |
| `SceneWorldParticipant.PrepareDestructionExpansion(WorldDestructionExpansionContext): PreparedWorldDestructionExpansion` | Sole registered hierarchy actor, paired with the existing `ExpandDescendantDestruction` grant; application writers do not acquire it. |
| `WorldDestructionExpansionWriter.InsertDescendantDestruction(WorldEntityProjectionReference): WorldStructuralCommandAnchor` | Insert only the next certified descendant under its expansion context. |
| `WorldDestructionExpansionContext.CreateResult(ImmutableArray<WorldOutputReservation>): PreparedWorldDestructionExpansion` | Validate and seal the complete expansion order; World merges it once at the original anchor. |
| `World.CommitStructuralTransactions()` | World-owned normalization, mode-preserving lowering, expansion/merge, comparison, receipt preparation and installation. Producers cannot call this lifecycle helper. |
| `CommandReceipt.DestroyedEntityIdentities.get` | Public immutable observation only; the World prepared-receipt path populates it. Systems, responders and cleanup callbacks cannot author it. |

These are exact semantic actor requirements for the future declarations, not existing compiler-enforcement evidence. The implementation policy must bind each exact signature/role; public callers receive only the two capture operations and copied receipt observation. No `World.*` type-wide grant, new friendship or dependency edge follows from these rows.

#### Semantic ownership and exact actors

The repository analyzer uses declaring assembly plus semantic namespace to resolve owners against the tables above, with exact reviewed exceptions for implementation namespaces. A `TerraFX` prefix alone does not identify Core; repository tooling's `TerraFX.Diagnostics` is not Core's diagnostics owner, and backend namespaces are not portable Graphics. The Runtime rows are non-transitive allowlists. Same-owner and ordinary BCL references are allowed; transitive project references, internal accessibility and issuer permission do not grant another dependency edge.

Analyze handwritten and generated declarations, effective signatures, bound type/member references, generic arguments including inference, conversions/operators and the selected implicit patterns below. Enable generated-code analysis/reporting and concurrent execution explicitly; keep symbols, semantic models, CFGs and all compilation-derived caches within their owning compilation. Do not recursively inspect arbitrary implementations or expand all base metadata at every use: a higher-layer implementation of a lower interface remains valid dependency inversion. Preserve public `World.Create`, shared `AssetStore.CreateAsync`, documented value construction, opaque-handle copying/defaults and unrelated geometry bitcasts.

The active semantic policy is exactly `tools\TerraFX.Diagnostics.Analyzers\Policies\RuntimeArchitecture.v1.json`, with `policyId = terrafx-runtime-architecture`, `format = TerraFX.RepositoryArchitecturePolicy`, `formatVersion = 1`, and a reviewed `revision` and `mode`. Required-build enrollment selects this one semantic-policy AdditionalFile; includes, overlays, environment substitution, nearest-file discovery, version fallback and caller-controlled policy selection are forbidden. Its closed top-level fields are `format`, `formatVersion`, `policyId`, `revision`, `mode`, `assemblies`, `projects`, `owners`, `exceptions`, `types`, `members`, `representations`, `viewTypes`, `exports`, `protected`, `grants` and `effects`. This format has not shipped: the accepted schema fixes version 1 before implementation; changing grammar or interpretation requires a format-version change, while reviewed inventory changes increment `revision`. Unknown versions, fields, tags and combinations fail closed.

The policy inventory identifies exact protected representations/storage, sensitive constructors/members, actor permissions, exports and overload effects. Applicable entries in the current compilation or available references must resolve without conflicts; actor entries are validated when their owning compilation exists. Future unimplemented symbols do not require stubs in unrelated Core compilations. Missing, malformed, conflicting, ambiguous or unresolved applicable policy invalidates analysis rather than authorizing access.

#### Canonical policy, coverage and symbol keys

Canonical policy bytes are strict UTF-8, no BOM, minified JSON, exactly one final LF. Object keys sort by unsigned UTF-8 bytes of decoded strings. Escape quotes and backslashes; encode controls as lowercase `\u00xx`; emit other Unicode scalar values as raw UTF-8 without normalization or case folding. Reject invalid surrogates, duplicate decoded object keys, comments, trailing commas, `null`, floating-point/exponent numbers and non-shortest UInt32 decimal spellings. Sets reject duplicates before sorting by canonical element bytes; signatures, modifiers, type arguments, paths and indexed copy records remain ordered sequences. The raw selected file must equal its canonical encoding.

Let `C` be the complete canonical file bytes, including LF. `PolicyDigest = SHA256(C)`, and the policy-content item in the `H` input manifest hashes those same bytes. Completion carries that same digest. A semantic projection digest or a hash omitting LF is not a substitute.

#### Trusted parser-admission profile and physical-source binding

`RepositoryArchitecturePolicy1` defines format validity and semantic interpretation;
resource admission is a separate trusted implementation profile. The trusted build
selects `RuntimeArchitectureParserProfile1`, not a policy field or caller property.
An input that is valid version-1 format but exceeds the selected profile is a
`TFX0007` resource-admission failure, not an unsupported `formatVersion`; it cannot
be analyzed or certified in Audit or Enforce. Changing the profile cannot change
version-1 grammar, Unicode/key interpretation, set semantics or authorization.
The profile identity and immutable definition digest, plus the relevant validator
implementation identities/digests, participate in `H`. This profile applies only to
the selected Runtime semantic policy, not coverage documents, proof transport or
unrelated AdditionalFiles.

The candidate profile has these inclusive limits and counting rules:

| Limit | Counting rule |
|---|---|
| 8,388,608 raw bytes | Original selected snapshot bytes before decoding, including the required final LF. |
| 8,388,608 canonical bytes | Complete canonical serialization including final LF; enforce its own bounded writer before growth. |
| 64 container depth | Root object is depth 1; each nested object/array adds one. A scalar directly inside a depth-64 container is allowed. |
| 262,144 nodes | Root plus every property value and array element recursively, including containers; keys are not nodes. Count before allocating the node, not after projection or set processing. |
| 32,768 entries per object | Count every property in every object, including the root. |
| 4,096 elements per array | Count every element in every array, regardless of semantic role. |
| 8,192 decoded UTF-8 bytes per string | Count each key and string value after JSON escape decoding, using strict UTF-8 and no normalization. |

These numeric limits remain unqualified until the approved production inventory,
required growth headroom, and measured allocation/latency for many-small-node,
many-key, maximum-string, maximum-depth and approved concurrent-compilation shapes
support this exact profile. Until then, exceeding a candidate limit fails closed but
the candidate values do not establish inventory capacity or implementation
acceptance. The analyzer cannot bound allocations already performed by an IDE host
before `AdditionalText.GetText()` returns; required-build capture and parser-owned
growth are bounded independently.

The required-build boundary captures one coherent, bounded snapshot of the selected
physical file under a qualified protocol that identifies both the selected file
object and its namespace binding. It validates the original bytes as strict UTF-8
without a BOM and requires exact equality with canonical version-1 bytes; those
original bytes are `C` and determine `PolicyDigest` and the policy-content input in
`H`. It supplies Csc an immutable content-addressed carrier containing those exact
bytes, with a qualified mapping to exactly one active logical policy AdditionalFile.
Contents and resolvable namespace binding remain stable through every compiler read
and final verification. The protocol states its supported-writer/threat model and
is qualified separately for each supported platform/storage mechanism; unsupported
storage or writer conditions fail required validation. Read-only flags, endpoint
hashes, arbitrary path reopening inside the analyzer, or a digest-named path alone
do not establish this boundary.

Capture the selected policy `SourceText` once. The qualified Csc/enrollment boundary
requires the effective checksum algorithm and observed `SourceText.ChecksumAlgorithm`
to be SHA-256, its `GetChecksum()` to match the trusted raw digest, and an independent
SHA-256 of strict UTF-8 encoding of the actual observed characters to match that
same digest and the manifest/completion policy digest. This consistency check is
provenance only within the qualified loader and enrollment boundary; checksums on
arbitrary or string-backed `SourceText` do not attest a physical file. The analyzer
does not reopen `AdditionalText.Path` to recover raw bytes.

Carrier selection preserves one logical active-policy identity and analyzer-visible
path semantics. Use a qualified path-compatible carrier or trusted manifest-based
mapping; reject missing, duplicate, substituted, mismatched and unapproved mappings.
Do not blanket-strip or rewrite `AdditionalText.Path`, and do not provide both the
original and carrier as duplicate active policies. A stable logical mapping avoids
fresh per-attempt carrier paths changing unchanged semantic inputs while preserving
the path value observable by analyzers.

The snapshot is the effective policy input for that compilation; do not claim that a
mutable original remained immutable after capture unless the selected platform
protocol proves it. Before every compilation-proof reuse decision, coherently
revalidate the current selected raw input, parser profile and validator identities,
enrollment, producer bindings and certified outputs. An old carrier cannot qualify
a now-missing, malformed, BOM-prefixed or over-limit current input. If current
inputs differ, current-input `H` must not match the stored proof. A fresh `N` is
required for actual compilation, not for a reuse decision.

Trusted required-build enrollment independently selects the exact revision/digest of `RuntimeArchitectureCoverage1` and `RuntimeArchitectureCoverageDerivation1`, plus the enrolled unit and reference-profile binding. The coverage document has the closed fields `baselineId`, `format`, `formatVersion`, `revision` and `units`; each unit binding contains its trusted project, `projectionSha256` and `referenceProfile`. The candidate policy cannot select, regenerate or exempt itself from that baseline. The independently derived projection includes all applicable authorization, effect, representation and export records in its selected domains, including type-wide grants. Deleting a restriction or adding a broader grant changes the projection. Required document facts remain required when a future assembly is absent; binding-only entries become applicable with their independently enrolled implementation unit. A candidate cannot label an already applicable symbol as future to avoid resolution. Re-encoding a deletion and recomputing policy digest and `H` must still fail mandatory coverage.

The accepted independent conformance vector `PositivePolicyFixture3` is 2,469 canonical bytes, SHA-256 `057c82ab7c6ee7df2ef3eed65445dda0b5412baacd9adcb174335ed87a8ad995`; its whole-domain projection is `a05f2b0c88c1712e1dfaa21815bcc068fc47297182d2b7a36cb559eb786daace`. `CoverageFixture3` is 368 bytes, SHA-256 `ad216d957e2c70cba6a2d89e749cc0eeea2cfd49af87b7f8417e1c486130766a`. With fixture revision changed to 4 and policy/`H` recomputed, deleting `protected.epoch`, `protected.raw` or `effects.acquire` still fails independently selected coverage; the respective candidate digests are `b9bbf2a1ea45d3c676aa718f71f30bba8ea390612281ed26ca16e41f714e4ff4`, `b47dc4997ceaa6c1f9756a879a70f7bc270f842d8f3f924685a3dbd6abbe56fb` and `78a0c9dae378739a88b12e5f634dace08b5e80228c1ff8437b5c0900528d4a4c`. The deleted target's grants are removed too, so these are not dangling-reference failures.

Resolve every applicable catalog key uniquely in its designated domain, validate its symbol kind/signature/relationship, normalize source/imported identity and only then compare structural keys. Unresolved values may not be fabricated into keys or compare equal. Type definitions retain declaring assembly, namespace segments and nested metadata names including generic arity; assembly stable identity is `[name,culture,publicKeyToken,contentType,retargetable]`, with actual version and contents still verified for references. Type expressions retain declaring-type/method generic ordinals, array rank/SZ distinction, pointers and exact function-pointer signatures. The array production is exactly `["array", rank, isSZArray, elementType]`; SZ arrays require rank 1. Custom modifiers and parameter/ref shapes are ordered.

An ordinary member name is `["name", metadataName]`; an explicit implementation uses `["explicit", InterfaceSlotSet]`, where `InterfaceSlot = [constructedInterfaceType, originalInterfaceMemberId]`. Derive slots from actual source `ExplicitInterfaceImplementations` or imported metadata implementation relationships, with generic substitution by declaring-type/method ordinal; compiler display names and parameter spelling are not identity. Accessors use `["accessor", canonicalAssociatedMemberId, "get"|"set"|"init"|"add"|"remove", CallSignature]`; `init` remains distinct from `set`, including required `IsExternalInit`. A call signature retains staticness, readonly-receiver status, generic arity, calling convention, exact result/ref/scoped shape and ordered custom modifiers. Resolve both source and imported implementation/reference symbols uniquely. Alias labels are document references, not authority identities; normalized collisions invalidate the policy.

Every access retains both its containing-type identity and canonical exact declared-member identity. Canonicalize a method as `(method.PartialDefinitionPart ?? method).OriginalDefinition`; retain its constructed containing-type/method arguments for dependency checks before normalization. Entries naming either partial half resolve to the same actor; conflicting permissions for those entries invalidate policy. Partial property/event permissions retain the canonical associated member and accessor kind, so get/set and add/remove remain distinct.

Lookup exact-member permission first, then only an explicit reviewed type-wide entry. An exact partial method grant does not extend to generated siblings, the containing partial type, nested helpers or derived types. Type-wide entries cover only their declared scope, not nested/derived types. Local functions/lambdas retain their enclosing declared-member actor; field/property initializers belong to their declared member, not an inferred constructor grant. Generated-file status supplies no authority.

#### Closed typed effect records and exports

Every overload effect is one closed `EffectRecord1` with exactly these fields:

```text
{
  "bind": Binding,
  "inputs": InputEffectSet,
  "ambient": [ReadSet, WriteSet, EscapeSet],
  "return": ReturnEffect,
  "updates": UpdateSet,
  "copies": CopySequence,
  "materializes": ProducedSet
}

InputEffect = [InputSlot, ReadSet, WriteSet, EscapeSet]
Footprint = ["at", Endpoint]
ReadSet / WriteSet / EscapeSet = duplicate-free set of Footprint
ReturnEffect = ["void"] | ["value", OriginSet] | ["storage", OriginSet]
Update = [PostPlace, NormalFinalOrigins, AnyAssignedOrigins]
PostPlace = ["argPostValue", ordinal] | ["store", LocationEndpoint]
Produced = ["return"] | ["post", PostPlace] | ["copy", copyOrdinal]
```

Input/update/footprint/origin/materialization collections are duplicate-free canonical sets; `copies` is an ordered sequence. A byref argument's empty-path destination canonicalizes to `["argPostValue", ordinal]` and cannot also be represented by an equivalent `store` update. `NormalFinalOrigins` describes values possible after successful return. `AnyAssignedOrigins` includes every value assigned at any execution prefix, including overwritten values and values assigned before throwing; it contains every normal-final and applicable copy-produced alias. Absence of an update asserts no change and is not an unknown-but-harmless fallback.

Endpoints are `[Root, Path]`. Roots are `["receiverValue"]`, `["receiverStorage"]`, `["argValue", ordinal]`, `["argStorage", ordinal]`, `["export", exportId]`, or `["exportInstance", exportId, InputSlot]`, where `InputSlot` is `"receiver"` or `["arg", ordinal]`. `export` is valid only for a certified static field; `exportInstance` is valid only for a certified instance field bound to that actual input. The checker distinguishes typed values `V<T>`, locations `L<T,mutability>` and element/byte regions `R<T,mutability>`. `argValue` is an entry value, not its byref destination; `argStorage` is the actual byref location; entry `load` of an uninitialized `out` value is invalid. A reference receiver's object is reached by `referent`, not a fabricated replaceable `this` slot.

Allowed path steps are closed: `"load"` (`L<T> → V<T>`, retaining contained aliases), `"referent"` (managed reference/typed pointer to referenced storage), `["field", memberId]` (uniquely resolved field on the substituted owner), `["valueField", memberId]` (copied aggregate component, never its original address), `["elements", extent]`, `["element", index]`, `["pointee", typeExpression]` and `["bytes", extent]`. Extents are `["one"]`, `["all"]`, `["elements", IndexTerm]`, `["bytes", IndexTerm]` or `["sizeof", TypeExpression]`. Index terms are `["constant","positive"|"negative",UInt32]`, `["arg",ordinal]` or `["any"]`; negative zero and negative lengths are invalid. Generic substitution occurs before endpoint typing, mutability and representation validation. Properties, accessors, display-name fields, incompatible owners and unsupported transitions are invalid.

Origins are `["valueCopy",ValueEndpoint]`, `["load",LocationEndpoint]`, `["address",LocationEndpoint,"readonly"|"writable"]`, `["independent",TypeExpression]`, `["default",TypeExpression]`, `["fresh",TypeExpression]`, `["freshShallow",Origin]`, `["view",TransformTag,Origin,TransformArguments]`, `["bits",Origin,TargetTypeExpression]` and `["rawLoad",ByteRegionEndpoint,TargetTypeExpression]`. `fresh` means genuinely new approved storage; a span over a new array is a view of `["fresh",["array",1,true,ElementType]]`, not fresh span storage. `freshShallow` preserves contained aliases. Raw/bits production of an issued representation is not an ordinary opaque-token copy.

The finite transform-argument forms are:

| Transform | Exact arguments and result restriction |
|---|---|
| `arrayToSpan`, `arrayToReadOnlySpan`, `stringToReadOnlySpan`, `memoryToSpan`, `readOnlyMemoryToSpan`, `readonlyView`, `asBytes`, `pointerFromAddress`, `removeReadonly` | `[]`; exact input/output view kind is fixed by the tag; only `removeReadonly` may widen address mutability, and only for an exact approved member |
| `slice` | `["elements", Start]` or `["elements", Start, Count]`, where `Start`/`Count` are `Term`; selected overload fixes suffix versus start/count form and exact formal ordinals |
| `spanCast`, `pointerCast`, `refCast` | `[TargetTypeExpression]`; exact substituted target/result type required; `MemoryMarshal.Cast<TFrom,TTo>` uses method parameter 1 |
| `spanFromAddress`, `readOnlySpanFromAddress` | `["elements", Count]`, where `Count` is `Term`; exact count formal and source mutability required |
| `pointerOffset`, `refOffset` | `["elements", TypeExpression, "add"|"subtract", Term]` or `["bytes", "add"|"subtract", Term]`; exact element/byte unit and formal required |

`Term` is `["constant","positive"|"negative",UInt32]`, `["arg",ordinal]` or `["any"]`. Negative zero and negative start/count constants are invalid. Integral argument terms bind the original formal ordinal through `IArgumentOperation.Parameter`, including reduced extensions, not source argument order. In a symbolic effect, `["methodParameter",ordinal]` names the resolved member's generic method parameter; it is substituted to the actual `TypeExpression` before result-type comparison. `any` is conservative unknown extent/offset and never establishes independence. Element offsets scale by `sizeof(T)`; byte offsets scale by one. Unknown arithmetic/overflow retains roots. No catch-all transform exists: wrong count, type, direction, unit, source/result view or readonly widening is invalid policy, not an empty origin.

These complete records are canonical examples attached to the exact normalized selected overloads; each object retains all fields, including empty effects:

```json
{"ambient":[[],[],[]],"bind":"available","copies":[],"inputs":[["receiver",[],[],[]],[["arg",0],[],[],[]],[["arg",1],[],[],[]]],"materializes":[],"return":["value",[["view","slice",["valueCopy",[["receiverValue"],[]]],["elements",["arg",0],["arg",1]]]]],"updates":[]}
```

This is `Span<T>.Slice(int,int)`: readonly receiver semantics do not make the returned `Span<T>` readonly.

```json
{"ambient":[[],[],[]],"bind":"available","copies":[],"inputs":[[["arg",0],[],[],[]]],"materializes":[],"return":["value",[["view","spanCast",["valueCopy",[["argValue",0],[]]],[["methodParameter",1]]]]],"updates":[]}
```

This is writable `MemoryMarshal.Cast<TFrom,TTo>(Span<TFrom>)`; its result element type is method parameter 1 after substitution. Its readonly-span overload has a separate key and readonly result.

```json
{"ambient":[[],[],[]],"bind":"available","copies":[],"inputs":[[["arg",0],[],[],[]],[["arg",1],[],[],[]]],"materializes":[],"return":["storage",[["view","refOffset",["address",[["argStorage",0],[]],"writable"],["elements",["methodParameter",0],"add",["arg",1]]]]],"updates":[]}
```

This is `Unsafe.Add<T>(ref T,int)`; byte-offset helpers use the `["bytes","add",["arg",1]]` form instead. Both preserve the source root and writable ref kind.

```json
{"ambient":[[],[],[]],"bind":"available","copies":[],"inputs":[[["arg",0],[],[],[]],[["arg",1],[],[],[]]],"materializes":[],"return":["storage",[["view","refOffset",["address",[["argStorage",0],[]],"writable"],["bytes","add",["arg",1]]]]],"updates":[]}
```

This is `Unsafe.AddByteOffset<T>(ref T,nint)`.

For the certified `BufferOwner.Acquire()` instance export:

```json
["field","bufferOwner","_cells",false,["value","none",["array",1,true,["named","i32",[]]],[],[]]]
```

```json
{"ambient":[[],[],[]],"bind":"available","copies":[],"inputs":[["receiver",[],[],[]]],"materializes":[],"return":["value",[["view","arrayToSpan",["load",[["exportInstance","ownerCells","receiver"],[]]],[]]]],"updates":[]}
```

`owner.Acquire()[0]` permits an acquisition-authorized scalar read; `owner.Acquire().Clear()` requires Write and is denied to an acquisition-only actor. The same applies through an argument-bound static bridge and after generic owner substitution.

Reject a two-argument `Slice` summarized with only one start term, missing or swapped formal ordinals, a `Cast` with the wrong number of target expressions or method-parameter 0 as its result type, an `Add` with omitted/unrelated scale type or byte units, an `AddByteOffset` with element units, and any readonly-to-writable result without exact `removeReadonly`. These are invalid policy (`TFX0007`) and cannot emit successful completion even after policy digest/`H` recomputation.

The positive array expression `["array",1,true,["named","i32",[]]]` is a 36-byte canonical encoding with SHA-256 `69c1f7b08eb76d442b18b9f0c0f83657a76e66f6191d9c2e4ac07e05e685b152`. Reject `["array",["named","i32",[]],1,true]`; slot 1 is not a rank. Historical policy fixtures remain unchanged. The five-record `TransformInstanceVectors1` bundle (`add`, `addByte`, `cast`, `instance`, `slice`) is 1,281 bytes with SHA-256 `41d36790fe1d8e4d8c76567ee9c5fd48d2f6f2e8c49b19641da43f3b5be14231`.

Copies are exactly `["value",Origin,LocationEndpoint]`, `["cells",RegionEndpoint,RegionEndpoint]`, `["rawValue",ByteRegionEndpoint,LocationEndpoint]` or `["bytes",ByteRegionEndpoint,ByteRegionEndpoint]`. `value` is a typed shallow copy; `cells` may copy equal substituted element types and retains aliases; `rawValue` produces the destination's exact `T`; `bytes` may partially mutate its destination and cannot assert independence. Copy reads/writes and all possible produced aliases participate in exceptional transfer. `materializes` names the produced value: `["return"]`, `["post",PostPlace]` or `["copy",copyOrdinal]`. Cover every raw-produced protected representation, including ref/out post-values and copied destinations; omitting its marker invalidates policy/coverage. Construction, sensitive-call, acquisition, Write, Escape and Materialization remain independent capabilities.

`ExportRecord` is exactly `["staticField", anchorMemberId, protectedTargetId, Path]` or `["instanceField", anchorMemberId, protectedTargetId, Path]`. Static anchors resolve uniquely to actual static fields; instance anchors resolve uniquely to actual nonstatic fields. `exportInstance` binds the anchor to the actual entry receiver or argument and its substituted owner type, never to a type globally, parameter name or arbitrary instance. The root retains that input's possible identities through copies; ambiguous inputs form a union, never an independent result. Rebinding the formal does not retarget the entry root. A static/instance mismatch, incompatible or ambiguous owner, unavailable projection, or value snapshot used as fabricated storage fails closed. A readonly field containing an array does not make its elements readonly.

Both export kinds require the same producer artifact-group proof. Bind the actual consumed reference by contents, identity and role; require exactly one accepted producer group, the current policy digest, approved coverage/export projection, successful current `I/H/N` completion and every certified output hash. Validate the paired implementation and resolve private anchors omitted from a reference DLL there, before structural comparison. Repeat validation before compilation and proof reuse. Bind consumed artifact role/hash, paired implementation hash and export-contract digest into caller `H`; exclude derived producer proofs, SARIF/logs and nonces. Missing, stale, ambiguous, replaced, wrong-policy or self-referential producer evidence fails verification and never turns protected storage into fresh/independent data.

#### Protected aliases and overload effects

Construction, sensitive call, callable acquisition, writable-alias acquisition, Write, Escape and raw Materialization are separate capabilities; none implies another. The protected-storage inventory explicitly distinguishes value-type substorage, container slots/elements and referenced objects. Replacing a reference and mutating its referent are not interchangeable operations.

| Bound operation | Required classification |
|---|---|
| Sensitive constructor or lifecycle/unchecked call | Construction or the exact sensitive-call capability |
| Simple/compound assignment, increment/decrement, `??=`, initializer write | Write to each protected destination |
| Deconstruction assignment | Write for each protected lvalue leaf |
| Ref assignment/local, writable ref-return or address acquisition | Writable-alias acquisition |
| `ref`/`out` passage of protected storage | Acquisition and Write; Escape additionally when the bound parameter permits it |
| Ref return, outward ref-field/heap storage, capture or unscoped alias passage | Escape, with the separately required acquisition/write effects |
| Protected method group, delegate conversion or function-pointer acquisition | Callable acquisition even without invocation; `nameof` is only a dependency reference |
| Known raw creation/reinterpretation of an issued representation | Materialization or writable-representation exposure according to protected target and actual ref kind |

Match `IArgumentOperation.Parameter` for named/reordered arguments and reduced extensions; inspect its actual `RefKind`/`ScopedKind` and the member's return-ref kind. A `scoped` writable parameter may write but does not automatically escape; an `out` parameter is not automatically unscoped. Readonly acquisition does not acquire Write permission. A recognized helper that removes readonly protection must apply its exact writable-acquisition effects.

A copy loses backing-storage provenance only when its reviewed representation/effect proves independent storage. Span, memory, pointer, array/object-reference and alias-bearing aggregate copies retain possible protected roots; by-value syntax is not independence. Ordinary copying of an opaque issued token remains valid under its representation contract, without granting dereference or mutation authority.

Use compilation-scoped intraprocedural CFG analysis for bodies encountering protected roots/aliases. Track a finite may-alias set, view mutability and supported storage projections for locals, parameters, flow captures and aggregate components:

| Transfer | Alias/effect rule |
|---|---|
| Alias-bearing assignment/copy | Preserve possible roots and view information |
| Reference/pointer cast or offset, span/memory conversion | Preserve roots with the recognized view transformation |
| Span construction, array/memory view, raw-reference helper | Derive roots from the inventoried receiver/formal parameter |
| Slice/range | Preserve protection; narrowing a view does not remove authority requirements |
| Indexer, ref-return or pointer dereference | Project addressed storage from receiver roots |
| Indexer assignment, `Clear`/`Fill` or raw writer | Require Write for every possible protected destination |
| Reviewed independent scalar/value copy | Remove backing-storage aliases only for independent contents |
| `ToArray` or another reviewed fresh copy | Fresh container, but preserve shallow element/reference aliases |
| Rebind a definitely identified local | Replace only that local's facts; previously copied aliases remain protected |
| Conditional/branch/loop join | Union possible roots and iterate loops to a fixed point |
| Heap storage, return, capture or unscoped passage | Apply the independent Escape capability and exact effect summary |

Strong updates require a definitely identified local/supported location; ambiguous heap updates are conservative. Rebinding on one incoming path cannot erase another path's root. Rebinding on every incoming path may prove independence only for that location, not earlier copies. For a call edge, the normal successor applies only successful normal-final updates, strong-updates definite locations, and produces the return origin. The exceptional successor retains the call-entry state and weakly unions every possible `AnyAssignedOrigins` and copy-produced alias into every possibly affected destination; it never produces the failed call's return value. This applies to both `ref` and `out`: the callee cannot read an incoming `out`, but throwing does not erase caller provenance that existed before the call. Propagate these states through filters, catches, nested exception regions and `finally`; account for `ControlFlowBranch.FinallyRegions`, not only ordinary `BasicBlock.Predecessors`. Evaluate receiver/arguments in language order so exceptions before invocation use their corresponding pre-call states. Missing or unsound exceptional effects cannot produce clean completion.

Cross-member behavior uses finite reviewed overload/parameter-effect summaries, not unrestricted interprocedural points-to analysis. Each resolved member entry specifies receiver/formal-parameter reads, writes, returned aliases and mutability, escape, independent storage, or shallow alias-bearing copies. The following groups require explicit entries for the actual selected-reference overloads; the names are coverage obligations, never wildcard authorization:

| Required overload inventory | Effects |
|---|---|
| Span/readonly-span array, ref and pointer constructors; array/memory views including `AsSpan`/`Memory.Span` | Alias of supplied storage |
| Span/memory `Slice(int)`/`Slice(int,int)` and range lowering | Alias of receiver |
| Span indexer and pinnable-reference access | Addressed alias with actual ref mutability |
| Span `Clear()`/`Fill(T)` | Write receiver |
| Span/readonly-span `CopyTo(Span<T>)`/`TryCopyTo(Span<T>)` and memory counterparts | Read source, potentially write destination; shallow copied contents retain aliases |
| Span/memory `ToArray()` | Independent container, not necessarily independent elements |
| Writable/readonly `MemoryMarshal.AsBytes`/`Cast`; `CreateSpan`/`CreateReadOnlySpan`, `GetReference`, `AsRef` | Preserve roots and apply exact view/ref effects |
| `Unsafe.As`, `AsRef`, `AsPointer` and supported offset/reference helpers | Preserve roots; exact acquisition/view effects |
| `Unsafe.BitCast`, `Read`/`ReadUnaligned`; `MemoryMarshal.Read`/`TryRead` | Materialization when the result is a protected issued representation, independently of alias provenance |
| `Unsafe.Write<T>(void*, T)`; pointer and `ref byte` `WriteUnaligned<T>` | Write parameter 0 |
| Both `Unsafe.Copy<T>` overloads; pointer/byref `CopyBlock`/`CopyBlockUnaligned` | Write parameter 0, read parameter 1 |
| Pointer/byref `InitBlock`/`InitBlockUnaligned` | Write parameter 0 |
| `MemoryMarshal.Write<T>(Span<byte>, in T)`/`TryWrite<T>(Span<byte>, in T)` | Potentially write parameter 0 |

`TryWrite`/`TryCopyTo` are potentially writing operations, not unconditional no-write summaries. Readonly helper input does not prove a readonly returned reference. Raw classification follows protected destination/representation and traced storage, not helper name alone. Outbound readonly byte views are not automatically issuance unless the exact inventory restricts representation reads; unrelated geometry bitcasts remain allowed, but a geometry exemption cannot override an exact protected target.

The exact selected raw-helper inventory distinguishes typed copies from raw production. In particular, `Unsafe.Copy<T>(void*, ref readonly T)` reads the selected readonly source and typed-copies to the destination; `Unsafe.Copy<T>(ref T, void*)` raw-loads the exact destination `T`; and `MemoryMarshal.TryRead<T>(ReadOnlySpan<byte>, out T)` materializes the `out T`, not its independent Boolean return. Match the selected formal `RefKind`/`ScopedKind`, not argument spelling. Readonly-to-writable helpers such as `MemoryMarshal.GetReference(ReadOnlySpan<T>)` and `Unsafe.AsRef(in T)` require an exact `removeReadonly` effect; acquisition permission and subsequent Write permission remain separate.

An unknown call receiving a tracked alias is an explicit unreviewed alias-effect violation unless an exact summary proves its effect. Unsupported alias transformations conservatively retain protection and deny the action; never classify them as fresh/clean. Resource exhaustion, missing required semantics or failure to complete analysis invalidates coverage/completion. This is bounded repository enforcement, not a proof against arbitrary reflection or unreviewed unsafe reconstruction. Wrappers crossing protected boundaries need exact summaries and reviewed unsafe contracts.

Invalid active policy takes precedence. Otherwise report one issuer finding per operation containing missing capabilities and possible roots; fold a dependency finding for the same protected target into it, but retain distinct forbidden references. An issuer grant cannot override a forbidden dependency. Preserve actor, target, action, root and policy identities in diagnostic properties; implicit findings use the originating syntax. Deduplicate semantic/CFG reports for the same actor, target, action and location.

#### Diagnostics, audit and suppression

The new Runtime architecture analyzer exposes these six stable, distinct descriptors
with category `Architecture`; preserve the existing Style IDs `TFX0001` and
`TFX0002`. Add each ID to the analyzer's `Unshipped` release ledger in the same
change that adds its supported descriptor: release tracking rejects an unshipped ID
that no analyzer currently supports. Keep the entries unshipped until an actual
release baseline is recorded. No automatic fix moves ownership or grants authority.

| Rule ID | Symbolic descriptor | Default severity | Enabled | Tags |
|---|---|---|---|---|
| `TFX0003` | `DependencyAudit` | Info | Yes | `NotConfigurable` |
| `TFX0004` | `IssuerAudit` | Info | Yes | `NotConfigurable` |
| `TFX0005` | `DependencyViolation` | Error | Yes | `NotConfigurable` |
| `TFX0006` | `IssuerViolation` | Error | Yes | `NotConfigurable` |
| `TFX0007` | `InvalidArchitecturePolicy` | Error | Yes | `NotConfigurable`, `CompilationEnd` |
| `TFX0008` | `ArchitectureAnalysisCompleted` | Info | Yes | `NotConfigurable`, `CompilationEnd` |

The versioned policy selects Audit or Enforce, not `.editorconfig` or warning promotion. Valid audit findings, including conservative alias-effect findings, remain nonblocking under warnings-as-errors. Invalid applicable policy or incomplete analysis cannot certify either mode. Audit is migration/triage evidence, never M03 acceptance; required M03 lanes use Enforce after reviewed representative-code qualification.

Audit implemented Core, then real World/jobs/scene/Assets/persistence and generated Runtime code as those owners become available. Retain revision, policy/analyzer digest, exact compiler/reference profile, diagnostic counts and every false-positive disposition. Fix extraction or exact inventory/effect defects before promotion. Synthetic fixtures alone do not justify unconditional Error enforcement, and broad namespace/type grants, suppressed baselines or "by-value means independent" cannot replace qualification.

`NotConfigurable` is not immunity to `SuppressMessage` source attributes. Do not falsely tag analyzer diagnostics `Compiler`. Required verification inspects retained source-suppressed SARIF results and the analyzer's pre-suppression counts: enforcement or invalid-policy findings fail even if suppressed; valid audit counts remain visible and nonblocking. Suppressing completion cannot certify execution. Test actual `.editorconfig`, `NoWarn`, pragma, local/assembly/module/targeted/unconditional suppression attributes and suppressors under the selected compiler, not only effective options in an in-memory driver.

#### Required Csc enrollment and completion proof

Required enrollment derives from a trusted canonical product-project/lane inventory, independent of `TerraFXUseRepositoryAnalyzers`, optional import switches and successful policy parsing. Analyzer/fixer and analyzer-test infrastructure retain explicit exemptions; a caller property cannot remove or downgrade an enrolled M03 lane. Use the existing repository-only analyzer project and ordinary SDK build/test workflow; MSBuild/SARIF verification stays outside the compiler-hosted assembly. The current optional analyzer reference is not itself required-enrollment or execution proof.

For each consumed producer reference, validate the actual artifact group, current producer enrollment and inputs, producer completion, policy/coverage/export binding, implementation/reference relationship and certified outputs before `Csc` and before any incremental-proof reuse. Definition-side analysis uses the current compilation, never prior outputs or its own producer proof. Acyclic producer bindings contribute the consumed artifact role/hash, paired implementation hash and export-contract digest to the caller's semantic `H`; producer proof files, SARIF and nonces remain excluded to avoid circular fingerprints.

The late precompile guard checks resolved `@(Analyzer)` identities, semantic policy and invocation `@(AdditionalFiles)`, effective execution after `_ComputeSkipAnalyzers`, compiler execution and a fresh error-log destination. Verify actual final `Csc` arguments, rejecting missing inputs, effective `_SkipAnalyzers`/implicit skipping, `SkipCompilerExecution`, `/skipanalyzers+` and unapproved response files. `RunAnalyzers=true` can override `RunAnalyzersDuringBuild=false`; `UseSharedCompilation=false` is not a disable switch. Required-lane verification also runs outside the optional enrollment import, so removing that import cannot silently qualify a build.

A versioned completion/proof record binds one compilation input set and its exact emitted outputs:

| Field | Binding |
|---|---|
| `I` | Canonical repository project, assembly, TFM, Configuration, Platform/RID as applicable, exact compiler profile and selected output paths |
| `H` | SHA-256 of the canonical ordered effective compiler-input manifest |
| `N` | Fresh unpredictable nonce for the actual compilation attempt |
| Policy/analyzer | Policy digest, mode, parser-profile identity/definition digest, validator implementation identities/digests and analyzer binary identity/digest |
| Analysis | Coverage status, generated-source digest and pre-suppression finding counts |
| Outputs | Paths and SHA-256 hashes added by the verifier only after successful emission |

Preserve meaningful input order in `H`: effective source inputs, reference contents/properties, analyzer/generator binaries, policy and other semantic AdditionalFiles, analyzer configuration, compiler options, relevant imported build configuration, resources/signing and declared generator inputs. Bind the selected policy's raw/canonical digest, trusted parser-profile definition and validator identities. Exclude this compilation's derived proofs, nonce transport, SARIF/logs, fingerprint marker and output contents to avoid circular/self-invalidating fingerprints. Per-attempt carrier transport paths are excluded only when a stable logical AdditionalText identity/path and equivalent analyzer-visible semantics remain bound; do not blanket-strip semantic paths. Referenced assemblies are inputs even if another project produced them; stable selected output paths remain part of identity/options.

The required sequence is:

1. Trusted build integration resolves enrollment and the parser profile, performs the qualified bounded raw snapshot/canonical-byte check, binds its immutable carrier and logical policy identity, computes `I/H` from effective compiler inputs and supplies `I/H/N` to the analyzer.
2. The analyzer captures the selected policy `SourceText` once, checks its SHA-256 checksum and actual-character digest against the enrolled raw digest/profile, validates the canonical text, validates observable compilation identity/options and source/reference content against `I/H`, analyzes generated sources and emits completion containing the same `I/H/N`, policy/mode/profile/validator identities, analyzer identity, coverage, generated-source digest and pre-suppression counts.
3. Each actual `Csc` execution uses a newly allocated invocation-specific SARIF destination with no prior results; an execution hook/observed task result distinguishes execution from an incremental skip. No two executions share a nonce/log.
4. After emission, verification requires exactly one matching unsuppressed completion, successful compiler exit, complete coverage, retained diagnostic results, unchanged enrolled effective inputs/carrier and hashes of every required output, including implementation/reference DLLs and PDBs where produced. Current physical policy input is coherently revalidated before any later proof reuse.
5. Only then atomically persist accepted reusable proof. Compiler failure after completion, analyzer load/execution failure, missing/duplicate/suppressed/mismatched completion or mixed input versions cannot update accepted proof.

A compilation-end diagnostic cannot contain final emitted-output hashes; the verifier supplies that second binding after compiler success. Fresh paths alone are not freshness proof: replayed content must still match `I/H/N`. This prevents stale/accidental replay, not a malicious rewrite of trusted tooling and all evidence.

Update a content fingerprint marker enrolled in `CustomAdditionalCompileInputs` when meaningful inputs change, including policy-only changes preserving timestamps. Transient invocation transport must not force every unchanged build to compile. An approved build-generated response file may carry the transient AdditionalFile separately from that marker; verify its effective final arguments explicitly rather than trusting response-file omission from SDK incremental inputs. Arbitrary response files remain forbidden.

An incremental compiler skip may reuse only accepted proof matching current `I/H`, enrollment/policy/analyzer identities and every certified output hash; timestamps or an existing clean log are insufficient. Separate logs/proofs by identity, serialize competing builds of the same identity/output set or reject collisions, and detect configurations sharing physical output paths. Debug/Release, TFMs and RIDs cannot overwrite or consume each other's evidence.

#### Selected foreach, await and async disposal

An operation walk alone does not expose all compiler-selected implicit dependencies. Retain the centrally selected Roslyn 5.9/C# 14 API compatibility lane separately from actual selected-SDK Roslyn 5.11/C# 15/.NET 11 compiler acceptance, recording serviced replacements rather than inventing future pins. Do not cast an undefined language enum or call 5.9 `Latest` C# 15. Existing style-test/default reference profiles and isolated API probes do not establish this engine-language or required-build gate.

| Construct | Public selected-semantic extraction |
|---|---|
| Both `foreach` forms, including deconstruction | `GetForEachStatementInfo(CommonForEachStatementSyntax)` |
| `await foreach` | The same, including `MoveNextAwaitableInfo`/`DisposeAwaitableInfo` |
| Explicit `await` | `GetAwaitExpressionInfo(AwaitExpressionSyntax)` |
| `await using` statement | `GetAwaitExpressionInfo(UsingStatementSyntax)` |
| `await using` declaration | `GetAwaitExpressionInfo(LocalDeclarationStatementSyntax)` |

Foreach checks selected enumerator, MoveNext, Current/getter, Dispose, element type and Current/Element conversions. Each await result checks selected GetAwaiter, IsCompleted/getter, GetResult and `RuntimeAwaitMethod` when supplied. Check effective signatures, containing types and generic arguments, not just member namespace. Null selected members may legitimately represent array lowering or Runtime Async; distinguish those supported cases from binding errors, dynamic/unverifiable selection or missing required semantics, which cannot yield passing coverage.

Roslyn 5.9 `IUsingOperation`/`IUsingDeclarationOperation` does not expose selected disposal and awaiter details together. Build the executable member's CFG once, locate its implicit disposal-finally `IAwaitOperation`, and take the operand's `IInvocationOperation.TargetMethod` as the selected disposal member. Correlate resource captures, syntax and regions to the originating using statement/declaration, combining this symbol with using-specific await info. Do not assume invocation syntax is the whole using statement, guess `DisposeAsync` by name, inspect all candidates/internal `DisposeInfo`, or inspect arbitrary implementations behind interface dispatch.

Cover expression resources, multiple declarators, nested/nullable resources, early exits and explicit body awaits without disposal misattribution; deduplicate CFG and syntax findings. Required fixtures isolate forbidden edges solely through selected enumerator/MoveNext/Current/Dispose or conversion, awaiter members, async-enumeration awaiters, and each async-using form's inherited disposal/GetAwaiter extension/IsCompleted/GetResult/signature/conversion. Include local-pattern and explicit `IAsyncDisposable` controls and misleading unselected overloads. Deliberately defective metadata producers are extraction fixtures, not valid product layering exceptions.

The independently selectable M03 fixtures and later-owner allocation are fixed in [35](35-implementation-roadmap.md#m03-runtime-analyzer-observations). Design/API-profile probes do not complete analyzer matching, representative-code accuracy, actual compiler enrollment, suppression retention, exceptional-flow coverage or compilation/output-proof acceptance.

### Library packages and consumer references

Each runtime/backend library has one matching package ID and one product assembly of the same name per supported target asset group. Do not bundle another product/private helper/third-party managed assembly in that library package; express managed dependencies as their own packages. Do not publish namespace-shaped compatibility assemblies/packages for the seven merged layers. RID/target variants of that same assembly, its related native assets and ordinary package metadata are not additional product assemblies. `TerraFX.Generators` is a separate analyzer package with its single compiler-hosted assembly, not a `lib` runtime dependency. Editor/content-tool applications deploy separately as qualified AOT artifacts; they are not payloads hidden in a runtime package.

An optional metapackage is dependency-only: package metadata/licenses and dependencies, with no managed/native/analyzer/build/content payload, assembly or automatic registration. Introduce one only for a useful named composition, not to disguise the project DAG. Runtime libraries from an engine release use the same version and the SDK's normal project-reference NuGet dependency ranges. Central package versions select independently versioned managed dependencies; native manifests pin native identities and hashes. NuGet lockfiles and locked restore are not required for development or CI. An optional release process may use a single solution-wide dependency lock/snapshot behind an explicit switch. No manual version matrix or separately versioned merged subsystem is imposed on consumers.

| Consumer | Direct library package references | Contract |
|---|---|---|
| Foundation/numerics/buffers | `TerraFX.Core` | BCL-only foundation |
| Standalone physics | `TerraFX.Physics` | Only Core is transitively required |
| Portable input or CPU assets | `TerraFX.Input` or `TerraFX.Assets` | Each transitively requires only Core |
| Standalone audio | `TerraFX.Audio` | Core and Assets, no Runtime |
| Standalone native window/input | `TerraFX.Platform` | Core and Input, no Runtime |
| Standalone graphics | One selected `TerraFX.Graphics.D3D12` or `TerraFX.Graphics.Vulkan`; `TerraFX.Graphics` alone for portable contracts | Backend brings Graphics/Core, not Runtime |
| Integrated headless simulation | `TerraFX.Runtime` | No graphics backend, editor, content tools or generator runtime dependency |
| Integrated interactive/offscreen application | `TerraFX.Runtime` and one selected backend package | Other library packages are transitive; namespaces such as `TerraFX.Entities` do not require separate references |

The integrated Runtime package deliberately has the library closure listed above, including portable/native adapter packages even for a headless consumer. Consolidation does not promise a minimal restore/download closure for an ECS-only application; independently minimal ECS packaging is not a separate baseline product. Missing/disabled native services are handled by explicit host profiles and selected registrations, not by loading them and hoping trimming removes the side effects. The ordinary application needs one runtime reference plus its explicitly selected backend, not thirteen manual references.

`TerraFX.Core` is safe-facing, not unsafe-free: it inherits the repository's shared `AllowUnsafeBlocks` setting for [06](06-owned-geometry-and-numerics-interchange.md)'s reviewed method-body bitcast blocks and [05](05-owned-buffers-and-internal-collections.md)'s private native allocator/window implementation. The bitcast operation is annotated now in preparation for Unsafe Evolution even when current ordinary C# 15 does not require the context. Proved forward/reverse by-value conversion facades remain safe-callable; consumer projects can keep `AllowUnsafeBlocks = false`. The compilation setting imposes no caller pointer obligation and is not inherited by package consumers. Generators support readonly geometry, explicit binary field tags/kinds and row getters, not mutable matrix fields. `EngineGeometryBinary32Rows2`, `FloatChunk32V2`, frozen exact unit exponent/chunk settings and `EBIN1`/`EPKG2` identities are recorded in manifests, including profile-table version 2 and the pinned UUIDv8 name recipe. Generated finite-admission code uses complete component reductions rather than treating the new mask as a scalar. Compiled NUnit qualification executes NM/BF/SP/BN cases in mandatory NativeAOT lanes and any selected optional JIT comparison lanes; inspecting JIT source is not evidence that a selected binary behaves identically.

`TerraFX.Physics` remains independently usable headlessly; ECS integration belongs in Runtime's `TerraFX.Integration` namespace, not in a duplicate solver inside entity storage. The assembly layering above keeps those dependencies directed. CI checks the physical project/package DAG, logical namespace dependency rules, package contents and public assembly API baselines as their implementations become available under [35](35-implementation-roadmap.md). Use focused tests and existing SDK/MSBuild capabilities; add repository tooling only for a concrete validation gap. Empty products do not require speculative analyzers, package-consumer qualification or full future API baselines. Public names/signatures stay unchanged when moving the seven layers into Runtime, but assembly identity changes require rebuilt consumers/generated metadata and an explicitly reviewed ABI baseline; no old assembly-qualified binary compatibility or schema migration is inferred. Reconcile the target repository's solution/build/package/CI conventions with this structure.

## 2. SDK selection

Development uses a compatible .NET 11.0 SDK selected by [`global.json`](../../global.json). Its `sdk.version` names a published SDK build so the same file can drive CI acquisition. Retain `allowPrerelease = true` and `rollForward = latestFeature`: the version is a compatible SDK minimum, not an exact developer pin. With a compatible SDK installed, a checkout builds through `dotnet build`, including normal NuGet restore. Developer builds do not require frozen SDK/runtime/compiler identities or qualification manifests.

Official-build qualification separately records exact SDK/runtime/target/compiler/instruction set architecture (ISA)/native-tool identities and qualification dates. The profile verified 2026-10-06 used SDK 11.0.100-rc.1.26425.128 and runtime 11.0.0-rc.1.26425.128; RC1 go-live support ends 2026-10-13. Before shipment, verify support against Microsoft's release metadata and requalify a serviced .NET 11 GA SDK/runtime, compiler/native/replay/performance profile. Exact SDK selection (`rollForward` = disable) belongs to those separate build settings, not the developer defaults. No future GA version/hash is invented in this specification.

All implementation tests use NUnit. The supplied repository determines its supported runner/adapter and centrally selected versions; retain its configured NUnit/VSTest foundation rather than selecting a new runner for M01. A conventional baseline uses NUnit, `NUnit3TestAdapter`, and `Microsoft.NET.Test.Sdk`; an established NUnit-compatible Microsoft.Testing.Platform (MTP) setup may be retained only after verifying its integration/options. Do not assume an MSTest SDK provides a NUnit foundation or mix VSTest/MTP switches. TRX reporting requires the selected runner's actual extension/logger; use inherited logging without adding a duplicate logger. Verify fresh per-module selected/executed counts for implemented selections; a test timeout does not make blocked native resource reclamation safe. The NUnit driver need not itself be published with NativeAOT: it launches/asserts closed generated test witnesses or the actual AOT runtime/editor/content-tool artifacts. A JIT test driver does not count as AOT engine execution, and ordinary JIT unit tests cannot substitute for those published-artifact lanes.

The build scripts use Microsoft's official PowerShell installer on Windows and Bash installer on Linux to acquire the SDK from the repository's `global.json` when an architecture is requested or CI is selected. Signing and publishing jobs also use that file through `actions/setup-dotnet`; SDK version/channel choices are not duplicated in scripts or workflows. Install into `artifacts\dotnet` and select it through the script's process environment without changing `global.json`, system installations or existing local SDK versions. Build outputs remain under ignored `artifacts`; no workloads are required for this desktop engine.

Developer builds use the SDK's runtime-pack defaults. Official self-contained/NativeAOT qualification can separately pin `RuntimeFrameworkVersion`, independently of SDK selection, and record servicing updates with tests/replay profile revisions. `PublishAot = true` is the baseline for player, editor and executable content tools under [26](26-csharp-systems-plugins-and-hot-reload.md)'s closed registries; set it in each executable project and do not propagate it to build-only analyzer/generator references, which retain their Roslyn-compatible target. Optional JIT comparisons are separately named artifacts, not a feature-complete shipping alternative; installing an SDK locally does not isolate every native/tool dependency.

### CPU and compilation profiles

Production `net11.0` projects retain the repository's module-wide `SkipLocalsInit` default. The source targets generate the module attribute, with a project opt-out through `SkipLocalsInit = false`; compiler-hosted generators opt out and test projects do not inherit the source default. Implementations must initialize every value they read or expose; suppressed implicit local initialization is not permission to observe uninitialized memory.

Prefer bounded `InlineArray`-backed storage over `stackalloc` for temporary spans, using shared internal fixed-capacity helpers where practical. For variable-size unmanaged spans, use the shared inline-or-rented owner: keep up to approximately 1024 bytes inline (`count <= 1024 / Unsafe.SizeOf<T>()`), rent larger spans from `ArrayPool<T>`, and dispose the owner on every exit. `Create` clears the exposed range; `CreateUninitialized` is only for callers that overwrite every element before reading it. Keep spans within the owner's lifetime and preserve explicit initialization under `SkipLocalsInit`; use `stackalloc` only where no suitable inline helper fits the shape.

Both required x64 targets require x86-64-v2: the existing x86-64 baseline plus CX16/POPCNT/SSE3/SSSE3/SSE4.1/SSE4.2. A v1-only central processing unit (CPU) cannot run.NET 11; packaging records that incompatibility and accepts the runtime/apphost's explicit baseline diagnostic rather than promising managed code can execute first. NativeAOT artifacts target the admitted v2 baseline directly; there is no shipping JIT fallback. For optional JIT comparison artifacts, Windows/Linux framework `ReadyToRun` targets v3 but supported v2 hardware can JIT fallback. That comparison detail is not an unconditional engine AVX2 requirement and its startup cost is measured separately.

`NativeAOT` uses the portable .NET 11 v2 baseline and records the actual compiler/instruction-set architecture (ISA) settings. Never use an unrecorded build-machine/native instruction-set setting. Optional wider-vector paths check the applicable `IsSupported` feature and retain tested scalar/reference behavior; an explicitly specialized artifact must declare its stronger CPU requirement before distribution.

Runtime Async does not change the public `Task`/`ValueTask` signatures in [Chapter 02](02-dotnet-api-memory-and-ownership.md). Baseline authoring does not require an engine-wide Runtime Async compiler flag; the .NET 11 base class library (BCL) already uses Runtime Async. An optional qualification build enables `Features=runtime-async=on` for engine sources and compares behavior, cancellation/lease lifetimes, diagnostics, size, and workload performance; it is not a new API or scheduling profile. Unsafe Evolution is independently optional: a separate compile/analyzer lane uses `LangVersion=preview` and `Features=updated-memory-safety-rules` to check caller propagation/generated interop. No preview-only syntax or flag enters required baseline sources, and such a lane cannot justify weakening bounds/lifetime proofs.

.NET 10 SDK 10.0.401/runtime 10.0.12 may be retained for matched portable benchmark harnesses only; the net11.0 engine is not claimed to compile/run on.NET 10. CI compiles the same portable workload against each target with recorded content/selection and compares latency/throughput/allocations, not strict hash equality across different runtime profiles.

## 3. Dependencies and provenance

Runtime graphics processing unit (GPU) bindings reuse `TerraFX.Interop.Windows` 10.0.26100.6 and `TerraFX.Interop.Vulkan` 1.4.328.1 (versions verified on `NuGet`); binding header version does not require Vulkan 1.4 device. Engine backend validates actual 1.3 features/extension functions; missing entry point is explicit failed capability.

Build DirectX Shader Compiler (DXC) release v1.8.2505.1/binaries 1.8.2505.32; Windows SDK 10.0.26100.0, native desktop tools only for private adapters/AOT; Vulkan headers/loader compatible 1.3 + and pinned validation SDK in developer manifest. Player ships native compiler only if explicitly enabled development feature, not needed core runtime.

Linux platform libraries: Wayland client/protocols/xkbcommon/`libX11`/XInput2/udev/`PipeWire`; image libpng/libjpeg-turbo, fonts HarfBuzz/FreeType/ICU; Vorbis decoder libvorbis. Windows operating system (OS) native WIC/Windows Audio Session API (WASAPI)/`GameInput`/`RawInput`/input method editor (IME) and shipped reviewed font/Vorbis dependencies where not OS supplied. Baseline native dependencies are distro/system-application binary interface (ABI)-admitted on Ubuntu 24.04; exact resolved versions/digests in build/runtime manifest, not fictitious identical Windows/Linux version promises.

For shipped native builds select the current supported upstream security release when implementing, pin exact source revision/artifact hash per RID before first build, generate reviewed ABI bindings where existing low-level binding unavailable. This is mechanical dependency resolution, not permission to substitute another rendering/audio/physics engine. Native flags/architecture/ABI/export table and license/SBOM required; managed package versions centrally selected, feeds HTTPS allowlist, no downloading a DLL by guessed filename at runtime.

Select the supplied foundation's NUnit/adapter/test-runner versions centrally after .NET 11 compatibility qualification; no guessed package pin is binding now. `BenchmarkDotNet` remains tools-only, uses the version selected in `Directory.Packages.props`, and requires qualification. [36](36-primary-sources-and-rationale.md) records provenance. `NativeAOT` builds on each target OS with required C++/clang/zlib tooling, not assumed cross-OS publishing. An optional logging adapter uses an explicitly pinned servicing-aligned `Microsoft.Extensions.Logging.Abstractions`, not a floating package or mandatory dependency-injection host.

## 4. Commands and build stages

### Feature roots and published assets

Trimming works from reachable code/metadata, not from the number of projects or packages. Executables select their closed trusted module/codec/system/accessor registrations and host/backend features before publication under [26](26-csharp-systems-plugins-and-hot-reload.md). The selected graph retains every required factory/generic instantiation; neither package reference nor namespace co-location registers all available services. Reject missing or inconsistent selections at build/admission without a runtime discovery fallback.

Unselected editor, content-tool, development shader-compiler and native backend registrations are not roots in a player. Headless/offline profiles do not open native windows/GPU/audio endpoints or initialize UI shaping merely because Runtime contains those namespaces. Module initializers/static initialization may not eagerly activate optional services. The publish manifest includes exactly the selected private native assets and their dependency/license closure, plus explicitly declared common assets; NuGet restore contents and publish contents are distinct. A selected native library cannot be omitted merely because the trimmer does not model its dynamic loading. Source dependency pins and restore/SBOM provenance are still recorded even where a downloaded asset is not shipped.

Qualification inspects the actual `.nupkg` asset groups/dependency manifests and published AOT code/metadata/native inventory for a minimal standalone consumer, headless Runtime and each selected backend profile. M03's handwritten component-provider witness is a real external public-only consumer, not a friend or generated wrapper: it compiles with warnings-as-errors and runs actual binding/read/default/reference/restore/write paths in ordinary and NativeAOT lanes on the required Windows/Linux x64 headless targets. Generated-provider results remain a separately selected variant against the same independent inputs and oracle. Record actual size/roots and retained dependencies; make no unmeasured size/speed win or universal cross-assembly inlining promise. A runtime feature choice that does not alter the build's roots may change activation but does not certify removal of that feature's bytes.

Use standard `dotnet restore`, `build`, `test`, `pack`, `run` and `publish` commands, with build behavior in MSBuild targets and dedicated tools invoked by those targets. Keep `scripts/build.ps1` and `scripts/build.sh` close to the supplied foundation: acquire/select the SDK, forward arguments, run selected commands in order and propagate failures. CI wrappers reproduce that sequence. Retain logs/results from those commands; a qualification report may consume their outputs, but must not replace ordinary developer build/test with its own project discovery or per-project orchestration.

Use the selected runner's documented category/filter/test-list options; verify a nonempty selected/executed set for implemented cases, process exit and current-run result files. [35](35-implementation-roadmap.md) distinguishes known empty scaffold suites from unexpectedly empty implemented selections. Preserve logs; stale results or process presence are not completion. Run focused Debug validation before Release; package/publish/native matrices execute when the affected implementation or milestone requires them, not as prerequisites of every managed edit.

> **Informative example:** Conventional VSTest commands are `dotnet restore .\TerraFX.slnx`, `dotnet build .\TerraFX.slnx -c Debug --no-restore` and NUnit through `dotnet test .\TerraFX.slnx -c Debug --no-build`, using the repository's inherited TRX logger. Repeat the applicable checks in Release after Debug. Once the player exists, its AOT command is `dotnet publish .\samples\TerraFX.Player\TerraFX.Player.csproj -c Release -r win-x64 --self-contained true`; the executable project sets `PublishAot=true` locally so the property does not flow to build-only project references. An NUnit/MTP foundation uses its verified equivalent, not these VSTest switches. These commands have not executed against an engine.

Content cooking is an incremental MSBuild target that invokes `TerraFX.Content.Tools` for the source DAG and builds both DirectX Intermediate Language (DXIL) and SPIR-V. Content-owning projects integrate it with build/debug-run and publish/deploy, with declared inputs/outputs, an explicit skip property and explicit target invocation. Schema/codegen/reflection validation precedes packaging. The package includes the player/native libraries, cooked assets, and configuration, with no editor/debug-only code by default; its manifest records SHA256, software bill of materials (SBOM), licenses, and engine/data/coordinate profile `LeftHandedYUpForwardZ1`. Recipe/shader caches and runtime save/content admission reject a prior right-handed (RH) profile identity instead of reusing/relabeling its bytes. Runtime checks all required native functions/features at startup and emits actionable missing-dependency diagnostics. The content/import contract is defined in [Chapter 11](11-assets-import-streaming-and-packages.md).

## 5. CI/deployment matrix

Required NativeAOT artifacts: Windows 11 D3D12 interactive/offscreen, Windows 11 Vulkan interactive/offscreen, Ubuntu 24.04 Vulkan Wayland and X11; headless simulation and native 2D/3D samples on both targets; editor and executable content-tool workflows on both. Generated registries, generic callables and bounded reflection metadata must qualify without dynamic-code warnings or suppressed missing instantiations. Optional JIT comparisons cannot stand in for these executions. Debug validation and Release performance are independent, using both backends' same scene assets.

Portable archive installer-free player default; self-contained runtime and private native library search restricted to application directory then explicit OS allowlist, no cwd DLL hijacking. Source/cooked content licenses separate from engine licenses. Signed releases/updater are application distribution policy, not a core engine network service. Engine API/data version and migration compatibility recorded before publishing.

The GA release gate also checks the CPU/SDK/runtime/compiler pins, ordinary `bool`/enum/domain-response APIs, exceptional `Task` faults, baseline plus optional compiler-lane compatibility, stable source-generated serialization discriminators, crash-report policy and numerical profile isolation. RC go-live support and successful.NET 10 comparison workloads do not certify a.NET 11 GA engine artifact.
