// 
//  Filesystem.cs
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
using System.IO;
using System.Reflection;
using log4net;
using XG.Plugin;

namespace XG.Business.Helper
{
	public class FileSystem : ANotificationSender
	{
		static readonly ILog Log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		public static bool MoveFile(string aNameOld, string aNameNew)
		{
			if (File.Exists(aNameOld))
			{
				try
				{
					File.Move(aNameOld, aNameNew);
					return true;
				}
				catch (Exception ex)
				{
					Log.Fatal("MoveFile('" + aNameOld + "', '" + aNameNew + "') ", ex);
					return false;
				}
			}
			return false;
		}

		/// <summary>
		/// Returns aPath, or "name (n).ext" next to it if aPath is already taken, so a finished download never replaces an existing file.
		/// </summary>
		public static string FreeFileName(string aPath)
		{
			if (!File.Exists(aPath) && !Directory.Exists(aPath))
			{
				return aPath;
			}
			string folder = Path.GetDirectoryName(aPath) ?? "";
			string name = Path.GetFileNameWithoutExtension(aPath);
			string extension = Path.GetExtension(aPath);
			for (int i = 1; ; i++)
			{
				string path = Path.Combine(folder, name + " (" + i + ")" + extension);
				if (!File.Exists(path) && !Directory.Exists(path))
				{
					return path;
				}
			}
		}

		public static bool DeleteFile(string aName)
		{
			if (File.Exists(aName))
			{
				try
				{
					File.Delete(aName);
					return true;
				}
				catch (Exception ex)
				{
					Log.Fatal("DeleteFile(" + aName + ") ", ex);
					return false;
				}
			}
			return false;
		}
	}
}
