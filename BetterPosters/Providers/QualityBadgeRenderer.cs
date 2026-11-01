using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace BetterPosters.Providers;

/// <summary>
/// Draws the quality badges onto a poster, in the same look as the Ushereats theme's poster badges:
/// a row centred near the top - the Dolby logo (with ATMOS / VISION underneath), the HDR kind, and a light
/// resolution chip - in Funnel Sans, with a soft shadow so it reads on any artwork.
/// Sizes are the theme's (designed for a ~185 px wide poster card) scaled to the poster's real width.
/// </summary>
public static class QualityBadgeRenderer
{
    private const float DesignWidth = 185f;
    private static readonly Lazy<FontFamily[]> Fonts = new(LoadFonts);

    /// <summary>
    /// Draws the badges onto the poster.
    /// </summary>
    /// <param name="posterBytes">The poster image (any format ImageSharp reads).</param>
    /// <param name="quality">What to show.</param>
    /// <param name="untaggedBytes">The same poster without btttr.cc's trend tag ("Oscar Nominee"...), when trend tags are on:
    /// comparing the two finds the tag, so the badges sit just under it instead of on top of it.</param>
    /// <returns>The poster as a JPEG with the badges on it.</returns>
    public static byte[] ApplyBadge(byte[] posterBytes, QualityInfo quality, byte[]? untaggedBytes = null)
    {
        ArgumentNullException.ThrowIfNull(posterBytes);
        ArgumentNullException.ThrowIfNull(quality);

        using var image = Image.Load<Rgba32>(posterBytes);
        if (quality.HasAnything)
        {
            var below = 0;
            if (untaggedBytes is not null)
            {
                try
                {
                    using var plain = Image.Load<Rgba32>(untaggedBytes);
                    below = TagBottom(image, plain);
                }
                catch (Exception ex) when (ex is ImageFormatException or UnknownImageFormatException)
                {
                    below = 0;
                }
            }

            Draw(image, quality, below);
        }

        using var output = new MemoryStream();
        image.SaveAsJpeg(output, new JpegEncoder { Quality = 95, ColorType = JpegEncodingColor.YCbCrRatio444 });
        return output.ToArray();
    }

    /// <summary>
    /// Draws the badges onto an image already in memory.
    /// </summary>
    /// <param name="image">The poster.</param>
    /// <param name="quality">What to show.</param>
    /// <param name="below">Keep the badges below this row (the bottom of a trend tag), 0 for none.</param>
    public static void Draw(Image<Rgba32> image, QualityInfo quality, int below = 0)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(quality);

        var s = image.Width / DesignWidth;
        var parts = BuildParts(quality, s, image);
        if (parts.Count == 0)
        {
            return;
        }

        var gap = 9 * s;
        var total = -gap;
        foreach (var p in parts)
        {
            total += p.Width + gap;
        }

        // never wider than the poster (small posters / long rows): shrink to fit
        var maxWidth = image.Width - (20 * s);
        if (total > maxWidth)
        {
            var k = maxWidth / total;
            s *= k;
            parts = BuildParts(quality, s, image);
            gap = 9 * s;
            total = -gap;
            foreach (var p in parts)
            {
                total += p.Width + gap;
            }
        }

        var rowHeight = 17 * s;
        // no tag: the chip's top edge where btttr.cc puts it (37 px down on a 500 x 750 poster)
        var top = below > 0 ? below + (7 * s) : 12.6f * s;
        var centerY = top + (rowHeight / 2f);
        var x = (image.Width - total) / 2f;

        // everything is drawn on its own layer first, so the shadow can be made from it
        using var layer = new Image<Rgba32>(image.Width, (int)Math.Ceiling(top + rowHeight + (16 * s)));
        layer.Mutate(ctx =>
        {
            foreach (var p in parts)
            {
                p.Draw(ctx, x, centerY);
                x += p.Width + gap;
            }
        });

        using var shadow = layer.Clone(ctx => ctx
            .ProcessPixelRowsAsVector4(row =>
            {
                for (var i = 0; i < row.Length; i++)
                {
                    row[i] = new System.Numerics.Vector4(0, 0, 0, row[i].W * 0.6f);
                }
            })
            .GaussianBlur(Math.Max(1f, 2f * s)));

