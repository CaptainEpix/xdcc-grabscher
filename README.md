# XG 3.3.3.0 — Mono/Docker Compatibility Fork

This fork revives **XG (XDCC Grabscher) 3.3.0.0** for modern Mono and Docker environments.

XG was originally created by **Lars Formella**. This fork preserves the original project, licensing, and attribution while applying compatibility and maintenance fixes needed to run XG on current systems.

This compatibility release is based on XG 3.3.0.0 and reports itself as **XG 3.3.3.0**.

## What's changed

### 3.3.3.0

- **Prowlarr, Sonarr and Radarr integration**: XG works as a Newznab indexer and a SABnzbd-compatible download client (see below)
- **Passive (reverse) DCC** support with forwarded ports, ready for VPN setups (see [Passive DCC](#passive-dcc))
- Optional **download folder per category** (`dl/tv`, `dl/movies`)
- More reliable downloads:
  - refused or silent DCC connections are retried instead of giving up on the first try
  - stale offers are cancelled, and offers are matched to packets by file name
  - a broken transfer is never finished as complete, and parts are resumed, also from another bot
  - finished files never replace existing ones (saved as `name (1).ext`)
  - the next packet from a bot, or a bot waiting for a free slot, is requested within seconds instead of up to 4 minutes
  - bots that never answer or send unusable offers end the request, so the *Arr can try another release
- Stability and security:
  - a malformed DCC line from anyone in a channel no longer crashes XG
  - IRC messages are never handled twice, and queues shared between threads are locked
  - pack lists are only downloaded when XG asked for them
- Packet names keep non-ASCII letters (umlauts, accents), and search ignores accents and apostrophes
- Fixed the crash on the very first start while creating `xgsnapshots.db`
- Fixed `/api/1.0` requests hanging and wrong responses
- Download folders changed in the web interface work without a restart
- A lab with a fake IRC network and scripted bots for testing XG without real IRC (`tools/lab`)

### 3.3.2.0 and earlier

- Builds and runs with **Mono 6.12**
- Added a reproducible multi-stage **Docker** build
- Fixed Mono build detection for current Mono/Roslyn toolchains
- Removed the obsolete Jabber plugin dependency
- Uses the Debian/Mono-compatible db4o assembly at runtime
- Forces SignalR to use long polling instead of unsupported WebSockets
- Automatically reconnects the web UI after SignalR disconnects
- Fixed packet searches hanging when the first search was entered manually
- Removed the obsolete remote-settings loader
- Removed the defunct XG cloud-search configuration and donation/server-status message
- Added native **IRC TLS** support, with automatic TLS on port 6697 and explicit `ircs://` support for custom TLS ports
- Validates IRC server certificates when using TLS
- Fixed **NickServ** authentication and reconnect handling, including protected-channel retries after identification
- Prevented NickServ credentials from being sent as an IRC server password and redacted authentication credentials from logs
- Fixed the IRC password field in the web interface

## Docker

### Build

From the repository root:

```bash
docker build -t xdcc-grabscher:3.3.3.0 .
```

### Run

```bash
docker run -d \
  --name xdcc-grabscher \
  --restart unless-stopped \
  -p 5556:5556 \
  -v /path/to/config:/config \
  -v /path/to/downloads:/config/.config/XG/dl \
  xdcc-grabscher:3.3.3.0
```

Then open:

```text
http://YOUR-SERVER:5556
```

**Change the default XG web password (`xgisgreat`) after your first login.**

XG deliberately refuses to run as root. The Docker image therefore runs XG as UID/GID **99:100**.

Make sure the directories mounted at `/config` and `/config/.config/XG/dl` are writable by that user. For example:

```bash
mkdir -p config downloads
chown -R 99:100 config downloads
```

The exact host paths are up to you; the paths above are only examples.

## Docker Compose

The repository includes a `docker-compose.yml` using local `config` and `downloads` directories.

Before starting it for the first time:

```bash
mkdir -p config downloads
chown -R 99:100 config downloads
docker compose up -d --build
```

## Persistent data

XG stores its configuration and application data underneath:

```text
/config
```

Downloads are written by default to:

```text
/config/.config/XG/dl
```

Both should normally be backed by persistent Docker bind mounts or volumes.

## Passive DCC

Most bots open a port and XG connects to them. Some bots only offer **passive** (reverse) DCC: XG has to open a port, tell the bot its public address, and the bot connects to XG. This needs a few ports that are reachable from the internet.

1. Pick a few ports, e.g. `50000-50004`. Each port carries one passive transfer at a time.
2. **Forward them on your router** (TCP) to the machine running XG.
3. **Publish them on the container** with the same numbers, e.g. `-p 50000-50004:50000-50004` (on unRAID add them as ports, or put that into *Extra Parameters*).
4. **Tell XG** with environment variables:

| Variable | Example | Meaning |
| --- | --- | --- |
| `XG_PASSIVE_DCC_PORTS` | `50000-50004` | Ports XG listens on. A `listen:public` pair like `50000:61234` advertises a different outside port, for routers or VPNs that forward another port to the container. Lists are separated by commas. |
| `XG_PASSIVE_DCC_IP` | `203.0.113.7` | Your public IPv4 address. Optional: XG looks it up at startup (and every 30 minutes) at `api.ipify.org`, `checkip.amazonaws.com` or `icanhazip.com`. |
| `XG_PASSIVE_DCC_PORTS_FILE` | `/gluetun/forwarded_port` | Optional file with the port(s) to use, read before every transfer. |
| `XG_PASSIVE_DCC_IP_URL` | | Optional service returning the public address as plain text, instead of the defaults. |

```bash
docker run -d \
  --name xdcc-grabscher \
  -p 5556:5556 -p 50000-50004:50000-50004 \
  -e XG_PASSIVE_DCC_PORTS=50000-50004 \
  -v /path/to/config:/config \
  -v /path/to/downloads:/config/.config/XG/dl \
  xdcc-grabscher:3.3.3.0
```

**Behind a VPN** (e.g. XG sharing the network of a gluetun container): the ports have to be forwarded by the VPN provider, and not every provider offers that. Because XG looks up its address through its own connection, it finds the VPN's address by itself. If the provider forwards a fixed port, set it in `XG_PASSIVE_DCC_PORTS`; if the forwarded port changes on every connection, share gluetun's port file with XG and point `XG_PASSIVE_DCC_PORTS_FILE` at it.

Only the address a bot announced may connect to a passive port; the port is closed again after the transfer or after 90 seconds without a connection. Without `XG_PASSIVE_DCC_PORTS`, passive offers are rejected as before, and bots which sent one are left out of the Prowlarr/Sonarr/Radarr search results for 7 days.

## Prowlarr / Sonarr / Radarr integration

XG can act as an **indexer** for Prowlarr and as a **download client** for Sonarr, Radarr and Prowlarr. Nothing has to be patched or installed next to XG:

```text
Sonarr/Radarr → Prowlarr → XG Newznab API → NZB file → XG SABnzbd API → XDCC download → Sonarr/Radarr import
```

To do that, XG offers two small compatibility APIs on its normal web port:

| Endpoint | Emulates | Used by |
| --- | --- | --- |
| `http://XG-HOST:5556/newznab/api` | a Newznab indexer | Prowlarr (Generic Newznab) |
| `http://XG-HOST:5556/sabnzbd/api` | a SABnzbd download client | Sonarr, Radarr, Prowlarr |

Please note:

- **XG is not a Usenet client.** No Usenet server is ever contacted.
- The NZB files XG hands out are only an **internal envelope** that carries the XG packet through Prowlarr/Sonarr/Radarr and back to XG. They are useless to a real Usenet downloader.
- The SABnzbd API exists **only** so the existing *Arr SABnzbd client can submit and monitor XDCC downloads.
- Search is **text only**. IMDb/TVDB/TMDB id searches are not supported, because XDCC packets only have a file name.

### 1. Create an API key in XG

In the XG web interface, open the settings menu (gear icon), choose **Api Keys** and add a key with a name (for example `arr`). **New keys start disabled:** click the icon at the left of the key's row to enable it. The long value in the **Api Key** column is what Prowlarr, Sonarr and Radarr need.

If Prowlarr or the *Arr test reports *Incorrect user credentials* or *API Key Incorrect*, the key is usually still disabled.

You can use one key for everything or one key per application.

### 2. Prowlarr: add XG as indexer

**Indexers → Add Indexer → Generic Newznab**

| Setting | Value |
| --- | --- |
| URL | `http://XG-HOST:5556/newznab` |
| API Path | `/api` (the default) |
| API Key | your XG API key |
| Categories | Movies (2000), TV (5000) |

The Prowlarr test asks XG for its newest packets, so **at least one bot must be online** with packets when you press *Test*.

Prowlarr then syncs XG to Sonarr (TV) and Radarr (Movies) like any other indexer.

By default only packets of bots that are currently online are returned, because offline bots can not deliver. To include offline bots anyway, set *Additional Parameters* to `&offline=1`.

### 3. Sonarr and Radarr: add XG as download client

**Settings → Download Clients → + → SABnzbd**

| Setting | Sonarr | Radarr |
| --- | --- | --- |
| Host | XG host | XG host |
| Port | `5556` | `5556` |
| Use SSL | off | off |
| URL Base | `/sabnzbd` | `/sabnzbd` |
| API Key | your XG API key | your XG API key |
| Username / Password | empty | empty |
| Category | `tv` | `movies` |

Prowlarr only provides the indexer; each *Arr application still needs XG configured as its download client.

If you want to grab directly from a Prowlarr search, add the same SABnzbd download client in Prowlarr too (its default category `prowlarr` exists in XG).

### 4. Remote Path Mapping

XG reports finished downloads with the path **XG itself sees**, by default inside its download folder:

```text
/config/.config/XG/dl/Some.Show.S02E05.720p.mkv
```

XG can not know how that folder is mounted into your Sonarr or Radarr container. If the paths differ, add a **Remote Path Mapping** in Sonarr/Radarr (**Settings → Download Clients → Remote Path Mappings**).

Example on unRAID, where the same share is mounted differently into both containers:

| Container | Host path | Container path |
| --- | --- | --- |
| XG | `/mnt/user/downloads/xdcc` | `/config/.config/XG/dl` |
| Sonarr | `/mnt/user/downloads` | `/downloads` |

Remote Path Mapping in Sonarr:

| Host | Remote Path | Local Path |
| --- | --- | --- |
| XG host, exactly as in the download client | `/config/.config/XG/dl/` | `/downloads/xdcc/` |

If you changed the download folder in the XG settings, XG reports that folder instead.

### 5. Optional: a folder per category

By default every download lands directly in XG's download folder. With the environment variable `XG_CATEGORY_FOLDERS`, downloads grabbed by an *Arr go into a subfolder named after the category it sent (`tv` from Sonarr, `movies` from Radarr, as set in their download client):

| Value | Effect |
| --- | --- |
| *(not set)*, `0`, `false` | everything in the download folder (default) |
| `all`, `1`, `true` | a folder for every category, e.g. `dl/tv/`, `dl/movies/`, `dl/prowlarr/` |
| `tv,movies` | folders only for the listed categories; others stay in the download folder |

Packets you start yourself in the XG web interface have no category and stay in the download folder, so other tools (like FileBot) can tell them apart from *Arr downloads. Category names only become folders if they are plain names (letters, digits, space, `.`, `_`, `-`). An existing Remote Path Mapping for the download folder also covers the subfolders.

### How it behaves

**Searching**

- Text queries are matched against packet file names the same way the XG search does. Dots, dashes and underscores count as word separators, and `-word` excludes a word.
- Sonarr episode searches become `S02E05`. Season searches match every packet whose name contains a token starting with `S02`. Daily shows are matched by their date.
- XG does not know whether a packet is a movie or an episode. The category comes from the request: movie searches are returned as Movies, TV searches as TV, other searches in the requested category or as Other (8000).
- The publish date is when the packet started to offer its file (when XG first saw it, or when the bot put a different file under that pack number). Bots announce their packets over and over, so the announcement time would make old packets look new. The feed without a search term (RSS) is sorted the same way.
- Bots reuse pack numbers. If a packet offers a different file by the time it is grabbed, the grab is refused, so the wrong file is never downloaded.

**Downloading**

- A grab enables the packet in XG, exactly like clicking it in the web interface.
- The *Arr queue shows the job as *queued* while XG waits for the bot, and as *downloading* with progress and speed during the transfer.
- When XG has moved the finished file into its download folder, the job moves to history as *completed*, including the file path, and the *Arr imports it.
- Some bots only accept the file transfer on part of the ports they offer. If the transfer connection fails, XG cancels the bot's offer and asks again, up to 3 times, before giving up.
- If XG stops the download (transfer connection failed 3 times, bot offline, request denied, pack no longer valid, or disabled by you in XG), the job is marked *failed* with the reason and the last bot message. The *Arr can then search for another release or retry.
- A bot that does not answer at all fails the job after 15 minutes, so the *Arr can try another release. Bots that answer, for example with a queue position, are waited for as long as it takes. `XG_COMPAT_SILENT_BOT_SECONDS` changes the limit.
- Removing a completed item from the *Arr history only forgets the job. The downloaded file is only deleted when the *Arr explicitly asks to remove the data, and only if it is still that job's file inside XG's download folder.
- Jobs are stored in `/config/.config/XG/arr-jobs.json` and survive restarts.

### Known limitations

- Text search only; no IMDb/TVDB/TMDB id lookups, no anime absolute episode numbers, and names like `2x05` are not found by episode searches.
- Removing a *downloading* item from the *Arr queue stops the XDCC transfer, and XG always deletes the partial file, even if the *Arr was asked to keep data.
- XG has no pause or priorities. SABnzbd priorities are accepted but ignored.
- Bots that only offer passive (reverse) DCC need forwarded ports, see [Passive DCC](#passive-dcc).
- Categories only get separate folders when `XG_CATEGORY_FOLDERS` is set (see above).
- Grabbing a packet that is already being downloaded for an *Arr returns the existing job instead of a second one.
- The reported SABnzbd version is a fixed compatibility value.
- API keys are passed in the URL, as Newznab and SABnzbd clients expect. Keep XG on your LAN (see below) and do not share these URLs.

## Security warning

XG is an old application and still depends on an old web stack, including legacy versions of Nancy, SignalR, jQuery, Bootstrap, and other libraries.

**Do not expose the XG web interface directly to the public Internet.**

For normal use, keep it accessible only on a trusted LAN. If remote access is required, place it behind an appropriately secured reverse proxy with authentication and TLS.

Modernizing the dependency stack is outside the scope of this initial compatibility release.

## Known issues

### SignalR I/O warnings under Mono

The container log may occasionally contain warnings similar to:

```text
SignalR exception thrown by Task: System.AggregateException:
One or more errors occurred. (I/O error occurred.)
```

These appear to be associated with SignalR long-poll connection turnover under Mono. During testing they have not interrupted the web interface, searches, IRC connectivity, or XDCC downloads.

### Truncated API answers under very heavy load

At a few hundred requests per second the embedded web server (Nowin) now and then cuts an answer off after 8 KB, about once in 20,000 requests in the lab's torture test. Prowlarr, Sonarr and Radarr send a few requests per minute and simply ask again on their next poll.

### WebSockets

The legacy SignalR/Nowin stack used by XG does not provide working WebSocket support under the current Mono environment. This fork explicitly uses SignalR long polling instead.

## Release naming

Application version:

```text
3.3.3.0
```

Compatibility release/tag:

```text
v3.3.3.0-mono2026
```

## Original project

Everything below this point is the original XG project documentation.

---

[![XG](http://xg.bitpir.at/images/xg_bw.png?v=3)](http://www.larsformella.de/lang/en/portfolio/programme-software/xg)

XG, called __X__dcc __G__rabscher, is a XDCC download manager. Grabscher is the german word for grabber :-)


# What makes it special?
XG is just a command line app which connects to one or multiple IRC networks and handles the whole network communication. The IRC servers, channels, bots and packets are presented within a nice and stylish web frontend. There you can search and download packets.

You can run XG on every machine that supports C# / Mono - even root servers without x(org), or an old weak pc running linux without a monitor - and control your downloads with your browser from everywhere. You don't have to keep a big PC running, but just a small download box which handles all the IRC stuff.


# How do i use it?
Run the program and point your browser to __127.0.0.1:5556__. The default password is __xgisgreat__. If you already added some servers and channels, it will take some time untill the Webfrontend is up and running. This is due to the build in SQLite database which is not really performant and takes some time to load the saved objects.

![Password Dialog](http://xg.bitpir.at/images/help/login.png?v=3)

## At first: change the settings
You can do this directly in the web frontend. Just click on the __Config__ link in the options menu.

![Options](http://xg.bitpir.at/images/help/options.png?v=3)

This is a small explanation to help you set the correct options. If you don't want to use a special feature, just disable it.

__Note:__ The Elastic Search configuration is not available in the webfrontend anymore and can be changed by editing the config file manually.

![Settings part 1](http://xg.bitpir.at/images/help/settings_1.png?v=3)

The web server password is filled with __xgisgreat__ and the port ist __5556__. The IRC passport and email can be left blank and are just needed if you want use nickserv.

![Settings part 2](http://xg.bitpir.at/images/help/settings_2.png?v=3)

### Filehandlers
If a packet is downloaded you can run several commands. If the regex of a file handler matches the file name, the process is started. A process is defined by a command, arguments and the next process. The next process can be left empty and only is called if the current one is successfully executed.

![Settings part 3](http://xg.bitpir.at/images/help/settings_3.png?v=3)

The following handler matches all rar / zip archives. It will create a separate folder, extract the archive into it and removes the archive. Every process is executed only, if the previous one was successfully. Because of this, the handler won't delete the archive if he could not extract it.

![Settings part 4](http://xg.bitpir.at/images/help/settings_4.png?v=3)

You can add as many file handlers as you want. They are also stored in the settings file.

#### Arguments
You can use different placeholders in your arguments:

* __%PATH%__ = full path of the file, like __/the/full/path/to/file_complete.rar__
* __%FOLDER%__ = full path of the folder of the file, like __/the/full/path/to__
* __%FILE%__ = the complete file name, like __file_complete.rar__
* __%FILENAME%__ = just the file name, like __file_complete__
* __%EXTENSION%__ = just the file extension, like __rar__

### Change settings manually
If you want to change the settings manually, you have to change the file named __xg.config__ located in your user folder:

* Windows 7: C:\Users\Username\AppData\Roaming\XG
* Linux: /home/Username/.config/XG
* Mac: /Users/Username/.config/XG

## Add servers and channels
Now you have to add IRC networks and channels. The bots and packets are generated and updated automatically. If you don't know which server and channels to add, try the integrated [xg.bitpir.at](http://xg.bitpir.at) search or add a XDCC link.

Normally the bots will announce their pakets directly in the channel. If they are silent, you can check the option __Check user versions__ and XG will ask the voiced users about their version. If XG detects an iroffer he will try to send __xdcc list__ commands to get packet lists. __\_DO NOT\___ check the option unless you know, that the bots in this channel wont announce their packets. Otherwhise you mostly will be banned!

![Server / Channel Dialog](http://xg.bitpir.at/images/help/servers.png?v=3)

## Search
You can search for packets by entering a custom search term and just hit enter. If your want to save your search, just click on the thumb button. Deleting a search works the same. The search items are working with the internal and external search and are also saved into the database. If you want to exclude words from your search you can use "-". To search for packages and exclude TS releases you could use **Spiderman -TS**. The size of packets can be controlled by the size box. Only packets which are bigger than the given value are displayed. If you don't want to use this feature, leave this field blank or zero.

![Search](http://xg.bitpir.at/images/help/search.png?v=3)

XG supportes wildcard searches to be able to search for tv shows. If you search for **under the dome s02e\*\*** you will get results for all season 2 episodes from 01 to 99. Even multiple wildcards are supported: **under the dome s\*\*e\*\*** will return results for all season from 01 to 10 and  episodes from 01 to 30. Because multiple wildcard searches are expensive, the results are limited to 10 seasons and 30 episodes.

The results are displayed in a table and the packets can be grouped by their bot or wildcard search. The grouping can be disabled, but you will lose some important informations. If you click on a packet icon, XG will try to download it and keeps you up to date with updated packet informations. The packet icon will match the file ending, so there are different versions.

![Packet Icons](http://xg.bitpir.at/images/help/search_results_bot.png?v=3)

![Packet Icons](http://xg.bitpir.at/images/help/search_results_wildcard.png?v=3)

## Notifications
If something happens inside XG you will get a notification. This can also be shown via your browser if you allow it.

![Notification Icon](http://xg.bitpir.at/images/help/notification.png?v=3)

## XDCC Links
You can add XDCC links in the following dialog. A XDCC link must have the following structure:

> xdcc:// __server__ / __server-name__ / __channel__ / __bot__ / __packet-id__ / __file-name__ /

The server, channel and bot is automatically added. If the server is connected and the channel joined, the packet will be requested.

![XDCC Links](http://xg.bitpir.at/images/help/xdcc-links.png?v=3)

The server and channel are not deleted after the packet is complete, so if you don't need them anymore, you have to delete them yourself.

## Extended Stats / Snapshots
XG will collect every 5 minutes some statistical data and generate nice graphs. There you can enable and disable different values to get an optimal view of your running XG copy.

![Extended Statistics](http://xg.bitpir.at/images/help/graphs.png?v=3)

This feature wont work in older browsers like the good old IE8, so do yourself a favor and use a newer one ;-)

## API
XG v3 supports a REST api to control it via external tools. You can add api keys and enable / disable them. 

![Api](http://xg.bitpir.at/images/help/api.png?v=3)

The following objects can be controlled with different methods via the api:

* servers
  * add
  * delete
  * enable / disable
  * list
* channels
  * add
  * delete
  * enable / disable
  * list
* bots
  * list
* packets
  * enable / disable
  * list
* files
  * delete
  * list
* searches
  * add
  * delete
  * list

The object name has to be entered after the __/api/1.0/__ path segment with the format in the wich the answer should be encoded, for example __/api/1.0/servers.json__. The data you want to pass to the method has to be encoded in same format. The content type must be the format, too. The __Authorization__ header is mandatory and has to match an api key which is enabled. If the apiKey was invalid or disabled, the method will result in an 401.

The currently allowed formats:

* json (preferred)

Api methods which create or update data, always return the following properties:

* __ReturnValue__ (int)
  * 0 - there was an error calling the method
  * 1 - everything is fine
* __Message__ (string)
  * a helpful message if an error occurred

---
### Delete
You can delete an object and all children.

#### URL
> __DELETE /api/1.0/[ servers | channels | files | searches ]/$guid.$format__

#### Example
```
curl -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XDELETE localhost:5556/api/1.0/servers/deebd412-9b16-4726-b613-7ec98e714f59.json
```

#### Return Value
```
{
  "ReturnValue":1,
  "Message":null
}
```

---
### Get
Get a single object by its guid.

#### URL
> __GET /api/1.0/[ servers | channels | bots | packets | files | searches ]/$guid.$format__

#### Example
```
curl -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XGET localhost:5556/api/1.0/servers/deebd412-9b16-4726-b613-7ec98e714f59.json
```

#### Return Value
```
{
	"Port":6667,
	"ErrorCode":0,
	"ParentGuid":"c31aa923-b615-4d03-840d-c82357c929d4",
	"Guid":"906bfd60-b6f1-4d38-a2f4-cb9cba983a24",
	"Name":"irc.abjects.net",
	"Connected":false,
	"Enabled":false
}
```

---
### Enable
If you enable servers and channels, they will be connected. If you enable a packet it will be downloaded.

#### URL
> __POST /api/1.0/[ servers | channels | packets ]/$guid/enable.$format__

#### Example
```
curl -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XPOST localhost:5556/api/1.0/servers/deebd412-9b16-4726-b613-7ec98e714f59/enable.json
```

#### Return Value
```
{
  "ReturnValue":1,
  "Message":null
}
```

---
### Disable
If you disable servers and channels, they will be disconnected. If you disable a packet the download will be stopped and the file is beeing deleted.

#### URL
> __POST /api/1.0/[ servers | channels | packets ]/$guid/disable.$format__

#### Example
```
curl -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XPOST localhost:5556/api/1.0/servers/deebd412-9b16-4726-b613-7ec98e714f59/disable.json
```

#### Return Value
```
{
  "ReturnValue":1,
  "Message":null
}
```

---
### Add
You can add an object. All parameters are mandatory.

If you got a XDCC link, you can use this method to add a packet and download it instantly. The server and channel are not deleted after the packet is complete, so if you dont need them anymore, you have to delete them yourself.

#### Url
> __PUT /api/1.0/[ servers | channels | packets | searches ].$format__

#### Post Parameters for servers
* server (string): irc.rizon.net
* port (integer): 11

#### Post Parameters for channels
* server (string): irc.rizon.net
* channel (string): #abjects

#### Post Parameters for packets
* server (string): irc.rizon.net
* channel (string): #abjects
* bot (string): [XDCC]Bot
* packetId (integer): 11
* packetName (string): My.Super.Movie.mkv

#### Post Parameters for searches
* search (string): german -mkv

#### Example
```
curl -H "Content-Type:application/json" -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XPOST localhost:5556/api/1.0/servers.json -d '
{
  "server":"irc.rizon.net",
  "port": 6667
}'
```

```
curl -H "Content-Type:application/json" -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XPOST localhost:5556/api/1.0/packets.json -d '
{
  "server":"irc.rizon.net",
  "channel":"#abjects",
  "bot":"[XDCC]Bot",
  "packetId":11,
  "packetName":"My.Super.Movie.mkv"
}'
```

#### Return Value
```
{
  "ReturnValue":0,
  "Message":"server is empty"
}
```

---
### List
You can list objects. If you want to list packets, you can controll the results.

#### Url
> __GET /api/1.0/[ servers | channels | packets | files | searches ].$format__

#### Get Parameters for packets
* __searchTerm__ (string) *: german -mkv
* __showOfflineBots__ (boolean): true | false
* __maxResults__ (integer)
* __page__ (integer)
* __sortBy__ (string): Id | Name | Size
* __sort__ (string): asc | desc

The properties __showOfflineBots__, __maxResults__, __page__, __sortBy__, __sort__ can be left blank. If you leave __showOfflineBots__ blank, it will be filled with __false__ and the search request will just return packets, which bots are online.

#### Example
```
curl -H "Authorization: 615d86bb-f867-47c1-a860-ac24e09e976c" -s -XGET 'localhost:5556/api/1.0/packets.json?searchTerm=mkv%20-seven&showOfflineBots=true'
```

#### Return Value
```
{
  "Results":
  [
    {
      ...
    }
  ],
  "ResultCount":5584
}
```
Results is an array containg the requestet objects.

## Shutdown XG gracefully
If you want to shutdown XG, just ctrl+c the process or close the command window. You can also stop XG by using the shutdown button in the webfrontend.

# Upgrading XG
If you are upgrading from version 2 to 3, you should finish your downloads and write down your servers and channels, because XG 3 is not able to load the data generated by previous versions.

If you are upgrading from XG 3.2 to 3.3 you should notice, that the db format switched from sqlite to db4o. Because of this, XG automatically transformes the db **xgobjects.db** into a db4o database **xgobjects.db4o** if it is not there already. The sqlite file can be safely deleted after the first start, but can also be keeped as backup. If you delete the db4o file, XG will start the transformation process again.
 
## Unnecessary Files
Because XG changed some internal routines you can safely delete the following files in the config folder:

### prior version 2
* XG/xgsnapshots.bin
* XG/xgsnapshots.bin.bak
* XG/statistics.xml

### prior version 3
* XG/xg.bin
* XG/xg.bin.bak
* XG/xgfiles.bin
* XG/xgfiles.bin.bak
* XG/xgsearches.bin
* XG/xgsearches.bin.bak
* XG/settings.xml


# Running XG

## On Windows
You need at least .net 4.5.

## On Linux with Mono
You need at least mono 3.x because some needed libs are running on .net 4.5 wich is not supported in earlier versions.

If you are using Debian / Ubuntu, take a look here to get newer mono packages:

> http://mono-project.com/DistroPackages/Debian

### Needed packets / libs
* mono-complete

#### Install command for Debian / Ubuntu to copy paste:
```bash
sudo apt-get install mono-complete
```
