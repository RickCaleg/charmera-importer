# Charmera Importer

A desktop app for importing photos and videos from the **Kodak Charmera** keychain camera.
It **repairs the broken metadata the camera writes into every file**, organizes everything
by the date it was taken, and never imports the same file twice.

[![CI](https://github.com/RickCaleg/charmera-importer/actions/workflows/ci.yml/badge.svg)](https://github.com/RickCaleg/charmera-importer/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/RickCaleg/charmera-importer)](https://github.com/RickCaleg/charmera-importer/releases/latest)
![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4)
![Platforms](https://img.shields.io/badge/platform-Linux%20%7C%20Windows-lightgrey)

![Charmera Importer main window](docs/images/screenshot.png)

## Why this app exists: the Charmera's broken metadata

The [Kodak Charmera](https://en.wikipedia.org/wiki/Kodak_Charmera) is a keychain digital
camera. Its firmware, running on a Generalplus chip, writes **defective EXIF metadata into
every photo**. As a result, photo libraries lose the capture date and file the pictures
under the day you copied them, show the wrong resolution, and label the camera as a chip.
Metadata editors often refuse to touch the files at all.

Charmera Importer repairs this during import, **in the copy it saves**, and is built
**exclusively for the Charmera**: every fix below is specific to that camera's firmware,
and would be wrong for any other camera.

### The defects, their cause, and the fix

All of the following was verified on original, unedited Charmera files. Every file has the
same EXIF structure: a little-endian TIFF block, 784 bytes.

**1. The capture date is in an invalid format.**
- **What the camera writes:** `DateTime`, `DateTimeOriginal` and `DateTimeDigitized`
  contain `2026:01:06:11:44:06`, with colons everywhere. The EXIF standard requires
  `YYYY:MM:DD HH:MM:SS`, with a space between date and time.
- **Effect:** standard readers (Windows, macOS/iOS Photos, Google Photos, Lightroom, the
  MetadataExtractor library…) reject the value, so the photo has *no* usable capture date.
  Software falls back to the file's modification time, which is usually the day the photos
  were copied.
- **Fix:** the date is parsed from the malformed form and written in the standard format to
  all three tags. It's kept as the local time the camera recorded, because the camera's
  clock has no time zone. If a photo has no usable date at all, the file's modification
  time is used instead.

**2. The recorded image size doesn't match the file.**
- **What the camera writes:** `PixelXDimension` × `PixelYDimension` (a.k.a.
  `ExifImageWidth/Height`) say **640×480**, but the JPEG's frame header, i.e. the actual
  image in the file, is **1440×1080**.
- **Effect:** apps that trust EXIF report the wrong resolution.
- **Fix:** the EXIF standard defines these tags as the size of the image stored in the file,
  so the real size is written, read from the JPEG's frame (SOF) header.
- **About 640×480:** it very likely isn't random. The Charmera's photos only carry
  **VGA-level detail**: the 1440×1080 image appears to be captured at about 640×480 and
  upscaled inside the camera (see [the analysis below](#appendix-how-much-detail-is-in-a-charmera-photo)).
  So 640×480 is probably the capture size, recorded in a tag that means something else.
  The repair doesn't change any pixels, it only makes the metadata describe the file correctly.

**3. The MakerNote points outside the metadata.**
- **What the camera writes:** the manufacturer-specific `MakerNote` tag claims 1,164 bytes
  at an offset that is **exactly the end of the EXIF block**, i.e. data that doesn't exist.
- **Effect:** metadata editors (exiftool, for example) report
  `Bad ExifIFD offset for MakerNoteUnknown` and refuse to write the file. Stricter parsers
  may discard the whole EXIF block.
- **Fix:** the broken MakerNote is dropped. It contains no usable information, since its
  data isn't there.

**4. The "camera" is the chip.**
- **What the camera writes:** `Make` = `Generalplus`, `Model` = `CBB3` (padded with spaces).
  That's the chip vendor and the chip, not the product.
- **Effect:** libraries group the photos under "Generalplus CBB3".
- **Fix:** `Make` = `Kodak` and `Model` = `Charmera`, plus the published lens specification:
  35 mm-equivalent focal length, f/2.4. The real focal length isn't published, so it isn't
  invented.

**5. Every video claims to be from 29 June 2010.**
- **What the camera writes:** videos are AVI files (Motion-JPEG video + PCM audio), and
  every one has the same hard-coded recording date, `2010-06-29`.
- **Effect:** apps that read the recording date (ffprobe/ffmpeg report it as
  `creation_time`) sort every Charmera video into 2010.
- **Where the real date is:** the camera writes the correct time as the file's timestamp on
  the memory card.
- **Fix:** the importer uses that timestamp to name and file the video. In the copy, it
  rewrites the standard AVI date fields (`IDIT` in the header, `ICRD` in the INFO list) that
  hold the fake date. The rewrite happens in place, in the same text format and byte length,
  so nothing else in the file moves. Only about 19 bytes of the copy change, and the video
  and audio data are untouched.
- **Scope:** a field is only rewritten when it holds exactly the known fake date. Anything
  else is left as it is.

### How the repair is done

The importer never edits the broken block in place, because its offsets can't be trusted.
Instead it:

1. Walks the JPEG's marker segments up to the image data, finding the EXIF segment (APP1)
   and the frame header (SOF), with every offset bounds-checked.
2. Salvages what's usable from the old EXIF (dates and orientation) with a tolerant reader
   that skips anything out of range.
3. Builds a **fresh, minimal, standards-compliant EXIF block**:

   | IFD0 | Exif IFD |
   |---|---|
   | Make `Kodak`, Model `Charmera`, Orientation, DateTime | DateTimeOriginal, DateTimeDigitized, PixelXDimension, PixelYDimension, ExifVersion 0232, FNumber f/2.4, FocalLengthIn35mmFilm 35, LensMake, LensModel |

4. Swaps it in for the old segment and copies every other segment **byte for byte**. That
   includes the compressed image data, the quantization/Huffman tables and the camera's
   `GPEncoder` comment. **Pixels are untouched: no re-encoding, no quality loss.**
5. Writes the result to a temporary file and renames it into place, so an interrupted
   import never leaves a half-written photo. The file keeps the original's modification time.

What it **doesn't** do:
- **The camera's files are never modified.** Importing only reads the card, unless you
  explicitly ask it to delete photos after import.
- **Unreadable files aren't lost.** If a file can't be parsed (e.g. a truncated photo), it's
  copied unchanged instead of being skipped.
- **No new dependencies.** It's pure C# in
  [`Services/CharmeraExif.cs`](charmera-importer/Services/CharmeraExif.cs), with no
  exiftool or ImageMagick required.

Duplicate detection keeps working across repairs:
- **Import history:** keyed by the SHA-256 of the *camera's* file, so re-importing an
  unformatted card skips everything.
- **Photos already in the destination:** compared against the *repaired* output, so a lost
  history doesn't produce `_1` copies.

### How videos are handled

- **Read without a decoder.** Only the AVI headers are read, with seeks, never the whole
  file. They give the size, duration, frame rate, codec, audio format and the date fields.
- **Thumbnail from the first frame.** Motion-JPEG stores each frame as a complete JPEG, so
  the first frame is shown directly, with no video decoder needed.
- **Written safely.** Each video is copied to a temporary file next to its destination and
  repaired there. It's then hashed, to recognize an identical video already at the
  destination, and renamed into place. The camera's file is never modified.
- **No MP4 conversion.** The AVI stays exactly as recorded, apart from the date. Motion-JPEG
  is large (about 2 MB per second), but converting would mean re-encoding (quality loss)
  and bundling ffmpeg. If you want smaller files, convert the imported copies yourself, e.g.
  `ffmpeg -i PICT0001.AVI -c:v libx264 -crf 20 -c:a aac PICT0001.mp4`.
  ffmpeg keeps the corrected recording date.

> **Not yet verified on an original Charmera video.** No unedited Charmera AVI was
> available while this was built. The fix targets the standard fields where ffprobe
> reports `creation_time`, which is how the fake date was documented. It was tested on real
> Motion-JPEG AVIs carrying the fake date in those fields:
> - ffprobe shows the real date after import;
> - every video frame and the audio are bit-identical;
> - the source files are unchanged.
>
> If the camera stores the date somewhere else, the video is still copied unchanged and
> named and filed by the correct date. An original `.avi` shared in an
> [issue](https://github.com/RickCaleg/charmera-importer/issues) would settle it.

### Why Charmera-only

Repairs like "the date is in the colons-everywhere form" or "Generalplus CBB3 means Kodak
Charmera" are facts about this camera, not about cameras in general. Applying them to other
cameras' files would risk corrupting good metadata. So the app only works with the Charmera:

- **Cameras:** only memory cards that are a Charmera are listed. A card is recognized by the
  `SPIDCIM` folder the camera creates next to `DCIM`, or by the Generalplus `GPEncoder`
  signature in its photos. It's judged by content, never by the volume name, so a renamed
  card still works, and other cameras and USB drives don't show up.
- **Files:** only JPEGs carrying that signature, and AVI videos, are imported. Anything
  else on the card, e.g. files copied onto it from elsewhere, is left alone.

### Verification

- **Real photos:** the repair was run on 24 original Charmera photos.
  - The output was checked with ImageMagick: all 24 read as `Kodak Charmera`, 1440×1080,
    with a valid capture date.
  - `magick compare` found **0 differing pixels** in every photo.
  - The files on the card kept identical SHA-256 hashes.
  - The photos were filed under the month they were **taken**, not the month they were
    **copied**.
- **Automated tests:** the test suite rebuilds each defect in a synthetic file with the same
  structure as the real ones. It first proves that a standard reader fails on it (no date,
  640×480, "Generalplus"), then checks the repair, the import end to end, duplicate
  handling, and card/photo detection.

### Appendix: how much detail is in a Charmera photo?

The Charmera is sold as 1.6 MP (1440×1080), and a CNET reviewer
[suspected](https://tech.yahoo.com/cameras/articles/kodak-charmera-worst-image-quality-110100308.html)
it actually captures 640×480 and upscales. To test that, the horizontal frequency spectrum
of the 24 original photos was measured and compared with controls, all saved as JPEG at the
Charmera's quality (80). The measure is the share of image energy **above the finest detail
a 640-pixel-wide image can hold**:

| Image | Energy above the 640 px limit |
|---|---|
| **Charmera, 24 original photos** | **1.8%** (0.6%–4.9%) |
| 640×480 upscaled to 1440×1080 (Lanczos) | 0.3% |
| 640×480 upscaled (bilinear) | 0.6% |
| 640×480 upscaled (bilinear) + sharpening | 1.6% |
| A real photo with genuine 1440×1080 detail | 10.1% |

**Result:** the Charmera's photos behave like a 640×480 image that was upscaled and
sharpened, with about 5× less fine detail than a real 1440×1080 photo. The same holds
vertically (480 → 1080). The spectrum alone can't prove *how* the detail was lost: a VGA
sensor upscaled by the camera and a larger sensor behind a very soft lens would look
alike. Combined with the firmware itself recording 640×480, the most likely explanation is
VGA capture with in-camera upscaling. The importer keeps the 1440×1080 files exactly as the
camera produced them.

Credits: the date, dimension and MakerNote defects were first documented by
[jphastings/charmera](https://github.com/jphastings/charmera) and
[RAIT-09/kodak-charmera-exif-fixer](https://github.com/RAIT-09/kodak-charmera-exif-fixer)
(both MIT). This app is an independent C# implementation, and adds the Make/Model finding,
card/photo detection and the resolution analysis above.

## About

Charmera Importer was built to solve a small, specific annoyance: getting
photos off a Kodak Charmera (which mounts as a plain USB drive) with the right dates,
without manually hunting through `DCIM` folders, renaming files by hand, or
accidentally re-copying photos that were already imported.

It lists removable USB drives, shows the photos on the selected one as
thumbnails, lets you pick a destination folder, a folder structure, and a
file-naming pattern, then imports everything — skipping anything that's
already been imported before, based on file content, not just the filename.

## Features

- **Kodak Charmera metadata repair** — see [above](#why-this-app-exists-the-charmeras-broken-metadata).
- **Videos too** — the Charmera's AVI videos are imported alongside photos, filed by their
  real recording date (the fake 2010 date is corrected), with thumbnails and duration. See
  [How videos are handled](#how-videos-are-handled).
- **Charmera detection** — recognizes the camera as soon as it's plugged in (Linux and
  Windows), by content rather than by name, and selects it automatically.
- **Thumbnail browser** — scans the device's `DCIM` folder and shows photos and videos
  as a grid of thumbnails, loaded progressively in the background.
- **EXIF metadata** — reads camera make/model, capture date and dimensions (taken from
  the JPEG frame, which can't be wrong), plus the full EXIF tag dump for each photo.
- **Flexible organization** — choose how imported files are organized:
  by year/month, year/month/day, or a single flat folder.
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
  delete already-imported files from the camera afterward, for people who
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
| Arch Linux (and derivatives) | one-line install script (below) | Run the script again (the app tells you when) |

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

**Arch Linux (and derivatives: EndeavourOS, Manjaro, Omarchy…):**

```bash
curl -fsSL https://raw.githubusercontent.com/RickCaleg/charmera-importer/main/packaging/arch/install.sh | bash
```

The script:
- builds a proper pacman package, `charmera-importer-bin`, from the official release,
  after checking its checksum against the release's `SHA256SUMS`;
- installs it with `pacman`, so the app shows up in your menu and uninstalls cleanly. It
  asks for your sudo password only for this step.

To manage it later:
- **Update:** run the script again.
- **Install a specific version:** `… | bash -s -- --version 0.5.0`.
- **Uninstall:** `… | bash -s -- --uninstall`. Your settings and imported files are kept.

Prefer to read it first? [Download `install.sh`](packaging/arch/install.sh), or build the
[`PKGBUILD`](packaging/arch/PKGBUILD) yourself with `makepkg -si`.

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

1. **Camera** — plug the Charmera in. It's recognized and selected automatically, and its
   photos and videos appear as thumbnails (videos show their duration). Click one to see
   its details, already read with the repaired date and size.
2. **Destination** — choose a folder, how to organize it and how to name the files. The
   *Example path* shows where a photo will end up. Every imported copy gets its metadata
   repaired; there's nothing to switch on.
3. **After import** — optionally delete the files from the camera once they're safely
   copied.

Then click **Import N photos and M videos**. Files imported before (matched by content,
not file name) are skipped and marked as duplicates. Language, update checks and *About* are
under the ⚙ button.

## Platform support

| Platform | Status |
|---|---|
| Linux | Fully tested, including the repair on original Charmera photos |
| Windows | Implemented (`DriveInfo`-based device detection + native folder picker), built and unit-tested in CI; not yet tested by hand on real Windows hardware |

Device detection on Linux works by reading `/proc/mounts` and `/sys/block/*/removable`
to find removable volumes mounted under `/media` or `/run/media` — the standard
layout on most Linux desktops.

## Known limitations

- Only the Kodak Charmera is supported, on purpose (see
  [Why Charmera-only](#why-charmera-only)).
- Videos are imported as AVI (Motion-JPEG), not converted to MP4. See
  [How videos are handled](#how-videos-are-handled).
- The video date repair hasn't been checked against an original Charmera video yet (see
  the note in that section).
- A photo with no usable date at all (e.g. the camera's clock was never set) is named and
  organized by the file's modification date.
- Automated tests cover the logic (path/naming rules, the photo and video repairs and
  their import, card detection, update version and checksum handling), not the UI. Those are
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
