#!/usr/bin/env bash
# Resume checks:
#  - a transfer from one bot breaks, the same file is then taken from another
#    bot, whose first transfer breaks as well
#  - a bot breaks a transfer early (less than the rollback of 500 KB), and the
#    retry, which starts over, breaks again shortly before the end
#
#   tools/lab/check-resume.sh <xg build dir>
#
# Expected: XG resumes (DCC RESUME) instead of starting over, and the finished
# file is complete and byte-identical to what the bots serve.
set -euo pipefail
XG="$1"
LAB="$(cd "$(dirname "$0")" && pwd)"
STATE="$(mktemp -d)"
NAME="Resume.Show.S01E01.720p.mkv"
NAME2="Resume.Movie.2014.1080p.mkv"
trap '"$LAB/stop.sh" "$STATE"' EXIT

started() {
	for i in $(seq 1 30); do
		grep -q "LAB started" "$STATE/xg.log" 2>/dev/null && return 0
		sleep 1
	done
	return 1
}
# waits for the n-th line of the xg log matching a pattern
wait_log() {
	for i in $(seq 1 "$3"); do
		[[ $(grep -c "$1" "$STATE/xg.log" || true) -ge $2 ]] && return 0
		sleep 1
	done
	echo "FAIL: no '$1' in the log"; return 1
}
"$LAB/run.sh" "$XG" "$LAB/scenarios/resume.json" "$STATE" >/dev/null
if ! started; then
	"$LAB/stop.sh" "$STATE"; "$LAB/run.sh" "$XG" "$LAB/scenarios/resume.json" "$STATE" >/dev/null
	started || { echo "FAIL: XG did not start"; tail -20 "$STATE/xg.log"; exit 1; }
fi
sleep 5

# bot A breaks after 2.5 of 3 MB, then it is given up on
echo "enable LAB-CUT-A 1" >>"$STATE/cmd"
wait_log "FinishWriting.*incomplete" 1 30
echo "disable LAB-CUT-A 1" >>"$STATE/cmd"
sleep 2

# the same file from bot B, which breaks once after 0.7 MB and then works
echo "enable LAB-CUT-B 1" >>"$STATE/cmd"
for i in $(seq 1 60); do
	sleep 1
	[[ -f "$STATE/home/.config/XG/dl/$NAME" ]] && break
done

# bot C: 300 KB, then 2.9 of 3 MB, then the rest
echo "enable LAB-CUT-C 1" >>"$STATE/cmd"
for i in $(seq 1 90); do
	sleep 1
	[[ -f "$STATE/home/.config/XG/dl/$NAME2" ]] && break
done
sleep 1

python3 - "$LAB" "$STATE" "$NAME" "$NAME2" <<'PY'
import json, os, sys
sys.path.insert(0, sys.argv[1])
from fakeirc import file_bytes
state = sys.argv[2]
events = [json.loads(l) for l in open(os.path.join(state, "irc.log")) if l.startswith("{")]
resumes = [e for e in events if e["event"] == "bot_resume" and e["bot"] == "LAB-CUT-B"]
checks = [("bot B was asked to resume the partial file of bot A", len(resumes) > 0 and resumes[0]["start"] > 0)]
for name in sys.argv[3:]:
    path = os.path.join(state, "home/.config/XG/dl", name)
    expected = file_bytes(name, 3145728)
    got = open(path, "rb").read() if os.path.exists(path) else None
    checks += [
        (name + " is finished", got is not None),
        (name + " is complete (%s of %d bytes)" % (len(got) if got else 0, len(expected)), got is not None and len(got) == len(expected)),
        (name + " is byte-identical", got == expected),
    ]
for text, ok in checks:
    print(("ok   " if ok else "FAIL ") + text)
sys.exit(0 if all(ok for _, ok in checks) else 1)
PY
