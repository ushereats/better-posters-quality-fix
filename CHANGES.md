# Better Posters fork — accurate local quality badge

## Root cause confirmed from source

`BetterPosterUrlBuilder.Build()` only ever appends a `q` flag to the poster
*path variant* requested from btttr.cc (`poster-q`, `poster-gq`, etc.). There
is no parameter anywhere that tells btttr.cc what your file's actual
resolution/HDR format is — it picks the badge value itself, server-side, from
its own database keyed by IMDb id. `BetterPostersImageProvider.GetImageResponse`
just proxies the raw bytes straight through. Nothing in this plugin ever reads
Jellyfin's own `MediaStream` data for the item. That's the entire mismatch.

## What changed

1. **`BetterPosterUrlBuilder.cs`** — removed the `q` suffix logic entirely.
   btttr.cc's own quality badge is never requested anymore.

2. **`Providers/QualityBadgeRenderer.cs`** *(new)* — reads the item's real
   `MediaStream` list, picks the highest-resolution video stream, maps it to
   `4K` / `1080p` / `720p` / `SD` by long-edge pixel count, and appends `HDR`
   / `HDR10+` / `DV` / `HLG` from `VideoRangeType` (falls back to the older
   free-text `VideoRange` field). Then draws a simple dark rounded... actually
   plain rectangle badge (see note below) with that label onto the top-right
   corner of the downloaded poster using ImageSharp, and re-encodes as JPEG.

3. **`Providers/BetterPostersImageProvider.cs`** — now takes `ILibraryManager`
   and `IMediaSourceManager` via constructor DI (already how Jellyfin wires
   this class up, per `PluginServiceRegistrator.cs` — no registrar change
   needed). `GetImages` embeds the item's GUID as a `bpItemId` query param on
   the URL it hands back (the `IRemoteImageProvider` interface only passes a
   bare URL string into `GetImageResponse`, so this is the only channel
   available to carry the item id through). `GetImageResponse` strips that
   param before forwarding the request to btttr.cc, downloads the plain
   poster, looks the item back up by the embedded id, resolves a quality
   label from its real streams, and composites the badge — only when
   `EnableQualityTags` is on. If anything in that path throws, it falls back
   to the unbadged poster rather than breaking image loading.

4. **`BetterPosters.csproj`** — added `SixLabors.ImageSharp`,
   `SixLabors.ImageSharp.Drawing`, `SixLabors.Fonts`. Fully managed, no native
   binaries, so no conflict with Jellyfin server's own (native) SkiaSharp in
   the same process.

## Series handling

A series has no single resolution. The fork currently badges a series poster
using its **highest-resolution episode**. If you'd rather badge by most
recent episode, or skip series entirely, that logic is isolated in
`ResolveQualityLabel`'s `if (item is Series series)` branch — easy to swap.

## What I could NOT verify

I don't have network access to nuget.org from this sandbox, only to GitHub
directly — so **none of this has been compiled against the real
`Jellyfin.Controller`/`Jellyfin.Model` 10.11.10 packages.** Before you trust
it:

- `dotnet build BetterPosters/BetterPosters.csproj -c Release` and fix any
  signature mismatches. The riskiest assumptions:
  - `IMediaSourceManager.GetMediaStreams(MediaStreamQuery)` — this is a
    long-standing Jellyfin API but confirm the exact namespace/signature in
    your installed `Jellyfin.Controller` version.
  - `MediaStream.VideoRangeType` — present on modern Jellyfin builds; if your
    target version doesn't have it, drop that check and rely on the
    `VideoRange` string fallback only.
  - `InternalItemsQuery.Parent` vs `ParentId` — the existing
    `BetterPostersUpdateTask.cs` in this repo uses `IncludeItemTypes` +
    `Recursive` but never filters by parent, so I inferred the property name;
    check against the actual `InternalItemsQuery` definition.
- The badge is currently a **plain rectangle**, not rounded — I stripped out
  a hand-rolled rounded-rect path-builder because I couldn't verify
  ImageSharp.Drawing's arc-path API without compiling. Rounding it is a small
  cosmetic follow-up once the build is green.
- Font availability: `SystemFonts.Collection.TryGet` walks a fallback list
  (DejaVu Sans, Liberation Sans, Noto Sans, Arial, Helvetica) since Jellyfin
  Docker images typically ship DejaVu. If your container has none of those,
  install a font package (e.g. `fonts-dejavu-core`) or add your own to the
  fallback list.
- **`BetterPosters.Tests/BetterPosterUrlBuilderTests.cs`** has two existing
  cases (`[InlineData(true, true, true, false, "poster-q")]` and the `"poster-qa"`
  one) that assert the OLD behavior — they will now fail on purpose, since
  btttr.cc's own quality suffix is gone. Update or delete those two cases.

## Build

```
dotnet publish BetterPosters/BetterPosters.csproj -c Release
```

Copy the resulting `BetterPosters.dll` (and the three SixLabors DLLs it now
depends on, from the publish output) into your Better Posters plugin folder,
replacing the existing files. Restart Jellyfin.
