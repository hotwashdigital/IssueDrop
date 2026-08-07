# IssueDrop

IssueDrop is a keyboard-first Windows 11 utility for creating GitHub issues without leaving your current application. Press `Alt+Space`, capture the issue, and press `Ctrl+Enter` to submit.

The app is a native, borderless WPF tray utility inspired by TickTick's quick-add experience. It uses the existing [GitHub CLI](https://cli.github.com/) login and stores drafts locally. There is no IssueDrop account, server, or telemetry.

The composer stays visible and topmost while you switch applications, take screenshots, use the clipboard, or cancel a file picker. Only an explicit `Escape` dismisses an unfinished entry. Repository-field pickers close when you click elsewhere without dismissing the composer.

## Run it

1. Install GitHub CLI and run `gh auth login` once.
2. Open `IssueDrop.exe` from the `IssueDrop-win-x64` release folder.
3. Press `Alt+Space` from anywhere. Every shortcut invocation starts a fresh issue; unfinished work remains available from the inline **Drafts** button. If another app already owns that shortcut, open IssueDrop from the tray and choose another shortcut in **Settings**.

The release is self-contained. .NET does not need to be installed. IssueDrop registers itself to start with Windows on first launch; this can be disabled in Settings.

## What it supports

- Fast capture with the Markdown description ready immediately and advanced fields collapsed
- Personal and organization repositories with write access
- Recent and pinned repositories with searchable selection
- Labels, multiple assignees, milestones, issue types, projects, and parent issues
- Markdown issue templates; YAML issue forms open in GitHub because their validation is web-defined
- Clipboard image paste and drag/drop files with native previews
- Local draft autosave, an inline draft picker, failure recovery, searchable history, and retention settings
- Windows light/dark/system themes and configurable global shortcut
- Compact success state with **Copy link**, **Open**, and **New** actions
- Tray lifecycle and launch-at-login

### Quick tokens

Tokens are optional and are only applied when you choose a suggestion, so ordinary `#` and `@` text is never removed accidentally.

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

Pasted images appear as thumbnails and dragged documents appear as removable file chips. Drafts own a durable local copy, so moving the original file does not break the draft.

GitHub does not provide its web editor's attachment-upload endpoint through the public issue API. On submission, IssueDrop therefore creates an `issuedrop-assets` branch in the selected repository, uploads files beneath `.issuedrop/<draft-id>/`, and inserts their URLs into the issue body. This keeps assets out of the default branch and preserves repository access controls.

The GitHub account must have repository contents permission to submit attachments. If uploading or issue creation fails, IssueDrop retains the entire draft and reports the exact error; it never silently drops files.

## Local data

Release data is stored under `%LocalAppData%\IssueDrop`:

- `settings.json` — shortcut, theme, repository pins, and retention
- `drafts.json` — active and submitted history metadata
- `attachments\` — local draft attachment copies
- `repository-cache.json` — short-lived GitHub metadata cache
- `issuedrop.log` — bounded operational log with no tokens or attachment contents

Delete individual drafts/history from the in-app history window. IssueDrop never stores the GitHub token; authentication stays with `gh` and Windows Credential Manager.

## Build from source

Requirements: Windows 11, .NET 10 SDK, and GitHub CLI.

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The portable release and ZIP are written to `artifacts`. To run validation:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

The solution intentionally has no third-party application dependencies. WPF, tray integration, JSON persistence, and GitHub process execution use the Windows/.NET platform libraries.

All interface colors are semantic resources. See [`docs/THEMING.md`](docs/THEMING.md) to add or adjust a theme without editing control markup.
