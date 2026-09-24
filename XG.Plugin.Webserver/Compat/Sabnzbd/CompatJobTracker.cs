//
//  CompatJobTracker.cs
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
using System.Reflection;
using log4net;
using XG.Model.Domain;

namespace XG.Plugin.Webserver.Compat.Sabnzbd
{
	/// <summary>
	/// A queue entry: the job plus the live state of its packet.
	/// </summary>
	public class CompatQueueItem
	{
		public CompatJob Job { get; set; }
		public string Status { get; set; }
		public Int64 TotalSize { get; set; }
		public Int64 RemainingSize { get; set; }
		public Int64 Speed { get; set; }
		public Int64 SecondsLeft { get; set; }
	}

	/// <summary>
	/// Error which is reported to the client as a SABnzbd error.
	/// </summary>
	public class SabnzbdException : Exception
	{
		public SabnzbdException(string aMessage) : base(aMessage) {}
	}

	/// <summary>
	/// Tracks the downloads submitted through the SABnzbd compatible api.
	/// It works on its own small job list and only looks up the packets of those jobs,
	/// so polling the queue or history never walks the whole packet tree.
	/// </summary>
	public class CompatJobTracker
	{
		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		// finished jobs which are never removed by a client are dropped eventually
		public const int MaxFinishedJobs = 1000;

		readonly object _lock = new object();
		readonly CompatJobStore _store;
		readonly Func<Guid, Packet> _packetLookup;
		readonly Func<string> _readyPath;
		List<CompatJob> _jobs = new List<CompatJob>();

		public CompatJobTracker(CompatJobStore aStore, Func<Guid, Packet> aPacketLookup, Func<string> aReadyPath)
		{
			_store = aStore;
			_packetLookup = aPacketLookup;
			_readyPath = aReadyPath;
		}

		#region LIFECYCLE

		/// <summary>
		/// Loads the persisted jobs and settles everything that happened while XG was not
		/// watching, like downloads which were finished during startup.
		/// </summary>
		public void Start()
		{
			lock (_lock)
			{
				_jobs = _store.Load();
				foreach (var job in _jobs.Where(j => j.State == CompatJobState.Active))
				{
					Reconcile(job);
				}
				Save();
				Log.Info("Start() tracking " + _jobs.Count(j => j.State == CompatJobState.Active) + " active and " + _jobs.Count(j => j.State != CompatJobState.Active) + " finished compatibility jobs");
			}
		}

		void Reconcile(CompatJob aJob)
		{
			var packet = _packetLookup(aJob.PacketGuid);
			if (packet != null && packet.Enabled && !aJob.Finishing)
			{
				return;
			}

			string path = FindFinishedFile(aJob, packet);
			if (path != null)
			{
				Complete(aJob, path);
			}
			else if (packet == null)
			{
				Fail(aJob, "The XG packet no longer exists");
			}
			else
			{
				Fail(aJob, "The download stopped while XG was not running");
			}
		}

		string FindFinishedFile(CompatJob aJob, Packet aPacket)
		{
			var names = new List<string>();
			if (!string.IsNullOrEmpty(aJob.FileName))
			{
				names.Add(aJob.FileName);
			}
			if (aPacket != null)
			{
				if (!string.IsNullOrEmpty(aPacket.RealName))
				{
					names.Add(XG.Model.Domain.Helper.RemoveBadCharsFromFileName(aPacket.RealName));
				}
				if (!string.IsNullOrEmpty(aPacket.Name))
				{
					names.Add(XG.Model.Domain.Helper.RemoveBadCharsFromFileName(aPacket.Name));
				}
			}

			foreach (string name in names.Distinct())
			{
				if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
				{
					continue;
				}
				var info = new FileInfo(Path.Combine(_readyPath(), name));
				if (info.Exists && (aJob.Size <= 0 || info.Length == aJob.Size))
				{
					return info.FullName;
				}
			}
			return null;
		}

		#endregion

		#region SABNZBD OPERATIONS

