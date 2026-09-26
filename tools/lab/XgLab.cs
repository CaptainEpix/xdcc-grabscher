//
//  XgLab.cs
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

// Starts the complete XG application like XG.Application does (database, IRC and
// webserver plugins), but seeds a lab IRC server, channel and API key first and
// logs at INFO level to the console. Used by tools/lab/run.sh.
//
// Usage: mono XgLab.exe <irc host> <irc port> <channel> <api key guid> <web port>

using System;
using System.IO;
using System.Linq;
using System.Threading;
using log4net.Appender;
using log4net.Core;
using log4net.Layout;
using log4net.Repository.Hierarchy;
using XG.Business;
using XG.Config.Properties;
using XG.Model.Domain;

class XgLab
{
	static App _app;

	static void Main(string[] args)
	{
		string ircHost = args[0];
		int ircPort = int.Parse(args[1]);
		string channelName = args[2];
		var apiKeyGuid = new Guid(args[3]);
		int webPort = int.Parse(args[4]);

		var root = ((Hierarchy)log4net.LogManager.GetRepository()).Root;
		var appender = new ConsoleAppender
		{
			Layout = new PatternLayout("%date{HH:mm:ss,fff} %-5level [%thread] %logger{1} %message%newline"),
			Threshold = Level.Info
		};
		appender.ActivateOptions();
		root.AddAppender(appender);
		root.Repository.Configured = true;

		string appData = Settings.Default.GetAppDataPath();
		Settings.Default.TempPath = appData + "tmp" + Path.DirectorySeparatorChar;
		Settings.Default.ReadyPath = appData + "dl" + Path.DirectorySeparatorChar;
		Settings.Default.WebserverPort = webPort;
		Directory.CreateDirectory(Settings.Default.TempPath);
		Directory.CreateDirectory(Settings.Default.ReadyPath);
		Settings.Default.Save();

		_app = new App();

		var server = _app.Servers.Server(ircHost);
		if (server == null)
		{
			_app.Servers.Add(ircHost, ircPort);
			server = _app.Servers.Server(ircHost);
			server.AddChannel(channelName);
			server.Channel(channelName).Enabled = true;
			Console.WriteLine("LAB seeded server " + ircHost + ":" + ircPort + " " + channelName);
		}

		if (_app.ApiKeys.WithGuid(apiKeyGuid) == null)
		{
			var key = new ApiKey { Name = "lab", Enabled = true };
			key.Guid = apiKeyGuid;
			_app.ApiKeys.Add(key);
		}

		_app.AddPlugin(new XG.Plugin.Irc.Plugin());
		var webServer = new XG.Plugin.Webserver.Plugin { RrdDB = _app.RrdDb };
		webServer.OnShutdown += delegate { _app.Shutdown(webServer); };
		_app.AddPlugin(webServer);
		_app.OnShutdownComplete += delegate { Environment.Exit(0); };
		_app.Start(typeof(App).ToString());
		Console.WriteLine("LAB started");

		// commands on stdin, like clicking in the web UI:
		//   enable <bot> <pack>   disable <bot> <pack>   packets   quit
		string line;
		while ((line = Console.ReadLine()) != null && line != "quit")
		{
			try
			{
				Command(line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
			}
			catch (Exception ex)
			{
				Console.WriteLine("LAB command failed: " + ex.Message);
			}
		}
		_app.Shutdown("lab");
		Thread.Sleep(Timeout.Infinite);
	}

	static void Command(string[] aArgs)
	{
		if (aArgs.Length == 0)
		{
			return;
		}
		var packets = from server in _app.Servers.All from channel in server.Channels from bot in channel.Bots from packet in bot.Packets select packet;
		switch (aArgs[0])
		{
			case "enable":
			case "disable":
				var packet = packets.First(p => p.Parent.Name == aArgs[1] && p.Id == int.Parse(aArgs[2]));
				packet.Enabled = aArgs[0] == "enable";
				Console.WriteLine("LAB " + aArgs[0] + "d " + packet);
				break;
			case "packets":
				foreach (var p in packets)
				{
					Console.WriteLine("LAB packet " + p.Parent.Name + " #" + p.Id + " enabled=" + p.Enabled + " connected=" + p.Connected + " " + p.Name);
				}
				break;
		}
	}
}
