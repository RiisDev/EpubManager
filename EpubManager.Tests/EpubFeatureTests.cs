using System.IO.Compression;
using System.Text.RegularExpressions;
using EpubManager.ContentSources;
using EpubManager.Util;

namespace EpubManager.Tests;

public class EpubFeatureTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "epubmanager-tests", Guid.NewGuid().ToString("N"));
	private readonly List<string> _log = [];
	private readonly TimeSpan _originalRetryDelay = EpubManagerClient.RetryBaseDelay;

	public EpubFeatureTests()
	{
		Directory.CreateDirectory(_dir);
		EpubManagerClient.RetryBaseDelay = TimeSpan.FromMilliseconds(10);
	}

	public void Dispose()
	{
		EpubManagerClient.RetryBaseDelay = _originalRetryDelay;
		try { Directory.Delete(_dir, true); } catch { /* best effort */ }
	}

	private string Chapter(string content, string name = "chapter.txt")
	{
		string path = Path.Combine(_dir, name);
		File.WriteAllText(path, content);
		return path;
	}

	private EpubStory Story(string title, params string[] chapterContents)
	{
		Dictionary<string, string> chapters = [];
		for (int i = 0; i < chapterContents.Length; i++)
			chapters[$"Chapter {i + 1}"] = Chapter(chapterContents[i], $"{title}-{i}.txt");
		return new EpubStory(title, "English", "Author", null, [], chapters);
	}

	private static string TempRoot => Path.Combine(Path.GetTempPath(), "EpubManager");
	private static int WorkFolderCount() => Directory.Exists(TempRoot) ? Directory.GetDirectories(TempRoot).Length : 0;

	// ---- packaging

	[Fact]
	public async Task Zip_HasMimetypeFirstStoredAndOthersCompressedInSortedOrder()
	{
		await StoryWriter.CreateEpubAsync(Story("Zip", "<p>text</p>"), _log.Add, _dir);

		using ZipArchive zip = ZipFile.OpenRead(Path.Combine(_dir, "Zip.epub"));
		Assert.Equal("mimetype", zip.Entries[0].FullName);
		Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);

		string[] rest = zip.Entries.Skip(1).Select(e => e.FullName).ToArray();
		Assert.Equal(rest.OrderBy(n => n, StringComparer.Ordinal), rest);
		Assert.DoesNotContain(rest, n => n.Contains('\\'));

		ZipArchiveEntry css = zip.GetEntry("EPUB/styles/style.css")!;
		Assert.True(css.CompressedLength < css.Length, "non-mimetype entries should be compressed");
	}

	[Fact]
	public async Task Packaging_LeavesNoWorkFoldersOrPartialFiles()
	{
		int before = WorkFolderCount();

		await StoryWriter.CreateEpubAsync(Story("Clean", "<p>text</p>"), _log.Add, _dir);

		Assert.Equal(before, WorkFolderCount());
		Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
	}

	[Fact]
	public async Task Failure_LeavesNoWorkFoldersOrBrokenEpub()
	{
		int before = WorkFolderCount();
		EpubStory story = new("Broken", "English", "A", null, [], new Dictionary<string, string> { ["x"] = Path.Combine(_dir, "missing.txt") });

		await Assert.ThrowsAnyAsync<IOException>(() => StoryWriter.CreateEpubAsync(story, _log.Add, _dir));

		Assert.Equal(before, WorkFolderCount());
		Assert.Empty(Directory.GetFiles(_dir, "*.epub*"));
	}

	[Fact]
	public async Task ConcurrentBuilds_DoNotInterfere()
	{
		string[] titles = ["Alpha", "Beta", "Gamma", "Delta"];

		await Task.WhenAll(titles.Select(t => StoryWriter.CreateEpubAsync(Story(t, $"<p>{t} text</p>"), null, _dir)));

		foreach (string title in titles)
		{
			using ZipArchive zip = ZipFile.OpenRead(Path.Combine(_dir, $"{title}.epub"));
			using StreamReader reader = new(zip.GetEntry("EPUB/text/chapter-0001.xhtml")!.Open());
			Assert.Contains($"{title} text", reader.ReadToEnd());
		}
	}

	[Fact]
	public async Task Build_IsCancellable()
	{
		using CancellationTokenSource cts = new();
		cts.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StoryWriter.CreateEpubAsync(Story("Cancelled", "<p>x</p>"), _log.Add, _dir, cancellationToken: cts.Token));

		Assert.Empty(Directory.GetFiles(_dir, "*.epub*"));
	}

	// ---- identity

	[Fact]
	public void StableId_IsAVersion5UuidAndDeterministic()
	{
		Assert.Equal(Guid.Parse("a601b004-7eef-5498-ac42-a019d7bb19d2"), StoryWriter.StableId("literotica:story:accidents"));
		Assert.Equal(Guid.Parse("0a300ee9-f9e4-5697-a51a-efc7fafaba67"), StoryWriter.StableId("http://example.com/"));
		Assert.NotEqual(StoryWriter.StableId("a"), StoryWriter.StableId("b"));
	}

	[Fact]
	public async Task Rebuild_WithSameIdentifier_KeepsBookIdentity()
	{
		EpubStory first = Story("Identity", "<p>x</p>") with { Identifier = StoryWriter.StableId("seed") };
		string Identifier()
		{
			using ZipArchive zip = ZipFile.OpenRead(Path.Combine(_dir, "Identity.epub"));
			using StreamReader reader = new(zip.GetEntry("EPUB/content.opf")!.Open());
			return Regex.Match(reader.ReadToEnd(), "urn:uuid:[0-9a-f-]+").Value;
		}

		await StoryWriter.CreateEpubAsync(first, null, _dir);
		string a = Identifier();
		await StoryWriter.CreateEpubAsync(first, null, _dir);

		Assert.Equal(a, Identifier());
		Assert.Equal($"urn:uuid:{StoryWriter.StableId("seed")}", a);
	}

	// ---- scene breaks

	[Theory]
	[InlineData("***")]
	[InlineData("* * *")]
	[InlineData("---")]
	[InlineData("<p>~~~</p>")]
	[InlineData("<hr>")]
	[InlineData("<HR class=\"x\">")]
	public void SceneBreaks_BecomeOneStyledRule(string line)
	{
		string result = WriterUtil.CleanMarkup($"<p>before</p>\n{line}\n<p>after</p>", "t");

		Assert.Equal("<p>before</p>\n<hr class=\"scene-break\" />\n<p>after</p>", result);
	}

	[Theory]
	[InlineData("a --- b")]
	[InlineData("***bold***")]
	[InlineData("--")]
	public void NonSceneBreakText_IsLeftAlone(string line)
	{
		Assert.DoesNotContain("scene-break", WriterUtil.CleanMarkup(line, "t"));
	}

	[Fact]
	public async Task SceneBreaks_PassEpubCheck()
	{
		EpubCheck.RequireAvailable();
		await StoryWriter.CreateEpubAsync(Story("Breaks", "<p>one</p>\n***\n<p>two</p>\n<hr>\n<p>three</p>"), null, _dir);

		Assert.Empty(EpubCheck.Validate(Path.Combine(_dir, "Breaks.epub")));
	}

	// ---- inline images

	[Fact]
	public async Task InlineImages_AreDownloadedEmbeddedAndRewritten()
	{
		using TestServer server = new((path, hit) => path switch
		{
			"/pic.png" => (200, TestServer.Png),
			"/flaky.png" => hit < 3 ? (503, []) : (200, TestServer.Png),
			_ => (404, [])
		});

		string html = $"<p>A <img src=\"{server.BaseUrl}/pic.png\" width=\"50\" border=\"1\"> picture</p>\n" +
			$"<p><img src=\"{server.BaseUrl}/pic.png\" alt=\"same\"></p>\n" +
			$"<p><img src=\"{server.BaseUrl}/flaky.png\"></p>\n" +
			$"<p><img src=\"{server.BaseUrl}/gone.png\"></p>\n" +
			"<p><img src=\"relative/thing.png\"></p>\n" +
			"<p><img src=\"data:image/png;base64,AAAA\"></p>\n" +
			"<p>end</p>";

		await StoryWriter.CreateEpubAsync(Story("Images", html), _log.Add, _dir);

		string epub = Path.Combine(_dir, "Images.epub");
		using (ZipArchive zip = ZipFile.OpenRead(epub))
		{
			string[] images = zip.Entries.Select(e => e.FullName).Where(n => n.StartsWith("EPUB/images/")).OrderBy(n => n).ToArray();
			Assert.Equal(["EPUB/images/image-0001.png", "EPUB/images/image-0002.png"], images); // pic.png deduplicated, flaky retried

			using StreamReader chapter = new(zip.GetEntry("EPUB/text/chapter-0001.xhtml")!.Open());
			string xhtml = chapter.ReadToEnd();
			Assert.Contains("src=\"../images/image-0001.png\"", xhtml);
			Assert.Contains("src=\"../images/image-0002.png\"", xhtml);
			Assert.DoesNotContain("localhost", xhtml);
			Assert.DoesNotContain("border=", xhtml);
			Assert.DoesNotContain("gone.png", xhtml);
			Assert.Contains("alt=\"\"", xhtml);

			using StreamReader opf = new(zip.GetEntry("EPUB/content.opf")!.Open());
			string manifest = opf.ReadToEnd();
			Assert.Contains("href=\"images/image-0001.png\"", manifest);
			Assert.Contains("href=\"images/image-0002.png\" media-type=\"image/png\"", manifest);
		}

		Assert.Equal(1, server.Hits("/pic.png"));
		Assert.Equal(3, server.Hits("/flaky.png"));
		Assert.Contains(_log, l => l.Contains("Warning") && l.Contains("image download failed (HTTP 404)"));
		Assert.Contains(_log, l => l.Contains("Warning") && l.Contains("unsupported image source"));
		Assert.Equal(2, _log.Count(l => l.Contains("unsupported image source")));
	}

	[Fact]
	public async Task InlineImages_PassEpubCheck()
	{
		EpubCheck.RequireAvailable();
		using TestServer server = new((_, _) => (200, TestServer.Png));

		await StoryWriter.CreateEpubAsync(Story("ImagesValid", $"<p>text <img src=\"{server.BaseUrl}/a.png\" alt=\"a\"></p>"), null, _dir);

		Assert.Empty(EpubCheck.Validate(Path.Combine(_dir, "ImagesValid.epub")));
	}

	// ---- retries

	[Fact]
	public async Task GetWithRetry_RetriesTransientStatusesThenSucceeds()
	{
		using TestServer server = new((_, hit) => hit < 3 ? (503, []) : (200, [1]));

		using HttpResponseMessage response = await EpubManagerClient.GetWithRetryAsync(server.BaseUrl + "/x");

		Assert.True(response.IsSuccessStatusCode);
		Assert.Equal(3, server.Hits("/x"));
	}

	[Fact]
	public async Task GetWithRetry_GivesUpAfterMaxAttemptsAndReturnsLastResponse()
	{
		using TestServer server = new((_, _) => (503, []));

		using HttpResponseMessage response = await EpubManagerClient.GetWithRetryAsync(server.BaseUrl + "/x", maxAttempts: 2);

		Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
		Assert.Equal(2, server.Hits("/x"));
	}

	[Fact]
	public async Task GetWithRetry_DoesNotRetryClientErrors()
	{
		using TestServer server = new((_, _) => (404, []));

		using HttpResponseMessage response = await EpubManagerClient.GetWithRetryAsync(server.BaseUrl + "/x");

		Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
		Assert.Equal(1, server.Hits("/x"));
	}

	[Fact]
	public async Task WithRetry_RetriesFailuresThenSucceeds()
	{
		int calls = 0;
		List<string> retries = [];

		int result = await EpubManagerClient.WithRetryAsync(() =>
		{
			if (++calls < 3) throw new InvalidOperationException("boom");
			return Task.FromResult(42);
		}, onRetry: retries.Add);

		Assert.Equal(42, result);
		Assert.Equal(3, calls);
		Assert.Equal(2, retries.Count);
	}

	[Fact]
	public async Task WithRetry_ThrowsLastErrorAfterMaxAttempts()
	{
		int calls = 0;

		await Assert.ThrowsAsync<InvalidOperationException>(() => EpubManagerClient.WithRetryAsync<int>(() =>
		{
			calls++;
			throw new InvalidOperationException("always");
		}, maxAttempts: 3));

		Assert.Equal(3, calls);
	}

	[Fact]
	public async Task WithRetry_StopsOnCancellation()
	{
		using CancellationTokenSource cts = new();
		int calls = 0;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EpubManagerClient.WithRetryAsync<int>(() =>
		{
			calls++;
			cts.Cancel();
			throw new InvalidOperationException("boom");
		}, cancellationToken: cts.Token));

		Assert.Equal(1, calls);
	}
}
