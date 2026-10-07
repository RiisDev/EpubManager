using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Net;
using System.Xml.Linq;
using EpubManager.ContentSources;

namespace EpubManager.Util
{
	internal static class WriterUtil
	{
		private static readonly Regex InvalidXmlCharsRegex = new(
			@"(?<![\uD800-\uDBFF])[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]",
			RegexOptions.Compiled
		);

		internal static string RemoveControlCharacters(this string input) => InvalidXmlCharsRegex.Replace(input, string.Empty);

		private static readonly string[] VoidTags = ["br", "hr", "img", "input", "meta", "link"];
		private static readonly Regex EntityRegex = new(@"&(?!(?:amp|lt|gt|quot|apos);)(#\d+|#[xX][0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);", RegexOptions.Compiled);
		private static readonly Regex BareAmpersandRegex = new(@"&(?!(?:#\d+|#[xX][0-9a-fA-F]+|[A-Za-z][A-Za-z0-9]*);)", RegexOptions.Compiled);
		private static readonly Regex BlockStartRegex = new(@"^<(div|h[1-6]|blockquote|ul|ol|table|hr|pre|section)[\s/>]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private static readonly Regex SceneBreakRegex = new(@"^(?:[*\-_~=#\u2022\u00B7]\s*){3,}$", RegexOptions.Compiled);
		private static readonly Regex TagRegex = new(@"<(/?)([a-zA-Z0-9]+)(?:\s[^>]*)?>", RegexOptions.Compiled);

		/// <summary>
		/// Converts raw chapter text (HTML-ish, one paragraph per line) into a well-formed XHTML fragment of
		/// <c>&lt;p&gt;</c> lines. Falls back to plain-text paragraphs if the markup cannot be repaired.
		/// </summary>
		internal static string CleanMarkup(string raw, string name, Action<string>? onLog = null)
		{
			// Named HTML entities (&mdash; etc.) are not valid XML; turn them into numeric references.
			string content = EntityRegex.Replace(raw.RemoveControlCharacters().Replace("\r\n", "\n").Replace('\r', '\n'), m =>
			{
				string decoded = WebUtility.HtmlDecode(m.Value);
				return decoded == m.Value ? m.Value.Replace("&", "&amp;") : $"&#{char.ConvertToUtf32(decoded, 0)};";
			});
			content = BareAmpersandRegex.Replace(content, "&amp;");

			content = Regex.Replace(content, @"<(/?)([A-Za-z][A-Za-z0-9]*)", m => $"<{m.Groups[1].Value}{m.Groups[2].Value.ToLowerInvariant()}");
			content = VoidTags.Aggregate(content, (current, tag) => Regex.Replace(current, $@"<({tag})(\s[^>]*?)?(?<!/)>", "<$1$2 />", RegexOptions.IgnoreCase));

			List<string> lines = [];
			foreach (string rawLine in content.Split('\n'))
			{
				string line = rawLine.Trim();
				if (line.Length == 0) continue;

				// Scene breaks (<hr>, ***, ---, ...) become one styled rule.
				string plain = Regex.Replace(line, "<[^>]*>", "").Trim();
				if (Regex.IsMatch(line, @"^<hr\b[^>]*>$", RegexOptions.IgnoreCase) || plain.Length > 0 && !line.Contains("<img") && SceneBreakRegex.IsMatch(plain))
				{
					lines.Add("<hr class=\"scene-break\" />");
					continue;
				}

				// Block-level lines (div, headings, lists...) must not be wrapped in <p>.
				bool isBlock = BlockStartRegex.IsMatch(line);

				if (!line.StartsWith("<", StringComparison.Ordinal))
					line = $"<p>{line}</p>";

				List<string> openTags = [];
				List<string> closeTags = [];
				foreach (Match match in TagRegex.Matches(line))
				{
					string tag = match.Groups[2].Value;
					if (match.Groups[1].Value == "/") closeTags.Add(tag);
					else if (!VoidTags.Contains(tag) && !match.Value.EndsWith("/>", StringComparison.Ordinal)) openTags.Add(tag);
				}

				// Drop tags that are opened but never closed (or the reverse) on this line.
				line = openTags.Where(tag => !closeTags.Contains(tag)).Aggregate(line, (current, tag) => Regex.Replace(current, $@"<{tag}(\s[^>]*)?>", ""));
				line = closeTags.Where(tag => !openTags.Contains(tag)).Aggregate(line, (current, tag) => Regex.Replace(current, $@"</{tag}>", ""));

				// Normalize to exactly one <p> wrapper.
				if (!isBlock)
					line = "<p>" + line.Replace("<p>", "").Replace("</p>", "") + "</p>";

				if (!string.IsNullOrWhiteSpace(Regex.Replace(line, "<[^>]*>", "")) || line.Contains("<hr") || line.Contains("<img"))
					lines.Add(line);
			}

			string cleaned = string.Join("\n", lines);

			try { _ = XElement.Parse($"<body>{cleaned}</body>"); }
			catch (Exception ex)
			{
				onLog?.Invoke($"[CleanChapter] Warning: markup in {name} could not be repaired ({ex.Message}); using plain text.");
				cleaned = string.Join("\n", content.Split('\n')
					.Select(l => Regex.Replace(l, "<[^>]*>", "").Trim())
					.Where(l => l.Length > 0)
					.Select(l => $"<p>{new XText(WebUtility.HtmlDecode(l))}</p>"));
			}

			return cleaned;
		}

		internal static XDocument GenerateChapterXhtml(string chapterTitle, string chapterMarkup, int chapterNumber, string language)
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			XNamespace epub = "http://www.idpf.org/2007/ops";

			XElement body = XElement.Parse($"<body xmlns=\"{xhtml}\">{chapterMarkup}</body>");
			body.AddFirst(new XElement(xhtml + "h1", new XAttribute("id", $"chapter-{chapterNumber:0000}"), chapterTitle));

			return new XDocument(
				new XDeclaration("1.0", "utf-8", "yes"),
				new XElement(xhtml + "html",
					new XAttribute(XNamespace.Xmlns + "epub", epub),
					new XAttribute("lang", language),
					new XAttribute(XNamespace.Xml + "lang", language),
					new XElement(xhtml + "head",
						new XElement(xhtml + "meta", new XAttribute("charset", "utf-8")),
						new XElement(xhtml + "title", chapterTitle),
						new XElement(xhtml + "link",
							new XAttribute("rel", "stylesheet"),
							new XAttribute("type", "text/css"),
							new XAttribute("href", "../styles/style.css")
						)
					),
					body
				)
			);
		}

		/// <summary>
		/// CSS appended after the stock stylesheet to apply a style. Returns an empty string when the style is the default,
		/// so the stock stylesheet is shipped untouched.
		/// </summary>
		internal static string GenerateStyleOverrides(EpubStyle? style)
		{
			if (style == null || style == EpubStyle.Default) return "";

			StringBuilder css = new();
			css.Append("\n/* Style options */\n");

			if (style.Font != EpubFont.Serif || style.FontSizePercent != 100 || Math.Abs(style.LineHeight - 1.2) > 1e-9)
			{
				css.Append("html {\n");
				if (style.Font != EpubFont.Serif)
					css.Append("  font-family: ").Append(style.Font == EpubFont.SansSerif ? "\"Helvetica Neue\", Helvetica, Arial, sans-serif" : "Menlo, Consolas, \"Courier New\", monospace").Append(";\n");
				if (style.FontSizePercent != 100)
					css.Append("  font-size: ").Append(style.FontSizePercent.ToString(CultureInfo.InvariantCulture)).Append("%;\n");
				if (Math.Abs(style.LineHeight - 1.2) > 1e-9)
					css.Append("  line-height: ").Append(style.LineHeight.ToString("0.###", CultureInfo.InvariantCulture)).Append(";\n");
				css.Append("}\n");
			}

			if (style.TextAlign == EpubTextAlign.Justify)
				css.Append("p {\n  text-align: justify;\n  hyphens: auto;\n}\n");

			if (style.ParagraphStyle == EpubParagraphStyle.Indented)
				css.Append("p {\n  margin: 0;\n  text-indent: 1.5em;\n}\nh1 + p, hr + p, .synopsis p {\n  text-indent: 0;\n}\n");

			if (style.ChapterHeadingAlign == EpubHeadingAlign.Center)
				css.Append("h1 {\n  text-align: center;\n}\n");

			if (style.SceneBreak != EpubStyle.Default.SceneBreak)
			{
				if (style.SceneBreak.Length == 0)
				{
					css.Append("hr.scene-break {\n  background-color: #1a1a1a;\n  height: 1px;\n}\nhr.scene-break::after {\n  content: none;\n}\n");
				}
				else
				{
					string text = style.SceneBreak.Replace("\r", "").Replace("\n", " ").Replace("\\", "\\\\").Replace("\"", "\\\"");
					css.Append("hr.scene-break::after {\n  content: \"").Append(text).Append("\";\n}\n");
				}
			}

			return css.ToString();
		}

		/// <summary>
		/// Maps a language name (e.g. "English") to a BCP 47 code. Codes pass through unchanged;
		/// unknown names become "und" (undetermined) rather than being mislabeled.
		/// </summary>
		internal static string LanguageCode(string language)
		{
			language = language.Trim();
			if (language.Length == 0) return "und";
			if (language.Length <= 3 || language.Contains('-')) return language.ToLowerInvariant();

			string? match = CultureInfo.GetCultures(CultureTypes.NeutralCultures)
				.FirstOrDefault(c => c.TwoLetterISOLanguageName.Length == 2
					&& (string.Equals(c.EnglishName, language, StringComparison.OrdinalIgnoreCase)
						|| string.Equals(c.NativeName, language, StringComparison.OrdinalIgnoreCase)))
				?.TwoLetterISOLanguageName;

			return match ?? "und";
		}

		/// <summary>Display title for a chapter: trimmed key, "Chapter 0001" tidied to "Chapter 1", blank becomes "Chapter N".</summary>
		internal static string ChapterTitle(string key, int number)
		{
			key = key.Trim();
			if (key.Length == 0) return $"Chapter {number}";

			Match m = Regex.Match(key, @"^Chapter\s*0*(\d+)$", RegexOptions.IgnoreCase);
			return m.Success ? $"Chapter {m.Groups[1].Value}" : key;
		}

		internal static XDocument GenerateTitlePage(EpubStory story)
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			XNamespace epub = "http://www.idpf.org/2007/ops";

			List<XElement> bodyElements = [new(xhtml + "h1", new XAttribute("class", "title"), story.Title)];

			if (story.Series != null)
			{
				bodyElements.Add(
					new XElement(xhtml + "h2", new XAttribute("class", "series"),
						$"{story.Series.Title} - Volume {story.Series.Volume}")
				);
			}

			if (!string.IsNullOrWhiteSpace(story.Description))
			{
				bodyElements.Add(new XElement(xhtml + "div", new XAttribute("class", "synopsis"),
					Regex.Split(story.Description!.Trim(), @"\r?\n\s*\r?\n|\r?\n")
						.Select(paragraph => paragraph.Trim())
						.Where(paragraph => paragraph.Length > 0)
						.Select(paragraph => new XElement(xhtml + "p", paragraph))));
			}

			return new XDocument(
				new XDeclaration("1.0", "utf-8", "yes"),
				new XElement(xhtml + "html",
					new XAttribute(XNamespace.Xmlns + "epub", epub),
					new XAttribute("lang", WriterUtil.LanguageCode(story.Language)),
					new XAttribute(XNamespace.Xml + "lang", WriterUtil.LanguageCode(story.Language)),
					new XElement(xhtml + "head",
						new XElement(xhtml + "meta", new XAttribute("charset", "utf-8")),
						new XElement(xhtml + "title", story.Title),
						new XElement(xhtml + "link",
							new XAttribute("rel", "stylesheet"),
							new XAttribute("type", "text/css"),
							new XAttribute("href", "../styles/style.css")
						)
					),
					new XElement(xhtml + "body", bodyElements)
				)
			);
		}

		internal static XDocument GenerateCoverPage(string coverPath, string language)
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			XNamespace epub = "http://www.idpf.org/2007/ops";

			return new XDocument(
				new XDeclaration("1.0", "utf-8", "yes"),
				new XElement(xhtml + "html",
					new XAttribute(XNamespace.Xmlns + "epub", epub),
					new XAttribute("lang", LanguageCode(language)),
					new XAttribute(XNamespace.Xml + "lang", LanguageCode(language)),
					new XElement(xhtml + "head",
						new XElement(xhtml + "meta", new XAttribute("charset", "utf-8")),
						new XElement(xhtml + "title", "Cover")
					),
					new XElement(xhtml + "body",
						new XElement(xhtml + "img", new XAttribute("src", $"../images/cover{Path.GetExtension(coverPath)}"),
							new XAttribute("alt", "Cover"))
					)
				)
			);
		}


