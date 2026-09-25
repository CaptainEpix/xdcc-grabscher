//
//  CompatTestData.cs
//  This file is part of XG - XDCC Grabscher
//  http://www.larsformella.de/lang/en/portfolio/programme-software/xg
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
using System.Xml;
using XG.Model.Domain;

namespace XG.Test.Plugin.Webserver.Compat
{
	/// <summary>
	/// A small packet tree, indexed by the webserver search.
	/// </summary>
	public class CompatTestData
	{
		public const string ApiKey = "2f0b6f5c-8d4c-4a57-9d3e-0d7f5c3a1b11";
		public const string ApiUrl = "http://xg.test:5556/newznab/api";

		public Servers Servers { get; private set; }
		public Bot OnlineBot { get; private set; }
		public Bot OfflineBot { get; private set; }

		public Packet Movie { get; private set; }
		public Packet Episode205 { get; private set; }
		public Packet Episode206 { get; private set; }
		public Packet Episode301 { get; private set; }
		public Packet Daily { get; private set; }
		public Packet Special { get; private set; }
		public Packet OfflineMovie { get; private set; }

		public CompatTestData()
		{
			Servers = new Servers();
			var server = new Server { Name = "irc.test" };
			Servers.Add(server);
			var channel = new Channel { Name = "#test" };
			server.AddChannel(channel);

			OnlineBot = new Bot { Name = "[XG]Online", Connected = true };
			OfflineBot = new Bot { Name = "[XG]Offline", Connected = false };
			channel.AddBot(OnlineBot);
			channel.AddBot(OfflineBot);

			var time = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
			Movie = Add(OnlineBot, 1, "Some.Movie.2024.1080p.BluRay.x264-GRP.mkv", 1000, time.AddMinutes(1));
			Episode205 = Add(OnlineBot, 2, "Some.Show.S02E05.720p.HDTV.x264-GRP.mkv", 2000, time.AddMinutes(2));
			Episode206 = Add(OnlineBot, 3, "Some.Show.S02E06.720p.HDTV.x264-GRP.mkv", 2001, time.AddMinutes(3));
			Episode301 = Add(OnlineBot, 4, "Some.Show.S03E01.720p.HDTV.x264-GRP.mkv", 2002, time.AddMinutes(4));
			Daily = Add(OnlineBot, 5, "Daily.Show.2024.05.12.720p.WEB.mkv", 3000, time.AddMinutes(5));
			Special = Add(OnlineBot, 6, "Tom & Jerry <Special> \"Quoted\".mkv", 4000, time.AddMinutes(6));
			OfflineMovie = Add(OfflineBot, 1, "Some.Movie.2024.720p.WEB.mkv", 500, time.AddMinutes(7));

			XG.Plugin.Webserver.Search.Packets.Servers = Servers;
			XG.Plugin.Webserver.Search.Packets.Initialize();
		}

		static Packet Add(Bot aBot, int aId, string aName, Int64 aSize, DateTime aLastMentioned)
		{
			// in these tests a packet started to offer its file when it was mentioned
			var packet = new Packet { Id = aId, Name = aName, Size = aSize, LastMentioned = aLastMentioned };
			packet.LastUpdated = aLastMentioned;
			aBot.AddPacket(packet);
			return packet;
		}

		public static bool IsValidKey(string aKey)
		{
			return aKey == ApiKey;
		}

		public static IDictionary<string, string> Query(params string[] aPairs)
		{
			var query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			for (int a = 0; a + 1 < aPairs.Length; a += 2)
			{
				query[aPairs[a]] = aPairs[a + 1];
			}
			return query;
		}

		public static XmlDocument Xml(byte[] aBody)
		{
			var doc = new XmlDocument();
			doc.LoadXml(System.Text.Encoding.UTF8.GetString(aBody));
			return doc;
		}

		public static XmlNamespaceManager Namespaces(XmlDocument aDoc)
		{
			var manager = new XmlNamespaceManager(aDoc.NameTable);
			manager.AddNamespace("newznab", XG.Plugin.Webserver.Compat.Newznab.NewznabXml.AttributeNamespace);
			return manager;
		}
	}
}
