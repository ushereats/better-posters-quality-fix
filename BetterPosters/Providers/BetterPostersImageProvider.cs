using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BetterPosters.Configuration;
using Jellyfin.Data.Enums;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace BetterPosters.Providers;

/// <summary>
/// Provides Better Posters primary images from btttr.cc, with an optional
/// locally-rendered quality badge sourced from the item's real MediaStream data
/// instead of btttr.cc's externally-sourced (and often mismatched) badge.
/// </summary>
public class BetterPostersImageProvider : IRemoteImageProvider, IHasOrder
{
    private const string ItemIdQueryKey = "bpItemId";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly IMediaSourceManager _mediaSourceManager;
    private readonly ILogger<BetterPostersImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BetterPostersImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager, used to resolve the item embedded in the image URL.</param>
    /// <param name="mediaSourceManager">The media source manager, used to read real stream resolution/HDR data.</param>
    /// <param name="logger">The logger.</param>
    public BetterPostersImageProvider(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        IMediaSourceManager mediaSourceManager,
        ILogger<BetterPostersImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _mediaSourceManager = mediaSourceManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => Plugin.PluginName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public bool Supports(BaseItem item)
    {
        return item is Movie or Series;
    }

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return Supports(item) ? [ImageType.Primary] : [];
    }

    /// <inheritdoc />
    public Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!Supports(item))
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>([]);
        }

        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return Task.FromResult<IEnumerable<RemoteImageInfo>>([]);
        }

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var url = BetterPosterUrlBuilder.Build(imdbId, configuration);

        // Embed the item's id so GetImageResponse (which only receives the URL
        // string, per IRemoteImageProvider) can look the item back up and read
        // its real MediaStream data before composing the quality badge. This
        // parameter is stripped before the request is forwarded to btttr.cc.
        var separator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        var urlWithItemId = $"{url}{separator}{ItemIdQueryKey}={item.Id:N}";

        var language = BetterPosterUrlBuilder.GetLanguageCode(configuration.Language) ?? "en";

        return Task.FromResult<IEnumerable<RemoteImageInfo>>(
            [
                new RemoteImageInfo
                {
                    ProviderName = Name,
                    Url = urlWithItemId,
                    ThumbnailUrl = urlWithItemId,
                    Type = ImageType.Primary,
                    Width = 500,
                    Height = 750,
                    Language = language
                }
            ]);
    }

    /// <inheritdoc />
    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var (cleanUrl, itemId) = ExtractAndStripItemId(url);

        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        var response = await client.GetAsync(new Uri(cleanUrl), cancellationToken).ConfigureAwait(false);

        var configuration = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        if (!configuration.EnableQualityTags || itemId is null || !response.IsSuccessStatusCode)
        {
            return response;
        }

        try
        {
            var label = ResolveQualityLabel(itemId.Value);
            if (string.IsNullOrEmpty(label))
            {
                return response;
            }

            var originalBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var badgedBytes = QualityBadgeRenderer.ApplyBadge(originalBytes, label);

            var badgedResponse = new HttpResponseMessage(response.StatusCode)
            {
                Content = new ByteArrayContent(badgedBytes)
            };
            badgedResponse.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

            return badgedResponse;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let a badge-rendering failure break poster loading entirely —
            // fall back to the plain (unbadged) btttr.cc poster.
            _logger.LogWarning(ex, "Failed to render local quality badge for item {ItemId}; using unbadged poster", itemId);
            return response;
        }
    }

    /// <summary>
    /// Manual query-string parsing here rather than a framework helper (e.g.
    /// System.Web.HttpUtility or Microsoft.AspNetCore.WebUtilities.QueryHelpers)
    /// deliberately — this keeps no new package-compatibility assumptions in play
    /// beyond what's already referenced. Swap in QueryHelpers.ParseQuery if
    /// Jellyfin.Controller already pulls in Microsoft.AspNetCore.WebUtilities
    /// transitively in your build (it likely does) and you'd rather use that.
    /// </summary>
    private static (string CleanUrl, Guid? ItemId) ExtractAndStripItemId(string url)
    {
        var uri = new Uri(url);
        var basePath = uri.GetLeftPart(UriPartial.Path);
        var rawQuery = uri.Query.TrimStart('?');

        if (string.IsNullOrEmpty(rawQuery))
        {
            return (url, null);
        }

        Guid? itemId = null;
        var remainingPairs = new List<string>();

        foreach (var pair in rawQuery.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            if (string.Equals(key, ItemIdQueryKey, StringComparison.Ordinal))
            {
                itemId = Guid.TryParse(value, out var parsed) ? parsed : null;
                continue;
            }

            remainingPairs.Add(pair);
        }

        var cleanUrl = remainingPairs.Count == 0
            ? basePath
            : basePath + "?" + string.Join('&', remainingPairs);

        return (cleanUrl, itemId);
    }

    private string? ResolveQualityLabel(Guid itemId)
    {
        var item = _libraryManager.GetItemById(itemId);
        if (item is null)
        {
            return null;
        }

        if (item is Series series)
        {
            // Series have no single resolution of their own — use the
            // highest-resolution episode found. Adjust this if you'd rather
            // badge series poster by, say, the most recently aired episode
            // instead of the sharpest one.
            var episodes = _libraryManager.GetItemList(new MediaBrowser.Controller.Entities.InternalItemsQuery
            {
                Parent = series,
                IncludeItemTypes = [BaseItemKind.Episode],
                Recursive = true
            });

            var bestStreams = episodes
                .Select(episode => _mediaSourceManager.GetMediaStreams(
                    new MediaStreamQuery { ItemId = episode.Id }))
                .Where(streams => streams.Count > 0)
                .OrderByDescending(streams => streams
                    .Where(s => s.Type == MediaStreamType.Video)
                    .Select(s => (long)(s.Width ?? 0) * (s.Height ?? 0))
                    .DefaultIfEmpty(0)
                    .Max())
                .FirstOrDefault();

            return bestStreams is null ? null : QualityBadgeRenderer.GetQualityLabel(bestStreams);
        }

        var streams = _mediaSourceManager.GetMediaStreams(new MediaStreamQuery { ItemId = item.Id });
        return QualityBadgeRenderer.GetQualityLabel(streams);
    }
}
