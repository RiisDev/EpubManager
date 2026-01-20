using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EpubManager.ContentSources
{
	public interface IStoryWriterUtil
	{
		public Task<string> GetStorySlugAsync(string url);

		public Task<string> GetSeriesIdAsync(string url);

		public Task<bool> VerifySlugAsync(string? slug);

		public Task<bool> VerifySeriesIdAsync(string? seriesId);
	}

	public static class StoryWriterUtil
	{
		extension(IStoryWriterUtil writerUtil)
		{
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
