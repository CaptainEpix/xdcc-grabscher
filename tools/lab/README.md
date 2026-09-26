# XG lab

Runs the real XG application against a local fake IRC network with scripted
XDCC bots, so downloads and the Prowlarr/Sonarr/Radarr APIs can be tested
without real IRC networks. Bots can be told to misbehave the way real bots do
(refused DCC ports, passive DCC, flaky port forwarding, late listeners,
dropped transfers, never answering); they support DCC RESUME like iroffer,
also for passive transfers. Lab builds run with passive DCC on ports
15600-15604 (`XG_PASSIVE_DCC_PORTS=` turns it off).

Requirements: Mono (`mono-devel`, `mono-xbuild`), `gcc`, Python 3 and access to
`api.nuget.org`. No Docker needed.

```bash
# build any git ref into a runnable directory
tools/lab/build.sh v3.3.2.0-mono2026 /tmp/xg-332
tools/lab/build.sh HEAD /tmp/xg-head

# start the fake network and XG (web UI and APIs on http://127.0.0.1:15556)
tools/lab/run.sh /tmp/xg-head tools/lab/scenarios/bots.json /tmp/lab-state

# click a packet, like in the web UI
echo "enable LAB-GOOD 1" >> /tmp/lab-state/cmd
echo "packets" >> /tmp/lab-state/cmd

# watch what happened
grep -v SignalR /tmp/lab-state/xg.log | tail   # XG
tail /tmp/lab-state/irc.log                    # bots (JSON events)

tools/lab/stop.sh /tmp/lab-state
```

`tools/lab/check.sh <build dir>` runs the whole Prowlarr/Sonarr path (search,
NZB, addfile, download, history) against a good, a flaky, a refusing, a mute, a
passive and a silent bot and fails unless the downloads complete, the refusing
and the silent bot fail cleanly and a second grab of the same release completes
quickly under a new name.

`tools/lab/torture.py <build dir>` (about 15 minutes) grabs 27 releases at once
from 17 bots (slow, flaky, dropping, late, passive, silent, broken offers,
awkward and hostile file names) while other clients hammer the API, kills XG and
then the IRC network in the middle of transfers, and sends garbage to both APIs.
Every file must end up byte-identical, every job completed or failed, and XG must
survive all of it.

`tools/lab/check-resume.sh <build dir>` breaks transfers midway, within one bot,
across two bots offering the same file and with a passive bot, and fails unless
XG resumes and the finished files are complete and byte-identical.

The lab API key is `0b1f5e2a-6c3d-4e7f-9a8b-1c2d3e4f5a6b` (override with
`XG_LAB_API_KEY`), so the Newznab and SABnzbd APIs can be driven with curl
exactly like Prowlarr, Sonarr and Radarr do.

Files:

- `fakeirc.py` – IRC server and iroffer-like bots; see the header for bot modes
- `scenarios/*.json` – bot setups
- `XgLab.cs` – starts XG like XG.Application, seeds the lab server and API key
- `kernel32shim.c` – lets the NuGet db4o build run on Linux Mono
- `build.sh`, `run.sh`, `stop.sh`, `check.sh`, `check-resume.sh`, `torture.py`

Builds before the first-start fix may crash once while creating `xgsnapshots.db`
(an existing issue in the RRD code); starting them again works.
