using System.Xml.Linq;
using EpubManager.Util;

namespace EpubManager.Tests;

public class WriterUtilTests
{
	[Theory]
	[InlineData("English", "en")]
	[InlineData("english", "en")]
	[InlineData("Deutsch", "de")]
	[InlineData("en", "en")]
	[InlineData("pt-BR", "pt-br")]
	[InlineData("Klingon", "und")]
	[InlineData("", "und")]
	public void LanguageCode_MapsNamesAndCodes(string input, string expected) =>
		Assert.Equal(expected, WriterUtil.LanguageCode(input));

	[Theory]
	[InlineData("Chapter 0001", "Chapter 1")]
	[InlineData("chapter12", "Chapter 12")]
	[InlineData("  The Dark  ", "The Dark")]
	[InlineData("   ", "Chapter 3")]
	[InlineData("Chapter One", "Chapter One")]
	public void ChapterTitle_TidiesKeys(string key, string expected) =>
		Assert.Equal(expected, WriterUtil.ChapterTitle(key, 3));

	[Fact]
	public void CleanMarkup_ConvertsEntitiesOnce()
	{
		string result = WriterUtil.CleanMarkup("<p>He said &quot;hi&quot; &amp; left&hellip; &mdash; 2 &lt; 3 &copy;</p>", "t");

		Assert.DoesNotContain("&#38;", result);
		Assert.DoesNotContain("&amp;amp;", result);
		Assert.Contains("&amp;", result); // a real ampersand stays a single, valid XML entity
		Assert.Contains("&#8230;", result);
		AssertWellFormed(result);
	}

	[Fact]
	public void CleanMarkup_EscapesBareAmpersandsAndUnknownEntities()
	{
		string result = WriterUtil.CleanMarkup("Tom & Jerry &notanentity; 5 > 3", "t");

		AssertWellFormed(result);
		Assert.StartsWith("<p>", result);
	}

	[Fact]
	public void CleanMarkup_WrapsPlainLinesAndDropsEmptyOnes()
	{
		string result = WriterUtil.CleanMarkup("first\n\n   \n<p></p>\nsecond", "t");

		Assert.Equal("<p>first</p>\n<p>second</p>", result);
	}

	[Fact]
	public void CleanMarkup_RepairsUnbalancedTagsAndLowercases()
	{
		string result = WriterUtil.CleanMarkup("<P>Unclosed <b>bold and <I>mixed</b></P>", "t");

		AssertWellFormed(result);
		Assert.DoesNotContain("<P>", result);
		Assert.DoesNotContain("<I>", result);
	}

	[Fact]
	public void CleanMarkup_DoesNotWrapBlockElementsInParagraphs()
	{
		string result = WriterUtil.CleanMarkup("<div>stray</div>\n<h2>Heading</h2>", "t");

		Assert.DoesNotContain("<p><div>", result);
		Assert.DoesNotContain("<p><h2>", result);
		AssertWellFormed(result);
	}

	[Fact]
	public void CleanMarkup_SelfClosesVoidTags()
	{
		string result = WriterUtil.CleanMarkup("<p>line<br>break</p>", "t");

		AssertWellFormed(result);
		Assert.Contains("<br />", result);
	}

	[Fact]
	public void CleanMarkup_StripsControlCharacters()
	{
		string result = WriterUtil.CleanMarkup("<p>a\u0001b\u0008c</p>", "t");

		Assert.Equal("<p>abc</p>", result);
	}

	[Fact]
	public void GenerateChapterXhtml_IsNamespacedAndTitled()
	{
		XDocument doc = WriterUtil.GenerateChapterXhtml("Chapter 1", "<p>text</p>", 1, "en");
		XNamespace xhtml = "http://www.w3.org/1999/xhtml";

		Assert.Equal("chapter-0001", doc.Descendants(xhtml + "h1").Single().Attribute("id")!.Value);
		Assert.Single(doc.Descendants(xhtml + "p"));
		Assert.Equal("en", doc.Root!.Attribute("lang")!.Value);
	}

	private static void AssertWellFormed(string fragment) => XElement.Parse($"<body>{fragment}</body>");
}
