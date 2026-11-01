using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterPosters.Configuration;
using BetterPosters.Providers;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.BaseItemManager;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace BetterPosters;

/// <summary>
/// The result of putting a poster on one title.
/// </summary>
public enum PosterResult
{
    /// <summary>The poster was replaced.</summary>
    Updated,

    /// <summary>Already up to date (same poster, same quality): nothing downloaded.</summary>
    Unchanged,

    /// <summary>Not handled (no IMDb id, not a movie/show, or switched off for that library).</summary>
    Skipped,

    /// <summary>btttr.cc has no poster for this title.</summary>
    NotAvailable,

    /// <summary>Something went wrong (see the log).</summary>
    Failed
}

/// <summary>
/// Puts Better Posters on titles: downloads the btttr.cc poster, draws the real quality badges onto it
/// (from Jellyfin's own scan of the file) and saves it as the title's poster. Remembers what each title got,
/// so later runs only touch titles whose poster or quality changed (new titles, upgraded files, new settings).
/// </summary>
public sealed class PosterService : IDisposable
{
    /// <summary>The name the original Better Posters plugin uses for its image fetcher.</summary>
    public const string OriginalFetcherName = "Better Posters";

    private const string StateVersion = "3";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly IProviderManager _providerManager;
    private readonly IBaseItemManager _baseItemManager;
    private readonly ILogger<PosterService> _logger;
    private readonly StudioPosterBuilder _studio;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<Guid, string> _applied = new();
    private readonly object _saveLock = new();
    private Timer? _saveTimer;
    private bool _loaded;

    /// <summary>
    /// Initializes a new instance of the <see cref="PosterService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="mediaSourceManager">The media source manager.</param>
    /// <param name="providerManager">The provider manager.</param>
    /// <param name="baseItemManager">The base item manager.</param>
    /// <param name="studio">The studio poster builder.</param>
    /// <param name="logger">The logger.</param>
    public PosterService(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        IProviderManager providerManager,
        IBaseItemManager baseItemManager,
        StudioPosterBuilder studio,
        ILogger<PosterService> logger)
    {
        _studio = studio;
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _providerManager = providerManager;
        _baseItemManager = baseItemManager;
        _logger = logger;
    }

    private static PluginConfiguration Config => Plugin.Instance?.Configuration ?? new PluginConfiguration();

    private static string StatePath => Path.Combine(Plugin.Instance?.DataFolderPath ?? Path.GetTempPath(), "applied.json");

    /// <summary>
    /// Whether a title is one this plugin handles at all.
    /// </summary>
    /// <param name="item">The title.</param>
    /// <returns>True for movies and shows.</returns>
    public static bool Handles(BaseItem? item) => item is Movie or Series;

    /// <summary>
    /// Reads a title's real quality: a movie's own file, or for a show the best of its newest episodes.
    /// </summary>
    /// <param name="item">The title.</param>
    /// <returns>The quality, or null if there's no video file.</returns>
    public QualityInfo? ResolveQuality(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item is Series series)
        {
            var episodes = _libraryManager.GetItemList(new InternalItemsQuery
            {
                Parent = series,
                IncludeItemTypes = [BaseItemKind.Episode],
                Recursive = true,
                IsVirtualItem = false
            });

            QualityInfo? best = null;
            foreach (var episode in episodes.OrderByDescending(e => e.DateCreated).Take(30))
            {
                var q = QualityInfo.FromStreams(_mediaSourceManager.GetMediaStreams(new MediaStreamQuery { ItemId = episode.Id }));
                best = QualityInfo.Best(best, q);
            }

            return best;
        }

