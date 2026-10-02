// 
//  Packet.cs
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
using System.Threading;
using XG.Extensions;
using XG.Model.Domain;

namespace XG.Plugin.Irc.Parser.Types.Info
{
	public class Packet : AParser
	{
		public override bool Parse(Message aMessage)
		{
			string[] regexes =
			{
				"#(?<pack_id>\\d+)(\u0240|)\\s+(\\d*)x\\s+\\[\\s*(?<pack_size>[\\<\\>\\d.]+)(?<pack_add>[BbGgiKMs]+)\\]\\s+(?<pack_name>.*)"
			};
			var match = Helper.Match(aMessage.Text, regexes);
			if (match.Success)
			{
				string tUserName = aMessage.Nick;
				Bot tBot = aMessage.Channel.Bot(tUserName);
				Model.Domain.Packet newPacket = null;

				bool insertBot = false;
				if (tBot == null)
				{
					insertBot = true;
					tBot = new Bot {Name = tUserName, Connected = true, LastMessage = "initial creation", LastContact = DateTime.Now};
				}

				try
				{
					int tPacketId;
					try
					{
						tPacketId = int.Parse(match.Groups["pack_id"].ToString());
					}
					catch (Exception ex)
					{
						Log.Fatal("Parse() " + tBot + " - can not parse packet id from string: " + aMessage, ex);
						return false;
					}

					string name = RemoveSpecialIrcCharsFromPacketName(match.Groups["pack_name"].ToString());
					Int64? size = ParseSize(match);

					Model.Domain.Packet tPack = tBot.Packet(tPacketId);
					if (tPack == null)
					{
						// a bot in several of our channels announces each packet in all of them, one copy is enough
						if (XG.Business.Helper.Objects.PacketOfSameBotElsewhere(aMessage.Channel, tUserName, tPacketId, name, size ?? 0) != null)
						{
							return true;
						}

						tPack = new Model.Domain.Packet();
						newPacket = tPack;
						tPack.Id = tPacketId;
						tBot.AddPacket(tPack);
					}
					tPack.LastMentioned = DateTime.Now;

					// older versions dropped every non-ASCII letter and apostrophe; such a name is still the same file
					bool sameFile = tPack.Name == LegacyName(name);
					if (tPack.Name != name && tPack.Name != "" && !sameFile)
					{
						tPack.Enabled = false;
						if (!tPack.Connected)
						{
							tPack.RealName = "";
							tPack.RealSize = 0;
						}
					}
					if (sameFile && tPack.Name != name)
					{
						var lastUpdated = tPack.LastUpdated;
						tPack.Name = name;
						tPack.LastUpdated = lastUpdated;
					}
					tPack.Name = name;

					if (size.HasValue)
					{
						tPack.Size = size.Value;
					}

					if (tPack.Commit() && newPacket == null)
					{
						Log.Info("Parse() updated " + tPack + " from " + tBot);
					}
				}
				catch (FormatException) {}

				// insert bot if ok
				if (insertBot)
				{
					if (aMessage.Channel.AddBot(tBot))
					{
						Log.Info("Parse() inserted " + tBot);
					}
					else
					{
						var duplicateBot = aMessage.Channel.Bot(tBot.Name);
						if (duplicateBot != null)
						{
							tBot = duplicateBot;
						}
						else
						{
							Log.Error("Parse() cant insert " + tBot + " into " + aMessage.Channel);
						}
					}
				}
				// and insert packet _AFTER_ this
				if (newPacket != null)
				{
					tBot.AddPacket(newPacket);
					Log.Info("Parse() inserted " + newPacket + " into " + tBot);
				}

				tBot.Commit();
				aMessage.Channel.Commit();
			}
			return match.Success;
		}

		#region HELPER

		/// <summary>
		/// The announced size in bytes, or null for a unit XG does not know.
		/// </summary>
		static Int64? ParseSize(System.Text.RegularExpressions.Match aMatch)
		{
			double sizeFormated;
			string stringSize = aMatch.Groups["pack_size"].ToString().Replace("<", "").Replace(">", "");
			if (Thread.CurrentThread.CurrentCulture.NumberFormat.NumberDecimalSeparator == ",")
			{
				stringSize = stringSize.Replace('.', ',');
			}
			double.TryParse(stringSize, out sizeFormated);

			switch (aMatch.Groups["pack_add"].ToString().ToLower())
			{
				case "k":
				case "kb":
					return (Int64) (sizeFormated * 1024);

				case "m":
				case "mb":
					return (Int64) (sizeFormated * 1024 * 1024);

				case "g":
				case "gb":
					return (Int64) (sizeFormated * 1024 * 1024 * 1024);
			}
			return null;
		}

		static string LegacyName(string aName)
		{
			return System.Text.RegularExpressions.Regex.Replace(aName, @"[^a-z0-9,.;:_\(\)\[\]\s-]", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
		}

		string RemoveSpecialIrcCharsFromPacketName(string aData)
		{
			string tData = Helper.RemoveSpecialIrcChars(aData);
			tData = tData.Replace("  ", " ");
			return tData.RemoveSpecialChars();
		}

		#endregion
	}
}
