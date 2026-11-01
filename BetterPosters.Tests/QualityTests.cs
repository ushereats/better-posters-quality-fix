using System;
using System.Collections.Generic;
using System.IO;
using BetterPosters.Providers;
using Jellyfin.Data.Enums;
using MediaBrowser.Model.Entities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BetterPosters.Tests;

public class QualityTests
{
    private static MediaStream Video(int w, int h, VideoRangeType range = VideoRangeType.SDR) => new()
    {
        Type = MediaStreamType.Video,
        Width = w,
        Height = h,
        Title = "range:" + range
    };

    // the range type is computed by Jellyfin from colour fields; the tests carry it in the title instead
    private static VideoRangeType RangeOf(MediaStream v)
        => v.Title is { } t && t.StartsWith("range:", StringComparison.Ordinal) ? Enum.Parse<VideoRangeType>(t[6..]) : VideoRangeType.SDR;

    private static QualityInfo? Q(params MediaStream[] streams) => QualityInfo.FromStreams(streams, RangeOf);

    private static MediaStream Audio(string codec, string? profile = null) => new()
    {
        Type = MediaStreamType.Audio,
        Codec = codec,
        Profile = profile
    };

    [Theory]
    [InlineData(3840, 2160, "4K")]
    [InlineData(3840, 1600, "4K")]
    [InlineData(2560, 1440, "1440p")]
    [InlineData(1920, 1080, "1080p")]
    [InlineData(1920, 800, "1080p")]
    [InlineData(1280, 720, "720p")]
    [InlineData(720, 480, "SD")]
    public void Resolution_FromFrameSize(int w, int h, string expected)
    {
        Assert.Equal(expected, Q(Video(w, h))!.Resolution);
    }

    [Fact]
    public void DolbyVision_HidesHdrWord_AndShowsVision()
    {
        var q = Q(Video(3840, 2160, VideoRangeType.DOVIWithHDR10), Audio("truehd", "TrueHD + Dolby Atmos"))!;
        Assert.True(q.DolbyVision);
        Assert.Null(q.Hdr);
        Assert.Equal("ATMOS", q.DolbyAudio);
    }

    [Theory]
    [InlineData(VideoRangeType.HDR10, "HDR")]
    [InlineData(VideoRangeType.HDR10Plus, "HDR10+")]
    [InlineData(VideoRangeType.HLG, "HLG")]
    [InlineData(VideoRangeType.SDR, null)]
    public void Hdr_Kinds(VideoRangeType range, string? expected)
    {
        Assert.Equal(expected, Q(Video(3840, 2160, range))!.Hdr);
    }

    [Fact]
    public void DolbyDigital_FromEac3()
    {
        Assert.Equal("DIGITAL", Q(Video(1920, 1080), Audio("eac3"))!.DolbyAudio);
        Assert.Null(Q(Video(1920, 1080), Audio("aac"))!.DolbyAudio);
    }

    [Fact]
    public void NoVideo_IsNull()
    {
        Assert.Null(Q(Audio("aac")));
    }

    [Fact]
    public void Best_PrefersHigherResolution()
    {
        var a = new QualityInfo("1080p", "HDR", false, "ATMOS");
        var b = new QualityInfo("4K", null, false, null);
        Assert.Same(b, QualityInfo.Best(a, b));
    }

