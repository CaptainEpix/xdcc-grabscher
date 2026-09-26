//
//  IrcConnection.cs
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


using System;
using System.Reflection;
using NUnit.Framework;

namespace XG.Test.Plugin.Irc
{
	[TestFixture]
	public class IrcConnection
	{
		[Test]
		public void PackListsOnlyWhenAskedTest()
		{
			var connection = new XG.Plugin.Irc.IrcConnection();
			var requests = (XG.Plugin.Irc.TimedList<string>) typeof(XG.Plugin.Irc.IrcConnection)
				.GetField("_latestXdccListRequests", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(connection);

			Assert.IsFalse(connection.AskedForXdccList("SomeBot"), "nobody asked yet");

			requests.Add("SomeBot@XDCC LIST", DateTime.Now.AddHours(1));
			Assert.IsTrue(connection.AskedForXdccList("SomeBot"));
			Assert.IsTrue(connection.AskedForXdccList("somebot"), "nicks are case insensitive");
			Assert.IsFalse(connection.AskedForXdccList("Some"), "only the exact nick");
			Assert.IsFalse(connection.AskedForXdccList("Stranger"));

			requests.Add("OldBot@XDCC LIST", DateTime.Now.AddSeconds(-1));
			Assert.IsFalse(connection.AskedForXdccList("OldBot"), "an old request does not count");
		}
	}
}
