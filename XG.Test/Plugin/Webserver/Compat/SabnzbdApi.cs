//
//  SabnzbdApi.cs
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
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using XG.Model.Domain;
using XG.Plugin.Webserver.Compat.Newznab;
using XG.Plugin.Webserver.Compat.Nzb;
using XG.Plugin.Webserver.Compat.Sabnzbd;

namespace XG.Test.Plugin.Webserver.Compat
{
	[TestFixture]
	public class SabnzbdApi
	{
		CompatTestData _data;
		string _dir;
		string _readyPath;
		CompatJobStore _store;
		CompatJobTracker _tracker;
		SabnzbdHandler _handler;

		[SetUp]
		public void SetUp()
		{
			_data = new CompatTestData();
			_dir = Path.Combine(Path.GetTempPath(), "xg-compat-test-" + Guid.NewGuid().ToString("N"));
			_readyPath = Path.Combine(_dir, "dl") + Path.DirectorySeparatorChar;
			Directory.CreateDirectory(_readyPath);
			_store = new CompatJobStore(Path.Combine(_dir, "arr-jobs.json"));
			StartTracker();
		}

		[TearDown]
		public void TearDown()
		{
			Directory.Delete(_dir, true);
		}

		void StartTracker()
		{
			_tracker = new CompatJobTracker(_store, XG.Plugin.Webserver.Search.Packets.GetPacket, () => _readyPath);
			_tracker.Start();
			_handler = new SabnzbdHandler(_tracker, CompatTestData.IsValidKey, () => _readyPath);
		}

		JObject Call(byte[] aUpload, params string[] aPairs)
		{
			var pairs = aPairs.ToList();
			pairs.Add("apikey");
			pairs.Add(CompatTestData.ApiKey);
			pairs.Add("output");
			pairs.Add("json");
			return JObject.Parse(_handler.Handle(CompatTestData.Query(pairs.ToArray()), aUpload));
		}

		JObject Call(params string[] aPairs)
		{
			return Call(null, aPairs);
		}

		static void AssertError(JObject aResponse, string aMessagePart = null)
		{
			Assert.AreEqual(false, (bool)aResponse["status"], aResponse.ToString());
			if (aMessagePart != null)
			{
				StringAssert.Contains(aMessagePart, (string)aResponse["error"]);
			}
		}

		static byte[] NzbFor(Packet aPacket)
		{
			return new SyntheticNzb(aPacket.Guid, NewznabHandler.PacketTitle(aPacket), NewznabHandler.PacketSize(aPacket)).ToBytes();
		}

		string AddFile(Packet aPacket, string aCategory = "tv")
		{
			var response = Call(NzbFor(aPacket), "mode", "addfile", "cat", aCategory, "priority", "-100");
			Assert.AreEqual(true, (bool)response["status"], response.ToString());
			return (string)response["nzo_ids"][0];
		}

		/// <summary>
		/// What XG does when a download completed: FinishFile announces, disables and moves.
		/// </summary>
		string FinishDownload(Packet aPacket, string aFileName, int aLength)
		{
			var file = new XG.Model.Domain.File(aFileName, aLength);
			file.CurrentSize = aLength;
			file.Packet = aPacket;
			file.Packet = null;

			_tracker.FileFinishing(file, new[] { aPacket });
			aPacket.Enabled = false;
			_tracker.PacketEnabledChanged(aPacket);

			string path = _readyPath + file.Name;
			System.IO.File.WriteAllBytes(path, new byte[aLength]);
			_tracker.FileFinished(file, new[] { aPacket }, path, true);
			return path;
		}

		[Test]
		public void VersionTest()
		{
			var response = JObject.Parse(_handler.Handle(CompatTestData.Query("mode", "version"), null));
			Assert.AreEqual(SabnzbdHandler.CompatibilityVersion, (string)response["version"]);
		}

