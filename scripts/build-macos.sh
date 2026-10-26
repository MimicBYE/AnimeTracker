#!/bin/sh
set -eu
ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
# PowerShell is included on the GitHub macOS runner. It keeps local and CI packaging identical.
exec pwsh -NoProfile -File "$ROOT/scripts/build-macos.ps1" "$@"
