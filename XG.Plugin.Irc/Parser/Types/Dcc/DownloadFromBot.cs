// 
//  ExistingBot.cs
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
using System.Net;
using System.Text.RegularExpressions;
using Meebey.SmartIrc4net;
using XG.Business.Helper;
using XG.Config.Properties;
using XG.Extensions;
using XG.Model.Domain;

namespace XG.Plugin.Irc.Parser.Types.Dcc
{
	public class DownloadFromBot : AParser
	{
		public override bool Parse(Message aMessage)
		{
			if (!aMessage.Text.StartsWith("\u0001DCC ", StringComparison.Ordinal))
			{
				return false;
			}
			// bots send all kinds of broken offers, never trust the number of fields
			string text = aMessage.Text.Substring(5).TrimEnd('\u0001');

			Bot tBot = aMessage.Channel.Bot(aMessage.Nick);
			if (tBot == null)
			{
				return false;
			}

			string offeredName = OfferedFileName(text);
			Packet tPacket = FindEnabledPacket(tBot, offeredName);
			if (tPacket == null)
			{
				// a bot re-sends a pending offer when it is asked again; if that offer is for a
				// packet we gave up on, tell the bot to drop it instead of using it for another packet
				if (offeredName != null && tBot.Packets.Any(p => !p.Enabled && NameMatches(p, offeredName)))
				{
					Log.Warn("Parse() cancelling stale DCC offer from " + tBot + " for " + offeredName);
					FireSendMessage(this, new EventArgs<Server, SendType, string, string>(aMessage.Channel.Parent, SendType.Message, tBot.Name, "XDCC CANCEL"));
					return true;
				}
				tPacket = tBot.OldestActivePacket();
			}
			if (tPacket == null)
			{
				Log.Error("Parse() DCC not activated from " + tBot);
				return false;
			}

			if (tPacket.Connected)
			{
				Log.Error("Parse() ignoring dcc from " + tBot + " because " + tPacket + " is already connected");
				return false;
			}

			bool isOk = false;
			// passive (reverse) DCC: the bot sends port 0 and a token and connects to XG instead
			bool passive = false;
			string token = null;

			int tPort = 0;
			File tFile = FileActions.TryGetFile(tPacket.RealName, tPacket.RealSize);
			Int64 startSize = 0;

			if (tFile != null)
			{
				if (tFile.Connected)
				{
					return false;
				}
				startSize = ResumePosition(tFile);
			}

			string[] tDataList = text.Split(' ');
			// if the name of the file contains spaces, we have to replace em
			Match tQuoted = QuotedOffer.Match(text);
			if (tQuoted.Success)
			{
				tDataList = (tQuoted.Groups["command"] + " " + tQuoted.Groups["packet_name"].ToString().Replace(" ", "_").Replace("'", "") + tQuoted.Groups["bot_data"]).Split(' ');
			}

			if (tDataList[0] == "SEND")
			{
				Log.Info("Parse() DCC from " + tBot);

				try
				{
					tBot.IP = IPAddress.Parse(tDataList[2]);
				}
				catch (Exception)
				{
					return RejectOffer(tBot, tPacket, "no valid ip", aMessage);
				}

				try
				{
					tPort = int.Parse(tDataList[3]);
				}
				catch (Exception)
				{
					return RejectOffer(tBot, tPacket, "no valid port", aMessage);
				}

				if (tPort == 0)
				{
					// port 0 asks the client to listen instead
					tBot.PassiveDccTime = DateTime.Now;
					tBot.Commit();
					if (PassiveDcc.Enabled && tDataList.Length > 5)
					{
						passive = true;
						token = tDataList[5];
						Log.Info("Parse() " + tBot + " offers a passive DCC transfer");
					}
				}

				// we cant connect to port <= 0
				if (tPort < 0 || tPort > 65535 || (tPort == 0 && !passive))
				{
					Log.Error("Parse() " + tBot + " submitted wrong port: " + tPort + (tPort == 0 ? " (passive DCC is not configured, see XG_PASSIVE_DCC_PORTS)" : "") + ", disabling packet");
					tPacket.Enabled = false;
					tPacket.Commit();

					FireNotificationAdded(Notification.Types.BotSubmittedWrongData, tPacket);
					return false;
				}

				tPacket.RealName = tDataList[1];

				if (tPacket.Name.Difference(tPacket.RealName) > 0.7)
				{
					FireNotificationAdded(Notification.Types.PacketNameDifferent, tPacket);
				}

				try
				{
					tPacket.RealSize = Int64.Parse(tDataList[4]);
				}
				catch (Exception)
				{
					return RejectOffer(tBot, tPacket, "no valid size", aMessage);
				}

				if (tPacket.RealSize <= 0)
				{
					Log.Error("Parse() " + tBot + " submitted wrong file size: " + tPacket.RealSize + ", disabling packet");
					tPacket.Enabled = false;
					tPacket.Commit();

					FireNotificationAdded(Notification.Types.BotSubmittedWrongData, tPacket);
					return false;
				}

				// look again with the real name and size, a packet offered for the first time has neither,
				// but a part of the same file may already be there from another bot
				tFile = FileActions.TryGetFile(tPacket.RealName, tPacket.RealSize);
				startSize = tFile != null ? ResumePosition(tFile) : 0;

				if (tFile != null)
				{
					if (tFile.Connected)
					{
						Log.Error("Parse() file for " + tPacket + " from " + tBot + " already in use or not found, disabling packet");
						tPacket.Enabled = false;
						FireUnRequestFromBot(this, new EventArgs<Bot>(tBot));
					}
					else if (tFile.CurrentSize > 0)
					{
						Log.Info("Parse() try resume from " + tBot + " for " + tPacket + " @ " + startSize);
						string resume = passive
							? "DCC RESUME " + DccName(offeredName ?? tPacket.RealName) + " 0 " + startSize + " " + token
							: "DCC RESUME " + tPacket.RealName + " " + tPort + " " + startSize;
						FireSendMessage(this, new EventArgs<Server, SendType, string, string>(aMessage.Channel.Parent, SendType.CtcpRequest, tBot.Name, resume));
					}
					else
					{
						isOk = true;
					}
				}
				else
				{
					isOk = true;
				}
			}
			else if (tDataList[0] == "ACCEPT")
			{
				Log.Info("Parse() DCC resume accepted from " + tBot);

				try
				{
					tPort = int.Parse(tDataList[2]);
				}
				catch (Exception)
				{
					return RejectOffer(tBot, tPacket, "no valid port in the resume", aMessage);
				}

				try
				{
					startSize = Int64.Parse(tDataList[3]);
				}
				catch (Exception)
				{
					return RejectOffer(tBot, tPacket, "no valid position in the resume", aMessage);
				}

				if (tPort == 0)
				{
					// the resume of a passive offer, the bot connects once XG sent its address
					if (!PassiveDcc.Enabled || tDataList.Length < 5)
					{
						Log.Error("Parse() " + tBot + " accepted a passive resume, but passive DCC is not configured");
						return false;
					}
					passive = true;
					token = tDataList[4];
				}

				isOk = true;
			}

			tPacket.Commit();
			if (isOk && passive)
			{
				StartPassive(aMessage, tBot, tPacket, startSize, offeredName ?? tPacket.RealName, token);
			}
			else if (isOk)
			{
				Log.Info("Parse() downloading from " + tBot + " - Starting: " + startSize + " - Size: " + tPacket.RealSize);
				FireAddDownload(this, new EventArgs<Packet, long, IPAddress, int>(tPacket, startSize, tBot.IP, tPort));
			}
			return true;
		}

