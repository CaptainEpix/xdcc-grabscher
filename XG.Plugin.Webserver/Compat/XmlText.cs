//
//  XmlText.cs
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

using System.Text;
using System.Xml;

namespace XG.Plugin.Webserver.Compat
{
	public static class XmlText
	{
		/// <summary>
		/// Removes characters which can not be represented in XML 1.0.
		/// </summary>
		public static string Clean(string aValue)
		{
			if (aValue == null)
			{
				return "";
			}
			var builder = new StringBuilder(aValue.Length);
			for (int a = 0; a < aValue.Length; a++)
			{
				char c = aValue[a];
				if (XmlConvert.IsXmlChar(c))
				{
					builder.Append(c);
				}
				else if (a + 1 < aValue.Length && XmlConvert.IsXmlSurrogatePair(aValue[a + 1], c))
				{
					builder.Append(c).Append(aValue[a + 1]);
					a++;
				}
			}
			return builder.ToString();
		}
	}
}