		internal static XDocument GenerateNavXhtml(EpubStory story)
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			XNamespace epub = "http://www.idpf.org/2007/ops";

			return new XDocument(
				new XDeclaration("1.0", "utf-8", "yes"),
				new XElement(xhtml + "html",
					new XAttribute(XNamespace.Xmlns + "epub", epub),
					new XAttribute("lang", LanguageCode(story.Language)),
					new XAttribute(XNamespace.Xml + "lang", LanguageCode(story.Language)),

					new XElement(xhtml + "head",
						new XElement(xhtml + "meta", new XAttribute("charset", "utf-8")),
						new XElement(xhtml + "title", story.Title),
						new XElement(xhtml + "link",
							new XAttribute("rel", "stylesheet"),
							new XAttribute("type", "text/css"),
							new XAttribute("href", "styles/style.css"))
					),

					new XElement(xhtml + "body",
						new XAttribute(epub + "type", "frontmatter"),

						new XElement(xhtml + "nav",
							new XAttribute(epub + "type", "toc"),
							new XAttribute("role", "doc-toc"),
							new XAttribute("id", "toc"),
							new XElement(xhtml + "h1",
								new XAttribute("id", "toc-title"),
								story.Title
							),
							new XElement(xhtml + "ol",
								new XAttribute("class", "toc"),
								GenerateNavLinks(story)
							)
						),

						new XElement(xhtml + "nav",
							new XAttribute(epub + "type", "landmarks"),
							new XAttribute("id", "landmarks"),
							new XAttribute("hidden", "hidden"),
							new XElement(xhtml + "ol",
								string.IsNullOrEmpty(story.CoverPath) ? null : new XElement(xhtml + "li",
									new XElement(xhtml + "a",
										new XAttribute("href", "text/cover-page.xhtml"),
										new XAttribute(epub + "type", "cover"),
										"Cover"
									)
								),
								new XElement(xhtml + "li",
									new XElement(xhtml + "a",
										new XAttribute("href", "text/title-page.xhtml"),
										new XAttribute(epub + "type", "titlepage"),
										"Title Page"
									)
								),
								new XElement(xhtml + "li",
									new XElement(xhtml + "a",
										new XAttribute("href", "#toc"),
										new XAttribute(epub + "type", "toc"),
										"Table of Contents"
									)
								),
								story.Chapters.Count == 0 ? null : new XElement(xhtml + "li",
									new XElement(xhtml + "a",
										new XAttribute("href", "text/chapter-0001.xhtml"),
										new XAttribute(epub + "type", "bodymatter"),
										"Start of Story"
									)
								)
							)
						)
					)
				)
			);
		}