		/// <summary>
		/// Listens on a free passive DCC port and tells the bot where to connect to.
		/// </summary>
		void StartPassive(Message aMessage, Bot aBot, Packet aPacket, Int64 aStartSize, string aName, string aToken)
		{
			var address = PassiveDcc.PublicAddress;
			if (address == null)
			{
				Log.Error("Parse() can not answer the passive offer of " + aBot + ", the public address is unknown (set XG_PASSIVE_DCC_IP)");
				aPacket.Enabled = false;
				aPacket.Commit();
				FireNotificationAdded(Notification.Types.BotSubmittedWrongData, aPacket);
				return;
			}

			var reservation = PassiveDcc.Reserve(aPacket.Guid);
			if (reservation == null)
			{
				// all passive ports are busy, ask again when one is free; the bot re-sends its pending offer then
				FireNoFreeSlot(this, new EventArgs<Bot>(aBot));
				return;
			}

			Log.Info("Parse() downloading passive from " + aBot + " - Starting: " + aStartSize + " - Size: " + aPacket.RealSize + " - Port: " + reservation.ListenPort);
			FireAddDownload(this, new EventArgs<Packet, long, IPAddress, int>(aPacket, aStartSize, aBot.IP, reservation.ListenPort));
			if (reservation.Stopped)
			{
				// the download was not started, e.g. because of the download limit
				return;
			}
			FireSendMessage(this, new EventArgs<Server, SendType, string, string>(aMessage.Channel.Parent, SendType.CtcpRequest, aBot.Name,
				"DCC SEND " + DccName(aName) + " " + PassiveDcc.ToDccAddress(address) + " " + reservation.PublicPort + " " + aPacket.RealSize + " " + aToken));
		}

