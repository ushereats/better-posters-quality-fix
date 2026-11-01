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
    private readonly PosterService _posters;
    private readonly ILogger<BetterPostersImageProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BetterPostersImageProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">The HTTP client factory.</param>
    /// <param name="libraryManager">The library manager, used to resolve the item embedded in the image URL.</param>
    /// <param name="posters">The poster service (download + quality badges).</param>
    /// <param name="logger">The logger.</param>
    public BetterPostersImageProvider(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        PosterService posters,
        ILogger<BetterPostersImageProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _posters = posters;
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
        var item = itemId is null ? null : _libraryManager.GetItemById(itemId.Value);
        if (item is null)
        {
            var client = _httpClientFactory.CreateClient(NamedClient.Default);
            return await client.GetAsync(new Uri(cleanUrl), cancellationToken).ConfigureAwait(false);
        }

        QualityInfo? quality = null;
        try
        {
            quality = _posters.ResolveQuality(item);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Couldn't read the quality of {Name}", item.Name);
        }

        var (status, bytes) = await _posters.MakeAsync(item, cleanUrl, quality, cancellationToken).ConfigureAwait(false);
        var response = new HttpResponseMessage(status);
        if (bytes is not null)
        {
            response.Content = new ByteArrayContent(bytes);
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
        }

        return response;
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
}
