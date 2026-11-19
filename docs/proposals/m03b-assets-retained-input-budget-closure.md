# M03-B Assets retained-input and fault budget closure

**Status: tentative blocker disposition under the user's proposal policy. A user-visible ownership/error-contract decision is required; no complete funded Assets profile is established. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision

**There is no code-only closure that both covers the complete retained graph and preserves acceptance of arbitrary current registrations/tokens plus original-exception delivery.** Select no exemption for supposedly “borrowed” codec, cancellation, or exception graphs without an independently funded owner whose obligation covers their actual retained lifetime. Keep the affected qualified lane fail-closed.

The smallest next decision concerns **ownership and observable refusal/error semantics**, not another quota, byte constant, file port, or upward funding callback. A narrower closed-input lane can retain today's signatures, but its restrictions require an explicit contract decision and its original-fault envelope still needs proof. Preserving arbitrary inputs instead requires a concrete external ownership/funding contract or an ownership-bearing capability; none is supplied by the existing API. Section 6 identifies these decisions independently.

This closes the retained-input assessment left by the [file-I/O implementation proposal](m03b-assets-file-io-port-implementation-closure.md#what-repository-evidence-does-not-bound). It does not repeat or alter that proposal's worker/acquisition/read/release selection. Its missing worker, hash-provider, shared-root and actual-return charges remain separate. The [bootstrap](m03b-assets-prelease-bootstrap-and-close-closure.md), [pre-lease owner](m03b-assets-prelease-recovery-owner.md), [capacity addendum](m03b-assets-prelease-operation-profile-capacity-addendum.md), and [release rectification](m03b-scene-producer-return-and-assets-release-rectification.md#assets-owned-release-rectification) retain their scopes.

This is not a claim that today's public wire-byte budget promises a bound on all application memory. [30 section 6](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) expressly excludes codec-owned managed allocations. The unfulfilled requirement is the proposed **complete, funded retained-control/input/fault envelope**; that exclusion does not identify a lifetime-paying owner for it.

## 1. Verified ownership edges

Paths below are beneath `sources\TerraFX.Assets` unless otherwise stated.

| Current source | Relevant fact |
|---|---|
| [`AssetStoreRegistrationBuilder`](../../sources/TerraFX.Assets/AssetStoreRegistrationBuilder.cs), [`AssetStoreRegistration`](../../sources/TerraFX.Assets/AssetStoreRegistration.cs) | Both codec interfaces accept caller-supplied objects. Each adapter holds its exact `_codec`; the immutable registration retains adapters in two maps. Admission checks schema/type uniqueness and exact compiled NativeAsset tuples, not implementation state, retained bytes, an external grant, or an end-of-borrow protocol. The contextual adapter also retains its opaque `ProfileOrigin`. |
| [`AssetStoreOptions`](../../sources/TerraFX.Assets/AssetStoreOptions.cs), [`AssetPackageBacking`](../../sources/TerraFX.Assets/AssetPackageBacking.cs) | Options copy the 32-byte digest but retain the supplied registration unchanged. Every backing retains its whole options object; `Registration` returns `Options.Registration`. Consequently even unused codec objects remain reachable while that backing remains reachable. |
| [`AssetOwnedReadDescriptor`](../../sources/TerraFX.Assets/AssetOwnedReadDescriptor.cs), [`AssetCodecRegistrationReceipt`](../../sources/TerraFX.Assets/AssetCodecRegistrationReceipt.cs) | A descriptor takes no package-reference/lease credit, but contains strong references to Store, Registration and Schemas. Its receipt retains the exact adapter/origin. “Non-owning” with respect to package credits does not mean non-retaining with respect to managed graphs. |
| [`AssetLeaseLifetime`](../../sources/TerraFX.Assets/AssetLeaseLifetime.cs), [`AssetPackageLease`](../../sources/TerraFX.Assets/AssetPackageLease.cs) | Leases retain the exact backing, not merely its entries. Replacement and Store disposal therefore do not end registration retention. Failed release retains further state. An independently retained descriptor can also prolong registration retention without pinning the old file. |
| [`AssetStore.PackageLease`](../../sources/TerraFX.Assets/AssetStore.PackageLease.cs), [`AssetCodecContext`](../../sources/TerraFX.Assets/AssetCodecContext.cs) | Loading carries the token through reads, decoding and reference validation. The contextual codec receives a managed context referencing the exact schema registry and limits. Only the source span is `scoped`; that does not prevent a codec retaining its context or other managed state. |
| [`AssetPackageBacking`](../../sources/TerraFX.Assets/AssetPackageBacking.cs), [`AssetLease`](../../sources/TerraFX.Assets/AssetLease.cs), [`AssetLeaseLifetime`](../../sources/TerraFX.Assets/AssetLeaseLifetime.cs), [`AssetPackageLease`](../../sources/TerraFX.Assets/AssetPackageLease.cs) | Release-failure fields retain original exceptions. A failed backing additionally enters `s_failedReleaseRoot`. Repeated failed release uses `ExceptionDispatchInfo.Capture(...).Throw()` rather than ending that exception's lifetime. |
| [`AssetStore`](../../sources/TerraFX.Assets/AssetStore.cs) | Replacement retains `_pendingReleaseFailure`; disposal caches a fault through its retained `_disposeCompletion`. A terminal or observed Task does not detach these references. Disposal reports its selected first failure; that is not a complete primary-plus-independent-cleanup delivery definition. |
| [`AssetPackageReader`](../../sources/TerraFX.Assets/AssetPackageReader.cs) | Zero CPU budget is now rejected before opening. The earlier proposals' zero-budget-success example is no longer current evidence. Positive-budget opening still retains the supplied options/registration, and its cleanup catch can replace a primary exception if disposal throws. |

[02 section 3](../design/02-dotnet-api-memory-and-ownership.md#3-leases-and-asynchronous-operations) requires actual-use retention, primary-preserving cleanup, and a preinstalled recovery owner. Its link to Chapter 11's exact “M03 4.7” recovery-delivery contract still has no corresponding section in [live Chapter 11](../design/11-assets-import-streaming-and-packages.md). [04 section 4](../design/04-capacity-and-workload-profiles.md#4-content-package-and-asset-loading-limits) bounds package/wire/decode/lease quantities, not these graphs. [29](../design/29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag) permits only Assets-to-Core dependencies and preserves standalone callers; a Scene/World/Host callback cannot supply the missing ownership.

## 2. Registration codecs and compiled metadata

### What can be bounded without inventing an owner

The Core registry's **data representation** is materially different from arbitrary codec state. [`CompiledContentSchemaRegistry`](../../sources/TerraFX.Core/CompiledContentSchemaRegistry.cs) contains arrays of sealed `DataSchema` and `ContentProfileDescriptor` objects. Their inspected closure comprises field/default descriptors, identity-name strings, primitive payload/numerator arrays and schema-reference arrays. These have inspectable typed lengths and contents. A selected implementation can derive conservative storage charges from the actual graph and qualified layouts, preserving the original registry identity. Package entry count is not its membership bound.

That is a route to a finite **per-instance derivation**, not an existing numerical profile or grant. Include collection storage, actual capacities, strings, defaults, shared metadata and construction/return overlap. If shared metadata is excluded from a Store charge, name its independent payer and lifetime; a static catalogue is an independent root, not automatically independent funding.

The same reasoning does not extend through `_codec`. Neither codec interface limits object fields, delegates, native owners, caches or graph mutation. Immutability of registration membership is not deep immutability of its elements. Copying an adapter/map or the reference to a codec retains the same opaque graph. Deep-copying arbitrary codecs is neither specified nor generally possible without changing their state/identity behavior.

### Distinguishing case

Consider two valid registrations with identical schemas, codec types, package bytes and options, with `B0 > 0`. One accepted codec holds a small application cache; the other holds an application graph larger than `B0`. It may be unused: opening a valid empty package still stores the full registration in its backing. Drop the caller's builder/options/registration references. The backing's strong path remains:

```text
Store or retained lease -> backing -> options -> registration
    -> map -> adapter -> exact caller codec -> application graph
```

This is a source-derived counterexample, not an allocation experiment. Every actual graph is finite; its size is **unbounded by the admitted package, wire, decode and lease quantities**. The caller could also grow the cache after admission. The builder's having allocated the adapter before `CreateAsync` does not fund its indefinite retention.

The [closed built-in Scene proposal](m03b-scene-built-in-codec-profile-admission.md) removes arbitrary **selected codec bodies** through factory receipts. It does not detach unused codecs in the same registration. Current descriptors expressly retain that registration. Requiring every retained codec/origin to have a closed graph profile would be a different, whole-registration admission rule; it would amend that proposal's explicit allowance for unrelated unused registrations. Do not silently substitute it for per-read provenance.

For general public loading, discarding unused codecs is not equivalent: a later `LoadAsync<T>` may legally request one. Weak registration/codec references would make that load depend on caller rooting and GC, introducing a new lifetime requirement. Reconstructing a smaller `AssetCodecContext.Schemas` can also change what a supplied contextual codec observes. A read receipt proves identity, not ownership or storage bounds.

### Caller ownership requires a real obligation

An external registration owner could discharge these costs only with evidence of all of:

- its original, independently funded grant for the exact registration/codec graph and any permitted growth;
- a lifetime spanning every referencing backing, old lease, descriptor, invocation and retained failure, including failed creation and late release after Store disposal;
- continued codec usability where the implementation owns disposable/native state, and an exact end/transfer rule.

No such grant or obligation appears in `Build`, `AssetStoreOptions`, the codec interfaces or the inspected consumers' Assets boundary. Registration construction, GC reachability, a caller's temporary local, and “the application already paid for it” are not substitutes. Store disposal is not a usable end-of-borrow notification for this contract.

## 3. Caller tokens, callback registrations and context

These are separate ownership edges:

| Edge | Required classification |
|---|---|
| Caller `CancellationToken` value | Its small struct size does not bound its source. The pinned runtime's [token][ct] holds a source reference; [`CancellationTokenSource`][cts] is extensible and retains callback/state/context machinery. A valid derived source can itself retain an arbitrary application graph, even without any Assets-created registration. |
| Caller-created callbacks/linked sources | Retaining the source can retain unrelated callback state and captured context. The caller may add registrations while the Assets operation is pending. Assets neither owns nor may dispose those registrations/source to enforce its own budget. A pre-admission measurement cannot constrain later growth. |
| Assets/lower-created registration | Its node, delegate/state, selected context and registration/consumption/return machinery require the original operation's provision. [`CancellationTokenRegistration`][ctr] retains its node, which reaches the source. Disposal does not mutate every copied registration struct into `default`; clear ended engine aliases after the real callback/registration invocations return. Unregistering is not universally a callback-return join. |
| Cancellation outcome | [`OperationCanceledException`][oce] stores the token, and [Task cancellation state][task] can store it and an exception dispatch. Clearing an operation's token field does not clear the same source retained by its outcome. Replacing the token with `default` or an unrelated linked token changes observable cancellation identity. |

The selected file port's polling/no-registration policy removes **that port's callback-registration edge only**. It does not remove the token's source edge, higher registrations, canceled-Task storage or captured caller context. `UnsafeRegister` avoids one `ExecutionContext` capture but still retains callback/state/source relationships. `ConfigureAwait(false)` is not execution-context suppression. This proposal authorizes no change to public context-flow semantics.

A noncancelable/default token removes its direct source edge and is a useful closed case. Pre-cancellation acquires no asset work under Chapter 02, but does not demonstrate the admitted cancellation path. Restricting the qualified lane to noncancelable or specifically owner-funded tokens is observable admission behavior, not an implementation-equivalent replacement for accepting arbitrary tokens.

An external token/context owner must fund its actual source/callback/context graph through the last consuming/publishing/returning reference and any retained cancellation outcome. Caller ownership of the source's disposal authority alone proves no such grant. Creating a linked source, Boolean cancellation proxy, or another callback merely relocates the obligation; it either keeps the original graph or changes cancellation/identity semantics.

## 4. Primary failure and cleanup failure are independent

### Primary execution failure

A permitted codec can throw an original exception containing arbitrary subclass fields, an inner-exception chain or `Data` values. [Chapter 02's original-exception requirement](../design/02-dotnet-api-memory-and-ownership.md#async-errors-and-admission) means a reference/EDI/Task relay retains those objects, not a bounded summary.

When cleanup succeeds, the public fault may eventually become entirely caller-owned: that requires the real implementation to end every Assets alias/use of the exception, dispatch, context and returned Task. This is a legitimate output boundary **when proved**, not permission to treat the unbounded pre-transfer capture/cleanup/publication peak as free. A caller merely receiving a Task is not acknowledgment that every Assets fault alias has ended.

### Cleanup failure and persistent debt

Cleanup can fail independently of the primary outcome, or be the only failure. Bounded resource membership can bound the number of cleanup slots; it does not bound any original exception graph in a slot. Derive sample membership from the actual cleanup closure rather than inventing a universal “two exception” allowance.

The current failure fields, failed-backing root and cached disposal promise make indefinite engine-side retention a real possibility. Primary-preserving replacement cleanup and first-failure disposal selection are useful behavior, but neither establishes complete capture/delivery ownership. The reader and load cleanup paths also still need the earlier proposals' primary-preservation closure; do not describe the missing combined contract as implemented.

Even a known BCL exception type is insufficient once exposed: [`Exception.Data`][exception] is mutable and arbitrary exceptions may override it. After observing the exact exception, a caller can attach another large graph while Assets still retains the original for failed cleanup or repeated disposal. A bound on the exception at initial construction is not a stable bound on this retained graph.

This latter schedule is conditional on a genuinely reached cleanup failure; it is not evidence that a valid native close failure was observed. The generic codec-primary counterexample needs no native failure.

### Delivery machinery is an additional charge

The pinned [Task exception holder][task-fault] stores a list of EDIs and captures an `Exception` input. [`ExceptionDispatchInfo`][edi] retains the original exception plus dispatch state. The inspected [CoreCLR implementation][dispatch] obtains a frozen stack representation and retains other dispatch data. This does **not** mean every capture deep-copies the application's graph; sharing still retains that graph, and actual stack/capture capacities and simultaneous wrappers need their own profile.

Include faulted/canceled Task state, holder/list/EDI storage, native capture work, rethrow/observation overlap and the actual delivery-return suffix. Idempotent resource close does not make repeated exception observation allocation-free or bound its stack state. Raw native count/error/disposition facts can avoid unnecessary lower exception construction, but the eventual public Task/error mapping still has these obligations.

No existing mechanism solves this by itself:

- `AggregateException` adds a wrapper while retaining originals; it neither bounds their graphs nor supplies the missing ordering/identity contract.
- `Exception.Data`, weak references, finalization or a dropped Task are not durable funded outcome storage.
- Stringifying/truncating/replacing an original exception changes the promised outcome and can itself invoke arbitrary code/allocate.
- Retaining an original on an uncharged “diagnostic” side channel leaves exactly the same ownership problem.

On cancellation with successful cleanup, preserve the established cancellation/token outcome. A cleanup failure must fault rather than be hidden as successful cancellation. The exact primary/cleanup representation, ordering, original identities, repeated-close behavior and retained-state authority still require Chapter 11's missing owner definition; no new exception family is selected here.

## 5. Why another finite reservation cannot close the gap

For the already proposed original Store envelope, `B0` remains the original `CpuAssetBudgetBytes`; replacement supplies no new purse. This assessment concerns disjoint retained input/fault charges within that envelope, not a second budget:

```text
RelevantRetained(t) =
    union of still-retained registration, codec/origin, token/context,
    primary/cleanup exception and delivery graphs

InputAndFaultDebit(t) =
    qualified charge for that union not already covered by
    a proved, live, independent paying grant
```

This is an ownership definition, not an instruction to traverse arbitrary objects at runtime. Shared physical storage is counted once under its actual paying owner; repeated references still require their own carrier storage. A grant cannot end when the first Store closes while another Store/generation/descriptor/fault still depends on it.

The codec example changes only an opaque codec's state; the token example changes only an opaque source's state; the primary-error example changes only the thrown object's graph. Each can exceed a fixed proposed input/fault allowance without changing the existing admitted quantities. Mutation can invalidate an initially observed size. Arbitrary graph inspection would require unsupported reflection/discovery or user code and still would not enforce future ownership.

Consequently, a fixed number of roots, a schema/type match, a receipt, a whole-`B0` reservation, and the absence of another allocation in Assets do not prove this term fits. Refusing every unknown graph is safe refusal, not an implemented useful funded lane. Resolving any one of the three counterexamples leaves the others intact.

## 6. Required public-contract decision

**Recommended disposition: preserve existing public behavior outside the qualified lane; retain the strict owner-proof requirement and keep the qualified lane closed.** Prefer a closed whole-graph profile over adding callbacks or a generic new quota, but do not claim that such a complete profile exists today.

The owners must resolve each row before enabling its affected lane:

| Subject | Decision required |
|---|---|
| Registration | Authorize whole-retained-registration admission/refusal, including unused codecs/origins, with finite closed profiles and the existing Store grant; **or** name an actual independent external owner/grant covering arbitrary registered graphs through all retained lifetimes. The former changes the per-read proposal's compatibility rule; the latter is absent from the present API. |
| Caller cancellation/context | Define which exact token/context ownership cases the qualified lane admits and how their grants survive actual use, original-token delivery and failure retention; **or** approve a new ownership-bearing cancellation boundary. Accepting arbitrary tokens while charging only their structs is not an option. |
| Primary and cleanup faults | Supply a funded owner for the complete original mutable graphs and actual capture/publication envelope, including post-delivery aliases; **or** explicitly revise original-exception/cleanup-sample/repeated-close delivery semantics to a bounded representation. Bounded raw facts alone are not the currently promised original exceptions. |

A signature-preserving contract amendment is possible only when its real owner obligations and distinguishing refusals are defined. A blanket “caller-owned inputs/errors are excluded” paragraph is not sufficient. In particular, assigning retention only until the public Task or Store disposal completes contradicts old-lease and failed-cleanup lifetimes. Without an existing owner able to accept those obligations, an ownership-bearing API decision or narrower accepted behavior is necessary.

No new public declaration is proposed. Chapters 02/11 must own the complete failure/cancellation/retention semantics; Chapters 04/30 must state budget scope and observable admission changes; Chapter 29's DAG remains unchanged. Tentative proposal approval selects this fail-closed disposition and permits independent prerequisite work, not an unproved grant or milestone acceptance.

## 7. Implementable work and required evidence

This request authorizes only this document. For later implementation assignments:

| Work | Disposition |
|---|---|
| Derive known immutable metadata/map/adapter/carrier storage; retain only the configuration fields actually needed; end dead token/registration/context aliases at proved last use/return | Behavior-preserving prerequisite work can proceed. Preserve exact registry/context semantics, digest/package identity and late-release ownership. These reductions do not bound opaque children. |
| Catalogue actual original-fault/cleanup/delivery roots and raw lower error facts; distinguish successfully transferred public outcomes from engine-cached outcomes | Can proceed without a new quota/API. Do not remove required originals or refund while aliases survive. Numerical activation still needs the actual runtime/profile closure. |
| Whole-registration restrictions, narrowed token acceptance, external-owner exemptions, changed fault delivery | Require the row-specific contract decision above. No silent public fallback, graph-size callback, Runtime funding callback, deep codec clone or new process quota. |
| Complete package/pre-lease profile, Scene preparation, nonempty restore and Scene-dependent M05 consumers | Remain fail-closed until these and their separately recorded producer/worker/return/receiver gates close. Do not clear the footprint guard, enable success-only admission, or quarantine normal successes indefinitely to conceal a missing bound. Independent dependency-ready M03-B/M05 work may continue. |

### Existing tests: scope of evidence, not results from this assessment

- `AssetStoreOptionsCopyDigestAndRequireAbsolutePackagePath` checks digest copying and exact registration sharing.
- `PackageAdmissionRetainsValidatedEmptyBacking` and `AssetPackageLeaseDescribesProfiledReadWithoutAcquiringPackageLease` assert exact registration/schema references. Receipt identity is not graph ownership.
- `PackageLeaseRetainsExactBackingAcrossReplacementAndStoreDisposal` and `AssetStoreRetainsDecodedValueAndPackageBackingUntilFinalHandleDisposal` witness the intended late lifetime, not a retained-input byte grant.
- `AssetStoreHonorsCancellationWithoutLeakingLoadReservations` uses pre-cancellation; it does not establish admitted source/registration/context retention or original-token fault delivery.
- `AssetPackageBackingFailedFinalStreamReleaseRootsBackingForProcessLifetime` uses a throwing stream subclass. It is managed-root evidence only, not a valid native failure, finite graph bound or independent grant.

### Exact missing checks after the decisions

Use the existing Assets registration, reader, Store and package-lease fixtures and real packages. A legitimate application codec/source is an input-contract witness, not a test-only grant issuer or native-failure substitute.

| Check | Required distinguishing evidence |
|---|---|
| **R1 — full registration retention/admission** | Same valid schemas/package/budgets, different caller codec graphs; include an unused codec alongside an actual factory-profiled selected codec. Establish the exact new refusal or genuine external grant **before package acquisition**. Retain an old lease/descriptor across replacement and Store disposal; its graph grant must not disappear. No large/OOM allocation experiment is needed to prove the structural counterexample. |
| **R2 — sharing and known metadata** | Exact actual registry/default/string/array/map membership and capacities; shared registration across Stores/generations; preserve codec/context identity and later typed loads where the contract still admits them. Match each excluded shared graph to a payer surviving every dependent reference. |
| **T1 — caller source versus engine registration** | Default token, an ordinary caller source and a valid derived source retaining state; caller callbacks with state/context, including growth during a pending real operation. Verify selected admission/owner behavior, no disposal of caller-owned sources/siblings, and exact cancellation-token identity. Do not describe the polling port as detaching these graphs. |
| **T2 — actual alias/return end** | For registrations actually present in the selected implementation, cover unregister/dispose versus running callback and registration return, then prove clearing of all engine token/registration/context aliases. Separately retain the canceled public Task and identify its owner. Do not add a port callback merely to manufacture this test. |
| **F1 — primary original and transfer** | A real publicly registered codec throws a preconstructed exception with subclass/inner/Data state. With successful cleanup, verify original identity and stack semantics, the pre-transfer charge and actual end/transfer of every engine alias. This tests the general public contract, not qualification of an otherwise refused custom codec. |
| **F2 — cleanup and observation** | On a supported genuinely reached cleanup-failure path, verify independent primary/cleanup identities and ordering, cancellation-plus-cleanup faulting, persistent original debt, repeated observation without another cleanup attempt, and mutation of a delivered original's `Data` while an engine alias survives. The selected owner must cover that lifetime/growth, or the explicitly revised delivery contract must prevent the edge. |
| **A1 — useful complete fit** | Final disjoint input/fault/capture/control profile, exact-fit and one-byte-short preclaims, old/current/candidate/failure coexistence, and one return only after actual end/accepted transfer. Known-layout inspection plus complete allocation/native-capture evidence must establish a useful fit; warm GC totals, exception counts and a successful small input do not. |

Where F2 or a required return/capture observation lacks a safe real stimulus, keep that specific [Chapter 35](../design/35-implementation-roadmap.md#4-completion-gates) observation blocked. Retry only when the missing capability/stimulus changes; do not corrupt handles/counts, inject allocation failures or reuse the throwing-stream test as physical qualification. Original-fault retention and native release disposition remain different observations.

Future implementation validation uses ordinary owning-project Debug then Release commands from [Building](../project/Building.md), with fresh nonzero selected/executed NUnit/VSTest membership and TRX evidence. Managed semantics, runtime allocation/capture bounds, native/AOT/public consumers and milestone acceptance remain separate.

**No build, test, allocation probe, native experiment, commit or remote mutation was performed. Only this proposal is authored; source, tests, designs, roadmaps and previous proposals are unchanged by this task.**

## Review-input identity

Snapshot UTC `2026-11-19T12:25:33.0747111Z`, HEAD `39bc1b26a05af61b2c186e3285b19a6caceaffd4`. All tracked inputs below were clean at that snapshot. Concurrent dirty Core diagnostics/Runtime Scene tests and the unrelated M05 proposal are outside this review and were not edited by this task.

Each set digest is SHA-256 of ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines, LF-joined, UTF-8 without BOM/trailing newline. `ALL` is the union, excluding this output. S is a schema identity/preservation input, not a schema review or qualification.

```text
A: sources\TerraFX.Assets\
AssetCodecContext.cs, AssetCodecRegistrationReceipt.cs, AssetEnvelopeDecoder.cs,
AssetLease.cs, AssetLeaseLifetime.cs, AssetOwnedReadDescriptor.cs,
AssetPackageBacking.cs, AssetPackageLease.cs, AssetPackageReader.cs,
AssetStore.cs, AssetStore.PackageLease.cs, AssetStore.PackagePin.cs,
AssetStore.ReadDescriptor.cs, AssetStoreOptions.cs, AssetStoreRegistration.cs,
AssetStoreRegistrationBuilder.cs, IBinarySchemaCodec.cs,
IContextualBinarySchemaCodec.cs

K: sources\TerraFX.Core\
BinarySchemaDiagnostic.cs, CompiledContentSchemaRegistry.cs,
ContentFieldDefaultDescriptor.cs, ContentFieldDescriptor.cs,
ContentProfileDescriptor.cs, ContentSchemaIdentityAssignment.cs, DataSchema.cs

D: docs\design\
02-dotnet-api-memory-and-ownership.md, 04-capacity-and-workload-profiles.md,
11-assets-import-streaming-and-packages.md, 29-project-setup-build-and-deployment.md,
30-public-api-review-surface.md, 35-implementation-roadmap.md;
docs\project\Building.md, Roadmap.md;
docs\proposals\
m03b-assets-acquisition-failure-owner.md,
m03b-assets-prelease-recovery-owner.md,
m03b-assets-prelease-operation-profile-capacity-addendum.md,
m03b-assets-prelease-bootstrap-and-close-closure.md,
m03b-assets-file-handle-release-acknowledgment.md,
m03b-assets-file-io-port-implementation-closure.md,
m03b-scene-producer-return-and-assets-release-rectification.md,
m03b-scene-built-in-codec-profile-admission.md

T: tests\TerraFX.Assets.Tests\
AssetPackageReaderTests.cs, AssetStoreRegistrationTests.cs,
AssetStoreTests.cs, AssetPackageLeaseTests.cs

C: .github\copilot-instructions.md, .github\workflows\ci.yml, global.json,
Directory.Build.props, Directory.Build.targets,
sources\Directory.Build.props, sources\Directory.Build.targets,
sources\TerraFX.Assets\TerraFX.Assets.csproj,
sources\TerraFX.Core\TerraFX.Core.csproj,
tests\TerraFX.Assets.Tests\TerraFX.Assets.Tests.csproj

R: .dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\.version
S: sources\TerraFX.Assets\NativeAssetSchemaCatalogue.cs
```

| Set / input | Files | SHA-256 |
|---|---:|---|
| A | 18 | `ef0cb53fe549f8e00bb0e87c3d2ade5620ffabfb0b3b992febd766dcc33a6d50` |
| K | 7 | `a90f8c7ac3754929a9e40822ef9137ce9ee9a9c4a97842c385ba4d6c0c53f759` |
| D | 16 | `f9ffb0ff25c4611dfcfa1af091b227b8be589b509de681c1fc8eaba40c67f127` |
| T | 4 | `f78f06266043cd4f5471e2fe2b2b3c3aa044d318974a978ae9fa04ffb9cc0732` |
| C | 10 | `6b025e2d6e43ff1efe7093db34c24fe93d55ebb6038aee631b638dbd940524a1` |
| R | 1 | `aab4f62773986811741aea2bbe2f631c7fbaeeefd485eca380deba5622b1ba8e` |
| S | 1 | `056679e50cf3a12f77d0b54b948ade6181b0ef74e9e9e73c0efe3ef42bd33a7a` |
| ALL | 57 | `1247600e243911cc06c550bfd09124833390025461f9bd3e5a5114ec093bc3f1` |
| Native catalogue raw file | 1 | `6b9537ea8cefebb48b5dc24261a8c03fb059e3314e7da089f29a639d9e55620a` |

The local runtime identity file selects `11.0.0-rc.3.26522.148`, unified-source revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. The nine runtime links below were inspected at that exact revision. These are static source/identity observations, not loaded-testhost, native binary or allocation qualification. Recheck input hashes before disposition/implementation; changed inputs require scoped reassessment.

[ct]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationToken.cs#L35
[cts]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenSource.cs#L26
[ctr]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/CancellationTokenRegistration.cs#L18
[oce]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/OperationCanceledException.cs#L19
[task]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/Task.cs#L375
[task-fault]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Threading/Tasks/TaskExceptionHolder.cs#L143
[exception]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Exception.cs#L63
[edi]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/ExceptionServices/ExceptionDispatchInfo.cs#L18
[dispatch]: https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/coreclr/System.Private.CoreLib/src/System/Exception.CoreCLR.cs#L219
