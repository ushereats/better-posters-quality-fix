using System;
using System.Collections.Concurrent;
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
/// without waiting for the nightly task. Runs quietly in the background, one title at a time.
/// </summary>
public sealed class NewTitleWatcher : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly PosterService _posters;
    private readonly ILogger<NewTitleWatcher> _logger;
    private readonly ConcurrentDictionary<Guid, byte> _queued = new();
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
            if (episode.Season is { } season && PosterService.Handles(season) && Plugin.Instance?.Configuration.AutoApplyNewTitles != false
                && _queued.TryAdd(season.Id, 0))
            {
                _signal.Release();
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

        if (_queued.TryAdd(item.Id, 0))
        {
            _signal.Release();
        }
    }

    private async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await _signal.WaitAsync(token).ConfigureAwait(false);
            // let a library scan finish its own work on the title first (metadata, images, file info)
            await Task.Delay(TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            foreach (var id in _queued.Keys)
            {
                _queued.TryRemove(id, out _);
                var item = _libraryManager.GetItemById(id);
                if (item is null)
                {
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
