# TerraFX repository instructions

## Build and tests

Use the SDK selected by `global.json` and ordinary .NET commands. Do not duplicate SDK pins in scripts or workflows. Package versions are centralized in `Directory.Packages.props`; shared build settings belong in the existing root and folder-specific props/targets.

```powershell
dotnet build TerraFX.slnx --configuration Debug
dotnet test TerraFX.slnx --configuration Debug
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName=TerraFX.Core.Tests.SpatialValueTests.PublicSurfaceHasOnlyNormativeMembersAndDimensionalOverloads"
```

Run Debug before Release when both configurations apply. Select the owning project and related test filters before escalating to the solution. Tests use NUnit/VSTest; current-run TRX results are under `artifacts\tst\<Configuration>`. Confirm nonzero selected and executed tests. Build enforces code style and repository analyzers with warnings as errors. See `docs\project\Building.md` for build-script and CI reproduction commands.

## Architecture

- Production projects, including Content.Tools, Editor, and consumer-facing Generators, belong under `sources`. `tools` is for repository-local utilities and architecture analyzers; tests belong under `tests`.
- `TerraFX.Core` is BCL-only. Public foundation namespaces use `TerraFX` and semantic child namespaces such as `TerraFX.Numerics`, not `TerraFX.Core`.
- `TerraFX.Runtime` contains the coupled Entities, Scenes, Animation, Rendering, UI, Integration, and Hosting subsystems. Namespaces are logical ownership boundaries, not separate assembly requirements. Internal accessibility does not authorize crossing them.
- Preserve the assembly and logical Runtime dependency directions in `docs\design\29-project-setup-build-and-deployment.md`. Lower layers cannot acquire Runtime, Editor, Content.Tools, or compiler-tooling dependencies through callbacks, registrations, or shared metadata.
- Check actual source and implementation status before assuming a designed public surface exists. Do not add stubs merely to populate a future design.

## Contract authority and review

- Read `docs\project\Roadmap.md` and the applicable milestone in `docs\design\35-implementation-roadmap.md`. Subsystem chapters own behavior; Chapter 30 owns public declarations; Chapters 32-34 own required observations; Chapter 35 owns prerequisites and gates.
- Before submitting a schema/API contract for review, close its referenced types, target versions, operator inputs, construction order, and independently derived membership sets. Demonstrate the change with a concrete distinguishing example. A precedence paragraph does not replace missing definitions.
- Freeze the complete review input set, including dirty-file and schema hashes. Do not edit those inputs during review. Verify hashes before applying a disposition; acceptance applies only to the reviewed bytes and scope.
- Keep one current status/index record for each workstream. Link historical decisions and label superseded proposals; do not present them as competing current authority. Design chapters contain contracts, while the project roadmap records implementation status and evidence. Keep session decision artifacts outside the repository and avoid duplicating history in maintained prose.

## Fixtures and qualification

- Use the owner contract's reviewed literal fixtures and directly derived expectations. Do not take production output as the sole expected-value source or maintain a second complete implementation for comparison. Preserve protected M02 fixture bytes and semantic facts; version changed schemas according to their owner contract.
- Verify exact versioned case membership. Distinguish framework-test counts from executed conformance cases, and identify unsupported cases. Unsupported filtering and empty selections are not passes.
- Design acceptance, managed behavior tests, compiled roots, native/AOT qualification, real consumer observations, and milestone acceptance are distinct. Apply the current Chapter 35 gates and each review's exact scope; evidence at one level does not establish another.
- Qualification tooling consumes ordinary developer build/test outputs. Do not replace normal .NET workflows with a separate runner or infer native/AOT, workload, or public-consumer qualification from managed helper tests.

## C# and compiler tooling

- Follow `.editorconfig` and repository diagnostics when first authoring a type, including member ordering, brace layout, and public XML documentation. Preserve central analyzer settings and test suppressions rather than adding local workarounds.
- Use TerraFX geometry types. When BCL numerics are needed alongside them, alias the specific BCL types rather than introducing ambiguous `System.Numerics` imports.
- Source products default to `SkipLocalsInit`; explicitly initialize every value read or exposed, including stack storage. Generators opt out, and tests do not inherit source-folder defaults.
- Invoke the applicable `.github\skills\add-analyzer` or `add-incremental-generator` workflow before that work. Repository-only analyzers belong under `tools`; consumer-facing generators remain under `sources\TerraFX.Generators`. Do not invent diagnostic IDs, severity policy, or schema contracts.
