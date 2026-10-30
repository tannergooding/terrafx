# Building the repository

The repository aims to be simple to configure, build, and produce binaries. To achieve this it pulls in all dependencies as NuGet packages and provides a set of build scripts to assist in performing common tasks such as restoring dependencies, building, testing, and produce NuGet packages.

These scripts are:
 * [scripts/build.ps1](../../scripts/build.ps1) on Windows
 * [scripts/build.sh](../../scripts/build.sh) on Linux

Each script performs the requested build steps and expects that the .NET SDK is already on the box. This means that after a clean enlistment, you must select both the `restore` and `build` steps (in that order).

In addition there are [scripts/cibuild.cmd](../../scripts/cibuild.cmd) and [scripts/cibuild.sh](../../scripts/cibuild.sh) scripts which allow you to reproduce everything the CI environment does. This includes automatically acquiring a compatible .NET SDK and running each step in the correct sequence (first restore, then build, then test, then pack). Run Debug before Release.

Specifying `-architecture` or `--architecture` also acquires the published SDK version from [global.json](../../global.json) under `artifacts\dotnet`. CI's SDK setup uses the same file; update its version for RC/GA releases and servicing without changing the scripts or workflow. Installed SDK selection retains the configured roll-forward policy.

## Requirements

.NET 11.0 SDK: https://dotnet.microsoft.com/download/dotnet/11.0

## Available Arguments

The scripts accept any combination of build steps and additionally forward any user specified arguments given.

For example, `scripts/build.ps1 -build -restore` runs both steps. The scripts are aware of command ordering requirements and will run them in the correct order (e.g. `restore` will always run before `build`).

The Windows arguments take a single dash (`-`) as the prefix; Bash arguments use two (`--`).

The available arguments can be seen via the `help` command, but are additionally listed below for reference:
```
Common settings:
  -configuration <value>  Build configuration (Debug, Release)
  -verbosity <value>      MSBuild verbosity (quiet, minimal, normal, detailed, diagnostic)
  -help                   Print help and exit

Actions:
  -restore                Restore dependencies
  -build                  Build solution
  -test                   Run all tests in the solution
  -pack                   Package build artifacts

Advanced settings:
  -solution <value>       Path to solution or project to build
  -ci                     Acquire the SDK and run restore, build, test, pack
  -architecture <value>   SDK architecture (<auto>, amd64, x64, x86, arm64, arm)
  -additionalArgs <array> Forward extra options (PowerShell)

Command line arguments not listed above are passed through to dotnet.
PowerShell arguments can be shortened as much as to be unambiguous (e.g. -co for configuration, -t for test, etc.).
```

Use `-additionalArgs @("...")` for options that conflict with PowerShell parameters. Bash accepts `--` before forwarded arguments.

## Utilizing `dotnet`

Given that the above build scripts all utilize `dotnet` behind the scenes, you can also utilize the same for working with the repository.

That is, all of the existing well known commands "just work". This includes, but is not limited to:
 * `dotnet restore`
 * `dotnet build`
 * `dotnet test`
 * `dotnet pack`

With a compatible SDK installed, `dotnet build` also restores dependencies automatically; no script or SDK acquisition step is required.

Test filtering uses the runner's existing `FullyQualifiedName` support, as in the foundation-codec commands below. For implemented cases, confirm nonzero selected and executed counts and inspect current-run TRX results under `artifacts\tst`. A successful command with no selected tests is not validation.

### Public structural EBIN and schema metadata

Core exposes bounded schema-agnostic EBIN reader/writer and scalar/array payload helpers. These validate wire structure and preserve canonical byte layout; they do not activate or validate an owner schema. `CompiledContentSchemaRegistry` contains immutable schema/profile metadata only. Typed owner codecs and their roots are separately compiled with the selected application registrations.