		/// <summary>
		/// Registers a job for a packet and starts the download with the normal XG machinery.
		/// </summary>
		public CompatJob Add(Nzb.SyntheticNzb aNzb, string aCategory, int aPriority)
		{
			var packet = _packetLookup(aNzb.PacketGuid);
			if (packet == null)
			{
				throw new SabnzbdException("Unknown XG packet, it may have been removed from the bot");
			}

			// XDCC bots reuse pack numbers, so make sure the packet still offers the same file
			string title = Newznab.NewznabHandler.PacketTitle(packet);
			if (!string.IsNullOrEmpty(aNzb.Title) && !string.Equals(aNzb.Title, title, StringComparison.OrdinalIgnoreCase))
			{
				throw new SabnzbdException("The XG packet changed since it was found, it now offers '" + title + "'");
			}

			CompatJob job;
			lock (_lock)
			{
				job = _jobs.FirstOrDefault(j => j.State == CompatJobState.Active && j.PacketGuid == packet.Guid);
				if (job != null)
				{
					Log.Info("Add() packet " + packet.Guid + " is already tracked by " + job.Id);
				}
				else
				{
					job = new CompatJob
					{
						Id = CompatJob.CreateId(),
						PacketGuid = packet.Guid,
						Title = title,
						Category = string.IsNullOrWhiteSpace(aCategory) ? "*" : aCategory.Trim(),
						Priority = aPriority,
						Added = DateTime.UtcNow,
						State = CompatJobState.Active,
						Size = Newznab.NewznabHandler.PacketSize(packet)
					};
					_jobs.Add(job);
					Save();
					Log.Info("Add() " + job.Id + " for packet " + packet.Guid + " '" + title + "' in category " + job.Category);
				}
				job = job.Clone();
			}

			// never toggle packets while holding the lock, the irc plugin reacts synchronously
			if (!packet.Enabled)
			{
				packet.Enabled = true;
				packet.Commit();
			}
			return job;
		}

		public List<CompatQueueItem> Queue(string aCategory)
		{
			lock (_lock)
			{
				return (from job in _jobs
				        where job.State == CompatJobState.Active && MatchesCategory(job, aCategory)
				        orderby job.Added
				        select CreateQueueItem(job)).ToList();
			}
		}

		CompatQueueItem CreateQueueItem(CompatJob aJob)
		{
			var item = new CompatQueueItem { Job = aJob.Clone(), TotalSize = aJob.Size, RemainingSize = aJob.Size, Status = "Queued" };

			if (aJob.Finishing)
			{
				item.Status = "Moving";
				item.RemainingSize = 0;
				return item;
			}

			var packet = _packetLookup(aJob.PacketGuid);
			if (packet == null)
			{
				return item;
			}

			var file = packet.File;
			if (file != null && file.Size > 0)
			{
				item.TotalSize = file.Size;
				item.RemainingSize = Math.Max(0, file.MissingSize);
				item.Speed = file.Speed;
				item.SecondsLeft = file.TimeMissing;
			}
			if (packet.Connected)
			{
				item.Status = "Downloading";
			}
			return item;
		}

		public List<CompatJob> History(string aCategory)
		{
			lock (_lock)
			{
				return (from job in _jobs
				        where job.State != CompatJobState.Active && MatchesCategory(job, aCategory)
				        orderby job.Finished descending
				        select job.Clone()).ToList();
			}
		}

		public IEnumerable<string> Categories()
		{
			lock (_lock)
			{
				return _jobs.Select(j => j.Category).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList();
			}
		}

		/// <summary>
		/// Cancels active jobs. XG removes the partial file when a running transfer is
		/// stopped, so partial data can not be kept regardless of what the client asked for.
		/// </summary>
		public int RemoveFromQueue(IEnumerable<string> aIds)
		{
			var removed = new List<CompatJob>();
			var packetsToStop = new List<Guid>();
			lock (_lock)
			{
				foreach (string id in aIds)
				{
					var job = _jobs.FirstOrDefault(j => j.Id == id && j.State == CompatJobState.Active);
					if (job != null)
					{
						_jobs.Remove(job);
						removed.Add(job);
					}
				}
				if (removed.Count > 0)
				{
					Save();
				}
				foreach (var job in removed)
				{
					if (!job.Finishing && !_jobs.Any(j => j.State == CompatJobState.Active && j.PacketGuid == job.PacketGuid))
					{
						packetsToStop.Add(job.PacketGuid);
					}
					Log.Info("RemoveFromQueue() " + job.Id + " for packet " + job.PacketGuid);
				}
			}

			foreach (var guid in packetsToStop)
			{
				var packet = _packetLookup(guid);
				if (packet != null && packet.Enabled)
				{
					packet.Enabled = false;
					packet.Commit();
				}
			}
			return removed.Count;
		}

