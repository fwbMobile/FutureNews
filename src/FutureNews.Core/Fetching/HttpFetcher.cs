using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace FutureNews.Core.Fetching;

/// <summary>
/// A shared, polite HTTP client: per-host minimum spacing, retries with exponential
/// backoff, transparent decompression and a browser-ish User-Agent (several publishers
/// reject requests without one).
/// </summary>
public sealed class HttpFetcher : IDisposable
{
    private readonly HttpClient _client;
    private readonly ConcurrentDictionary<string, HostGate> _gates = new();
    private readonly Func<string, TimeSpan> _defaultDelay;

    public HttpFetcher(Func<string, TimeSpan>? defaultDelay = null)
    {
        _defaultDelay = defaultDelay ?? (_ => TimeSpan.FromSeconds(1));
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            UseCookies = false,
            ClientCertificateOptions = ClientCertificateOption.Manual,
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(45),
        };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 FutureNews/1.0");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US,en;q=0.9,zh-CN;q=0.8,zh;q=0.7");
    }

    /// <summary>Minimum spacing between two requests to the same host.</summary>
    public TimeSpan DelayFor(string host) => _gates.GetOrAdd(host, h => new HostGate(_defaultDelay(h))).Delay;

    public async Task<string> GetStringAsync(string url, CancellationToken ct, int retries = 2)
    {
        var host = new Uri(url).Host;
        var gate = _gates.GetOrAdd(host, h => new HostGate(_defaultDelay(h)));
        Exception? last = null;

        for (var attempt = 0; attempt <= retries; attempt++)
        {
            await gate.EnterAsync(ct).ConfigureAwait(false);
            try
            {
                using var response = await _client.GetAsync(url, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    return Decode(bytes, response.Content.Headers.ContentType?.CharSet);
                }

                var status = (int)response.StatusCode;
                if (status == 429 || status == 503)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 + attempt * 3), ct).ConfigureAwait(false);
                    continue;
                }

                if (status is 403 or 406 or 404 or 410)
                {
                    throw new HttpRequestException($"HTTP {status} for {url}", null, response.StatusCode);
                }

                throw new HttpRequestException($"HTTP {status} for {url}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                if (attempt < retries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), ct).ConfigureAwait(false);
                }
            }
            finally
            {
                gate.Exit();
            }
        }

        throw last ?? new HttpRequestException($"Failed to fetch {url}");
    }

    public async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
    {
        var text = await GetStringAsync(url, ct).ConfigureAwait(false);
        return Encoding.UTF8.GetBytes(text);
    }

    private static string Decode(byte[] bytes, string? charset)
    {
        if (charset is not null && charset.Contains("gb", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                return Encoding.GetEncoding(charset).GetString(bytes);
            }
            catch (ArgumentException)
            {
                // fall through to UTF-8
            }
        }

        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose() => _client.Dispose();

    private sealed class HostGate
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private DateTime _last = DateTime.MinValue;

        public HostGate(TimeSpan delay) => Delay = delay;

        public TimeSpan Delay { get; }

        /// <summary>Acquires the host slot and waits out the configured inter-request spacing.</summary>
        public async Task EnterAsync(CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var since = DateTime.UtcNow - _last;
                if (since < Delay)
                {
                    await Task.Delay(Delay - since, ct).ConfigureAwait(false);
                }
                _last = DateTime.UtcNow;
            }
            catch
            {
                _semaphore.Release();
                throw;
            }
        }

        public void Exit() => _semaphore.Release();
    }
}
