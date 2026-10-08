#!/bin/sh
# R.A.D.A.R. installer for macOS and Linux.
#
#   curl -fsSL https://raw.githubusercontent.com/lookashdev/radar-releases/main/install.sh | sh
#
# What it does, and nothing else:
#   1. picks the archive for this system from the latest release (or RADAR_VERSION=0.1.0),
#   2. downloads it and SHA256SUMS.txt, and stops when the checksum does not match,
#   3. unpacks it into ~/.radar/app (replacing a previous copy),
#   4. links it as ~/.local/bin/radar.
# It needs no sudo and does not touch your shell configuration. To remove R.A.D.A.R., delete ~/.radar and ~/.local/bin/radar.
#
# Environment (all optional): RADAR_VERSION, RADAR_HOME (default ~/.radar), RADAR_BIN_DIR (default ~/.local/bin),
# RADAR_REPO (default lookashdev/radar-releases), RADAR_BASE_URL (a mirror or a test server; replaces the GitHub download URL).

# the whole script lives in a function that is called on the last line, so a download that is cut short never runs half of it
main() {
  set -eu

  REPO="${RADAR_REPO:-lookashdev/radar-releases}"
  HOME_DIR="${RADAR_HOME:-$HOME/.radar}"
  BIN_DIR="${RADAR_BIN_DIR:-$HOME/.local/bin}"

  say() { printf '%s\n' "$*"; }
  die() { printf 'R.A.D.A.R. installer: %s\n' "$*" >&2; exit 1; }

  case "$(uname -s)" in
    Darwin) os=osx ;;
    Linux) os=linux ;;
    *) die "this installer is for macOS and Linux; on Windows use install.ps1" ;;
  esac
  case "$(uname -m)" in
    arm64|aarch64) arch=arm64 ;;
    x86_64|amd64) arch=x64 ;;
    *) die "unsupported processor: $(uname -m)" ;;
  esac
  [ "$os" = linux ] && [ "$arch" != x64 ] && die "Linux builds exist for x86-64 only"

  if [ "$os" = linux ]; then file="radar-$os-$arch.tar.gz"; else file="radar-$os-$arch.zip"; fi

  if [ -n "${RADAR_BASE_URL:-}" ]; then
    base="$RADAR_BASE_URL"
  elif [ -n "${RADAR_VERSION:-}" ]; then
    base="https://github.com/$REPO/releases/download/v${RADAR_VERSION#v}"
  else
    base="https://github.com/$REPO/releases/latest/download"
  fi

  if command -v curl >/dev/null 2>&1; then
    fetch() { curl -fsSL --retry 2 -o "$2" "$1"; }
  elif command -v wget >/dev/null 2>&1; then
    fetch() { wget -q -O "$2" "$1"; }
  else
    die "needs curl or wget"
  fi

  if command -v sha256sum >/dev/null 2>&1; then
    sha() { sha256sum "$1" | cut -d ' ' -f 1; }
  elif command -v shasum >/dev/null 2>&1; then
    sha() { shasum -a 256 "$1" | cut -d ' ' -f 1; }
  else
    die "needs sha256sum or shasum to check the download"
  fi

  tmp="$(mktemp -d)"
  trap 'rm -rf "$tmp"' EXIT INT TERM

  say "Downloading $file ..."
  fetch "$base/$file" "$tmp/$file" || die "could not download $base/$file"
  fetch "$base/SHA256SUMS.txt" "$tmp/SHA256SUMS.txt" || die "could not download the checksums"

  want="$(awk -v f="$file" '{ n = $2; sub(/^\*/, "", n); if (n == f) print $1 }' "$tmp/SHA256SUMS.txt")"
  [ -n "$want" ] || die "$file is not listed in SHA256SUMS.txt"
  got="$(sha "$tmp/$file")"
  [ "$want" = "$got" ] || die "checksum mismatch for $file (expected $want, got $got); nothing was installed"
  say "Checksum OK."

  mkdir -p "$tmp/x"
  if [ "$os" = linux ]; then
    tar -xzf "$tmp/$file" -C "$tmp/x"
  else
    command -v unzip >/dev/null 2>&1 || die "needs unzip"
    unzip -q "$tmp/$file" -d "$tmp/x"
  fi
  [ -x "$tmp/x/radar/radar" ] || die "the archive does not contain radar/radar"

  mkdir -p "$HOME_DIR" "$BIN_DIR"
  rm -rf "$HOME_DIR/app"
  mv "$tmp/x/radar" "$HOME_DIR/app"
  # a file fetched by curl is not quarantined, but one unpacked from a browser download may be
  [ "$os" = osx ] && xattr -dr com.apple.quarantine "$HOME_DIR/app" 2>/dev/null || true
  ln -sf "$HOME_DIR/app/radar" "$BIN_DIR/radar"

  say ""
  say "Installed in $HOME_DIR/app"
  say "Start it with:  $BIN_DIR/radar"
  case ":$PATH:" in
    *":$BIN_DIR:"*) say "(\"radar\" alone works, $BIN_DIR is on your PATH.)" ;;
    *) say "To start it with just \"radar\", add this line to your shell profile:  export PATH=\"$BIN_DIR:\$PATH\"" ;;
  esac
  say "It opens http://127.0.0.1:5178 in your browser. Press Ctrl+C in the terminal to quit."
}

main "$@"