		/// <summary>
		/// Removes finished jobs. The downloaded file is only deleted on explicit request,
		/// and only if it still is the very file this job produced inside the ready folder.
		/// </summary>
		public int RemoveFromHistory(IEnumerable<string> aIds, bool aDeleteFiles)
		{
			var removed = new List<CompatJob>();
			lock (_lock)
			{
				foreach (string id in aIds)
				{
					var job = _jobs.FirstOrDefault(j => j.Id == id && j.State != CompatJobState.Active);
					if (job != null)
					{
						_jobs.Remove(job);
						removed.Add(job);
					}
				}
				if (removed.Count > 0)
				{
					Save();
				}
			}

			foreach (var job in removed)
			{
				Log.Info("RemoveFromHistory() " + job.Id + (aDeleteFiles ? " with files" : ""));
				if (aDeleteFiles)
				{
					DeleteJobFile(job);
				}
			}
			return removed.Count;
		}

		void DeleteJobFile(CompatJob aJob)
		{
			if (aJob.State != CompatJobState.Completed || string.IsNullOrEmpty(aJob.StoragePath))
			{
				return;
			}
			try
			{
				string readyPath = Path.GetFullPath(_readyPath());
				if (!readyPath.EndsWith("" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
				{
					readyPath += Path.DirectorySeparatorChar;
				}
				var info = new FileInfo(aJob.StoragePath);
				if (!info.FullName.StartsWith(readyPath, StringComparison.Ordinal))
				{
					Log.Warn("DeleteJobFile() " + aJob.Id + " refusing to delete a file outside of the ready folder");
					return;
				}
				if (info.Exists && (aJob.Size <= 0 || info.Length == aJob.Size))
				{
					info.Delete();
					Log.Info("DeleteJobFile() " + aJob.Id + " deleted " + info.FullName);
				}
			}
			catch (Exception ex)
			{
				Log.Error("DeleteJobFile() " + aJob.Id, ex);
			}
		}

		/// <summary>
		/// Restarts a failed job with the same id.
		/// </summary>
		public CompatJob Retry(string aId)
		{
			CompatJob job;
			Packet packet;
			lock (_lock)
			{
				job = _jobs.FirstOrDefault(j => j.Id == aId);
				if (job == null)
				{
					throw new SabnzbdException("Unknown job");
				}
				if (job.State != CompatJobState.Failed)
				{
					throw new SabnzbdException("Only failed jobs can be retried");
				}
				packet = _packetLookup(job.PacketGuid);
				if (packet == null)
				{
					throw new SabnzbdException("The XG packet no longer exists");
				}
				if (_jobs.Any(j => j != job && j.State == CompatJobState.Active && j.PacketGuid == job.PacketGuid))
				{
					throw new SabnzbdException("The XG packet is already being downloaded by another job");
				}

				job.State = CompatJobState.Active;
				job.Finishing = false;
				job.FailMessage = null;
				job.Finished = null;
				job.Added = DateTime.UtcNow;
				Save();
				Log.Info("Retry() " + job.Id + " for packet " + job.PacketGuid);
				job = job.Clone();
			}

			if (!packet.Enabled)
			{
				packet.Enabled = true;
				packet.Commit();
			}
			return job;
		}

		#endregion

		#region XG EVENTS

		public void PacketEnabledChanged(Packet aPacket)
		{
			if (aPacket.Enabled)
			{
				return;
			}
			lock (_lock)
			{
				bool changed = false;
				foreach (var job in _jobs.Where(j => j.State == CompatJobState.Active && !j.Finishing && j.PacketGuid == aPacket.Guid))
				{
					string message = "XG stopped the download (bot unavailable, request denied or disabled in XG)";
					if (aPacket.Parent != null && !string.IsNullOrWhiteSpace(aPacket.Parent.LastMessage))
					{
						message += ". Last bot message: " + aPacket.Parent.LastMessage;
					}
					Fail(job, message);
					changed = true;
				}
				if (changed)
				{
					Save();
				}
			}
		}

		public void PacketRemoved(Packet aPacket)
		{
			lock (_lock)
			{
				bool changed = false;
				foreach (var job in _jobs.Where(j => j.State == CompatJobState.Active && !j.Finishing && j.PacketGuid == aPacket.Guid))
				{
					Fail(job, "The packet was removed from the bot");
					changed = true;
				}
				if (changed)
				{
					Save();
				}
			}
		}

		public void FileFinishing(XG.Model.Domain.File aFile, IEnumerable<Packet> aPackets)
		{
			var guids = PacketGuids(aFile, aPackets);
			lock (_lock)
			{
				bool changed = false;
				foreach (var job in _jobs.Where(j => j.State == CompatJobState.Active && guids.Contains(j.PacketGuid)))
				{
					job.Finishing = true;
					job.FileName = aFile.Name;
					job.Size = aFile.Size;
					changed = true;
					Log.Info("FileFinishing() " + job.Id + " downloaded, moving " + aFile.Name);
				}
				if (changed)
				{
					Save();
				}
			}
		}

		public void FileFinished(XG.Model.Domain.File aFile, IEnumerable<Packet> aPackets, string aReadyPath, bool aSuccess)
		{
			var guids = PacketGuids(aFile, aPackets);
			lock (_lock)
			{
				bool changed = false;
				foreach (var job in _jobs.Where(j => j.State == CompatJobState.Active && j.Finishing && guids.Contains(j.PacketGuid)))
				{
					if (aSuccess)
					{
						Complete(job, aReadyPath);
					}
					else
					{
						Fail(job, "XG could not move the finished file into its download folder");
					}
					changed = true;
				}
				if (changed)
				{
					Save();
				}
			}
		}

		static HashSet<Guid> PacketGuids(XG.Model.Domain.File aFile, IEnumerable<Packet> aPackets)
		{
			var guids = new HashSet<Guid>(aPackets.Where(p => p != null).Select(p => p.Guid));
			// the packet which actually delivered the file, even if its name did not match
			if (aFile.PacketGuid != Guid.Empty)
			{
				guids.Add(aFile.PacketGuid);
			}
			if (aFile.PacketGuidOld != Guid.Empty)
			{
				guids.Add(aFile.PacketGuidOld);
			}
			return guids;
		}

		#endregion

		#region HELPER

		void Complete(CompatJob aJob, string aPath)
		{
			aJob.State = CompatJobState.Completed;
			aJob.Finishing = false;
			aJob.StoragePath = aPath;
			aJob.FileName = Path.GetFileName(aPath);
			aJob.Finished = DateTime.UtcNow;
			try
			{
				var info = new FileInfo(aPath);
				if (info.Exists)
				{
					aJob.Size = info.Length;
				}
			}
			catch (Exception) {}
			Log.Info("Complete() " + aJob.Id + " stored at " + aPath);
		}

		void Fail(CompatJob aJob, string aMessage)
		{
			aJob.State = CompatJobState.Failed;
			aJob.Finishing = false;
			aJob.FailMessage = aMessage;
			aJob.Finished = DateTime.UtcNow;
			Log.Warn("Fail() " + aJob.Id + " for packet " + aJob.PacketGuid + ": " + aMessage);
		}

		static bool MatchesCategory(CompatJob aJob, string aCategory)
		{
			return string.IsNullOrWhiteSpace(aCategory) || aCategory == "*" || string.Equals(aJob.Category, aCategory, StringComparison.OrdinalIgnoreCase);
		}

		void Save()
		{
			var finished = _jobs.Where(j => j.State != CompatJobState.Active).OrderByDescending(j => j.Finished).ToList();
			if (finished.Count > MaxFinishedJobs)
			{
				foreach (var job in finished.Skip(MaxFinishedJobs))
				{
					_jobs.Remove(job);
				}
			}

			try
			{
				_store.Save(_jobs);
			}
			catch (Exception ex)
			{
				// the in memory state stays correct, the next change tries again
				Log.Error("Save() can not persist compatibility jobs to " + _store.Path, ex);
			}
		}

		#endregion
	}
}
