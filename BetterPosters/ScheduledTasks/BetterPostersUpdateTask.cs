using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace BetterPosters.ScheduledTasks;

/// <summary>
/// Puts Better Posters (with real quality badges) on every movie and show.
/// Only titles whose poster or quality changed are downloaded again, so the daily run is quick.
/// </summary>
public class BetterPostersUpdateTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly PosterService _posters;
    private readonly BetterPosters.Providers.TmdbClient _tmdb;
    private readonly ILogger<BetterPostersUpdateTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BetterPostersUpdateTask"/> class.
    /// </summary>
    /// <param name="libraryManager">The library manager.</param>
    /// <param name="posters">The poster service.</param>
    /// <param name="tmdb">TMDb (its cache is tidied after each run).</param>
    /// <param name="logger">The logger.</param>
    public BetterPostersUpdateTask(ILibraryManager libraryManager, PosterService posters, BetterPosters.Providers.TmdbClient tmdb, ILogger<BetterPostersUpdateTask> logger)
    {
        _tmdb = tmdb;
        _libraryManager = libraryManager;
        _posters = posters;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Update Better Posters (Local Quality Fix)";

    /// <inheritdoc />
    public string Key => "BetterPostersQualityFixUpdate";

    /// <inheritdoc />
    public string Description => "Puts posters with your files' real quality (4K, HDR, Dolby Vision, Atmos) on every movie, show, season and collection. Only titles that changed are made again.";

    /// <inheritdoc />
    public string Category => Plugin.PluginName;

    /// <inheritdoc />
    public bool IsHidden => false;

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <inheritdoc />
    public bool IsLogged => true;

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);
        var force = Plugin.Instance?.Configuration.ForceNextRun == true;
        if (force)
        {
            Plugin.Instance!.Configuration.ForceNextRun = false;
            Plugin.Instance.SaveConfiguration();
        }

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.BoxSet],
            Recursive = true,
            IsVirtualItem = false
        })
        // shows before their seasons
        .OrderBy(i => i is MediaBrowser.Controller.Entities.TV.Season ? 1 : 0)
        .ToList();

        var counts = new System.Collections.Concurrent.ConcurrentDictionary<PosterResult, int>();
        var done = 0;
        RunStatus.Start(items.Count, force);
        try
        {
            // three at a time (the poster service allows three downloads at once)
            await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken }, async (item, ct) =>
            {
                RunStatus.Current = item.Name;
                var result = await _posters.ApplyAsync(item, force, ct).ConfigureAwait(false);
                counts.AddOrUpdate(result, 1, (_, n) => n + 1);
                RunStatus.Count(result);
                progress.Report(Interlocked.Increment(ref done) * 100D / items.Count);
            }).ConfigureAwait(false);
        }
        finally
        {
            RunStatus.Finish(cancellationToken.IsCancellationRequested);
        }

        _tmdb.Prune();
        progress.Report(100);
        _logger.LogInformation(
            "Better Posters: {Updated} updated, {Unchanged} already up to date, {NotAvailable} no poster found, {Skipped} skipped, {Failed} failed (of {Total})",
            counts.GetValueOrDefault(PosterResult.Updated),
            counts.GetValueOrDefault(PosterResult.Unchanged),
            counts.GetValueOrDefault(PosterResult.NotAvailable),
            counts.GetValueOrDefault(PosterResult.Skipped),
            counts.GetValueOrDefault(PosterResult.Failed),
            items.Count);
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        // every night (new titles, upgraded files and changed settings are picked up)
        yield return new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = TimeSpan.FromHours(4).Ticks };
    }
}
