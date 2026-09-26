//
//  SyntheticNzb.cs
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
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace XG.Plugin.Webserver.Compat.Nzb
{
	/// <summary>
	/// The NZB which carries an XG packet through Prowlarr/Sonarr/Radarr back into XG.
	/// It is never used for Usenet retrieval; the file/segment entries only exist
	/// because clients reject NZB documents without them.
	/// </summary>
	public class SyntheticNzb
	{
		public const string Namespace = "http://www.newzbin.com/DTD/2003/nzb";
		public const string MetaPacketGuid = "xg-packet-guid";
		public const string MetaTitle = "xg-title";
		public const string MetaSize = "xg-size";

		public const string ContentType = "application/x-nzb";

		// a synthetic nzb is a few hundred bytes; anything big is not ours
		public const int MaxLength = 64 * 1024;
		const int MaxTitleLength = 1024;

		public Guid PacketGuid { get; private set; }
		public string Title { get; private set; }
		public Int64 Size { get; private set; }

		public SyntheticNzb(Guid aPacketGuid, string aTitle, Int64 aSize)
		{
			PacketGuid = aPacketGuid;
			Title = aTitle ?? "";
			Size = aSize;
		}

		public byte[] ToBytes()
		{
			var settings = new XmlWriterSettings
			{
				Encoding = new UTF8Encoding(false),
				Indent = true
			};

			using (var stream = new MemoryStream())
			{
				using (var writer = XmlWriter.Create(stream, settings))
				{
					string guid = PacketGuid.ToString("D");
					string title = XmlText.Clean(Title);

					writer.WriteStartDocument();
					writer.WriteStartElement("nzb", Namespace);

					writer.WriteStartElement("head", Namespace);
					WriteMeta(writer, "name", title);
					WriteMeta(writer, MetaPacketGuid, guid);
					WriteMeta(writer, MetaTitle, title);
					WriteMeta(writer, MetaSize, Size.ToString(CultureInfo.InvariantCulture));
					writer.WriteEndElement();

					writer.WriteStartElement("file", Namespace);
					writer.WriteAttributeString("poster", "XG <xg@xg.invalid>");
					writer.WriteAttributeString("date", "0");
					writer.WriteAttributeString("subject", "XG XDCC packet " + guid + " (1/1)");
					writer.WriteStartElement("groups", Namespace);
					writer.WriteElementString("group", Namespace, "xg.xdcc.compatibility");
					writer.WriteEndElement();
					writer.WriteStartElement("segments", Namespace);
					writer.WriteStartElement("segment", Namespace);
					writer.WriteAttributeString("bytes", "1");
					writer.WriteAttributeString("number", "1");
					writer.WriteString(guid + "@xg.invalid");
					writer.WriteEndElement();
					writer.WriteEndElement();
					writer.WriteEndElement();

					writer.WriteEndElement();
					writer.WriteEndDocument();
				}
				return stream.ToArray();
			}
		}

		static void WriteMeta(XmlWriter aWriter, string aType, string aValue)
		{
			aWriter.WriteStartElement("meta", Namespace);
			aWriter.WriteAttributeString("type", aType);
			aWriter.WriteString(aValue);
			aWriter.WriteEndElement();
		}

		/// <summary>
		/// Parses an uploaded NZB. Everything in it is untrusted: only the packet guid
		/// and a display title are taken, and the guid still has to be validated
		/// against the known packets by the caller.
		/// </summary>
		/// <exception cref="FormatException">if the document is not a valid XG nzb</exception>
		public static SyntheticNzb Parse(byte[] aData)
		{
			if (aData == null || aData.Length == 0)
			{
				throw new FormatException("empty nzb");
			}
			if (aData.Length > MaxLength)
			{
				throw new FormatException("nzb too large");
			}

			var settings = new XmlReaderSettings
			{
				DtdProcessing = DtdProcessing.Prohibit,
				XmlResolver = null,
				IgnoreComments = true,
				IgnoreProcessingInstructions = true,
				MaxCharactersInDocument = MaxLength
			};

			string guidValue = null;
			string title = null;
			string sizeValue = null;
			int files = 0;

			try
			{
				using (var reader = XmlReader.Create(new MemoryStream(aData), settings))
				{
					reader.MoveToContent();
					if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "nzb")
					{
						throw new FormatException("root element is not nzb");
					}

					while (reader.Read())
					{
						if (reader.NodeType != XmlNodeType.Element)
						{
							continue;
						}
						if (reader.LocalName == "file")
						{
							files++;
						}
						else if (reader.LocalName == "meta")
						{
							string type = reader.GetAttribute("type");
							if (reader.IsEmptyElement)
							{
								continue;
							}
							string value = reader.ReadElementContentAsString();
							if (type == MetaPacketGuid && guidValue == null)
							{
								guidValue = value;
							}
							else if (type == MetaTitle && title == null)
							{
								title = value;
							}
							else if (type == MetaSize && sizeValue == null)
							{
								sizeValue = value;
							}
						}
					}
				}
			}
			catch (XmlException ex)
			{
				throw new FormatException("malformed nzb: " + ex.Message);
			}

			if (files == 0)
			{
				throw new FormatException("nzb contains no file");
			}
			if (string.IsNullOrWhiteSpace(guidValue))
			{
				throw new FormatException("nzb contains no XG packet guid");
			}

			Guid guid;
			if (!Guid.TryParseExact(guidValue.Trim(), "D", out guid) || guid == Guid.Empty)
			{
				throw new FormatException("nzb contains an invalid XG packet guid");
			}

			title = (title ?? "").Trim();
			if (title.Length > MaxTitleLength)
			{
				title = title.Substring(0, MaxTitleLength);
			}

			Int64 size;
			if (!Int64.TryParse(sizeValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 0)
			{
				size = 0;
			}

			return new SyntheticNzb(guid, title, size);
		}
	}
}