		/// <summary>
		/// A bot that answers with an offer XG can not use would otherwise be asked again and again forever.
		/// </summary>
		bool RejectOffer(Bot aBot, Packet aPacket, string aReason, Message aMessage)
		{
			Log.Error("Parse() " + aBot + " submitted wrong data (" + aReason + "): " + aMessage.Text + ", disabling packet");
			aPacket.Enabled = false;
			aPacket.Commit();
			FireNotificationAdded(Notification.Types.BotSubmittedWrongData, aPacket);
			return true;
		}

		static string DccName(string aName)
		{
			return aName.Contains(" ") ? "\"" + aName + "\"" : aName;
		}

		static readonly Regex QuotedOffer = new Regex("^(?<command>SEND|ACCEPT) \"(?<packet_name>.+)\"(?<bot_data>[^\"]+)$");

		static Int64 ResumePosition(File aFile)
		{
			return aFile.CurrentSize > Settings.Default.FileRollbackBytes ? aFile.CurrentSize - Settings.Default.FileRollbackBytes : 0;
		}

		/// <summary>
		/// The file name of a DCC SEND or DCC ACCEPT offer.
		/// </summary>
		static string OfferedFileName(string aText)
		{
			if (aText.StartsWith("SEND \"", StringComparison.Ordinal) || aText.StartsWith("ACCEPT \"", StringComparison.Ordinal))
			{
				Match tMatch = QuotedOffer.Match(aText);
				return tMatch.Success ? tMatch.Groups["packet_name"].ToString() : null;
			}
			string[] tDataList = aText.Split(' ');
			return tDataList.Length > 1 && (tDataList[0] == "SEND" || tDataList[0] == "ACCEPT") ? tDataList[1] : null;
		}

		/// <summary>
		/// The enabled packet the offer is for. Bots offer their files under the listed
		/// name, so offers are matched by name instead of taking the oldest enabled packet.
		/// </summary>
		static Packet FindEnabledPacket(Bot aBot, string aOfferedName)
		{
			if (aOfferedName == null)
			{
				return null;
			}
			return aBot.Packets.Where(p => p.Enabled && NameMatches(p, aOfferedName)).OrderBy(p => p.EnabledTime).FirstOrDefault();
		}

		static bool NameMatches(Packet aPacket, string aOfferedName)
		{
			string offered = Shrink(aOfferedName);
			if (offered == null)
			{
				return false;
			}
			foreach (string name in new[] { aPacket.RealName, aPacket.Name })
			{
				string candidate = Shrink(name);
				if (candidate != null && (candidate == offered || candidate.EndsWith(offered, StringComparison.Ordinal) || offered.EndsWith(candidate, StringComparison.Ordinal)))
				{
					return true;
				}
			}
			return false;
		}

		static string Shrink(string aName)
		{
			if (string.IsNullOrEmpty(aName))
			{
				return null;
			}
			string name = XG.Model.Domain.Helper.ShrinkFileName(aName, 0);
			// only the size suffix left, e.g. a name without latin letters or digits
			return name.Length > 2 ? name : null;
		}
	}
}
