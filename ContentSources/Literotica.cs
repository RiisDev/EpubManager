using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using LiteroticaApi.Api;
using LiteroticaApi.DataObjects;

namespace EpubManager.ContentSources
{
	public class LiteroticaUrlUtil : IStoryWriterUtil
	{
		internal record Data(
			[property: JsonPropertyName("id")] int? Id,
			[property: JsonPropertyName("user_id")] int? UserId,
			[property: JsonPropertyName("url")] object? Url,
			[property: JsonPropertyName("created_at")] DateTime? CreatedAt,
			[property: JsonPropertyName("modified_at")] DateTime? ModifiedAt,
			[property: JsonPropertyName("title")] string Title,
			[property: JsonPropertyName("language")] int? Language,
			[property: JsonPropertyName("state")] string State,
			[property: JsonPropertyName("description")] object Description,
			[property: JsonPropertyName("view_count")] int? ViewCount,
			[property: JsonPropertyName("comments_count")] int? CommentsCount,
			[property: JsonPropertyName("favorites_count")] int? FavoritesCount,
			[property: JsonPropertyName("lists_count")] int? ListsCount,
			[property: JsonPropertyName("user")] Author User,
			[property: JsonPropertyName("work_count")] int? WorkCount,
			[property: JsonPropertyName("introduction")] object? Introduction
		);
		internal record InternalSeriesRoot(
			[property: JsonPropertyName("success")] bool? Success,
			[property: JsonPropertyName("data")] Data Data
		);

		public async Task<string> GetStorySlugAsync(string url)
		{
			string foundSlug = url;

			if (url.Contains("/s/"))
			{
				Match slugMatch = Regex.Match(url, "(?<=\\/s\\/)[^\\/]+", RegexOptions.Singleline | RegexOptions.Compiled);
				if (slugMatch.Success)
					foundSlug = slugMatch.Value;
			}
			else if (url.Contains("/story/"))
			{
				Match slugMatch = Regex.Match(url, "(?<=\\/story\\/)[^\\/]+", RegexOptions.Singleline | RegexOptions.Compiled);
				if (slugMatch.Success)
					foundSlug = slugMatch.Value;
			}
			else if (url.Contains("/stories/"))
			{
				Match slugMatch = Regex.Match(url, "(?<=\\/stories\\/)[^\\/]+", RegexOptions.Singleline | RegexOptions.Compiled);
				if (slugMatch.Success)
					foundSlug = slugMatch.Value;
			}

			foundSlug = foundSlug.Trim().Trim('/');

			if (string.IsNullOrEmpty(foundSlug) || !await VerifySlugAsync(foundSlug).ConfigureAwait(false))
				throw new Exception($"{foundSlug} is an invalid story.");

			return foundSlug;
		}

		public async Task<string> GetSeriesIdAsync(string url)
		{
			string foundSlug = url;

			if (url.Contains("/se/"))
			{
				Match slugMatch = Regex.Match(url, "(?<=\\/se\\/)[^\\/]+", RegexOptions.Singleline | RegexOptions.Compiled);
				if (slugMatch.Success)
					foundSlug = slugMatch.Value;
			}

			foundSlug = foundSlug.Trim().Trim('/');

			int seriesId = 0;
			bool seriesExist = !string.IsNullOrEmpty(foundSlug) && await VerifySeriesIdAsync(foundSlug);
			bool validId = !string.IsNullOrEmpty(foundSlug) && int.TryParse(foundSlug, out seriesId);

			if (seriesExist && !validId)
			{
				InternalSeriesRoot internalSeries = await Client.Get<InternalSeriesRoot>($"series/{foundSlug}");
				seriesId = internalSeries.Data.Id ?? -1;
			}

			if (seriesId <= 0)
				throw new Exception($"{foundSlug} is an invalid series.");

			foundSlug = seriesId.ToString();

			return foundSlug;
		}

