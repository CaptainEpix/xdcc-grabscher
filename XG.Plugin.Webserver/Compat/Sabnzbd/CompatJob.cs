//
//  CompatJob.cs
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

namespace XG.Plugin.Webserver.Compat.Sabnzbd
{
	public enum CompatJobState
	{
		Active,
		Completed,
		Failed
	}

	/// <summary>
	/// A download submitted through the SABnzbd compatible api.
	/// The job has its own id, so the same packet can be grabbed again after a job ended.
	/// </summary>
	public class CompatJob
	{
		public const string IdPrefix = "XG_";

		/// <summary>
		/// The SABnzbd style id (nzo_id), stable for the whole life of the job.
		/// </summary>
		public string Id { get; set; }

		public Guid PacketGuid { get; set; }

		public string Title { get; set; }

		public string Category { get; set; }

		public int Priority { get; set; }

		public DateTime Added { get; set; }

		public CompatJobState State { get; set; }

		/// <summary>
		/// The download is complete and XG is moving it into the ready folder.
		/// </summary>
		public bool Finishing { get; set; }

		/// <summary>
		/// Name of the downloaded file as sent by the bot.
		/// </summary>
		public string FileName { get; set; }

		/// <summary>
		/// Size in bytes; the packet size until the download finished, then the real file size.
		/// </summary>
		public Int64 Size { get; set; }

		/// <summary>
		/// Full path of the finished file, as seen by XG.
		/// </summary>
		public string StoragePath { get; set; }

		public DateTime? Finished { get; set; }

		public string FailMessage { get; set; }

		public static string CreateId()
		{
			return IdPrefix + Guid.NewGuid().ToString("N");
		}

		public CompatJob Clone()
		{
			return (CompatJob)MemberwiseClone();
		}
	}
}
