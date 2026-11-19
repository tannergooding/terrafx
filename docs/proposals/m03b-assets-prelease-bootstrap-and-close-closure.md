# M03-B Assets pre-lease bootstrap and close closure

**Status: tentative rectification proposal; implementation NO-GO pending the authorization and lower-boundary gates below. No numerical opening profile or physical-close qualification. Keep `PreparedScene.QualifiedFootprint` null.**

## Decision

The package-opening interval **cannot currently be funded by an authorized existing Assets issuer and the existing limits**. Select one conditional remedy: extend the existing `AssetStore.CreateAsync` issuance boundary to a package-acquisition-free, unpublished Store, extend `AssetPackageBacking` into its opening carrier, and use the existing intrusive Assets root for acquisition and failed-release retention. Request an explicit package-control earmark within the original `CpuAssetBudgetBytes`, not another option or quota.

That earmark changes the budget's present decoded-wire-length contract. It requires approval before implementation; neither tentative approval of proposals nor unused numeric capacity supplies that authority. If changing this admission contract is unacceptable, there is no code-only solution within the stated constraints. Independently, a managed owner cannot repair an unreturned or already-invalidated lower handle.

The requested decision following the [acquisition-failure proposal](m03b-assets-acquisition-failure-owner.md) is therefore an **early existing-Store issuer, subject to explicit same-envelope authorization**, not a Scene purse, new public recovery owner, registration-funded pool, global quota, or generic cleanup service. Its [pre-lease operation proposal](m03b-assets-prelease-recovery-owner.md) and [capacity addendum](m03b-assets-prelease-operation-profile-capacity-addendum.md) retain their separate typed-load obligations.

## Verified current boundary

