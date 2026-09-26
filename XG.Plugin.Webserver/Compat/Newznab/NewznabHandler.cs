//
//  NewznabHandler.cs
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
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using log4net;
using XG.Business.Helper;
using XG.Model.Domain;

namespace XG.Plugin.Webserver.Compat.Newznab
{
	public class NewznabResponse
	{
		public string ContentType { get; set; }
		public byte[] Body { get; set; }
		public string FileName { get; set; }

		public static NewznabResponse Xml(string aXml)
		{
			return new NewznabResponse { ContentType = NewznabXml.ContentType, Body = Encoding.UTF8.GetBytes(aXml) };
		}
	}

	/// <summary>
	/// The Newznab indexer facade: caps, search, tvsearch, movie and get.
	/// </summary>
	public class NewznabHandler
	{
		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		readonly Func<string, bool> _isValidApiKey;

		public NewznabHandler(Func<string, bool> aIsValidApiKey)
		{
			_isValidApiKey = aIsValidApiKey;
		}

		/// <param name="aParameters">the query parameters, case insensitive</param>
		/// <param name="aApiUrl">the absolute url of this endpoint, used for download links</param>
		public NewznabResponse Handle(IDictionary<string, string> aParameters, string aApiUrl)
		{
			try
			{
				string function = NewznabRequest.NormalizeFunction(NewznabRequest.Get(aParameters, "t"));
				if (string.IsNullOrEmpty(function))
				{
					throw new NewznabException(NewznabException.MissingParameter, "Missing parameter (t)");
				}

				string apiKey = NewznabRequest.Get(aParameters, "apikey");
				if (string.IsNullOrWhiteSpace(apiKey))
				{
					throw new NewznabException(NewznabException.IncorrectCredentials, "Missing parameter (apikey)");
				}
				if (!_isValidApiKey(apiKey))
				{
					Log.Warn("Newznab request rejected: invalid or disabled api key");
					throw new NewznabException(NewznabException.IncorrectCredentials, "Incorrect user credentials");
				}

				switch (function)
				{
					case "caps":
						return NewznabResponse.Xml(NewznabXml.Capabilities());

					case "search":
					case "tvsearch":
					case "movie":
						return Search(NewznabRequest.Parse(aParameters), aApiUrl, apiKey);

					case "get":
						return Get(NewznabRequest.Get(aParameters, "id"), NewznabRequest.Get(aParameters, "v"));

					default:
						throw new NewznabException(NewznabException.NoSuchFunction, "No such function");
				}
			}
			catch (NewznabException ex)
			{
				return NewznabResponse.Xml(NewznabXml.Error(ex.Code, ex.Message));
			}
			catch (Exception ex)
			{
				Log.Error("Newznab request failed", ex);
				return NewznabResponse.Xml(NewznabXml.Error(NewznabException.UnknownError, "Internal error"));
			}
		}

		NewznabResponse Search(NewznabRequest aRequest, string aApiUrl, string aApiKey)
		{
			// bots announce their packets over and over, so the recent feed goes by when a packet started to offer its file
			string sort = aRequest.IsRecent ? "LastUpdated" : "LastMentioned";
			var result = Webserver.Search.Packets.GetResults(aRequest.Required, aRequest.Excluded, aRequest.Prefixes, aRequest.ShowOfflineBots, !PassiveDcc.Enabled, aRequest.Offset, aRequest.Limit, sort, true);

			var items = new List<NewznabItem>();
			foreach (var packet in result.Packets)
			{
				if (packet == null)
				{
					continue;
				}
				var item = CreateItem(packet, aRequest.Categories);
				if (string.IsNullOrWhiteSpace(item.Title))
				{
					continue;
				}
				item.DownloadUrl = aApiUrl + "?t=get&id=" + packet.Guid.ToString("D") + "&v=" + Fingerprint(packet) + "&apikey=" + Uri.EscapeDataString(aApiKey);
				items.Add(item);
			}

			Log.Info("Newznab " + aRequest.Function + (aRequest.IsRecent ? " (recent)" : " '" + string.Join(" ", aRequest.Required.Concat(aRequest.Prefixes.Select(p => p + "*"))) + "'") + " returned " + items.Count + " of " + result.Total);
			return NewznabResponse.Xml(NewznabXml.Results(items, aRequest.Offset, result.Total, aApiUrl));
		}

