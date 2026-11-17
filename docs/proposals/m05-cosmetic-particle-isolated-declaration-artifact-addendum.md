# M05 isolated declaration artifact and provenance

## Decision and exact next action

**Tentative local qualification workflow; not execution authorization or funding/ownership closure.** An actual candidate-layout artifact can be produced reliably without modifying live World types: use two isolated materializations of the **current working source**, with a reviewed declaration-only patch in one, ordinary production projects, and constructor-free cases in the existing Runtime fixture.

The [common-layout qualification addendum](m05-cosmetic-particle-common-layout-qualification-addendum.md) supplies the observation ownership/boundary; the [common-representation addendum](m05-cosmetic-particle-common-representation-addendum.md) supplies the representation. This closes their artifact/provenance procedure, not their measured values or production budget entries.

**Next action:** obtain authorization to stage the two isolated source/artifact lanes and prepare the exact candidate patch plus shared measurement-fixture patch for review. Freeze those inputs before building or measuring. No live source/test edit is needed for that staging. This proposal turn creates neither lane nor patch/artifact and runs no probe.

## 1. Freeze the actual baseline, not just HEAD

The live baseline has modified registration/lifetime files and untracked production credit/use files. A clean checkout of HEAD alone is not the inspected baseline.

Use local, explicitly authorized isolated checkouts/artifact roots, for example below `artifacts\qualification\m05-common-layout\baseline` and `candidate`; do not use temporary directories or the live build outputs. Normal detached Git worktrees with an exact frozen working-tree overlay are suitable. No commit, push or live index reset is required.

Freeze the build input closure under quiescence or a verified before/copy/after hash boundary:

- Actual tracked bytes, including staged/unstaged changes and deletions, plus untracked production/build/test inputs. Overlay real disk contents and reproduce deletions; never resurrect a HEAD file that the live baseline removed.
- Shared props/targets, projects, `.editorconfig`, central package versions, NuGet configuration, `global.json`, linked sources, generated-input sources and shader/toolchain manifests needed by the owning project. Explicitly identify any ignored input the build consumes.
- Resolved external SDK/runtime/compiler/package/native-tool identities. Do not copy caches, `bin`/`obj` or stale generated outputs as source, and do not copy credentials/account state.

Verify both materializations against that frozen set before applying lane-specific changes. Keep the bounded per-run provenance locally in ignored artifacts, not a maintained repository inventory. Do not recursively copy `artifacts` into a staging directory beneath itself.

The production source in the baseline lane must remain byte-identical to this snapshot. Add the same measurement-only fixture logic to both lanes; it changes no production field. A rebuilt baseline is a **source-exact rebuild**, not automatically byte-identical to an earlier live DLL: retain both identities and establish their production-source/layout-relevant correspondence. Stop if that correspondence cannot be established.

## 2. Freeze one real declaration-only candidate patch

The only common field additions are:

| Production declaration | Candidate field and placement |
|---|---|
| `WorldRegistrationBuilder` | `private bool _cosmeticCreditStorageSelected;`, after `_components` and before `_cpuExtractionSupport` |
| `WorldRegistration` | `private readonly bool _cosmeticCreditStorageSelected;`, after `_components` and before `_cpuExtractionSupport` |
| `WorldLifetimeControl` | `private WorldCosmeticCreditStandingControl? _cosmeticCreditStandingControl;`, after `_admissionDomain` and before `_defaultDomainOwner` |

Use the real `TerraFX.Entities.WorldCosmeticCreditStandingControl` declaration with the common-representation addendum's actual binding/amount/phase/owner/failure inventory. Freeze its concrete fields, types and necessary capture/read members in the reviewed patch. It is not constructed or measured by this lane; the parent reference measurement does not qualify its header or lifecycle. An empty placeholder class or `object` substitute is not acceptable. If that concrete declaration cannot be closed from the selected inventory, stop for that exact owner input.

Only necessary private/internal capture/read declarations may accompany the fields so the real project compiles normally. Identify every such edit. Do not wire builder `Build`, fresh/restore admission, lifetime construction, disposal, phase dispatch or any qualification gate to execute the new selection/control. No public API, layout attribute, changed packing/base type, test-only production symbol, warning suppression, no-op participant or alternative producer is permitted.

