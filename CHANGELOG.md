# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- New titles get their poster a few seconds after Jellyfin has their details (was a fixed 20-second wait that could miss titles still being filled in; those are now checked again every few seconds for up to 5 minutes).

### Added
- **Season and collection posters** (studio style): each season gets its own TMDb artwork (or the show's) with the show's logo and "Season 2" in the info line; collections get their TMDb collection artwork with the name and the number of films. Quality badges come from the season's episodes / the collection's best film.
- **Live progress** on the settings page: how far a run is, which title it's on, time left, and how many posters were made, already up to date, not found or failed.
- **New settings page**: a status card with the Update / Replace buttons at the top, the style picked from cards, sections (Look, Badges, Updating, Advanced) you can jump between, only the options for the chosen style, and a save bar that shows up when something changed.

### Changed
- Artwork downloaded from TMDb is kept on the server, so rebuilding posters after a settings change downloads nothing again (unused files are cleared after 120 days, and the cache never grows past 4 GB).
- TMDb's limits are respected: a few requests at a time, spaced out, and retried with growing waits when TMDb is busy or briefly down, so big libraries no longer end up with gaps.

### Changed
- The 4K / 1440p / 1080p / 720p chip is now a copy of btttr.cc's: frosted light glass with the poster faintly behind it, the letters cut out of it (the poster shows through them, darkened), an Arial-style heavy bold (Arimo, built in), btttr.cc's size, corners and position.
- Studio style: titles with no clean artwork on TMDb now get TMDb's full-size official poster (with just the badges added) instead of btttr.cc's 500 px one, so they're sharp too.
- Updating is about 3x faster: three titles at a time.

## [1.1.25] - 2026-11-01

### Changed
- Sharper posters: studio posters are made from TMDb's full-size artwork and logos at 1000 x 1500 (was 780 x 1170), with better resizing, and every poster is saved at higher JPEG quality with full colour detail.
- The tag at the top of studio posters is off by default (setting "Tag at the top").

## [1.1.23] - 2026-11-01

### Added
- Studio posters look closer to SpatialPosters: a tag hanging from the top edge (the studio or network, e.g. "Adult Swim", or the genre; setting "Tag at the top"), coloured from the poster itself, with the quality badges under it; bigger title logos; a soft grey info line.

## [1.1.21] - 2026-11-01

### Added
- **Studio posters** (the new default style): each title's clean artwork from TMDb (no text printed on it), its logo placed at the bottom over a soft fade, an info line underneath (rating, year, genre, age rating) and your real quality badges at the top. Titles TMDb has no clean artwork for get the btttr.cc poster as before. Switch back to btttr.cc's posters under Poster Style.
- Settings for the studio style: title logo on/off (off = the title in text), how dark the fade is, year on/off, and an optional TMDb key (empty = the one Jellyfin's TMDb plugin uses). Genre, Ratings, Age Rating and Language apply to both styles.

### Changed
- The 4K / 1440p / 1080p / 720p chip now matches btttr.cc's own quality badge: its size, padding and corners, and near-black letters cut out of the chip.

## [1.1.15] - 2026-11-01

### Fixed
- Installing from the plugin repository works: the old manifest pointed at a release that no longer existed, and a second, broken release workflow ran on every tag. One workflow now builds, tests on a real Jellyfin 12, releases and writes the manifest (with the right checksum) automatically.
- The settings page saved to the *original* Better Posters plugin's settings instead of this one's, so "Quality badges" never turned on. It now saves to this plugin.
- "Update Better Posters" skipped every library unless "Better Posters (Local Quality Fix)" had been ticked as an image fetcher in each library, so posters were never replaced. It now covers every movie and show library (switchable).
- The update task never drew the quality badges (it saved btttr.cc's plain poster). It now draws them.
- Badges no longer depend on fonts being installed on the server (the font is built in).

### Changed
- New badge look matching the Ushereats theme: Dolby logo with ATMOS / VISION, HDR / HDR10+ / HLG, and a light 4K / 1440p / 1080p / 720p chip, centred at the top with a soft shadow. With trend tags on ("Oscar Nominee"...), the badges sit just under the tag instead of over it.
- Dolby Atmos and Dolby Digital are detected from the audio tracks.
- Shows use the best of their newest episodes.
- New titles (and upgraded files) get their poster within a minute; the task runs nightly and only re-downloads titles that changed.
- Settings: "Update posters now" and "Replace every poster now" buttons.

## [1.0.5] - 2026-08-25

### Changed
- Move Better Posters updates to the official GitHub project; replace the old Jellyfin update source and reinstall the plugin to keep receiving releases.

## [1.0.4] - 2026-07-10

### Changed
- Moved the plugin repository to a new home. Reinstall Better Posters with the updated repository URL to keep receiving updates.

## [1.0.3] - 2026-06-16

### Added
- Add catalog and preview artwork for plugin listings.

### Fixed
- Prevent Update Better Posters from replacing posters in libraries where Better Posters is disabled.

## [1.0.2] - 2026-06-15

### Fixed
- Fix Update Better Posters so manual task runs replace existing movie and show posters.

## [1.0.1] - 2026-06-15

### Changed
- Move automatic poster update scheduling to Jellyfin Scheduled Tasks.

### Fixed
- Stop Update Better Posters from being scheduled daily by default.
- Clarify rating labels in plugin settings.

## [1.0.0] - 2026-06-15

### Added
- Add Better Posters as a Jellyfin image provider for movie and show posters from btttr.cc.

[Unreleased]: https://github.com/neurekadev/better-posters/compare/1.0.5...HEAD
[1.0.5]: https://github.com/neurekadev/better-posters/compare/1.0.4...1.0.5
[1.0.4]: https://github.com/neurekadev/better-posters/compare/1.0.3...1.0.4
[1.0.3]: https://github.com/neurekadev/better-posters/compare/1.0.2...1.0.3
[1.0.2]: https://github.com/neurekadev/better-posters/compare/1.0.1...1.0.2
[1.0.1]: https://github.com/neurekadev/better-posters/compare/1.0.0...1.0.1
[1.0.0]: https://github.com/neurekadev/better-posters/tree/1.0.0
