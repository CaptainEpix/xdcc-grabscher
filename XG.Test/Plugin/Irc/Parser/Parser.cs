//
//  Parser.cs
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

using System.Threading;
using NUnit.Framework;
using XG.Plugin.Irc.Parser;

namespace XG.Test.Plugin.Irc.Parser
{
	[TestFixture]
	public class Parser : AParser
	{
		[Test]
		public void EveryMessageIsParsedOnceTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Parser();
			int downloads = 0;
			parser.OnAddDownload += (sender, e) => Interlocked.Increment(ref downloads);
			parser.Initialize();
			try
			{
				// a bot sends its notice and the DCC offer back to back
				for (int a = 0; a < 20; a++)
				{
					downloads = 0;
					Packet.Connected = false;
					parser.Parse(new Message { Channel = Channel, Nick = Bot.Name, Text = "** Sending you pack #1 (\"Testfile.with.a.long.name.mkv\"), which is 930MB. (resume supported)" });
					parser.Parse(new Message { Channel = Channel, Nick = Bot.Name, Text = "\u0001DCC SEND Testfile.with.a.long.name.mkv 1203194610 45000 975304559\u0001" });
					// parsing runs on its own thread and is slow on a cold start, so wait for the offer,
					// then a little more for a second, wrong one
					for (int wait = 0; wait < 200 && downloads == 0; wait++)
					{
						Thread.Sleep(10);
					}
					Thread.Sleep(50);
					Assert.AreEqual(1, downloads, "the DCC offer was not parsed exactly once in round " + a);
				}
			}
			finally
			{
				parser.DeInitialize();
			}
		}

		[Test]
		public void BadMessageDoesNotStopParsingTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Parser();
			int downloads = 0;
			parser.OnAddDownload += (sender, e) => Interlocked.Increment(ref downloads);
			parser.Initialize();
			try
			{
				Packet.Connected = false;
				// messages which made parsers throw, which ended the parse thread (and XG) for good;
				// sent by someone else, a bot's broken offer would end its own request
				parser.Parse(new Message { Channel = Channel, Nick = "SomeoneElse", Text = "\u0001DCC SEND\u0001" });
				parser.Parse(new Message { Channel = Channel, Nick = "SomeoneElse", Text = "\u0001DCC " });
				parser.Parse(new Message { Channel = Channel, Nick = Bot.Name, Text = "\u0001DCC SEND Testfile.with.a.long.name.mkv 1203194610 45000 975304559\u0001" });
				for (int wait = 0; wait < 200 && downloads == 0; wait++)
				{
					Thread.Sleep(10);
				}
				Assert.AreEqual(1, downloads, "the parser still works after bad messages");
			}
			finally
			{
				parser.DeInitialize();
				Packet.Enabled = true;
			}
		}
	}
}
