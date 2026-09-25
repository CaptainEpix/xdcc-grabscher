//
//  DownloadFromBot.cs
//  This file is part of XG - XDCC Grabscher
//  http://www.larsformella.de/lang/en/portfolio/programme-software/xg
//
//  Author:
//       Lars Formella <ich@larsformella.de>
//
//  Copyright (c) 2013 Lars Formella
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
using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using XG.Extensions;
using XG.Model.Domain;

namespace XG.Test.Plugin.Irc.Parser.Types.Dcc
{
	[TestFixture]
	public class DownloadFromBot : AParser
	{
		[Test]
		public void DccDownloadTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
			EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
			parser.OnAddDownload += (sender, e) => raisedEvent = e;

			raisedEvent = null;
			Parse(parser, "\u0001DCC SEND Testfile.with.a.long.name.mkv 1203194610 45000 975304559\u0001");

			Assert.AreEqual(0, raisedEvent.Value2);
			Assert.AreEqual("71.183.74.242", raisedEvent.Value3.ToString());
			Assert.AreEqual(45000, raisedEvent.Value4);
		}

		readonly List<Packet> _addedPackets = new List<Packet>();

		Packet AddPacket(int aId, string aName, bool aEnabled)
		{
			var packet = new Packet { Id = aId, Name = aName, Size = 100 };
			Bot.AddPacket(packet);
			packet.Enabled = aEnabled;
			_addedPackets.Add(packet);
			return packet;
		}

		// the fixture objects are shared by all tests of this class
		[TearDown]
		public void TearDown()
		{
			foreach (var packet in _addedPackets)
			{
				Bot.RemovePacket(packet);
			}
			_addedPackets.Clear();
			Packet.Enabled = true;
			Packet.Connected = false;
		}

		[Test]
		public void DccOfferMatchedByNameTest()
		{
			// the older enabled packet must not take an offer for another file
			var other = AddPacket(2, "Other.File.S01E02.mkv", true);
			var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
			EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
			parser.OnAddDownload += (sender, e) => raisedEvent = e;

			Parse(parser, "\u0001DCC SEND Other.File.S01E02.mkv 1203194610 45001 100\u0001");

			Assert.IsNotNull(raisedEvent);
			Assert.AreSame(other, raisedEvent.Value1);
			Assert.AreEqual(45001, raisedEvent.Value4);
		}

		[Test]
		public void DccOfferWithQuotedNameTest()
		{
			var other = AddPacket(2, "Other File With Spaces.mkv", true);
			var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
			EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
			parser.OnAddDownload += (sender, e) => raisedEvent = e;

			Parse(parser, "\u0001DCC SEND \"Other File With Spaces.mkv\" 1203194610 45002 100\u0001");

			Assert.AreSame(other, raisedEvent.Value1);
		}

		[Test]
		public void StaleDccOfferIsCancelledTest()
		{
			// the bot re-sends its pending offer for a packet XG already gave up on
			Packet.Enabled = false;
			AddPacket(2, "Other.File.S01E02.mkv", true);
			var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
			EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
			string sentMessage = null;
			parser.OnAddDownload += (sender, e) => raisedEvent = e;
			parser.OnSendMessage += (sender, e) => sentMessage = e.Value3 + ": " + e.Value4;

			Parse(parser, "\u0001DCC SEND Testfile.with.a.long.name.mkv 1203194610 45000 975304559\u0001");

			Assert.IsNull(raisedEvent, "a stale offer must not start a download for another packet");
			Assert.AreEqual(Bot.Name + ": XDCC CANCEL", sentMessage);
		}

		[Test]
		public void PassiveDccOfferTest()
		{
			// port 0 asks XG to listen (reverse DCC), which it does not support
			var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
			EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
			Notification notification = null;
			parser.OnAddDownload += (sender, e) => raisedEvent = e;
			parser.OnNotificationAdded += (sender, e) => notification = e.Value1;

			Parse(parser, "\u0001DCC SEND Testfile.with.a.long.name.mkv 1203194610 0 975304559 1234\u0001");

			Assert.IsNull(raisedEvent);
			Assert.IsFalse(Packet.Enabled);
			Assert.AreEqual(Notification.Types.BotSubmittedWrongData, notification.Type);
			// the message template names the packet and its bot
			Assert.AreSame(Packet, notification.Object1);
		}

		[Test]
		public void FirstOfferResumesPartFromAnotherBotTest()
		{
			// a part of the file is there from another bot; this packet was never offered, so it has no real name yet
			var packet = AddPacket(2, "Other.File.S01E02.mkv", true);
			Packet.Enabled = false;
			var part = new XG.Model.Domain.File("Other.File.S01E02.mkv", 5000000);
			part.CurrentSize = 2000000;
			var files = new Files();
			files.Add(part);
			var oldFiles = XG.Business.Helper.FileActions.Files;
			var oldTempPath = XG.Config.Properties.Settings.Default.TempPath;
			XG.Config.Properties.Settings.Default.TempPath = System.IO.Path.GetTempPath();
			string partPath = XG.Config.Properties.Settings.Default.TempPath + part.TmpName;
			System.IO.File.WriteAllBytes(partPath, new byte[0]);
			try
			{
				XG.Business.Helper.FileActions.Files = files;
				var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
				EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
				string sentMessage = null;
				parser.OnAddDownload += (sender, e) => raisedEvent = e;
				parser.OnSendMessage += (sender, e) => sentMessage = e.Value4;

				Parse(parser, "\u0001DCC SEND Other.File.S01E02.mkv 1203194610 45004 5000000\u0001");

				Assert.IsNull(raisedEvent, "the download must not start from the beginning");
				Assert.AreEqual("DCC RESUME Other.File.S01E02.mkv 45004 " + (2000000 - XG.Config.Properties.Settings.Default.FileRollbackBytes), sentMessage);
				Assert.AreEqual("Other.File.S01E02.mkv", packet.RealName);
			}
			finally
			{
				XG.Business.Helper.FileActions.Files = oldFiles;
				XG.Config.Properties.Settings.Default.TempPath = oldTempPath;
				System.IO.File.Delete(partPath);
			}
		}

		[Test]
		public void UnknownOfferNameUsesOldestPacketTest()
		{
			// some bots send a different file name than they list; keep the old behaviour then
			AddPacket(2, "Other.File.S01E02.mkv", true);
			var parser = new XG.Plugin.Irc.Parser.Types.Dcc.DownloadFromBot();
			EventArgs<Packet, Int64, IPAddress, int> raisedEvent = null;
			parser.OnAddDownload += (sender, e) => raisedEvent = e;

			Parse(parser, "\u0001DCC SEND Completely.Different.Name.mkv 1203194610 45003 100\u0001");

			Assert.AreSame(Packet, raisedEvent.Value1);
		}
	}
}
