#!/usr/bin/env bash
# Installs or updates Charmera Importer on Arch Linux (and derivatives) as a pacman package,
# so it shows up in your app menu and uninstalls cleanly.
#
#   curl -fsSL https://raw.githubusercontent.com/RickCaleg/charmera-importer/main/packaging/arch/install.sh | bash
#
# Options:
#   --version X.Y.Z   install that release instead of the latest
#   --uninstall       remove the package (your photos and settings are kept)
#
# Run it again at any time to update. It builds the package from the official release
# tarball, whose checksum is checked against the release's SHA256SUMS.
set -euo pipefail

repo="RickCaleg/charmera-importer"
package="charmera-importer-bin"
version=""

usage() {
  cat <<'USAGE'
Installs or updates Charmera Importer on Arch Linux as a pacman package.

Usage: install.sh [--version X.Y.Z] [--uninstall]
  --version X.Y.Z   install that release instead of the latest
  --uninstall       remove the package (your photos and settings are kept)
USAGE
}

die() { echo "error: $*" >&2; exit 1; }
info() { echo "==> $*"; }

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) version="${2:-}"; shift 2 ;;
    --uninstall)
      pacman -Q "$package" >/dev/null 2>&1 || { info "Charmera Importer is not installed."; exit 0; }
      sudo pacman -R "$package"
      exit 0 ;;
    -h|--help) usage; exit 0 ;;
    *) die "unknown option: $1" ;;
  esac
done

command -v pacman >/dev/null && command -v makepkg >/dev/null || die "this installer is for Arch Linux (pacman + makepkg)."
[[ $EUID -ne 0 ]] || die "run it as your normal user; it asks for sudo only to install the package."
[[ "$(uname -m)" == "x86_64" ]] || die "only x86_64 is supported."
command -v curl >/dev/null || die "curl is required (sudo pacman -S curl)."

if [[ -z "$version" ]]; then
  info "Looking up the latest release..."
  version="$(curl -fsSL "https://api.github.com/repos/${repo}/releases/latest" \
    | sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' | head -n1)"
  [[ -n "$version" ]] || die "could not determine the latest version (GitHub API unreachable or rate-limited)."
fi
version="${version#v}"

installed="$(pacman -Q "$package" 2>/dev/null | awk '{print $2}' | cut -d- -f1 || true)"
if [[ "$installed" == "$version" ]]; then
  info "Charmera Importer ${version} is already installed."
  exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

info "Fetching checksums for ${version}..."
curl -fsSL -o "$work/SHA256SUMS" "https://github.com/${repo}/releases/download/v${version}/SHA256SUMS" \
  || die "release v${version} not found."
tarball="charmera-importer-${version}-linux-x64.tar.gz"
sha="$(awk -v f="$tarball" '$2 == f || $2 == "*" f { print $1 }' "$work/SHA256SUMS")"
[[ "$sha" =~ ^[0-9a-f]{64}$ ]] || die "no checksum for ${tarball} in the release's SHA256SUMS."

# Use the PKGBUILD next to this script when it runs from a checkout; when piped from curl
# there's no script file, so fetch it (CHARMERA_PKGBUILD_URL overrides, e.g. to test a fork).
script="${BASH_SOURCE[0]:-}"
if [[ -n "$script" && -f "$script" && -f "$(dirname "$script")/PKGBUILD" ]]; then
  cp "$(dirname "$script")/PKGBUILD" "$work/PKGBUILD"
else
  pkgbuild_url="${CHARMERA_PKGBUILD_URL:-https://raw.githubusercontent.com/${repo}/main/packaging/arch/PKGBUILD}"
  curl -fsSL -o "$work/PKGBUILD" "$pkgbuild_url" || die "could not download the PKGBUILD from ${pkgbuild_url}."
fi
sed -i \
  -e "s/^pkgver=.*/pkgver=${version}/" \
  -e "s/^pkgrel=.*/pkgrel=1/" \
  -e "s/^sha256sums=.*/sha256sums=('${sha}')/" \
  "$work/PKGBUILD"

info "Building and installing ${package} ${version}${installed:+ (replacing ${installed})}..."
# --noconfirm: when piped from curl, stdin isn't a terminal for pacman's prompt; sudo still
# asks for the password on the terminal.
(cd "$work" && makepkg --syncdeps --install --noconfirm --needed)

info "Done. Launch \"Charmera Importer\" from your app menu, or run: charmera-importer"
