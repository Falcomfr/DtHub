#!/usr/bin/env bash
# Installs or updates DT Hub on Linux (x86_64), from the latest release:
#
#   curl -fsSL https://dthub.app/install-linux.sh | bash
#
# Everything goes into the user's own folders, so no password is asked:
# the application in ~/.local/share/dthub-app, a "dthub" command in
# ~/.local/bin and an entry in the desktop's application menu. Its data
# (settings, logs, adb and scrcpy) stays apart, in ~/.local/share/DtHub,
# and an update never touches it. Run the same line again to update.
#
# The archive is checked against the checksum the release pipeline wrote
# (.github/workflows/livraison.yml) before anything is replaced.

set -euo pipefail

# Everything inside main, called on the last line: a "curl | bash" cut off
# midway runs nothing rather than half a script.
main() {
    local download="${DTHUB_DOWNLOAD:-https://github.com/Falcomfr/DtHub/releases/latest/download}"
    local archive="DtHub-linux-x64.tar.gz"
    local data="${XDG_DATA_HOME:-$HOME/.local/share}"
    local target="$data/dthub-app"

    [ "$(uname -s)" = Linux ] \
        || fail "Ce script installe DT Hub sur Linux." "This script installs DT Hub on Linux."
    [ "$(uname -m)" = x86_64 ] \
        || fail "DT Hub pour Linux demande un processeur 64 bits x86_64." \
                "DT Hub for Linux needs a 64-bit x86_64 processor."

    # Replacing the application under a running copy would leave it half
    # old, half new until it quits.
    if pgrep -x DtHub.Mac > /dev/null; then
        fail "Quittez DT Hub, puis relancez cette commande." "Quit DT Hub, then run this command again."
    fi

    work="$(mktemp -d)"
    trap 'rm -rf "$work"' EXIT

    say "Téléchargement de DT Hub… / Downloading DT Hub…"
    curl -fL --progress-bar -o "$work/$archive" "$download/$archive" \
        || fail "Le téléchargement a échoué." "The download failed."
    curl -fsSL -o "$work/$archive.sha256" "$download/$archive.sha256" \
        || fail "L'empreinte de l'archive n'a pas pu être téléchargée." "The archive's checksum could not be downloaded."

    # The checksum the release pipeline wrote, "<hash>  DtHub-linux-x64.tar.gz".
    (cd "$work" && sha256sum -c "$archive.sha256" > /dev/null 2>&1) \
        || fail "L'archive téléchargée est abîmée : rien n'a été installé." \
                "The downloaded archive is damaged: nothing was installed."

    # Unpacked beside the target, on the same disk, so the swap below is a
    # rename and not a copy.
    mkdir -p "$data"
    local fresh="$target.nouveau"
    rm -rf "$fresh"
    mkdir -p "$fresh"
    tar -xzf "$work/$archive" -C "$fresh" --strip-components=1
    [ -x "$fresh/DtHub.Mac" ] || fail "L'archive ne contient pas DT Hub." "The archive does not hold DT Hub."

    # The old copy steps aside until the new one is in place: a move that
    # fails leaves the old one, never none.
    local aside="$target.ancien"
    rm -rf "$aside"
    if [ -d "$target" ]; then
        mv "$target" "$aside"
    fi

    if ! mv "$fresh" "$target"; then
        [ -d "$aside" ] && mv "$aside" "$target"
        fail "DT Hub n'a pas pu être remplacé : l'ancienne version reste." \
             "DT Hub could not be replaced: the old version stays."
    fi

    rm -rf "$aside"

    mkdir -p "$HOME/.local/bin" "$data/applications"
    ln -sfn "$target/DtHub.Mac" "$HOME/.local/bin/dthub"

    # StartupWMClass: the windows' X11 class is the executable's name, which ties them to this entry's icon.
    cat > "$data/applications/dthub.desktop" <<EOF
[Desktop Entry]
Type=Application
Name=DT Hub
Comment=DOFUS Touch multi-account, from your own Android phones
Exec="$target/DtHub.Mac"
Icon=$target/app.png
StartupWMClass=DtHub.Mac
Terminal=false
Categories=Game;
EOF

    say "" "DT Hub est installé : menu des applications, ou commande « dthub »." \
           "DT Hub is installed: application menu, or the \"dthub\" command."
}

say() { printf '%s\n' "$@"; }
fail() { say "" "$@" >&2; exit 1; }

main "$@"
