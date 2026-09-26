#!/usr/bin/env python3
"""
Torture test for a lab build of XG, driven through the Newznab and SABnzbd APIs
like Prowlarr/Sonarr/Radarr, with tools/lab/scenarios/torture.json:

  load      26 jobs at once from 16 bots (slow, flaky, dropping, late, passive,
            awkward file names, malformed offers, a hostile file name) while
            other clients hammer the API
  restart   XG is killed in the middle of several transfers and started again
  ircdown   the IRC network dies in the middle of transfers and comes back
  fuzz      garbage parameters for both APIs

  tools/lab/torture.py <xg build dir>

Every finished file must be byte-identical, every job must end up completed or
failed (never stuck), the API must never answer with a server error and XG must
survive all of it.
"""

import json
import os
import random
import re
import signal
import subprocess
import sys
import tempfile
import threading
import time
import urllib.parse
import urllib.request

LAB = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, LAB)
from fakeirc import file_bytes  # noqa: E402

KEY = "0b1f5e2a-6c3d-4e7f-9a8b-1c2d3e4f5a6b"
URL = "http://127.0.0.1:15556"
SCENARIO = os.path.join(LAB, "scenarios", "torture.json")
os.environ["NO_PROXY"] = os.environ["no_proxy"] = "127.0.0.1"
# silent or garbage sending bots fail within the test
os.environ["XG_COMPAT_SILENT_BOT_SECONDS"] = "60"
os.environ["XG_CATEGORY_FOLDERS"] = "tv"

failures = []


def check(ok, text):
    print(("ok   " if ok else "FAIL ") + text, flush=True)
    if not ok:
        failures.append(text)


def http(path, data=None, headers=None, timeout=20):
    request = urllib.request.Request(URL + path, data=data, headers=headers or {})
    try:
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as ex:
        return ex.code, ex.read()


def sab(**params):
    params.setdefault("apikey", KEY)
    params.setdefault("output", "json")
    status, body = http("/sabnzbd/api?" + urllib.parse.urlencode(params))
    return json.loads(body)


def scenario_packs():
    config = json.load(open(SCENARIO))
    return {p["name"]: (b, p) for b in config["bots"] for p in b["packs"]}


def links():
    """title -> NZB link from the recent feed"""
    result = {}
    for offset in range(0, 400, 100):
        status, body = http("/newznab/api?t=search&limit=100&offset=%d&apikey=%s" % (offset, KEY))
        items = re.findall(r"<item>.*?</item>", body.decode("utf-8"), re.S)
        for item in items:
            title = re.search(r"<title>(.*?)</title>", item, re.S).group(1)
            link = re.search(r"<link>(.*?)</link>", item, re.S).group(1).replace("&amp;", "&")
            result[title.replace("&apos;", "'").replace("&amp;", "&")] = link
        if len(items) < 100:
            break
    return result


def grab(link, category):
    with urllib.request.urlopen(link, timeout=20) as response:
        nzb = response.read()
    boundary = "----xgtorture%d" % random.randint(0, 1 << 30)
    body = ("--%s\r\nContent-Disposition: form-data; name=\"name\"; filename=\"grab.nzb\"\r\n"
            "Content-Type: application/x-nzb\r\n\r\n" % boundary).encode() + nzb + ("\r\n--%s--\r\n" % boundary).encode()
    status, answer = http("/sabnzbd/api?mode=addfile&cat=%s&apikey=%s&output=json" % (category, KEY), body,
                          {"Content-Type": "multipart/form-data; boundary=" + boundary})
    answer = json.loads(answer)
    return answer["nzo_ids"][0] if answer.get("status") else None


def title_of(name):
    # the Newznab title is the file name without extension, see NewznabHandler.PacketTitle
    return name


def find_link(all_links, name):
    for title, link in all_links.items():
        if title == name or name.startswith(title) or title.startswith(os.path.splitext(name)[0]):
            return link
    return None


class Lab:
    def __init__(self, build):
        self.build = build
        self.state = tempfile.mkdtemp(prefix="xg-torture-")

    def start(self, xg_only=False):
        env = dict(os.environ)
        if xg_only:
            env["LAB_XG_ONLY"] = "1"
        subprocess.run([os.path.join(LAB, "run.sh"), self.build, SCENARIO, self.state], env=env, check=True,
                       stdout=subprocess.DEVNULL)
        for _ in range(60):
            try:
                if http("/sabnzbd/api?mode=version", timeout=2)[0] == 200:
                    return
            except OSError:
                pass
            time.sleep(1)
        raise RuntimeError("XG did not start")

    def processes(self, marker):
        found = []
        for pid in os.listdir("/proc"):
            if pid.isdigit() and int(pid) != os.getpid():
                try:
                    cmdline = open("/proc/%s/cmdline" % pid, "rb").read().decode("utf-8", "replace")
                except OSError:
                    continue
                if marker in cmdline and cmdline.split("\0")[0] in ("mono", "python3", "/usr/bin/mono", "/usr/bin/python3"):
                    found.append(int(pid))
        return found

    def kill(self, marker):
        for pid in self.processes(marker):
            os.kill(pid, signal.SIGKILL)
        time.sleep(1)

    def start_irc(self):
        log = open(os.path.join(self.state, "irc.log"), "a")
        subprocess.Popen(["python3", "-u", os.path.join(LAB, "fakeirc.py"), SCENARIO], stdout=log, stderr=log)
        time.sleep(1)

    def stop(self):
        subprocess.run([os.path.join(LAB, "stop.sh"), self.state], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)

    def log(self):
        return open(os.path.join(self.state, "xg.log"), encoding="utf-8", errors="replace").read()


