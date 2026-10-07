using System.IO.Compression;
using EpubManager.ContentSources;
using EpubManager.Util;

namespace EpubManager.Tests;

public class EpubOptionsTests : IDisposable
{
	private readonly string _dir = Path.Combine(Path.GetTempPath(), "epubmanager-tests", Guid.NewGuid().ToString("N"));
	private readonly List<string> _log = [];

	public EpubOptionsTests() => Directory.CreateDirectory(_dir);

	public void Dispose()
	{
		try { Directory.Delete(_dir, true); } catch { /* best effort */ }
	}

	private EpubStory Story(string title = "Options", string language = "English", string? description = null, EpubStyle? style = null)
	{
		string chapter = Path.Combine(_dir, title + ".txt");
		File.WriteAllText(chapter, "<p>First paragraph.</p>\n<p>Second paragraph.</p>\n***\n<p>After the break.</p>");
		return new EpubStory(title, language, "Author", null, [], new Dictionary<string, string> { ["One"] = chapter }, Description: description, Style: style);
	}

	private async Task<ZipArchive> Build(EpubStory story)
	{
		await StoryWriter.CreateEpubAsync(story, _log.Add, _dir);
		return ZipFile.OpenRead(Path.Combine(_dir, $"{StoryWriterUtil.ToSafeFileName(story.Title)}.epub"));
	}

	private static string Read(ZipArchive zip, string entry)
	{
		using StreamReader reader = new(zip.GetEntry(entry)!.Open());
		return reader.ReadToEnd();
	}

	// ---- style CSS

	[Fact]
	public void DefaultStyle_EmitsNoOverrides()
	{
		Assert.Equal("", WriterUtil.GenerateStyleOverrides(null));
		Assert.Equal("", WriterUtil.GenerateStyleOverrides(EpubStyle.Default));
		Assert.Equal("", WriterUtil.GenerateStyleOverrides(new EpubStyle()));
	}

	[Fact]
	public void Style_FontSizeAndLineHeight()
	{
		string css = WriterUtil.GenerateStyleOverrides(new EpubStyle { Font = EpubFont.SansSerif, FontSizePercent = 120, LineHeight = 1.5 });

		Assert.Contains("font-family: \"Helvetica Neue\", Helvetica, Arial, sans-serif;", css);
		Assert.Contains("font-size: 120%;", css);
		Assert.Contains("line-height: 1.5;", css);
	}

	[Fact]
	public void Style_OnlyEmitsWhatChanged()
	{
		string css = WriterUtil.GenerateStyleOverrides(new EpubStyle { TextAlign = EpubTextAlign.Justify });

		Assert.Contains("text-align: justify;", css);
		Assert.DoesNotContain("font-family", css);
		Assert.DoesNotContain("font-size", css);
		Assert.DoesNotContain("text-indent", css);
	}

	[Fact]
	public void Style_IndentedParagraphsSkipTheFirstAfterHeadingsAndBreaks()
	{
		string css = WriterUtil.GenerateStyleOverrides(new EpubStyle { ParagraphStyle = EpubParagraphStyle.Indented });

		Assert.Contains("text-indent: 1.5em;", css);
		Assert.Contains("h1 + p, hr + p", css);
	}

	[Fact]
	public void Style_CenteredChapterHeadings()
	{
		Assert.Contains("h1 {\n  text-align: center;", WriterUtil.GenerateStyleOverrides(new EpubStyle { ChapterHeadingAlign = EpubHeadingAlign.Center }));
	}

	[Fact]
	public void Style_SceneBreakTextIsEscaped()
	{
		string css = WriterUtil.GenerateStyleOverrides(new EpubStyle { SceneBreak = "a\"b\\c\nd" });

		Assert.Contains("content: \"a\\\"b\\\\c d\";", css);
	}

	[Fact]
	public void Style_EmptySceneBreakDrawsALine()
	{
		string css = WriterUtil.GenerateStyleOverrides(new EpubStyle { SceneBreak = "" });

		Assert.Contains("content: none;", css);
		Assert.Contains("height: 1px;", css);
	}