        image.Mutate(ctx =>
        {
            ctx.DrawImage(shadow, new Point(0, (int)Math.Round(1 * s)), 1f);
            ctx.DrawImage(layer, new Point(0, 0), 1f);
        });
    }

    /// <summary>
    /// Finds the bottom of the trend tag that hangs from the top edge: the lowest row in the top quarter
    /// where the tagged and untagged posters differ. 0 when they don't (no tag on this title).
    /// </summary>
    /// <param name="tagged">The poster with the tag.</param>
    /// <param name="plain">The same poster without it.</param>
    /// <returns>The row just under the tag, or 0.</returns>
    internal static int TagBottom(Image<Rgba32> tagged, Image<Rgba32> plain)
    {
        if (tagged.Width != plain.Width || tagged.Height != plain.Height)
        {
            return 0;
        }

        // a tag row: changed from the untagged poster AND mostly the tag's own colour (the tagged poster also
        // darkens the art under the tag, which changes it too, but that isn't the tag's colour)
        var w = tagged.Width;
        var h = tagged.Height;
        var tagColour = tagged[w / 2, Math.Min(2, h - 1)];
        var bottom = 0;
        var gap = 0;
        for (var y = 0; y < h / 4; y++)
        {
            int changed = 0, tagLike = 0, samples = 0;
            for (var x = w / 4; x < w * 3 / 4; x += 2)
            {
                samples++;
                var a = tagged[x, y];
                var b = plain[x, y];
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 45)
                {
                    changed++;
                }

                if (Math.Abs(a.R - tagColour.R) + Math.Abs(a.G - tagColour.G) + Math.Abs(a.B - tagColour.B) < 36)
                {
                    tagLike++;
                }
            }

            if (changed > samples * 0.5 && tagLike > samples * 0.4)
            {
                bottom = y + 1;
                gap = 0;
            }
            else if (bottom > 0)
            {
                // the tag's text interrupts it for a few rows; a longer run means the tag is over
                if (++gap > h / 20)
                {
                    break;
                }
            }
            else if (y > h / 30)
            {
                // nothing at the very top: no tag on this title
                return 0;
            }
        }

        return bottom;
    }

    private static List<Part> BuildParts(QualityInfo q, float s, Image<Rgba32> poster)
    {
        var parts = new List<Part>();
        if (q.DolbyVision || q.DolbyAudio is not null)
        {
            var sub = string.Join(" · ", new[] { q.DolbyAudio, q.DolbyVision ? "VISION" : null }.Where2());
            parts.Add(new DolbyPart(sub, s));
        }

        if (q.Hdr is not null)
        {
            parts.Add(new TextPart(q.Hdr, Font(800, 11 * s), 0.02f));
        }

        if (q.Resolution is not null)
        {
            parts.Add(new ChipPart(q.Resolution, s, poster));
        }

        return parts;
    }

    // btttr.cc's resolution chip uses an Arial-style bold: Arimo (same shapes and widths as Arial, free to ship)
    internal static Font ChipFont(float size) => Fonts.Value[3].CreateFont(Math.Max(1f, size), FontStyle.Regular);

    internal static Font Font(int weight, float size)
    {
        var families = Fonts.Value;
        var family = weight >= 800 ? families[2] : weight >= 700 ? families[1] : families[0];
        return family.CreateFont(Math.Max(1f, size), FontStyle.Regular);
    }

    private static FontFamily[] LoadFonts()
    {
        var collection = new FontCollection();
        var asm = typeof(QualityBadgeRenderer).Assembly;
        FontFamily Load(string weight)
        {
            using var stream = asm.GetManifestResourceStream("BetterPosters.Fonts.FunnelSans-" + weight + ".ttf")
                ?? throw new InvalidOperationException("Embedded font missing: " + weight);
            return collection.Add(stream);
        }

        using var arimo = asm.GetManifestResourceStream("BetterPosters.Fonts.Arimo-700.ttf")
            ?? throw new InvalidOperationException("Embedded font missing: Arimo");
        return [Load("500"), Load("700"), Load("800"), collection.Add(arimo)];
    }

    private static IPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        var pts = new List<PointF>();
        void Arc(float cx, float cy, float start)
        {
            for (var i = 0; i <= 6; i++)
            {
                var a = (start + (90f * i / 6f)) * MathF.PI / 180f;
                pts.Add(new PointF(cx + (r * MathF.Cos(a)), cy + (r * MathF.Sin(a))));
            }
        }

        Arc(x + w - r, y + r, 270);
        Arc(x + w - r, y + h - r, 0);
        Arc(x + r, y + h - r, 90);
        Arc(x + r, y + r, 180);
        return new Polygon(new LinearLineSegment(pts.ToArray()));
    }

    // letter spacing (em): only the wide-spaced small capitals use it; tiny values are left to the font
    internal static float Advance(string text, Font font, float tracking)
    {
        if (Math.Abs(tracking) < 0.05f)
        {
            return TextMeasurer.MeasureAdvance(text, new TextOptions(font)).Width;
        }

        var w = 0f;
        foreach (var ch in text)
        {
            w += TextMeasurer.MeasureAdvance(ch.ToString(), new TextOptions(font)).Width + (tracking * font.Size);
        }

        return w - (tracking * font.Size);
    }

    internal static void DrawText(IImageProcessingContext ctx, string text, Font font, float tracking, float x, float centerY, Color color)
    {
        if (Math.Abs(tracking) < 0.05f)
        {
            DrawRun(ctx, text, font, x, centerY, color);
            return;
        }

        foreach (var ch in text)
        {
            var c = ch.ToString();
            DrawRun(ctx, c, font, x, centerY, color);
            x += TextMeasurer.MeasureAdvance(c, new TextOptions(font)).Width + (tracking * font.Size);
        }
    }

    private static void DrawRun(IImageProcessingContext ctx, string text, Font font, float x, float centerY, Color color)
    {
        ctx.DrawText(
            new RichTextOptions(font)
            {
                Origin = new PointF(x, centerY),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            },
            text,
            color);
    }

    private abstract class Part
    {
        public float Width { get; protected set; }

        public abstract void Draw(IImageProcessingContext ctx, float x, float centerY);
    }

    // resolution chip measured from btttr.cc's own quality badge (500 px poster: 40 px tall chip, ~21 px
    // capitals, ~12 px side padding, ~8 px corners, near-black bold text on light grey), at the 185 px design size
    private sealed class ChipPart : Part
    {
        private readonly string _text;
        private readonly Font _font;
        private readonly float _s;
        private readonly float _textWidth;
        private readonly Image<Rgba32> _poster;

        // btttr.cc's letters sit tighter and heavier than plain Arial bold
        private const float Tracking = -0.05f;

        public ChipPart(string text, float s, Image<Rgba32> poster)
        {
            _text = text;
            _s = s;
            _poster = poster;
            _font = ChipFont(11.3f * s);
            _textWidth = Advance(text, _font, Tracking);
            Width = Math.Max(15 * s, _textWidth + (7.2f * s));
        }

        // btttr.cc's chip: frosted light glass over the poster, the letters cut out of it (the poster shows
        // through them, darkened), Arial-style bold, small round corners
        public override void Draw(IImageProcessingContext ctx, float x, float centerY)
        {
            var h = 14.8f * _s;
            var rect = new Rectangle((int)Math.Round(x), (int)Math.Round(centerY - (h / 2f)), (int)Math.Round(Width), (int)Math.Round(h));
            rect.Intersect(new Rectangle(0, 0, _poster.Width, _poster.Height));
            if (rect.Width < 2 || rect.Height < 2)
            {
                return;
            }

            using var under = _poster.Clone(c => c.Crop(rect));
            using var glass = under.Clone(c => c.GaussianBlur(Math.Max(1f, 2.5f * _s)).Fill(Color.FromRgba(240, 240, 238, 230)));
            using var letters = under.Clone(c => c.Fill(Color.FromRgba(0, 0, 0, 118)));
            using var mask = new Image<Rgba32>(rect.Width, rect.Height);
            mask.Mutate(c =>
            {
                var pen = Pens.Solid(Color.White, 0.55f * _s);
                var cx = (rect.Width - _textWidth) / 2f;
                foreach (var ch in _text)
                {
                    var t = ch.ToString();
                    c.DrawText(
                        new RichTextOptions(_font)
                        {
                            Origin = new PointF(cx, (rect.Height / 2f) + (0.3f * _s)),
                            HorizontalAlignment = HorizontalAlignment.Left,
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        t,
                        Brushes.Solid(Color.White),
                        pen);
                    cx += Advance(t, _font, 0) + (Tracking * _font.Size);
                }
            });
            using var shape = new Image<Rgba32>(rect.Width, rect.Height);
            shape.Mutate(c => c.Fill(Color.White, RoundedRect(0, 0, rect.Width, rect.Height, 3f * _s)));

            glass.ProcessPixelRows(letters, mask, (g, l, m) =>
            {
                for (var y = 0; y < g.Height; y++)
                {
                    var gr = g.GetRowSpan(y);
                    var lr = l.GetRowSpan(y);
                    var mr = m.GetRowSpan(y);
                    for (var i = 0; i < gr.Length; i++)
                    {
                        var a = mr[i].A / 255f;
                        if (a > 0)
                        {
                            gr[i] = new Rgba32(
                                (byte)((gr[i].R * (1 - a)) + (lr[i].R * a)),
                                (byte)((gr[i].G * (1 - a)) + (lr[i].G * a)),
                                (byte)((gr[i].B * (1 - a)) + (lr[i].B * a)),
                                255);
                        }
                    }
                }
            });
            glass.ProcessPixelRows(shape, (g, m) =>
            {
                for (var y = 0; y < g.Height; y++)
                {
                    var gr = g.GetRowSpan(y);
                    var mr = m.GetRowSpan(y);
                    for (var i = 0; i < gr.Length; i++)
                    {
                        gr[i].A = mr[i].A;
                    }
                }
            });

            ctx.DrawImage(glass, new Point(rect.X, rect.Y), 1f);
        }
    }

    // plain white wordmark: "HDR", "HDR10+", "HLG"
    private sealed class TextPart : Part
    {
        private readonly string _text;
        private readonly Font _font;
        private readonly float _tracking;

        public TextPart(string text, Font font, float tracking)
        {
            _text = text;
            _font = font;
            _tracking = tracking;
            Width = Advance(text, font, tracking);
        }

        public override void Draw(IImageProcessingContext ctx, float x, float centerY)
            => DrawText(ctx, _text, _font, _tracking, x, centerY, Color.White);
    }

    // the Dolby double-D mark + "Dolby", with ATMOS / VISION in small spaced capitals underneath
    private sealed class DolbyPart : Part
    {
        private readonly string _sub;
        private readonly float _s;
        private readonly Font _word;
        private readonly Font _subFont;
        private readonly float _wordWidth;
        private readonly float _subWidth;
        private readonly float _topWidth;

        public DolbyPart(string sub, float s)
        {
            _sub = sub;
            _s = s;
            _word = Font(500, 10.5f * s);
            _subFont = Font(700, 5.5f * s);
            _wordWidth = Advance("Dolby", _word, -0.01f);
            _subWidth = string.IsNullOrEmpty(sub) ? 0 : Advance(sub, _subFont, 0.22f);
            _topWidth = (13 * s) + (2 * s) + _wordWidth;
            Width = Math.Max(_topWidth, _subWidth);
        }

        public override void Draw(IImageProcessingContext ctx, float x, float centerY)
        {
            var s = _s;
            var hasSub = _subWidth > 0;
            var topY = hasSub ? centerY - (3.6f * s) : centerY;
            var left = x + ((Width - _topWidth) / 2f);

            // mark: 13 x 7.5, from the 24 x 14 logo (two D's back to back)
            var k = 13f * s / 24f;
            var markTop = topY - (7.5f * s / 2f);
            ctx.Fill(Color.White, DShape(left, markTop, k, false));
            ctx.Fill(Color.White, DShape(left, markTop, k, true));

            DrawText(ctx, "Dolby", _word, -0.01f, left + (15 * s), topY + (0.4f * s), Color.White);
            if (hasSub)
            {
                DrawText(ctx, _sub, _subFont, 0.22f, x + ((Width - _subWidth) / 2f) + (0.6f * s), topY + (7.6f * s), Color.FromRgba(255, 255, 255, 230));
            }
        }

        // left D: square edge at x=0, round side bulging right; right D mirrored at x=24
        private static IPath DShape(float ox, float oy, float k, bool mirrored)
        {
            var pts = new List<PointF>();
            PointF P(float px, float py) => new(ox + ((mirrored ? 24 - px : px) * k), oy + (py * k));
            pts.Add(P(0, 0));
            pts.Add(P(5.5f, 0));
            for (var i = 0; i <= 16; i++)
            {
                var a = (-90f + (180f * i / 16f)) * MathF.PI / 180f;
                pts.Add(P(5.5f + (4.5f * MathF.Cos(a)), 7 + (7 * MathF.Sin(a))));
            }

            pts.Add(P(0, 14));
            return new Polygon(new LinearLineSegment(pts.ToArray()));
        }
    }
}

/// <summary>
/// Small helpers.
/// </summary>
internal static class Seq
{
    /// <summary>
    /// The non-empty strings of a list.
    /// </summary>
    /// <param name="items">The list.</param>
    /// <returns>Its non-empty strings.</returns>
    public static IEnumerable<string> Where2(this IEnumerable<string?> items)
    {
        foreach (var i in items)
        {
            if (!string.IsNullOrEmpty(i))
            {
                yield return i;
            }
        }
    }
}
