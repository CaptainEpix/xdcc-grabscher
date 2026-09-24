//
//  SabnzbdModule.cs
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
using System.IO;
using System.Linq;
using Nancy;
using XG.Plugin.Webserver.Compat.Sabnzbd;

namespace XG.Plugin.Webserver.Nancy.Compat
{
	/// <summary>
	/// SABnzbd download client endpoint for Sonarr/Radarr/Prowlarr: {base}/sabnzbd/api
	/// </summary>
	public class SabnzbdModule : CompatModule
	{
		/// <summary>
		/// Set by the webserver plugin once the job tracker is running.
		/// </summary>
		public static SabnzbdHandler Handler { get; set; }

		public SabnzbdModule() : base("/sabnzbd")
		{
			Get["/api"] = _ => Handle();
			Post["/api"] = _ => Handle();
		}

		Response Handle()
		{
			var handler = Handler;
			string json = handler != null ? handler.Handle(Parameters(), ReadUpload()) : SabnzbdHandler.Error("XG is starting");
			return Utf8(json, SabnzbdHandler.ContentType);
		}

		byte[] ReadUpload()
		{
			// SABnzbd accepts the nzb as "name" or "nzbfile"; take the first file otherwise
			var file = Request.Files.FirstOrDefault(f => f.Key == "name" || f.Key == "nzbfile") ?? Request.Files.FirstOrDefault();
			if (file == null || file.Value == null)
			{
				return null;
			}

			using (var memory = new MemoryStream())
			{
				var buffer = new byte[8192];
				int read;
				while ((read = file.Value.Read(buffer, 0, buffer.Length)) > 0)
				{
					memory.Write(buffer, 0, read);
					// a synthetic nzb is tiny, do not buffer arbitrary uploads
					if (memory.Length > XG.Plugin.Webserver.Compat.Nzb.SyntheticNzb.MaxLength)
					{
						return new byte[XG.Plugin.Webserver.Compat.Nzb.SyntheticNzb.MaxLength + 1];
					}
				}
				return memory.ToArray();
			}
		}
	}
}
