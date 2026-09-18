# Release packaging

Releases are built and published by [`.github/workflows/release.yml`](../.github/workflows/release.yml)
whenever a `v*` tag is pushed. Nothing needs to be built by hand.

## Cutting a release

1. Bump `<Version>` in `charmera-importer/charmera-importer.csproj` (used for local builds;
   CI overrides it with the tag's version anyway).
2. Move the `[Unreleased]` notes in `CHANGELOG.md` under a new `## [x.y.z] - YYYY-MM-DD`
   heading. The workflow **fails** without that section, because the GitHub release notes
   come from it.
3. Commit, then tag and push:

   ```bash
   git tag v0.2.0
   git push origin main v0.2.0
   ```

The workflow runs the tests, then builds on Linux and Windows runners in parallel, and
publishes one GitHub Release with:

| Asset | Built with | Notes |
|---|---|---|
| `charmera-importer-<v>-linux-x64.tar.gz` | `dotnet publish` + `tar` | Portable; self-updates in place |
| `charmera-importer_<v>_amd64.deb` | [nfpm](https://nfpm.goreleaser.com) ([`nfpm.yaml`](nfpm.yaml)) | Installs to `/opt`, updated via the package |
| `charmera-importer-<v>-1.x86_64.rpm` | nfpm | Same as `.deb` |
| `charmera-importer-<v>-win-x64-setup.exe` | [Inno Setup 6](https://jrsoftware.org/isinfo.php) ([`windows/charmera-importer.iss`](windows/charmera-importer.iss)) | Per-user install, no admin; self-updates |
| `SHA256SUMS` | `sha256sum` | **Required** — the updater refuses anything not listed here |

All binaries are self-contained single files (they bundle the .NET runtime), so users
don't need .NET installed.

## How self-update uses these assets

The app queries `https://api.github.com/repos/RickCaleg/charmera-importer/releases/latest`,
compares its tag with its own version, and picks the asset for its platform **by name
suffix** (`-linux-x64.tar.gz` / `-win-x64-setup.exe`, see
`charmera-importer/Services/ReleaseMetadata.cs`). Renaming an asset in the workflow means
updating those constants too, or existing installs stop finding their updates.

- **Windows**: runs the new setup with `/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS`.
  The installer's fixed `AppId` makes it upgrade the existing install in place, and its
  `[Run]` section relaunches the app after a silent install.
- **Linux portable**: extracts the tarball into a staging folder next to the running binary.
  After the app exits, a detached `/bin/sh` helper renames the new binary into place and
  relaunches it. The swap can't happen while the app runs: a single-file .NET app loads
  assemblies from its own file *by path*, so replacing it mid-run crashes the old process.
- **`.deb`/`.rpm`** (anything under `/opt` or `/usr`), dev builds, and non-x64 machines:
  the app only links to the release page. The package manager owns those files.

Only full releases count — drafts and pre-releases are ignored by `releases/latest`.

## Building locally

For testing packaging changes without tagging:

```bash
VERSION=0.1.0
dotnet publish charmera-importer/charmera-importer.csproj -c Release -r linux-x64 \
  --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version=$VERSION -o publish/linux

VERSION=$VERSION LINUX_BINARY=publish/linux/charmera-importer \
  envsubst < packaging/nfpm.yaml > nfpm.resolved.yaml
nfpm package --config nfpm.resolved.yaml --packager deb --target dist/
```

The Windows installer needs Inno Setup, which only runs on Windows (or Wine) — see the
header of `windows/charmera-importer.iss` for the `iscc` command line.
