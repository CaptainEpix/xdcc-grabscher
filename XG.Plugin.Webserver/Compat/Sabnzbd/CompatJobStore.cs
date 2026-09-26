//
//  CompatJobStore.cs
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
using System.Reflection;
using System.Text;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace XG.Plugin.Webserver.Compat.Sabnzbd
{
	/// <summary>
	/// Persists the compatibility jobs as a small JSON file next to the XG database.
	/// Kept separate from db4o on purpose: the jobs are an integration concern and a
	/// plain file is easy to inspect, back up or delete.
	/// </summary>
	public class CompatJobStore
	{
		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		readonly string _path;

		static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
		{
			Formatting = Formatting.Indented,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			Converters = new List<JsonConverter> { new StringEnumConverter() }
		};

		public CompatJobStore(string aPath)
		{
			_path = aPath;
		}

		public string Path
		{
			get { return _path; }
		}

		public List<CompatJob> Load()
		{
			foreach (string path in new[] { _path, _path + ".bak" })
			{
				if (!File.Exists(path))
				{
					continue;
				}
				try
				{
					var jobs = JsonConvert.DeserializeObject<List<CompatJob>>(File.ReadAllText(path, Encoding.UTF8), SerializerSettings);
					if (jobs != null)
					{
						if (path != _path)
						{
							Log.Warn("Load() restored compatibility jobs from " + path);
						}
						jobs.RemoveAll(job => job == null || string.IsNullOrEmpty(job.Id));
						return jobs;
					}
				}
				catch (Exception ex)
				{
					Log.Error("Load() can not read compatibility jobs from " + path, ex);
					PreserveBrokenFile(path);
				}
			}
			return new List<CompatJob>();
		}

		/// <summary>
		/// Writes to a temporary file first and swaps it in, so an interrupted write
		/// never leaves a truncated state file behind.
		/// </summary>
		public void Save(IEnumerable<CompatJob> aJobs)
		{
			string json = JsonConvert.SerializeObject(new List<CompatJob>(aJobs), SerializerSettings);
			string tmpPath = _path + ".tmp";

			string directory = System.IO.Path.GetDirectoryName(_path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			byte[] bytes = new UTF8Encoding(false).GetBytes(json);
			using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				stream.Write(bytes, 0, bytes.Length);
				stream.Flush(true);
			}

			if (File.Exists(_path))
			{
				File.Replace(tmpPath, _path, _path + ".bak");
			}
			else
			{
				File.Move(tmpPath, _path);
			}
		}

		void PreserveBrokenFile(string aPath)
		{
			try
			{
				File.Move(aPath, aPath + ".broken-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
			}
			catch (Exception ex)
			{
				Log.Error("PreserveBrokenFile() can not move " + aPath, ex);
			}
		}
	}
}
