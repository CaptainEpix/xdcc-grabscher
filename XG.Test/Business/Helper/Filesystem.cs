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

using System.IO;
using NUnit.Framework;
using XG.Business.Helper;

namespace XG.Test.Business.Helper
{
	[TestFixture]
	public class Filesystem
	{
		[Test]
		public void MoveFileTest()
		{
			const string fileNameOld = "test1.txt";
			const string fileNameNew = "test2.txt";

			File.Delete(fileNameOld);
			File.Delete(fileNameNew);

			bool result = FileSystem.MoveFile(fileNameOld, fileNameNew);
			Assert.AreEqual(false, result);

			File.Create(fileNameOld).Close();

			result = FileSystem.MoveFile(fileNameOld, fileNameNew);
			Assert.AreEqual(true, result);
			Assert.AreEqual(false, File.Exists(fileNameOld));
			Assert.AreEqual(true, File.Exists(fileNameNew));

			File.Delete(fileNameNew);
		}

		[Test]
		public void FreeFileNameTest()
		{
			string folder = Path.Combine(Path.GetTempPath(), "xg-free-name-test");
			if (Directory.Exists(folder))
			{
				Directory.Delete(folder, true);
			}
			Directory.CreateDirectory(folder);
			try
			{
				string path = Path.Combine(folder, "Show.S01E01.mkv");
				Assert.AreEqual(path, FileSystem.FreeFileName(path));

				File.Create(path).Close();
				Assert.AreEqual(Path.Combine(folder, "Show.S01E01 (1).mkv"), FileSystem.FreeFileName(path));

				File.Create(Path.Combine(folder, "Show.S01E01 (1).mkv")).Close();
				Assert.AreEqual(Path.Combine(folder, "Show.S01E01 (2).mkv"), FileSystem.FreeFileName(path));

				string noExtension = Path.Combine(folder, "pack");
				Directory.CreateDirectory(noExtension);
				Assert.AreEqual(Path.Combine(folder, "pack (1)"), FileSystem.FreeFileName(noExtension));
			}
			finally
			{
				Directory.Delete(folder, true);
			}
		}
	}
}
