//
//  Packet.cs
//  This file is part of XG - XDCC Grabscher
//  http://www.larsformella.de/lang/en/portfolio/programme-software/xg
//
//  Author:
//       Lars Formella <ich@larsformella.de>
//
//  Copyright (c) 2013 Lars Formella
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
using NUnit.Framework;
using XG.Model.Domain;

namespace XG.Test.Plugin.Irc.Parser.Types.Info
{
	[TestFixture]
	public class Packet : AParser
	{
		[Test]
		public void PacketParseTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Types.Info.Packet();

			Parse(parser, "#5   90x [181M] 6,9 Serie 9,6 The.Big.Bang.Theory.S05E05.Ab.nach.Baikonur.GERMAN.DUBBED.HDTVRiP.XviD-SOF.rar ");
			var tPack = Bot.Packet(5);
			Assert.AreEqual(181 * 1024 * 1024, tPack.Size);
			Assert.AreEqual("Serie The.Big.Bang.Theory.S05E05.Ab.nach.Baikonur.GERMAN.DUBBED.HDTVRiP.XviD-SOF.rar", tPack.Name);

			Parse(parser, "#3  54x [150M] 2,11 [ABOOK] Fanny_Mueller--Grimms_Maerchen_(Abook)-2CD-DE-2008-OMA.rar ");
			tPack = Bot.Packet(3);
			Assert.AreEqual(150 * 1024 * 1024, tPack.Size);
			Assert.AreEqual("[ABOOK] Fanny_Mueller--Grimms_Maerchen_(Abook)-2CD-DE-2008-OMA.rar", tPack.Name);

			Parse(parser, "#1  0x [  5M] 5meg");
			tPack = Bot.Packet(1);
			Assert.AreEqual(5 * 1024 * 1024, tPack.Size);
			Assert.AreEqual("5meg", tPack.Name);

			Parse(parser, "#88   5x [505M] [~TnF~] Ginga Kikoutai Majestic Prince 01 [720p][10Bit][H264][AAC][1d606f90].mkv");
			tPack = Bot.Packet(88);
			Assert.AreEqual(505 * 1024 * 1024, tPack.Size);
			Assert.AreEqual("[TnF] Ginga Kikoutai Majestic Prince 01 [720p][10Bit][H264][AAC][1d606f90].mkv", tPack.Name);

			Parse(parser, "#18  5x [2.2G] Payback.Heute.ist.Zahltag.2011.German.DL.1080p.BluRay.x264-LeechOurStuff.mkv");
			tPack = Bot.Packet(18);
			Assert.AreEqual((Int64) (2.2 * 1024 * 1024 * 1024), tPack.Size);
			Assert.AreEqual("Payback.Heute.ist.Zahltag.2011.German.DL.1080p.BluRay.x264-LeechOurStuff.mkv", tPack.Name);
		}

		[Test]
		public void NonAsciiNamesTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Types.Info.Packet();

			Parse(parser, "#40   0x [1.0M] Café.Déjà.Vu.S01E02.720p.mkv");
			Assert.AreEqual("Café.Déjà.Vu.S01E02.720p.mkv", Bot.Packet(40).Name);

			Parse(parser, "#41   0x [1.0M] It's.Someone's.Show.S01E03.720p.mkv");
			Assert.AreEqual("It's.Someone's.Show.S01E03.720p.mkv", Bot.Packet(41).Name);

			Parse(parser, "#42   0x [1.0M] Die.Brücke.am.Fluß.2021.German.1080p.mkv");
			Assert.AreEqual("Die.Brücke.am.Fluß.2021.German.1080p.mkv", Bot.Packet(42).Name);

			// file system characters still go
			Parse(parser, "#43   0x [1.0M] What?.Why\\Not/Now*.mkv");
			Assert.AreEqual("What.WhyNotNow.mkv", Bot.Packet(43).Name);
		}

		[Test]
		public void OldStrippedNameIsTheSameFileTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Types.Info.Packet();
			// how older versions stored the name
			Parse(parser, "#44   0x [1.0M] Caf.Dj.Vu.S01E04.720p.mkv");
			var packet = Bot.Packet(44);
			packet.Enabled = true;
			packet.LastUpdated = new DateTime(2026, 1, 1);

			Parse(parser, "#44   0x [1.0M] Café.Déjà.Vu.S01E04.720p.mkv");
			Assert.AreEqual("Café.Déjà.Vu.S01E04.720p.mkv", packet.Name);
			Assert.IsTrue(packet.Enabled, "a queued download must not be cancelled by the new spelling");
			Assert.AreEqual(new DateTime(2026, 1, 1), packet.LastUpdated, "it is not a new release");

			// a really different file still is
			Parse(parser, "#44   0x [1.0M] Another.Show.S01E01.720p.mkv");
			Assert.IsFalse(packet.Enabled);
		}
	}
}
