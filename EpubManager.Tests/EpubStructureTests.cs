using System.IO.Compression;
using EpubManager.ContentSources;

namespace EpubManager.Tests;

public class EpubStructureTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "epubmanager-tests", Guid.NewGuid().ToString("N"));
	private readonly List<string> _log = [];

	public EpubStructureTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { /* best effort */ }
	}

	private string Write(string name, string content)
	{
		string path = Path.Combine(_dir, name);
		File.WriteAllText(path, content);
		return path;
	}

	private string WriteCover(string name = "cover.png")
	{
		// 1x1 PNG
		byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
		string path = Path.Combine(_dir, name);
		File.WriteAllBytes(path, png);
		return path;
	}

	private EpubStory Story(string title = "Test Story", string? cover = null, EpubSeries? series = null)
	{
		Dictionary<string, string> chapters = new()
		{
			["Chapter 0001"] = Write("one.txt", "<p>He said &quot;hello&quot; &amp; left&hellip;</p>\n<p>Café “quotes” &mdash; dash<br>break</p>\n<p>2 &lt; 3</p>"),
			["Chapter 0002"] = Write("two.txt", "Plain line & ampersand\n<P>Unclosed <b>bold and <I>mixed</b></P>\n<div>block</div>"),
			["  "] = Write("three.txt", "<p>Third.</p>")
		};
		return new EpubStory(title, "English", "Some Author", series, ["tag1", "tag2"], chapters, cover);
	}

	private string Build(EpubStory story)
	{
		StoryWriter.CreateEpub(story, _log.Add, _dir);
		return Path.Combine(_dir, $"{StoryWriterUtil.ToSafeFileName(story.Title)}.epub");
	}

	[Fact]
	public void Epub_WithCoverAndSeries_PassesEpubCheck()
	{
		EpubCheck.RequireAvailable();
		string epub = Build(Story(cover: WriteCover(), series: new EpubSeries("My Series", 2)));

		Assert.Empty(EpubCheck.Validate(epub));
	}

	[Fact]
	public void Epub_WithoutCoverOrSeries_PassesEpubCheck()
	{
		EpubCheck.RequireAvailable();
		string epub = Build(Story());

		Assert.Empty(EpubCheck.Validate(epub));
	}

	[Fact]
	public void Epub_TitleWithSpecialCharacters_PassesEpubCheck()
	{
		EpubCheck.RequireAvailable();
		string epub = Build(Story(title: "Test: A & B <3 \"quoted\""));

		Assert.Empty(EpubCheck.Validate(epub));
	}

	[Fact]
	public void Epub_MimetypeIsFirstAndStored()
	{
		using ZipArchive zip = ZipFile.OpenRead(Build(Story()));

		ZipArchiveEntry first = zip.Entries[0];
		Assert.Equal("mimetype", first.FullName);
		Assert.Equal(first.Length, first.CompressedLength);
		using StreamReader reader = new(first.Open());
		Assert.Equal("application/epub+zip", reader.ReadToEnd());
	}

	[Fact]
	public void Epub_UsesConsistentFileNames()
	{
		using ZipArchive zip = ZipFile.OpenRead(Build(Story(cover: WriteCover())));
		string[] names = zip.Entries.Select(e => e.FullName).ToArray();

		string[] expected =
		[
			"EPUB/content.opf", "EPUB/nav.xhtml", "EPUB/toc.ncx", "EPUB/styles/style.css",
			"EPUB/text/title-page.xhtml", "EPUB/text/cover-page.xhtml", "EPUB/images/cover.png",
			"EPUB/text/chapter-0001.xhtml", "EPUB/text/chapter-0002.xhtml", "EPUB/text/chapter-0003.xhtml",
			"META-INF/container.xml"
		];
		foreach (string name in expected) Assert.Contains(name, names);
		Assert.DoesNotContain(names, n => n.Contains("stylesheet1"));
	}

	[Fact]
	public void Epub_ChapterTextHasCleanEntities()
	{
		using ZipArchive zip = ZipFile.OpenRead(Build(Story()));
		using StreamReader reader = new(zip.GetEntry("EPUB/text/chapter-0001.xhtml")!.Open());
		string xhtml = reader.ReadToEnd();

		Assert.DoesNotContain("&#38;", xhtml);
		Assert.Contains("&amp;", xhtml);
		Assert.Contains("…", xhtml);
	}

	[Fact]
	public void Epub_MissingCover_WarnsAndStillBuildsValidBook()
	{
		EpubCheck.RequireAvailable();
		string epub = Build(Story(cover: Path.Combine(_dir, "does-not-exist.png")));

		Assert.Contains(_log, l => l.Contains("Warning") && l.Contains("without a cover"));
		using (ZipArchive zip = ZipFile.OpenRead(epub))
		{
			Assert.DoesNotContain(zip.Entries, e => e.FullName.Contains("cover"));
			using StreamReader reader = new(zip.GetEntry("EPUB/content.opf")!.Open());
			Assert.DoesNotContain("cover", reader.ReadToEnd());
		}
		Assert.Empty(EpubCheck.Validate(epub));
	}

	[Fact]
	public void Epub_UnsupportedCoverType_WarnsAndContinues()
	{
		string epub = Build(Story(cover: Write("cover.txt", "not an image")));

		Assert.Contains(_log, l => l.Contains("Warning") && l.Contains("not a supported"));
		Assert.True(File.Exists(epub));
	}

	[Fact]
	public void Epub_WarningGoesToConsoleErrorWhenNoLogger()
	{
		StringWriter captured = new();
		TextWriter original = Console.Error;
		Console.SetError(captured);
		try
		{
			StoryWriter.CreateEpub(Story(cover: Path.Combine(_dir, "nope.png")), null, _dir);
		}
		finally { Console.SetError(original); }

		Assert.Contains("without a cover", captured.ToString());
	}

	[Fact]
	public void Epub_RawOutput_KeepsFolderAndSkipsArchive()
	{
		StoryWriter.CreateEpub(Story(title: "Raw Story"), _log.Add, _dir, raw: true);

		Assert.True(Directory.Exists(Path.Combine(_dir, "Raw Story", "EPUB")));
		Assert.False(File.Exists(Path.Combine(_dir, "Raw Story.epub")));
	}
}
