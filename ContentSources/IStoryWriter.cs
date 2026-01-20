using EpubManager.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace EpubManager.ContentSources
{
	/// <summary>
	/// Represents metadata about a story series, including its title and volume number.
	/// </summary>
	/// <param name="Title">The title of the series.</param>
	/// <param name="Volume">The volume number of the story within the series.</param>
	public record EpubSeries(string Title, int Volume);

	/// <summary>
	/// Represents a story prepared for EPUB generation, containing its metadata and chapters.
	/// </summary>
	/// <param name="Title">The title of the story.</param>
	/// <param name="Language">The language in which the story is written (e.g., "English").</param>
	/// <param name="Author">The author’s name or pseudonym.</param>
	/// <param name="Series">Optional series metadata if the story belongs to one.</param>
	/// <param name="Tags">An array of associated tags or genres describing the story.</param>
	/// <param name="Chapters">A collection of file paths to the chapter text files for the story.</param>
	/// <param name="CoverPath">Optional file path to the story’s cover image.</param>
	/// <param name="CoverPath">Optional file path to the story’s stylesheet.</param>
	public record EpubStory(
		string Title,
		string Language,
		string Author,
		EpubSeries? Series,
		string[] Tags,
		IReadOnlyDictionary<string, string> Chapters,
		string? CoverPath = null,
		string? StyleSheet = null)
	{
		/// <summary>
		/// Gets a unique identifier for this story instance. 
		/// Used internally for metadata consistency and manifest references.
		/// </summary>
		public object Identifier { get; set; } = Guid.NewGuid();
	}

	public interface IStoryWriter
	{
		public void Log(string message);
		
		public Task CreateEpubFromSeriesAsync(string seriesUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, int startIndex = 0, int endIndex = 0);

		public Task CreateEpubFromStoryAsync(string storyUrl, string outputDirectory, string coverOverwrite = "", bool raw = false);
	}

	public static class StoryWriter
	{
		public static readonly string TempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");

		public static void CreateEpub(EpubStory story, Action<string>? onLog = null, string? outputDirectory = null, bool raw = false)
		{
			string baseDirectory = string.IsNullOrEmpty(outputDirectory)
				? AppDomain.CurrentDomain.BaseDirectory
				: outputDirectory!;
			string storyDirectory = Path.Combine(baseDirectory, UrlUtil.ToSafeFileName(story.Title));

			onLog?.Invoke("[CreateEpub] Writing EPUB base files...");
			// Extract the default EPUB manifest structure from embedded resources.
			ResourceExtractor.WriteEpubManifest(storyDirectory);

			// Generate core EPUB metadata and navigation files.
			XDocument tocNcx = WriterUtil.GenerateTocNcx(story);
			string tocNcxPath = Path.Combine(storyDirectory, "EPUB", "toc.ncx");
			tocNcx.Save(tocNcxPath);

			XDocument contentOpf = WriterUtil.GenerateContentOpf(story);
			string contentOpfPath = Path.Combine(storyDirectory, "EPUB", "content.opf");
			contentOpf.Save(contentOpfPath);

			XDocument navXhtml = WriterUtil.GenerateNavXhtml(story);
			string navXhtmlPath = Path.Combine(storyDirectory, "EPUB", "nav.xhtml");
			navXhtml.Save(navXhtmlPath);

			// Generate title page.
			XDocument titlePageXhtml = WriterUtil.GenerateTitlePage(story);
			string titlePagePath = Path.Combine(storyDirectory, "EPUB", "text", "title_page.xhtml");
			titlePageXhtml.Save(titlePagePath);

			onLog?.Invoke("[CreateEpub] Checking for cover art...");

			if (!string.IsNullOrEmpty(story.CoverPath))
			{
				string coverDestDir = Path.Combine(storyDirectory, "EPUB", "images");
				Directory.CreateDirectory(coverDestDir);
				string coverFileName = "cover" + Path.GetExtension(story.CoverPath);
				string coverDestPath = Path.Combine(coverDestDir, coverFileName);

				if (story.CoverPath!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						using HttpClient httpClient = new();
						HttpResponseMessage response = httpClient.GetAsync(story.CoverPath).Result;

						if (response.IsSuccessStatusCode)
						{
							using FileStream fileStream = new(
								coverDestPath,
								FileMode.Create,
								FileAccess.Write,
								FileShare.None,
								bufferSize: 8192,
								useAsync: true);

							response.Content.CopyToAsync(fileStream).Wait();
							onLog?.Invoke($"[CreateEpub] Downloaded cover from URL: {story.CoverPath}");
						}
						else
						{
							onLog?.Invoke($"[CreateEpub] Failed to download cover (HTTP {response.StatusCode}) from {story.CoverPath}");
							return;
						}
					}
					catch (Exception ex)
					{
						onLog?.Invoke($"[CreateEpub] Error downloading cover: {ex.Message}");
						return;
					}
				}
				else
				{
					if (!File.Exists(story.CoverPath))
					{
						onLog?.Invoke("[CreateEpub] CoverArt set, but file not found.");
						return;
					}

					try
					{
						File.Copy(story.CoverPath, coverDestPath, overwrite: true);
						onLog?.Invoke($"[CreateEpub] Copied cover from {story.CoverPath}");
					}
					catch (Exception ex)
					{
						onLog?.Invoke($"[CreateEpub] Error copying cover file: {ex.Message}");
						return;
					}
				}

				try
				{
					XDocument coverXhtml = WriterUtil.GenerateCoverPage(story);
					string coverTextDir = Path.Combine(storyDirectory, "EPUB", "text");
					Directory.CreateDirectory(coverTextDir);
					string coverXhtmlPath = Path.Combine(coverTextDir, "cover.xhtml");
					coverXhtml.Save(coverXhtmlPath);
					onLog?.Invoke($"[CreateEpub] Generated cover.xhtml at {coverXhtmlPath}");
				}
				catch (Exception ex)
				{
					onLog?.Invoke($"[CreateEpub] Error generating cover.xhtml: {ex.Message}");
				}
			}


			onLog?.Invoke("[CreateEpub] Writing chapters to file...");

			// Write each chapter file into the EPUB structure.
			int chapterIndex = 0;
			foreach (KeyValuePair<string, string> keyValue in story.Chapters)
			{
				chapterIndex++;
				string chapterPath = keyValue.Value;
				string chapterContent = File.ReadAllText(chapterPath);

				chapterContent = chapterContent.RemoveControlCharacters();

				XDocument chapterDoc = WriterUtil.GenerateChapterXhtml(
					Path.GetFileNameWithoutExtension(chapterPath),
					chapterContent,
					chapterIndex
				);

				string chapterOutputPath = Path.Combine(storyDirectory, "EPUB", "text", $"ch{chapterIndex + 1:0000}.xhtml");
				chapterDoc.Save(chapterOutputPath);

				onLog?.Invoke($"[CreateEpub] Writing ch{chapterIndex + 1:0000}.xhtml to file...");

				WriterUtil.CleanChapterFile(chapterOutputPath, onLog);
			}
			
			// Package all files into a final EPUB zip archive.);
			string epubFilePath = Path.Combine(baseDirectory, $"{UrlUtil.ToSafeFileName(story.Title)}.epub");
			onLog?.Invoke($"[CreateEpub] Creating final EPUB file {epubFilePath}");
			if (File.Exists(epubFilePath)) File.Delete(epubFilePath);

			if (raw)
			{
				onLog?.Invoke("[CreateEpub] Raw output requested, skipping .epub creation.");
				goto Clean;
			}
			ZipFile.CreateFromDirectory(storyDirectory, epubFilePath, CompressionLevel.NoCompression, false);

			onLog?.Invoke("[CreateEpub] EPUB creation complete, cleaning up.");
			// Clean up temporary working directory.
			Directory.Delete(storyDirectory, true);

		Clean:
			if (!Directory.Exists(TempDir)) return;
			try { Directory.Delete(TempDir, true); } catch {/**/}
		}
	}
}