		[Test]
		public void AuthenticationTest()
		{
			AssertError(JObject.Parse(_handler.Handle(CompatTestData.Query("mode", "get_config"), null)), "API Key Required");
			AssertError(JObject.Parse(_handler.Handle(CompatTestData.Query("mode", "get_config", "apikey", "wrong"), null)), "API Key Incorrect");
			AssertError(JObject.Parse(_handler.Handle(CompatTestData.Query("mode", "queue", "apikey", Guid.NewGuid().ToString()), null)), "API Key Incorrect");
		}

		[Test]
		public void GetConfigTest()
		{
			var config = Call("mode", "get_config")["config"];
			var names = config["categories"].Select(c => (string)c["name"]).ToList();
			CollectionAssert.IsSubsetOf(new[] { "*", "tv", "movies", "xg", "prowlarr" }, names);
			Assert.IsTrue(config["categories"].All(c => (string)c["dir"] == ""));

			var misc = config["misc"];
			Assert.AreEqual(_readyPath.TrimEnd(Path.DirectorySeparatorChar), (string)misc["complete_dir"]);
			Assert.AreEqual(false, (bool)misc["enable_tv_sorting"]);
			Assert.AreEqual(false, (bool)misc["enable_movie_sorting"]);
			Assert.AreEqual(false, (bool)misc["enable_date_sorting"]);
			Assert.AreEqual(false, (bool)misc["pre_check"]);
			Assert.AreEqual(0, config["sorters"].Count());
		}

		[Test]
		public void FullStatusTest()
		{
			Assert.AreEqual(_readyPath.TrimEnd(Path.DirectorySeparatorChar), (string)Call("mode", "fullstatus", "skip_dashboard", "1")["status"]["completedir"]);
		}

		[Test]
		public void UnsupportedModeTest()
		{
			AssertError(Call("mode", "shutdown"));
			AssertError(Call("mode", "pause"));
			AssertError(Call("mode", "queue", "name", "pause"));
		}

		[Test]
		public void AddFileTest()
		{
			Assert.IsFalse(_data.Episode205.Enabled);
			string id = AddFile(_data.Episode205);
			StringAssert.StartsWith(CompatJob.IdPrefix, id);
			Assert.IsTrue(_data.Episode205.Enabled, "the packet download must be started");

			// the same packet is not tracked twice
			Assert.AreEqual(id, AddFile(_data.Episode205));
			Assert.AreEqual(1, _tracker.Queue(null).Count);
		}

		[Test]
		public void AddFileRejectsTest()
		{
			AssertError(Call("mode", "addfile", "cat", "tv"), "No NZB");
			AssertError(Call(System.Text.Encoding.UTF8.GetBytes("<nzb><broken"), "mode", "addfile", "cat", "tv"), "Not an XG NZB");
			AssertError(Call(new SyntheticNzb(Guid.NewGuid(), "x", 1).ToBytes(), "mode", "addfile", "cat", "tv"), "Unknown XG packet");

			// the bot reused the pack number for another file
			var nzb = NzbFor(_data.Episode206);
			_data.Episode206.Name = "Other.File.mkv";
			AssertError(Call(nzb, "mode", "addfile", "cat", "tv"), "changed");
			Assert.IsFalse(_data.Episode206.Enabled);
			Assert.AreEqual(0, _tracker.Queue(null).Count);
		}

		[Test]
		public void AddUrlTest()
		{
			string url = CompatTestData.ApiUrl + "?t=get&id=" + _data.Movie.Guid + "&v=" + NewznabHandler.Fingerprint(_data.Movie) + "&apikey=" + CompatTestData.ApiKey;
			var response = Call("mode", "addurl", "name", url, "cat", "movies");
			Assert.AreEqual(true, (bool)response["status"], response.ToString());
			Assert.IsTrue(_data.Movie.Enabled);

			AssertError(Call("mode", "addurl", "name", "http://evil.test/download?id=" + _data.Movie.Guid, "cat", "movies"), "Only XG");
			AssertError(Call("mode", "addurl", "name", "not a url", "cat", "movies"));
		}

