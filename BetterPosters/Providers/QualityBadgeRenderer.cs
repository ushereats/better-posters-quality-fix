using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
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
        // VideoRangeType (Jellyfin.Data.Enums.VideoRangeType) and VideoRange
        // (Jellyfin.Data.Enums.VideoRange) are both proper enums, not strings —
        // the original version of this method treated them as text and failed
        // to compile. VideoRangeType is the more specific/authoritative field;
        // VideoRange (Hdr/Sdr/Unknown) is the fallback for older records where
        // VideoRangeType never got populated.
        switch (videoStream.VideoRangeType)
        {
            case VideoRangeType.DOVI:
            case VideoRangeType.DOVIWithEL:
            case VideoRangeType.DOVIWithELHDR10Plus:
            case VideoRangeType.DOVIWithHDR10:
            case VideoRangeType.DOVIWithHDR10Plus:
            case VideoRangeType.DOVIWithHLG:
            case VideoRangeType.DOVIWithSDR:
                return "DV";
            case VideoRangeType.HDR10Plus:
                return "HDR10+";
            case VideoRangeType.HDR10:
                return "HDR";
            case VideoRangeType.HLG:
                return "HLG";
            case VideoRangeType.SDR:
                return null;
        }

        return videoStream.VideoRange == VideoRange.HDR ? "HDR" : null;
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

        var paddingX = image.Width * 0.035f;
        var paddingY = image.Width * 0.035f;
        var badgePaddingX = image.Width * 0.022f;
        var badgePaddingY = image.Width * 0.014f;

        var badgeWidth = measured.Width + (badgePaddingX * 2);
        var badgeHeight = measured.Height + (badgePaddingY * 2);

        var badgeX = image.Width - badgeWidth - paddingX;
        var badgeY = paddingY;

        // Plain rectangle rather than a rounded one — RoundedRectanglePolygon
        // and PathBuilder.AddRoundedRectangle only exist on newer
        // ImageSharp.Drawing major versions than the one pinned in the
        // csproj (2.1.6). Rounding it later is a version-bump job, not a
        // code-logic one.
        var badgeRect = new RectangularPolygon(badgeX, badgeY, badgeWidth, badgeHeight);

        // Soft drop shadow: same shape, offset slightly, drawn underneath at
        // lower opacity rather than a real gaussian blur (keeps this fast
        // and dependency-free).
        var shadowOffset = badgeHeight * 0.08f;
        var shadowRect = new RectangularPolygon(badgeX, badgeY + shadowOffset, badgeWidth, badgeHeight);

        // Accent color reflects the resolution tier so 4K/1080p/720p/SD are
        // distinguishable at a glance, not just by reading the text.
        Color accent;
        Color accentBorder;
        if (label.StartsWith("4K", StringComparison.Ordinal))
        {
            accent = Color.FromRgba(124, 58, 237, 255);
            accentBorder = Color.FromRgba(124, 58, 237, 230);
        }
        else if (label.StartsWith("1080p", StringComparison.Ordinal))
        {
            accent = Color.FromRgba(56, 189, 248, 255);
            accentBorder = Color.FromRgba(56, 189, 248, 230);
        }
        else if (label.StartsWith("720p", StringComparison.Ordinal))
        {
            accent = Color.FromRgba(148, 163, 184, 255);
            accentBorder = Color.FromRgba(148, 163, 184, 230);
        }
        else
        {
            accent = Color.FromRgba(107, 114, 128, 255);
            accentBorder = Color.FromRgba(107, 114, 128, 230);
        }

        image.Mutate(ctx =>
        {
            ctx.Fill(Color.FromRgba(0, 0, 0, 90), shadowRect);

            var backgroundGradient = new LinearGradientBrush(
                new PointF(badgeX, badgeY),
                new PointF(badgeX, badgeY + badgeHeight),
                GradientRepetitionMode.None,
                new ColorStop(0f, Color.FromRgba(24, 24, 27, 235)),
                new ColorStop(1f, Color.FromRgba(9, 9, 11, 235)));

            ctx.Fill(backgroundGradient, badgeRect);
            ctx.Draw(accentBorder, 1.5f, badgeRect);

            // Small accent dot before the text, same idea as a colored
            // status pill — cheap to draw, reads clearly at poster-card size.
            var dotRadius = badgeHeight * 0.16f;
            var dotCenter = new PointF(
                badgeX + badgePaddingX + dotRadius,
                badgeY + (badgeHeight / 2f));
            ctx.Fill(accent, new EllipsePolygon(dotCenter, dotRadius));

            var textPosition = new PointF(
                badgeX + badgePaddingX + (dotRadius * 2.6f),
                badgeY + badgePaddingY);
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
