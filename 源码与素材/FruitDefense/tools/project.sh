#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ACTION=doctor
if [[ $# -gt 0 ]]; then ACTION="$1"; fi
export DOTNET_CLI_HOME="$ROOT/.local/dotnet-home"
export NUGET_PACKAGES="$ROOT/.local/nuget"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=0
export DOTNET_GENERATE_ASPNET_CERTIFICATE=false
export DOTNET_NOLOGO=1
mkdir -p "$ROOT/.local"
if [[ "$ACTION" == setup ]]; then
  shift || true
  exec python3 "$ROOT/tools/setup.py" "$@"
fi
DOTNET="$(printenv FRUITDEFENSE_DOTNET || true)"
if [[ -x "$ROOT/tools/dotnet/dotnet" ]]; then
  DOTNET="$ROOT/tools/dotnet/dotnet"
elif [[ -z "$DOTNET" ]]; then
  DOTNET="$(command -v dotnet || true)"
fi
[[ -n "$DOTNET" ]] || { echo ".NET SDK missing. Run: python3 tools/setup.py --download"; exit 1; }
export DOTNET_ROOT="$(dirname "$DOTNET")"
export PATH="$DOTNET_ROOT:$PATH"
GODOT="$(printenv FRUITDEFENSE_GODOT || true)"
if [[ -z "$GODOT" && -d "$ROOT/tools/godot" ]]; then
  GODOT="$(find "$ROOT/tools/godot" -type f \( -name '*mono_linux.x86_64' -o -name '*mono_linux.arm64' -o -path '*/Contents/MacOS/Godot' \) -print -quit)"
fi
if [[ -z "$GODOT" ]]; then
  GODOT="$(command -v godot-mono || command -v godot4 || command -v godot || true)"
fi
cd "$ROOT/game"
VERSION="$("$DOTNET" --version)"
[[ "$VERSION" == 10.0.4[0-9][0-9] ]] || { echo "Expected SDK 10.0.402 or compatible 10.0.4xx: $VERSION"; exit 1; }
echo "Project: $ROOT"
echo "Dotnet: $DOTNET ($VERSION)"
echo "NuGet: $NUGET_PACKAGES"
if [[ "$ACTION" != build && "$ACTION" != test ]]; then
  [[ -n "$GODOT" ]] || { echo "Godot .NET missing. Run setup.py --download or set FRUITDEFENSE_GODOT."; exit 1; }
  ENGINE_VERSION="$("$GODOT" --version)"
  [[ "$ENGINE_VERSION" == 4.7.2*mono* ]] || { echo "Expected Godot .NET 4.7.2: $ENGINE_VERSION"; exit 1; }
  echo "Godot: $GODOT ($ENGINE_VERSION)"
fi
case "$ACTION" in
  doctor) ;;
  build) "$DOTNET" build "$ROOT/game/FruitDefense.csproj" --configuration Debug --nologo ;;
  test) "$DOTNET" run --project "$ROOT/tests/DomainChecks/DomainChecks.csproj" -- "$ROOT/game/config/prototype.json" "$ROOT/06_预览与测试/试玩记录/domain-checks-cloud.json" ;;
  import) "$GODOT" --headless --editor --path "$ROOT/game" --import ;;
  play|editor|test-ui)
    if [[ "$(uname -s)" == Linux && -z "$(printenv DISPLAY || true)" && -z "$(printenv WAYLAND_DISPLAY || true)" ]]; then
      echo "No graphical session. Use build/test/import, or configure a display before graphical checks."
      exit 1
    fi
    "$DOTNET" build "$ROOT/game/FruitDefense.csproj" --configuration Debug --nologo
    if [[ "$ACTION" == editor ]]; then
      "$GODOT" --editor --path "$ROOT/game"
    elif [[ "$ACTION" == test-ui ]]; then
      "$GODOT" --path "$ROOT/game" --resolution 450x800 -- --integration="$ROOT/06_预览与测试/试玩记录/ui-checks-cloud.json"
    else
      "$GODOT" --path "$ROOT/game"
    fi ;;
  *) echo "Usage: bash tools/project.sh setup|doctor|build|test|import|play|editor|test-ui"; exit 2 ;;
esac
