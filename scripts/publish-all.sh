#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIG="${CONFIG:-Release}"
ARTIFACTS_DIR="$ROOT/artifacts"
IFS=' ' read -r -a RID_LIST <<< "${RIDS:-linux-x64 linux-arm64 osx-arm64 win-x64}"

# Determine version from env or project file
VERSION="${VERSION:-}"
if [[ -z "$VERSION" ]]; then
  VERSION=$(grep -m1 -oE '<Version>[^<]+' "$ROOT/src/DukascopyDownloader.Cli/DukascopyDownloader.Cli.csproj" | sed 's/<Version>//') || true
fi
VERSION=${VERSION:-dev}

rm -rf "$ARTIFACTS_DIR"
mkdir -p "$ARTIFACTS_DIR"

for rid in "${RID_LIST[@]}"; do
  echo "==> Publishing $rid (version $VERSION)"
  dotnet publish "$ROOT/src/DukascopyDownloader.Cli/DukascopyDownloader.Cli.csproj" \
    -c "$CONFIG" \
    -r "$rid" \
    --self-contained true \
    /p:DebugType=none

  publish_dir="$ROOT/src/DukascopyDownloader.Cli/bin/$CONFIG/net9.0/$rid/publish"
  stage_dir="$ARTIFACTS_DIR/stage-$rid"
  mkdir -p "$stage_dir"

  asset_base="dukascopy-downloader-${VERSION}-${rid}"
  if [[ "$rid" == win-* ]]; then
    cp "$publish_dir/dukascopy-downloader.exe" "$stage_dir/"
    asset_path="$ARTIFACTS_DIR/${asset_base}.zip"
  else
    cp "$publish_dir/dukascopy-downloader" "$stage_dir/"
    chmod +x "$stage_dir/dukascopy-downloader"
    asset_path="$ARTIFACTS_DIR/${asset_base}.tar.gz"
  fi

  for extra in README.md LICENSE; do
    if [[ -f "$ROOT/$extra" ]]; then
      cp "$ROOT/$extra" "$stage_dir/"
    fi
  done

  pushd "$stage_dir" >/dev/null
  if [[ "$asset_path" == *.zip ]]; then
    zip -q -r "$asset_path" .
  else
    tar -czf "$asset_path" .
  fi
  popd >/dev/null

  rm -rf "$stage_dir"
  echo "   -> $asset_path"
done

echo "Artifacts ready under $ARTIFACTS_DIR"
