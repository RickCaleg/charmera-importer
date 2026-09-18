# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Release notes on GitHub are generated from this file — every release needs a
`## [x.y.z]` section before its tag is pushed.

## [Unreleased]

## [0.4.0] - 2026-09-18

### Added

- **Kodak Charmera metadata repair.** Every imported photo gets repaired EXIF:
  - the malformed capture date (`YYYY:MM:DD:HH:MM:SS`) is normalized;
  - the 640×480 size tags are corrected to the real 1440×1080;
  - the MakerNote that points outside the EXIF block is dropped;
  - `Generalplus`/`CBB3` (the chip) is replaced by Kodak/Charmera.

  The camera's files are never modified, and the image data is copied byte for byte. All
  of this was verified on original Charmera photos. The README explains each defect and fix.
- "About" window (Settings → About) with the app's features, privacy notes, links and
  credits.

### Changed

- **The app is now exclusively for the Kodak Charmera.** Only Charmera memory cards are
  listed (recognized by the `SPIDCIM` folder or the `GPEncoder` signature, not by name),
  and only Charmera photos are imported.
- Removed the "by camera model" folder organization (there's only one camera now).

### Fixed

- Photo dimensions come from the JPEG frame instead of EXIF tags that can be wrong.
- Dates in the Charmera's malformed format are now read for every photo, so they're
  organized and named by the real capture date instead of the file date.
- Capture dates in the details panel follow the app's language instead of the OS locale.

## [0.3.0] - 2026-09-18

### Changed

- Redesigned the main window around the import workflow: three numbered steps
  (Camera → Destination → After import) that check off as they're completed, with the
  Import button pinned at the bottom. Language, updates and version moved into a
  Settings popover in the app bar.
- The Import button says how many photos it will import, and explains why when it's
  disabled.
- Live example of the final file path under the folder/naming options.
- Separate empty states for "no camera", "looking for photos" and "no photos found".
- Photo details show a larger preview and a close button; the raw EXIF tag list is
  collapsed by default.

### Added

- Cameras are detected automatically when plugged in (no need to press refresh), and a
  single connected camera is selected for you.

## [0.2.2] - 2026-09-18

### Fixed

- The app hung at startup, with no window, once a settings file existed, i.e. after
  changing any preference. Loading settings blocked the UI thread while waiting on work
  that needed that same thread. Affected installs can't self-update, so download 0.2.2
  from the release page once.

## [0.2.1] - 2026-09-18

### Fixed

- Linux self-update crashed the running app right after swapping in the new binary: a
  single-file .NET app keeps loading its own assemblies from the executable's path. The new
  binary is now moved into place by a small helper after the app has exited, and the app
  then relaunches normally. Updating *from* 0.2.0 on Linux may still show "Update failed".
  The new version is already installed at that point, so just reopen the app.

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