The candidate must differ from the frozen baseline by exactly this reviewed production patch; the fixture logic is identical. Record the patch hash, complete affected field inventories and all resulting module identities. Later changes invalidate evidence to the extent they change the reviewed layout inputs; do not silently promote a different artifact.

## 3. Preserve build isolation and the resolved toolchain

Existing root props derive `BaseArtifactsPath` from their own file directory, then derive output/intermediate/package roots. Test props derive TRX paths from that root; shader outputs/state derive from `IntermediateOutputPath` and test shader copies use `OutDir`. A separate source root therefore provides the normal isolation mechanism; no replacement project or runner is needed.

Before any build, verify evaluated outputs for the owning project and its project-reference closure, including analyzer/generator/tools/consumer references and shader outputs. Resolve junctions/links and reject any output/copy destination that escapes its lane or points to live artifacts. Remove no live outputs, caches or shared build servers; do not fix isolation by disabling analyzers, shaders, references or diagnostics.

Copy `global.json` unchanged. The inspected live SDK resolves from the repository-local `.dotnet`, not the installed stable SDK. In each lane:

- Use the frozen SDK host and verify `dotnet --info` resolves the same actual SDK/runtime/compiler lane. `global.json` already includes `$host$`; do not introduce another version pin.
- Scope `PATH`/appropriate `DOTNET_ROOT` settings to that lane's child process so child `Exec` calls resolve the same host. This matters because the shader target invokes `dotnet` by name even when the top-level invocation uses an absolute host path.
- Record relevant MSBuild global/environment properties, restored package identities and external tool hashes. Reject unexpected output-path overrides or source reads resolving back into mutable live inputs. Read-only access to the frozen SDK/package/toolchain roots is distinct from output sharing.

Keep all ordinary restore/build inputs; new isolated intermediates may need normal restore. Do not reuse live `obj` files to bypass it. Build binlogs remain local and identify actual compiler inputs, references and output paths; source/hash records remain necessary.

## 4. Use only constructor-free measurement cases

Add three narrow cases to the existing `WorldLifetimeControlQuarantineTests` fixture, under the fixed method selector `CommonRepresentationShallowLayout`: builder, immutable registration and lifetime control. The lane-local runsettings supplies the required NUnit parameter `CommonLayoutLane`, exactly `Baseline` or `Candidate`, to select the corresponding reviewed field inventory. This tag controls test expectations only; it is not a production feature flag. Missing, unknown or mismatched tags/fields fail rather than choosing an inferred fallback.

The fixture must:

- Verify actual loaded assembly path/hash/MVID, type identity and complete instance-field metadata against the lane inputs. Use separate fresh test processes for baseline and candidate; do not exchange same-named assemblies inside one running test host.
- Check the actual target types have no finalizer/critical-resource path. Allocate only bounded, test-owned constructor-free subjects of the three common types, using the existing `RuntimeHelpers.GetUninitializedObject` pattern. Never invoke their constructors, members, factories or disposal, or pass them into production code.
- Allocate subject storage and finish reflection/type setup outside the measurement interval; retain subjects through the readout. Observe per-type shallow managed allocation, derive/check target layout, and report only matched baseline/candidate deltas. This does not observe constructors, native costs, cold production work or return.
- Construct no World, credit owner or standing control, and invoke no World/candidate lifecycle, Scene or pool path. Confirm fixture/test-host initialization does not introduce those operations.

Use ordinary build then exact-filter test commands in **each isolated root**, with Debug before Release. For example, after the fixture/tag input is frozen:

```powershell
& $SdkHost build .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug -bl:.\artifacts\log\common-layout-debug.binlog
& $SdkHost test .\tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj --configuration Debug --no-build --no-restore --filter "FullyQualifiedName~TerraFX.Runtime.Tests.Entities.WorldLifetimeControlQuarantineTests.CommonRepresentationShallowLayout" --settings .\artifacts\common-layout.runsettings --logger "trx;LogFileName=common-layout-debug.trx"
```

`$SdkHost` is the recorded resolved host, not a new SDK pin; the lane-local runsettings supplies only its explicit expected-inventory tag. Repeat with Release after Debug completes. These commands are proposed, not run or verified here.

