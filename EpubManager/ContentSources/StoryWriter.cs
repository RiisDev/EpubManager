using EpubManager.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EpubManager.ContentSources
{
	/// <summary>
	/// Packages an <see cref="EpubStory"/> into a valid EPUB 3 file.
	/// </summary>
	/// <remarks>Each call builds in its own temporary folder, so concurrent calls are safe. Cover and inline
	/// image problems never abort the build: the item is skipped and a warning is logged.</remarks>
	public static class StoryWriter
	{
		private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".svg"];

		/// <summary>
		/// Creates a new, empty, uniquely named working folder under the system temp path.
		/// The caller is responsible for deleting it.
		/// </summary>
		public static string NewTempDirectory()
		{
			string path = Path.Combine(Path.GetTempPath(), "EpubManager", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(path);
			return path;
		}

		/// <summary>
		/// Derives a stable identifier (name-based UUID, version 5) from a seed such as a source URL, so rebuilding
		/// the same story yields the same book identity and e-readers keep progress instead of adding a duplicate.
		/// </summary>
		public static Guid StableId(string seed)
		{
			// RFC 4122 URL namespace, big-endian.
			byte[] ns = [0x6b, 0xa7, 0xb8, 0x11, 0x9d, 0xad, 0x11, 0xd1, 0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8];
			byte[] name = Encoding.UTF8.GetBytes(seed);
			byte[] data = ns.Concat(name).ToArray();

			byte[] hash;
			using (SHA1 sha1 = SHA1.Create()) hash = sha1.ComputeHash(data);

			byte[] b = new byte[16];
			Array.Copy(hash, b, 16);
			b[6] = (byte)((b[6] & 0x0F) | 0x50);
			b[8] = (byte)((b[8] & 0x3F) | 0x80);

			// Guid's first three fields are little-endian.
			Array.Reverse(b, 0, 4);
			Array.Reverse(b, 4, 2);
			Array.Reverse(b, 6, 2);
			return new Guid(b);
		}

		/// <summary>Synchronous wrapper for <see cref="CreateEpubAsync"/>.</summary>
		public static void CreateEpub(EpubStory story, Action<string>? onLog = null, string? outputDirectory = null, bool raw = false) =>
			CreateEpubAsync(story, onLog, outputDirectory, raw).GetAwaiter().GetResult();

		/// <summary>
		/// Builds an EPUB from the story and writes it to the output directory.
		/// </summary>
		/// <param name="story">The story: metadata, chapter files (title to path of an HTML-ish text file) and optional cover.</param>
		/// <param name="onLog">Optional progress and warning callback. Warnings go to <see cref="Console.Error"/> when this is null.</param>
		/// <param name="outputDirectory">Where to put the result. Defaults to the application's base directory.</param>
		/// <param name="raw">If true, writes the unzipped EPUB folder (<c>&lt;title&gt;/</c>) instead of a <c>.epub</c> file.</param>
		/// <param name="cancellationToken">Cancels the build, including cover and image downloads.</param>
		public static async Task CreateEpubAsync(EpubStory story, Action<string>? onLog = null, string? outputDirectory = null, bool raw = false, CancellationToken cancellationToken = default)
		{
			story.Style?.Validate();

			string baseDirectory = string.IsNullOrEmpty(outputDirectory) ? AppDomain.CurrentDomain.BaseDirectory : outputDirectory!;
			Directory.CreateDirectory(baseDirectory);
			string safeTitle = UrlUtil.ToSafeFileName(story.Title);

			string work = NewTempDirectory();
			try
			{
				onLog?.Invoke("[CreateEpub] Writing EPUB base files...");
				ResourceExtractor.WriteEpubManifest(work);

				string styleOverrides = WriterUtil.GenerateStyleOverrides(story.Style);
				if (styleOverrides.Length > 0)
					File.AppendAllText(Path.Combine(work, "EPUB", "styles", "style.css"), styleOverrides, new UTF8Encoding(false));

				if (WriterUtil.LanguageCode(story.Language) == "und" && !string.IsNullOrWhiteSpace(story.Language))
					(onLog ?? Console.Error.WriteLine).Invoke($"[CreateEpub] Warning: unrecognized language '{story.Language}'; using 'und'. Use a BCP 47 code such as 'en'.");

				// Cover first: if it fails we continue without one, and the metadata below must not reference it.
				onLog?.Invoke("[CreateEpub] Checking for cover art...");
				story = story with { CoverPath = await TryAddCoverAsync(story.CoverPath, story.Language, work, onLog, cancellationToken).ConfigureAwait(false) };

				onLog?.Invoke("[CreateEpub] Writing chapters to file...");
				string language = WriterUtil.LanguageCode(story.Language);
				Dictionary<string, string> images = new(StringComparer.Ordinal);
				int chapterIndex = 0;

				foreach (KeyValuePair<string, string> chapter in story.Chapters)
				{
					cancellationToken.ThrowIfCancellationRequested();
					chapterIndex++;
					string chapterFile = $"chapter-{chapterIndex:0000}.xhtml";

					string markup = WriterUtil.CleanMarkup(File.ReadAllText(chapter.Value), chapterFile, onLog);
					markup = await EmbedImagesAsync(markup, work, images, onLog, cancellationToken).ConfigureAwait(false);

					WriterUtil.GenerateChapterXhtml(WriterUtil.ChapterTitle(chapter.Key, chapterIndex), markup, chapterIndex, language)
						.Save(Path.Combine(work, "EPUB", "text", chapterFile));
					onLog?.Invoke($"[CreateEpub] Wrote {chapterFile}");
				}

				// Metadata last: the manifest needs to know about the images found in the chapters.
				WriterUtil.GenerateTocNcx(story).Save(Path.Combine(work, "EPUB", "toc.ncx"));
				WriterUtil.GenerateContentOpf(story, images.Values.Distinct().ToList()).Save(Path.Combine(work, "EPUB", "content.opf"));
				WriterUtil.GenerateNavXhtml(story).Save(Path.Combine(work, "EPUB", "nav.xhtml"));
				WriterUtil.GenerateTitlePage(story).Save(Path.Combine(work, "EPUB", "text", "title-page.xhtml"));

				if (raw)
				{
					string target = Path.Combine(baseDirectory, safeTitle);
					onLog?.Invoke($"[CreateEpub] Raw output requested, writing folder {target}");
					if (Directory.Exists(target)) Directory.Delete(target, true);
					CopyDirectory(work, target);
					return;
				}

				string epubPath = Path.Combine(baseDirectory, $"{safeTitle}.epub");
				onLog?.Invoke($"[CreateEpub] Creating final EPUB file {epubPath}");
				WriteEpubZip(work, epubPath);
				onLog?.Invoke("[CreateEpub] EPUB creation complete.");
			}
			finally
			{
				try { Directory.Delete(work, true); } catch { /* best effort */ }
			}
		}

		/// <summary>
		/// Zips the folder as an EPUB container: <c>mimetype</c> first and stored, everything else compressed,
		/// in a deterministic order. Written to a temp file and moved into place so a failure never leaves a broken .epub.
		/// </summary>
		private static void WriteEpubZip(string directory, string epubPath)
		{
			string partial = epubPath + ".tmp";
			try
			{
				using (FileStream stream = new(partial, FileMode.Create, FileAccess.Write))
				using (ZipArchive zip = new(stream, ZipArchiveMode.Create))
				{
					zip.CreateEntryFromFile(Path.Combine(directory, "mimetype"), "mimetype", CompressionLevel.NoCompression);

					foreach (string relative in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
						.Select(f => f.Substring(directory.Length).TrimStart('\\', '/').Replace('\\', '/'))
						.Where(f => f != "mimetype")
						.OrderBy(f => f, StringComparer.Ordinal))
					{
						zip.CreateEntryFromFile(Path.Combine(directory, relative), relative, CompressionLevel.Optimal);
					}
				}

				if (File.Exists(epubPath)) File.Delete(epubPath);
				File.Move(partial, epubPath);
			}
			catch
			{
				try { File.Delete(partial); } catch { /* best effort */ }
				throw;
			}
		}

		private static void CopyDirectory(string source, string destination)
		{
			Directory.CreateDirectory(destination);
			foreach (string file in Directory.GetFiles(source))
				File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
			foreach (string directory in Directory.GetDirectories(source))
				CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
		}

		private static string ExtensionOf(string location, bool isUrl)
		{
			string extension = Path.GetExtension(isUrl ? new Uri(location).AbsolutePath : location).ToLowerInvariant();
			return isUrl && extension.Length == 0 ? ".jpg" : extension;
		}

		/// <summary>
		/// Copies or downloads the cover into the EPUB folder and writes cover-page.xhtml.
		/// Returns the cover's final path, or <see langword="null"/> (after a warning) if it could not be added.
		/// </summary>
		private static async Task<string?> TryAddCoverAsync(string? coverPath, string language, string work, Action<string>? onLog, CancellationToken ct)
		{
			if (string.IsNullOrEmpty(coverPath)) return null;

			void Warn(string message) => (onLog ?? Console.Error.WriteLine).Invoke($"[CreateEpub] Warning: {message}; continuing without a cover.");

			string imagesDir = Path.Combine(work, "EPUB", "images");
			string? destPath = null;

			try
			{
				bool isUrl = coverPath!.StartsWith("http", StringComparison.OrdinalIgnoreCase);
				string extension = ExtensionOf(coverPath, isUrl);

				if (Array.IndexOf(ImageExtensions, extension) < 0)
				{
					Warn($"cover type '{extension}' is not a supported EPUB image type (jpg, png, gif, svg)");
					return null;
				}

				destPath = Path.Combine(imagesDir, "cover" + extension);

				if (isUrl)
				{
					using System.Net.Http.HttpResponseMessage response = await EpubManagerClient.GetWithRetryAsync(coverPath, cancellationToken: ct).ConfigureAwait(false);
					if (!response.IsSuccessStatusCode)
					{
						Warn($"cover download failed (HTTP {(int)response.StatusCode}) from {coverPath}");
						return null;
					}
					Directory.CreateDirectory(imagesDir);
					File.WriteAllBytes(destPath, await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
					onLog?.Invoke($"[CreateEpub] Downloaded cover from URL: {coverPath}");
				}
				else
				{
					if (!File.Exists(coverPath))
					{
						Warn($"cover file not found: {coverPath}");
						return null;
					}
					Directory.CreateDirectory(imagesDir);
					File.Copy(coverPath, destPath, overwrite: true);
					onLog?.Invoke($"[CreateEpub] Copied cover from {coverPath}");
				}

				string textDir = Path.Combine(work, "EPUB", "text");
				Directory.CreateDirectory(textDir);
				WriterUtil.GenerateCoverPage(destPath, language).Save(Path.Combine(textDir, "cover-page.xhtml"));
				return destPath;
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				Warn($"could not add cover ({ex.Message})");
				if (destPath != null) try { File.Delete(destPath); } catch { /* best effort */ }
				return null;
			}
		}

		/// <summary>
		/// Downloads every remote <c>&lt;img&gt;</c> in the chapter markup into the EPUB and rewrites its
		/// <c>src</c> to the packaged copy. Images that cannot be embedded are dropped with a warning.
		/// </summary>
		private static async Task<string> EmbedImagesAsync(string markup, string work, Dictionary<string, string> images, Action<string>? onLog, CancellationToken ct)
		{
			if (markup.IndexOf("<img", StringComparison.OrdinalIgnoreCase) < 0) return markup;

			void Warn(string message) => (onLog ?? Console.Error.WriteLine).Invoke($"[CreateEpub] Warning: {message}; image skipped.");

			XElement body = XElement.Parse($"<body>{markup}</body>");
			string imagesDir = Path.Combine(work, "EPUB", "images");

			foreach (XElement img in body.Descendants("img").ToList())
			{
				ct.ThrowIfCancellationRequested();
				string src = ((string?)img.Attribute("src") ?? "").Trim();

				if (!images.TryGetValue(src, out string? fileName))
				{
					fileName = null;
					try
					{
						if (!src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !src.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
							Warn($"unsupported image source '{(src.Length > 60 ? src.Substring(0, 60) + "..." : src)}'");
						else
						{
							string extension = ExtensionOf(src, true);
							if (Array.IndexOf(ImageExtensions, extension) < 0)
								Warn($"image type '{extension}' is not a supported EPUB image type ({src})");
							else
							{
								using System.Net.Http.HttpResponseMessage response = await EpubManagerClient.GetWithRetryAsync(src, cancellationToken: ct).ConfigureAwait(false);
								if (!response.IsSuccessStatusCode) Warn($"image download failed (HTTP {(int)response.StatusCode}) from {src}");
								else
								{
									string candidate = $"image-{images.Count + 1:0000}{extension}";
									Directory.CreateDirectory(imagesDir);
									File.WriteAllBytes(Path.Combine(imagesDir, candidate), await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
									fileName = candidate;
									onLog?.Invoke($"[CreateEpub] Embedded image {src}");
								}
							}
						}
					}
					catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
					catch (Exception ex) { Warn($"could not embed {src} ({ex.Message})"); }

					if (fileName != null) images[src] = fileName;
				}

				if (fileName == null)
				{
					img.Remove();
					continue;
				}

				// Keep only attributes valid in XHTML; remote URLs and obsolete presentational attributes are dropped.
				string alt = (string?)img.Attribute("alt") ?? "";
				string? title = (string?)img.Attribute("title");
				img.RemoveAttributes();
				img.SetAttributeValue("src", "../images/" + fileName);
				img.SetAttributeValue("alt", alt);
				if (!string.IsNullOrEmpty(title)) img.SetAttributeValue("title", title);
			}

			return string.Join("\n", body.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
		}
	}
}
