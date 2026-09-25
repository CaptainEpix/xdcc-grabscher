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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using log4net;

namespace XG.Business.Helper
{
	/// <summary>
	/// Passive (reverse) DCC: the bot asks XG to listen, XG answers with its public address
	/// and one of its ports, and the bot connects to XG. Configured with environment variables:
	///   XG_PASSIVE_DCC_PORTS       ports to listen on: "50000-50004", or "listen:public" pairs
	///                              like "50000:61234" when a router or VPN forwards another outside port
	///   XG_PASSIVE_DCC_PORTS_FILE  file with the port(s) to use, read before every transfer
	///                              (VPN containers like gluetun write their forwarded port there)
	///   XG_PASSIVE_DCC_IP          public IPv4 address to announce, detected when not set
	///   XG_PASSIVE_DCC_IP_URL      service which returns the public address as text
	/// </summary>
	public static class PassiveDcc
	{
		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public const int AcceptTimeoutSeconds = 90;
		const int UnclaimedTimeoutSeconds = 300;
		static readonly TimeSpan AddressLifetime = TimeSpan.FromMinutes(30);
		static readonly TimeSpan AddressRetry = TimeSpan.FromMinutes(1);
		static readonly string[] DefaultAddressUrls = { "https://api.ipify.org", "https://checkip.amazonaws.com", "https://icanhazip.com" };

		public class PortMapping
		{
			public int Listen { get; set; }
			public int Public { get; set; }
		}

		/// <summary>
		/// A listening port waiting for one bot to connect.
		/// </summary>
		public class Reservation
		{
			public Guid Packet { get; internal set; }
			public TcpListener Listener { get; internal set; }
			public int ListenPort { get; internal set; }
			public int PublicPort { get; internal set; }
			/// <summary>
			/// The port is not listening any more.
			/// </summary>
			public bool Stopped { get; internal set; }
			internal DateTime Created { get; set; }
		}

		static readonly object _lock = new object();
		static List<PortMapping> _ports = new List<PortMapping>();
		static string _portsFile;
		static IPAddress _configuredAddress;
		static string[] _addressUrls = DefaultAddressUrls;
		static IPAddress _detectedAddress;
		static DateTime _addressChecked = DateTime.MinValue;
		static bool _detecting;
		static readonly Dictionary<Guid, Reservation> _unclaimed = new Dictionary<Guid, Reservation>();
		static readonly HashSet<int> _portsInUse = new HashSet<int>();

		static PassiveDcc()
		{
			try
			{
				Configure(Environment.GetEnvironmentVariable("XG_PASSIVE_DCC_PORTS"),
				          Environment.GetEnvironmentVariable("XG_PASSIVE_DCC_PORTS_FILE"),
				          Environment.GetEnvironmentVariable("XG_PASSIVE_DCC_IP"),
				          Environment.GetEnvironmentVariable("XG_PASSIVE_DCC_IP_URL"));
			}
			catch (Exception ex)
			{
				Log.Error("PassiveDcc() invalid configuration, passive DCC is disabled", ex);
				Configure(null, null, null, null);
			}
		}

		#region CONFIGURATION

		public static void Configure(string aPorts, string aPortsFile, string aAddress, string aAddressUrl)
		{
			lock (_lock)
			{
				_ports = ParsePorts(aPorts);
				_portsFile = string.IsNullOrWhiteSpace(aPortsFile) ? null : aPortsFile.Trim();
				_configuredAddress = null;
				if (!string.IsNullOrWhiteSpace(aAddress))
				{
					IPAddress address;
					if (!IPAddress.TryParse(aAddress.Trim(), out address) || address.AddressFamily != AddressFamily.InterNetwork)
					{
						throw new FormatException("XG_PASSIVE_DCC_IP must be an IPv4 address: " + aAddress);
					}
					_configuredAddress = address;
				}
				_addressUrls = string.IsNullOrWhiteSpace(aAddressUrl) ? DefaultAddressUrls : new[] { aAddressUrl.Trim() };
				_detectedAddress = null;
				_addressChecked = DateTime.MinValue;
			}
			if (Enabled)
			{
				Log.Info("Configure() passive DCC on ports " + (_portsFile != null ? "from " + _portsFile : string.Join(", ", _ports.Select(Describe))) +
				         ", public address " + (_configuredAddress != null ? _configuredAddress.ToString() : "detected automatically"));
			}
		}