    [Fact]
    public void TagBottom_FindsTheTag()
    {
        using var plain = new Image<Rgba32>(500, 750, new Rgba32(30, 50, 80));
        using var tagged = plain.Clone();
        tagged.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < 70; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 60; x < 440; x++)
                {
                    row[x] = new Rgba32(220, 170, 100);
                }
            }
        });
        Assert.Equal(70, QualityBadgeRenderer.TagBottom(tagged, plain));
        Assert.Equal(0, QualityBadgeRenderer.TagBottom(plain, plain));
    }

    // Draws the badges on a sample poster (and any real posters in $BP_PREVIEW_IN), saved to $BP_PREVIEW_OUT for a look
    [Fact]
    public void Renders_OnPosters()
    {
        var outDir = Environment.GetEnvironmentVariable("BP_PREVIEW_OUT") ?? Path.Combine(Path.GetTempPath(), "bp-preview");
        Directory.CreateDirectory(outDir);
        var samples = new List<(string Name, QualityInfo Q)>
        {
            ("4k-dv-atmos", new QualityInfo("4K", null, true, "ATMOS")),
            ("4k-hdr10p", new QualityInfo("4K", "HDR10+", false, null)),
            ("4k-hdr-dd", new QualityInfo("4K", "HDR", false, "DIGITAL")),
            ("1080p", new QualityInfo("1080p", null, false, null)),
            ("720p-hlg", new QualityInfo("720p", "HLG", false, null))
        };

        var posters = new List<(string Name, byte[] Bytes)>();
        var inDir = Environment.GetEnvironmentVariable("BP_PREVIEW_IN");
        if (inDir is not null && Directory.Exists(inDir))
        {
            foreach (var f in Directory.GetFiles(inDir, "*.jpg"))
            {
                posters.Add((Path.GetFileNameWithoutExtension(f), File.ReadAllBytes(f)));
            }
        }

        if (posters.Count == 0)
        {
            using var blank = new Image<Rgba32>(500, 750, new Rgba32(40, 60, 90));
            using var ms = new MemoryStream();
            blank.SaveAsJpeg(ms);
            posters.Add(("blank", ms.ToArray()));
        }

        foreach (var (pname, bytes) in posters)
        {
            // "<id>.jpg" has btttr.cc's trend tag, "<id>-plain.jpg" doesn't: the badges must go under the tag
            byte[]? plain = null;
            if (inDir is not null && File.Exists(Path.Combine(inDir, pname + "-plain.jpg")))
            {
                plain = File.ReadAllBytes(Path.Combine(inDir, pname + "-plain.jpg"));
            }

            foreach (var (qname, q) in samples)
            {
                var result = QualityBadgeRenderer.ApplyBadge(bytes, q, plain);
                Assert.NotEmpty(result);
                File.WriteAllBytes(Path.Combine(outDir, pname + "-" + qname + ".jpg"), result);
            }
        }
    }

    // Studio layout with a made-up logo on the real sample artwork ($BP_PREVIEW_IN), saved for a look
    [Fact]
    public void Studio_Composes()
    {
        var outDir = Environment.GetEnvironmentVariable("BP_PREVIEW_OUT") ?? Path.Combine(Path.GetTempPath(), "bp-preview");
        Directory.CreateDirectory(outDir);
        byte[] art;
        var inDir = Environment.GetEnvironmentVariable("BP_PREVIEW_IN");
        var sample = inDir is null ? null : Path.Combine(inDir, "tt0816692-bare.jpg");
        if (sample is not null && File.Exists(sample))
        {
            art = File.ReadAllBytes(sample);
        }
        else
        {
            using var blank = new Image<Rgba32>(500, 750, new Rgba32(40, 60, 90));
            using var ms = new MemoryStream();
            blank.SaveAsJpeg(ms);
            art = ms.ToArray();
        }

        // a fake logo: a white bar with transparent edges (trimmed and centred by the builder)
        byte[] logo;
        using (var l = new Image<Rgba32>(900, 300))
        {
            l.ProcessPixelRows(rows =>
            {
                for (var y = 80; y < 220; y++)
                {
                    var row = rows.GetRowSpan(y);
                    for (var x = 100; x < 800; x++)
                    {
                        row[x] = new Rgba32(255, 255, 255, 255);
                    }
                }
            });
            using var ms = new MemoryStream();
            l.SaveAsPng(ms);
            logo = ms.ToArray();
        }

        var config = new BetterPosters.Configuration.PluginConfiguration { EnableAgeRating = true };
        var info = new StudioInfo("Interstellar", 8.6f, 2014, "Adventure", "M");
        var q = new QualityInfo("4K", null, true, "ATMOS");
        var withLogo = StudioPosterBuilder.Compose(art, logo, info, q, config);
        var withText = StudioPosterBuilder.Compose(art, null, info, new QualityInfo("1080p", null, false, null), config);
        File.WriteAllBytes(Path.Combine(outDir, "studio-logo.jpg"), withLogo);
        File.WriteAllBytes(Path.Combine(outDir, "studio-text.jpg"), withText);
        using var check = Image.Load<Rgba32>(withLogo);
        Assert.Equal(StudioPosterBuilder.Width, check.Width);
        Assert.Equal(StudioPosterBuilder.Height, check.Height);
    }
}
