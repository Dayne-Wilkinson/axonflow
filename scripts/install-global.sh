#!/usr/bin/env bash
# Pack AxonFlow and install or update the global dotnet tool (axonflow on PATH).
# Run from the repository root.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CSPROJ="$ROOT/src/AxonFlow/AxonFlow.csproj"
if [[ ! -f "$CSPROJ" ]]; then
  echo "Expected csproj at $CSPROJ" >&2
  exit 1
fi

VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$CSPROJ" | head -1)"
if [[ -z "$VERSION" ]]; then
  echo "Could not read Version from $CSPROJ" >&2
  exit 1
fi

ARTIFACTS="$ROOT/artifacts"
mkdir -p "$ARTIFACTS"

echo "Packing AxonFlow $VERSION -> $ARTIFACTS"
dotnet pack "$CSPROJ" -c Release -o "$ARTIFACTS"

PKG="AxonFlow"
echo "Updating or installing global tool $PKG $VERSION (from $ARTIFACTS)"
if ! dotnet tool update --global "$PKG" --add-source "$ARTIFACTS" --version "$VERSION"; then
  echo "dotnet tool update failed; trying install (first-time or different feed)." >&2
  dotnet tool install --global "$PKG" --add-source "$ARTIFACTS" --version "$VERSION"
fi

echo "Done. Ensure ~/.dotnet/tools is on PATH, then run: axonflow --help"
