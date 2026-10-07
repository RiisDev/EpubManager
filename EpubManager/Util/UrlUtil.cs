using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace EpubManager.Util
{
	/// <summary>
	/// Provides utility methods for parsing Literotica story and series URLs
	/// and extracting identifying information such as slugs or IDs.
	/// </summary>
	public static class UrlUtil
	{
		
		/// <summary>
		/// Converts a string into a safe filename valid for both Windows and Unix systems.
		/// </summary>
		/// <param name="input">The input string to sanitize.</param>
		/// <returns>A safe filename string.</returns>
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
