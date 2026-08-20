# Security policy

## Reporting a vulnerability

Please do not open a public issue for a suspected vulnerability. Use
[GitHub's private vulnerability reporting](https://github.com/hotwashdigital/IssueDrop/security/advisories/new)
and include the affected version, reproduction steps, and potential impact.

You should receive an acknowledgement within seven days. Confirmed issues will
be coordinated privately until a fix and disclosure plan are ready.

## Supported versions

Security fixes are provided for the latest published release. Upgrade to the
newest version before reporting an issue that may already have been corrected.

## Security model

IssueDrop delegates authentication to GitHub CLI and never reads or stores a
GitHub token itself. Local settings, drafts, attachments, and logs are stored in
the current user's `%LocalAppData%\IssueDrop` directory.