		internal static IEnumerable<XElement> GenerateNavLinks(EpubStory story)
		{
			XNamespace xhtml = "http://www.w3.org/1999/xhtml";
			
			for (int i = 0; i < story.Chapters.Count; i++)
			{
				string href = $"text/chapter-{i + 1:0000}.xhtml#chapter-{i + 1:0000}";

				KeyValuePair<string, string> chapterInfo = story.Chapters.ElementAt(i);
				
				yield return new XElement(xhtml + "li",
					new XAttribute("id", $"toc-li-{i + 1}"),
					new XElement(xhtml + "a",
						new XAttribute("href", href),
						ChapterTitle(chapterInfo.Key, i + 1)
					)
				);
			}
		}


		internal static XDocument GenerateContentOpf(EpubStory story, IReadOnlyList<string> images)
		{
			string opfNamespace = "http://www.idpf.org/2007/opf";
			XNamespace dc = "http://purl.org/dc/elements/1.1/";

			XNamespace opf = opfNamespace;
			string modified = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

			List<XElement> metadata =
			[
				new(dc + "title", story.Title),
				new(dc + "language", LanguageCode(story.Language)),
				new(dc + "creator", story.Author),
				new(dc + "identifier", new XAttribute("id", "bookid"), $"urn:uuid:{story.Identifier}"),
				new(dc + "date", modified),
				new(opf + "meta", new XAttribute("property", "dcterms:modified"), modified)
			];

			metadata.AddRange(story.Tags.Select(tag => new XElement(dc + "subject", tag)));

			if (!string.IsNullOrWhiteSpace(story.Description))
				metadata.Add(new XElement(dc + "description", story.Description!.Trim()));

			if (story.Series != null)
			{
				metadata.Add(new XElement(opf + "meta", new XAttribute("property", "belongs-to-collection"), new XAttribute("id", "series"), story.Series.Title));
				metadata.Add(new XElement(opf + "meta", new XAttribute("refines", "#series"), new XAttribute("property", "collection-type"), "series"));
				metadata.Add(new XElement(opf + "meta", new XAttribute("refines", "#series"), new XAttribute("property", "group-position"), story.Series.Volume.ToString()));
			}

			if (!string.IsNullOrEmpty(story.CoverPath))
			{
				// Legacy EPUB 2 cover hint, still read by Kindle/Calibre alongside the EPUB 3 cover-image property.
				metadata.Add(new XElement(opf + "meta", new XAttribute("name", "cover"), new XAttribute("content", "cover-image")));
			}

			return new XDocument(
				new XElement(XName.Get("package", opfNamespace),
					new XAttribute("version", "3.0"),
					new XAttribute("unique-identifier", "bookid"),
					new XElement(XName.Get("metadata", opfNamespace), metadata),
					new XElement(XName.Get("manifest", opfNamespace), GenerateManifestItems(story, opfNamespace, images)),
					new XElement(XName.Get("spine", opfNamespace), new XAttribute("toc", "ncx"), GenerateSpineItems(story, opfNamespace))
				)
			);
		}


