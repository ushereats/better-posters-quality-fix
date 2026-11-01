# Better Posters (Local Quality Fix)

Fork of [Better Posters](https://github.com/neurekadev/better-posters) for Jellyfin 12.

The original takes its 4K / HDR badges from btttr.cc's own database, so they're often wrong for the file you actually have. This version reads the quality from Jellyfin's own scan of your file (the same data as "Media Info") and draws the badges onto the btttr.cc poster itself, so they show in every Jellyfin app (web, Android TV, phones, Infuse...):

- **Dolby** logo with **ATMOS** / **VISION** underneath (Dolby Atmos, Dolby Digital, Dolby Vision)
- **HDR**, **HDR10+**, **HLG**
- **4K**, **1440p**, **1080p**, **720p**, **SD** chip

The look matches the Ushereats theme's poster badges.

## Install

Dashboard → Plugins → Repositories → Add:

```
https://raw.githubusercontent.com/ushereats/better-posters-quality-fix/main/manifest.json
```

Then Dashboard → Plugins → Catalog → **Better Posters (Local Quality Fix)** → Install → restart Jellyfin. Disable the original Better Posters plugin.

That's it: every movie and show gets its poster (the "Update Better Posters (Local Quality Fix)" task runs every night and only re-downloads titles that changed), and new titles get theirs within a minute of being added. The plugin's settings page has "Update posters now" and "Replace every poster now".

## Notes

- Needs the title's IMDb id (Jellyfin's normal metadata gives it).
- If you use the Ushereats theme with its own "Quality badges" on, the theme draws the same badges over the poster in the web app; turn one of them off if you prefer only one.

Releases are built, tested on a real Jellyfin 12 and published automatically on every push.
