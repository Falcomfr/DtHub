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

# Everything inside main, called on the last line: a "curl | bash" cut off
# midway runs nothing rather than half a script.
main() {
    local download="${DTHUB_DOWNLOAD:-https://github.com/Falcomfr/DtHub/releases/latest/download}"
    local archive="DtHub-mac.zip"
    local app="DT Hub.app"

    [ "$(uname -s)" = Darwin ] \
        || fail "Ce script installe DT Hub sur un Mac." "This script installs DT Hub on a Mac."
    [ "$(uname -m)" = arm64 ] \
        || fail "DT Hub demande un Mac à puce Apple (M1 ou plus récent)." \
                "DT Hub needs a Mac with Apple silicon (M1 or later)."
    [ "$(sw_vers -productVersion | cut -d. -f1)" -ge 14 ] \
        || fail "DT Hub demande macOS 14 (Sonoma) ou plus récent." "DT Hub needs macOS 14 (Sonoma) or later."

    # Replacing the application under a running copy would leave it half
    # old, half new until it quits.
    if pgrep -xq DtHub.Mac; then
        fail "Quittez DT Hub, puis relancez cette commande." "Quit DT Hub, then run this command again."
    fi

    # Where a copy already is, so an update never leaves two of them; for a
    # first install, /Applications when it can be written to, as on most
    # Macs, the user's own Applications folder otherwise, which needs no
    # password.
    local target
    if [ -d "/Applications/$app" ]; then
        target=/Applications
    elif [ -d "$HOME/Applications/$app" ]; then
        target="$HOME/Applications"
    elif [ -w /Applications ]; then
        target=/Applications
    else
        target="$HOME/Applications"
    fi

    [ -w "$target" ] || [ ! -e "$target" ] \
        || fail "DT Hub est dans $target, où ce compte ne peut pas écrire." \
                "DT Hub is in $target, which this account cannot write to."
    mkdir -p "$target"

    work="$(mktemp -d)"
    trap 'rm -rf "$work"' EXIT

    say "Téléchargement de DT Hub… / Downloading DT Hub…"
    curl -fL --progress-bar -o "$work/$archive" "$download/$archive" \
        || fail "Le téléchargement a échoué." "The download failed."
    curl -fsSL -o "$work/$archive.sha256" "$download/$archive.sha256" \
        || fail "L'empreinte de l'archive n'a pas pu être téléchargée." "The archive's checksum could not be downloaded."

    # The checksum the release pipeline wrote, "<hash>  DtHub-mac.zip".
    (cd "$work" && shasum -a 256 -c "$archive.sha256" > /dev/null 2>&1) \
        || fail "L'archive téléchargée est abîmée : rien n'a été installé." \
                "The downloaded archive is damaged: nothing was installed."

    ditto -x -k "$work/$archive" "$work/out"
    [ -d "$work/out/$app" ] || fail "L'archive ne contient pas DT Hub." "The archive does not hold DT Hub."

    # The old copy steps aside, beside it on the same disk, until the new
    # one is in place: a move that fails leaves the old one, never none.
    local aside="$target/.$app.ancien"
    rm -rf "$aside"
    if [ -d "$target/$app" ]; then
        mv "$target/$app" "$aside"
    fi

    if ! mv "$work/out/$app" "$target/$app"; then
        [ -d "$aside" ] && mv "$aside" "$target/$app"
        fail "DT Hub n'a pas pu être remplacé : l'ancienne version reste." \
             "DT Hub could not be replaced: the old version stays."
    fi

    rm -rf "$aside"

    # Nothing marks a file curl wrote, but a mark copied along by a proxy or
    # an archive tool would bring Gatekeeper's refusal back.
    xattr -dr com.apple.quarantine "$target/$app" 2> /dev/null || true

    say "" "DT Hub est installé dans $target. / DT Hub is installed in $target."
    open "$target/$app"
}

say() { printf '%s\n' "$@"; }
fail() { say "" "$@" >&2; exit 1; }

main "$@"
