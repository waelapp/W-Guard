# Code signing policy / سياسة توقيع الكود

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org) *(application in review)*.

## What is signed
- `W-Guard.exe` (the application)
- `W-Guard-Setup.exe` (the installer, built with Inno Setup)

Only files built by the [GitHub Actions workflow](.github/workflows/build.yml) from the source code in this repository are submitted for signing. Nothing built on a personal computer is signed.

## Team roles
| Role | Member |
|---|---|
| Author / committer | [WAEL (waelapp)](https://github.com/waelapp) |
| Reviewer (for changes from other contributors) | [WAEL (waelapp)](https://github.com/waelapp) |
| Approver (approves every signing request) | [WAEL (waelapp)](https://github.com/waelapp) |

All team members use multi-factor authentication for GitHub and SignPath.

## Privacy policy
This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.
The only exception is a version check against `api.github.com` when the program starts (it sends a standard HTTPS request with the program version in the User-Agent and receives the latest release information). It can be turned off in **Settings → Updates**. W-Guard never downloads or runs any file by itself.

## User control
- Scanning is read-only. Every change (delete, disable, uninstall, move) is listed and needs the user's confirmation first.
- Deletes go to the Recycle Bin by default.
- The installer adds an entry to Windows "Installed apps", and W-Guard can be fully removed from there.

© 2026 WAEL
