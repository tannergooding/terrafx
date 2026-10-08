#!/usr/bin/env bash

set -euo pipefail

SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do
  ScriptRoot="$(cd -P -- "$(dirname -- "$SOURCE")" && pwd)"
  SOURCE="$(readlink "$SOURCE")"
  [[ $SOURCE != /* ]] && SOURCE="$ScriptRoot/$SOURCE"
done
ScriptRoot="$(cd -P -- "$(dirname -- "$SOURCE")" && pwd)"
RepoRoot="$ScriptRoot/.."

architecture=''
build=false
ci=false
configuration='Debug'
help=false
pack=false
restore=false
solution=''
test=false
verbosity='minimal'
additionalArgs=()

while [[ $# -gt 0 ]]; do
  lower="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
  case "$lower" in
    --architecture)
      architecture="${2:?--architecture requires a value}"
      shift 2
      ;;
    --build)
      build=true
      shift
      ;;
    --ci)
      ci=true
      shift
      ;;
    --configuration)
      configuration="${2:?--configuration requires a value}"
      shift 2
      ;;
    --help)
      help=true
      shift
      ;;
    --pack)
      pack=true
      shift
      ;;
    --restore)
      restore=true
      shift
      ;;
    --solution)
      solution="${2:?--solution requires a value}"
      shift 2
      ;;
    --test)
      test=true
      shift
      ;;
    --verbosity)
      verbosity="${2:?--verbosity requires a value}"
      shift 2
      ;;
    --)
      shift
      additionalArgs+=("$@")
      break
      ;;
    *)
      additionalArgs+=("$1")
      shift
      ;;
  esac
done

function Build {
  dotnet build -c "$configuration" --no-restore -v "$verbosity" "/bl:$LogDir/build.binlog" /err "${additionalArgs[@]}" "$solution"
}

function Help {
  echo "Common settings:"
  echo "  --configuration <value>  Build configuration (Debug, Release)"
  echo "  --verbosity <value>      MSBuild verbosity (quiet, minimal, normal, detailed, diagnostic)"
  echo "  --help                   Print help and exit"
  echo ""
  echo "Actions:"
  echo "  --restore                Restore dependencies"
  echo "  --build                  Build solution"
  echo "  --test                   Run all tests in the solution"
  echo "  --pack                   Package build artifacts"
  echo ""
  echo "Advanced settings:"
  echo "  --solution <value>       Path to solution or project to build"
  echo "  --ci                     Acquire the SDK and run restore, build, test, pack"
  echo "  --architecture <value>   SDK architecture (<auto>, amd64, x64, x86, arm64, arm)"
  echo ""
  echo "Command line arguments not listed above are passed through to dotnet."
}

function Pack {
  dotnet pack -c "$configuration" --no-build --no-restore -v "$verbosity" "/bl:$LogDir/pack.binlog" /err "${additionalArgs[@]}" "$solution"

  if $ci; then
    dotnet pack -c "$configuration" --no-build --no-restore -v "$verbosity" "/bl:$LogDir/pack.preview.binlog" /err /p:PACKAGE_PUBLISH_MODE=preview "${additionalArgs[@]}" "$solution"
    dotnet pack -c "$configuration" --no-build --no-restore -v "$verbosity" "/bl:$LogDir/pack.stable.binlog" /err /p:PACKAGE_PUBLISH_MODE=stable "${additionalArgs[@]}" "$solution"
  fi
}

function Restore {
  dotnet restore -v "$verbosity" "/bl:$LogDir/restore.binlog" /err "${additionalArgs[@]}" "$solution"
}

function Test {
  dotnet test -c "$configuration" --no-build --no-restore -v "$verbosity" "/bl:$LogDir/test.binlog" /err "${additionalArgs[@]}" "$solution"
}

if $help; then
  Help
  exit 0
fi

case "$(printf '%s' "$configuration" | tr '[:upper:]' '[:lower:]')" in
  debug) configuration='Debug' ;;
  release) configuration='Release' ;;
  *) echo "Unsupported configuration: $configuration" >&2; exit 1 ;;
esac

verbosity="$(printf '%s' "$verbosity" | tr '[:upper:]' '[:lower:]')"
architecture="$(printf '%s' "$architecture" | tr '[:upper:]' '[:lower:]')"

case "$verbosity" in
  quiet|minimal|normal|detailed|diagnostic) ;;
  *) echo "Unsupported verbosity: $verbosity" >&2; exit 1 ;;
esac

case "$architecture" in
  ''|'<auto>'|amd64|x64|x86|arm64|arm) ;;
  *) echo "Unsupported SDK architecture: $architecture" >&2; exit 1 ;;
esac

if $ci; then
  build=true
  pack=true
  restore=true
  test=true
  additionalArgs+=('-p:ContinuousIntegrationBuild=true')
  architecture="${architecture:-<auto>}"
fi

solution="${solution:-$RepoRoot/TerraFX.slnx}"
ArtifactsDir="$RepoRoot/artifacts"
LogDir="$ArtifactsDir/log/$configuration"
mkdir -p -- "$LogDir"
if [[ -n "$architecture" ]]; then
  export DOTNET_CLI_TELEMETRY_OPTOUT=1
  export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
  DotNetInstallScript="$ArtifactsDir/dotnet-install.sh"
  curl -fsSL 'https://dot.net/v1/dotnet-install.sh' -o "$DotNetInstallScript"
  DotNetInstallDirectory="$ArtifactsDir/dotnet"
  bash "$DotNetInstallScript" --jsonfile "$RepoRoot/global.json" --install-dir "$DotNetInstallDirectory" --architecture "$architecture" --no-path
  export DOTNET_ROOT="$DotNetInstallDirectory"
  export PATH="$DotNetInstallDirectory:$PATH"
fi

if $restore; then
  Restore
fi

if $build; then
  Build
fi

if $test; then
  Test
fi

if $pack; then
  Pack
fi
