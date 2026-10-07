using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EpubManager.ContentSources;
using LiteroticaApi.Api;
using LiteroticaApi.DataObjects;

namespace EpubManager.Writers
{
	/// <summary>
	/// Adds base util to Literotica story writer.
	/// </summary>
	public class LiteroticaUrlUtil : IStoryWriterUtil
	{
		/// <summary>
		/// Extracts the story slug from a Literotica story URL.
		/// </summary>
		/// <param name="url">The full URL of the story (e.g., <c>https://www.literotica.com/s/example-story</c>).</param>
		/// <returns>The story slug extracted from the URL (e.g., <c>example-story</c>).</returns>
		/// <exception cref="Exception">Thrown when the URL does not contain a valid or verifiable story slug.</exception>
		/// <remarks>
		/// This method supports multiple URL formats:
		/// <list type="bullet">
		///   <item><description><c>/s/{slug}</c></description></item>
		///   <item><description><c>/story/{slug}</c></description></item>
		///   <item><description><c>/stories/{slug}</c></description></item>
		/// </list>
		/// The slug is validated using <see cref="VerifySlugAsync(string)"/>.
		/// </remarks>
		public Task<string> GetStorySlugAsync(string url) => LiteroticaApi.LiteroticaUrlUtil.GetStorySlugAsync(url);

		/// <summary>
		/// Extracts the numeric series ID from a Literotica series URL.
		/// </summary>
		/// <param name="url">The full URL of the series (e.g., <c>https://www.literotica.com/se/12345</c>).</param>
		/// <returns>The series ID extracted from the URL.</returns>
		/// <exception cref="Exception">Thrown when the URL does not contain a valid or verifiable series ID.</exception>
		/// <remarks>
		/// The series ID is validated using <see cref="VerifySeriesIdAsync(string)"/>.
		/// </remarks>
		public Task<string> GetSeriesIdAsync(string url) => LiteroticaApi.LiteroticaUrlUtil.GetSeriesIdAsync(url);

		/// <summary>
		/// Asynchronously verifies whether the specified series ID exists on Literotica by sending a HEAD request to the API.
		/// </summary>
		/// <remarks>This method does not throw an exception for non-existent series IDs; it returns <see
		/// langword="false"/> if the series is not found or if the request fails. Network errors or invalid IDs may also
		/// result in a <see langword="false"/> return value.</remarks>
		/// <param name="seriesId">The unique identifier of the series to verify. Can be null or empty, but such values will result in a failed
		/// verification.</param>
		/// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the series ID
		/// exists; otherwise, <see langword="false"/>.</returns>
		public Task<bool> VerifySeriesIdAsync(string? seriesId) => LiteroticaApi.LiteroticaUrlUtil.VerifySeriesIdAsync(seriesId);

		/// <summary>
		/// Checks whether a story slug exists on Literotica by sending a HEAD request to the API.
		/// </summary>
		/// <remarks>This method performs a network request to Literotica's API. The operation may fail or return
		/// false if the slug is invalid, does not exist, or if there are network issues.</remarks>
		/// <param name="slug">The slug identifier of the story to verify. Can be null or empty; if so, the method will return false.</param>
		/// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the slug exists;
		/// otherwise, <see langword="false"/>.</returns>
		public Task<bool> VerifySlugAsync(string? slug) => LiteroticaApi.LiteroticaUrlUtil.VerifySlugAsync(slug);
	}

	/// <summary>
	/// Provides functionality for downloading and converting stories or series from Literotica into EPUB format.
	/// </summary>
	/// <remarks>Use the Literotica class to generate EPUB files from either individual stories or entire series
	/// hosted on Literotica. The class offers asynchronous methods for both scenarios, allowing you to specify output
	/// directories, custom cover images, and formatting options. This class is intended for use in applications that
	/// automate the retrieval and conversion of Literotica content for offline reading. All methods require valid
	/// Literotica URLs and may throw exceptions if the content cannot be found or retrieved.</remarks>
	public class Literotica : IStoryWriter
	{
		/// <summary>
		/// Provides a shared instance of the LiteroticaUrlUtil class for working with Literotica URLs.
		/// </summary>
		/// <remarks>This static field can be used to access URL utility methods without creating a new
		/// LiteroticaUrlUtil instance. The instance is thread-safe for concurrent use if LiteroticaUrlUtil itself is
		/// thread-safe.</remarks>
		public static readonly LiteroticaUrlUtil UrlUtil = new ();

