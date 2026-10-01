// 
//  Objects.cs
//  This file is part of XG - XDCC Grabscher
//  http://www.larsformella.de/lang/en/portfolio/programme-software/xg
//
//  Author:
//       Lars Formella <ich@larsformella.de>
// 
//  Copyright (c) 2012 Lars Formella
// 
//  This program is free software; you can redistribute it and/or modify
//  it under the terms of the GNU General Public License as published by
//  the Free Software Foundation; either version 2 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU General Public License for more details.
// 
//  You should have received a copy of the GNU General Public License
//  along with this program; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
//  

using System;
using System.Linq;
using NUnit.Framework;
using XG.Model.Domain;

namespace XG.Test.Business.Helper
{
	[TestFixture]
	public class Objects
	{
		const int count = 10;
		const int fakeId = 5;

		[Test]
		public void CheckAndRemoveDuplicatesTest()
		{
			var servers = new Servers();
			for (int a = 1; a <= count; a++)
			{
				servers.Add(createServer("server " + a));
			}
			servers.Named("server " + count).Name = "server " + fakeId;

			Assert.AreEqual(count, servers.All.Count());
			Assert.AreEqual(count * count, (from server in servers.All from channel in server.Channels select channel).Count());
			Assert.AreEqual(count * count * count, (from server in servers.All from channel in server.Channels from bot in channel.Bots select bot).Count());
			Assert.AreEqual(count * count * count * count, (from server in servers.All from channel in server.Channels from bot in channel.Bots from packet in bot.Packets select packet).Count());

			XG.Business.Helper.Objects.CheckAndRemoveDuplicates(servers);

			int newCount = count - 1;
			Assert.AreEqual(newCount, servers.All.Count());
			Assert.AreEqual(newCount * newCount, (from server in servers.All from channel in server.Channels select channel).Count());
			Assert.AreEqual(newCount * newCount * newCount, (from server in servers.All from channel in server.Channels from bot in channel.Bots select bot).Count());
			Assert.AreEqual(newCount * newCount * newCount * newCount, (from server in servers.All from channel in server.Channels from bot in channel.Bots from packet in bot.Packets select packet).Count());
		}

		[Test]
		public void RemoveDuplicatePacketsTest()
		{
			var servers = new Servers();
			var rizon = new Server { Name = "irc.rizon.net" };
			var coreirc = new Server { Name = "irc.coreirc.net" };
			servers.Add(rizon);
			servers.Add(coreirc);
			var channelA = new Channel { Name = "#a" };
			var channelB = new Channel { Name = "#b" };
			rizon.AddChannel(channelA);
			coreirc.AddChannel(channelB);

			// the same bot sits in both channels, online only in the second one
			var offlineBot = new Bot { Name = "[EWG]DB", Connected = false };
			var onlineBot = new Bot { Name = "[ewg]db", Connected = true };
			channelA.AddBot(offlineBot);
			channelB.AddBot(onlineBot);
			var otherBot = new Bot { Name = "OtherBot", Connected = true };
			channelA.AddBot(otherBot);

			for (int id = 1; id <= 3; id++)
			{
				offlineBot.AddPacket(new Packet { Id = id, Name = "File" + id + ".mkv", Size = id * 100 });
				onlineBot.AddPacket(new Packet { Id = id, Name = "File" + id + ".mkv", Size = id * 100 });
				otherBot.AddPacket(new Packet { Id = id, Name = "File" + id + ".mkv", Size = id * 100 });
			}
			// a packet only one copy has, and one whose number now offers another file
			offlineBot.AddPacket(new Packet { Id = 4, Name = "Only.Here.mkv", Size = 400 });
			onlineBot.AddPacket(new Packet { Id = 5, Name = "New.mkv", Size = 500 });
			offlineBot.AddPacket(new Packet { Id = 5, Name = "Old.mkv", Size = 500 });
			// a packet XG downloads is never removed, even from the offline copy
			offlineBot.Packet(2).Enabled = true;

			Assert.AreEqual(3, XG.Business.Helper.Objects.RemoveDuplicatePackets(servers));

			CollectionAssert.AreEquivalent(new[] { 2, 4, 5 }, offlineBot.Packets.Select(p => p.Id));
			CollectionAssert.AreEquivalent(new[] { 1, 3, 5 }, onlineBot.Packets.Select(p => p.Id));
			Assert.AreEqual(3, otherBot.Packets.Count(), "another bot offering the same files is a source of its own");

			Assert.AreEqual(0, XG.Business.Helper.Objects.RemoveDuplicatePackets(servers));
		}

		[Test]
		public void PacketOfSameBotElsewhereTest()
		{
			var servers = new Servers();
			var server = new Server { Name = "irc.rizon.net" };
			servers.Add(server);
			var channelA = new Channel { Name = "#a" };
			var channelB = new Channel { Name = "#b" };
			server.AddChannel(channelA);
			server.AddChannel(channelB);
			var bot = new Bot { Name = "[EWG]DB", Connected = true };
			channelA.AddBot(bot);
			bot.AddPacket(new Packet { Id = 7, Name = "Some.Show.S01E01.mkv", Size = 1000 });

			Assert.IsNotNull(XG.Business.Helper.Objects.PacketOfSameBotElsewhere(channelB, "[ewg]db", 7, "Some.Show.S01E01.mkv", 1000));
			Assert.IsNull(XG.Business.Helper.Objects.PacketOfSameBotElsewhere(channelA, "[EWG]DB", 7, "Some.Show.S01E01.mkv", 1000), "the own channel does not count");
			Assert.IsNull(XG.Business.Helper.Objects.PacketOfSameBotElsewhere(channelB, "[EWG]DB", 7, "Other.File.mkv", 1000));
			Assert.IsNull(XG.Business.Helper.Objects.PacketOfSameBotElsewhere(channelB, "[EWG]DB", 7, "Some.Show.S01E01.mkv", 2000));
			Assert.IsNull(XG.Business.Helper.Objects.PacketOfSameBotElsewhere(channelB, "OtherBot", 7, "Some.Show.S01E01.mkv", 1000));

			bot.Connected = false;
			Assert.IsNull(XG.Business.Helper.Objects.PacketOfSameBotElsewhere(channelB, "[EWG]DB", 7, "Some.Show.S01E01.mkv", 1000), "a copy of an offline bot can not be downloaded");
		}

		Server createServer(String aName)
		{
			var server = new Server { Name = aName };
			for (int a = 1; a <= count; a++)
			{
				server.AddChannel(createChannel("channel " + a));
			}
			server.Named("channel " + count).Name = "channel " + fakeId;
			return server;
		}

		Channel createChannel(String aName)
		{
			var channel = new Channel { Name = aName };
			for (int a = 1; a <= count; a++)
			{
				channel.AddBot(createBot("bot " + a));
			}
			channel.Named("bot " + count).Name = "bot " + fakeId;
			return channel;
		}

		Bot createBot(String aName)
		{
			var bot = new Bot { Name = aName };
			for (int a = 1; a <= count; a++)
			{
				bot.AddPacket(createPacket(a));
			}
			bot.Packet(count).Id = fakeId;
			return bot;
		}

		Packet createPacket(int aId)
		{
			var packet = new Packet { Id = aId };
			return packet;
		}
	}
}
