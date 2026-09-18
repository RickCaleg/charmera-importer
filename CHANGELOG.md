# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Release notes on GitHub are generated from this file — every release needs a
`## [x.y.z]` section before its tag is pushed.

## [Unreleased]

## [0.2.0] - 2026-09-18

First release with downloadable installers.

### Added

- Self-update: checks GitHub Releases on startup (can be turned off in the sidebar),
  shows a banner when a new version is out, verifies the download against the release's
  `SHA256SUMS`, and installs it in place. This works for the Windows installer and the
  Linux portable build. `.deb`/`.rpm` installs are sent to the release page instead.
- Windows installer built with Inno Setup (per-user, no admin rights needed, upgrades in
  place), replacing the hand-written console installer.
- Automated releases: a version tag builds the Linux tarball, `.deb`, `.rpm` and Windows
  installer on CI and publishes them with checksums.
- Unit tests for path/naming rules and update metadata handling. CI runs on Linux and Windows.
- Code of Conduct, security policy, third-party notices, and issue/PR templates.

### Fixed

- Destination folder and file names no longer depend on the OS calendar/culture
  (e.g. Thai Buddhist-calendar years).
- Repository URL typo in the README and package metadata.

## [0.1.0] - 2026-09-18

Initial version (tagged, not published as a GitHub release).

### Added

- Removable USB device detection on Linux (`/proc/mounts` + `/sys/block`) and Windows.
- Photo browser for the device's `DCIM` folder, with background thumbnail loading.
- EXIF metadata reading (make, model, capture date, dimensions, full tag dump).
- Folder organization by year/month, year/month/day, camera model, or a single folder.
- File naming by capture date/time, optionally keeping the original name as a suffix.
- SHA-256 duplicate detection backed by a local import history.
- Optional, off-by-default deletion of photos from the camera after a confirmed import.
- English and Portuguese UI, auto-detected from the OS locale.
