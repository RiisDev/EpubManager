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

	/// <summary>
	/// Defines methods for logging messages and generating EPUB files from online story or series sources.
	/// </summary>
	/// <remarks>Implementations of this interface provide functionality to create EPUB files from specified story
	/// or series URLs, with options for customizing output and logging progress or errors. Methods are asynchronous and
	/// may perform network and file system operations.</remarks>
	public interface IStoryWriter
	{
		/// <summary>
		/// Writes the specified message to the log output.
		/// </summary>
		/// <param name="message">The message to be logged. Cannot be null.</param>
		public void Log(string message);
		
		/// <summary>
		/// Asynchronously creates an EPUB file from the specified series URL and saves it to the given output directory.
		/// </summary>
		/// <param name="seriesUrl">The URL of the series to download and convert to EPUB. Must be a valid, accessible series URL.</param>
		/// <param name="outputDirectory">The directory where the generated EPUB file will be saved. Must be a valid path with write permissions.</param>
		/// <param name="coverOverwrite">The file path to a custom cover image to use for the EPUB. If empty, the default series cover is used.</param>
		/// <param name="raw">If <see langword="true"/>, downloads the raw, unprocessed content; otherwise, applies formatting and processing.</param>
		/// <param name="startIndex">The zero-based index of the first chapter to include. Must be greater than or equal to zero.</param>
		/// <param name="endIndex">The zero-based index of the last chapter to include. If zero, all chapters from <paramref name="startIndex"/>
		/// onward are included.</param>
		/// <returns>A task that represents the asynchronous operation of creating the EPUB file.</returns>
		public Task CreateEpubFromSeriesAsync(string seriesUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, int startIndex = 0, int endIndex = 0);

		/// <summary>
		/// Asynchronously creates an EPUB file from the specified story URL and saves it to the given output directory.
		/// </summary>
		/// <param name="storyUrl">The URL of the story to download and convert to EPUB format. Must be a valid, accessible URL.</param>
		/// <param name="outputDirectory">The directory where the generated EPUB file will be saved. Must exist and be writable.</param>
		/// <param name="coverOverwrite">The file path to a custom cover image to use for the EPUB. If empty, the default cover is used.</param>
		/// <param name="raw">Specifies whether to use the raw, unprocessed version of the story. If <see langword="true"/>, the EPUB will
		/// contain the original content without formatting; otherwise, formatting is applied.</param>
		/// <returns>A task that represents the asynchronous operation of creating the EPUB file.</returns>
		public Task CreateEpubFromStoryAsync(string storyUrl, string outputDirectory, string coverOverwrite = "", bool raw = false);
	}

	/// <summary>
	/// Provides static methods and constants for generating EPUB files from story data.
	/// </summary>
	/// <remarks>The StoryWriter class is intended for use in scenarios where stories, including chapters and cover
	/// art, need to be packaged into a valid EPUB format. All members are static and thread safety is not guaranteed;
	/// concurrent calls should be managed externally if required.</remarks>
	public static class StoryWriter
	{
		/// <summary>
		/// Represents the full path to the application's temporary directory.
		/// </summary>
		/// <remarks>The directory is located under the application's base directory and is named "temp". This path
		/// can be used for storing temporary files specific to the application's runtime environment.</remarks>
		public static readonly string TempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");

		/// <summary>
		/// Creates an EPUB file from the specified story, including chapters, metadata, and optional cover art.
		/// </summary>
		/// <remarks>The method writes temporary files to a working directory and may download or copy cover art if
		/// specified. If the raw parameter is set to <see langword="true"/>, the final .epub archive is not created, and the
		/// working directory is retained for inspection. The method may overwrite existing files in the output directory.
		/// Logging via the onLog callback can provide detailed progress and error information.</remarks>
		/// <param name="story">The story to be converted into an EPUB file. Must contain chapter information and metadata such as title and,
		/// optionally, cover art.</param>
		/// <param name="onLog">An optional callback that receives log messages during the EPUB creation process. Can be used to monitor progress
		/// or capture errors.</param>
		/// <param name="outputDirectory">The directory in which to place the generated EPUB file and temporary working files. If null or empty, the
		/// application's base directory is used.</param>
		/// <param name="raw">If <see langword="true"/>, the method generates the EPUB file structure without packaging it into a final .epub
		/// archive. If <see langword="false"/>, the method creates the .epub archive and cleans up temporary files.</param>
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
