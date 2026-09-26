//
//  TimedList.cs
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
using System.Linq;
using NUnit.Framework;

namespace XG.Test.Plugin.Irc
{
	[TestFixture]
	public class TimedList
	{
		[Test]
		public void AddKeepsDueItemsOfOthersTest()
		{
			var list = new XG.Plugin.Irc.TimedList<string>();
			list.Add("due bot", DateTime.Now.AddSeconds(-1));
			list.Add("other bot", DateTime.Now.AddSeconds(15));

			// the trigger has not run yet, so the due item must still be handed out
			Assert.AreEqual(new[] { "due bot" }, list.GetExpiredItems().ToArray());
			Assert.IsFalse(list.Contains("due bot"));
			Assert.IsTrue(list.Contains("other bot"));
		}

		[Test]
		public void AddReplacesTimeTest()
		{
			var list = new XG.Plugin.Irc.TimedList<string>();
			list.Add("bot", DateTime.Now.AddSeconds(240));
			list.Add("bot", DateTime.Now.AddSeconds(-1));

			Assert.AreEqual(new[] { "bot" }, list.GetExpiredItems().ToArray());
			Assert.IsEmpty(list.GetExpiredItems().ToArray());
		}

		[Test]
		public void RemoveExpiredItemsTest()
		{
			var list = new XG.Plugin.Irc.TimedList<string>();
			list.Add("old request", DateTime.Now.AddSeconds(-1));
			list.Add("new request", DateTime.Now.AddSeconds(10));

			list.RemoveExpiredItems();

			Assert.IsFalse(list.Contains("old request"));
			Assert.IsTrue(list.Contains("new request"));
			Assert.Greater(list.GetMissingSeconds("new request"), 5);
		}
	}
}
