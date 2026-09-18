# Security policy

## Supported versions

Only the latest release receives fixes. The app updates itself (or tells you where to
download the new version), so staying current is usually one click.

## Reporting a vulnerability

Please **don't open a public issue** for security problems. Report them privately through
GitHub instead: go to the repository's **Security** tab and choose
**[Report a vulnerability](https://github.com/RickCaleg/charmera-importer/security/advisories/new)**.

Include what you found, how to reproduce it, and what an attacker could do with it. You
should get a reply within a week. Once a fix is released, you'll be credited in the
advisory unless you'd rather stay anonymous.

## What's in scope

The areas that matter most for this app:

- **Self-update** — the app downloads and runs release assets from GitHub. Downloads are
  verified against the release's `SHA256SUMS` before installation; anything that could
  bypass that check is a vulnerability.
- **File handling** — importing copies files from removable media and can optionally
  delete them from the camera. Anything that makes it write outside the chosen destination
  folder, or delete files it shouldn't, is in scope.
