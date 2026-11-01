# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
