using System.IO.Compression;
using LiteroticaApi.Api;

namespace EpubManager.Tests;

/// <summary>
/// Hits literotica.com using the URLs in LITEROTICA_TEST_STORY_URL / LITEROTICA_TEST_SERIES_URL
/// (from the environment or a .env file; tests skip when unset). Run only these with
/// <c>dotnet test --project EpubManager.Tests --filter-trait "Category=Live"</c>, or skip them with
/// <c>--filter-not-trait "Category=Live"</c>.
/// </summary>
[Trait("Category", "Live")]
public class LiteroticaLiveTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "epubmanager-tests", "live-" + Guid.NewGuid().ToString("N"));
	private readonly ITestOutputHelper _output;

	public LiteroticaLiveTests(ITestOutputHelper output)
	{
		_output = output;
		Directory.CreateDirectory(_dir);
	}

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { /* best effort */ }
	}

	private static ContentSources.IStoryWriter Writer => EpubManager.Writers.Literotica ?? throw new InvalidOperationException("Literotica plugin not found.");

	private string SingleEpub()
	{
		string[] files = Directory.GetFiles(_dir, "*.epub");
		Assert.Single(files);
		return files[0];
	}

	private static string ReadEntry(string epub, string entry)
	{
		using ZipArchive zip = ZipFile.OpenRead(epub);
		using StreamReader reader = new(zip.GetEntry(entry)!.Open());
		return reader.ReadToEnd();
	}

	private int ChapterCount(string epub)
	{
		using ZipArchive zip = ZipFile.OpenRead(epub);
		int chapters = zip.Entries.Count(e => e.FullName.StartsWith("EPUB/text/chapter-"));
		_output.WriteLine($"{Path.GetFileName(epub)}: {chapters} chapters, {new FileInfo(epub).Length / 1024} KB");
		return chapters;
	}

	private void AssertEpubCheckClean(string epub)
	{
		EpubCheck.RequireAvailable();
		List<string> problems = EpubCheck.Validate(epub);
		foreach (string p in problems) _output.WriteLine(p);
		Assert.DoesNotContain(problems, p => p.StartsWith("ERROR") || p.StartsWith("FATAL"));
	}

	[Fact]
	public async Task Story_BuildsValidEpubWithStoryTitleAndNoSeries()
	{
		string url = TestEnv.Require(TestEnv.StoryUrl);
		string slug = await LiteroticaApi.LiteroticaUrlUtil.GetStorySlugAsync(url);
		string title = (await StoryApi.GetStoryInfoAsync(slug))!.Submission.Title;

		await Writer.CreateEpubFromStoryAsync(url, _dir);

		string epub = SingleEpub();
		Assert.Equal(1, ChapterCount(epub));
		Assert.Contains(System.Net.WebUtility.HtmlEncode(title), ReadEntry(epub, "EPUB/nav.xhtml"));
		Assert.DoesNotContain("belongs-to-collection", ReadEntry(epub, "EPUB/content.opf"));
		AssertEpubCheckClean(epub);
	}

	[Fact]
	public async Task Story_AppliesLanguageDescriptionAndStyleOptions()
	{
		ContentSources.EpubOptions options = new()
		{
			Language = "French",
			Description = "A short synopsis.\n\nSecond paragraph.",
			Style = new ContentSources.EpubStyle { Font = ContentSources.EpubFont.SansSerif, FontSizePercent = 110, TextAlign = ContentSources.EpubTextAlign.Justify }
		};

		await Writer.CreateEpubFromStoryAsync(TestEnv.Require(TestEnv.StoryUrl), _dir, options: options);

		string epub = SingleEpub();
		Assert.Contains(">fr</language>", ReadEntry(epub, "EPUB/content.opf"));
		Assert.Contains("A short synopsis.", ReadEntry(epub, "EPUB/content.opf"));
		Assert.Contains("<p>Second paragraph.</p>", ReadEntry(epub, "EPUB/text/title-page.xhtml"));
		Assert.Contains("font-size: 110%;", ReadEntry(epub, "EPUB/styles/style.css"));
		AssertEpubCheckClean(epub);
	}

	[Fact]
	public async Task Story_RawOutput_SkipsArchive()
	{
		await Writer.CreateEpubFromStoryAsync(TestEnv.Require(TestEnv.StoryUrl), _dir, raw: true);

		Assert.Empty(Directory.GetFiles(_dir, "*.epub"));
		Assert.NotEmpty(Directory.GetDirectories(_dir));
	}

	[Fact]
	public async Task Series_IncludesEveryPartInOrderWithRealTitles()
	{
		string url = TestEnv.Require(TestEnv.SeriesUrl);
		var parts = (await SeriesApi.GetSeriesInfoAsync(url))!.Parts;

		await Writer.CreateEpubFromSeriesAsync(url, _dir);

		string epub = SingleEpub();
		Assert.Equal(parts.Count, ChapterCount(epub));

		string nav = ReadEntry(epub, "EPUB/nav.xhtml");
		int previous = -1;
		foreach (var part in parts)
		{
			int at = nav.IndexOf(System.Net.WebUtility.HtmlEncode(part.Title), StringComparison.Ordinal);
			Assert.True(at > previous, $"'{part.Title}' missing or out of order in nav");
			previous = at;
		}
		AssertEpubCheckClean(epub);
	}

	[Fact]
	public async Task Series_RespectsInclusiveChapterRange()
	{
		string url = TestEnv.Require(TestEnv.SeriesUrl);
		if ((await SeriesApi.GetSeriesInfoAsync(url))!.Parts.Count < 3) Assert.Skip("series needs at least 3 parts for the range test.");

		await Writer.CreateEpubFromSeriesAsync(url, _dir, startIndex: 0, endIndex: 1);

		Assert.Equal(2, ChapterCount(SingleEpub()));
	}

	[Fact]
	public async Task Series_StartIndexSkipsEarlierParts()
	{
		string url = TestEnv.Require(TestEnv.SeriesUrl);
		int count = (await SeriesApi.GetSeriesInfoAsync(url))!.Parts.Count;
		if (count < 3) Assert.Skip("series needs at least 3 parts for the start-index test.");

		await Writer.CreateEpubFromSeriesAsync(url, _dir, startIndex: 2);

		Assert.Equal(count - 2, ChapterCount(SingleEpub()));
	}

	[Fact]
	public async Task InvalidUrl_Throws()
	{
		await Assert.ThrowsAnyAsync<Exception>(() => Writer.CreateEpubFromStoryAsync("https://literotica.com/s/this-story-does-not-exist-zzzz-9999", _dir));
	}
}
