using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;

namespace BetterPosters.Providers;

/// <summary>
/// Everything that comes from TMDb, kept on disk:
/// - artwork and logos are kept for good (TMDb never changes a file once published), so rebuilding posters after a
///   settings change doesn't download anything again;
/// - lists of a title's artwork are kept for a week (new artwork shows up after that), and an old list is used
///   when TMDb can't be reached.
/// Requests are spaced out and limited to a few at once, and retried with growing waits when TMDb is busy (429)
/// or briefly down, so a full run over a big library never hits TMDb's limits or leaves gaps.
/// </summary>
public sealed class TmdbClient : IDisposable
{
    private static readonly TimeSpan ListAge = TimeSpan.FromDays(7);
    private static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(30);   // ~30 requests a second at most
    private static readonly TimeSpan KeepUnused = TimeSpan.FromDays(120);
    private const long MaxCacheBytes = 4L * 1024 * 1024 * 1024;               // 4 GB

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TmdbClient> _logger;
    private readonly SemaphoreSlim _concurrent = new(4, 4);
    private readonly SemaphoreSlim _spacing = new(1, 1);
    private DateTime _last = DateTime.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="TmdbClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="logger">The logger.</param>
    public TmdbClient(IHttpClientFactory httpClientFactory, ILogger<TmdbClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static string Root => Path.Combine(Plugin.Instance?.DataFolderPath ?? Path.GetTempPath(), "cache");

    /// <summary>
    /// A TMDb API answer (kept a week; an older copy is used when TMDb can't be reached).
    /// </summary>
    /// <param name="url">The API link (with the key).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The JSON, or null.</returns>
    public async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        var file = PathFor("api", StripKey(url), ".json");
        var fresh = File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < ListAge;
        if (fresh)
        {
            var cached = TryParse(file);
            if (cached is not null)
            {
                return cached;
            }
        }

        var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            Save(file, bytes);
            try
            {
                return JsonDocument.Parse(bytes);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // TMDb couldn't be reached: an older list is better than nothing
        return File.Exists(file) ? TryParse(file) : null;
    }

    /// <summary>
    /// A TMDb image (artwork, logo), kept for good once downloaded.
    /// </summary>
    /// <param name="url">The image link.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The image, or null.</returns>
    public async Task<byte[]?> GetImageAsync(string url, CancellationToken cancellationToken)
    {
        var file = PathFor("img", url, ".bin");
        if (File.Exists(file))
        {
            try
            {
                var data = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
                Touch(file);
                return data;
            }
            catch (IOException)
            {
            }
        }

        var bytes = await DownloadAsync(url, cancellationToken).ConfigureAwait(false);
        if (bytes is not null)
        {
            Save(file, bytes);
        }

        return bytes;
    }

    /// <summary>
    /// Clears out cached files nobody used in a long time, and the oldest ones when the cache gets too big.
    /// </summary>
    public void Prune()
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return;
            }

            var files = new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.LastWriteTimeUtc).ToList();
            long total = files.Sum(f => f.Length);
            foreach (var f in files)
            {
                if (DateTime.UtcNow - f.LastWriteTimeUtc > KeepUnused || total > MaxCacheBytes)
                {
                    total -= f.Length;
                    f.Delete();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Couldn't tidy the poster cache");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _concurrent.Dispose();
        _spacing.Dispose();
    }

    // up to 4 attempts: TMDb busy (429) waits as long as it asks; server errors and timeouts wait 1, 2, 4 s
    private async Task<byte[]?> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await _concurrent.WaitAsync(cancellationToken).ConfigureAwait(false);
            TimeSpan? wait = null;
            try
            {
                await SpaceAsync(cancellationToken).ConfigureAwait(false);
                var client = _httpClientFactory.CreateClient(NamedClient.Default);
                using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 << attempt);
                }
                else if ((int)response.StatusCode >= 500)
                {
                    wait = TimeSpan.FromSeconds(1 << attempt);
                }
                else
                {
                    return null;   // 404 and the like: no point asking again
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                wait = TimeSpan.FromSeconds(1 << attempt);
                _logger.LogDebug(ex, "TMDb request failed (attempt {Attempt})", attempt + 1);
            }
            finally
            {
                _concurrent.Release();
            }

            if (wait is { } w)
            {
                await Task.Delay(w > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : w, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogWarning("TMDb didn't answer after 4 tries: {Url}", StripKey(url));
        return null;
    }

    private async Task SpaceAsync(CancellationToken cancellationToken)
    {
        await _spacing.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var gap = _last + Spacing - DateTime.UtcNow;
            if (gap > TimeSpan.Zero)
            {
                await Task.Delay(gap, cancellationToken).ConfigureAwait(false);
            }

            _last = DateTime.UtcNow;
        }
        finally
        {
            _spacing.Release();
        }
    }

    private static string StripKey(string url)
    {
        var i = url.IndexOf("api_key=", StringComparison.Ordinal);
        if (i < 0)
        {
            return url;
        }

        var end = url.IndexOf('&', i);
        return url.Remove(i, (end < 0 ? url.Length : end + 1) - i);
    }

    private static string PathFor(string kind, string key, string ext)
    {
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
        return Path.Combine(Root, kind, hash[..2], hash + ext);
    }

    private static JsonDocument? TryParse(string file)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(file));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void Save(string file, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var tmp = file + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, file, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Couldn't keep {File}", file);
        }
    }

    private static void Touch(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