        return QualityInfo.FromStreams(_mediaSourceManager.GetMediaStreams(new MediaStreamQuery { ItemId = item.Id }));
    }

    /// <summary>
    /// Whether posters should be put on this title's library.
    /// </summary>
    /// <param name="item">The title.</param>
    /// <returns>True when allowed.</returns>
    public bool IsEnabledFor(BaseItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (Config.ApplyToAllLibraries)
        {
            return true;
        }

        var typeOptions = _libraryManager.GetLibraryOptions(item).GetTypeOptions(item.GetType().Name);
        return _baseItemManager.IsImageFetcherEnabled(item, typeOptions, Plugin.PluginName)
            || _baseItemManager.IsImageFetcherEnabled(item, typeOptions, OriginalFetcherName);
    }

    /// <summary>
    /// Downloads a poster and draws the quality badges on it (used by the image provider too).
    /// </summary>
    /// <param name="url">The btttr.cc poster link.</param>
    /// <param name="quality">The quality to draw, or null for none.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The status code and the finished image bytes (null when not found).</returns>
    public async Task<(HttpStatusCode Status, byte[]? Bytes)> DownloadAsync(string url, QualityInfo? quality, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        using var response = await client.GetAsync(new Uri(url), cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (response.StatusCode, null);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        if (Config.EnableQualityTags && quality is not null && quality.HasAnything)
        {
            try
            {
                // with trend tags on, the same poster without the tag shows where the tag is (badges go under it)
                byte[]? untagged = null;
                if (Config.EnableTrendTags)
                {
                    var plainUrl = url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "tag=none";
                    using var plain = await client.GetAsync(new Uri(plainUrl), cancellationToken).ConfigureAwait(false);
                    if (plain.IsSuccessStatusCode)
                    {
                        untagged = await plain.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                bytes = QualityBadgeRenderer.ApplyBadge(bytes, quality, untagged);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // a badge problem (or the untagged download)  never stops the poster itself
                _logger.LogWarning(ex, "Couldn't draw the quality badges on {Url}; using the poster without them", url);
            }
        }

        return (response.StatusCode, bytes);
    }

    /// <summary>
    /// Makes a title's poster in the chosen style: a studio poster, or btttr.cc's when that's chosen or the
    /// title has no clean artwork on TMDb.
    /// </summary>
    /// <param name="item">The title.</param>
    /// <param name="btttrUrl">Its btttr.cc poster link (the fallback).</param>
    /// <param name="quality">The quality to draw, or null.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The status code and the finished image bytes (null when not found).</returns>
    public async Task<(HttpStatusCode Status, byte[]? Bytes)> MakeAsync(BaseItem item, string btttrUrl, QualityInfo? quality, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var config = Config;
        if (config.Style == PosterStyle.Studio)
        {
            try
            {
                var studio = await _studio.BuildAsync(item, config.EnableQualityTags ? quality : null, config, cancellationToken).ConfigureAwait(false);
                if (studio is not null)
                {
                    return (HttpStatusCode.OK, studio);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Couldn't make the studio poster for {Name}; using btttr.cc's", item.Name);
            }
        }

        return await DownloadAsync(btttrUrl, quality, cancellationToken).ConfigureAwait(false);
    }

    // everything in the settings that changes how a studio poster looks
    private static string StyleKey(PluginConfiguration c) => c.Style == PosterStyle.Studio
        ? string.Join(',', "studio", c.StudioLogo, c.StudioFade, c.StudioShowYear, c.EnableGenre, c.EnableRating, c.EnableAgeRating, c.Language)
        : "btttr";

    /// <summary>
    /// Puts the poster on one title, unless it already has this exact poster.
    /// </summary>
    /// <param name="item">The title.</param>
    /// <param name="force">Replace even if nothing changed.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>What happened.</returns>
    public async Task<PosterResult> ApplyAsync(BaseItem item, bool force, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Handles(item))
        {
            return PosterResult.Skipped;
        }

        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (string.IsNullOrWhiteSpace(imdbId) || !IsEnabledFor(item))
        {
            return PosterResult.Skipped;
        }

        EnsureLoaded();
        var config = Config;
        var url = BetterPosterUrlBuilder.Build(imdbId, config);
        var quality = config.EnableQualityTags ? ResolveQuality(item) : null;
        var signature = string.Join('|', StateVersion, StyleKey(config), url, config.EnableQualityTags ? quality?.Key ?? "none" : "off");

        if (!force && _applied.TryGetValue(item.Id, out var had) && had == signature && item.HasImage(ImageType.Primary, 0))
        {
            return PosterResult.Unchanged;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var (status, bytes) = await MakeAsync(item, url, quality, cancellationToken).ConfigureAwait(false);
            if (bytes is null)
            {
                if (status == HttpStatusCode.NotFound)
                {
                    return PosterResult.NotAvailable;
                }

                _logger.LogWarning("btttr.cc answered {Status} for {Name} ({Url})", (int)status, item.Name, url);
                return PosterResult.Failed;
            }

            using var stream = new MemoryStream(bytes);
            await _providerManager.SaveImage(item, stream, "image/jpeg", ImageType.Primary, null, cancellationToken).ConfigureAwait(false);
            await item.UpdateToRepositoryAsync(ItemUpdateType.ImageUpdate, cancellationToken).ConfigureAwait(false);
            _applied[item.Id] = signature;
            ScheduleSave();
            return PosterResult.Updated;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Couldn't update the poster of {Name} ({Id})", item.Name, item.Id);
            return PosterResult.Failed;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Whether a title has never had a poster from this plugin.
    /// </summary>
    /// <param name="id">The title id.</param>
    /// <returns>True when it hasn't.</returns>
    public bool IsNew(Guid id)
    {
        EnsureLoaded();
        return !_applied.ContainsKey(id);
    }

    /// <summary>
    /// Forgets every title (the next run puts every poster on again).
    /// </summary>
    public void Reset()
    {
        EnsureLoaded();
        _applied.Clear();
        ScheduleSave();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _saveTimer?.Dispose();
        SaveNow();
        _gate.Dispose();
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        lock (_saveLock)
        {
            if (_loaded)
            {
                return;
            }

            try
            {
                if (File.Exists(StatePath))
                {
                    var data = JsonSerializer.Deserialize<Dictionary<Guid, string>>(File.ReadAllText(StatePath));
                    if (data is not null)
                    {
                        foreach (var kv in data)
                        {
                            _applied[kv.Key] = kv.Value;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't read {Path}; posters will be checked again", StatePath);
            }

            _loaded = true;
        }
    }

    private void ScheduleSave()
    {
        lock (_saveLock)
        {
            _saveTimer ??= new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
            _saveTimer.Change(TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
    }

    private void SaveNow()
    {
        lock (_saveLock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
                File.WriteAllText(StatePath, JsonSerializer.Serialize(_applied.ToDictionary(kv => kv.Key, kv => kv.Value)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Couldn't save {Path}", StatePath);
            }
        }
    }
}