class Hammer:
    """Clients polling the API as fast as they can, like several *Arr instances would."""

    def __init__(self, threads=8, pause=0.02):
        self.pause = pause
        self.running = True
        self.requests = 0
        self.errors = []
        self.slowest = 0.0
        self.threads = [threading.Thread(target=self.run, daemon=True) for _ in range(threads)]
        for thread in self.threads:
            thread.start()

    def run(self):
        paths = ["/sabnzbd/api?mode=queue&apikey=%s&output=json" % KEY,
                 "/sabnzbd/api?mode=history&apikey=%s&output=json" % KEY,
                 "/sabnzbd/api?mode=get_config&apikey=%s&output=json" % KEY,
                 "/newznab/api?t=search&q=load&apikey=%s" % KEY,
                 "/newznab/api?t=tvsearch&q=load%%20show&season=1&ep=2&apikey=%s" % KEY,
                 "/newznab/api?t=caps&apikey=%s" % KEY]
        while self.running:
            start = time.time()
            try:
                status, body = http(random.choice(paths), timeout=30)
                if status >= 500:
                    self.errors.append("HTTP %d" % status)
            except Exception as ex:  # noqa: BLE001 - any failure counts
                self.errors.append(repr(ex))
            self.slowest = max(self.slowest, time.time() - start)
            self.requests += 1
            time.sleep(self.pause)

    def stop(self):
        self.running = False
        for thread in self.threads:
            thread.join(timeout=35)


def wait_jobs(ids, seconds):
    until = time.time() + seconds
    while time.time() < until:
        history = {s["nzo_id"]: s for s in sab(mode="history")["history"]["slots"]}
        if all(i in history for i in ids):
            return history
        time.sleep(3)
    return {s["nzo_id"]: s for s in sab(mode="history")["history"]["slots"]}


def verify_files(history, ids_by_name, expect_failed=()):
    packs = scenario_packs()
    for name, job in ids_by_name.items():
        slot = history.get(job)
        if name in expect_failed:
            check(slot is not None and slot["status"] == "Failed",
                  "%s failed cleanly: %s" % (name, slot and (slot["status"] + " " + slot.get("fail_message", ""))[:160]))
            continue
        if slot is None or slot["status"] != "Completed":
            check(False, "%s completed: %s" % (name, slot and (slot["status"] + " " + slot.get("fail_message", ""))[:160] or "still queued"))
            continue
        bot, pack = packs[name]
        data = open(slot["storage"], "rb").read() if os.path.exists(slot["storage"]) else b""
        check(data == file_bytes(pack["name"], pack["size"]),
              "%s complete and byte-identical (%d bytes in %s)" % (name, len(data), os.path.dirname(slot["storage"]).split("/XG/")[-1]))


def grab_all(names, category="tv"):
    all_links = {}
    for _ in range(40):
        all_links = links()
        if all(find_link(all_links, n) for n in names):
            break
        time.sleep(2)
    ids = {}
    threads = []

    def one(name):
        link = find_link(all_links, name)
        ids[name] = grab(link, category) if link else None

    for name in names:
        threads.append(threading.Thread(target=one, args=(name,)))
        threads[-1].start()
    for thread in threads:
        thread.join()
    missing = [n for n, i in ids.items() if not i]
    check(not missing, "all %d grabs accepted%s" % (len(names), (", missing: " + ", ".join(missing)) if missing else ""))
    return {n: i for n, i in ids.items() if i}


