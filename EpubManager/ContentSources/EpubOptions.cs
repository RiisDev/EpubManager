using System;

namespace EpubManager.ContentSources
{
	/// <summary>Font family used for the body text.</summary>
	public enum EpubFont
	{
		/// <summary>Georgia / Times style serif (the default).</summary>
		Serif,
		/// <summary>Helvetica / Arial style sans-serif.</summary>
		SansSerif,
		/// <summary>Fixed-width font.</summary>
		Monospace
	}

	/// <summary>Horizontal alignment of paragraph text.</summary>
	public enum EpubTextAlign
	{
		/// <summary>Ragged right (the default).</summary>
		Left,
		/// <summary>Justified, with automatic hyphenation where the reader supports it.</summary>
		Justify
	}

	/// <summary>How paragraphs are separated.</summary>
	public enum EpubParagraphStyle
	{
		/// <summary>A blank line between paragraphs, no indent (the default).</summary>
		Spaced,
		/// <summary>No blank line; each paragraph is indented, as in printed novels. The first paragraph after a heading or scene break is not indented.</summary>
		Indented
	}

	/// <summary>Alignment of chapter headings.</summary>
	public enum EpubHeadingAlign
	{
		/// <summary>Left aligned (the default).</summary>
		Left,
		/// <summary>Centered.</summary>
		Center
	}

	/// <summary>
	/// How the book looks. Every property has a default that matches the stock appearance; set only what you want to change.
	/// Readers may still apply the user's own font and size settings on top.
	/// </summary>
	public record EpubStyle
	{
		/// <summary>The stock appearance.</summary>
		public static EpubStyle Default { get; } = new();

		/// <summary>Body font family. Default <see cref="EpubFont.Serif"/>.</summary>
		public EpubFont Font { get; init; } = EpubFont.Serif;

		/// <summary>Base text size as a percentage of the reader's default, 50 to 300. Default 100.</summary>
		public int FontSizePercent { get; init; } = 100;

		/// <summary>Line height as a multiple of the font size, 1.0 to 3.0. Default 1.2.</summary>
		public double LineHeight { get; init; } = 1.2;

		/// <summary>Paragraph alignment. Default <see cref="EpubTextAlign.Left"/>.</summary>
		public EpubTextAlign TextAlign { get; init; } = EpubTextAlign.Left;

		/// <summary>Paragraph separation. Default <see cref="EpubParagraphStyle.Spaced"/>.</summary>
		public EpubParagraphStyle ParagraphStyle { get; init; } = EpubParagraphStyle.Spaced;

		/// <summary>Chapter heading alignment. Default <see cref="EpubHeadingAlign.Left"/>.</summary>
		public EpubHeadingAlign ChapterHeadingAlign { get; init; } = EpubHeadingAlign.Left;

		/// <summary>Text shown centered at scene breaks, for example "* * *" (the default) or "~". An empty string draws a thin horizontal line instead.</summary>
		public string SceneBreak { get; init; } = "* * *";

		/// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if a value is outside its supported range.</summary>
		public void Validate()
		{
			if (FontSizePercent < 50 || FontSizePercent > 300)
				throw new ArgumentOutOfRangeException(nameof(FontSizePercent), FontSizePercent, "Font size must be between 50 and 300 percent.");
			if (double.IsNaN(LineHeight) || LineHeight < 1.0 || LineHeight > 3.0)
				throw new ArgumentOutOfRangeException(nameof(LineHeight), LineHeight, "Line height must be between 1.0 and 3.0.");
			if (!Enum.IsDefined(typeof(EpubFont), Font)) throw new ArgumentOutOfRangeException(nameof(Font));
			if (!Enum.IsDefined(typeof(EpubTextAlign), TextAlign)) throw new ArgumentOutOfRangeException(nameof(TextAlign));
			if (!Enum.IsDefined(typeof(EpubParagraphStyle), ParagraphStyle)) throw new ArgumentOutOfRangeException(nameof(ParagraphStyle));
			if (!Enum.IsDefined(typeof(EpubHeadingAlign), ChapterHeadingAlign)) throw new ArgumentOutOfRangeException(nameof(ChapterHeadingAlign));
			if (SceneBreak == null) throw new ArgumentNullException(nameof(SceneBreak));
		}
	}

	/// <summary>
	/// Options a caller can pass to a story writer. Anything left <see langword="null"/> keeps the writer's own value.
	/// </summary>
	public record EpubOptions
	{
		/// <summary>Language of the book: a BCP 47 code ("en", "pt-BR") or a name such as "English". Overrides the writer's default.</summary>
		public string? Language { get; init; }

		/// <summary>Synopsis shown on the title page and stored as the book's description. Blank lines separate paragraphs.</summary>
		public string? Description { get; init; }

		/// <summary>Appearance of the book.</summary>
		public EpubStyle? Style { get; init; }

		/// <summary>Returns the story with these options applied.</summary>
		public EpubStory Apply(EpubStory story) => story with
		{
			Language = string.IsNullOrWhiteSpace(Language) ? story.Language : Language!.Trim(),
			Description = string.IsNullOrWhiteSpace(Description) ? story.Description : Description!.Trim(),
			Style = Style ?? story.Style
		};
	}
}