Run the focused structural, payload, and registry metadata regressions:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~EbinStructuralReaderTests|FullyQualifiedName~EbinStructuralWriterTests|FullyQualifiedName~EbinPayloadWriterTests|FullyQualifiedName~ContentSchemaRegistryTests"
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~EbinStructuralReaderTests|FullyQualifiedName~EbinStructuralWriterTests|FullyQualifiedName~EbinPayloadWriterTests|FullyQualifiedName~ContentSchemaRegistryTests"
```

These tests establish only the behaviors they exercise; schema-owner semantics, World registration, package loading, persistence, and M03 acceptance remain distinct.

### Core-private foundation codecs

The EBIN1 structural layer and M02 foundation admission layer live in `sources\TerraFX.Core`.
They are internal, BCL-only implementation details, not a public codec API or an Assets dependency.
The literal eight-schema catalogue fixes canonical registry/kernel bytes and digests; schema versions
own their fixed behavior, and incoming manifests cannot replace that catalogue. Profile admission uses
an explicit selection of already registered identities/settings. Optional extensions use the reserved
EBIN extension set with signed 64-bit IDs and inert opaque payloads. Frame I/O additionally requires a private persistence binding
containing the stable world GUID, profile, runtime owner and binding revision.

Reads validate bounded spans before copying an owned staged wire value. They neither allocate a
runtime owner nor register profiles or activate worlds. Projection and capture are explicit operations
against an owner binding; retained optional fields remain inert and survive recapture. Writers validate
input and checked extent before destination capacity, leave the entire destination unchanged on failure,
and report zero written. Structural payload I/O does not substitute for schema/context admission.

Run the focused foundation-codec regressions:

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~M02FoundationCodecTests"
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~M02FoundationCodecTests"
```

The tests construct five canonical EBIN records in C# and check their exact lengths and SHA-256 values.
Focused cases exercise distinct codec, validation, binding and ownership behavior; there is no separate
M02 fixture manifest, materializer, prefix corpus or fixture-package pin to maintain. The test project
also builds the separate unsafe-disabled public spatial consumer.

The spatial consumer can be run separately as JIT or published for a Windows x64 NativeAOT execution:

```powershell
dotnet run --project .\tests\TerraFX.Core.SpatialConsumer\TerraFX.Core.SpatialConsumer.csproj --configuration Release
dotnet publish .\tests\TerraFX.Core.SpatialConsumer\TerraFX.Core.SpatialConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

This consumer covers its selected spatial API surface only; it does not complete the M02 NativeAOT numerical profiles or Linux target qualification.

### Focused production behavior tests

The Core suite contains focused literal cases for bounded rounding, spatial conversion/normalization,
Angle, and geometry behavior. Physics primitive tests exercise the production validators and measures;
these selections do not imply unimplemented M03 package, World, native, or AOT behavior.

```powershell
dotnet test .\tests\TerraFX.Core.Tests\TerraFX.Core.Tests.csproj --configuration Debug --filter "FullyQualifiedName~M02FoundationCodecTests|FullyQualifiedName~BoundedRationalTests|FullyQualifiedName~SpatialMathTests|FullyQualifiedName~SpatialValueTests|FullyQualifiedName~AngleTests|FullyQualifiedName~GeometryKernelTests|FullyQualifiedName~GeometryComparisonTests|FullyQualifiedName~ContentSchemaReferenceTests"
dotnet test .\tests\TerraFX.Physics.Tests\TerraFX.Physics.Tests.csproj --configuration Debug --filter "FullyQualifiedName~PrimitiveShapeValidationTests"
```

Repeat the applicable selections in Release after Debug succeeds. Confirm nonzero selected and executed counts in current-run TRX results under `artifacts\tst\<Configuration>`.

### Runtime public-provider consumers

The Runtime test project builds two independent unsafe-disabled executable witnesses: the original handwritten component consumer and the generated-component consumer. The generated consumer references `TerraFX.Generators` as an analyzer and exercises the same public schema registration, World mutation, default, and scoped-query path without changing the handwritten source.

Run each executable separately after building:

```powershell
dotnet run --project .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Debug
dotnet run --project .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Debug
```

For Windows NativeAOT qualification, each executable project enables `PublishAot` locally when `TerraFXPublishAot=true`; the generated project's analyzer reference removes that switch. Publish and execute the artifacts separately:

```powershell
dotnet publish .\tests\TerraFX.Runtime.ComponentConsumer\TerraFX.Runtime.ComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
dotnet publish .\tests\TerraFX.Runtime.GeneratedComponentConsumer\TerraFX.Runtime.GeneratedComponentConsumer.csproj --configuration Release --runtime win-x64 --self-contained true -p:TerraFXPublishAot=true
```

NativeAOT does not support cross-OS compilation; publish Linux artifacts on the required Linux host, then execute them there. JIT or Windows NativeAOT execution does not qualify the Linux target or M02 profiles.