| Source/contract | Fact and consequence |
|---|---|
| [`AssetPackageReader.OpenAsync`](../../sources/TerraFX.Assets/AssetPackageReader.cs#L23) | Constructs the exact framework `FileStream`, read-only, buffer size 1, asynchronous/random-access, before its `try`. Hashing, profile/directory validation and entry construction precede backing construction. Its catch awaits disposal before rethrowing: a cleanup exception can replace the primary failure. |
| [`AssetStore.CreateAsync`](../../sources/TerraFX.Assets/AssetStore.cs#L41) | Awaits the reader before constructing a Store or entering its cleanup `try`. A failed open returns no Store/backing. Failed Store construction calls `ReleaseOwner`, which can also replace the construction exception. Moving the reader's `try` does not install an earlier owner. |
| [`ReplacePackageAsync`](../../sources/TerraFX.Assets/AssetStore.cs#L220), [`DisposeAsync`](../../sources/TerraFX.Assets/AssetStore.cs#L74) | One replacement body is elected. Its candidate variable is null until opening returns. Only an existing backing can enter the pending-release slot. The `finally` completes the replacement barrier successfully on every outcome; disposal joins that barrier. This is body completion, not a disposition for an unreturned candidate. |
| [`AssetPackageBacking`](../../sources/TerraFX.Assets/AssetPackageBacking.cs) | Final release retains its stream/reference while calling synchronous disposal. Caught failure records the exception and links the existing backing into `s_failedReleaseRoot`; repeated release reports failure rather than retrying. This supplies reachability after backing construction, neither earlier admission nor native-close acknowledgment. |
| [`AssetStoreOptions`](../../sources/TerraFX.Assets/AssetStoreOptions.cs), [`AcquireLoad`](../../sources/TerraFX.Assets/AssetStore.cs#L360), [Chapter 30 section 6](../design/30-public-api-review-surface.md#6-assets-schema-codecs-and-packages) | CPU bytes charge declared decoded wire lengths; decode bytes and caller-visible lease credits have their separate scopes. Codec-owned managed allocations are expressly unmeasured. Opening does not debit these counters. Options select limits; they are not a mutable funding account. `AssetStoreRegistration`/its builder contain schema/codec registrations, not an opening grant or durable cleanup issuer. |
| [Chapter 04 section 4](../design/04-capacity-and-workload-profiles.md#4-content-package-and-asset-loading-limits), [Chapter 29](../design/29-project-setup-build-and-deployment.md#1-repository-and-assembly-dag) | Package/directory ceilings bound format extents, not a complete allocation grant. Assets depends only on Core and supports CPU assets/audio/offline tools without World or Host. WorldGuid cells, Scene `P`, and Runtime callbacks cannot bootstrap this factory. |
| [Chapter 02 section 3](../design/02-dotnet-api-memory-and-ownership.md#helper-phases-and-retained-cleanup-ownership) | Requires admitted recovery ownership before external acquisition and primary-preserving cleanup, but its linked “11 M03 4.7” recovery definition is absent from live Chapter 11. Exact dual-failure delivery and retained-state authority remain missing, not implicitly approved. |

**Distinguishing source-derived example, not an execution:** a valid empty package can pass this opening path with all four configurable budgets set to zero: validation admits nonnegative options, there are no asset entries/loads, yet opening still constructs a stream, hashes, allocates metadata and constructs a Store. Thus neither a lease credit nor decoded-byte reservation presently funds bootstrap. Charging the proposed opening envelope would intentionally change this case to pre-acquisition refusal.

A second conditional schedule exposes the ownership hole: correctly checksummed malformed replacement → validation failure → suppose actual cleanup also fails → reader returns no backing → Store candidate remains null → replacement barrier completes → Store disposal can release the original package without a candidate disposition. This does not establish that a valid production close failure is reachable or that an OS handle leaked.

`dd4fd10f` is an ancestor of the inspected HEAD and implements the phased World restore owner, not standalone Assets bootstrap. The current [`PreparedScene` guard](../../sources/TerraFX.Runtime/Scenes/PreparedScene.cs#L85) remains unavailable.

## Lower closure: what the selected runtime does and does not establish

The local runtime identity file names `11.0.0-rc.3.26522.148`, unified-source revision `5254d8bcf59917edc1e1abc33bdf2442b77b1c70`. The following are static source observations, not binary/native qualification:

- [`FileStreamHelpers`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/FileStreamHelpers.cs#L22) does not add buffering for size 1 or a derived-stream wrapper for the exact `FileStream` type. [`OSFileStreamStrategy`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/IO/Strategies/OSFileStreamStrategy.cs#L111) disposes the thread-pool binding and then the SafeFileHandle in both close paths; its asynchronous path returns a completed ValueTask. Absence of a write-buffer flush is not proof that every remaining action is nonthrowing.
- The path constructor assigns the strategy only after its construction. [`SafeFileHandle.Open` on Windows](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Windows.cs#L119) acquires the file before asynchronous binding. The binding initializer catches `ArgumentException` and disposes an owned handle; that is not an all-exception unwind proof. The strategy's catch begins after `SafeFileHandle.Open` returns. The Unix opening path has its own catch/dispose. An early Assets carrier cannot capture a stream/handle that never returned; critical-finalizer fallback is not deterministic constructor unwind.
- [`SafeHandle.InternalRelease`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/System/Runtime/InteropServices/SafeHandle.cs#L189) commits its closed/disposed/reference state before invoking `ReleaseHandle` and discards that method's Boolean result. The [`Unix implementation`](https://github.com/dotnet/dotnet/blob/5254d8bcf59917edc1e1abc33bdf2442b77b1c70/src/runtime/src/libraries/System.Private.CoreLib/src/Microsoft/Win32/SafeHandles/SafeFileHandle.Unix.cs#L145) returns native close status. Consequently, normal managed disposal and `IsClosed` do not themselves expose a successful OS-close acknowledgment. This establishes information loss, **not** reachability of a retaining native failure on the valid admitted path.

| Observed boundary outcome | Permitted conclusion |
|---|---|
| Constructor throws without returning a handle/stream | No Assets transfer occurred. Require complete lower acquisition/unwind coverage, including initialization/allocation failures after acquisition; do not call this “nothing acquired.” |
| Nonfinal backing-reference release succeeds | Remaining real references own the package; no physical-close claim. |
| Final stream disposal returns normally | Managed disposal completed. Physical release requires the exact valid lower-path proof, including outstanding native SafeHandle references/binding work; it is not inferred from the return alone. |
| Disposal throws | Preserve the exact progress and surviving ownership. Without a lower disposition, do not infer that release was unattempted or that retry is safe. |
| Lower release is confirmed, later accounting/return fails | Record release before the later step; retain only the remaining debt and never close again. |

Do not retry uncertain disposal, wrap a cached raw handle in another SafeHandle, or add `DangerousAddRef` after the fact. Handle values can be recycled, and rooting a logically closed SafeHandle cannot reconstruct ownership. Splitting `File.OpenHandle` from `new FileStream(handle, ...)` merely moves the first-return boundary unless the opening API's own partial-acquisition closure is established.

The minimum lower gate is either a **complete valid physical-release and constructor-unwind proof** for the selected runtime/OS/filesystem/read path, or an authorized narrow lower boundary that installs acquisition into the preowned carrier and reports released versus still-owned with preserved ownership. An unknown disposition remains NO-GO. This proposal authorizes no private native replacement for the BCL and claims no such capability currently exists.

## Required same-envelope authorization

The proposed contract amendment must state all of the following before code:

1. **Original issuer:** permit `CreateAsync` to make a checked, package-acquisition-free scalar bootstrap preclaim before managed owner construction, then transfer it once into the unpublished Store. Managed constructor failure must have acquired no package file. The existing Assets process anchor must retain the carrier before the external file constructor is entered.
2. **Existing byte limit, changed scope:** use `B0 = original options.CpuAssetBudgetBytes` for existing wire-byte debits **plus** the complete opening/package-control ledger below. Keep this original upper envelope through replacement, Store close and failed factory construction. Candidate options are not a second purse; their existing payload limits still apply, but increasing a replacement limit cannot refinance old debt. This tighter admission behavior, including the zero-budget example, needs explicit compatibility approval. Lease/decode limits do not acquire new meanings.
3. **Cold and failed-root funding:** name the lifetime-paying grant for the shared intrusive anchor/gate, including cold initialization, and for every retained carrier/issuer graph. The required chain is `Assets process anchor -> unpublished backing -> exact Store issuer, opening progress, resources, outcomes and grants`. A static link is a root, not funding; a Store/carrier cycle is neither an independent root nor a grant. No implicit type initializer, cleanup allocation or later quota admission may fill this gap.
4. **Failed standalone creation:** no returned Store is required for the installed root to retain its original issuer, claims, primary outcome and remaining obligations. That issuer is closed to further acquisition; dropping the failed Task does not refund it. A new `CreateAsync` call is an independent Store envelope, not release or retry of the old debt. Existing limits impose no aggregate process bound on arbitrarily many independent creations; a requirement to bound that aggregate cannot be met without separately authorized authority/capacity, excluded here.
5. **Failure delivery:** define the missing Chapter 11 bounded primary/cleanup capture and public fault/cancellation semantics without changing Chapter 30 signatures. Reserve the actual exception/EDI/Task-delivery machinery, not just an array of sample references. Retaining an original exception can retain its entire object graph. No finite capture bound is established by naming a sample count.

These are requested authorizations, not statements that present options, registrations or static fields already issue these grants.

## Complete symbolic preclaim and retained-root ledger

Let `q` be the qualified charge for the actual chosen allocation/layout/capacity. No numerical `q` is supplied here; World-specific array charges are not automatically Assets authority. All terms below are disjoint ownership charges; shared storage is counted once only when its independent paying grant and lifetime are established.

| Term | Required contents |
|---|---|
| `Fixed` | Unpublished Store/issuer, cold gates and attributable shared-anchor provision, disposal promise, root/transfer links and their complete retained controls. |
| `Keep(g)` | One exact package generation's backing/token/gates, entry-array capacity, profile/read limits/options and retained input ownership, FileStream/strategy/SafeFileHandle/binding graph, and protected final-close/fault provision. Include otherwise-unfunded retained path/digest/registration graphs; an immutable or borrowed label is not a grant. Do not equate entry CLR stride to its wire stride. |
| `Open(o,p)` | Opening phase `p`'s additional controls and scratch: digest/hash algorithm and actual rented/native workspace; header, profile and directory buffers; entry-builder wrapper; padding/read/validation/Core codec workspace; active I/O and cancellation/context registrations; Tasks/ValueTasks/state machines; exact destination installation and producer/return suffix; protected primary/cleanup capture and delivery. A backing/entry array already in `Keep` is not counted again. |
| `Wire(t)` | The existing aggregate decoded-wire debit, not evidence of actual codec-managed memory. Existing decode/count checks remain independent. This ledger does not qualify the separately gated typed-load allocation closure. |
| `Unspent(t)` | Preclaimed future materialization and protected cleanup/return provision not already represented above. Spending converts this into allocated charge, never a second charge or entitlement. |

`Generations(t)` is the union of current, opening candidate, displaced owner, **all** lease/temporary-reference-retained older generations, and failed/quarantined generations. Deduplicate exact generation identity, not different packages sharing an AssetId. `Operations(t)` includes still-returning and failed-retained openings, not just elected replacement bodies.

```text
Debit(t) =
    Fixed + Wire(t)
    + sum Keep(g,t), for g in Generations(t)
    + sum Open(o,current-phase,t), for o in Operations(t)
    + Unspent(t)
Debit(t) <= B0

CreatePreclaim =
    Fixed + max reachable simultaneous
        (Keep(candidate) + Open(create,phase) + Unspent)
CreatePreclaim <= B0

ReplacePreclaim =
    Debit(before election) + max reachable additional
        (candidate Keep + opening/installation/retirement/return work
         + additional unspent provision)
ReplacePreclaim <= B0
```

The maxima cover success, fault, cancellation, constructor prefixes, publication, independent cleanup and actual return. Before opening, use admitted format/profile maxima where counts are unknown: profile ceiling, directory-entry ceiling and exact directory-length relation. The complete header/profile/directory/entry-builder coexistence and real hash/rent capacities belong in the profile. File extent is not an array charge. No unknown runtime/native/fault term is zero.

Reserve replacement's additional claim under the existing Store election before allocating its completion/control machinery or opening the candidate. Concurrent loads cannot spend that reservation. Current and lease-retained old packages remain charged while the candidate is built; switching current to displaced changes ownership edges, not byte entitlement. Package acquisition/final-release/active-I/O dispositions are recorded separately from bytes; a file's extent or a wire debit is not an OS-handle receipt.

For any failure, the surviving root owes:

```text
RetainedDebt =
    q(union of the exact still-owned/reachable issuer, carrier, resource,
      input, original-exception, cleanup-sample, active-I/O,
      invocation and return graphs)
    + unspent still-required cleanup / outcome provision
```

This is the retained subset of `Debit`, **not** an extra recovery allowance. Scratch still retained by an async frame/exception remains charged even after parsing stops. A small root node does not bound its graph. Release only portions whose actual ownership/access ended or transferred to another already-funded owner. If the full retained graph or fault/return envelope cannot be bounded, the preclaim cannot be admitted.

## Implementation sequence after those gates close

1. Validate/admit bootstrap; construct the empty Store and unpublished backing; install the existing intrusive root with explicit opening state. No candidate file has been acquired if new carrier construction fails. Root the returned FileStream immediately before another fallible step/await. Constructor-internal acquisition still follows the separate lower contract above.
2. Carry that same owner through unchanged digest/layout/profile validation. Unpublished entries/provenance/read access cannot be acquired as a usable package. Cancellation stops actual reads/validation before cleanup; no terminal outcome substitutes for their join.
3. Factory success transfers to the exact Store only after its acceptance. Replacement transfers at the existing atomic switch, with current/candidate/old-lease grants coexisting; cancellation after that switch does not undo publication. Root/operation provision ends only after transfer acknowledgment **and** actual last access/return, not Task readiness. Missing return proof is a gate, not permission to root every successful call forever.
4. Capture the primary exception/token before cleanup. Attempt acquired cleanup in reverse acquisition/dependency order outside gates, continuing with independently safe siblings after failure. Retain original cleanup facts without catch-path record/list allocation. With successful cleanup, rethrow the original failure preserving its stack. Cleanup failure must not overwrite it; admitted cancellation with failed cleanup must fault rather than return a canceled Task. The approved Chapter 11 delivery contract governs that fault. Do not invent `Exception.Data`, aggregate-wrapper allocation or a new exception family to bypass that missing contract.
5. Record an unresolved candidate in the preinstalled owner **even when `OpenAsync` returns no backing**. Replacement's body barrier may signal execution ended only after its accesses end; candidate debt has its own retained disposition. A later replacement remains refused while that debt occupies its owner.
6. Store disposal joins the body barrier outside locks, then independently attempts safe current-package release despite candidate/displaced failure. Preserve both outcomes/graphs and all charges. Do not retry an uncertain release. A post-switch old-package failure faults retirement/replacement without rolling back the committed candidate; confirmed release followed by accounting failure never triggers another close.

## Bounded evidence and NO-GO gates

Future managed work extends existing Assets reader/Store/package-lease tests: empty package and explicit new low-budget admission; checksum mismatch versus correctly checksummed malformed directory; real missing/inaccessible file and cancellation; exact/one-short approved preclaim; held old-package leases across repeated replacement; current-plus-candidate refusal; serialized replacement/disposal and once-only transfer/refund. Use real packages and supported synchronization, not new failure producers. Preserve the existing package-identity behavior.

`AssetPackageBackingFailedFinalStreamReleaseRootsBackingForProcessLifetime` uses a subclass throwing before base disposal; its helper later disposes again. It proves managed backing reachability only. It is not a production FileStream constructor/OS-close failure witness. Dual-failure/partial-constructor cases lacking a supported real stimulus remain statically reviewed and observationally blocked, not replaced with allocator injection, raw-handle corruption or artificial close failures.

| NO-GO | Exact condition for retry |
|---|---|
| No original bootstrap or shared-root grant; CPU scope/cap-replacement semantics unapproved | Chapters 02/04/11/30 owners approve the package-acquisition-free original issuer, same-envelope debit and cold/failed-root lifetime funding, including standalone failed creation and compatibility. |
| Incomplete `q`, retained exception graph, runtime/native workspace or actual-return bound | A closed selected opening profile derives every simultaneous term and protected suffix from the final implementation; the complete preclaim fits the original envelope before acquisition. No guessed constant or blanket reservation proves fit. |
| Constructor can acquire without a returned owner and complete unwind is unproved | Exact lower constructor/binding/unwind closure is proved, or an explicitly authorized ownership-preserving acquisition boundary exists. Earlier `try`, a returned-handle wrapper, or finalization alone does not close it. |
| Physical close disposition is unavailable/unproved | Complete valid-path physical-release proof or a supported ownership-preserving lower acknowledgment on the selected runtime/OS/filesystem. Native failure observations retry only when a safe real stimulus/capability changes. Normal/no-op second Dispose, GC and `IsClosed` are not substitutes. |
| Missing primary-plus-cleanup delivery or replacement debt closure | The missing Chapter 11 contract defines bounded capture/fault delivery; candidate and independently failed current/displaced releases remain separately retained and charged after body completion. |

Then validate through ordinary owning-project Debug before Release checks with fresh nonzero NUnit/VSTest selections/TRX results per [Building](../project/Building.md). Managed behavior, native/AOT/public-consumer observation and [Chapter 35](../design/35-implementation-roadmap.md#2-implementation-prerequisites-and-handoff) acceptance remain separate. No Scene preparation, new quota/API/service, World/Host dependency, success-shaped fallback, schema change or broader pre-lease implementation is selected here.

No builds, tests or runtime failure experiments were run for this proposal.

## Bounded review-input identity

Inspected UTC `2026-11-19T09:48:41Z`; HEAD `c9302fa1a27562051b582dbc5c27d3c1892b960c`. The closed local sets below use SHA-256 of ordinal-sorted `relative-Windows-path<TAB>lowercase-raw-file-SHA256` lines, joined with LF, UTF-8 without BOM/trailing newline. `ALL` is their union, excluding this new output. This is not a repository inventory or transitive binary/native attestation.

```text
A: sources\TerraFX.Assets\
AssetPackageReader.cs, AssetPackageBacking.cs, AssetPackageEntry.cs,
AssetStore.cs, AssetStoreOptions.cs, AssetStoreRegistration.cs,
AssetStoreRegistrationBuilder.cs, TerraFX.Assets.csproj;
sources\TerraFX.Runtime\Scenes\PreparedScene.cs

D: docs\design\
02-dotnet-api-memory-and-ownership.md, 04-capacity-and-workload-profiles.md,
11-assets-import-streaming-and-packages.md, 29-project-setup-build-and-deployment.md,
30-public-api-review-surface.md, 35-implementation-roadmap.md;
docs\project\Building.md, docs\project\Roadmap.md;
docs\proposals\m03b-assets-acquisition-failure-owner.md,
docs\proposals\m03b-assets-prelease-recovery-owner.md,
docs\proposals\m03b-assets-prelease-operation-profile-capacity-addendum.md

T: tests\TerraFX.Assets.Tests\
AssetStoreTests.cs, AssetPackageReaderTests.cs, AssetPackageLeaseTests.cs
C: .github\copilot-instructions.md, .github\workflows\ci.yml,
Directory.Build.props, Directory.Build.targets, global.json
R: .dotnet\shared\Microsoft.NETCore.App\11.0.0-rc.3.26522.148\.version
```

| Set | Files | SHA-256 |
|---|---:|---|
| A | 9 | `65569ca2241ba5bc2d6bbc705fa7d9fb67c7866cee56fea32f3bc75f88387e01` |
| D | 11 | `6ce70b52bd41d19ff1d91bf77fe105babd26a4033db4a29c6c74e33031bdd9fc` |
| T | 3 | `81abf72eb23e0d691447d70e0a6f184c6a32ee9945572f764cdc7ad59c48c872` |
| C | 5 | `dae084263f610f43a0d4b34730bfeffd8883fb9e5296ca806efddb1498e7f06b` |
| R | 1 | `aab4f62773986811741aea2bbe2f631c7fbaeeefd485eca380deba5622b1ba8e` |
| ALL | 29 | `bd5251095651a343a7caa92195b0d59c45ebabfd8c5ba762e863cb6006595d8d` |

Chapter 04 was dirty with raw hash `a4d86f3fa373a64548435c53582bf5916e29d77f34650f9d161ccec90e6f0245`; Chapter 30 was dirty with `d4f622d69ed58ab96118c36fbbf4e2d31e062a3cbf3ce3ba1d64309f662fe4ed`. They are inspected context, not accepted amendments. Other listed tracked inputs were clean; no schema/fixture bytes were changed.

Runtime source observations are pinned to the unified revision above, beneath `src/runtime/src/libraries/System.Private.CoreLib/src/`: `System/IO/FileStream.cs`, `System/IO/Strategies/{FileStreamHelpers,FileStreamHelpers.Windows,FileStreamHelpers.Unix,OSFileStreamStrategy}.cs`, `Microsoft/Win32/SafeHandles/{SafeFileHandle,SafeFileHandle.Windows,SafeFileHandle.Unix,SafeThreadPoolIOHandle}.cs`, `System/Runtime/InteropServices/SafeHandle.cs`, and `System/Threading/ThreadPoolBoundHandle.Windows.cs`; the inspected native import declaration is `src/runtime/src/libraries/Common/src/Interop/Windows/Kernel32/Interop.CloseHandle.cs`. These source identities do not qualify their platform closure.

Recheck HEAD, these bytes and dirty state before disposition. Changed inputs require reassessment, not silent hash refresh. Only this proposal is authored; source, maintained contracts, tests, other proposals, index and remote state are outside its changes.
