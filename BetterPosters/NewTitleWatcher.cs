using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BetterPosters;

/// <summary>
/// New titles (and titles whose file changed, e.g. upgraded to 4K) get their Better Poster straight away,
/// without waiting for the nightly task: about 2 seconds after Jellyfin has the title's details (its TMDb /
/// IMDb id, which the poster is looked up by). Until then the title is checked again every few seconds.
/// Runs quietly in the background.
/// </summary>
public sealed class NewTitleWatcher : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly PosterService _posters;
    private readonly ILogger<NewTitleWatcher> _logger;
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(2);      // a burst of updates to one title becomes one
    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(5);     // details not there yet: look again
    private const int MaxChecks = 60;                                       // ... for up to 5 minutes
    private readonly ConcurrentDictionary<Guid, (DateTime Due, int Checks)> _queued = new();
    private readonly SemaphoreSlim _signal = new(0);
    private CancellationTokenSource? _stop;
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="NewTitleWatcher"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="posters">The poster service.</param>
    /// <param name="logger">The logger.</param>
    public NewTitleWatcher(ILibraryManager libraryManager, PosterService posters, ILogger<NewTitleWatcher> logger)
    {
        _libraryManager = libraryManager;
        _posters = posters;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stop = new CancellationTokenSource();
        _libraryManager.ItemAdded += OnChanged;
        _libraryManager.ItemUpdated += OnChanged;
        _loop = Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnChanged;
        _libraryManager.ItemUpdated -= OnChanged;
        if (_stop is not null)
        {
            await _stop.CancelAsync().ConfigureAwait(false);
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _stop?.Dispose();
        _signal.Dispose();
    }

    private void OnChanged(object? sender, ItemChangeEventArgs e)
    {
        var item = e.Item;
        // a new or changed episode can change its show's and its season's quality (e.g. now has 4K episodes)
        if (item is Episode episode && e.UpdateReason != ItemUpdateType.ImageUpdate)
        {
            if (episode.Season is { } season && PosterService.Handles(season) && Plugin.Instance?.Configuration.AutoApplyNewTitles != false)
            {
                Queue(season.Id);
            }

            item = episode.Series;
        }

        if (item is null || !PosterService.Handles(item) || Plugin.Instance?.Configuration.AutoApplyNewTitles == false)
        {
            return;
        }

        // our own poster save comes back here as an image update: nothing to do for those
        if (e.UpdateReason == ItemUpdateType.ImageUpdate && !_posters.IsNew(item.Id))
        {
            return;
        }

        Queue(item.Id);
    }

    private void Queue(Guid id)
    {
        _queued.AddOrUpdate(id, _ => (DateTime.UtcNow + Settle, 0), (_, old) => (DateTime.UtcNow + Settle, old.Checks));
        _signal.Release();
    }

    // the poster is looked up by the title's TMDb / IMDb id: a title Jellyfin is still filling in has none yet
    private static bool Ready(BaseItem item)
    {
        BaseItem? owner = item is Season s ? s.Series : item;
        if (owner?.ProviderIds is null)
        {
            return false;
        }

        return owner.ProviderIds.ContainsKey("Tmdb") || owner.ProviderIds.ContainsKey("Imdb") || owner.ProviderIds.ContainsKey("TmdbCollection");
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (_queued.IsEmpty)
            {
                await _signal.WaitAsync(token).ConfigureAwait(false);
                continue;
            }

            // sleep until the next title is due (or something new comes in)
            var next = _queued.Values.Min(v => v.Due) - DateTime.UtcNow;
            if (next > TimeSpan.Zero)
            {
                await _signal.WaitAsync(next < TimeSpan.FromSeconds(1) ? next : TimeSpan.FromSeconds(1), token).ConfigureAwait(false);
                continue;
            }

            foreach (var (id, state) in _queued.ToArray())
            {
                if (state.Due > DateTime.UtcNow || !_queued.TryRemove(new KeyValuePair<Guid, (DateTime, int)>(id, state)))
                {
                    continue;
                }

                var item = _libraryManager.GetItemById(id);
                if (item is null)
                {
                    continue;
                }

                if (!Ready(item))
                {
                    if (state.Checks < MaxChecks)
                    {
                        _queued.TryAdd(id, (DateTime.UtcNow + Recheck, state.Checks + 1));
                    }

                    continue;
                }

                try
                {
                    var r = await _posters.ApplyAsync(item, false, token).ConfigureAwait(false);
                    if (r == PosterResult.Updated)
                    {
                        _logger.LogInformation("Better Posters: new poster for {Name}", item.Name);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Better Posters: couldn't update {Name}", item.Name);
                }
            }
        }
    }
}