		internal static XDocument GenerateTocNcx(EpubStory story)
		{
			string ncxNamespace = "http://www.daisy.org/z3986/2005/ncx/";

			return new XDocument(
				new XElement(XName.Get("ncx", ncxNamespace),
					new XAttribute("version", "2005-1"),
					new XElement(XName.Get("head", ncxNamespace),
						new XElement(XName.Get("meta", ncxNamespace),
							new XAttribute("name", "dtb:uid"),
							new XAttribute("content", $"urn:uuid:{story.Identifier}")),
						new XElement(XName.Get("meta", ncxNamespace),
							new XAttribute("name", "dtb:depth"),
							new XAttribute("content", "1")),
						new XElement(XName.Get("meta", ncxNamespace),
							new XAttribute("name", "dtb:totalPageCount"),
							new XAttribute("content", "0")),
						new XElement(XName.Get("meta", ncxNamespace),
							new XAttribute("name", "dtb:maxPageNumber"),
							new XAttribute("content", "0"))
					),
					new XElement(XName.Get("docTitle", ncxNamespace),
						new XElement(XName.Get("text", ncxNamespace), story.Title)
					),
					new XElement(XName.Get("navMap", ncxNamespace),
						GenerateNavPoints(story, ncxNamespace)
					)
				)
			);
		}

