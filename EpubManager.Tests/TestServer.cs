using System.Net;
using System.Net.Sockets;

namespace EpubManager.Tests;

/// <summary>Tiny local HTTP server so download paths (covers, images, retries) can be tested offline.</summary>
internal sealed class TestServer : IDisposable
{
	private readonly HttpListener _listener = new();
	private readonly Func<string, int, (int Status, byte[] Body)> _handler;
	private readonly Dictionary<string, int> _hits = [];

	public string BaseUrl { get; }

	/// <param name="handler">Receives the request path and how many times it has been requested (1-based).</param>
	public TestServer(Func<string, int, (int Status, byte[] Body)> handler)
	{
		_handler = handler;

		TcpListener probe = new(IPAddress.Loopback, 0);
		probe.Start();
		int port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();

		BaseUrl = $"http://localhost:{port}";
		_listener.Prefixes.Add(BaseUrl + "/");
		_listener.Start();
		_ = Task.Run(Loop);
	}

	public int Hits(string path) => _hits.TryGetValue(path, out int n) ? n : 0;

	private async Task Loop()
	{
		while (_listener.IsListening)
		{
			HttpListenerContext context;
			try { context = await _listener.GetContextAsync(); }
			catch { return; }

			string path = context.Request.Url!.AbsolutePath;
			int count;
			lock (_hits) count = _hits[path] = Hits(path) + 1;

			(int status, byte[] body) = _handler(path, count);
			context.Response.StatusCode = status;
			context.Response.ContentLength64 = body.Length;
			await context.Response.OutputStream.WriteAsync(body, 0, body.Length);
			context.Response.Close();
		}
	}

	public void Dispose() => _listener.Close();

	/// <summary>A valid 1x1 PNG.</summary>
	public static byte[] Png { get; } = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
