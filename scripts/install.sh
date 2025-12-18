#!/usr/bin/env bash
set -euo pipefail

REPO="${REPO:-cpcerrato/dukascopy-downloader}"
VERSION="${VERSION:-latest}"
INCLUDE_PRERELEASE=${INCLUDE_PRERELEASE:-false}
INSTALL_DIR="${INSTALL_DIR:-/usr/local/bin}"

require() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing dependency: $1" >&2
    exit 1
  }
}

require curl
require tar
require jq

uname_s=$(uname -s)
uname_m=$(uname -m)

case "$uname_s" in
  Linux) platform="linux" ;;
  Darwin) platform="osx" ;;
  *) echo "Unsupported platform: $uname_s" >&2; exit 1 ;;
esac

case "$uname_m" in
  x86_64|amd64) arch="x64" ;;
  arm64|aarch64) arch="arm64" ;;
  *) echo "Unsupported architecture: $uname_m" >&2; exit 1 ;;
esac

if [[ "$VERSION" == "latest" ]]; then
  if [[ "$INCLUDE_PRERELEASE" == "true" ]]; then
    release_tag=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases?per_page=1" | jq -r '.[0].tag_name // empty')
  else
    release_tag=$(curl -fsSL "https://api.github.com/repos/${REPO}/releases/latest" | jq -r '.tag_name // empty')
  fi
  if [[ -z "$release_tag" ]]; then
    echo "Unable to determine latest release tag for ${REPO}" >&2
    exit 1
  fi
  asset="dukascopy-downloader-${release_tag}-${platform}-${arch}.tar.gz"
  url="https://github.com/${REPO}/releases/download/${release_tag}/${asset}"
else
  asset="dukascopy-downloader-${VERSION}-${platform}-${arch}.tar.gz"
  url="https://github.com/${REPO}/releases/download/${VERSION}/${asset}"
fi

tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT

echo "Downloading $url"
curl -fsSL "$url" -o "$tmpdir/$asset"

tar -xzf "$tmpdir/$asset" -C "$tmpdir"

mkdir -p "$INSTALL_DIR"
chmod +x "$tmpdir/dukascopy-downloader"
cp "$tmpdir/dukascopy-downloader" "$INSTALL_DIR/"

echo "Installed to $INSTALL_DIR/dukascopy-downloader"
