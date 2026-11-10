#!/usr/bin/env bash
# A player's first start of the Linux build, on the virtual screen that
# xvfb-run gives: forty seconds later DT Hub must still be running, show its
# panel and the pairing window, and have logged no error. Still running was
# not enough: a startup that throws shows a warning and waits for a click
# that never comes, and a fault on the UI thread is only logged.
#
#   xvfb-run -a bash .github/linux-first-start.sh <path to DtHub.Mac>

set -u

app="$1"
logs="${XDG_DATA_HOME:-$HOME/.local/share}/DtHub/logs"
failed=""

"$app" &
pid=$!
sleep 40

if kill -0 "$pid" 2> /dev/null; then
    windows="$(xdotool search --onlyvisible --name '.' getwindowname %@ 2> /dev/null)"
    printf 'Windows on screen:\n%s\n' "$windows"

    grep -qx 'DT Hub' <<< "$windows" || failed="The panel is not on screen."
    [ "$(grep -c . <<< "$windows")" -ge 2 ] || failed="${failed:-The pairing window is not on screen.}"

    kill "$pid"
    wait "$pid" 2> /dev/null
else
    wait "$pid"
    failed="DT Hub ended on its own with code $?."
fi

if grep -E '\[(ERR|FTL)\]' "$logs"/*.log; then
    failed="${failed:-Errors in the log.}"
fi

if [ -n "$failed" ]; then
    echo "$failed"
    cat "$logs"/*.log
    exit 1
fi

echo "DT Hub started: its panel and the pairing window on screen, no error in the log."
