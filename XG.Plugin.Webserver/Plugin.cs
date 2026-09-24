// 
//  Plugin.cs
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Owin.Hosting;
using Nowin;
using SharpRobin.Core;
using XG.Business.Helper;
using XG.Config.Properties;
using XG.Extensions;
using XG.Model.Domain;
using XG.Plugin.Webserver.Compat;
using XG.Plugin.Webserver.Compat.Sabnzbd;

namespace XG.Plugin.Webserver
{
	public class Plugin : APlugin
	{
		#region VARIABLES

		IDisposable _server;
		SignalR.EventForwarder _eventForwarder;
		CompatJobTracker _compatJobTracker;

		public RrdDb RrdDB { get; set; }

		SHA256Managed _sha256 = new SHA256Managed();

		#endregion

		#region EVENTS

		public virtual event EventHandler OnShutdown = delegate {};

		protected void FireShutdown(object aSender, EventArgs aEventArgs)
		{
			OnShutdown(aSender, aEventArgs);
		}

		#endregion

		string Hash(string aStr = null)
		{
			byte[] bytes = aStr == null ? BitConverter.GetBytes(new Random().Next()) : Encoding.UTF8.GetBytes(aStr);
			return BitConverter.ToString(_sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
		}

		#region AWorker

		protected override void StartRun()
		{
			Search.Packets.Servers = Servers;
			Search.Packets.Initialize();

			StartCompat();

			AddRepeatingJob(typeof(Job.SearchUpdater), "SearchUpdater", "WebserverPlugin", Settings.Default.TakeSnapshotTimeInMinutes * 60,
				new JobItem("Searches", Searches));

			string salt = Hash();
			string passwortHash = Hash(salt + Settings.Default.Password + salt);

			SignalR.Hub.Helper.Servers = Servers;
			SignalR.Hub.Helper.Files = Files;
			SignalR.Hub.Helper.Searches = Searches;
			SignalR.Hub.Helper.Notifications = Notifications;
			SignalR.Hub.Helper.RrdDb = RrdDB;
			SignalR.Hub.Helper.ApiKeys = ApiKeys;
			SignalR.Hub.Helper.PasswortHash = passwortHash;

			Nancy.Helper.Servers = Servers;
			Nancy.Helper.Files = Files;
			Nancy.Helper.Searches = Searches;
			Nancy.Helper.ApiKeys = ApiKeys;
			Nancy.Helper.Salt = salt;
			Nancy.Helper.PasswortHash = passwortHash;
			Nancy.Helper.OnShutdown += FireShutdown;

			var options = new StartOptions("http://*:" + Settings.Default.WebserverPort)
			{
				ServerFactory = "Nowin"
			};
			_server = WebApp.Start<Startup>(options);

			_eventForwarder = new SignalR.EventForwarder();
			_eventForwarder.Servers = Servers;
			_eventForwarder.Files = Files;
			_eventForwarder.Searches = Searches;
			_eventForwarder.Notifications = Notifications;
			_eventForwarder.ApiKeys = ApiKeys;
			_eventForwarder.Start(typeof(SignalR.EventForwarder).ToString());

			var settings = new RemoteSettings { Version = new Version(), ExternalSearch = new ExternalSearch { Enabled = false } };
			SignalR.Hub.Helper.RemoteSettings = settings;
			Nancy.Helper.RemoteSettings = settings;
		}

		protected override void StopRun()
		{
			Nancy.Helper.OnShutdown -= FireShutdown;
			_eventForwarder.Stop();
			_server.Dispose();
			StopCompat();
		}

		#endregion

		#region COMPATIBILITY APIS

		void StartCompat()
		{
			CompatApiKeys.ApiKeys = ApiKeys;

			_compatJobTracker = new CompatJobTracker(
				new CompatJobStore(Settings.Default.GetAppDataPath() + "arr-jobs.json"),
				Search.Packets.GetPacket,
				() => Settings.Default.ReadyPath);

			FileActions.OnFileFinishing += FileFinishing;
			FileActions.OnFileFinished += FileFinished;
			_compatJobTracker.Start();

			Nancy.Compat.SabnzbdModule.Handler = new SabnzbdHandler(_compatJobTracker, CompatApiKeys.IsValid, () => Settings.Default.ReadyPath);
		}

		void StopCompat()
		{
			Nancy.Compat.SabnzbdModule.Handler = null;
			FileActions.OnFileFinishing -= FileFinishing;
			FileActions.OnFileFinished -= FileFinished;
		}

		void FileFinishing(object aSender, EventArgs<File, Packet[]> aEventArgs)
		{
			_compatJobTracker.FileFinishing(aEventArgs.Value1, aEventArgs.Value2);
		}

		void FileFinished(object aSender, EventArgs<File, Packet[], string, bool> aEventArgs)
		{
			_compatJobTracker.FileFinished(aEventArgs.Value1, aEventArgs.Value2, aEventArgs.Value3, aEventArgs.Value4);
		}

		protected override void ObjectEnabledChanged(object aSender, EventArgs<AObject> aEventArgs)
		{
			var packet = aEventArgs.Value1 as Packet;
			if (packet != null && _compatJobTracker != null)
			{
				_compatJobTracker.PacketEnabledChanged(packet);
			}
		}

		protected override void ObjectRemoved(object aSender, EventArgs<AObject, AObject> aEventArgs)
		{
			if (_compatJobTracker == null)
			{
				return;
			}
			// removing a bot, channel or server removes its packets without separate events
			foreach (var packet in PacketsOf(aEventArgs.Value2))
			{
				_compatJobTracker.PacketRemoved(packet);
			}
		}

		static IEnumerable<Packet> PacketsOf(AObject aObject)
		{
			var packet = aObject as Packet;
			if (packet != null)
			{
				return new[] { packet };
			}
			var objects = aObject as AObjects;
			if (objects != null)
			{
				return objects.Children.SelectMany(PacketsOf).ToArray();
			}
			return new Packet[0];
		}

		#endregion
	}
}
