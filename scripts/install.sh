#!/usr/bin/env bash
set -euo pipefail

REPO="${REPO:-cpcerrato/dukascopy-downloader}"
VERSION="${VERSION:-latest}"
INSTALL_DIR="${INSTALL_DIR:-/usr/local/bin}"

require() {
  command -v "$1" >/dev/null 2>&1 || {
    echo "Missing dependency: $1" >&2
    exit 1
  }
}

require curl
require tar

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

asset="dukascopy-downloader-${platform}-${arch}.tar.gz"
if [[ "$VERSION" == "latest" ]]; then
  url="https://github.com/${REPO}/releases/latest/download/${asset}"
else
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