		public static bool Enabled
		{
			get
			{
				lock (_lock)
				{
					return _ports.Count > 0 || _portsFile != null;
				}
			}
		}

		/// <summary>
		/// Parses "50000-50004", "50000:61234" and comma, space or line separated lists of both.
		/// </summary>
		public static List<PortMapping> ParsePorts(string aPorts)
		{
			var result = new List<PortMapping>();
			if (string.IsNullOrWhiteSpace(aPorts))
			{
				return result;
			}
			foreach (string part in aPorts.Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (part.Contains(":"))
				{
					var pair = part.Split(':');
					if (pair.Length != 2)
					{
						throw new FormatException("Invalid passive DCC port: " + part);
					}
					result.Add(new PortMapping { Listen = ParsePort(pair[0]), Public = ParsePort(pair[1]) });
				}
				else if (part.Contains("-"))
				{
					var range = part.Split('-');
					if (range.Length != 2)
					{
						throw new FormatException("Invalid passive DCC port range: " + part);
					}
					int first = ParsePort(range[0]), last = ParsePort(range[1]);
					if (last < first || last - first > 1000)
					{
						throw new FormatException("Invalid passive DCC port range: " + part);
					}
					for (int port = first; port <= last; port++)
					{
						result.Add(new PortMapping { Listen = port, Public = port });
					}
				}
				else
				{
					int port = ParsePort(part);
					result.Add(new PortMapping { Listen = port, Public = port });
				}
			}
			return result.GroupBy(p => p.Listen).Select(g => g.First()).ToList();
		}

		static int ParsePort(string aPort)
		{
			int port;
			if (!int.TryParse(aPort.Trim(), out port) || port < 1 || port > 65535)
			{
				throw new FormatException("Invalid passive DCC port: " + aPort);
			}
			return port;
		}

		static string Describe(PortMapping aMapping)
		{
			return aMapping.Listen == aMapping.Public ? "" + aMapping.Listen : aMapping.Listen + " (public " + aMapping.Public + ")";
		}

		static List<PortMapping> Ports()
		{
			if (_portsFile == null)
			{
				return _ports;
			}
			try
			{
				return ParsePorts(System.IO.File.ReadAllText(_portsFile));
			}
			catch (Exception ex)
			{
				Log.Error("Ports() can not read passive DCC ports from " + _portsFile, ex);
				return new List<PortMapping>();
			}
		}

		#endregion

		#region PUBLIC ADDRESS

		/// <summary>
		/// The IPv4 address bots have to connect to, null if it is not known (yet).
		/// </summary>
		public static IPAddress PublicAddress
		{
			get
			{
				IPAddress address;
				bool refresh;
				lock (_lock)
				{
					if (_configuredAddress != null)
					{
						return _configuredAddress;
					}
					address = _detectedAddress;
					refresh = !_detecting && DateTime.Now - _addressChecked > (address != null ? AddressLifetime : AddressRetry);
				}
				if (refresh)
				{
					if (address == null)
					{
						// nothing to answer with yet, so wait for the first detection
						DetectAddress();
						lock (_lock)
						{
							address = _detectedAddress;
						}
					}
					else
					{
						new Thread(DetectAddress) { IsBackground = true, Name = "PassiveDcc.DetectAddress" }.Start();
					}
				}
				return address;
			}
		}

		/// <summary>
		/// Looks up the public address in the background, so it is known before the first passive offer.
		/// </summary>
		public static void Prepare()
		{
			if (Enabled)
			{
				new Thread(() => { var address = PublicAddress; }) { IsBackground = true, Name = "PassiveDcc.Prepare" }.Start();
			}
		}

