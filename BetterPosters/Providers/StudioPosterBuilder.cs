using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterPosters.Configuration;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BetterPosters.Providers;

/// <summary>
/// The info printed under the title on a studio poster.
/// </summary>
/// <param name="Title">The title (drawn as text when there's no logo).</param>
/// <param name="Rating">Community rating out of 10, or null.</param>
/// <param name="Year">Year, or null.</param>
/// <param name="Genre">First genre, or null.</param>
/// <param name="AgeRating">Age rating (PG-13, TV-MA...), or null.</param>
/// <param name="Tag">The coloured tag at the top (studio / network / genre), or null.</param>
/// <param name="Lead">Shown first in the info line ("Season 2", "4 films"), or null.</param>
public sealed record StudioInfo(string Title, float? Rating, int? Year, string? Genre, string? AgeRating, string? Tag = null, string? Lead = null);

/// <summary>
/// Studio posters: the title's clean artwork (no text on it) from TMDb, its logo placed at the bottom over a soft
/// fade, a small info line (rating, year, genre) and the real quality badges at the top. Titles TMDb has no clean
/// artwork for return null, and the caller falls back to btttr.cc's poster.
/// </summary>
public sealed class StudioPosterBuilder
{
    /// <summary>Width of the posters made (2:3).</summary>
    public const int Width = 1000;

    /// <summary>Height of the posters made.</summary>
    public const int Height = 1500;

    private const string Api = "https://api.themoviedb.org/3/";
    private const string Images = "https://image.tmdb.org/t/p/";

    private readonly TmdbClient _tmdb;
    private readonly ILogger<StudioPosterBuilder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StudioPosterBuilder"/> class.
    /// </summary>
    /// <param name="tmdb">TMDb (cached).</param>
    /// <param name="logger">The logger.</param>
    public StudioPosterBuilder(TmdbClient tmdb, ILogger<StudioPosterBuilder> logger)
    {
        _tmdb = tmdb;
        _logger = logger;
    }

