using EpubManager.ContentSources;

namespace EpubManager.Tests;

public class PluginDiscoveryTests
{
	private sealed class FakeWriter : IStoryWriter
	{
		public void Log(string message) { }
		public Task CreateEpubFromSeriesAsync(string seriesUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, int startIndex = 0, int endIndex = 0, EpubOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
		public Task CreateEpubFromStoryAsync(string storyUrl, string outputDirectory, string coverOverwrite = "", bool raw = false, EpubOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	[Fact]
	public void LiteroticaPlugin_IsDiscoveredAutomatically()
	{
		Assert.NotNull(EpubManager.Writers.Literotica);
		Assert.Contains("Literotica", EpubManager.Writers.Available);
		Assert.Same(EpubManager.Writers.Literotica, EpubManager.Writers.Get("literotica")); // case-insensitive
	}

	[Fact]
	public void UnknownWriter_ReturnsNull()
	{
		Assert.Null(EpubManager.Writers.Get("NoSuchSite"));
		Assert.Null(EpubManager.Writers.ScribbleHub);
	}

	[Fact]
	public void Register_AddsCustomWriter()
	{
		FakeWriter writer = new();
		EpubManager.Writers.Register("Fake", writer);

		Assert.Same(writer, EpubManager.Writers.Get("Fake"));
	}
}