		static void DetectAddress()
		{
			string[] urls;
			lock (_lock)
			{
				if (_detecting)
				{
					return;
				}
				_detecting = true;
				urls = _addressUrls;
			}

			IPAddress found = null;
			foreach (string url in urls)
			{
				try
				{
					var request = (HttpWebRequest) WebRequest.Create(url);
					request.Timeout = 5000;
					request.ReadWriteTimeout = 5000;
					using (var response = request.GetResponse())
					using (var reader = new StreamReader(response.GetResponseStream()))
					{
						string text = reader.ReadToEnd().Trim();
						IPAddress address;
						if (IPAddress.TryParse(text, out address) && address.AddressFamily == AddressFamily.InterNetwork)
						{
							found = address;
							break;
						}
						Log.Warn("DetectAddress() " + url + " did not return an IPv4 address");
					}
				}
				catch (Exception ex)
				{
					Log.Warn("DetectAddress() " + url + " failed: " + ex.Message);
				}
			}

			lock (_lock)
			{
				_detecting = false;
				_addressChecked = DateTime.Now;
				if (found != null)
				{
					if (!found.Equals(_detectedAddress))
					{
						Log.Info("DetectAddress() public address is " + found);
					}
					_detectedAddress = found;
				}
				else if (_detectedAddress == null)
				{
					Log.Error("DetectAddress() the public address is unknown, set XG_PASSIVE_DCC_IP to receive passive DCC transfers");
				}
			}
		}

		/// <summary>
		/// The address as DCC writes it: the IPv4 address as one unsigned number.
		/// </summary>
		public static string ToDccAddress(IPAddress aAddress)
		{
			byte[] bytes = aAddress.GetAddressBytes();
			return "" + ((uint) bytes[0] << 24 | (uint) bytes[1] << 16 | (uint) bytes[2] << 8 | bytes[3]);
		}

		#endregion

		#region PORTS

		/// <summary>
		/// Starts listening on a free port for the packet. Returns null if no port is free.
		/// </summary>
		public static Reservation Reserve(Guid aPacket)
		{
			lock (_lock)
			{
				// offers which were answered but never turned into a download
				foreach (var stale in _unclaimed.Values.Where(r => r.Packet == aPacket || (DateTime.Now - r.Created).TotalSeconds > UnclaimedTimeoutSeconds).ToList())
				{
					_unclaimed.Remove(stale.Packet);
					Stop(stale);
				}

				foreach (var mapping in Ports())
				{
					if (_portsInUse.Contains(mapping.Listen))
					{
						continue;
					}
					try
					{
						var listener = new TcpListener(IPAddress.Any, mapping.Listen);
						listener.Start(1);
						var reservation = new Reservation
						{
							Packet = aPacket,
							Listener = listener,
							ListenPort = mapping.Listen,
							PublicPort = mapping.Public,
							Created = DateTime.Now
						};
						_portsInUse.Add(mapping.Listen);
						_unclaimed[aPacket] = reservation;
						Log.Info("Reserve() listening on port " + Describe(mapping) + " for packet " + aPacket);
						return reservation;
					}
					catch (SocketException ex)
					{
						Log.Warn("Reserve() port " + mapping.Listen + " is not available: " + ex.Message);
					}
				}
				Log.Error("Reserve() no passive DCC port is free");
				return null;
			}
		}

		/// <summary>
		/// Hands the reservation of the packet over to its download, null if there is none.
		/// </summary>
		public static Reservation Claim(Guid aPacket)
		{
			lock (_lock)
			{
				Reservation reservation;
				if (_unclaimed.TryGetValue(aPacket, out reservation))
				{
					_unclaimed.Remove(aPacket);
				}
				return reservation;
			}
		}

		public static void Release(Reservation aReservation)
		{
			if (aReservation == null)
			{
				return;
			}
			lock (_lock)
			{
				Reservation current;
				if (_unclaimed.TryGetValue(aReservation.Packet, out current) && current == aReservation)
				{
					_unclaimed.Remove(aReservation.Packet);
				}
				Stop(aReservation);
			}
		}

		static void Stop(Reservation aReservation)
		{
			aReservation.Stopped = true;
			try
			{
				aReservation.Listener.Stop();
			}
			catch (Exception) {}
			_portsInUse.Remove(aReservation.ListenPort);
		}

		#endregion
	}
}