	[Theory]
	[InlineData(49)]
	[InlineData(301)]
	public void Style_RejectsOutOfRangeFontSize(int percent) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new EpubStyle { FontSizePercent = percent }.Validate());

	[Theory]
	[InlineData(0.9)]
	[InlineData(3.1)]
	[InlineData(double.NaN)]
	public void Style_RejectsOutOfRangeLineHeight(double height) =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new EpubStyle { LineHeight = height }.Validate());

	[Fact]
	public async Task Build_RejectsInvalidStyleBeforeWritingAnything()
	{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => StoryWriter.CreateEpubAsync(Story(style: new EpubStyle { FontSizePercent = 5 }), _log.Add, _dir));

		Assert.Empty(Directory.GetFiles(_dir, "*.epub*"));
	}

	// ---- built books

	[Fact]
	public async Task DefaultStyle_ShipsTheStockStylesheetUntouched()
	{
		using ZipArchive zip = await Build(Story());

		Assert.DoesNotContain("Style options", Read(zip, "EPUB/styles/style.css"));
	}

	[Fact]
	public async Task CustomStyle_IsAppendedToTheStylesheet()
	{
		using ZipArchive zip = await Build(Story(style: new EpubStyle { Font = EpubFont.Monospace, SceneBreak = "~" }));
		string css = Read(zip, "EPUB/styles/style.css");

		Assert.Contains("Style options", css);
		Assert.Contains("monospace", css);
		Assert.Contains("content: \"~\";", css);
		Assert.True(css.IndexOf("Style options", StringComparison.Ordinal) > css.IndexOf("hr.scene-break", StringComparison.Ordinal), "overrides must come after the stock rules");
	}

	[Fact]
	public async Task EveryStyleOptionTogether_PassesEpubCheck()
	{
		EpubCheck.RequireAvailable();
		EpubStyle style = new()
		{
			Font = EpubFont.SansSerif,
			FontSizePercent = 115,
			LineHeight = 1.6,
			TextAlign = EpubTextAlign.Justify,
			ParagraphStyle = EpubParagraphStyle.Indented,
			ChapterHeadingAlign = EpubHeadingAlign.Center,
			SceneBreak = "❦"
		};

		using ZipArchive zip = await Build(Story("AllStyles", style: style, description: "A synopsis."));
		string epub = Path.Combine(_dir, "AllStyles.epub");

		Assert.Empty(EpubCheck.Validate(epub));
	}

	[Fact]
	public async Task EmptySceneBreakStyle_PassesEpubCheck()
	{
		EpubCheck.RequireAvailable();
		using ZipArchive zip = await Build(Story("LineBreak", style: new EpubStyle { SceneBreak = "" }));

		Assert.Empty(EpubCheck.Validate(Path.Combine(_dir, "LineBreak.epub")));
	}

	// ---- description

	[Fact]
	public async Task Description_IsStoredAndShownOnTheTitlePage()
	{
		using ZipArchive zip = await Build(Story(description: "First paragraph & more.\n\nSecond <paragraph>.\nThird line."));

		Assert.Contains("<description xmlns=\"http://purl.org/dc/elements/1.1/\">First paragraph &amp; more.", Read(zip, "EPUB/content.opf"));

		string titlePage = Read(zip, "EPUB/text/title-page.xhtml");
		Assert.Contains("class=\"synopsis\"", titlePage);
		Assert.Contains("<p>First paragraph &amp; more.</p>", titlePage);
		Assert.Contains("<p>Second &lt;paragraph&gt;.</p>", titlePage);
		Assert.Contains("<p>Third line.</p>", titlePage);
	}

	[Fact]
	public async Task NoDescription_AddsNothing()
	{
		using ZipArchive zip = await Build(Story());

		Assert.DoesNotContain("description", Read(zip, "EPUB/content.opf"));
		Assert.DoesNotContain("synopsis", Read(zip, "EPUB/text/title-page.xhtml"));
	}

	[Fact]
	public async Task Description_PassesEpubCheck()
	{
		EpubCheck.RequireAvailable();
		using ZipArchive zip = await Build(Story("Synopsis", description: "One.\n\nTwo & <three>."));

		Assert.Empty(EpubCheck.Validate(Path.Combine(_dir, "Synopsis.epub")));
	}

	// ---- language and options

	[Fact]
	public async Task OptionsLanguage_OverridesTheStoryLanguage()
	{
		EpubStory story = new EpubOptions { Language = "Deutsch" }.Apply(Story(language: "English"));
		using ZipArchive zip = await Build(story);

		Assert.Contains(">de</language>", Read(zip, "EPUB/content.opf"));
		Assert.Contains("lang=\"de\"", Read(zip, "EPUB/text/chapter-0001.xhtml"));
		Assert.Contains("lang=\"de\"", Read(zip, "EPUB/nav.xhtml"));
	}

	[Fact]
	public async Task UnrecognizedLanguage_WarnsAndUsesUndetermined()
	{
		using ZipArchive zip = await Build(Story(language: "Klingon"));

		Assert.Contains(">und</language>", Read(zip, "EPUB/content.opf"));
		Assert.Contains(_log, l => l.Contains("Warning") && l.Contains("Klingon"));
	}

	[Fact]
	public void Apply_OnlyOverridesWhatWasSet()
	{
		EpubStory original = Story(language: "English", description: "kept", style: new EpubStyle { FontSizePercent = 110 });
		original.Identifier = StoryWriter.StableId("id");

		EpubStory untouched = new EpubOptions().Apply(original);
		Assert.Equal("English", untouched.Language);
		Assert.Equal("kept", untouched.Description);
		Assert.Equal(110, untouched.Style!.FontSizePercent);

		EpubStory blank = new EpubOptions { Language = "  ", Description = "" }.Apply(original);
		Assert.Equal("English", blank.Language);
		Assert.Equal("kept", blank.Description);

		EpubStory changed = new EpubOptions { Language = " fr ", Description = " new ", Style = new EpubStyle { LineHeight = 2 } }.Apply(original);
		Assert.Equal("fr", changed.Language);
		Assert.Equal("new", changed.Description);
		Assert.Equal(2, changed.Style!.LineHeight);
		Assert.Equal(original.Identifier, changed.Identifier);
	}
}
