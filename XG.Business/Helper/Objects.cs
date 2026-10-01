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
using XG.Business.Model;
using XG.Model;
using XG.Model.Domain;
using System.Collections.Generic;

namespace XG.Business.Helper
{
	public static class Objects
	{
		public static void CheckAndRemoveDuplicates(Servers aServers)
		{
			IEnumerable<Server> servers = (from server in aServers.All select server).ToArray();
			foreach(var obj in servers.GroupBy(obj => obj.Name).Where(list => list.Count() > 1).Select(list => list.Skip(1)).SelectMany(list => list))
			{
				obj.Parent.Remove(obj);
			}

			IEnumerable<Channel> channels = (from server in servers from channel in server.Channels select channel).ToArray();
			foreach(var obj in channels.GroupBy(obj => obj.Parent.Name + "/" + obj.Name).Where(list => list.Count() > 1).Select(list => list.Skip(1)).SelectMany(list => list))
			{
				obj.Parent.RemoveChannel(obj);
			}

			IEnumerable<Bot> bots = (from channel in channels from bot in channel.Bots select bot).ToArray();
			foreach(var obj in bots.GroupBy(obj => obj.Parent.Parent.Name + "/" + obj.Parent.Name + "/" + obj.Name).Where(list => list.Count() > 1).Select(list => list.Skip(1)).SelectMany(list => list))
			{
				obj.Parent.RemoveBot(obj);
			}

			IEnumerable<Packet> packets = (from bot in bots from packet in bot.Packets select packet).ToArray();
			foreach(var obj in packets.GroupBy(obj => obj.Parent.Parent.Parent.Name + "/" + obj.Parent.Parent.Name + "/" + obj.Parent.Name + "/" + obj.Id).Where(list => list.Count() > 1).Select(list => list.Skip(1)).SelectMany(list => list))
			{
				obj.Parent.RemovePacket(obj);
			}
		}

		/// <summary>
		/// A bot sitting in several of our channels, also on different networks, announces each packet in all of them.
		/// Returns the copy an online bot of that name already offers somewhere else, or null.
		/// </summary>
		public static Packet PacketOfSameBotElsewhere(Channel aChannel, string aBotName, int aId, string aName, Int64 aSize)
		{
			var server = aChannel.Parent as Server;
			var servers = server != null ? server.Parent as Servers : null;
			if (servers == null)
			{
				return null;
			}

			foreach (var otherServer in servers.All)
			{
				foreach (var channel in otherServer.Channels)
				{
					if (channel == aChannel)
					{
						continue;
					}
					var bot = channel.Bot(aBotName);
					if (bot == null || !bot.Connected)
					{
						continue;
					}
					var packet = bot.Packet(aId);
					if (packet != null && packet.Name == aName && packet.Size == aSize)
					{
						return packet;
					}
				}
			}
			return null;
		}

		/// <summary>
		/// Removes packets a bot of the same name offers in another of our channels too, keeping one copy.
		/// Copies of online bots are kept first, and a packet which is queued or downloading is never removed.
		/// </summary>
		public static int RemoveDuplicatePackets(Servers aServers)
		{
			Bot[] bots = (from server in aServers.All from channel in server.Channels from bot in channel.Bots select bot).ToArray();

			int removed = 0;
			foreach (var sameBots in bots.GroupBy(bot => bot.Name.Trim().ToLower()).Where(group => group.Count() > 1))
			{
				Bot[] ordered = sameBots.OrderByDescending(bot => bot.Connected).ToArray();

				var kept = new HashSet<string>();
				foreach (var bot in ordered)
				{
					foreach (var packet in bot.Packets.Where(IsBusy))
					{
						kept.Add(DuplicateKey(packet));
					}
				}
				foreach (var bot in ordered)
				{
					foreach (var packet in bot.Packets.Where(packet => !IsBusy(packet)))
					{
						if (!kept.Add(DuplicateKey(packet)))
						{
							bot.RemovePacket(packet);
							removed++;
						}
					}
				}
			}
			return removed;
		}

		static bool IsBusy(Packet aPacket)
		{
			return aPacket.Enabled || aPacket.Connected || aPacket.File != null;
		}

		static string DuplicateKey(Packet aPacket)
		{
			return aPacket.Id + "|" + aPacket.Size + "|" + aPacket.Name;
		}
	}
}
