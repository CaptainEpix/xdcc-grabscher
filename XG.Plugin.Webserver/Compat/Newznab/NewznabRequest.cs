//
//  NewznabRequest.cs
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

namespace XG.Plugin.Webserver.Compat.Newznab
{
	/// <summary>
	/// A Newznab search request translated into XG terms.
	/// </summary>
	public class NewznabRequest
	{
		public const int CategoryMovies = 2000;
		public const int CategoryTv = 5000;
		public const int CategoryOther = 8000;

		public const int DefaultLimit = 100;
		public const int MaxLimit = 100;

		public string Function { get; private set; }

		public List<string> Required { get; private set; }
		public List<string> Excluded { get; private set; }
		public List<string> Prefixes { get; private set; }

		public int[] Categories { get; private set; }

		public int Offset { get; private set; }
		public int Limit { get; private set; }

		public bool ShowOfflineBots { get; private set; }

		/// <summary>
		/// Without any search term the request is a "recent packets" feed,
		/// which Prowlarr uses for its connection test and the *Arrs for RSS sync.
		/// </summary>
		public bool IsRecent
		{
			get { return Required.Count == 0 && Prefixes.Count == 0; }
		}

		NewznabRequest()
		{
			Required = new List<string>();
			Excluded = new List<string>();
			Prefixes = new List<string>();
		}

		/// <summary>
		/// Parses t=search, t=tvsearch and t=movie requests.
		/// </summary>
		/// <exception cref="NewznabException">for invalid parameters</exception>
		public static NewznabRequest Parse(IDictionary<string, string> aParameters)
		{
			var request = new NewznabRequest();
			request.Function = NormalizeFunction(Get(aParameters, "t"));

			string q = Get(aParameters, "q");
			if (!string.IsNullOrWhiteSpace(q))
			{
				foreach (string word in q.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
				{
					if (word.Length > 1 && word.StartsWith("-", StringComparison.Ordinal))
					{
						request.Excluded.AddRange(Search.Packets.AnalyzeName(word.Substring(1)));
					}
					else
					{
						request.Required.AddRange(Search.Packets.AnalyzeName(word));
					}
				}
			}

			if (request.Function == "tvsearch")
			{
				request.AddEpisodeTerms(Get(aParameters, "season"), Get(aParameters, "ep"));
			}

			request.Categories = ResolveCategories(request.Function, ParseCategories(Get(aParameters, "cat")));

			request.Offset = ParseInt(Get(aParameters, "offset"), 0, "offset");
			if (request.Offset < 0)
			{
				throw new NewznabException(NewznabException.IncorrectParameter, "Incorrect parameter (offset)");
			}
			request.Limit = ParseInt(Get(aParameters, "limit"), DefaultLimit, "limit");
			if (request.Limit <= 0 || request.Limit > MaxLimit)
			{
				request.Limit = request.Limit <= 0 ? DefaultLimit : MaxLimit;
			}

			// offline bots can not deliver right now, so they are only returned on explicit request
			string offline = Get(aParameters, "offline");
			request.ShowOfflineBots = offline == "1" || string.Equals(offline, "true", StringComparison.OrdinalIgnoreCase);

			return request;
		}

		public static string NormalizeFunction(string aFunction)
		{
			if (aFunction == null)
			{
				return null;
			}
			aFunction = aFunction.Trim().ToLowerInvariant();
			switch (aFunction)
			{
				case "tv":
					return "tvsearch";
				case "m":
					return "movie";
				case "g":
					return "get";
				case "s":
					return "search";
				default:
					return aFunction;
			}
		}

		void AddEpisodeTerms(string aSeason, string aEpisode)
		{
			if (string.IsNullOrWhiteSpace(aSeason))
			{
				return;
			}

			int season = ParseInt(aSeason, 0, "season");
			if (season < 0)
			{
				throw new NewznabException(NewznabException.IncorrectParameter, "Incorrect parameter (season)");
			}

			if (string.IsNullOrWhiteSpace(aEpisode))
			{
				// season search: match every token starting with S02 (S02, S02E05, ...)
				Prefixes.Add("s" + season.ToString("D2", CultureInfo.InvariantCulture));
				return;
			}

			if (aEpisode.Contains("/"))
			{
				// daily episodes are sent as season=2024&ep=05/12
				Required.AddRange(Search.Packets.AnalyzeName(aSeason));
				Required.AddRange(Search.Packets.AnalyzeName(aEpisode.Replace("/", " ")));
				return;
			}

			int episode = ParseInt(aEpisode, 0, "ep");
			if (episode < 0)
			{
				throw new NewznabException(NewznabException.IncorrectParameter, "Incorrect parameter (ep)");
			}
			Required.Add("s" + season.ToString("D2", CultureInfo.InvariantCulture) + "e" + episode.ToString("D2", CultureInfo.InvariantCulture));
		}

		/// <summary>
		/// XG does not know whether a packet is a movie or an episode, so the category
		/// is taken from the request context instead of being guessed from the file name.
		/// </summary>
		public static int[] ResolveCategories(string aFunction, int[] aRequested)
		{
			if (aFunction == "movie")
			{
				return new[] { CategoryMovies };
			}
			if (aFunction == "tvsearch")
			{
				return new[] { CategoryTv };
			}

			bool movies = aRequested.Any(c => c >= 2000 && c < 3000);
			bool tv = aRequested.Any(c => c >= 5000 && c < 6000);
			if (movies && tv)
			{
				return new[] { CategoryMovies, CategoryTv };
			}
			if (movies)
			{
				return new[] { CategoryMovies };
			}
			if (tv)
			{
				return new[] { CategoryTv };
			}
			return new[] { CategoryOther };
		}

		static int[] ParseCategories(string aCategories)
		{
			var categories = new List<int>();
			if (!string.IsNullOrWhiteSpace(aCategories))
			{
				foreach (string str in aCategories.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
				{
					int category;
					if (int.TryParse(str.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out category))
					{
						categories.Add(category);
					}
				}
			}
			return categories.ToArray();
		}

		static int ParseInt(string aValue, int aDefault, string aName)
		{
			if (string.IsNullOrWhiteSpace(aValue))
			{
				return aDefault;
			}
			int value;
			if (!int.TryParse(aValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
			{
				throw new NewznabException(NewznabException.IncorrectParameter, "Incorrect parameter (" + aName + ")");
			}
			return value;
		}

		public static string Get(IDictionary<string, string> aParameters, string aName)
		{
			string value;
			return aParameters.TryGetValue(aName, out value) ? value : null;
		}
	}
}