		public async Task<bool> VerifySeriesIdAsync(string? seriesId)
		{
			HttpResponseMessage? responseMessage = await Client.HttpClientInstance.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"https://literotica.com/api/3/series/{seriesId}"));
			return responseMessage.IsSuccessStatusCode;
		}

		public async Task<bool> VerifySlugAsync(string? slug)
		{
			HttpResponseMessage? responseMessage = await Client.HttpClientInstance.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"https://literotica.com/api/3/stories/{slug}"));
			return responseMessage.IsSuccessStatusCode;
		}
	}

	public class Literotica : IStoryWriter
	{
		public static readonly LiteroticaUrlUtil UrlUtil = new ();

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
		public async Task CreateEpubFromSeriesAsync(string seriesUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, int startIndex = 0, int endIndex = 0)
		{
			Log("[CreateEpubFromSeries] Verifying series url...");
			string seriesSlug = await UrlUtil.GetSeriesIdAsync(seriesUrl);

			Log("[CreateEpubFromSeries] Fetching series info from api...");
			Series? seriesData = await SeriesApi.GetSeriesInfoAsync(seriesSlug);

			if (seriesData is null || seriesData.Parts.Count == 0 || !seriesData.UserId.HasValue)
				throw new Exception("No stories found in the specified series.");

			Author? author = await AuthorsApi.GetAuthorByIdAsync(seriesData.UserId.Value);

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
					Cover cover = await SeriesApi.GetSeriesCoverAsync(seriesSlug);
					coverPath = cover.Data.Mobile.X1.FilePath;
				}
				else coverPath = coverOverwrite;
			}
			catch
			{
				coverPath = "";
			}

			Log($"[CreateEpubFromSeries] {(string.IsNullOrEmpty(coverPath) ? "Found no cover art." : "Cover art found.")}");

			// Fetch content for each story in the series.
			Dictionary<string, string> chapters = [];

			for (int storyIndex = startIndex; storyIndex < seriesData.Parts.Count; storyIndex++)
			{
				if (storyIndex > endIndex) break;

				Part story = seriesData.Parts[storyIndex];
				Log($"[CreateEpubFromSeries] Fetching content: {story.Title}");
				string[] pages = await StoryApi.GetStoryContentAsync(story.Url);
				chapters.Add(story.Title, string.Join(Environment.NewLine + Environment.NewLine, pages));
			}

			// Prepare temporary directory for writing chapter files.
			string storyLocation = Path.Combine(StoryWriter.TempDir, StoryWriterUtil.ToSafeFileName(seriesData.Title), "Chapters");
			Directory.CreateDirectory(storyLocation);

			Log("[CreateEpubFromSeries] Writing chapters to file...");
			foreach (KeyValuePair<string, string> chapter in chapters)
			{
				string chapterFilePath = Path.Combine(storyLocation, $"{StoryWriterUtil.ToSafeFileName(chapter.Key)}.txt");
				File.WriteAllText(chapterFilePath, chapter.Value);
			}

			Dictionary<string, string> chapterFiles = [];
			string[] files = Directory.GetFiles(storyLocation);
			for (int fileIndex = 0; fileIndex < files.Length; fileIndex++) 
				chapterFiles.Add($"Chapter {fileIndex:0000}", files[fileIndex]);

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
			);

			StoryWriter.CreateEpub(epubStory, Log, outputDirectory, raw);
		}

		/// <summary>
		/// Generates an EPUB file from a single Literotica story.
		/// </summary>
		/// <param name="storyUrl">The URL of the story to convert.</param>
		/// <param name="outputDirectory">The directory where the EPUB file should be created.</param>
		/// <param name="coverOverwrite">Forcefully set cover art for Epub</param>
		/// <param name="raw">If you don't want it to output .epub but instead the raw formatting.</param>
		/// <exception cref="Exception">Thrown if the story or author information cannot be retrieved.</exception>
		public async Task CreateEpubFromStoryAsync(string storyUrl, string outputDirectory, string coverOverwrite = "", bool raw = false)
		{
			Log("[CreateEpubFromStory] Verifying story url...");
			string storySlug = await UrlUtil.GetStorySlugAsync(storyUrl).ConfigureAwait(false);

			Log("[CreateEpubFromStory] Fetching story info from api...");
			StoryInfo? storyData = await StoryApi.GetStoryInfoAsync(storySlug);

			if (storyData is null || string.IsNullOrEmpty(storyData.Submission.Authorname))
				throw new Exception("The specified story could not be found or contains no valid content.");

			Log("[CreateEpubFromStory] Fetching story content...");

			string[] storyText = await StoryApi.GetStoryContentAsync(storyData.Submission.Url);
			
			// Prepare directory for temporary text file storage.
			string storyLocation = Path.Combine(StoryWriter.TempDir, StoryWriterUtil.ToSafeFileName(storyData.Submission.Title), "Chapters");
			Directory.CreateDirectory(storyLocation);

			Log("[CreateEpubFromStory] Writing story to file...");
			string chapterFilePath = Path.Combine(storyLocation, $"{StoryWriterUtil.ToSafeFileName(storyData.Submission.Title)}.txt");
			File.WriteAllText(chapterFilePath, string.Join("\n\n", storyText));

			Dictionary<string, string> chapterFiles = [];
			string[] files = Directory.GetFiles(storyLocation);
			for (int fileIndex = 0; fileIndex < files.Length; fileIndex++)
				chapterFiles.Add($"Chapter {fileIndex:0000}", files[fileIndex]);

			Log("[CreateEpubFromStory] Generating Epub...");
			// Construct the EPUB metadata and generate the final file.
			EpubStory epubStory = new(
				Title: storyData.Submission.Title,
				Language: "English",
				CoverPath: string.IsNullOrEmpty(coverOverwrite) ? null : coverOverwrite,
				Author: storyData.Submission.Author.Username,
				Series: new EpubSeries(storyData.Submission.Title, 1),
				Tags: storyData.Submission.Tags.Select(tag => tag.TagText.ToString()).ToArray(),
				Chapters: chapterFiles
			);

			StoryWriter.CreateEpub(epubStory, Log, outputDirectory, raw);
		}
	}
}
