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
        VideoRangeType = range
    };

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
        Assert.Equal(expected, QualityInfo.FromStreams([Video(w, h)])!.Resolution);
    }

    [Fact]
    public void DolbyVision_HidesHdrWord_AndShowsVision()
    {
        var q = QualityInfo.FromStreams([Video(3840, 2160, VideoRangeType.DOVIWithHDR10), Audio("truehd", "TrueHD + Dolby Atmos")])!;
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
        Assert.Equal(expected, QualityInfo.FromStreams([Video(3840, 2160, range)])!.Hdr);
    }

    [Fact]
    public void DolbyDigital_FromEac3()
    {
        Assert.Equal("DIGITAL", QualityInfo.FromStreams([Video(1920, 1080), Audio("eac3")])!.DolbyAudio);
        Assert.Null(QualityInfo.FromStreams([Video(1920, 1080), Audio("aac")])!.DolbyAudio);
    }

    [Fact]
    public void NoVideo_IsNull()
    {
        Assert.Null(QualityInfo.FromStreams([Audio("aac")]));
    }

    [Fact]
    public void Best_PrefersHigherResolution()
    {
        var a = new QualityInfo("1080p", "HDR", false, "ATMOS");
        var b = new QualityInfo("4K", null, false, null);
        Assert.Same(b, QualityInfo.Best(a, b));
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
            foreach (var (qname, q) in samples)
            {
                var result = QualityBadgeRenderer.ApplyBadge(bytes, q);
                Assert.NotEmpty(result);
                File.WriteAllBytes(Path.Combine(outDir, pname + "-" + qname + ".jpg"), result);
            }
        }
    }
}
