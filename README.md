# IssueDrop

[![CI](https://github.com/hotwashdigital/IssueDrop/actions/workflows/ci.yml/badge.svg)](https://github.com/hotwashdigital/IssueDrop/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

IssueDrop is a keyboard-first Windows 11 utility for creating GitHub issues
without leaving your current application. Press `Alt+Space`, capture the issue,
and press `Ctrl+Enter` to submit.

It is a native WPF tray app that uses your existing
[GitHub CLI](https://cli.github.com/) login. There is no IssueDrop account,
server, or telemetry, and GitHub tokens are never stored by the app.

## Install

1. Install GitHub CLI and run `gh auth login` once.
2. Download `IssueDrop-Setup-<version>.exe` from the
   [latest release](https://github.com/hotwashdigital/IssueDrop/releases/latest).
3. Run Setup, launch IssueDrop, and press `Alt+Space` from any app.

Setup installs IssueDrop for the current user, so administrator access is not
required. It adds a Start menu shortcut and offers an optional desktop shortcut.
IssueDrop starts with Windows by default; change that behavior in **Settings**.

Every release also includes a portable `IssueDrop-win-x64.zip`. Extract the
complete folder and run `IssueDrop.exe`; the .NET runtime does not need to be
installed. Verify either download against the included `SHA256SUMS.txt` if
desired.

> IssueDrop release binaries are not yet Authenticode-signed. Windows may show
> an “Unknown publisher” warning until project code signing is configured.

If another app already owns `Alt+Space`, open IssueDrop from the system tray and
choose a different shortcut in **Settings**.

## Features

- Fast capture with the Markdown description ready immediately and advanced
  fields collapsed
- Personal and organization repositories with write access
- Recent and pinned repositories with searchable selection
- Labels, multiple assignees, milestones, issue types, projects, and parent issues
- Markdown issue templates; YAML issue forms open in GitHub because their
  validation is web-defined
- Clipboard image paste and drag-and-drop files with native previews
- Local draft autosave, failure recovery, searchable history, and retention settings
- Windows light, dark, and system themes with a configurable global shortcut
- Compact success state with **Copy link**, **Open**, and **New** actions
- System tray lifecycle and launch at sign-in

### Quick tokens

Tokens are optional and are only applied when you choose a suggestion, so
ordinary `#` and `@` text is never removed accidentally.

| Token | Action |
|---|---|
| `#bug` | Add a label |
| `@username` | Add an assignee |
| `!high` | Find priority-like labels |
| `/repo:` | Change repository |
| `/milestone:` | Set milestone |
| `/type:` | Set issue type |

### Keyboard

| Shortcut | Action |
|---|---|
| `Alt+Space` | Open IssueDrop globally (configurable) |
| `Enter` from title | Expand and focus the description |
| `Ctrl+Enter` | Submit |
| `Ctrl+Shift+Space` | Open all fields |
| `Escape` | Save and dismiss |

## Attachments

Pasted images appear as thumbnails and dragged documents appear as removable
file chips. Drafts own a durable local copy, so moving the original file does
not break the draft.

GitHub does not expose its web editor's attachment upload through the public
issue API. On submission, IssueDrop creates an `issuedrop-assets` branch in the
selected repository, uploads files beneath `.issuedrop/<draft-id>/`, and inserts
their URLs into the issue body. This keeps assets out of the default branch and
preserves repository access controls.

The GitHub account must have repository contents permission to submit
attachments. If upload or issue creation fails, IssueDrop retains the entire
draft and reports the error; it never silently drops files.

## Local data and privacy

Release data is stored under `%LocalAppData%\IssueDrop`:

- `settings.json` — shortcut, theme, repository pins, and retention
- `drafts.json` — active and submitted history metadata
- `attachments\` — local draft attachment copies
- `repository-cache.json` — short-lived GitHub metadata cache
- `issuedrop.log` — bounded operational log with no tokens or attachment contents

Open the data directory from **About IssueDrop** in the tray menu. Delete
individual drafts and history in the app, or uninstall IssueDrop and remove this
directory to erase all remaining local data. Authentication stays with `gh` and
Windows Credential Manager.

See [SECURITY.md](SECURITY.md) to report a vulnerability privately.

## Build from source

Requirements:

- Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [GitHub CLI](https://cli.github.com/)
- [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the Setup executable

Run the full build and test gate:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

Build the portable release, checksums, and—when Inno Setup is installed—the
per-user installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

Artifacts are written to `artifacts`. Use `-RequireInstaller` in release
automation to fail if the installer cannot be produced.

The application intentionally has no third-party package dependencies. WPF,
tray integration, JSON persistence, and GitHub process execution use the
Windows/.NET platform libraries. Self-contained releases include the applicable
.NET license and third-party notices.

For development guidance, see [CONTRIBUTING.md](CONTRIBUTING.md). All interface
colors are semantic resources; see [docs/THEMING.md](docs/THEMING.md) before
changing themes.

## Release process

1. Move completed entries from **Unreleased** in [CHANGELOG.md](CHANGELOG.md).
2. Set the matching version in `Directory.Build.props`.
3. Run `scripts\verify.ps1` and `scripts\publish.ps1 -RequireInstaller`.
4. Merge through CI, then create and push an annotated `v<version>` tag.

The release workflow validates the tag, rebuilds from source, creates the Setup
executable, portable ZIP, and checksums, then publishes them to GitHub Releases.

## License

IssueDrop is available under the [MIT License](LICENSE).
