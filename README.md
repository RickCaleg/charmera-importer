# Charmera Importer

A desktop app for importing photos from a Kodak camera (or any USB mass-storage
device) with automatic EXIF-based organization and duplicate detection.

[![CI](https://github.com/RickCaleg/charmera-importer/actions/workflows/ci.yml/badge.svg)](https://github.com/RickCaleg/charmera-importer/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/RickCaleg/charmera-importer)](https://github.com/RickCaleg/charmera-importer/releases/latest)
![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![Platforms](https://img.shields.io/badge/platform-Linux%20%7C%20Windows-lightgrey)

![Charmera Importer main window](docs/images/screenshot.png)

## Fixes the Kodak Charmera's broken photo metadata

The [Kodak Charmera](https://en.wikipedia.org/wiki/Kodak_Charmera) keychain camera
writes **defective EXIF metadata** into every photo. Most photo apps then show the
wrong date, file the pictures under the day you copied them, or report the wrong
resolution. Charmera Importer detects Charmera photos automatically and **repairs
the metadata of each imported copy**:

| Charmera defect | What you'd see elsewhere | What the importer writes |
|---|---|---|
| Capture date stored as `2026:03:03:12:16:29` instead of the standard `2026:03:03 12:16:29` | No capture date: photos sorted and filed by the day you copied them | The same date in the standard format, so every app reads it |
| `ExifImageWidth/Height` of **640×480** on **1440×1080** photos | Wrong resolution in photo libraries | The real size, read from the JPEG frame |
| Corrupt MakerNote block with invalid offsets | Metadata editors refuse to touch the file | A clean EXIF block; the broken MakerNote is dropped |
| No camera make/model | "Unknown camera" | `Kodak` / `Charmera`, plus the published lens info (35 mm-equiv, f/2.4) |

- **Only the metadata changes.** The image data is copied byte for byte, so the pixels
  are identical. This was verified by decoding and comparing them.
- **The camera is never modified.** Repairs happen on the imported copy; the original
  file on the card stays untouched.
- **Automatic detection, and you can turn it off.** Charmera photos are recognized by
  the Generalplus `GPEncoder` signature the camera embeds, whatever the card is named.
  The repair is on by default and can be switched off under *Destination*.
- **Correct folders and names.** Because the real capture date is recovered, photos
  land in the right year/month folders and get the right date-based file names.

The camera's clock has no time zone, so dates are kept as the local time the camera
recorded. The defects and the repair approach are documented by
[jphastings/charmera](https://github.com/jphastings/charmera) and
[RAIT-09/kodak-charmera-exif-fixer](https://github.com/RAIT-09/kodak-charmera-exif-fixer)
(both MIT). This app is an independent, dependency-free C# implementation
(`Services/CharmeraExif.cs`), covered by tests that reproduce each defect.

## About

Charmera Importer was built to solve a small, specific annoyance: getting
photos off a Kodak point-and-shoot camera (which mounts as a plain USB drive)
without manually hunting through `DCIM` folders, renaming files by hand, or
accidentally re-copying photos that were already imported.

It lists removable USB drives, shows the photos on the selected one as
thumbnails, lets you pick a destination folder, a folder structure, and a
file-naming pattern, then imports everything — skipping anything that's
already been imported before, based on file content, not just the filename.

## Features

- **Kodak Charmera metadata repair** — see [above](#fixes-the-kodak-charmeras-broken-photo-metadata).
- **Removable device detection** — detects the camera as soon as it's plugged in (Linux
  and Windows), and selects it automatically when it's the only one.
- **Thumbnail browser** — scans the device's `DCIM` folder and shows photos as
  a grid of thumbnails, loaded progressively in the background.
- **EXIF metadata** — reads camera make/model, capture date and dimensions (taken from
  the JPEG frame, which can't be wrong), plus the full EXIF tag dump for each photo.
- **Flexible organization** — choose how imported photos are organized:
  by year/month, year/month/day, by camera model, or a single flat folder.
- **Configurable file naming** — rename files based on capture date/time, with
  the option to keep the original filename as a suffix.
- **Duplicate detection** — every imported file is hashed (SHA-256) and
  recorded in a local history, so re-running an import (e.g. with an
  unformatted card) never creates duplicate copies.
- **Remembers your preferences** — destination folder, organization scheme,
  naming preset, and language are saved automatically and pre-selected the
  next time you open the app.
- **Copies by default, deletes only if you ask** — files are always copied
  from the camera. An explicit, always-off-by-default checkbox lets you also
  delete already-imported photos from the camera afterward, for people who
  want to clear the card as they go.
- **Self-updating** — checks GitHub Releases on startup (can be turned off), shows a
  banner when a new version is out, and installs it with one click after verifying the
  download's SHA-256 checksum.
- **Language selection** — the UI auto-detects a supported language from the
  OS locale on first run (currently English and Portuguese), and remembers
  your choice if you change it in Settings.

## Installation

Download the latest version from the
**[Releases page](https://github.com/RickCaleg/charmera-importer/releases/latest)**.
All builds are self-contained — no .NET installation needed.

| Platform | File | Updates |
|---|---|---|
| Windows 10/11 (x64) | `charmera-importer-<version>-win-x64-setup.exe` | Automatic, from inside the app |
| Linux (x64), any distro | `charmera-importer-<version>-linux-x64.tar.gz` | Automatic, from inside the app |
| Debian / Ubuntu | `charmera-importer_<version>_amd64.deb` | Download the new `.deb` (the app tells you when) |
| Fedora / openSUSE | `charmera-importer-<version>-1.x86_64.rpm` | Download the new `.rpm` (the app tells you when) |

**Windows:** run the setup. It installs for your user only, so no administrator prompt.
Windows SmartScreen may warn that the publisher is unknown, because the installer isn't
code-signed. Choose *More info → Run anyway*.

**Linux (portable):** extract it somewhere you own, such as `~/Applications/charmera-importer`,
and run `./charmera-importer`. The folder must stay writable by you for self-update to
work. The tarball also contains a `.desktop` file and icon if you want a menu entry.

**Linux (.deb/.rpm):**

```bash
sudo apt install ./charmera-importer_<version>_amd64.deb     # Debian/Ubuntu
sudo dnf install ./charmera-importer-<version>-1.x86_64.rpm  # Fedora
```

You can verify any download against `SHA256SUMS` from the same release
(`sha256sum -c SHA256SUMS --ignore-missing`).

## Tech stack

- [.NET 10](https://dotnet.microsoft.com/) / C#
- [Avalonia UI](https://avaloniaui.net/) (cross-platform desktop UI)
- [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/) for MVVM
- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) for EXIF parsing

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Linux or Windows (see [Platform support](#platform-support))

### Build, test, and run

```bash
git clone https://github.com/RickCaleg/charmera-importer.git
cd charmera-importer
dotnet test charmera-importer.slnx
dotnet run --project charmera-importer/charmera-importer.csproj
```

Development builds never replace themselves. The update banner only links to the release
page. See [packaging/README.md](packaging/README.md) for how releases are built.

## Usage

The left panel walks through the import in three steps:

1. **Camera** — plug the camera in. It's detected automatically (or pick it from the
   list), and its photos appear as thumbnails. Click one to see its details.
2. **Destination** — choose a folder, how to organize it and how to name the files. The
   *Example path* shows where a photo will end up. The Charmera repair lives here too.
3. **After import** — optionally delete the photos from the camera once they're safely
   copied.

Then click **Import N photos**. Photos imported before (matched by content, not file
name) are skipped and marked as duplicates. Language, update checks and *About* are
under the ⚙ button.

## Platform support

| Platform | Status |
|---|---|
| Linux | Fully tested, including with a real Kodak camera |
| Windows | Implemented (`DriveInfo`-based device detection + native folder picker), built and unit-tested in CI; not yet tested by hand on real Windows hardware |

Device detection on Linux works by reading `/proc/mounts` and `/sys/block/*/removable`
to find removable volumes mounted under `/media` or `/run/media` — the standard
layout on most Linux desktops.

## Known limitations

- Cameras that write no capture date at all fall back to the file's modification date
  for naming and organizing. EXIF fields a camera doesn't provide are hidden, not shown
  blank.
- Charmera **videos** (`.avi`) aren't imported yet. The camera also stamps them with a
  wrong, hard-coded date (2010-06-29).
- Thumbnails aren't generated for RAW formats (`.cr2`, `.nef`, `.arw`, `.dng`) —
  EXIF is still read for these, just no preview image.
- Automated tests cover the logic (path/naming rules, Charmera metadata repair and import,
  update version and checksum handling), not the UI or device detection. Those are
  still verified by hand.
- Only English and Portuguese are translated so far — see `Localization/Translations.cs`
  to add another language (it's just a dictionary of strings per language code).

## Project structure

```
charmera-importer/
├── Models/         # Plain data types (RemovableDevice, PhotoImportCandidate, ImportSettings, ...)
├── Services/       # Device detection, scanning, EXIF, thumbnails, hashing, import pipeline
├── ViewModels/     # MVVM view models (CommunityToolkit.Mvvm)
├── Views/          # Avalonia XAML views
├── Styles/         # Shared visual theme (colors, control styles)
└── Localization/   # Language detection/persistence and the {loc:Loc Key} XAML markup extension
tests/              # xUnit tests
packaging/          # nfpm (.deb/.rpm) config, Inno Setup script, .desktop file
.github/workflows/  # CI (build + test) and release (build + publish all installers)
```

## AI disclosure

This project was built with substantial assistance from **[Claude Code](https://claude.com/claude-code)**
(Anthropic's AI coding agent) — including architecture and implementation of
the services/view-model layers, the EXIF/duplicate-detection pipeline, and the
visual redesign of the UI. All AI-assisted work was directed, reviewed, and
tested by the project author, including manual end-to-end verification against
a real, physically connected Kodak camera. If you find a bug or something that
looks off, please open an issue — it's genuinely useful feedback regardless of
how a given line of code came to exist.

## Contributing

Issues and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT — see [LICENSE](LICENSE).
