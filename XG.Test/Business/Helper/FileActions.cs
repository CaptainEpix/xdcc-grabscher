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

using NUnit.Framework;

namespace XG.Test.Business.Helper
{
	[TestFixture]
	public class FileActions
	{
		[Test]
		public void RemoveFile()
		{
			var file = new XG.Model.Domain.File("test", 1000);

			var files = new XG.Model.Domain.Files();
			files.Add(file);

			XG.Business.Helper.FileActions.Files = files;

			files.Remove(file);
		}

		[Test]
		public void FinishFileIntoResolvedFolderTest()
		{
			string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xg-finish-test-" + System.Guid.NewGuid().ToString("N"));
			var settings = XG.Config.Properties.Settings.Default;
			string oldTemp = settings.TempPath, oldReady = settings.ReadyPath;
			var oldFiles = XG.Business.Helper.FileActions.Files;
			var oldServers = XG.Business.Helper.FileActions.Servers;
			string finishedPath = null;
			System.EventHandler<XG.Extensions.EventArgs<XG.Model.Domain.File, XG.Model.Domain.Packet[], string, bool>> finished = (s, e) => finishedPath = e.Value3;
			try
			{
				settings.TempPath = System.IO.Path.Combine(dir, "tmp") + System.IO.Path.DirectorySeparatorChar;
				settings.ReadyPath = System.IO.Path.Combine(dir, "dl") + System.IO.Path.DirectorySeparatorChar;
				System.IO.Directory.CreateDirectory(settings.TempPath);
				System.IO.Directory.CreateDirectory(settings.ReadyPath);

				var file = new XG.Model.Domain.File("Some.Show.S01E01.mkv", 10);
				file.CurrentSize = 10;
				System.IO.File.WriteAllBytes(settings.TempPath + file.TmpName, new byte[10]);
				var files = new XG.Model.Domain.Files();
				files.Add(file);
				XG.Business.Helper.FileActions.Files = files;
				XG.Business.Helper.FileActions.Servers = new XG.Model.Domain.Servers();
				XG.Business.Helper.FileActions.ReadyFolderResolver = (f, p) => settings.ReadyPath + "tv";
				XG.Business.Helper.FileActions.OnFileFinished += finished;

				XG.Business.Helper.FileActions.FinishFile(file);

				string expected = settings.ReadyPath + "tv" + System.IO.Path.DirectorySeparatorChar + "Some.Show.S01E01.mkv";
				Assert.AreEqual(expected, finishedPath);
				Assert.IsTrue(System.IO.File.Exists(expected));
			}
			finally
			{
				XG.Business.Helper.FileActions.OnFileFinished -= finished;
				XG.Business.Helper.FileActions.ReadyFolderResolver = null;
				XG.Business.Helper.FileActions.Files = oldFiles;
				XG.Business.Helper.FileActions.Servers = oldServers;
				settings.TempPath = oldTemp;
				settings.ReadyPath = oldReady;
				System.IO.Directory.Delete(dir, true);
			}
		}
	}
}
