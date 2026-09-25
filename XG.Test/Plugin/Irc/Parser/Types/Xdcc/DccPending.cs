//
//  DccPending.cs
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

using NUnit.Framework;
using XG.Extensions;
using XG.Model.Domain;

namespace XG.Test.Plugin.Irc.Parser.Types.Xdcc
{
	[TestFixture]
	public class DccPending : AParser
	{
		[Test]
		public void RequestAgainAfterTimeoutTest()
		{
			var parser = new XG.Plugin.Irc.Parser.Types.Xdcc.DccPending();
			EventArgs<Bot, int> raisedEvent = null;
			parser.OnQueueRequestFromBot += (sender, e) => raisedEvent = e;

			Parse(parser, "** You have a DCC pending, Set your client to receive the transfer. Type \"/MSG " + Bot.Name + " XDCC CANCEL\" to abort the transfer. (150 seconds remaining until timeout)");

			Assert.IsNotNull(raisedEvent);
			// the bot queue works in seconds; this used to be 152000 (42 hours)
			Assert.AreEqual(152, raisedEvent.Value2);
		}
	}
}
