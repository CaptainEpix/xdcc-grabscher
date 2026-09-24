//
//  NewznabApi.cs
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
using System.Linq;
using System.Xml;
using NUnit.Framework;
using XG.Plugin.Webserver.Compat.Newznab;
using XG.Plugin.Webserver.Compat.Nzb;

namespace XG.Test.Plugin.Webserver.Compat
{
	[TestFixture]
	public class NewznabApi
	{
		CompatTestData _data;
		NewznabHandler _handler;

		[SetUp]
		public void SetUp()
		{
			_data = new CompatTestData();
			_handler = new NewznabHandler(CompatTestData.IsValidKey);
		}

		NewznabResponse Handle(params string[] aPairs)
		{
			return _handler.Handle(CompatTestData.Query(aPairs), CompatTestData.ApiUrl);
		}

		XmlDocument Search(params string[] aPairs)
		{
			var pairs = new List<string>(aPairs) { "apikey", CompatTestData.ApiKey };
			var response = Handle(pairs.ToArray());
			Assert.AreEqual(NewznabXml.ContentType, response.ContentType);
			var doc = CompatTestData.Xml(response.Body);
			Assert.IsNull(doc.SelectSingleNode("/error"), "unexpected error: " + doc.OuterXml);
			return doc;
		}

		static List<string> Titles(XmlDocument aDoc)
		{
			return aDoc.SelectNodes("/rss/channel/item/title").Cast<XmlNode>().Select(n => n.InnerText).ToList();
		}

		static List<string> Categories(XmlDocument aDoc)
		{
			return aDoc.SelectNodes("/rss/channel/item/newznab:attr[@name='category']/@value", CompatTestData.Namespaces(aDoc)).Cast<XmlNode>().Select(n => n.Value).Distinct().ToList();
		}

		static void AssertError(NewznabResponse aResponse, int aCode)
		{
			var error = CompatTestData.Xml(aResponse.Body).SelectSingleNode("/error");
			Assert.IsNotNull(error, "expected an error");
			Assert.AreEqual(aCode.ToString(), error.Attributes["code"].Value);
			Assert.IsNotNull(error.Attributes["description"]);
		}

		[Test]
		public void CapabilitiesTest()
		{
			var doc = Search("t", "caps");
			Assert.AreEqual("yes", doc.SelectSingleNode("/caps/searching/search/@available").Value);
			Assert.AreEqual("q", doc.SelectSingleNode("/caps/searching/search/@supportedParams").Value);
			Assert.AreEqual("q,season,ep", doc.SelectSingleNode("/caps/searching/tv-search/@supportedParams").Value);
			Assert.AreEqual("q", doc.SelectSingleNode("/caps/searching/movie-search/@supportedParams").Value);
			Assert.AreEqual("no", doc.SelectSingleNode("/caps/searching/audio-search/@available").Value);
			Assert.AreEqual("100", doc.SelectSingleNode("/caps/limits/@max").Value);

			var categories = doc.SelectNodes("/caps/categories/category/@id").Cast<XmlNode>().Select(n => n.Value).ToList();
			CollectionAssert.AreEquivalent(new[] { "2000", "5000", "8000" }, categories);
		}

		[Test]
		public void AuthenticationTest()
		{
			AssertError(Handle("t", "caps"), NewznabException.IncorrectCredentials);
			AssertError(Handle("t", "search", "q", "some", "apikey", "wrong"), NewznabException.IncorrectCredentials);
			AssertError(Handle("t", "get", "id", _data.Movie.Guid.ToString(), "apikey", Guid.NewGuid().ToString()), NewznabException.IncorrectCredentials);
		}

		[Test]
		public void ProtocolErrorTest()
		{
			AssertError(Handle("apikey", CompatTestData.ApiKey), NewznabException.MissingParameter);
			AssertError(Handle("t", "register", "apikey", CompatTestData.ApiKey), NewznabException.NoSuchFunction);
			AssertError(Handle("t", "search", "q", "some", "limit", "many", "apikey", CompatTestData.ApiKey), NewznabException.IncorrectParameter);
			AssertError(Handle("t", "tvsearch", "q", "some", "season", "x", "apikey", CompatTestData.ApiKey), NewznabException.IncorrectParameter);
		}

