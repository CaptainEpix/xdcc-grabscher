#!/usr/bin/env bash
# End-to-end check of a lab build through the Newznab and SABnzbd APIs, the way
# Prowlarr/Sonarr/Radarr use them, against a good, a flaky and a refusing bot.
#
#   tools/lab/check.sh <xg build dir>
#
# Expected: good and flaky downloads complete, the refusing bot fails cleanly,
# and grabbing the good release again completes quickly under a new name.
set -euo pipefail
XG="$1"
LAB="$(cd "$(dirname "$0")" && pwd)"
STATE="$(mktemp -d)"
K="0b1f5e2a-6c3d-4e7f-9a8b-1c2d3e4f5a6b"
U="http://127.0.0.1:15556"
export NO_PROXY=127.0.0.1 no_proxy=127.0.0.1
trap '"$LAB/stop.sh" "$STATE"' EXIT

ready() {
	for i in $(seq 1 30); do
		curl -sS -m 2 -o /dev/null "$U/sabnzbd/api?mode=version" 2>/dev/null && return 0
		sleep 1
	done
	return 1
}
"$LAB/run.sh" "$XG" "$LAB/scenarios/arr.json" "$STATE" >/dev/null
if ! ready; then
	# the first start can crash creating xgsnapshots.db, the second works
	"$LAB/stop.sh" "$STATE"; "$LAB/run.sh" "$XG" "$LAB/scenarios/arr.json" "$STATE" >/dev/null
	ready || { echo "FAIL: XG did not start"; tail -20 "$STATE/xg.log"; exit 1; }
fi
sleep 5

grab() {
	local link
	link=$(curl -sS "$U/newznab/api?t=search&q=$1&apikey=$K" | grep -o '<link>[^<]*t=get[^<]*' | head -1 | sed 's/<link>//; s/&amp;/\&/g' || true)
	[[ -n "$link" ]] || { echo "FAIL: no search result for $1"; exit 1; }
	curl -sS -o "$STATE/grab.nzb" "$link"
	curl -sS -F "name=@$STATE/grab.nzb" "$U/sabnzbd/api?mode=addfile&cat=$2&apikey=$K&output=json" | grep -q '"status":true' || { echo "FAIL: addfile $1"; exit 1; }
}
grab "good%20show" tv
grab "flaky%20movie" movies
grab "refuse%20show" tv

for i in $(seq 1 40); do
	sleep 3
	history=$(curl -sS "$U/sabnzbd/api?mode=history&apikey=$K")
	[[ $(grep -o '"status":"\(Completed\|Failed\)"' <<<"$history" | wc -l) -ge 3 ]] && break
done

python3 - "$history" <<'PY'
import json, sys
slots = {s["name"]: s for s in json.loads(sys.argv[1])["history"]["slots"]}
expected = {
    "Good.Show.S01E01.720p.mkv": "Completed",
    "Flaky.Movie.2014.1080p.mkv": "Completed",
    "Refuse.Show.S01E01.720p.mkv": "Failed",
}
ok = True
for name, status in expected.items():
    got = slots.get(name, {}).get("status", "missing")
    print(("ok   " if got == status else "FAIL ") + name + ": " + got + " (expected " + status + ")")
    ok = ok and got == status
sys.exit(0 if ok else 1)
PY

# grabbing the same release again must not wait for the old request timer
# and must not replace the first file
grab "good%20show" tv
for i in $(seq 1 15); do
	sleep 3
	history=$(curl -sS "$U/sabnzbd/api?mode=history&apikey=$K")
	[[ $(grep -o '"status":"\(Completed\|Failed\)"' <<<"$history" | wc -l) -ge 4 ]] && break
done
python3 - "$history" <<'PY'
import json, sys
paths = sorted(s["storage"] for s in json.loads(sys.argv[1])["history"]["slots"]
               if s["name"] == "Good.Show.S01E01.720p.mkv" and s["status"] == "Completed")
ok = len(paths) == 2 and paths[0].endswith("Good.Show.S01E01.720p (1).mkv")
print(("ok   " if ok else "FAIL ") + "second grab of Good.Show.S01E01.720p.mkv: " + ", ".join(paths))
sys.exit(0 if ok else 1)
PY
