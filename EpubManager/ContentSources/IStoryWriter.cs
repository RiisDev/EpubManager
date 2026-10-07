using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
	/// <param name="Description">Optional synopsis, shown on the title page and stored as the book description.</param>
	/// <param name="Style">Optional appearance; <see langword="null"/> uses <see cref="EpubStyle.Default"/>.</param>
	public record EpubStory(
		string Title,
		string Language,
		string Author,
		EpubSeries? Series,
		string[] Tags,
		IReadOnlyDictionary<string, string> Chapters,
		string? CoverPath = null,
		string? Description = null,
		EpubStyle? Style = null)
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
		/// <param name="options">Optional language, description and style overrides. <see langword="null"/> keeps the writer's defaults.</param>
		/// <param name="cancellationToken">Cancels the operation, including in-flight downloads.</param>
		/// <returns>A task that represents the asynchronous operation of creating the EPUB file.</returns>
		public Task CreateEpubFromSeriesAsync(string seriesUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, int startIndex = 0, int endIndex = 0, EpubOptions? options = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously creates an EPUB file from the specified story URL and saves it to the given output directory.
		/// </summary>
		/// <param name="storyUrl">The URL of the story to download and convert to EPUB format. Must be a valid, accessible URL.</param>
		/// <param name="outputDirectory">The directory where the generated EPUB file will be saved. Must exist and be writable.</param>
		/// <param name="coverOverwrite">The file path to a custom cover image to use for the EPUB. If empty, the default cover is used.</param>
		/// <param name="raw">Specifies whether to use the raw, unprocessed version of the story. If <see langword="true"/>, the EPUB will
		/// contain the original content without formatting; otherwise, formatting is applied.</param>
		/// <param name="options">Optional language, description and style overrides. <see langword="null"/> keeps the writer's defaults.</param>
		/// <param name="cancellationToken">Cancels the operation, including in-flight downloads.</param>
		/// <returns>A task that represents the asynchronous operation of creating the EPUB file.</returns>
		public Task CreateEpubFromStoryAsync(string storyUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, EpubOptions? options = null, CancellationToken cancellationToken = default);
	}
}
