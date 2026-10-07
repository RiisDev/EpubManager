using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace EpubManager.Tests;

/// <summary>Runs the W3C EpubCheck validator (needs Java; downloads the pinned release once into the temp folder).</summary>
internal static class EpubCheck
{
	private const string Version = "5.4.0";
	private static readonly Lazy<string?> Jar = new(FindOrDownloadJar);

	/// <summary>Skips the calling test when Java or EpubCheck is unavailable.</summary>
	public static void RequireAvailable()
	{
		if (Jar.Value == null) Assert.Skip("EpubCheck unavailable (needs Java on PATH and network access to download it once).");
	}

	/// <summary>Returns EpubCheck's FATAL/ERROR/WARNING lines for the file; empty means valid.</summary>
	public static List<string> Validate(string epubPath)
	{
		RequireAvailable();

		using Process process = Process.Start(new ProcessStartInfo("java", $"-jar \"{Jar.Value}\" \"{epubPath}\"")
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false
		})!;

		Task<string> stderr = process.StandardError.ReadToEndAsync();
		string output = process.StandardOutput.ReadToEnd() + stderr.GetAwaiter().GetResult();
		process.WaitForExit();

		return output.Split('\n')
			.Select(l => l.Trim())
			.Where(l => Regex.IsMatch(l, @"^(FATAL|ERROR|WARNING)\("))
			.ToList();
	}

	private static string? FindOrDownloadJar()
	{
		try
		{
			string root = Path.Combine(Path.GetTempPath(), "epubmanager-tests");
			string jar = Path.Combine(root, $"epubcheck-{Version}", "epubcheck.jar");
			if (File.Exists(jar)) return jar;

			using Process java = Process.Start(new ProcessStartInfo("java", "-version") { RedirectStandardError = true, UseShellExecute = false })!;
			java.StandardError.ReadToEnd();
			java.WaitForExit();
			if (java.ExitCode != 0) return null;

			Directory.CreateDirectory(root);
			string zip = Path.Combine(root, $"epubcheck-{Version}.zip");
			using (HttpClient http = new())
			{
				byte[] data = http.GetByteArrayAsync($"https://github.com/w3c/epubcheck/releases/download/v{Version}/epubcheck-{Version}.zip").GetAwaiter().GetResult();
				File.WriteAllBytes(zip, data);
			}
			ZipFile.ExtractToDirectory(zip, root, overwriteFiles: true);
			return File.Exists(jar) ? jar : null;
		}
		catch
		{
			return null;
		}
	}
}
