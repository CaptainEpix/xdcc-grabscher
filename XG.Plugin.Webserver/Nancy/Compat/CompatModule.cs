//
//  CompatModule.cs
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
using System.Text;
using Nancy;

namespace XG.Plugin.Webserver.Nancy.Compat
{
	/// <summary>
	/// Base for the Newznab and SABnzbd compatibility endpoints. These authenticate with
	/// the apikey parameter themselves and answer in their own protocol, so they do not
	/// use the /api/1.0 authentication.
	/// </summary>
	public abstract class CompatModule : NancyModule
	{
		protected CompatModule(string aModulePath) : base(aModulePath) {}

		/// <summary>
		/// Query and form parameters with case insensitive names; query parameters win.
		/// </summary>
		protected IDictionary<string, string> Parameters()
		{
			var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			Add(parameters, Request.Query as DynamicDictionary);
			Add(parameters, Request.Form as DynamicDictionary);
			return parameters;
		}

		static void Add(IDictionary<string, string> aParameters, DynamicDictionary aValues)
		{
			if (aValues == null)
			{
				return;
			}
			foreach (var pair in aValues.ToDictionary())
			{
				if (aParameters.ContainsKey(pair.Key))
				{
					continue;
				}
				var value = pair.Value is DynamicDictionaryValue ? ((DynamicDictionaryValue)pair.Value).Value : pair.Value;
				aParameters.Add(pair.Key, value == null ? null : Convert.ToString(value));
			}
		}

		/// <summary>
		/// The absolute url of the api endpoint of this module, as the client addressed it.
		/// </summary>
		protected string ApiUrl()
		{
			Uri uri = Request.Url;
			return uri.GetLeftPart(UriPartial.Authority) + (Request.Url.BasePath ?? "").TrimEnd('/') + ModulePath + "/api";
		}

		protected static Response Utf8(string aContent, string aContentType)
		{
			var bytes = Encoding.UTF8.GetBytes(aContent);
			return Bytes(bytes, aContentType + "; charset=utf-8");
		}

		protected static Response Bytes(byte[] aContent, string aContentType)
		{
			return new Response
			{
				StatusCode = HttpStatusCode.OK,
				ContentType = aContentType,
				Contents = stream => stream.Write(aContent, 0, aContent.Length)
			};
		}
	}
}