Require exactly the three named shallow cases in each lane/configuration, nonzero execution and no unsupported/skipped cases represented as passes. Compare baseline Debug with candidate Debug, and baseline Release with candidate Release—not Debug against Release. Keep input/runtime tags, test source, source/patch hashes, paths, compiler command lines, assembly hashes/MVIDs, field inventories and TRXs together.

## 5. Stop and promotion boundary

Stop before building if source materialization, referent declaration, SDK resolution or output containment cannot be made exact. Stop before comparison for extra/missing fields, differing layout-relevant inputs, wrong loaded assemblies, unsupported layout/finalizer conditions or incomplete case membership. Do not replace missing evidence with a shadow type, Reflection.Emit approximation, expected pointer size or guessed delta.

A successful lane supplies three measured **layout inputs**, explicitly unfunded. It does not admit registration metadata, change World/Chapter 04 budgets, prove spare bytes in the 4096-byte control group, or qualify standing construction/cleanup/return/publishers. Original-grant admission/coverage must still be implemented and reviewed before these fields enter normal construction. No live DLL/source patch is promoted as part of this measurement workflow.

If staging cannot preserve the exact dirty baseline/toolchain or contain all outputs, the minimal required user decision is permission/location for a separate faithful qualification checkout with those inputs. If the concrete standing-control declaration remains unresolved, its exact data declaration is the minimal owner input. Neither condition authorizes a surrogate. All operational gates remain closed.

## Inspected boundary

These are inspection inputs, not staged candidate artifacts or measured deltas.

| Input | SHA-256 |
|---|---|
| `Directory.Build.props` | `770EE4B2A422861A6F23F0D39DD0ECA361A81CB87E64EBAC4E12170F71231D7A` |
| `Directory.Build.targets` | `AEEC1D08E8037470239035812351C4045124607D67A354D482112F4D642D4105` |
| `sources\Directory.Build.props` | `D44373AF315523F8AF9F09F36BDED2A688E2B5C1572FCC0732310C7EF590EAD8` |
| `tests\Directory.Build.props` | `281AAD4237121FEAB1903C2384F1A5F0AED7E218A704C918FE953E9FC8E75764` |
| `global.json` | `1BCE01871321C2DF82E887DA428CE5C121A6C6541FE65017FE3D249BE47E190C` |
| `tests\TerraFX.Runtime.Tests\TerraFX.Runtime.Tests.csproj` | `B8DFBC8438BD32A93ACB54DD420AD09A7BF259C2DB5D366D2100229D066CF583` |
| `tools\TerraFX.ShaderCompiler\ShaderCompilation.targets` | `13D412EFC73883D4C2CE60F39FD9A279A29FACCEE411F4B5443AC78E58B25126` |
| `docs\proposals\m05-cosmetic-particle-common-layout-qualification-addendum.md` | `2117438FF3D4635A9DFDAF4A1383EC8E183FD32C196A773F11E4E7FF83BCBBEF` |
| `docs\proposals\m05-cosmetic-particle-common-representation-addendum.md` | `F79FAE8F40CC5D7C014DA96B4B336C9EF59C851E3A80A83BB0AA4F620D2AF443` |
| `sources\TerraFX.Runtime\Entities\WorldRegistrationBuilder.cs` | `40B6C12CF129A56FC1BE0890311AFF5203F805C50376DDE9184EECB42E703487` |
| `sources\TerraFX.Runtime\Entities\WorldRegistration.cs` | `0FA11E1B9C75FF4D2EB8309EF2E2226C54820D003CE8579CEE2D91D52E311D6E` |
| `sources\TerraFX.Runtime\Entities\WorldLifetimeControl.cs` | `7F47BE8C4943CE6FC2610FD98D3D2B7470B8F83EA00EC5D9C6C4A3F5C46A9BEB` |
| `tests\TerraFX.Runtime.Tests\Entities\WorldLifetimeControlQuarantineTests.cs` | `F52196A4EA7F99AB47931B436CF7CE9512D7A10F8ADA80860D6928DA90F552AE` |
| `artifacts\log\particle-credit-layout\dotnet-info.log` | `6A8AFD0CC738AA4FDA668F44A3F7B126D48CE95CA43647CD045DCCCA577A1210` |

Only this proposal is added. No checkout, source/test/build-file edit, candidate artifact, build, probe or World/Scene/pool operation is created/executed.