    /// <summary>
    /// Makes the studio poster for a title.
    /// </summary>
    /// <param name="item">The movie or show.</param>
    /// <param name="quality">The quality badges to draw, or null.</param>
    /// <param name="config">The settings.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The poster as a JPEG, or null when TMDb has no clean artwork for it.</returns>
    public async Task<byte[]?> BuildAsync(BaseItem item, QualityInfo? quality, PluginConfiguration config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);
        var key = ApiKey(config);
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogWarning("Studio posters need a TMDb key: none in the settings and Jellyfin's couldn't be found");
            return null;
        }

        var lang = BetterPosterUrlBuilder.GetLanguageCode(config.Language)?.Split('-')[0] ?? "en";
        var langs = $"include_image_language={lang},en,null";
        var k = Uri.EscapeDataString(key);
        var info = InfoFor(item, config);

        // which TMDb lists hold the artwork (and the logos)
        string? artList;
        string? logoList;
        switch (item)
        {
            case Season season:
            {
                var show = season.Series;
                var showId = show is null ? null : await TmdbIdAsync(show, "tv", key, cancellationToken).ConfigureAwait(false);
                if (showId is null || season.IndexNumber is not { } n)
                {
                    return null;
                }

                artList = $"{Api}tv/{showId}/season/{n}/images?api_key={k}&{langs}";
                logoList = $"{Api}tv/{showId}/images?api_key={k}&{langs}";
                break;
            }

            case BoxSet set:
            {
                var id = set.GetProviderId(MetadataProvider.TmdbCollection) ?? set.GetProviderId(MetadataProvider.Tmdb);
                if (string.IsNullOrWhiteSpace(id))
                {
                    return null;
                }

                artList = $"{Api}collection/{Uri.EscapeDataString(id)}/images?api_key={k}&{langs}";
                logoList = null;   // TMDb has no logos for collections: the name is written in text
                break;
            }

            default:
            {
                var kind = item is Series ? "tv" : "movie";
                var id = await TmdbIdAsync(item, kind, key, cancellationToken).ConfigureAwait(false);
                if (id is null)
                {
                    return null;
                }

                artList = $"{Api}{kind}/{id}/images?api_key={k}&{langs}";
                logoList = artList;
                break;
            }
        }

        using var doc = await _tmdb.GetJsonAsync(artList, cancellationToken).ConfigureAwait(false);
        if (doc is null)
        {
            return null;
        }

        // clean artwork: posters with no language are the ones without the title printed on them
        var art = Pick(doc.RootElement, "posters", p => Lang(p) is null);
        if (art is null && item is Season)
        {
            // a season with no clean artwork of its own: its show's clean artwork, with "Season 2" underneath
            using var showDoc = logoList is null ? null : await _tmdb.GetJsonAsync(logoList, cancellationToken).ConfigureAwait(false);
            art = showDoc is null ? null : Pick(showDoc.RootElement, "posters", p => Lang(p) is null);
        }

        if (art is null)
        {
            // no clean artwork: the full-size official poster (its title is printed on it, so nothing is added but
            // the quality badges) - much sharper than btttr.cc's 500 px fallback
            var printed = Pick(doc.RootElement, "posters", p => Lang(p) == lang) ?? Pick(doc.RootElement, "posters", p => Lang(p) == "en");
            var printedBytes = printed is null ? null : await _tmdb.GetImageAsync($"{Images}original{printed}", cancellationToken).ConfigureAwait(false);
            return printedBytes is null ? null : Compose(printedBytes, null, info, quality, config, printed: true);
        }

        string? logoPath = null;
        if (config.StudioLogo && logoList is not null)
        {
            using var logos = logoList == artList ? null : await _tmdb.GetJsonAsync(logoList, cancellationToken).ConfigureAwait(false);
            var root = (logos ?? doc).RootElement;
            logoPath = Pick(root, "logos", p => Lang(p) == lang && IsPng(p))
                ?? Pick(root, "logos", p => Lang(p) == "en" && IsPng(p))
                ?? Pick(root, "logos", p => Lang(p) is null && IsPng(p));
        }

        var artBytes = await _tmdb.GetImageAsync($"{Images}original{art}", cancellationToken).ConfigureAwait(false);
        if (artBytes is null)
        {
            return null;
        }

        byte[]? logoBytes = logoPath is null ? null : await _tmdb.GetImageAsync($"{Images}original{logoPath}", cancellationToken).ConfigureAwait(false);
        return Compose(artBytes, logoBytes, info, quality, config);
    }

    private async Task<string?> TmdbIdAsync(BaseItem item, string kind, string key, CancellationToken cancellationToken)
    {
        var id = item.GetProviderId(MetadataProvider.Tmdb);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = await FindByImdbAsync(item.GetProviderId(MetadataProvider.Imdb), kind, key, cancellationToken).ConfigureAwait(false);
        }

        return string.IsNullOrWhiteSpace(id) ? null : Uri.EscapeDataString(id);
    }

    /// <summary>
    /// The info line for a title, as the settings ask.
    /// </summary>
    /// <param name="item">The title.</param>
    /// <param name="config">The settings.</param>
    /// <returns>The info.</returns>
    public static StudioInfo InfoFor(BaseItem item, PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(config);
        // seasons take their show's studio, genre and age rating; their own name ("Season 2") leads the info line
        var source = item is Season { Series: { } show } ? show : item;
        var tag = config.StudioTopTag switch
        {
            StudioTag.Studio => source.Studios?.FirstOrDefault(),
            StudioTag.Genre => source.Genres?.FirstOrDefault(),
            _ => null
        };
        if (item is Season season)
        {
            return new StudioInfo(
                source.Name ?? string.Empty,
                null,
                config.StudioShowYear ? season.ProductionYear : null,
                config.EnableGenre && config.StudioTopTag != StudioTag.Genre ? source.Genres?.FirstOrDefault() : null,
                config.EnableAgeRating && !string.IsNullOrWhiteSpace(source.OfficialRating) ? source.OfficialRating : null,
                string.IsNullOrWhiteSpace(tag) ? null : tag.Trim(),
                season.Name);
        }

        if (item is BoxSet set)
        {
            var films = set.GetLinkedChildren().Count(c => c is MediaBrowser.Controller.Entities.Movies.Movie);
            return new StudioInfo(
                set.Name ?? string.Empty,
                null,
                null,
                null,
                null,
                null,
                films > 0 ? films.ToString(CultureInfo.InvariantCulture) + (films == 1 ? " film" : " films") : null);
        }

        return new StudioInfo(
            item.Name ?? string.Empty,
            config.EnableRating && item.CommunityRating is > 0 ? item.CommunityRating : null,
            config.StudioShowYear ? item.ProductionYear : null,
            // the genre isn't repeated in the info line when it's already the tag
            config.EnableGenre && config.StudioTopTag != StudioTag.Genre ? item.Genres?.FirstOrDefault() : null,
            config.EnableAgeRating && !string.IsNullOrWhiteSpace(item.OfficialRating) ? item.OfficialRating : null,
            string.IsNullOrWhiteSpace(tag) ? null : tag.Trim());
    }

    /// <summary>
    /// Puts a studio poster together (no network: used by the tests too).
    /// </summary>
    /// <param name="artBytes">The clean artwork.</param>
    /// <param name="logoBytes">The title logo (transparent PNG), or null for the title in text.</param>
    /// <param name="info">The info line.</param>
    /// <param name="quality">The quality badges, or null.</param>
    /// <param name="config">The settings.</param>
    /// <param name="printed">The artwork already has its title printed on it: only the badges are added.</param>
    /// <returns>The poster as a JPEG.</returns>
    public static byte[] Compose(byte[] artBytes, byte[]? logoBytes, StudioInfo info, QualityInfo? quality, PluginConfiguration config, bool printed = false)
    {
        ArgumentNullException.ThrowIfNull(artBytes);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(config);
        using var image = Image.Load<Rgba32>(artBytes);
        image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(Width, Height), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center, Sampler = KnownResamplers.Lanczos3 }));

        var meta = printed ? new List<(bool, string)>() : MetaParts(info);
        var fade = printed ? 0 : Math.Clamp(config.StudioFade, 0, 100) / 100f;

        // soft dark fade at the bottom for the title and info line, and a light one at the top for the badges
        if (fade > 0)
        {
            var top = Height * 0.42f;
            var brush = new LinearGradientBrush(
                new PointF(0, top),
                new PointF(0, Height),
                GradientRepetitionMode.None,
                new ColorStop(0f, Color.FromRgba(0, 0, 0, 0)),
                new ColorStop(0.45f, Color.FromRgba(0, 0, 0, (byte)(255 * fade * 0.45f))),
                new ColorStop(1f, Color.FromRgba(0, 0, 0, (byte)(255 * fade))));
            image.Mutate(x => x.Fill(brush, new RectangularPolygon(0, top, Width, Height - top)));
        }

        if (quality is not null && quality.HasAnything)
        {
            var topBrush = new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(0, Height * 0.14f),
                GradientRepetitionMode.None,
                new ColorStop(0f, Color.FromRgba(0, 0, 0, 60)),
                new ColorStop(1f, Color.FromRgba(0, 0, 0, 0)));
            image.Mutate(x => x.Fill(topBrush, new RectangularPolygon(0, 0, Width, Height * 0.14f)));
        }

        // title: logo (or text) centred, its bottom just above the info line
        var titleBottom = Height * (meta.Count > 0 ? 0.878f : 0.93f);
        var drewLogo = printed;
        if (logoBytes is not null && !printed)
        {
            try
            {
                using var logo = Image.Load<Rgba32>(logoBytes);
                Trim(logo);
                var k = Math.Min(Width * 0.8f / logo.Width, Height * 0.22f / logo.Height);
                var w = Math.Max(1, (int)Math.Round(logo.Width * k));
                var h = Math.Max(1, (int)Math.Round(logo.Height * k));
                logo.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(w, h), Mode = ResizeMode.Stretch, Sampler = KnownResamplers.Lanczos3 }));
                var at = new Point((Width - w) / 2, (int)Math.Round(titleBottom - h));
                Shadowed(image, logo, at, 0.55f, 7f, 3);
                drewLogo = true;
            }
            catch (Exception ex) when (ex is ImageFormatException or UnknownImageFormatException or InvalidImageContentException)
            {
                drewLogo = false;
            }
        }

        if (!drewLogo && Drawable(info.Title))
        {
            DrawTitle(image, info.Title, titleBottom);
        }

        if (meta.Count > 0)
        {
            DrawMeta(image, meta, Height * 0.918f);
        }

        var below = 0;
        if (!printed && info.Tag is { } tag && Drawable(tag))
        {
            below = DrawTag(image, tag, Accent(image));
        }

        if (quality is not null && quality.HasAnything && config.EnableQualityTags)
        {
            QualityBadgeRenderer.Draw(image, quality, below);
        }

        using var output = new MemoryStream();
        // high quality, full colour detail (no colour subsampling: sharp edges on logos and badges)
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 95, ColorType = JpegEncodingColor.YCbCrRatio444 });
        return output.ToArray();
    }

    private static List<(bool Star, string Text)> MetaParts(StudioInfo info)
    {
        var parts = new List<(bool, string)>();
        if (!string.IsNullOrWhiteSpace(info.Lead) && Drawable(info.Lead))
        {
            parts.Add((false, info.Lead));
        }

        if (info.Rating is { } r)
        {
            parts.Add((true, r.ToString("0.0", CultureInfo.InvariantCulture)));
        }

        if (info.Year is { } y)
        {
            parts.Add((false, y.ToString(CultureInfo.InvariantCulture)));
        }

        if (!string.IsNullOrWhiteSpace(info.Genre) && Drawable(info.Genre))
        {
            parts.Add((false, info.Genre));
        }

        if (!string.IsNullOrWhiteSpace(info.AgeRating) && Drawable(info.AgeRating))
        {
            parts.Add((false, info.AgeRating));
        }

        return parts;
    }

    // the built-in font covers Latin text only: other scripts are left out rather than drawn as boxes
    private static bool Drawable(string text) => text.All(c => c < 0x250 || c is '•' or '–' or '—' or '’');

    private static void DrawMeta(Image<Rgba32> image, List<(bool Star, string Text)> parts, float centerY)
    {
        var font = QualityBadgeRenderer.Font(700, Height * 0.027f);
        var size = font.Size;
        var gap = size * 0.55f;
        var dot = "•";
        var dotW = QualityBadgeRenderer.Advance(dot, font, 0);
        var starW = size * 0.95f;
        var total = 0f;
        for (var i = 0; i < parts.Count; i++)
        {
            total += (parts[i].Star ? starW + (size * 0.28f) : 0) + QualityBadgeRenderer.Advance(parts[i].Text, font, 0);
            if (i < parts.Count - 1)
            {
                total += gap + dotW + gap;
            }
        }

        var colour = Color.FromRgba(255, 255, 255, 190);
        using var layer = new Image<Rgba32>(Width, Height);
        var x = (Width - total) / 2f;
        layer.Mutate(ctx =>
        {
            for (var i = 0; i < parts.Count; i++)
            {
                if (parts[i].Star)
                {
                    ctx.Fill(colour, Star(x + (starW / 2f), centerY - (size * 0.04f), starW / 2f));
                    x += starW + (size * 0.28f);
                }

                QualityBadgeRenderer.DrawText(ctx, parts[i].Text, font, 0, x, centerY, colour);
                x += QualityBadgeRenderer.Advance(parts[i].Text, font, 0);
                if (i < parts.Count - 1)
                {
                    x += gap;
                    QualityBadgeRenderer.DrawText(ctx, dot, font, 0, x, centerY, Color.FromRgba(255, 255, 255, 140));
                    x += dotW + gap;
                }
            }
        });
        Shadowed(image, layer, new Point(0, 0), 0.6f, 4f, 2);
    }

    // the coloured tag hanging from the top edge (rounded at the bottom), white text; returns its bottom
    private static int DrawTag(Image<Rgba32> image, string text, Color fill)
    {
        var size = Height * 0.03f;
        var font = QualityBadgeRenderer.Font(700, size);
        var maxText = Width * 0.62f;
        while (size > Height * 0.02f && QualityBadgeRenderer.Advance(text, font, 0) > maxText)
        {
            size *= 0.93f;
            font = QualityBadgeRenderer.Font(700, size);
        }

        var textW = QualityBadgeRenderer.Advance(text, font, 0);
        if (textW > maxText)
        {
            return 0;   // still too long: no tag rather than a squashed one
        }

        var padX = Height * 0.03f;
        var h = Height * 0.058f;
        var r = Height * 0.016f;
        var w = textW + (2 * padX);
        var x = (Width - w) / 2f;
        using var layer = new Image<Rgba32>(Width, (int)Math.Ceiling(h + (r * 2)));
        layer.Mutate(ctx =>
        {
            // drawn from above the edge so only the bottom corners show as rounded
            ctx.Fill(fill, RoundedRect(x, -r, w, h + r, r));
            QualityBadgeRenderer.DrawText(ctx, text, font, 0, x + padX, h / 2f, Color.White);
        });
        Shadowed(image, layer, new Point(0, 0), 0.35f, 6f, 2);
        return (int)Math.Ceiling(h);
    }

    // the poster's main colour (the most common strong colour), darkened enough for white text
    private static Color Accent(Image<Rgba32> image)
    {
        var bins = new double[12, 4];   // hue bins: weight, r, g, b
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y += 6)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x += 6)
                {
                    var p = row[x];
                    float r = p.R / 255f, g = p.G / 255f, b = p.B / 255f;
                    float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
                    if (max < 0.22f || d / max < 0.35f)
                    {
                        continue;
                    }

                    var hue = d == 0 ? 0 : max == r ? ((g - b) / d % 6) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;
                    var bin = ((int)Math.Floor((hue < 0 ? hue + 6 : hue) * 2)) % 12;
                    var wgt = d * max;
                    bins[bin, 0] += wgt;
                    bins[bin, 1] += p.R * wgt;
                    bins[bin, 2] += p.G * wgt;
                    bins[bin, 3] += p.B * wgt;
                }
            }
        });

        var best = 0;
        for (var i = 1; i < 12; i++)
        {
            if (bins[i, 0] > bins[best, 0]) { best = i; }
        }

        if (bins[best, 0] <= 0)
        {
            return Color.FromRgb(38, 38, 42);   // no strong colour: dark grey
        }

        double cr = bins[best, 1] / bins[best, 0], cg = bins[best, 2] / bins[best, 0], cb = bins[best, 3] / bins[best, 0];
        // white text needs it darkish: scale down until its brightness is at most ~50%
        var lum = ((0.2126 * cr) + (0.7152 * cg) + (0.0722 * cb)) / 255;
        var k = lum > 0.42 ? 0.42 / lum : 1;
        // and a little more vivid, like the posters it comes from
        double mean = (cr + cg + cb) / 3;
        cr = Math.Clamp(mean + ((cr - mean) * 1.15), 0, 255) * k;
        cg = Math.Clamp(mean + ((cg - mean) * 1.15), 0, 255) * k;
        cb = Math.Clamp(mean + ((cb - mean) * 1.15), 0, 255) * k;
        return Color.FromRgb((byte)cr, (byte)cg, (byte)cb);
    }

    private static IPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        var pts = new List<PointF>();
        void Arc(float cx, float cy, float start)
        {
            for (var i = 0; i <= 8; i++)
            {
                var a = (start + (90f * i / 8f)) * MathF.PI / 180f;
                pts.Add(new PointF(cx + (r * MathF.Cos(a)), cy + (r * MathF.Sin(a))));
            }
        }

        Arc(x + w - r, y + r, 270);
        Arc(x + w - r, y + h - r, 0);
        Arc(x + r, y + h - r, 90);
        Arc(x + r, y + r, 180);
        return new Polygon(new LinearLineSegment(pts.ToArray()));
    }

    private static void DrawTitle(Image<Rgba32> image, string title, float bottom)
    {
        var size = Height * 0.075f;
        var font = QualityBadgeRenderer.Font(800, size);
        var max = Width * 0.84f;
        while (size > Height * 0.038f && QualityBadgeRenderer.Advance(title, font, 0) > max)
        {
            size *= 0.92f;
            font = QualityBadgeRenderer.Font(800, size);
        }

        var w = QualityBadgeRenderer.Advance(title, font, 0);
        using var layer = new Image<Rgba32>(Width, Height);
        layer.Mutate(ctx => QualityBadgeRenderer.DrawText(ctx, title, font, 0, (Width - w) / 2f, bottom - (size * 0.55f), Color.White));
        Shadowed(image, layer, new Point(0, 0), 0.6f, 8f, 3);
    }

    // draws a layer with a soft shadow under it
    private static void Shadowed(Image<Rgba32> image, Image<Rgba32> layer, Point at, float strength, float blur, int drop)
    {
        using var shadow = layer.Clone(ctx => ctx
            .ProcessPixelRowsAsVector4(row =>
            {
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = new System.Numerics.Vector4(0, 0, 0, row[i].W * strength);
                }
            })
            .GaussianBlur(blur));
        image.Mutate(ctx =>
        {
            ctx.DrawImage(shadow, new Point(at.X, at.Y + drop), 1f);
            ctx.DrawImage(layer, at, 1f);
        });
    }

    // logos often come with empty transparent borders: cut them off so sizing and centring are right
    private static void Trim(Image<Rgba32> logo)
    {
        int minX = logo.Width, minY = logo.Height, maxX = -1, maxY = -1;
        logo.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x].A > 12)
                    {
                        if (x < minX) { minX = x; }
                        if (x > maxX) { maxX = x; }
                        if (y < minY) { minY = y; }
                        if (y > maxY) { maxY = y; }
                    }
                }
            }
        });
        if (maxX >= minX && maxY >= minY && (maxX - minX + 1 < logo.Width || maxY - minY + 1 < logo.Height))
        {
            logo.Mutate(x => x.Crop(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1)));
        }
    }

    private static IPath Star(float cx, float cy, float r)
    {
        var pts = new PointF[10];
        for (var i = 0; i < 10; i++)
        {
            var radius = i % 2 == 0 ? r : r * 0.45f;
            var a = (-90f + (36f * i)) * MathF.PI / 180f;
            pts[i] = new PointF(cx + (radius * MathF.Cos(a)), cy + (radius * MathF.Sin(a)));
        }

        return new Polygon(new LinearLineSegment(pts));
    }

    private static string? Lang(JsonElement p) =>
        p.TryGetProperty("iso_639_1", out var l) && l.ValueKind == JsonValueKind.String && l.GetString() is { Length: > 0 } s && s != "xx" ? s : null;

    private static bool IsPng(JsonElement p) =>
        p.TryGetProperty("file_path", out var f) && (f.GetString() ?? string.Empty).EndsWith(".png", StringComparison.OrdinalIgnoreCase);

    // the best-voted image of a kind (most votes as the tie-break)
    private static string? Pick(JsonElement root, string kind, Func<JsonElement, bool> filter)
    {
        if (!root.TryGetProperty(kind, out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return list.EnumerateArray()
            .Where(filter)
            .OrderByDescending(p => p.TryGetProperty("vote_average", out var v) && v.TryGetDouble(out var d) ? d : 0)
            .ThenByDescending(p => p.TryGetProperty("vote_count", out var c) && c.TryGetInt32(out var n) ? n : 0)
            .Select(p => p.TryGetProperty("file_path", out var f) ? f.GetString() : null)
            .FirstOrDefault(f => !string.IsNullOrEmpty(f));
    }

    private async Task<string?> FindByImdbAsync(string? imdbId, string kind, string key, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return null;
        }

        using var doc = await _tmdb.GetJsonAsync($"{Api}find/{Uri.EscapeDataString(imdbId)}?external_source=imdb_id&api_key={Uri.EscapeDataString(key)}", cancellationToken).ConfigureAwait(false);
        if (doc is null || !doc.RootElement.TryGetProperty(kind == "tv" ? "tv_results" : "movie_results", out var results))
        {
            return null;
        }

        foreach (var r in results.EnumerateArray())
        {
            if (r.TryGetProperty("id", out var id))
            {
                return id.ToString();
            }
        }

        return null;
    }

    // the plugin's own key, else the one Jellyfin's built-in TMDb plugin uses (its settings, or its built-in key)
    private static string? ApiKey(PluginConfiguration config)
    {
        if (!string.IsNullOrWhiteSpace(config.TmdbApiKey))
        {
            return config.TmdbApiKey.Trim();
        }

        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "MediaBrowser.Providers");
            var pluginType = asm?.GetType("MediaBrowser.Providers.Plugins.Tmdb.Plugin");
            var instance = pluginType?.GetProperty("Instance")?.GetValue(null);
            var cfg = instance?.GetType().GetProperty("Configuration")?.GetValue(instance);
            if (cfg?.GetType().GetProperty("TmdbApiKey")?.GetValue(cfg) is string own && !string.IsNullOrWhiteSpace(own))
            {
                return own;
            }

            return asm?.GetType("MediaBrowser.Providers.Plugins.Tmdb.TmdbUtils")?.GetField("ApiKey")?.GetValue(null) as string;
        }
        catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or MemberAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
