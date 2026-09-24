//
//  CompatApiKeys.cs
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
using XG.Model.Domain;

namespace XG.Plugin.Webserver.Compat
{
	/// <summary>
	/// Validates the apikey query parameter of the compatibility apis against the
	/// normal XG api keys. Keys are never logged.
	/// </summary>
	public static class CompatApiKeys
	{
		public static ApiKeys ApiKeys { get; set; }

		public static bool IsValid(string aKey)
		{
			var apiKeys = ApiKeys;
			Guid guid;
			if (apiKeys == null || string.IsNullOrWhiteSpace(aKey) || !Guid.TryParse(aKey.Trim(), out guid))
			{
				return false;
			}

			var apiKey = apiKeys.WithGuid(guid) as ApiKey;
			if (apiKey == null)
			{
				return false;
			}
			if (!apiKey.Enabled)
			{
				apiKey.ErrorCount++;
				apiKey.Commit();
				return false;
			}

			apiKey.SuccessCount++;
			apiKey.Commit();
			return true;
		}
	}
}