		NewznabResponse Get(string aId, string aFingerprint)
		{
			Guid guid;
			if (string.IsNullOrWhiteSpace(aId))
			{
				throw new NewznabException(NewznabException.MissingParameter, "Missing parameter (id)");
			}
			if (!Guid.TryParse(aId.Trim(), out guid))
			{
				throw new NewznabException(NewznabException.IncorrectParameter, "Incorrect parameter (id)");
			}

			var packet = Webserver.Search.Packets.GetPacket(guid);
			if (packet == null)
			{
				throw new NewznabException(NewznabException.NoSuchItem, "No such item");
			}
			// bots reuse pack numbers, so a search result may point to a different file by now
			if (!string.IsNullOrEmpty(aFingerprint) && aFingerprint != Fingerprint(packet))
			{
				Log.Warn("Newznab get " + packet.Guid + " rejected, the packet changed since it was found");
				throw new NewznabException(NewznabException.NoSuchItem, "No such item, the XG packet changed since it was found");
			}

			var nzb = new Nzb.SyntheticNzb(packet.Guid, PacketTitle(packet), PacketSize(packet));
			Log.Info("Newznab get " + packet.Guid + " '" + nzb.Title + "'");
			return new NewznabResponse
			{
				ContentType = Nzb.SyntheticNzb.ContentType,
				Body = nzb.ToBytes(),
				FileName = SafeFileName(nzb.Title) + ".nzb"
			};
		}

		public static NewznabItem CreateItem(Packet aPacket, int[] aCategories)
		{
			return new NewznabItem
			{
				Guid = aPacket.Guid,
				Title = PacketTitle(aPacket),
				Description = PacketDescription(aPacket),
				Size = PacketSize(aPacket),
				PublishDate = PublishDate(aPacket),
				Categories = aCategories
			};
		}

		/// <summary>
		/// Identifies what a packet offers, to notice when a bot reuses the pack number.
		/// </summary>
		public static string Fingerprint(Packet aPacket)
		{
			using (var sha1 = SHA1.Create())
			{
				var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(PacketTitle(aPacket) + "|" + PacketSize(aPacket)));
				return BitConverter.ToString(hash, 0, 6).Replace("-", "").ToLowerInvariant();
			}
		}

		public static string PacketTitle(Packet aPacket)
		{
			return !string.IsNullOrWhiteSpace(aPacket.RealName) ? aPacket.RealName : aPacket.Name;
		}

		public static Int64 PacketSize(Packet aPacket)
		{
			return aPacket.RealSize > 0 ? aPacket.RealSize : aPacket.Size;
		}

		static string PacketDescription(Packet aPacket)
		{
			var bot = aPacket.Parent;
			var channel = bot != null ? bot.Parent : null;
			var server = channel != null ? channel.Parent : null;
			return "XDCC #" + aPacket.Id + (bot != null ? " from " + bot.Name : "") + (channel != null ? " in " + channel.Name : "") + (server != null ? " on " + server.Name : "");
		}

		/// <summary>
		/// XG has no upload date. The best real value is the time a bot last announced the packet;
		/// if a packet was never announced the time it was last updated is used, and as a last
		/// resort the current time, because the packet is on offer right now.
		/// </summary>
		public static DateTime PublishDate(Packet aPacket)
		{
			// when the packet started to offer its file; announcements repeat, so they would make old packets look new
			var minimum = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
			if (aPacket.LastUpdated > minimum)
			{
				return aPacket.LastUpdated;
			}
			if (aPacket.LastMentioned > minimum)
			{
				return aPacket.LastMentioned;
			}
			return DateTime.UtcNow;
		}

		static string SafeFileName(string aName)
		{
			var builder = new StringBuilder();
			foreach (char c in aName ?? "")
			{
				builder.Append(char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_' || c == ' ' ? c : '_');
			}
			string name = builder.ToString().Trim();
			return name.Length > 0 ? name : "xg";
		}
	}
}