		[Test]
		public void QueueTest()
		{
			string tvId = AddFile(_data.Episode205, "tv");
			string movieId = AddFile(_data.Movie, "movies");

			var queue = Call("mode", "queue", "start", "0", "limit", "0", "category", "tv")["queue"];
			Assert.AreEqual(false, (bool)queue["paused"]);
			var slots = queue["slots"].ToList();
			Assert.AreEqual(1, slots.Count);

			var slot = slots[0];
			Assert.AreEqual(tvId, (string)slot["nzo_id"]);
			Assert.AreEqual(_data.Episode205.Name, (string)slot["filename"]);
			Assert.AreEqual("tv", (string)slot["cat"]);
			Assert.AreEqual("Queued", (string)slot["status"]);
			Assert.AreEqual("0:00:00", (string)slot["timeleft"]);
			Assert.AreEqual((double)(2000m / 1048576m), double.Parse((string)slot["mb"], System.Globalization.CultureInfo.InvariantCulture), 0.01);
			Assert.AreEqual((string)slot["mb"], (string)slot["mbleft"]);
			Assert.AreEqual("Normal", (string)slot["priority"]);

			slots = Call("mode", "queue", "category", "movies")["queue"]["slots"].ToList();
			Assert.AreEqual(movieId, (string)slots.Single()["nzo_id"]);

			Assert.AreEqual(2, Call("mode", "queue")["queue"]["slots"].Count());
			Assert.AreEqual(1, Call("mode", "queue", "start", "1", "limit", "1")["queue"]["slots"].Count());
		}

		[Test]
		public void QueueDownloadingTest()
		{
			AddFile(_data.Episode205);
			var file = new XG.Model.Domain.File(_data.Episode205.Name, 2000);
			file.CurrentSize = 500;
			file.Speed = 100;
			_data.Episode205.File = file;
			_data.Episode205.Connected = true;

			var slot = Call("mode", "queue")["queue"]["slots"][0];
			Assert.AreEqual("Downloading", (string)slot["status"]);
			Assert.AreEqual("0:00:15", (string)slot["timeleft"]);
			Assert.AreEqual(25, (int)slot["percentage"]);
		}

		[Test]
		public void CompletedHistoryTest()
		{
			string id = AddFile(_data.Episode205);
			string path = FinishDownload(_data.Episode205, _data.Episode205.Name, 2000);

			Assert.AreEqual(0, Call("mode", "queue")["queue"]["slots"].Count());

			var history = Call("mode", "history", "start", "0", "limit", "60", "category", "tv")["history"];
			var slot = history["slots"].Single();
			Assert.AreEqual(id, (string)slot["nzo_id"]);
			Assert.AreEqual("Completed", (string)slot["status"]);
			Assert.AreEqual(path, (string)slot["storage"]);
			Assert.AreEqual(2000, (long)slot["bytes"]);
			Assert.AreEqual(_data.Episode205.Name, (string)slot["name"]);
			Assert.AreEqual("tv", (string)slot["category"]);
			Assert.AreEqual(0, Call("mode", "history", "category", "movies")["history"]["slots"].Count());
		}

		[Test]
		public void FinishingIsShownAsMovingTest()
		{
			AddFile(_data.Episode205);
			var file = new XG.Model.Domain.File(_data.Episode205.Name, 2000);
			_tracker.FileFinishing(file, new[] { _data.Episode205 });
			_data.Episode205.Enabled = false;
			_tracker.PacketEnabledChanged(_data.Episode205);

			// disabling a finishing packet is part of completing, not a failure
			var slot = Call("mode", "queue")["queue"]["slots"].Single();
			Assert.AreEqual("Moving", (string)slot["status"]);
			Assert.AreEqual(0, Call("mode", "history")["history"]["slots"].Count());
		}

