//
//  NewznabXml.cs
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
using System.IO;
using System.Text;
using System.Xml;

namespace XG.Plugin.Webserver.Compat.Newznab
{
	/// <summary>
	/// A single packet as a Newznab feed item.
	/// </summary>
	public class NewznabItem
	{
		public Guid Guid { get; set; }
		public string Title { get; set; }
		public string Description { get; set; }
		public Int64 Size { get; set; }
		public DateTime PublishDate { get; set; }
		public string DownloadUrl { get; set; }
		public int[] Categories { get; set; }
	}

	/// <summary>
	/// Serializes the Newznab documents XG supports.
	/// </summary>
	public static class NewznabXml
	{
		public const string ContentType = "application/xml";
		public const string AttributeNamespace = "http://www.newznab.com/DTD/2010/feeds/attributes/";

		static readonly Dictionary<int, string> CategoryNames = new Dictionary<int, string>
		{
			{ NewznabRequest.CategoryMovies, "Movies" },
			{ NewznabRequest.CategoryTv, "TV" },
			{ NewznabRequest.CategoryOther, "Other" }
		};

		public static string Capabilities()
		{
			return Write(writer =>
			{
				writer.WriteStartElement("caps");

				writer.WriteStartElement("server");
				writer.WriteAttributeString("version", "1.0");
				writer.WriteAttributeString("title", "XG");
				writer.WriteAttributeString("strapline", "XDCC Grabscher Newznab compatibility");
				writer.WriteEndElement();

				writer.WriteStartElement("limits");
				writer.WriteAttributeString("max", NewznabRequest.MaxLimit.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("default", NewznabRequest.DefaultLimit.ToString(CultureInfo.InvariantCulture));
				writer.WriteEndElement();

				// only text based searching is supported, XG has no external ids
				writer.WriteStartElement("searching");
				WriteSearch(writer, "search", true, "q");
				WriteSearch(writer, "tv-search", true, "q,season,ep");
				WriteSearch(writer, "movie-search", true, "q");
				WriteSearch(writer, "audio-search", false, "");
				WriteSearch(writer, "book-search", false, "");
				writer.WriteEndElement();

				writer.WriteStartElement("categories");
				foreach (var category in CategoryNames)
				{
					writer.WriteStartElement("category");
					writer.WriteAttributeString("id", category.Key.ToString(CultureInfo.InvariantCulture));
					writer.WriteAttributeString("name", category.Value);
					writer.WriteEndElement();
				}
				writer.WriteEndElement();

				writer.WriteEndElement();
			});
		}

		static void WriteSearch(XmlWriter aWriter, string aName, bool aAvailable, string aParams)
		{
			aWriter.WriteStartElement(aName);
			aWriter.WriteAttributeString("available", aAvailable ? "yes" : "no");
			aWriter.WriteAttributeString("supportedParams", aParams);
			aWriter.WriteEndElement();
		}

		public static string Results(IEnumerable<NewznabItem> aItems, int aOffset, int aTotal, string aLink)
		{
			return Write(writer =>
			{
				writer.WriteStartElement("rss");
				writer.WriteAttributeString("version", "2.0");
				writer.WriteAttributeString("xmlns", "newznab", null, AttributeNamespace);

				writer.WriteStartElement("channel");
				writer.WriteElementString("title", "XG");
				writer.WriteElementString("description", "XG XDCC packets");
				writer.WriteElementString("link", aLink);

				writer.WriteStartElement("response", AttributeNamespace);
				writer.WriteAttributeString("offset", aOffset.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("total", aTotal.ToString(CultureInfo.InvariantCulture));
				writer.WriteEndElement();

				foreach (var item in aItems)
				{
					string guid = item.Guid.ToString("D");
					string size = item.Size.ToString(CultureInfo.InvariantCulture);

					writer.WriteStartElement("item");
					writer.WriteElementString("title", XmlText.Clean(item.Title));

					writer.WriteStartElement("guid");
					writer.WriteAttributeString("isPermaLink", "false");
					writer.WriteString(guid);
					writer.WriteEndElement();

					writer.WriteElementString("link", item.DownloadUrl);
					writer.WriteElementString("pubDate", FormatDate(item.PublishDate));
					if (!string.IsNullOrEmpty(item.Description))
					{
						writer.WriteElementString("description", XmlText.Clean(item.Description));
					}

					writer.WriteStartElement("enclosure");
					writer.WriteAttributeString("url", item.DownloadUrl);
					writer.WriteAttributeString("length", size);
					writer.WriteAttributeString("type", Nzb.SyntheticNzb.ContentType);
					writer.WriteEndElement();

					foreach (int category in item.Categories)
					{
						WriteAttribute(writer, "category", category.ToString(CultureInfo.InvariantCulture));
					}
					WriteAttribute(writer, "size", size);

					writer.WriteEndElement();
				}

				writer.WriteEndElement();
				writer.WriteEndElement();
			});
		}

		static void WriteAttribute(XmlWriter aWriter, string aName, string aValue)
		{
			aWriter.WriteStartElement("newznab", "attr", AttributeNamespace);
			aWriter.WriteAttributeString("name", aName);
			aWriter.WriteAttributeString("value", aValue);
			aWriter.WriteEndElement();
		}

		public static string Error(int aCode, string aDescription)
		{
			return Write(writer =>
			{
				writer.WriteStartElement("error");
				writer.WriteAttributeString("code", aCode.ToString(CultureInfo.InvariantCulture));
				writer.WriteAttributeString("description", XmlText.Clean(aDescription));
				writer.WriteEndElement();
			});
		}

		/// <summary>
		/// RFC 822 date as used by RSS.
		/// </summary>
		public static string FormatDate(DateTime aDate)
		{
			return aDate.ToUniversalTime().ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " +0000";
		}

		static string Write(Action<XmlWriter> aAction)
		{
			var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true };
			using (var stream = new MemoryStream())
			{
				using (var writer = XmlWriter.Create(stream, settings))
				{
					writer.WriteStartDocument();
					aAction(writer);
					writer.WriteEndDocument();
				}
				return Encoding.UTF8.GetString(stream.ToArray());
			}
		}
	}
}
