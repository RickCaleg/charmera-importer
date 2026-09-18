# Contributing

Thanks for considering a contribution to Charmera Importer! This is a small,
personal-use tool, so the process is intentionally lightweight.

## Reporting bugs / requesting features

Open a [GitHub issue](../../issues) with:

- What you expected to happen vs. what actually happened.
- Your OS (Linux distro / Windows version) and, if relevant, your camera's
  make/model.
- Steps to reproduce, if it's a bug.

## Submitting changes

1. Fork the repo and create a branch from `main`.
2. Make your changes. Try to match the existing style:
   - MVVM with [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)
     source-generated `[ObservableProperty]` / `[RelayCommand]` — avoid manual
     backing fields where the source generator covers the case.
   - New functionality behind an interface in `Services/`, injected via the
     constructor (there's no DI container — wiring happens in `App.axaml.cs`).
   - No new NuGet dependency without a good reason — this project deliberately
     avoids native dependencies (no SQLite, no platform-specific USB libraries)
     where a simpler approach works.
3. Run the tests (`dotnet test charmera-importer.slnx`), and add tests for new pure logic
   (path rules, parsing, and similar). Then run the app
   (`dotnet run --project charmera-importer/charmera-importer.csproj`) and verify the change
   by hand, since UI and device detection aren't covered by automated tests.
4. Add a line under `[Unreleased]` in [CHANGELOG.md](CHANGELOG.md) for anything users
   will notice.
5. Open a pull request describing what changed and why, and how you tested it. CI builds
   and tests on both Linux and Windows.

By contributing, you agree that your contributions are licensed under the project's
[MIT license](LICENSE). Please also follow the [Code of Conduct](CODE_OF_CONDUCT.md).
Report security issues privately as described in [SECURITY.md](SECURITY.md), not in a
public issue.

## Development notes

- Target framework is `net10.0`; the app is built with
  [Avalonia UI](https://avaloniaui.net/) for cross-platform desktop support.
- Linux device detection reads `/proc/mounts` and `/sys/block/*/removable`
  directly (see `Services/LinuxRemovableDeviceService.cs`) rather than
  shelling out to `lsblk`/`udisksctl` or adding a native dependency.
- This project uses AI-assisted development (see the README's "AI disclosure"
  section) — that's fine to continue doing in contributions too, just make
  sure you've actually run and verified whatever you're submitting.
- Releases are fully automated: pushing a `v*` tag builds every installer and publishes
  the GitHub Release. See [packaging/README.md](packaging/README.md).