		[Test]
		public void FailedHistoryAndRetryTest()
		{
			string id = AddFile(_data.Episode205);
			_data.OnlineBot.LastMessage = "XDCC SEND denied, you must be on a known channel to request a pack";
			_data.Episode205.Enabled = false;
			_tracker.PacketEnabledChanged(_data.Episode205);

			var slot = Call("mode", "history")["history"]["slots"].Single();
			Assert.AreEqual(id, (string)slot["nzo_id"]);
			Assert.AreEqual("Failed", (string)slot["status"]);
			StringAssert.Contains("XDCC SEND denied", (string)slot["fail_message"]);
			Assert.AreEqual("", (string)slot["storage"]);

			var retry = Call("mode", "retry", "value", id);
			Assert.AreEqual(true, (bool)retry["status"]);
			Assert.AreEqual(id, (string)retry["nzo_id"]);
			Assert.IsTrue(_data.Episode205.Enabled);
			Assert.AreEqual(id, (string)Call("mode", "queue")["queue"]["slots"].Single()["nzo_id"]);
			Assert.AreEqual(0, Call("mode", "history")["history"]["slots"].Count());

			AssertError(Call("mode", "retry", "value", id), "Only failed");
			AssertError(Call("mode", "retry", "value", "XG_unknown"), "Unknown job");
		}

		[Test]
		public void SilentBotFailsTest()
		{
			string id = AddFile(_data.Episode205);
			var added = DateTime.UtcNow;

			Assert.AreEqual(0, _tracker.CheckSilentBots(added.AddMinutes(10)));
			Assert.AreEqual(1, Call("mode", "queue")["queue"]["slots"].Count());

			Assert.AreEqual(1, _tracker.CheckSilentBots(added.AddMinutes(16)));
			var slot = Call("mode", "history")["history"]["slots"].Single();
			Assert.AreEqual(id, (string)slot["nzo_id"]);
			Assert.AreEqual("Failed", (string)slot["status"]);
			StringAssert.Contains("did not answer", (string)slot["fail_message"]);
			Assert.IsFalse(_data.Episode205.Enabled, "XG stops asking the silent bot");
		}

		[Test]
		public void AnsweringBotIsWaitedForTest()
		{
			AddFile(_data.Episode205);
			// e.g. a queue position; the bot may take hours then
			_data.OnlineBot.LastMessage = "** All Slots Full, Added you to the main queue in position 7";

			Assert.AreEqual(0, _tracker.CheckSilentBots(DateTime.UtcNow.AddHours(5)));
			Assert.AreEqual(1, Call("mode", "queue")["queue"]["slots"].Count());
		}

		[Test]
		public void BusyBotIsNotSilentTest()
		{
			AddFile(_data.Episode205);
			var start = DateTime.UtcNow;
			// the bot sends another packet first, XG asks for this one afterwards
			_data.Episode206.Connected = true;
			Assert.AreEqual(0, _tracker.CheckSilentBots(start.AddMinutes(30)));
			_data.Episode206.Connected = false;

			Assert.AreEqual(0, _tracker.CheckSilentBots(start.AddMinutes(40)), "the silence starts after the other transfer");
			Assert.AreEqual(1, _tracker.CheckSilentBots(start.AddMinutes(46)));
		}

		[Test]
		public void PassiveOfferFailMessageTest()
		{
			AddFile(_data.Episode205);
			_data.OnlineBot.PassiveDccTime = DateTime.Now;
			_data.Episode205.Enabled = false;
			_tracker.PacketEnabledChanged(_data.Episode205);

			var slot = Call("mode", "history")["history"]["slots"].Single();
			StringAssert.Contains("passive DCC", (string)slot["fail_message"]);
			StringAssert.Contains("XG_PASSIVE_DCC_PORTS", (string)slot["fail_message"]);
		}

		[Test]
		public void RemovedPacketFailsTest()
		{
			AddFile(_data.Episode205);
			_tracker.PacketRemoved(_data.Episode205);
			Assert.AreEqual("Failed", (string)Call("mode", "history")["history"]["slots"].Single()["status"]);
		}

		[Test]
		public void QueueDeleteTest()
		{
			string id = AddFile(_data.Episode205);
			var response = Call("mode", "queue", "name", "delete", "value", id, "del_files", "1");
			Assert.AreEqual(true, (bool)response["status"]);
			Assert.IsFalse(_data.Episode205.Enabled, "the XG download must be stopped");
			Assert.AreEqual(0, Call("mode", "queue")["queue"]["slots"].Count());
			// a cancelled job is gone, not failed
			Assert.AreEqual(0, Call("mode", "history")["history"]["slots"].Count());

			AssertError(Call("mode", "queue", "name", "delete"), "Missing job id");
		}