		[Test]
		public void GenericSearchTest()
		{
			var doc = Search("t", "search", "q", "Some Movie 2024");
			CollectionAssert.AreEqual(new[] { _data.Movie.Name }, Titles(doc));
			// no requested category: neutral fallback instead of a guess
			CollectionAssert.AreEqual(new[] { "8000" }, Categories(doc));

			var item = doc.SelectSingleNode("/rss/channel/item");
			Assert.AreEqual(_data.Movie.Guid.ToString(), item.SelectSingleNode("guid").InnerText);
			Assert.AreEqual("false", item.SelectSingleNode("guid/@isPermaLink").Value);
			Assert.AreEqual("1000", item.SelectSingleNode("enclosure/@length").Value);
			Assert.AreEqual(SyntheticNzb.ContentType, item.SelectSingleNode("enclosure/@type").Value);
			Assert.AreEqual("1000", item.SelectSingleNode("newznab:attr[@name='size']/@value", CompatTestData.Namespaces(doc)).Value);
			Assert.AreEqual("Thu, 01 Jan 2026 12:01:00 +0000", item.SelectSingleNode("pubDate").InnerText);

			string link = item.SelectSingleNode("link").InnerText;
			Assert.AreEqual(link, item.SelectSingleNode("enclosure/@url").Value);
			StringAssert.StartsWith(CompatTestData.ApiUrl + "?t=get&id=" + _data.Movie.Guid, link);
			StringAssert.Contains("&v=" + NewznabHandler.Fingerprint(_data.Movie), link);
			StringAssert.EndsWith("&apikey=" + CompatTestData.ApiKey, link);
		}

		[Test]
		public void CategoryFromRequestTest()
		{
			CollectionAssert.AreEqual(new[] { "2000" }, Categories(Search("t", "search", "q", "some movie", "cat", "2000,2040")));
			CollectionAssert.AreEqual(new[] { "5000" }, Categories(Search("t", "search", "q", "some movie", "cat", "5030,5040")));
			CollectionAssert.AreEqual(new[] { "2000", "5000" }, Categories(Search("t", "search", "q", "some movie", "cat", "2000,5000")));
			CollectionAssert.AreEqual(new[] { "8000" }, Categories(Search("t", "search", "q", "some movie", "cat", "3000")));
			CollectionAssert.AreEqual(new[] { "2000" }, Categories(Search("t", "movie", "q", "some movie", "cat", "5000")));
			CollectionAssert.AreEqual(new[] { "5000" }, Categories(Search("t", "tvsearch", "q", "some show")));
		}

		[Test]
		public void MovieSearchTest()
		{
			var doc = Search("t", "movie", "q", "Some Movie 2024");
			CollectionAssert.AreEqual(new[] { _data.Movie.Name }, Titles(doc));
		}

		[Test]
		public void TvSearchEpisodeTest()
		{
			var doc = Search("t", "tvsearch", "q", "Some Show", "season", "2", "ep", "5");
			CollectionAssert.AreEqual(new[] { _data.Episode205.Name }, Titles(doc));
		}

		[Test]
		public void TvSearchSeasonTest()
		{
			var doc = Search("t", "tvsearch", "q", "Some Show", "season", "2");
			CollectionAssert.AreEquivalent(new[] { _data.Episode205.Name, _data.Episode206.Name }, Titles(doc));
		}

		[Test]
		public void TvSearchDailyTest()
		{
			var doc = Search("t", "tvsearch", "q", "Daily Show", "season", "2024", "ep", "05/12");
			CollectionAssert.AreEqual(new[] { _data.Daily.Name }, Titles(doc));
		}

		[Test]
		public void QueryNormalizationTest()
		{
			// punctuation and separators are treated like the indexed packet names
			CollectionAssert.AreEqual(new[] { _data.Episode205.Name }, Titles(Search("t", "search", "q", "Some-Show: s02e05!")));
			CollectionAssert.AreEqual(new[] { _data.Special.Name }, Titles(Search("t", "search", "q", "tom jerry special")));
		}

		[Test]
		public void ExclusionTest()
		{
			var doc = Search("t", "search", "q", "some show -s02e05");
			CollectionAssert.AreEquivalent(new[] { _data.Episode206.Name, _data.Episode301.Name }, Titles(doc));
		}

