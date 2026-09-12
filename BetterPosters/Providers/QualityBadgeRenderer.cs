using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Model.Entities;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BetterPosters.Providers;

/// <summary>
/// Computes a real quality label from Jellyfin's own MediaStream data and draws it
/// onto a downloaded poster image, replacing btttr.cc's externally-sourced badge
/// (which is not derived from the user's actual local file).
/// </summary>
public static class QualityBadgeRenderer
{
    private static readonly string[] FontFallbacks =
    [
        "DejaVu Sans", "Liberation Sans", "Noto Sans", "Arial", "Helvetica", "Sans Serif"
    ];

    /// <summary>
    /// Derives a short quality label (e.g. "4K DV", "1080p HDR", "720p") from the
    /// highest-resolution video stream found.
    /// </summary>
    /// <param name="streams">Media streams for the item (or the item's best-quality episode, for a series).</param>
    /// <returns>A short label, or null if no video stream was found.</returns>
    public static string? GetQualityLabel(IReadOnlyList<MediaStream> streams)
    {
        var videoStream = streams
            .Where(s => s.Type == MediaStreamType.Video)
            .OrderByDescending(s => (long)(s.Width ?? 0) * (s.Height ?? 0))
            .FirstOrDefault();

        if (videoStream is null)
        {
            return null;
        }

        var height = videoStream.Height ?? 0;
        var width = videoStream.Width ?? 0;
        var longEdge = Math.Max(width, height);

        var resolutionLabel = longEdge switch
        {
            >= 3840 => "4K",
            >= 1920 => "1080p",
            >= 1280 => "720p",
            > 0 => "SD",
            _ => null
        };

        if (resolutionLabel is null)
        {
            return null;
        }

        var rangeSuffix = GetRangeSuffix(videoStream);
        return rangeSuffix is null ? resolutionLabel : $"{resolutionLabel} {rangeSuffix}";
    }

    private static string? GetRangeSuffix(MediaStream videoStream)
    {
        // VideoRangeType is the authoritative field on modern Jellyfin builds.
        // VideoRange (free-text string, e.g. "HDR"/"SDR") is kept as a fallback
        // for older metadata that never got VideoRangeType populated.
        var rangeType = videoStream.VideoRangeType.ToString();

        if (rangeType.Contains("DolbyVision", StringComparison.OrdinalIgnoreCase))
        {
            return "DV";
        }

        if (rangeType.Contains("HDR10Plus", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR10+";
        }

        if (rangeType.Contains("HDR", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR";
        }

        if (rangeType.Contains("HLG", StringComparison.OrdinalIgnoreCase))
        {
            return "HLG";
        }

        if (!string.IsNullOrWhiteSpace(videoStream.VideoRange) &&
            videoStream.VideoRange.Contains("HDR", StringComparison.OrdinalIgnoreCase))
        {
            return "HDR";
        }

        return null;
    }

    /// <summary>
    /// Draws a badge with the given label onto the top-right corner of the poster.
    /// </summary>
    /// <param name="posterBytes">The original poster image bytes.</param>
    /// <param name="label">The label to draw (e.g. "4K HDR").</param>
    /// <returns>Re-encoded JPEG bytes with the badge applied.</returns>
    public static byte[] ApplyBadge(byte[] posterBytes, string label)
    {
        using var image = Image.Load<Rgba32>(posterBytes);

        var font = ResolveFont(image.Width);
        var textOptions = new RichTextOptions(font)
        {
            Origin = default,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };

        var measured = TextMeasurer.MeasureSize(label, textOptions);

        var paddingX = image.Width * 0.03f;
        var paddingY = image.Width * 0.03f;
        var badgePaddingX = image.Width * 0.018f;
        var badgePaddingY = image.Width * 0.012f;

        var badgeWidth = measured.Width + (badgePaddingX * 2);
        var badgeHeight = measured.Height + (badgePaddingY * 2);

        var badgeX = image.Width - badgeWidth - paddingX;
        var badgeY = paddingY;

        // Plain rectangle rather than a hand-rolled rounded-rect path — kept simple
        // and dependency-light since this could not be compile-tested in this
        // environment (see CHANGES.md). Swap in a rounded corner shape later if
        // ImageSharp.Drawing's arc-path API differs from what's assumed here.
        var badgeRect = new RectangularPolygon(badgeX, badgeY, badgeWidth, badgeHeight);

        image.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgba(0, 0, 0, 178), badgeRect);
            ctx.Draw(Color.FromRgba(255, 255, 255, 90), 1.5f, badgeRect);

            var textPosition = new PointF(badgeX + badgePaddingX, badgeY + badgePaddingY);
            ctx.DrawText(
                new RichTextOptions(font)
                {
                    Origin = textPosition,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top
                },
                label,
                Color.White);
        });

        using var output = new MemoryStream();
        image.SaveAsJpeg(output);
        return output.ToArray();
    }

    private static Font ResolveFont(int posterWidth)
    {
        var size = Math.Max(18f, posterWidth * 0.045f);

        foreach (var name in FontFallbacks)
        {
            if (SystemFonts.Collection.TryGet(name, out var family))
            {
                return family.CreateFont(size, FontStyle.Bold);
            }
        }

        // Last resort: whatever the first registered system family is.
        var fallback = SystemFonts.Collection.Families.FirstOrDefault();
        if (fallback.Name is not null)
        {
            return fallback.CreateFont(size, FontStyle.Bold);
        }

        throw new InvalidOperationException(
            "No system fonts are available to render the quality badge. Install a font " +
            "package (e.g. fonts-dejavu-core) in the Jellyfin container/host.");
    }
}
