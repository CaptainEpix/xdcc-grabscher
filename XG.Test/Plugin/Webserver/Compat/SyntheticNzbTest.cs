//
//  SyntheticNzbTest.cs
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
using System.Text;
using NUnit.Framework;
using XG.Plugin.Webserver.Compat.Nzb;

namespace XG.Test.Plugin.Webserver.Compat
{
	[TestFixture]
	public class SyntheticNzbTest
	{
		const string Guid1 = "5b3f7c0e-6a51-4e0c-9f5b-2c1d7a9e8b40";

		static byte[] Bytes(string aXml)
		{
			return Encoding.UTF8.GetBytes(aXml);
		}

		static string Nzb(string aHead, string aFile = "<file><segments><segment number=\"1\">x</segment></segments></file>")
		{
			return "<?xml version=\"1.0\"?><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"><head>" + aHead + "</head>" + aFile + "</nzb>";
		}

		[Test]
		public void RoundTripTest()
		{
			var guid = Guid.NewGuid();
			var nzb = new SyntheticNzb(guid, "Tom & Jerry <S01E01> \"x\" äöü 🎬.mkv", 123456789012);
			var parsed = SyntheticNzb.Parse(nzb.ToBytes());
			Assert.AreEqual(guid, parsed.PacketGuid);
			Assert.AreEqual(nzb.Title, parsed.Title);
			Assert.AreEqual(123456789012, parsed.Size);
		}

		[Test]
		public void InvalidXmlCharactersAreDroppedTest()
		{
			var guid = Guid.NewGuid();
			var parsed = SyntheticNzb.Parse(new SyntheticNzb(guid, "bad\u0001name\u0000.mkv", 1).ToBytes());
			Assert.AreEqual("badname.mkv", parsed.Title);
		}

		[Test]
		public void ParseMinimalTest()
		{
			var parsed = SyntheticNzb.Parse(Bytes(Nzb("<meta type=\"xg-packet-guid\">" + Guid1 + "</meta>")));
			Assert.AreEqual(new Guid(Guid1), parsed.PacketGuid);
			Assert.AreEqual("", parsed.Title);
			Assert.AreEqual(0, parsed.Size);
		}

		[Test]
		public void RejectMalformedTest()
		{
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(null));
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(new byte[0]));
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes("not xml at all")));
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes("<nzb><head><meta type=\"xg-packet-guid\">" + Guid1 + "</meta></head>")));
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes("<error code=\"100\" description=\"x\"/>")));
		}

		[Test]
		public void RejectMissingOrInvalidGuidTest()
		{
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(Nzb("<meta type=\"name\">x</meta>"))));
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(Nzb("<meta type=\"xg-packet-guid\">/etc/passwd</meta>"))));
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(Nzb("<meta type=\"xg-packet-guid\">" + Guid.Empty + "</meta>"))));
		}

		[Test]
		public void RejectWithoutFileTest()
		{
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(Nzb("<meta type=\"xg-packet-guid\">" + Guid1 + "</meta>", ""))));
		}

		[Test]
		public void RejectDtdTest()
		{
			// entity expansion and external entities must never be processed
			string bomb = "<?xml version=\"1.0\"?><!DOCTYPE nzb [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\">]>" +
				"<nzb><head><meta type=\"xg-packet-guid\">" + Guid1 + "</meta><meta type=\"xg-title\">&b;</meta></head><file/></nzb>";
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(bomb)));

			string external = "<?xml version=\"1.0\"?><!DOCTYPE nzb [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>" +
				"<nzb><head><meta type=\"xg-packet-guid\">" + Guid1 + "</meta><meta type=\"xg-title\">&x;</meta></head><file/></nzb>";
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(external)));
		}

		[Test]
		public void RejectOversizedTest()
		{
			var padding = new string(' ', SyntheticNzb.MaxLength);
			Assert.Throws<FormatException>(() => SyntheticNzb.Parse(Bytes(Nzb("<meta type=\"xg-packet-guid\">" + Guid1 + "</meta>" + padding))));
		}
	}
}