def main():
    build = sys.argv[1]
    lab = Lab(build)
    try:
        lab.start()
        print("state in " + lab.state, flush=True)

        # --- load ------------------------------------------------------------
        load = (["Load.Show.S01E0%d.720p.mkv" % e for e in range(1, 9)] +
                ["Load.Movie.%d.1080p.mkv" % y for y in range(2021, 2025)] +
                ["Passive.Load.S02E0%d.720p.mkv" % e for e in range(1, 9)] +
                ["Show With Spaces S01E01 720p.mkv", "Café.Déjà.Vu.S01E02.720p.mkv", "It's.Someone's.Show.S01E03.720p.mkv"] +
                ["Evil.Show.S01E0%d.720p.mkv" % e for e in range(1, 4)] +
                ["Escape.Show.S01E01.720p.mkv"])
        hammer = Hammer()
        started = time.time()
        ids = grab_all(load)
        history = wait_jobs(list(ids.values()), 420)
        hammer.stop()
        print("load took %ds, %d API requests, slowest %.1fs" % (time.time() - started, hammer.requests, hammer.slowest), flush=True)
        verify_files(history, ids, expect_failed=[n for n in load if n.startswith("Evil.")])
        check(not hammer.errors, "API answered every request (%d errors%s)" % (len(hammer.errors), (": " + ", ".join(sorted(set(hammer.errors))[:3])) if hammer.errors else ""))
        # the *Arr applications give up after 100s; at a few hundred requests per second the old
        # embedded web server now and then needs several seconds for an answer
        check(hammer.slowest < 30, "no API request timed out (slowest answer %.1fs at about %d requests/s)" % (hammer.slowest, hammer.requests / max(1, time.time() - started)))
        escaped = [p for p in (os.path.join(lab.state, "home", "escape.mkv"), os.path.join(lab.state, "home", ".config", "escape.mkv")) if os.path.exists(p)]
        check(not escaped, "the hostile file name stayed inside the download folder")
        check("already connected" not in lab.log(), "every DCC offer was handled once")

        # --- restart ---------------------------------------------------------
        restart = ["Restart.Show.S01E0%d.720p.mkv" % e for e in range(1, 5)] + ["Restart.Movie.2021.1080p.mkv", "Restart.Movie.2022.1080p.mkv"]
        ids = grab_all(restart)
        time.sleep(6)
        queue = sab(mode="queue")["queue"]["slots"]
        check(any(s["status"] == "Downloading" for s in queue), "transfers running before the restart (%d downloading)" % sum(s["status"] == "Downloading" for s in queue))
        lab.kill("XgLab.exe")
        lab.start(xg_only=True)
        check(len(sab(mode="queue")["queue"]["slots"]) + len(sab(mode="history")["history"]["slots"]) >= len(ids), "jobs survived the restart")
        history = wait_jobs(list(ids.values()), 420)
        verify_files(history, ids)

        # --- IRC network dies ------------------------------------------------
        crash = ["Crash.Show.S01E01.720p.mkv", "Crash.Show.S01E02.720p.mkv", "Crash.Movie.2021.1080p.mkv"]
        ids = grab_all(crash)
        time.sleep(6)
        lab.kill("fakeirc.py")
        time.sleep(5)
        lab.start_irc()
        history = wait_jobs(list(ids.values()), 480)
        verify_files(history, ids)

        # --- fuzz ------------------------------------------------------------
        junk = ["", "0", "-1", "999999999999999999999", "1e9", "%00", "..", "../../etc", "é", "' OR 1=1", "<x>", "\\", "*", "NaN",
                "a" * 5000, "S01E01", "tv", "delete", "queue", "history", "‮", "\t", "-", "{}", "[]"]
        server_errors = []
        for _ in range(600):
            api = random.choice(["newznab", "sabnzbd"])
            params = {}
            for _ in range(random.randint(0, 6)):
                key = random.choice(["t", "q", "mode", "name", "value", "cat", "limit", "offset", "season", "ep", "id", "v",
                                     "apikey", "output", "del_files", "category", "start", "imdbid", "offline", junk[random.randrange(len(junk))]])
                params[key] = random.choice(junk) if random.random() < 0.7 else (KEY if key == "apikey" else "search")
            if random.random() < 0.6:
                params["apikey"] = KEY
            if len(urllib.parse.urlencode(params)) > 4000:
                # the embedded web server rejects request lines of about 8 KB itself
                continue
            try:
                status, body = http("/%s/api?%s" % (api, urllib.parse.urlencode(params)), timeout=20)
                if status >= 500:
                    server_errors.append("%d %s %s" % (status, api, params))
            except Exception as ex:  # noqa: BLE001
                server_errors.append("%r %s %s" % (ex, api, params))
        check(not server_errors, "600 garbage API requests without a server error%s" % ((": " + server_errors[0][:200]) if server_errors else ""))
        try:
            http("/sabnzbd/api?mode=" + "a" * 20000, timeout=20)
        except Exception:  # noqa: BLE001 - only survival matters
            pass
        check(http("/sabnzbd/api?mode=version")[0] == 200, "XG still running after all of it")
    finally:
        lab.stop()
        print("state kept in " + lab.state, flush=True)

    print("%d failure(s)" % len(failures))
    sys.exit(1 if failures else 0)


if __name__ == "__main__":
    main()
