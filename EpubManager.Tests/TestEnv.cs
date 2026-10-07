namespace EpubManager.Tests;

/// <summary>
/// Reads test settings from environment variables, loading a <c>.env</c> file (searched upward from the
/// test binaries) for any variable that is not already set. The real environment always wins.
/// </summary>
internal static class TestEnv
{
	public const string StoryUrl = "LITEROTICA_TEST_STORY_URL";
	public const string SeriesUrl = "LITEROTICA_TEST_SERIES_URL";

	static TestEnv()
	{
		for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			string path = Path.Combine(dir.FullName, ".env");
			if (!File.Exists(path)) continue;

			foreach (string raw in File.ReadAllLines(path))
			{
				string line = raw.Trim();
				int eq = line.IndexOf('=');
				if (line.Length == 0 || line[0] == '#' || eq <= 0) continue;

				string key = line[..eq].Trim();
				string value = line[(eq + 1)..].Trim().Trim('"', '\'');
				if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
					Environment.SetEnvironmentVariable(key, value);
			}
			return;
		}
	}

	/// <summary>Returns the variable's value, or skips the calling test when it is not set.</summary>
	public static string Require(string name)
	{
		string? value = Environment.GetEnvironmentVariable(name);
		if (string.IsNullOrWhiteSpace(value)) Assert.Skip($"{name} is not set (add it to .env; see .env.example).");
		return value!;
	}
}
