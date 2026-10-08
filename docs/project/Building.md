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

Test filtering uses the runner's existing support, for example `dotnet test --filter TestCategory=Unit`.
