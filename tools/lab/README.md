# XG lab

Runs the real XG application against a local fake IRC network with scripted
XDCC bots, so downloads and the Prowlarr/Sonarr/Radarr APIs can be tested
without real IRC networks. Bots can be told to misbehave the way real bots do
(refused DCC ports, passive DCC, flaky port forwarding, late listeners).

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

The lab API key is `0b1f5e2a-6c3d-4e7f-9a8b-1c2d3e4f5a6b` (override with
`XG_LAB_API_KEY`), so the Newznab and SABnzbd APIs can be driven with curl
exactly like Prowlarr, Sonarr and Radarr do.

Files:

- `fakeirc.py` – IRC server and iroffer-like bots; see the header for bot modes
- `scenarios/*.json` – bot setups
- `XgLab.cs` – starts XG like XG.Application, seeds the lab server and API key
- `kernel32shim.c` – lets the NuGet db4o build run on Linux Mono
- `build.sh`, `run.sh`, `stop.sh`

On the very first start XG may crash while creating `xgsnapshots.db`
(an existing issue in the RRD code); starting it again works.
