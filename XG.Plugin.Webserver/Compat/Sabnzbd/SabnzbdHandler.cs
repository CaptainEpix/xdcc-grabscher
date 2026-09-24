//
//  SabnzbdHandler.cs
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
using System.Globalization;
using System.Linq;
using System.Reflection;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace XG.Plugin.Webserver.Compat.Sabnzbd
{
	/// <summary>
	/// The minimal SABnzbd api Sonarr, Radarr and Prowlarr use for their SABnzbd download client.
	/// This is a compatibility facade for XDCC downloads; XG never contacts a Usenet server.
	/// </summary>
	public class SabnzbdHandler
	{
		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		/// <summary>
		/// Reported SABnzbd version. The clients require at least 0.7.0 and switch
		/// behaviour on 1.1, 2.0 and 4.3; 4.3.0 selects the current code paths.
		/// </summary>
		public const string CompatibilityVersion = "4.3.0";

		/// <summary>
		/// Categories which always exist, so the default *Arr categories pass the connection test.
		/// </summary>
		public static readonly string[] DefaultCategories = { "tv", "movies", "xg", "prowlarr" };

		public const string ContentType = "application/json";

		readonly CompatJobTracker _tracker;
		readonly Func<string, bool> _isValidApiKey;
		readonly Func<string> _readyPath;

		public SabnzbdHandler(CompatJobTracker aTracker, Func<string, bool> aIsValidApiKey, Func<string> aReadyPath)
		{
			_tracker = aTracker;
			_isValidApiKey = aIsValidApiKey;
			_readyPath = aReadyPath;
		}

		/// <param name="aParameters">query and form parameters, case insensitive</param>
		/// <param name="aNzbUpload">the uploaded nzb of mode=addfile, if any</param>
		public string Handle(IDictionary<string, string> aParameters, byte[] aNzbUpload)
		{
			try
			{
				string mode = (Get(aParameters, "mode") ?? "").Trim().ToLowerInvariant();

				// like SABnzbd, the version is available without authentication
				if (mode == "version")
				{
					return Serialize(new JObject { { "version", CompatibilityVersion } });
				}

				string apiKey = Get(aParameters, "apikey");
				if (string.IsNullOrWhiteSpace(apiKey))
				{
					return Error("API Key Required");
				}
				if (!_isValidApiKey(apiKey))
				{
					Log.Warn("SABnzbd request rejected: invalid or disabled api key");
					return Error("API Key Incorrect");
				}

				switch (mode)
				{
					case "get_config":
						return GetConfig();
					case "fullstatus":
						return Serialize(new JObject { { "status", new JObject { { "completedir", CompleteDir() } } } });
					case "get_cats":
						return Serialize(new JObject { { "categories", new JArray(AllCategories().ToArray()) } });
					case "addfile":
						return AddFile(aParameters, aNzbUpload);
					case "addurl":
						return AddUrl(aParameters);
					case "queue":
						return Queue(aParameters);
					case "history":
						return History(aParameters);
					case "retry":
						return Retry(aParameters);
					case "":
						return Error("Missing mode");
					default:
						return Error("Not supported by XG");
				}
			}
			catch (SabnzbdException ex)
			{
				return Error(ex.Message);
			}
			catch (Exception ex)
			{
				Log.Error("SABnzbd request failed", ex);
				return Error("Internal error");
			}
		}

		#region MODES

		string GetConfig()
		{
			var categories = new JArray();
			categories.Add(Category("*", "Default"));
			foreach (string category in AllCategories())
			{
				categories.Add(Category(category, "Default"));
			}

			// sorting must be off and history kept, otherwise the *Arr clients complain
			var misc = new JObject
			{
				{ "complete_dir", CompleteDir() },
				{ "tv_categories", new JArray() },
				{ "enable_tv_sorting", false },
				{ "movie_categories", new JArray() },
				{ "enable_movie_sorting", false },
				{ "date_categories", new JArray() },
				{ "enable_date_sorting", false },
				{ "pre_check", false },
				{ "history_retention", "" },
				{ "history_retention_option", "all" },
				{ "history_retention_number", 0 }
			};

			return Serialize(new JObject
			{
				{ "config", new JObject
					{
						{ "misc", misc },
						{ "categories", categories },
						{ "servers", new JArray() },
						{ "sorters", new JArray() }
					}
				}
			});
		}

		static JObject Category(string aName, string aScript)
		{
			// an empty dir means the complete dir itself; XG has no category folders
			return new JObject
			{
				{ "name", aName },
				{ "order", 0 },
				{ "pp", "" },
				{ "script", aScript },
				{ "dir", "" },
				{ "newzbin", "" },
				{ "priority", -100 }
			};
		}

		IEnumerable<string> AllCategories()
		{
			return DefaultCategories.Concat(_tracker.Categories().Where(c => c != "*")).Distinct(StringComparer.OrdinalIgnoreCase);
		}

		string AddFile(IDictionary<string, string> aParameters, byte[] aNzbUpload)
		{
			if (aNzbUpload == null || aNzbUpload.Length == 0)
			{
				return Error("No NZB file uploaded");
			}

			Nzb.SyntheticNzb nzb;
			try
			{
				nzb = Nzb.SyntheticNzb.Parse(aNzbUpload);
			}
			catch (FormatException ex)
			{
				Log.Warn("addfile rejected: " + ex.Message);
				return Error("Not an XG NZB: " + ex.Message);
			}

			return Add(nzb, aParameters);
		}

		/// <summary>
		/// Prowlarr sends the download link instead of the nzb when its indexer is set to redirect.
		/// Only XG's own Newznab links are accepted and nothing is fetched: the packet guid is
		/// taken from the link and validated like an uploaded nzb.
		/// </summary>
		string AddUrl(IDictionary<string, string> aParameters)
		{
			string url = Get(aParameters, "name");
			Uri uri;
			if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri))
			{
				return Error("Missing or invalid url");
			}

			var query = ParseQuery(uri.Query);
			if (!uri.AbsolutePath.TrimEnd('/').EndsWith("/api", StringComparison.OrdinalIgnoreCase) || Get(query, "t") != "get")
			{
				return Error("Only XG Newznab download links are supported");
			}

			Guid guid;
			if (!Guid.TryParse(Get(query, "id") ?? "", out guid))
			{
				return Error("Invalid XG packet id");
			}

			var packet = Webserver.Search.Packets.GetPacket(guid);
			if (packet == null)
			{
				return Error("Unknown XG packet, it may have been removed from the bot");
			}
			string fingerprint = Get(query, "v");
			if (!string.IsNullOrEmpty(fingerprint) && fingerprint != Newznab.NewznabHandler.Fingerprint(packet))
			{
				return Error("The XG packet changed since it was found");
			}

			return Add(new Nzb.SyntheticNzb(packet.Guid, Newznab.NewznabHandler.PacketTitle(packet), Newznab.NewznabHandler.PacketSize(packet)), aParameters);
		}

		string Add(Nzb.SyntheticNzb aNzb, IDictionary<string, string> aParameters)
		{
			int priority;
			if (!int.TryParse(Get(aParameters, "priority"), NumberStyles.Integer, CultureInfo.InvariantCulture, out priority))
			{
				priority = -100;
			}

			var job = _tracker.Add(aNzb, Get(aParameters, "cat"), priority);
			return Serialize(new JObject { { "status", true }, { "nzo_ids", new JArray(job.Id) } });
		}

		string Queue(IDictionary<string, string> aParameters)
		{
			string name = Get(aParameters, "name");
			if (name == "delete")
			{
				var ids = ParseIds(Get(aParameters, "value"));
				int count = _tracker.RemoveFromQueue(ids);
				return Serialize(new JObject { { "status", true }, { "nzo_ids", new JArray(ids.ToArray()) }, { "removed", count } });
			}
			if (!string.IsNullOrEmpty(name))
			{
				return Error("Not supported by XG");
			}

			var items = _tracker.Queue(Get(aParameters, "category"));
			var page = Page(items, aParameters);

			var slots = new JArray();
			int index = ParseInt(Get(aParameters, "start"), 0);
			foreach (var item in page)
			{
				slots.Add(new JObject
				{
					{ "status", item.Status },
					{ "index", index++ },
					{ "timeleft", FormatTimeLeft(item.SecondsLeft) },
					{ "mb", ToMegabytes(item.TotalSize) },
					{ "mbleft", ToMegabytes(item.RemainingSize) },
					{ "filename", item.Job.Title },
					{ "priority", "Normal" },
					{ "cat", item.Job.Category },
					{ "percentage", Percentage(item.TotalSize, item.RemainingSize) },
					{ "nzo_id", item.Job.Id }
				});
			}

			Int64 speed = items.Sum(i => i.Speed);
			return Serialize(new JObject
			{
				{ "queue", new JObject
					{
						{ "status", items.Any(i => i.Status == "Downloading") ? "Downloading" : "Idle" },
						{ "paused", false },
						{ "kbpersec", (speed / 1024.0).ToString("0.00", CultureInfo.InvariantCulture) },
						{ "noofslots", slots.Count },
						{ "noofslots_total", items.Count },
						{ "slots", slots }
					}
				}
			});
		}

		string History(IDictionary<string, string> aParameters)
		{
			string name = Get(aParameters, "name");
			if (name == "delete")
			{
				var ids = ParseIds(Get(aParameters, "value"));
				int count = _tracker.RemoveFromHistory(ids, Get(aParameters, "del_files") == "1");
				return Serialize(new JObject { { "status", true }, { "removed", count } });
			}
			if (!string.IsNullOrEmpty(name))
			{
				return Error("Not supported by XG");
			}

			var jobs = _tracker.History(Get(aParameters, "category"));
			var slots = new JArray();
			foreach (var job in Page(jobs, aParameters))
			{
				bool completed = job.State == CompatJobState.Completed;
				slots.Add(new JObject
				{
					{ "nzo_id", job.Id },
					{ "name", job.Title },
					{ "nzb_name", job.Title + ".nzb" },
					{ "category", job.Category },
					{ "bytes", job.Size },
					{ "status", completed ? "Completed" : "Failed" },
					{ "fail_message", completed ? "" : job.FailMessage ?? "" },
					{ "storage", completed ? job.StoragePath ?? "" : "" },
					{ "download_time", job.Finished.HasValue ? (int)Math.Max(0, (job.Finished.Value - job.Added).TotalSeconds) : 0 },
					{ "completed", job.Finished.HasValue ? ToUnixTime(job.Finished.Value) : 0 }
				});
			}

			return Serialize(new JObject
			{
				{ "history", new JObject
					{
						{ "paused", false },
						{ "noofslots", jobs.Count },
						{ "slots", slots }
					}
				}
			});
		}

		string Retry(IDictionary<string, string> aParameters)
		{
			string id = Get(aParameters, "value");
			if (string.IsNullOrWhiteSpace(id))
			{
				return Error("Missing job id");
			}
			var job = _tracker.Retry(id.Trim());
			return Serialize(new JObject { { "status", true }, { "nzo_id", job.Id } });
		}

		#endregion

		#region HELPER

		/// <summary>
		/// The ready folder as seen by XG. Clients in other containers translate it with a Remote Path Mapping.
		/// </summary>
		string CompleteDir()
		{
			string path = _readyPath() ?? "";
			return path.Length > 1 ? path.TrimEnd('/', '\\') : path;
		}

		static List<T> Page<T>(List<T> aItems, IDictionary<string, string> aParameters)
		{
			int start = Math.Max(0, ParseInt(Get(aParameters, "start"), 0));
			int limit = ParseInt(Get(aParameters, "limit"), 0);
			var page = aItems.Skip(start);
			if (limit > 0)
			{
				page = page.Take(limit);
			}
			return page.ToList();
		}

		static List<string> ParseIds(string aValue)
		{
			if (string.IsNullOrWhiteSpace(aValue))
			{
				throw new SabnzbdException("Missing job id");
			}
			return aValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).Where(id => id.Length > 0).ToList();
		}

		public static string FormatTimeLeft(Int64 aSeconds)
		{
			if (aSeconds < 0)
			{
				aSeconds = 0;
			}
			var span = TimeSpan.FromSeconds(aSeconds);
			return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)span.TotalHours, span.Minutes, span.Seconds);
		}

		static string ToMegabytes(Int64 aBytes)
		{
			return (aBytes / 1048576.0).ToString("0.00", CultureInfo.InvariantCulture);
		}

		static int Percentage(Int64 aTotal, Int64 aRemaining)
		{
			if (aTotal <= 0)
			{
				return 0;
			}
			return (int)Math.Max(0, Math.Min(100, (aTotal - aRemaining) * 100 / aTotal));
		}

		static Int64 ToUnixTime(DateTime aDate)
		{
			return (Int64)(aDate.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
		}

		static int ParseInt(string aValue, int aDefault)
		{
			int value;
			return int.TryParse(aValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : aDefault;
		}

		static IDictionary<string, string> ParseQuery(string aQuery)
		{
			var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (string part in (aQuery ?? "").TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
			{
				int pos = part.IndexOf('=');
				string key = Uri.UnescapeDataString((pos < 0 ? part : part.Substring(0, pos)).Replace('+', ' '));
				string value = pos < 0 ? "" : Uri.UnescapeDataString(part.Substring(pos + 1).Replace('+', ' '));
				if (!result.ContainsKey(key))
				{
					result.Add(key, value);
				}
			}
			return result;
		}

		static string Get(IDictionary<string, string> aParameters, string aName)
		{
			string value;
			return aParameters.TryGetValue(aName, out value) ? value : null;
		}

		public static string Error(string aMessage)
		{
			return Serialize(new JObject { { "status", false }, { "error", aMessage } });
		}

		static string Serialize(JObject aObject)
		{
			return aObject.ToString(Formatting.None);
		}

		#endregion
	}
}