		// Courtesy pause between part downloads when fetching a series.
		private static readonly TimeSpan PartDelay = TimeSpan.FromMilliseconds(250);

		/// <summary>
		/// Writes the specified message to both the standard output and the debug output streams.
		/// </summary>
		/// <param name="message">The message to be logged. If <paramref name="message"/> is null, no output is written.</param>
		public void Log(string message)
		{
			Console.WriteLine(message);
			Debug.WriteLine(message);
		}

		/// <summary>
		/// Generates an EPUB file from an entire series on Literotica, including all its parts (stories).
		/// </summary>
		/// <param name="seriesUrl">The URL of the Literotica series to download and convert.</param>
		/// <param name="outputDirectory">The directory where the EPUB file should be created.</param>
		/// <param name="coverOverwrite">Forcefully set cover art for Epub</param>
		/// <param name="raw">If you don't want it to output .epub but instead the raw formatting.</param>
		/// <param name="startIndex">What chapter of the series to start at</param>
		/// <param name="endIndex">What chapter of the series to end at</param>
		/// <exception cref="Exception">Thrown if the series cannot be found or has no valid stories.</exception>
		public async Task CreateEpubFromSeriesAsync(string seriesUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, int startIndex = 0, int endIndex = 0, EpubOptions? options = null, CancellationToken cancellationToken = default)
		{
			Log("[CreateEpubFromSeries] Verifying series url...");
			string seriesSlug = await UrlUtil.GetSeriesIdAsync(seriesUrl).ConfigureAwait(false);

			Log("[CreateEpubFromSeries] Fetching series info from api...");
			Series? seriesData = await SeriesApi.GetSeriesInfoAsync(seriesSlug).ConfigureAwait(false);

			if (seriesData is null || seriesData.Parts.Count == 0 || !seriesData.UserId.HasValue)
				throw new Exception("No stories found in the specified series.");

			Author? author = await AuthorsApi.GetAuthorByIdAsync(seriesData.UserId.Value).ConfigureAwait(false);

			if (author is null || string.IsNullOrEmpty(author.Username))
				throw new Exception("Failed to fetch author.");

			Log($"[CreateEpubFromSeries] Discovered: {seriesData.Title} by {author.Username} with {seriesData.Parts.Count} chapters.");

			Log("[CreateEpubFromSeries] Checking for cover art...");
			// Attempt to retrieve the series cover image.
			string? coverPath;
			try
			{
				if (string.IsNullOrEmpty(coverOverwrite))
				{
					Cover cover = await SeriesApi.GetSeriesCoverAsync(seriesSlug).ConfigureAwait(false);
					coverPath = cover.Data.Mobile.X1.FilePath;
				}
				else coverPath = coverOverwrite;
			}
			catch
			{
				coverPath = "";
			}

			Log($"[CreateEpubFromSeries] {(string.IsNullOrEmpty(coverPath) ? "Found no cover art." : "Cover art found.")}");

			// Fetch content for each story in the series (endIndex is inclusive; 0 means "through the last").
			int firstIndex = Math.Max(startIndex, 0);
			int lastIndex = endIndex > 0 ? Math.Min(endIndex, seriesData.Parts.Count - 1) : seriesData.Parts.Count - 1;

			string workDirectory = StoryWriter.NewTempDirectory();
			try
			{
				// Insertion order is the reading order; the file name prefix keeps titles unique.
				Dictionary<string, string> chapterFiles = [];

				for (int storyIndex = firstIndex; storyIndex <= lastIndex; storyIndex++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					if (storyIndex > firstIndex) await Task.Delay(PartDelay, cancellationToken).ConfigureAwait(false);

					Part story = seriesData.Parts[storyIndex];
					Log($"[CreateEpubFromSeries] Fetching content: {story.Title}");
					string[] pages = await EpubManagerClient.WithRetryAsync(() => StoryApi.GetStoryContentAsync(story.Url), onRetry: Log, cancellationToken: cancellationToken).ConfigureAwait(false);

					string chapterFilePath = Path.Combine(workDirectory, $"{storyIndex + 1:0000}-{StoryWriterUtil.ToSafeFileName(story.Title)}.txt");
					File.WriteAllText(chapterFilePath, string.Join(Environment.NewLine + Environment.NewLine, pages));
					chapterFiles[chapterFiles.ContainsKey(story.Title) ? $"{story.Title} ({storyIndex + 1})" : story.Title] = chapterFilePath;
				}

				Log("[CreateEpubFromSeries] Generating Epub...");
				// Assemble and create the EPUB.
				EpubStory epubStory = new(
					Title: seriesData.Title,
					Language: "English",
					CoverPath: string.IsNullOrEmpty(coverOverwrite) ? string.IsNullOrEmpty(coverPath) ? null : coverPath : coverOverwrite,
					Author: author.Username,
					Series: new EpubSeries(seriesData.Title, 1),
					Tags: [],
					Chapters: chapterFiles
				)
				{
					// Same series and part range always yields the same book identity.
					Identifier = StoryWriter.StableId($"literotica:series:{seriesSlug}:{firstIndex}-{lastIndex}")
				};

				await StoryWriter.CreateEpubAsync(options?.Apply(epubStory) ?? epubStory, Log, outputDirectory, raw, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				try { Directory.Delete(workDirectory, true); } catch { /* best effort */ }
			}
		}

		/// <summary>
		/// Generates an EPUB file from a single Literotica story.
		/// </summary>
		/// <param name="storyUrl">The URL of the story to convert.</param>
		/// <param name="outputDirectory">The directory where the EPUB file should be created.</param>
		/// <param name="coverOverwrite">Forcefully set cover art for Epub</param>
		/// <param name="raw">If you don't want it to output .epub but instead the raw formatting.</param>
		/// <exception cref="Exception">Thrown if the story or author information cannot be retrieved.</exception>
		public async Task CreateEpubFromStoryAsync(string storyUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, EpubOptions? options = null, CancellationToken cancellationToken = default)
		{
			Log("[CreateEpubFromStory] Verifying story url...");
			string storySlug = await UrlUtil.GetStorySlugAsync(storyUrl).ConfigureAwait(false);

			Log("[CreateEpubFromStory] Fetching story info from api...");
			StoryInfo? storyData = await StoryApi.GetStoryInfoAsync(storySlug).ConfigureAwait(false);

			if (storyData is null || string.IsNullOrEmpty(storyData.Submission.Authorname))
				throw new Exception("The specified story could not be found or contains no valid content.");

			Log("[CreateEpubFromStory] Fetching story content...");

			string[] storyText = await EpubManagerClient.WithRetryAsync(() => StoryApi.GetStoryContentAsync(storyData.Submission.Url), onRetry: Log, cancellationToken: cancellationToken).ConfigureAwait(false);

			string workDirectory = StoryWriter.NewTempDirectory();
			try
			{
				Log("[CreateEpubFromStory] Writing story to file...");
				string chapterFilePath = Path.Combine(workDirectory, $"{StoryWriterUtil.ToSafeFileName(storyData.Submission.Title)}.txt");
				File.WriteAllText(chapterFilePath, string.Join("\n\n", storyText));

				Dictionary<string, string> chapterFiles = new() { [storyData.Submission.Title] = chapterFilePath };

				Log("[CreateEpubFromStory] Generating Epub...");
				// Construct the EPUB metadata and generate the final file.
				EpubStory epubStory = new(
					Title: storyData.Submission.Title,
					Language: "English",
					CoverPath: string.IsNullOrEmpty(coverOverwrite) ? null : coverOverwrite,
					Author: storyData.Submission.Author.Username,
					Series: null,
					Tags: storyData.Submission.Tags.Select(tag => tag.TagText.ToString()).ToArray(),
					Chapters: chapterFiles
				)
				{
					Identifier = StoryWriter.StableId($"literotica:story:{storySlug}")
				};

				await StoryWriter.CreateEpubAsync(options?.Apply(epubStory) ?? epubStory, Log, outputDirectory, raw, cancellationToken).ConfigureAwait(false);
			}
			finally
			{
				try { Directory.Delete(workDirectory, true); } catch { /* best effort */ }
			}
		}
	}
}
