#!/usr/bin/env python3
"""
A tiny IRC network with scripted XDCC bots, for testing XG without real IRC.

One plaintext IRC server, one channel, and bots that behave like iroffer-dinoex:
they announce packs in the channel, answer "XDCC SEND #n" with a notice and a
DCC SEND offer, keep one pending offer per user and re-send it (with the
"You have a DCC pending" notice) when the user asks again, and serve the file
on a TCP port when XG connects.

Every bot has a DCC behaviour, so failures seen with real bots can be replayed:

  good       listens on the offered port and sends the file
  refuse     offers a port nobody listens on (like a bot with broken port forwarding)
  flaky      offers ports from a range; only some of them accept connections
             ("port_sequence" fixes the order of offered ports for repeatable runs)
  passive    offers port 0, asking the client to listen (reverse DCC)
  late       starts listening only some time after sending the offer

Usage: fakeirc.py lab.json
All events are written as JSON lines to stdout so tests can assert on them.
"""

import asyncio
import hashlib
import json
import random
import socket
import sys
import time

SERVER = "lab.irc"


def log(event, **fields):
    fields["event"] = event
    fields["t"] = round(time.time(), 3)
    print(json.dumps(fields), flush=True)


def file_bytes(name, size):
    """Deterministic content, so the downloaded file can be verified."""
    seed = hashlib.sha256(name.encode()).digest()
    block = (seed * (65536 // len(seed) + 1))[:65536]
    full, rest = divmod(size, len(block))
    return block * full + block[:rest]


def format_size(size):
    for unit, factor in (("G", 1 << 30), ("M", 1 << 20), ("K", 1 << 10)):
        if size >= factor:
            return "%.1f%s" % (size / factor, unit)
    return "%dB" % size


class Offer:
    def __init__(self, pack, port, created):
        self.pack = pack
        self.port = port
        self.created = created
        self.server = None


class Bot:
    def __init__(self, network, config):
        self.network = network
        self.nick = config["nick"]
        self.mode = config.get("mode", "good")
        self.ports = config.get("ports", [0])
        self.open_ports = set(config.get("open_ports", self.ports))
        self.late_ms = config.get("late_ms", 0)
        self.pending_timeout = config.get("pending_timeout", 180)
        self.speed = config.get("speed", 0)
        self.packs = {p["id"]: p for p in config["packs"]}
        self.offers = {}
        self.rng = random.Random(config.get("seed", self.nick))
        self.port_sequence = list(config.get("port_sequence", []))

    def announce_lines(self):
        lines = ["** %d packs **  1 of 1 slot open" % len(self.packs)]
        for pack in self.packs.values():
            lines.append("#%d   0x [%s] %s" % (pack["id"], format_size(pack["size"]), pack["name"]))
        return lines

    async def on_private(self, user, text):
        words = text.strip().split()
        command = " ".join(words[:2]).upper()
        if command == "XDCC SEND" and len(words) > 2:
            await self.on_send(user, words[2].lstrip("#"))
        elif command in ("XDCC REMOVE", "XDCC CANCEL"):
            offer = self.offers.pop(user.nick, None)
            log("bot_cancel", bot=self.nick, user=user.nick, command=command, had_offer=offer is not None)
            if offer:
                self.close_offer(offer)
            await user.notice(self.nick, "** Cancelled pending DCC offer" if offer else "** You don't appear to be in a queue")

    async def on_send(self, user, pack_id):
        try:
            pack = self.packs[int(pack_id)]
        except (ValueError, KeyError):
            log("bot_invalid_pack", bot=self.nick, pack=pack_id)
            await user.notice(self.nick, "** Invalid Pack Number, Try Again")
            return

        offer = self.offers.get(user.nick)
        if offer and time.time() - offer.created < self.pending_timeout:
            remaining = int(self.pending_timeout - (time.time() - offer.created))
            log("bot_pending", bot=self.nick, requested=pack["id"], pending=offer.pack["id"], port=offer.port, remaining=remaining)
            await user.notice(self.nick, "** You have a DCC pending, Set your client to receive the transfer. "
                                         "Type \"/MSG %s XDCC CANCEL\" to abort the transfer. (%d seconds remaining until timeout)" % (self.nick, remaining))
            await self.send_offer(user, offer)
            return

        if self.mode == "passive":
            port = 0
        elif self.port_sequence:
            port = self.port_sequence.pop(0)
        else:
            port = self.rng.choice(self.ports)
        offer = Offer(pack, port, time.time())
        self.offers[user.nick] = offer
        log("bot_offer", bot=self.nick, user=user.nick, pack=pack["id"], name=pack["name"], port=port, mode=self.mode)
        await user.notice(self.nick, "** Sending you pack #%d (\"%s\"), which is %s. (resume supported)" % (pack["id"], pack["name"], format_size(pack["size"])))

        listens = self.mode in ("good", "late") or (self.mode == "flaky" and port in self.open_ports)
        if listens:
            if self.mode == "late":
                asyncio.get_running_loop().call_later(self.late_ms / 1000.0, lambda: asyncio.ensure_future(self.listen(user, offer)))
            else:
                await self.listen(user, offer)
        await self.send_offer(user, offer)
        asyncio.get_running_loop().call_later(self.pending_timeout, self.expire, user.nick, offer)

    async def send_offer(self, user, offer):
        pack = offer.pack
        if offer.port == 0:
            line = "\x01DCC SEND %s %d 0 %d %d\x01" % (pack["name"], 2130706433, pack["size"], 1234)
        else:
            line = "\x01DCC SEND %s %d %d %d\x01" % (pack["name"], 2130706433, offer.port, pack["size"])
        await user.privmsg(self.nick, line)

    async def listen(self, user, offer):
        if offer.server is not None or self.offers.get(user.nick) is not offer:
            return

        async def handle(reader, writer):
            log("bot_connected", bot=self.nick, port=offer.port, pack=offer.pack["id"])
            data = file_bytes(offer.pack["name"], offer.pack["size"])
            try:
                for i in range(0, len(data), 65536):
                    writer.write(data[i:i + 65536])
                    await writer.drain()
                    if self.speed:
                        await asyncio.sleep(65536 / self.speed)
                log("bot_sent", bot=self.nick, pack=offer.pack["id"], bytes=len(data))
                await asyncio.sleep(1)
            except (ConnectionError, OSError) as ex:
                log("bot_send_failed", bot=self.nick, pack=offer.pack["id"], error=str(ex))
            finally:
                writer.close()
                self.offers.pop(user.nick, None)
                self.close_offer(offer)

        try:
            offer.server = await asyncio.start_server(handle, "127.0.0.1", offer.port, reuse_address=True)
            log("bot_listening", bot=self.nick, port=offer.port)
        except OSError as ex:
            log("bot_listen_failed", bot=self.nick, port=offer.port, error=str(ex))

    def expire(self, nick, offer):
        if self.offers.get(nick) is offer:
            log("bot_offer_expired", bot=self.nick, pack=offer.pack["id"], port=offer.port)
            self.offers.pop(nick, None)
            self.close_offer(offer)

    def close_offer(self, offer):
        if offer.server is not None:
            offer.server.close()
            offer.server = None


class User:
    def __init__(self, network, reader, writer):
        self.network = network
        self.reader = reader
        self.writer = writer
        self.nick = None

    async def send(self, line):
        self.writer.write((line + "\r\n").encode("utf-8", "replace"))
        await self.writer.drain()

    async def notice(self, source, text):
        await self.send(":%s!bot@%s NOTICE %s :%s" % (source, SERVER, self.nick, text))

    async def privmsg(self, source, text):
        await self.send(":%s!bot@%s PRIVMSG %s :%s" % (source, SERVER, self.nick, text))


class Network:
    def __init__(self, config):
        self.channel = config.get("channel", "#lab")
        self.announce_every = config.get("announce_every", 30)
        self.bots = {b["nick"].lower(): Bot(self, b) for b in config["bots"]}
        self.users = []

    async def handle(self, reader, writer):
        user = User(self, reader, writer)
        self.users.append(user)
        try:
            while True:
                raw = await reader.readline()
                if not raw:
                    break
                line = raw.decode("utf-8", "replace").rstrip("\r\n")
                await self.on_line(user, line)
        except (ConnectionError, OSError):
            pass
        finally:
            self.users.remove(user)
            log("user_disconnected", nick=user.nick)

    async def on_line(self, user, line):
        parts = line.split(" ", 1)
        command = parts[0].upper()
        rest = parts[1] if len(parts) > 1 else ""
        if command == "NICK":
            user.nick = rest.lstrip(":").strip()
        elif command == "USER":
            log("user_registered", nick=user.nick)
            for num, text in (("001", "Welcome to the XG lab"), ("002", "Your host is " + SERVER), ("003", "Created now"), ("004", SERVER + " lab o o")):
                await user.send(":%s %s %s :%s" % (SERVER, num, user.nick, text))
            await user.send(":%s 005 %s CHANTYPES=# PREFIX=(ov)@+ NETWORK=XGLab :are supported" % (SERVER, user.nick))
            await user.send(":%s 375 %s :- MOTD" % (SERVER, user.nick))
            await user.send(":%s 376 %s :End of MOTD" % (SERVER, user.nick))
        elif command == "PING":
            await user.send(":%s PONG %s :%s" % (SERVER, SERVER, rest.lstrip(":")))
        elif command == "JOIN":
            channel = rest.split()[0].lstrip(":")
            await user.send(":%s!xg@client JOIN :%s" % (user.nick, channel))
            if channel.lower() == self.channel.lower():
                names = " ".join("+" + b.nick for b in self.bots.values())
                await user.send(":%s 332 %s %s :XG lab channel" % (SERVER, user.nick, channel))
                await user.send(":%s 353 %s = %s :%s %s" % (SERVER, user.nick, channel, user.nick, names))
                await user.send(":%s 366 %s %s :End of NAMES" % (SERVER, user.nick, channel))
                log("user_joined", nick=user.nick, channel=channel)
                asyncio.ensure_future(self.announce_to(user))
        elif command == "PRIVMSG":
            target, _, text = rest.partition(" :")
            bot = self.bots.get(target.lower())
            log("user_privmsg", nick=user.nick, target=target, text=text)
            if bot:
                if text.startswith("\x01"):
                    if text.upper().startswith("\x01VERSION"):
                        await user.notice(bot.nick, "\x01VERSION iroffer-dinoex 3.33 [lab]\x01")
                else:
                    await bot.on_private(user, text)
        elif command in ("MODE", "WHO", "USERHOST", "ISON"):
            pass
        elif command == "QUIT":
            user.writer.close()

    async def announce_to(self, user):
        await asyncio.sleep(1)
        while user in self.users:
            for bot in self.bots.values():
                for text in bot.announce_lines():
                    await user.send(":%s!bot@%s PRIVMSG %s :%s" % (bot.nick, SERVER, self.channel, text))
            await asyncio.sleep(self.announce_every)


async def main(path):
    with open(path) as f:
        config = json.load(f)
    network = Network(config)
    server = await asyncio.start_server(network.handle, "127.0.0.1", config.get("port", 16667), reuse_address=True)
    log("irc_listening", port=config.get("port", 16667), bots=[b.nick for b in network.bots.values()])
    async with server:
        await server.serve_forever()


if __name__ == "__main__":
    asyncio.run(main(sys.argv[1]))