		internal static IEnumerable<XElement> GenerateManifestItems(EpubStory story, string opfNamespace, IReadOnlyList<string> images)
		{
			yield return new XElement(XName.Get("item", opfNamespace),
				new XAttribute("id", "ncx"),
				new XAttribute("href", "toc.ncx"),
				new XAttribute("media-type", "application/x-dtbncx+xml")
			);

			yield return new XElement(XName.Get("item", opfNamespace),
				new XAttribute("id", "nav"),
				new XAttribute("href", "nav.xhtml"),
				new XAttribute("media-type", "application/xhtml+xml"),
				new XAttribute("properties", "nav")
			);

			yield return new XElement(XName.Get("item", opfNamespace),
				new XAttribute("id", "style"),
				new XAttribute("href", "styles/style.css"),
				new XAttribute("media-type", "text/css")
			);

			yield return new XElement(XName.Get("item", opfNamespace),
				new XAttribute("id", "title-page"),
				new XAttribute("href", "text/title-page.xhtml"),
				new XAttribute("media-type", "application/xhtml+xml")
			);

			if (!string.IsNullOrEmpty(story.CoverPath))
			{
				yield return new XElement(XName.Get("item", opfNamespace),
					new XAttribute("id", "cover-image"),
					new XAttribute("href", "images/cover" + Path.GetExtension(story.CoverPath)),
					new XAttribute("media-type", ImageMediaType(story.CoverPath!)),
					new XAttribute("properties", "cover-image")
				);

				yield return new XElement(XName.Get("item", opfNamespace),
					new XAttribute("id", "cover-page"),
					new XAttribute("href", "text/cover-page.xhtml"),
					new XAttribute("media-type", "application/xhtml+xml")
				);
			}

			foreach (string image in images)
			{
				yield return new XElement(XName.Get("item", opfNamespace),
					new XAttribute("id", Path.GetFileNameWithoutExtension(image)),
					new XAttribute("href", "images/" + image),
					new XAttribute("media-type", ImageMediaType(image))
				);
			}

			for (int i = 0; i < story.Chapters.Count; i++)
			{
				string id = $"chapter-{i + 1:0000}";
				string href = $"text/chapter-{i + 1:0000}.xhtml";

				yield return new XElement(XName.Get("item", opfNamespace),
					new XAttribute("id", id),
					new XAttribute("href", href),
					new XAttribute("media-type", "application/xhtml+xml")
				);
			}
		}

