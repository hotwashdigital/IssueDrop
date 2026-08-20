# Contributing to IssueDrop

Thank you for helping improve IssueDrop.

## Before you start

- Search existing issues before filing a bug or feature request.
- Use a focused branch and keep pull requests small enough to review.
- Do not include tokens, private repository data, or personal draft files in an
  issue, log, screenshot, commit, or test fixture.

## Development setup

You need Windows 11, the .NET 10 SDK, and GitHub CLI. Clone the repository, then
run the complete local quality gate:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify.ps1
```

Build distributable artifacts with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
```

The portable ZIP is always produced. Install Inno Setup 6 to produce the
per-user Setup executable as well.

## Pull requests

- Explain the user-facing problem and the chosen behavior.
- Add or update tests for behavior changes.
- Run `scripts\verify.ps1` before opening the pull request.
- Update `README.md` and `CHANGELOG.md` when behavior or distribution changes.
- Keep the app dependency-light and avoid logging credentials or issue content.

By contributing, you agree that your contribution is licensed under the MIT
License in this repository.
