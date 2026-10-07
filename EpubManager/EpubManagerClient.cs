using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable CheckNamespace
#pragma warning disable IDE0130

namespace EpubManager
{
	/// <summary>
	/// Provides a centralized HTTP client used by the EpubManager library.
	/// This static client manages all outbound HTTP requests and supports both
	/// a default internal client and user-supplied custom clients.
	/// </summary>
	public static class EpubManagerClient
	{
		private static readonly Lazy<HttpClient> LazyClient = new(() =>
		{
			HttpClient client = new()
			{
				Timeout = TimeSpan.FromSeconds(30)
			};
			client.DefaultRequestHeaders.Add("User-Agent", "IrisAgent Nuget_IrisDev/1.0");
			return client;
		});

		/// <summary>
		/// Gets or sets the active <see cref="HttpClient"/> instance used by all API operations.
		/// </summary>
		/// <remarks>
		/// By default, this property returns the internal shared client.
		/// If you wish to override it with your own client (for example, to
		/// add custom headers, proxy configurations, or handlers), simply set
		/// <see cref="HttpClientInstance"/> to your own <see cref="HttpClient"/>.
		/// </remarks>
		public static HttpClient HttpClientInstance
		{
			get => _customClient ?? LazyClient.Value;
			set => _customClient = value;
		}

		private static HttpClient? _customClient;

		private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
		
		/// <summary>
		/// Sends an HTTP GET request to the specified endpoint and returns the response deserialized to the specified type.
		/// </summary>
		/// <remarks>The method uses an internal cache to store and retrieve responses for identical requests. If a
		/// cached value exists, it is returned without making a network call. The response is deserialized using <see
		/// cref="System.Text.Json.JsonSerializer"/> unless <typeparamref name="T"/> is <see langword="string"/>, in which
		/// case the raw response body is returned.</remarks>
		/// <typeparam name="T">The type to which the HTTP response will be deserialized. If <typeparamref name="T"/> is <see langword="string"/>,
		/// the raw response body is returned.</typeparam>
		/// <param name="baseUrl">The base URL of the target API. Must not be null or empty.</param>
		/// <param name="endpoint">The endpoint path to append to the base URL. If the path starts with '/', it will be trimmed.</param>
		/// <param name="paramsQuery">The query parameter name used to pass serialized JSON content. Defaults to "params".</param>
		/// <param name="jsonContent">An optional object to serialize as JSON and include in the query string. If null, no query parameter is added.</param>
		/// <returns>A task representing the asynchronous operation. The result contains the deserialized response of type
		/// <typeparamref name="T"/>.</returns>
		/// <exception cref="InvalidOperationException">Thrown if the HTTP request fails or if deserialization of the response returns null.</exception>
		public static async Task<T> Get<T>(string baseUrl, string endpoint, string paramsQuery = "params", object? jsonContent = null)
		{
			if (endpoint[0] == '/')
				endpoint = endpoint[1..];

			string queryString = jsonContent is not null
				? $"?{paramsQuery}=" + Uri.EscapeDataString(JsonSerializer.Serialize(jsonContent))
				: string.Empty;

			string url = $"{baseUrl}{endpoint}{queryString}";

			Debug.WriteLine(url);

			string cacheKey = $"{typeof(T).FullName}:{url}";

			if (Cache.TryGetValue(cacheKey, out T? cachedValue)) return cachedValue!;

			HttpRequestMessage request = new(HttpMethod.Get, url);
			HttpResponseMessage response = await HttpClientInstance.SendAsync(request).ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
				
				throw new InvalidOperationException( 
					$"Request to '{url}' failed with {(int)response.StatusCode} ({response.StatusCode}).\nResponse: {responseText}"
				);
			}

			string jsonResponse = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

#if DEBUG
			Debug.WriteLine(jsonResponse);
#endif

			T result;

			if (typeof(T) == typeof(string))
			{
				result = (T)(object)jsonResponse;
			}
			else
			{
				T deserialized = JsonSerializer.Deserialize<T>(jsonResponse) ?? throw new InvalidOperationException("Deserialization returned null.");
				result = deserialized;
			}

			Cache.Set(cacheKey, result, CacheDuration);

			return result;
		}

		/// <summary>Base delay between retries; doubles each attempt. Tests shorten it.</summary>
		internal static TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(1);

		private static bool IsTransient(HttpStatusCode status) => (int)status == 429 || (int)status >= 500;

		/// <summary>
		/// GETs a URL, retrying on network errors, HTTP 429 and 5xx (honoring Retry-After). Returns the last response,
		/// so callers still check <see cref="HttpResponseMessage.IsSuccessStatusCode"/>.
		/// </summary>
		public static async Task<HttpResponseMessage> GetWithRetryAsync(string url, int maxAttempts = 3, CancellationToken cancellationToken = default)
		{
			for (int attempt = 1; ; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				TimeSpan delay = TimeSpan.FromTicks(RetryBaseDelay.Ticks << (attempt - 1));

				try
				{
					HttpResponseMessage response = await HttpClientInstance.GetAsync(url, cancellationToken).ConfigureAwait(false);
					if (!IsTransient(response.StatusCode) || attempt >= maxAttempts) return response;

					if (response.Headers.RetryAfter?.Delta is { } retryAfter) delay = retryAfter;
					response.Dispose();
				}
				catch (Exception ex) when (attempt < maxAttempts && (ex is HttpRequestException || ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
				{
					// network error or HttpClient timeout: fall through to retry
				}

				await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
			}
		}

		/// <summary>
		/// Runs an operation, retrying any failure (except cancellation) with exponential backoff. For transient
		/// network trouble when fetching content; do not wrap operations whose failure is a definitive answer.
		/// </summary>
		public static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, int maxAttempts = 3, Action<string>? onRetry = null, CancellationToken cancellationToken = default)
		{
			for (int attempt = 1; ; attempt++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					return await action().ConfigureAwait(false);
				}
				catch (Exception ex) when (attempt < maxAttempts && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
				{
					onRetry?.Invoke($"Attempt {attempt} failed ({ex.Message}); retrying...");
				}

				await Task.Delay(TimeSpan.FromTicks(RetryBaseDelay.Ticks << (attempt - 1)), cancellationToken).ConfigureAwait(false);
			}
		}

		internal static (bool, T?) TryDeserialize<T>(string jsonResponse)
		{
			try
			{
				return (true, JsonSerializer.Deserialize<T>(jsonResponse));
			}
			catch
			{
				return (false, default);
			}
		}
	}

	internal static class Cache
	{
		private static readonly ConcurrentDictionary<string, (DateTimeOffset Expiry, object Value)> CacheData = new();

		internal static bool TryGetValue<T>(string key, out T? value)
		{
			if (CacheData.TryGetValue(key, out (DateTimeOffset Expiry, object Value) entry))
			{
				if (DateTimeOffset.UtcNow < entry.Expiry)
				{
					value = (T)entry.Value;
					return true;
				}

				CacheData.TryRemove(key, out _);
			}

			value = default;
			return false;
		}

		internal static void Set<T>(string key, T value, TimeSpan duration)
		{
			DateTimeOffset expiry = DateTimeOffset.UtcNow.Add(duration);
			CacheData[key] = (expiry, value!);

			if (CacheData.Count < 500) return;

			foreach (KeyValuePair<string, (DateTimeOffset Expiry, object Value)> kvp in CacheData)
			{
				if (kvp.Value.Expiry <= DateTimeOffset.UtcNow)
					CacheData.TryRemove(kvp.Key, out _);
			}
		}
	}
}
