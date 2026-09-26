//
//  NewznabModule.cs
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

using Nancy;
using XG.Plugin.Webserver.Compat;
using XG.Plugin.Webserver.Compat.Newznab;

namespace XG.Plugin.Webserver.Nancy.Compat
{
	/// <summary>
	/// Newznab indexer endpoint for Prowlarr: {base}/newznab/api
	/// </summary>
	public class NewznabModule : CompatModule
	{
		public NewznabModule() : base("/newznab")
		{
			Get["/api"] = _ => Handle();
		}

		Response Handle()
		{
			var handler = new NewznabHandler(CompatApiKeys.IsValid);
			var result = handler.Handle(Parameters(), ApiUrl());

			var response = Bytes(result.Body, result.ContentType + "; charset=utf-8");
			if (!string.IsNullOrEmpty(result.FileName))
			{
				response.ContentType = result.ContentType;
				response.Headers["Content-Disposition"] = "attachment; filename=\"" + result.FileName.Replace("\"", "") + "\"";
			}
			return response;
		}
	}
}