		[Test]
		public void HistoryDeleteKeepsFileTest()
		{
			string id = AddFile(_data.Episode205);
			string path = FinishDownload(_data.Episode205, _data.Episode205.Name, 2000);

			var response = Call("mode", "history", "name", "delete", "value", id, "del_files", "0", "archive", "1");
			Assert.AreEqual(true, (bool)response["status"]);
			Assert.AreEqual(0, Call("mode", "history")["history"]["slots"].Count());
			Assert.IsTrue(System.IO.File.Exists(path), "an imported file must not be deleted");
		}

		[Test]
		public void HistoryDeleteWithFilesTest()
		{
			string id = AddFile(_data.Episode205);
			string path = FinishDownload(_data.Episode205, _data.Episode205.Name, 2000);
			string other = _readyPath + "unrelated.mkv";
			System.IO.File.WriteAllBytes(other, new byte[10]);

			Call("mode", "history", "name", "delete", "value", id, "del_files", "1");
			Assert.IsFalse(System.IO.File.Exists(path));
			Assert.IsTrue(System.IO.File.Exists(other));
		}

		[Test]
		public void HistoryDeleteNeverLeavesReadyFolderTest()
		{
			string id = AddFile(_data.Episode205);
			FinishDownload(_data.Episode205, _data.Episode205.Name, 2000);

			// tamper with the stored path; a delete must stay inside the ready folder
			string outside = Path.Combine(_dir, "outside.mkv");
			System.IO.File.WriteAllBytes(outside, new byte[2000]);
			var json = System.IO.File.ReadAllText(_store.Path).Replace(_readyPath.Replace("\\", "\\\\") + _data.Episode205.Name, outside.Replace("\\", "\\\\"));
			System.IO.File.WriteAllText(_store.Path, json);
			StartTracker();

			Call("mode", "history", "name", "delete", "value", id, "del_files", "1");
			Assert.IsTrue(System.IO.File.Exists(outside));
		}

		[Test]
		public void PersistenceTest()
		{
			string completedId = AddFile(_data.Episode205);
			string path = FinishDownload(_data.Episode205, _data.Episode205.Name, 2000);
			string activeId = AddFile(_data.Episode206, "tv");
			string movieId = AddFile(_data.Movie, "movies");

			// restart
			StartTracker();

			Assert.AreEqual(completedId, (string)Call("mode", "history")["history"]["slots"].Single()["nzo_id"]);
			Assert.AreEqual(path, (string)Call("mode", "history")["history"]["slots"].Single()["storage"]);
			CollectionAssert.AreEquivalent(new[] { activeId, movieId }, Call("mode", "queue")["queue"]["slots"].Select(s => (string)s["nzo_id"]));
		}

		[Test]
		public void RestartReconcileTest()
		{
			string finishedId = AddFile(_data.Episode205);
			string stoppedId = AddFile(_data.Episode206);
			string missingId = AddFile(_data.Episode301);

			// while XG was down: one download was finished by the startup recovery,
			// one was stopped and one packet vanished
			System.IO.File.WriteAllBytes(_readyPath + _data.Episode205.Name, new byte[2000]);
			_data.Episode205.Enabled = false;
			_data.Episode206.Enabled = false;
			_data.OnlineBot.RemovePacket(_data.Episode301);

			StartTracker();

			var history = Call("mode", "history")["history"]["slots"].ToDictionary(s => (string)s["nzo_id"]);
			Assert.AreEqual("Completed", (string)history[finishedId]["status"]);
			Assert.AreEqual(_readyPath + _data.Episode205.Name, (string)history[finishedId]["storage"]);
			Assert.AreEqual("Failed", (string)history[stoppedId]["status"]);
			Assert.AreEqual("Failed", (string)history[missingId]["status"]);
			Assert.AreEqual(0, Call("mode", "queue")["queue"]["slots"].Count());
		}

