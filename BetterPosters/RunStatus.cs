using System;
using System.Threading;

namespace BetterPosters;

/// <summary>
/// How the poster run is going, for the live progress on the settings page.
/// </summary>
public static class RunStatus
{
    private static int s_done;
    private static int s_updated;
    private static int s_unchanged;
    private static int s_notFound;
    private static int s_skipped;
    private static int s_failed;

    /// <summary>Gets a value indicating whether a run is going.</summary>
    public static bool Running { get; private set; }

    /// <summary>Gets a value indicating whether the run replaces every poster.</summary>
    public static bool ReplaceAll { get; private set; }

    /// <summary>Gets the number of titles in the run.</summary>
    public static int Total { get; private set; }

    /// <summary>Gets or sets the title being done now.</summary>
    public static string? Current { get; set; }

    /// <summary>Gets when the run started.</summary>
    public static DateTime? StartedUtc { get; private set; }

    /// <summary>Gets when the run ended.</summary>
    public static DateTime? FinishedUtc { get; private set; }

    /// <summary>Gets a value indicating whether the run was stopped.</summary>
    public static bool Cancelled { get; private set; }

    /// <summary>A run starts.</summary>
    /// <param name="total">Titles in it.</param>
    /// <param name="replaceAll">Every poster replaced.</param>
    public static void Start(int total, bool replaceAll)
    {
        s_done = s_updated = s_unchanged = s_notFound = s_skipped = s_failed = 0;
        Total = total;
        ReplaceAll = replaceAll;
        Current = null;
        Cancelled = false;
        StartedUtc = DateTime.UtcNow;
        FinishedUtc = null;
        Running = true;
    }

    /// <summary>One title done.</summary>
    /// <param name="result">What happened.</param>
    public static void Count(PosterResult result)
    {
        Interlocked.Increment(ref s_done);
        switch (result)
        {
            case PosterResult.Updated: Interlocked.Increment(ref s_updated); break;
            case PosterResult.Unchanged: Interlocked.Increment(ref s_unchanged); break;
            case PosterResult.NotAvailable: Interlocked.Increment(ref s_notFound); break;
            case PosterResult.Skipped: Interlocked.Increment(ref s_skipped); break;
            default: Interlocked.Increment(ref s_failed); break;
        }
    }

    /// <summary>The run ends.</summary>
    /// <param name="cancelled">It was stopped.</param>
    public static void Finish(bool cancelled)
    {
        Running = false;
        Cancelled = cancelled;
        Current = null;
        FinishedUtc = DateTime.UtcNow;
    }

    /// <summary>Gets the status as the settings page reads it.</summary>
    /// <returns>The status.</returns>
    public static object Snapshot() => new
    {
        running = Running,
        replaceAll = ReplaceAll,
        total = Total,
        done = s_done,
        updated = s_updated,
        unchanged = s_unchanged,
        notFound = s_notFound,
        skipped = s_skipped,
        failed = s_failed,
        current = Current,
        startedUtc = StartedUtc,
        finishedUtc = FinishedUtc,
        cancelled = Cancelled
    };
}
