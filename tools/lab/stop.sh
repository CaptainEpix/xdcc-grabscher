#!/usr/bin/env bash
# Stops what tools/lab/run.sh started.
STATE="$(realpath -m "$1")"
for p in $(pgrep -f "XgLab.exe" || true); do kill "$p" 2>/dev/null || true; done
[[ -f "$STATE/irc.pid" ]] && kill "$(cat "$STATE/irc.pid")" 2>/dev/null || true
# fake networks of earlier runs (matched by process, not by this script's own command line)
for p in $(pgrep -x python3 || true); do
	grep -q fakeirc.py "/proc/$p/cmdline" 2>/dev/null && kill "$p" 2>/dev/null || true
done
sleep 1
