#!/usr/bin/env bash

set -euo pipefail

SOURCE="${BASH_SOURCE[0]}"
while [ -h "$SOURCE" ]; do
  ScriptRoot="$(cd -P -- "$(dirname -- "$SOURCE")" && pwd)"
  SOURCE="$(readlink "$SOURCE")"
  [[ $SOURCE != /* ]] && SOURCE="$ScriptRoot/$SOURCE"
done
ScriptRoot="$(cd -P -- "$(dirname -- "$SOURCE")" && pwd)"

exec bash "$ScriptRoot/build.sh" --ci "$@"
