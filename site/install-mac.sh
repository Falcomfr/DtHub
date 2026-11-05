#!/bin/bash
# Installs or updates DT Hub on a Mac:
#
#   curl -fsSL https://dthub.app/install-mac.sh | bash
#
# Downloaded by curl, the application carries no quarantine mark, so
# Gatekeeper lets it open without a Developer ID (docs/LIVRAISON.md, "The
# Mac"). The same line updates it: it replaces the installed copy with the
# latest release, settings and accounts untouched (they live in
# ~/Library/Application Support/DtHub).
#
# DTHUB_DOWNLOAD overrides where the archive comes from, to try the script
# against a local build.

set -euo pipefail

download="${DTHUB_DOWNLOAD:-https://github.com/Falcomfr/DtHub/releases/latest/download}"
archive="DtHub-mac.zip"
app="DT Hub.app"

say() { printf '%s\n' "$@"; }
fail() { say "" "$@" >&2; exit 1; }

[ "$(uname -s)" = Darwin ] \
    || fail "Ce script installe DT Hub sur un Mac." "This script installs DT Hub on a Mac."
[ "$(uname -m)" = arm64 ] \
    || fail "DT Hub demande un Mac à puce Apple (M1 ou plus récent)." \
            "DT Hub needs a Mac with Apple silicon (M1 or later)."
[ "$(sw_vers -productVersion | cut -d. -f1)" -ge 14 ] \
    || fail "DT Hub demande macOS 14 (Sonoma) ou plus récent." "DT Hub needs macOS 14 (Sonoma) or later."

# Replacing the application under a running copy would leave it half old,
# half new until it quits.
if pgrep -xq DtHub.Mac; then
    fail "Quittez DT Hub, puis relancez cette commande." "Quit DT Hub, then run this command again."
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

say "Téléchargement de DT Hub… / Downloading DT Hub…"
curl -fL --progress-bar -o "$work/$archive" "$download/$archive" \
    || fail "Le téléchargement a échoué." "The download failed."
curl -fsSL -o "$work/$archive.sha256" "$download/$archive.sha256" \
    || fail "L'empreinte de l'archive n'a pas pu être téléchargée." "The archive's checksum could not be downloaded."

# The checksum the release pipeline wrote, "<hash>  DtHub-mac.zip".
(cd "$work" && shasum -a 256 -c "$archive.sha256" > /dev/null) \
    || fail "L'archive téléchargée est abîmée : rien n'a été installé." \
            "The downloaded archive is damaged: nothing was installed."

ditto -x -k "$work/$archive" "$work/out"
[ -d "$work/out/$app" ] || fail "L'archive ne contient pas DT Hub." "The archive does not hold DT Hub."

# /Applications when it can be written to, as on most Macs; the user's own
# Applications folder otherwise, which needs no password.
target=/Applications
if [ ! -w "$target" ]; then
    target="$HOME/Applications"
    mkdir -p "$target"
fi

rm -rf "${target:?}/$app"
mv "$work/out/$app" "$target/"

# Nothing marks a file curl wrote, but a mark copied along by a proxy or an
# archive tool would bring Gatekeeper's refusal back.
xattr -dr com.apple.quarantine "$target/$app" 2> /dev/null || true

say "" "DT Hub est installé dans $target. / DT Hub is installed in $target."
open "$target/$app"
