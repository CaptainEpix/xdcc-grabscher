// 
//  FileActions.cs
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
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using XG.Config.Properties;
using XG.Extensions;
using XG.Model.Domain;
using log4net;

namespace XG.Business.Helper
{
	public static class FileActions
	{
		#region VARIABLES

		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static Servers Servers { get; set; }

		static Files _files;
		public static Files Files
		{
			get { return _files; }
			set
			{
				if (_files != null)
				{
					_files.OnRemoved -= OnRemoveFile;
				}
				_files = value;
				if (_files != null)
				{
					_files.OnRemoved += OnRemoveFile;
				}
			}
		}

		#endregion

		#region EVENTS

		public static event EventHandler<EventArgs<Notification>> OnNotificationAdded = delegate {};

		public static void FireNotificationAdded(Notification.Types aType, AObject aObject)
		{
			var eventArgs = new EventArgs<Notification>(new Notification(aType, aObject));
			OnNotificationAdded(null, eventArgs);
		}

		/// <summary>
		/// A completely downloaded file is about to be moved into the ready folder.
		/// Fired before the matching packets are disabled; carries the file and the matching packets.
		/// </summary>
		public static event EventHandler<EventArgs<XG.Model.Domain.File, Packet[]>> OnFileFinishing = delegate {};

		/// <summary>
		/// A file was finished. Carries the file, the matching packets, the ready path
		/// and whether the file could be moved there.
		/// </summary>
		public static event EventHandler<EventArgs<XG.Model.Domain.File, Packet[], string, bool>> OnFileFinished = delegate {};

		static void FireFileFinishing(XG.Model.Domain.File aFile, Packet[] aPackets)
		{
			try
			{
				OnFileFinishing(null, new EventArgs<XG.Model.Domain.File, Packet[]>(aFile, aPackets));
			}
			catch (Exception ex)
			{
				Log.Error("FireFileFinishing(" + aFile + ")", ex);
			}
		}

		static void FireFileFinished(XG.Model.Domain.File aFile, Packet[] aPackets, string aReadyPath, bool aSuccess)
		{
			try
			{
				OnFileFinished(null, new EventArgs<XG.Model.Domain.File, Packet[], string, bool>(aFile, aPackets, aReadyPath, aSuccess));
			}
			catch (Exception ex)
			{
				Log.Error("FireFileFinished(" + aFile + ")", ex);
			}
		}

		#endregion

		#region FILE

		/// <summary>
		/// Optional: the folder a finished file goes to instead of the ready folder, null or empty for the ready folder.
		/// </summary>
		public static Func<XG.Model.Domain.File, Packet[], string> ReadyFolderResolver { get; set; }

