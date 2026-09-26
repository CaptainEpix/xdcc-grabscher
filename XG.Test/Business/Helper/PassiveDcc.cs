//
//  PassiveDcc.cs
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
using System.Net;
using System.Net.Sockets;
using NUnit.Framework;
using XG.Business.Helper;

namespace XG.Test.Business.Helper
{
	[TestFixture]
	public class PassiveDccTest
	{
		[TearDown]
		public void TearDown()
		{
			PassiveDcc.Configure(null, null, null, null);
		}

		public static int FreePort()
		{
			var listener = new TcpListener(IPAddress.Loopback, 0);
			listener.Start();
			int port = ((IPEndPoint) listener.LocalEndpoint).Port;
			listener.Stop();
			return port;
		}

		[Test]
		public void ParsePortsTest()
		{
			var ports = PassiveDcc.ParsePorts("50000-50004");
			Assert.AreEqual(new[] { 50000, 50001, 50002, 50003, 50004 }, ports.Select(p => p.Listen).ToArray());
			Assert.AreEqual(new[] { 50000, 50001, 50002, 50003, 50004 }, ports.Select(p => p.Public).ToArray());

			// a router or VPN which forwards another outside port
			ports = PassiveDcc.ParsePorts("50000:61234, 50001\n");
			Assert.AreEqual(2, ports.Count);
			Assert.AreEqual(50000, ports[0].Listen);
			Assert.AreEqual(61234, ports[0].Public);
			Assert.AreEqual(50001, ports[1].Public);

			Assert.IsEmpty(PassiveDcc.ParsePorts(" "));
			Assert.Throws<FormatException>(() => PassiveDcc.ParsePorts("70000"));
			Assert.Throws<FormatException>(() => PassiveDcc.ParsePorts("50004-50000"));
			Assert.Throws<FormatException>(() => PassiveDcc.ParsePorts("abc"));
		}

		[Test]
		public void ConfigureTest()
		{
			Assert.IsFalse(PassiveDcc.Enabled);
			PassiveDcc.Configure("50000-50004", null, "203.0.113.7", null);
			Assert.IsTrue(PassiveDcc.Enabled);
			Assert.AreEqual(IPAddress.Parse("203.0.113.7"), PassiveDcc.PublicAddress);
			Assert.Throws<FormatException>(() => PassiveDcc.Configure("50000", null, "2001:db8::1", null));
		}

		[Test]
		public void DccAddressTest()
		{
			Assert.AreEqual("3405803783", PassiveDcc.ToDccAddress(IPAddress.Parse("203.0.113.7")));
			Assert.AreEqual("2130706433", PassiveDcc.ToDccAddress(IPAddress.Loopback));
		}

		[Test]
		public void ReserveClaimReleaseTest()
		{
			int port = FreePort();
			PassiveDcc.Configure("" + port, null, "127.0.0.1", null);
			var first = Guid.NewGuid();
			var second = Guid.NewGuid();

			var reservation = PassiveDcc.Reserve(first);
			Assert.IsNotNull(reservation);
			Assert.AreEqual(port, reservation.ListenPort);
			Assert.IsNull(PassiveDcc.Reserve(second), "the only port is taken");

			Assert.AreSame(reservation, PassiveDcc.Claim(first));
			Assert.IsNull(PassiveDcc.Claim(first), "a reservation is handed out once");

			PassiveDcc.Release(reservation);
			Assert.IsTrue(reservation.Stopped);
			var again = PassiveDcc.Reserve(second);
			Assert.IsNotNull(again, "a released port can be used again");
			PassiveDcc.Release(again);
		}

		[Test]
		public void PortsFileTest()
		{
			int port = FreePort();
			string file = System.IO.Path.GetTempFileName();
			try
			{
				System.IO.File.WriteAllText(file, port + "\n");
				PassiveDcc.Configure(null, file, "127.0.0.1", null);
				Assert.IsTrue(PassiveDcc.Enabled);
				var reservation = PassiveDcc.Reserve(Guid.NewGuid());
				Assert.AreEqual(port, reservation.ListenPort);
				PassiveDcc.Release(reservation);
			}
			finally
			{
				System.IO.File.Delete(file);
			}
		}
	}
}
