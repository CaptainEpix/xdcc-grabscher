#!/usr/bin/env bash
# Starts the fake IRC network and a lab build of XG against it.
#
#   tools/lab/run.sh <xg build dir> <scenario.json> <state dir>
#
# XG's web UI and APIs listen on port 15556, the API key is printed at start.
# Logs: <state dir>/irc.log (JSON events of the fake network), <state dir>/xg.log.
# Commands (like clicking in the web UI): echo "enable LAB-GOOD 1" >> <state dir>/cmd
# Stop with: tools/lab/stop.sh <state dir>
set -euo pipefail
XG="$(realpath "$1")"
SCENARIO="$(realpath "$2")"
STATE="$(realpath -m "$3")"
LAB="$(cd "$(dirname "$0")" && pwd)"
API_KEY="${XG_LAB_API_KEY:-0b1f5e2a-6c3d-4e7f-9a8b-1c2d3e4f5a6b}"
IRC_PORT="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1])).get("port",16667))' "$SCENARIO")"
CHANNEL="$(python3 -c 'import json,sys;print(json.load(open(sys.argv[1])).get("channel","#lab"))' "$SCENARIO")"

mkdir -p "$STATE/home"
python3 -u "$LAB/fakeirc.py" "$SCENARIO" >"$STATE/irc.log" 2>&1 &
echo $! >"$STATE/irc.pid"
sleep 1
if ! kill -0 "$(cat "$STATE/irc.pid")" 2>/dev/null; then
	echo "fake IRC network did not start (port $IRC_PORT in use by an earlier run?):" >&2
	cat "$STATE/irc.log" >&2
	exit 1
fi

touch "$STATE/cmd"
cd "$XG"
(tail -n 0 -f "$STATE/cmd" | HOME="$STATE/home" XDG_CONFIG_HOME="$STATE/home/.config" \
	mono XgLab.exe 127.0.0.1 "$IRC_PORT" "$CHANNEL" "$API_KEY" 15556 >"$STATE/xg.log" 2>&1) &
echo $! >"$STATE/xg.pid"

for i in $(seq 1 120); do
	grep -q "LAB started" "$STATE/xg.log" 2>/dev/null && break
	sleep 0.5
done
echo "XG $(cat "$XG/REVISION") running, API key $API_KEY"
