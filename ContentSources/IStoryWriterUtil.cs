using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EpubManager.ContentSources
{
	/// <summary>
	/// Provides utility methods for retrieving and validating story-related identifiers, such as slugs and series IDs,
	/// from external sources.
	/// </summary>
	/// <remarks>Implementations of this interface typically interact with remote services or data sources to
	/// extract or verify story information. Methods are asynchronous and may involve network operations. Callers should
	/// handle potential exceptions related to connectivity or invalid input as appropriate.</remarks>
	public interface IStoryWriterUtil
	{
		/// <summary>
		/// Asynchronously retrieves the story slug from the specified story URL.
		/// </summary>
		/// <param name="url">The URL of the story from which to extract the slug. Cannot be null or empty.</param>
		/// <returns>A task that represents the asynchronous operation. The task result contains the slug string extracted from the
		/// story URL, or null if the slug cannot be determined.</returns>
		public Task<string> GetStorySlugAsync(string url);

		/// <summary>
		/// Asynchronously retrieves the unique series identifier from the specified URL.
		/// </summary>
		/// <param name="url">The URL of the series page from which to extract the series identifier. Cannot be null or empty.</param>
		/// <returns>A task that represents the asynchronous operation. The task result contains the series identifier as a string, or
		/// null if the identifier cannot be found.</returns>
		public Task<string> GetSeriesIdAsync(string url);

		/// <summary>
		/// Asynchronously verifies whether the specified slug is valid and available for use.
		/// </summary>
		/// <param name="slug">The slug to validate. Can be null. If null or empty, the method returns <see langword="false"/>.</param>
		/// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the slug is valid
		/// and available; otherwise, <see langword="false"/>.</returns>
		public Task<bool> VerifySlugAsync(string? slug);

		/// <summary>
		/// Asynchronously verifies whether the specified series identifier exists and is valid.
		/// </summary>
		/// <param name="seriesId">The series identifier to verify. Can be null or empty; if so, the method returns <see langword="false"/>.</param>
		/// <returns>A task that represents the asynchronous operation. The task result is <see langword="true"/> if the series
		/// identifier is valid; otherwise, <see langword="false"/>.</returns>
		public Task<bool> VerifySeriesIdAsync(string? seriesId);
	}

	/// <summary>
	/// Provides utility methods for working with story writer file names, including generating safe file names suitable
	/// for use on the file system.
	/// </summary>
	/// <remarks>This class contains extension methods for the IStoryWriterUtil interface. All members are static
	/// and thread-safe. Use these utilities to ensure file names are valid and compatible across different operating
	/// systems.</remarks>
	public static class StoryWriterUtil
	{
		/// <summary>
		/// Provides an extension method to generate a safe file name from an arbitrary input string.
		/// </summary>
		extension(IStoryWriterUtil writerUtil)
		{
			/// <summary>
			/// Generates a file name that is safe for use on the file system by replacing or removing invalid characters and
			/// reserved names.
			/// </summary>
			/// <remarks>The returned file name will not contain characters invalid for file names and will avoid
			/// reserved system names such as "CON" or "NUL". The result is trimmed to a maximum of 255 characters. If the
			/// sanitized name matches a reserved name, underscores are added to avoid conflicts.</remarks>
			/// <param name="input">The input string to convert into a safe file name. May contain invalid file name characters or reserved names.</param>
			/// <returns>A sanitized string suitable for use as a file name. Returns "untitled" if the input is null, empty, or results in
			/// an empty file name after sanitization.</returns>
			public static string ToSafeFileName(string input)
			{
				if (string.IsNullOrWhiteSpace(input)) return "untitled";

				char[] invalidChars = Path.GetInvalidFileNameChars();
				string invalidCharsPattern = $"[{Regex.Escape(new string(invalidChars))}]";

				string safeName = Regex.Replace(input, invalidCharsPattern, "_");

				safeName = safeName.Trim(' ', '.');

				const int maxFileNameLength = 255;
				if (safeName.Length > maxFileNameLength) safeName = safeName[..maxFileNameLength];

				string[] reservedNames =
				[
					"CON", "PRN", "AUX", "NUL",
					"COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
					"LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
				];

				string upperSafeName = safeName.ToUpperInvariant();
				if (reservedNames.Contains(upperSafeName)) safeName = $"_{safeName}_";
				if (string.IsNullOrWhiteSpace(safeName)) safeName = "untitled";

				return safeName;
			}
		}
	}
}