		internal static string ImageMediaType(string path) => Path.GetExtension(path).TrimStart('.').ToLowerInvariant() switch
		{
			"jpg" or "jpeg" => "image/jpeg",
			"svg" => "image/svg+xml",
			var ext => $"image/{ext}"
		};

		internal static IEnumerable<XElement> GenerateSpineItems(EpubStory story, string opfNamespace)
		{
			if (!string.IsNullOrEmpty(story.CoverPath))
			{
				yield return new XElement(XName.Get("itemref", opfNamespace), new XAttribute("idref", "cover-page"));
			}

			yield return new XElement(XName.Get("itemref", opfNamespace),
				new XAttribute("idref", "title-page"),
				new XAttribute("linear", "yes")
			);

			yield return new XElement(XName.Get("itemref", opfNamespace),
				new XAttribute("idref", "nav")
			);

			for (int i = 0; i < story.Chapters.Count; i++)
			{
				string idref = $"chapter-{i + 1:0000}";
				yield return new XElement(XName.Get("itemref", opfNamespace),
					new XAttribute("idref", idref)
				);
			}
		}

		internal static IEnumerable<XElement> GenerateNavPoints(EpubStory story, string ncxNamespace)
		{
			yield return new XElement(XName.Get("navPoint", ncxNamespace),
				new XAttribute("id", "navPoint-0"),
				new XElement(XName.Get("navLabel", ncxNamespace),
					new XElement(XName.Get("text", ncxNamespace), story.Title)
				),
				new XElement(XName.Get("content", ncxNamespace),
					new XAttribute("src", "text/title-page.xhtml")
				)
			);

			for (int i = 0; i < story.Chapters.Count; i++)
			{
				KeyValuePair<string, string> chapterInfo = story.Chapters.ElementAt(i);

				yield return new XElement(XName.Get("navPoint", ncxNamespace),
					new XAttribute("id", $"navPoint-{i + 1}"),
					new XElement(XName.Get("navLabel", ncxNamespace),
						new XElement(XName.Get("text", ncxNamespace), ChapterTitle(chapterInfo.Key, i + 1))
					),
					new XElement(XName.Get("content", ncxNamespace),
						new XAttribute("src", $"text/chapter-{i + 1:0000}.xhtml")
					)
				);
			}
		}
	}
}