		[Test]
		public void RecentTest()
		{
			// no query: newest packets of online bots, used by the Prowlarr test and RSS sync
			var doc = Search("t", "search");
			var titles = Titles(doc);
			Assert.AreEqual(6, titles.Count);
			Assert.AreEqual(_data.Special.Name, titles.First());
			Assert.AreEqual(_data.Movie.Name, titles.Last());
			Assert.AreEqual("6", doc.SelectSingleNode("/rss/channel/newznab:response/@total", CompatTestData.Namespaces(doc)).Value);

			CollectionAssert.AreEqual(new[] { "5000" }, Categories(Search("t", "tvsearch", "cat", "5000")));
		}

		[Test]
		public void OfflineBotTest()
		{
			CollectionAssert.AreEqual(new[] { _data.Movie.Name }, Titles(Search("t", "search", "q", "some movie")));
			CollectionAssert.AreEquivalent(new[] { _data.Movie.Name, _data.OfflineMovie.Name }, Titles(Search("t", "search", "q", "some movie", "offline", "1")));
		}

		[Test]
		public void PaginationTest()
		{
			var doc = Search("t", "search", "limit", "2", "offset", "1");
			var titles = Titles(doc);
			CollectionAssert.AreEqual(new[] { _data.Daily.Name, _data.Episode301.Name }, titles);
			var response = doc.SelectSingleNode("/rss/channel/newznab:response", CompatTestData.Namespaces(doc));
			Assert.AreEqual("1", response.Attributes["offset"].Value);
			Assert.AreEqual("6", response.Attributes["total"].Value);

			doc = Search("t", "search", "limit", "2", "offset", "6");
			Assert.AreEqual(0, Titles(doc).Count);

			// limits above the advertised maximum are capped
			doc = Search("t", "search", "limit", "5000");
			Assert.AreEqual(6, Titles(doc).Count);
		}

		[Test]
		public void XmlEscapingTest()
		{
			var doc = Search("t", "search", "q", "tom");
			CollectionAssert.AreEqual(new[] { "Tom & Jerry <Special> \"Quoted\".mkv" }, Titles(doc));
		}

		[Test]
		public void GetTest()
		{
			var response = Handle("t", "get", "id", _data.Episode205.Guid.ToString(), "v", NewznabHandler.Fingerprint(_data.Episode205), "apikey", CompatTestData.ApiKey);
			Assert.AreEqual(SyntheticNzb.ContentType, response.ContentType);
			StringAssert.EndsWith(".nzb", response.FileName);

			// valid xml with a file entry, as the *Arr nzb validation requires
			var doc = CompatTestData.Xml(response.Body);
			var manager = new XmlNamespaceManager(doc.NameTable);
			manager.AddNamespace("nzb", SyntheticNzb.Namespace);
			Assert.IsNotNull(doc.SelectSingleNode("/nzb:nzb/nzb:file", manager));

			var nzb = SyntheticNzb.Parse(response.Body);
			Assert.AreEqual(_data.Episode205.Guid, nzb.PacketGuid);
			Assert.AreEqual(_data.Episode205.Name, nzb.Title);
			Assert.AreEqual(2000, nzb.Size);
		}

		[Test]
		public void GetErrorTest()
		{
			AssertError(Handle("t", "get", "apikey", CompatTestData.ApiKey), NewznabException.MissingParameter);
			AssertError(Handle("t", "get", "id", "../../etc/passwd", "apikey", CompatTestData.ApiKey), NewznabException.IncorrectParameter);
			AssertError(Handle("t", "get", "id", Guid.NewGuid().ToString(), "apikey", CompatTestData.ApiKey), NewznabException.NoSuchItem);
		}

		[Test]
		public void GetChangedPacketTest()
		{
			string fingerprint = NewznabHandler.Fingerprint(_data.Episode205);
			// the bot now offers a different file under the same pack number
			_data.Episode205.Name = "Other.Show.S01E01.mkv";
			AssertError(Handle("t", "get", "id", _data.Episode205.Guid.ToString(), "v", fingerprint, "apikey", CompatTestData.ApiKey), NewznabException.NoSuchItem);
		}
	}
}
