[CmdletBinding(PositionalBinding=$false)]
Param(
  [ValidateSet("<auto>", "amd64", "x64", "x86", "arm64", "arm")][string] $architecture = "",
  [switch] $build,
  [switch] $ci,
  [ValidateSet("Debug", "Release")][string] $configuration = "Debug",
  [switch] $help,
  [switch] $pack,
  [switch] $restore,
  [string] $solution = "",
  [switch] $test,
  [ValidateSet("quiet", "minimal", "normal", "detailed", "diagnostic")][string] $verbosity = "minimal",
  [Parameter(ValueFromRemainingArguments=$true)][String[]] $additionalArgs = @()
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Build() {
  $logFile = Join-Path -Path $LogDir -ChildPath "build.binlog"
  & dotnet build -c "$configuration" --no-restore -v "$verbosity" /bl:"$logFile" /err $additionalArgs "$solution"

  if ($LastExitCode -ne 0) {
    throw "'Build' failed for '$solution'"
  }
}

function Help() {
  Write-Host -Object "Common settings:"
  Write-Host -Object "  -configuration <value>  Build configuration (Debug, Release)"
  Write-Host -Object "  -verbosity <value>      MSBuild verbosity (quiet, minimal, normal, detailed, diagnostic)"
  Write-Host -Object "  -help                   Print help and exit"
  Write-Host -Object ""
  Write-Host -Object "Actions:"
  Write-Host -Object "  -restore                Restore dependencies"
  Write-Host -Object "  -build                  Build solution"
  Write-Host -Object "  -test                   Run all tests in the solution"
  Write-Host -Object "  -pack                   Package build artifacts"
  Write-Host -Object ""
  Write-Host -Object "Advanced settings:"
  Write-Host -Object "  -solution <value>       Path to solution or project to build"
  Write-Host -Object "  -ci                     Acquire the SDK and run restore, build, test, pack"
  Write-Host -Object "  -architecture <value>   SDK architecture (<auto>, amd64, x64, x86, arm64, arm)"
  Write-Host -Object "  -additionalArgs <array> Forward extra options to dotnet"
  Write-Host -Object ""
  Write-Host -Object "Command line arguments not listed above are passed through to dotnet."
  Write-Host -Object "The above arguments can be shortened as much as to be unambiguous (e.g. -co for configuration, -t for test, etc.)."
}

function Pack() {
  $logFile = Join-Path -Path $LogDir -ChildPath "pack.binlog"
  & dotnet pack -c "$configuration" --no-build --no-restore -v "$verbosity" /bl:"$logFile" /err $additionalArgs "$solution"

  if ($LastExitCode -ne 0) {
    throw "'Pack' failed for '$solution'"
  }

  if ($ci) {
    & dotnet pack -c "$configuration" --no-build --no-restore -v "$verbosity" /bl:"$LogDir\pack.preview.binlog" /err /p:PACKAGE_PUBLISH_MODE=preview $additionalArgs "$solution"

    if ($LastExitCode -ne 0) {
      throw "'Pack' failed for '$solution' (preview)"
    }

    & dotnet pack -c "$configuration" --no-build --no-restore -v "$verbosity" /bl:"$LogDir\pack.stable.binlog" /err /p:PACKAGE_PUBLISH_MODE=stable $additionalArgs "$solution"

    if ($LastExitCode -ne 0) {
      throw "'Pack' failed for '$solution' (stable)"
    }
  }
}

function Restore() {
  $logFile = Join-Path -Path $LogDir -ChildPath "restore.binlog"
  & dotnet restore -v "$verbosity" /bl:"$logFile" /err $additionalArgs "$solution"

  if ($LastExitCode -ne 0) {
    throw "'Restore' failed for '$solution'"
  }
}

function Test() {
  $logFile = Join-Path -Path $LogDir -ChildPath "test.binlog"
  & dotnet test -c "$configuration" --no-build --no-restore -v "$verbosity" /bl:"$logFile" /err $additionalArgs "$solution"

  if ($LastExitCode -ne 0) {
    throw "'Test' failed for '$solution'"
  }
}

try {
  if ($help) {
    Help
    exit 0
  }

  if ($configuration -eq "Debug") {
    $configuration = "Debug"
  }
  else {
    $configuration = "Release"
  }

  if ($ci) {
    $build = $true
    $pack = $true
    $restore = $true
    $test = $true
    $additionalArgs += "-p:ContinuousIntegrationBuild=true"

    if ($architecture -eq "") {
      $architecture = "<auto>"
    }
  }

  $RepoRoot = Join-Path -Path $PSScriptRoot -ChildPath ".."

  if ($solution -eq "") {
    $solution = Join-Path -Path $RepoRoot -ChildPath "TerraFX.slnx"
  }

  $ArtifactsDir = Join-Path -Path $RepoRoot -ChildPath "artifacts"
  $LogDir = Join-Path -Path $ArtifactsDir -ChildPath "log\$configuration"
  New-Item -Path $LogDir -Force -ItemType "Directory" | Out-Null
  if ($architecture -ne "") {
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 1
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 1
    $DotNetInstallScript = Join-Path -Path $ArtifactsDir -ChildPath "dotnet-install.ps1"
    Invoke-WebRequest -Uri "https://dot.net/v1/dotnet-install.ps1" -OutFile $DotNetInstallScript -UseBasicParsing

    $DotNetInstallDirectory = Join-Path -Path $ArtifactsDir -ChildPath "dotnet"
    $GlobalJsonFile = Join-Path -Path $RepoRoot -ChildPath "global.json"
    & $DotNetInstallScript -JSonFile $GlobalJsonFile -InstallDir $DotNetInstallDirectory -Architecture $architecture -NoPath

    $env:DOTNET_ROOT = $DotNetInstallDirectory
    $env:PATH = "$DotNetInstallDirectory;$env:PATH"
  }

  if ($restore) {
    Restore
  }

  if ($build) {
    Build
  }

  if ($test) {
    Test
  }

  if ($pack) {
    Pack
  }
}
catch {
  Write-Host -Object $_
  Write-Host -Object $_.Exception
  Write-Host -Object $_.ScriptStackTrace
  exit 1
}

exit 0
