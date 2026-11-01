# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Fixed
- Installing from the plugin repository works: the old manifest pointed at a release that no longer existed, and a second, broken release workflow ran on every tag. One workflow now builds, tests on a real Jellyfin 12, releases and writes the manifest (with the right checksum) automatically.
- The settings page saved to the *original* Better Posters plugin's settings instead of this one's, so "Quality badges" never turned on. It now saves to this plugin.
- "Update Better Posters" skipped every library unless "Better Posters (Local Quality Fix)" had been ticked as an image fetcher in each library, so posters were never replaced. It now covers every movie and show library (switchable).
- The update task never drew the quality badges (it saved btttr.cc's plain poster). It now draws them.
- Badges no longer depend on fonts being installed on the server (the font is built in).

### Changed
- New badge look matching the Ushereats theme: Dolby logo with ATMOS / VISION, HDR / HDR10+ / HLG, and a light 4K / 1440p / 1080p / 720p chip, centred at the top with a soft shadow.
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
