# Better Posters (Local Quality Fix)

Fork of [Better Posters](https://github.com/neurekadev/better-posters).

The original plugin's quality badge (4K/1080p/etc) is pulled from btttr.cc's own external database, not from your actual files. It's often wrong — shows 4K on a 1080p file, or the other way around.

This fork fixes that. The quality badge is now read directly from Jellyfin's own scan of your file (real resolution, real HDR/DV/HLG) — same data you'd see under a file's "Media Info" in Jellyfin. What it says is what you actually have.

Built for Jellyfin 12.0.

## Install

Add this repository in Jellyfin:

**Dashboard → Plugins → Repositories → Add Repository**

```
https://raw.githubusercontent.com/ushereats/better-posters-quality-fix/master/manifest.json
```

Then **Dashboard → Plugins → Catalog** → install **Better Posters (Local Quality Fix)** → restart Jellyfin.

Turn on **Quality Tags** in the plugin's settings. Disable the original Better Posters plugin so they don't both try to claim the same poster.

## Updates

Jellyfin checks this repo on its own via the "Check for plugin updates" scheduled task. New versions install automatically.