		[Test]
		public void BrokenStateFileTest()
		{
			string first = AddFile(_data.Episode205);
			AddFile(_data.Episode206);
			System.IO.File.WriteAllText(_store.Path, "{ this is not json");

			// the backup of the previous write is used
			StartTracker();
			Assert.AreEqual(first, (string)Call("mode", "queue")["queue"]["slots"].Single()["nzo_id"]);
			Assert.IsTrue(Directory.GetFiles(_dir, "arr-jobs.json.broken-*").Any(), "the broken file is kept for inspection");
		}

		[Test]
		public void CategoryFolderSettingTest()
		{
			Assert.IsNull(_tracker.CategoryFolderName("tv"), "off by default");

			_tracker.ConfigureCategoryFolders("tv, Movies");
			Assert.AreEqual("tv", _tracker.CategoryFolderName("tv"));
			Assert.AreEqual("movies", _tracker.CategoryFolderName("movies"));
			Assert.IsNull(_tracker.CategoryFolderName("prowlarr"));

			_tracker.ConfigureCategoryFolders("all");
			Assert.AreEqual("prowlarr", _tracker.CategoryFolderName("prowlarr"));
			Assert.AreEqual("tv-sonarr", _tracker.CategoryFolderName("tv-sonarr"));
			// categories come from the clients, only plain names become folders
			Assert.IsNull(_tracker.CategoryFolderName("*"));
			Assert.IsNull(_tracker.CategoryFolderName(".."));
			Assert.IsNull(_tracker.CategoryFolderName("../etc"));
			Assert.IsNull(_tracker.CategoryFolderName("tv/../../x"));
			Assert.IsNull(_tracker.CategoryFolderName(".hidden"));
			Assert.IsNull(_tracker.CategoryFolderName(""));

			_tracker.ConfigureCategoryFolders("false");
			Assert.IsNull(_tracker.CategoryFolderName("tv"));
		}

		[Test]
		public void CategoryFolderRoutingTest()
		{
			_tracker.ConfigureCategoryFolders("tv");
			AddFile(_data.Episode205, "tv");
			AddFile(_data.Movie, "movies");
			var episode = new XG.Model.Domain.File(_data.Episode205.Name, 2000);
			var movie = new XG.Model.Domain.File(_data.Movie.Name, 1000);
			_tracker.FileFinishing(episode, new[] { _data.Episode205 });
			_tracker.FileFinishing(movie, new[] { _data.Movie });

			Assert.AreEqual(Path.Combine(_readyPath, "tv"), _tracker.ReadyFolder(episode, new[] { _data.Episode205 }));
			Assert.IsNull(_tracker.ReadyFolder(movie, new[] { _data.Movie }), "movies stay in the download folder");
			// a manual download has no job
			Assert.IsNull(_tracker.ReadyFolder(new XG.Model.Domain.File(_data.Daily.Name, 3000), new[] { _data.Daily }));

			var config = Call("mode", "get_config")["config"]["categories"].ToDictionary(c => (string)c["name"]);
			Assert.AreEqual("tv", (string)config["tv"]["dir"]);
			Assert.AreEqual("", (string)config["movies"]["dir"]);
		}

		[Test]
		public void RestartFindsFileInCategoryFolderTest()
		{
			string id = AddFile(_data.Episode205, "tv");
			string folder = Path.Combine(_readyPath, "tv");
			Directory.CreateDirectory(folder);
			System.IO.File.WriteAllBytes(Path.Combine(folder, _data.Episode205.Name), new byte[2000]);
			_data.Episode205.Enabled = false;

			StartTracker();

			var slot = Call("mode", "history")["history"]["slots"].Single();
			Assert.AreEqual(id, (string)slot["nzo_id"]);
			Assert.AreEqual("Completed", (string)slot["status"]);
			Assert.AreEqual(Path.Combine(folder, _data.Episode205.Name), (string)slot["storage"]);
		}

		[Test]
		public void CategoriesFromJobsTest()
		{
			AddFile(_data.Episode205, "anime");
			var names = Call("mode", "get_config")["config"]["categories"].Select(c => (string)c["name"]).ToList();
			CollectionAssert.Contains(names, "anime");
		}
	}
}