		static string ReadyFolder(XG.Model.Domain.File aFile, Packet[] aPackets)
		{
			var resolver = ReadyFolderResolver;
			if (resolver != null)
			{
				try
				{
					string folder = resolver(aFile, aPackets);
					if (!string.IsNullOrEmpty(folder))
					{
						if (!folder.EndsWith("" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
						{
							folder += Path.DirectorySeparatorChar;
						}
						Directory.CreateDirectory(folder);
						return folder;
					}
				}
				catch (Exception ex)
				{
					Log.Error("ReadyFolder(" + aFile + ") using the ready folder", ex);
				}
			}
			return Settings.Default.ReadyPath;
		}

		public static XG.Model.Domain.File TryGetFile(string aName, Int64 aSize)
		{
			string name = XG.Model.Domain.Helper.ShrinkFileName(aName, aSize);
			foreach (var file in Files.All)
			{
				if (file.TmpName == name)
				{
					// lets check if the directory is still on the harddisk
					if (!System.IO.File.Exists(Settings.Default.TempPath + file.TmpName))
					{
						Log.Warn("TryGetFile(" + aName + ", " + aSize + ") " + file + " is missing ");
						Files.Remove(file);
						break;
					}
					return file;
				}
			}
			return null;
		}

		public static XG.Model.Domain.File GetFileOrCreateNew(string aName, Int64 aSize)
		{
			var tFile = TryGetFile(aName, aSize);
			if (tFile != null)
			{
				return tFile;
			}

			tFile = new XG.Model.Domain.File(aName, aSize);
			try
			{
				System.IO.File.Create(Settings.Default.TempPath + tFile.TmpName).Close();
				Files.Add(tFile);
			}
			catch (Exception ex)
			{
				Log.Fatal("GetFileOrCreateNew(" + aName + ", " + aSize + ")", ex);
				tFile = null;
			}

			return tFile;
		}

		static void OnRemoveFile(object aSender, EventArgs<AObject, AObject> aEventArgs)
		{
			if (aEventArgs.Value2 is XG.Model.Domain.File)
			{
				var file = (XG.Model.Domain.File)aEventArgs.Value2;
				Log.Info("RemoveFile(" + file + ")");
				FileSystem.DeleteFile(Settings.Default.TempPath + file.TmpName);
			}
		}

		#endregion

		#region FINISH FILE

		public static void FinishFile(XG.Model.Domain.File aFile)
		{
			lock (aFile)
			{
				if (aFile.MissingSize > 0)
				{
					return;
				}

				#region DISABLE ALL MATCHING PACKETS

				string fileName = XG.Model.Domain.Helper.ShrinkFileName(aFile.Name, 0);
				List<Packet> matchedPackets = (from server in Servers.All from channel in server.Channels from bot in channel.Bots from packet in bot.Packets where packet.Enabled && (XG.Model.Domain.Helper.ShrinkFileName(packet.RealName, 0).EndsWith(fileName) || XG.Model.Domain.Helper.ShrinkFileName(packet.Name, 0).EndsWith(fileName)) select packet).ToList();
				FireFileFinishing(aFile, matchedPackets.ToArray());
				foreach (Packet tPack in matchedPackets)
				{
					Log.Info("FinishFile(" + aFile + ") disabling " + tPack + " from " + tPack.Parent);
					tPack.Enabled = false;
					tPack.Commit();
				}

				#endregion

				string tmpPath = Settings.Default.TempPath + aFile.TmpName;
				string readyFolder = ReadyFolder(aFile, matchedPackets.ToArray());
				string readyPath = FileSystem.FreeFileName(readyFolder + aFile.Name);
				if (readyPath != readyFolder + aFile.Name)
				{
					Log.Warn("FinishFile(" + aFile + ") " + aFile.Name + " already exists, saving as " + readyPath);
				}

				try
				{
					if (FileSystem.MoveFile(tmpPath, readyPath))
					{
						Files.Remove(aFile);
						FireFileFinished(aFile, matchedPackets.ToArray(), readyPath, true);

						// great, all went right, so lets check what we can do with the file
						var thread = new Thread(() =>
						{
							// an unhandled exception in any thread ends the whole process under Mono
							try
							{
								HandleFile(readyPath);
							}
							catch (Exception ex)
							{
								Log.Fatal("HandleFile(" + readyPath + ")", ex);
							}
						});
						thread.Name = "HandleFile|" + aFile.Name;
						thread.Start();
					}
					else
					{
						Log.Fatal("FinishFile(" + aFile + ") cant move file");
						FireFileFinished(aFile, matchedPackets.ToArray(), readyPath, false);

						FireNotificationAdded(Notification.Types.FileFinishFailed, aFile);
					}
				}
				catch (Exception ex)
				{
					Log.Fatal("FinishFile(" + aFile + ") cant finish file", ex);
					FireFileFinished(aFile, matchedPackets.ToArray(), readyPath, false);

					FireNotificationAdded(Notification.Types.FileFinishFailed, aFile);
				}
			}
		}

		static void HandleFile(string aFile)
		{
			if (!String.IsNullOrEmpty(aFile))
			{
				string folder = Path.GetDirectoryName(aFile);
				string file = Path.GetFileName(aFile);
				string fileName = Path.GetFileNameWithoutExtension(aFile);
				string fileExtension = Path.GetExtension(aFile);
				if (fileExtension != null && fileExtension.StartsWith(".", StringComparison.CurrentCulture))
				{
					fileExtension = fileExtension.Substring(1);
				}

				foreach (FileHandler handler in Settings.Default.GetFileHandlers())
				{
					try
					{
						Match tMatch = Regex.Match(aFile, handler.Regex, RegexOptions.IgnoreCase);
						if (tMatch.Success)
						{
							RunFileHandlerProcess(handler.Process, aFile, folder, file, fileName, fileExtension);
						}
					}
					catch (Exception ex)
					{
						Log.Fatal("RunFileHandler(" + aFile + ") Regex Error (" + handler.Regex + ")", ex);
					}
				}
			}
		}

		static void RunFileHandlerProcess(FileHandlerProcess aHandler, string aPath, string aFolder, string aFile, string aFileName, string aFileExtension)
		{
			if (aHandler == null || String.IsNullOrEmpty(aHandler.Command) || String.IsNullOrEmpty(aHandler.Arguments))
			{
				return;
			}

			string arguments = aHandler.Arguments;
			arguments = arguments.Replace("%PATH%", aPath);
			arguments = arguments.Replace("%FOLDER%", aFolder);
			arguments = arguments.Replace("%FILE%", aFile);
			arguments = arguments.Replace("%FILENAME%", aFileName);
			arguments = arguments.Replace("%EXTENSION%", aFileExtension);

			var p = new Process {Command = aHandler.Command, Arguments = arguments};

			if (p.Run())
			{
				RunFileHandlerProcess(aHandler.Next, aPath, aFolder, aFile, aFileName, aFileExtension);
			}
		}

		#endregion
	}
}
