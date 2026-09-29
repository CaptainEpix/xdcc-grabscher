//
//  Bot.cs
//  This file is part of XG - XDCC Grabscher
//  http://www.larsformella.de/lang/en/portfolio/programme-software/xg
//
//  Author:
//       Lars Formella <ich@larsformella.de>
//
//  Copyright (c) 2012 Lars Formella
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

using NUnit.Framework;

namespace XG.Test.Model.Domain
{
	[TestFixture]
	public class Bot
	{
		[Test]
		public void PacketLookupTest()
		{
			var bot = new XG.Model.Domain.Bot { Name = "[XG]TestBot" };
			for (int id = 1; id <= 1000; id++)
			{
				Assert.IsTrue(bot.AddPacket(new XG.Model.Domain.Packet { Id = id, Name = "File" + id + ".mkv" }));
			}

			Assert.AreEqual("File1.mkv", bot.Packet(1).Name);
			Assert.AreEqual("File734.mkv", bot.Packet(734).Name);
			Assert.AreEqual(bot, bot.Packet(1000).Parent);
			Assert.IsNull(bot.Packet(0));
			Assert.IsNull(bot.Packet(1001));

			// a pack number is only added once
			Assert.IsFalse(bot.AddPacket(new XG.Model.Domain.Packet { Id = 734, Name = "Other.mkv" }));
			Assert.AreEqual("File734.mkv", bot.Packet(734).Name);

			bot.RemovePacket(bot.Packet(734));
			Assert.IsNull(bot.Packet(734));
			Assert.IsTrue(bot.AddPacket(new XG.Model.Domain.Packet { Id = 734, Name = "Other.mkv" }));
			Assert.AreEqual("Other.mkv", bot.Packet(734).Name);
		}

		[Test]
		public void BotLookupTest()
		{
			var channel = new XG.Model.Domain.Channel { Name = "#test" };
			Assert.IsTrue(channel.AddBot(new XG.Model.Domain.Bot { Name = "[XG]TestBot" }));
			Assert.IsTrue(channel.AddBot(new XG.Model.Domain.Bot { Name = "OtherBot" }));

			// nick names are compared without case and surrounding spaces
			Assert.AreEqual("[XG]TestBot", channel.Bot("[xg]testbot").Name);
			Assert.AreEqual("OtherBot", channel.Bot(" OTHERBOT ").Name);
			Assert.IsNull(channel.Bot("MissingBot"));
			Assert.IsFalse(channel.AddBot(new XG.Model.Domain.Bot { Name = "otherbot" }));
		}
	}
}
