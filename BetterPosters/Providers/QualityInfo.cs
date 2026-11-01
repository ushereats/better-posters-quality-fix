using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;

namespace BetterPosters.Providers;

/// <summary>
/// What a file really is, read from Jellyfin's own scan of it (the same data as "Media Info"):
/// resolution, HDR kind, Dolby Vision, Dolby audio.
/// </summary>
/// <param name="Resolution">"4K", "1440p", "1080p", "720p" or "SD".</param>
/// <param name="Hdr">"HDR10+", "HDR", "HLG" or null (not shown when the file is Dolby Vision).</param>
/// <param name="DolbyVision">Whether the video is Dolby Vision.</param>
/// <param name="DolbyAudio">"ATMOS", "DIGITAL" or null.</param>
public sealed record QualityInfo(string? Resolution, string? Hdr, bool DolbyVision, string? DolbyAudio)
{
    /// <summary>
    /// Gets a short text that changes whenever the badge would look different (used to skip unchanged posters).
    /// </summary>
    public string Key => string.Join('|', Resolution ?? "-", Hdr ?? "-", DolbyVision ? "dv" : "-", DolbyAudio ?? "-");

    /// <summary>
    /// Gets a value indicating whether there is anything to draw.
    /// </summary>
    public bool HasAnything => Resolution is not null || Hdr is not null || DolbyVision || DolbyAudio is not null;

    /// <summary>
    /// Reads the quality from an item's streams (the highest-resolution video stream, every audio stream).
    /// </summary>
    /// <param name="streams">The item's media streams.</param>
    /// <returns>The quality, or null when there is no video stream.</returns>
    public static QualityInfo? FromStreams(IReadOnlyList<MediaStream> streams) => FromStreams(streams, null);

    // rangeOf: tests supply the range type directly (Jellyfin works it out from the stream's colour fields)
    internal static QualityInfo? FromStreams(IReadOnlyList<MediaStream> streams, Func<MediaStream, VideoRangeType>? rangeOf)
    {
        ArgumentNullException.ThrowIfNull(streams);
        rangeOf ??= s => s.VideoRangeType;

        var video = streams
            .Where(s => s.Type == MediaStreamType.Video && !s.IsExternal)
            .OrderByDescending(s => (long)(s.Width ?? 0) * (s.Height ?? 0))
            .FirstOrDefault();
        if (video is null)
        {
            return null;
        }

        var resolution = GetResolution(video.Width ?? 0, video.Height ?? 0);
        var range = rangeOf(video);
        var dolbyVision = IsDolbyVision(range);
        var hdr = dolbyVision ? null : GetHdr(video, range);

        string? audio = null;
        foreach (var a in streams.Where(s => s.Type == MediaStreamType.Audio))
        {
            var text = string.Join(' ', a.Profile, a.DisplayTitle, a.Title, a.Codec).ToUpperInvariant();
            if (text.Contains("ATMOS", StringComparison.Ordinal) || text.Contains("JOC", StringComparison.Ordinal))
            {
                audio = "ATMOS";
                break;
            }

            if (a.Codec is not null && (a.Codec.Equals("eac3", StringComparison.OrdinalIgnoreCase) || a.Codec.Equals("ac3", StringComparison.OrdinalIgnoreCase) || a.Codec.Equals("truehd", StringComparison.OrdinalIgnoreCase)))
            {
                audio = "DIGITAL";
            }
        }

        return new QualityInfo(resolution, hdr, dolbyVision, audio);
    }

    /// <summary>
    /// Picks the better of two qualities (higher resolution first, then HDR / Dolby Vision, then Dolby audio).
    /// </summary>
    /// <param name="a">One quality.</param>
    /// <param name="b">The other.</param>
    /// <returns>The better one.</returns>
    public static QualityInfo? Best(QualityInfo? a, QualityInfo? b)
    {
        if (a is null)
        {
            return b;
        }

        if (b is null)
        {
            return a;
        }

        return Score(b) > Score(a) ? b : a;
    }

    /// <summary>
    /// The resolution label for a frame size. Uses either edge, so letterboxed / cropped films still count
    /// (a 3840x1600 film is 4K, a 1920x800 film is 1080p).
    /// </summary>
    /// <param name="width">Frame width.</param>
    /// <param name="height">Frame height.</param>
    /// <returns>The label, or null for an unknown size.</returns>
    public static string? GetResolution(int width, int height)
    {
        if (width >= 3200 || height >= 2000)
        {
            return "4K";
        }

        if (width >= 2200 || height >= 1300)
        {
            return "1440p";
        }

        if (width >= 1800 || height >= 1000)
        {
            return "1080p";
        }

        if (width >= 1200 || height >= 700)
        {
            return "720p";
        }

        return width > 0 || height > 0 ? "SD" : null;
    }

    private static bool IsDolbyVision(VideoRangeType type) => type is VideoRangeType.DOVI
        or VideoRangeType.DOVIWithEL
        or VideoRangeType.DOVIWithELHDR10Plus
        or VideoRangeType.DOVIWithHDR10
        or VideoRangeType.DOVIWithHDR10Plus
        or VideoRangeType.DOVIWithHLG
        or VideoRangeType.DOVIWithSDR;

    private static string? GetHdr(MediaStream video, VideoRangeType range)
    {
        switch (range)
        {
            case VideoRangeType.HDR10Plus:
                return "HDR10+";
            case VideoRangeType.HDR10:
                return "HDR";
            case VideoRangeType.HLG:
                return "HLG";
            case VideoRangeType.SDR:
                return null;
        }

        return video.VideoRange == VideoRange.HDR ? "HDR" : null;
    }

    private static int Score(QualityInfo q)
    {
        var res = q.Resolution switch
        {
            "4K" => 50,
            "1440p" => 40,
            "1080p" => 30,
            "720p" => 20,
            "SD" => 10,
            _ => 0
        };
        return res + (q.DolbyVision ? 4 : q.Hdr is not null ? 3 : 0) + (q.DolbyAudio == "ATMOS" ? 2 : q.DolbyAudio is not null ? 1 : 0);
    }
}
