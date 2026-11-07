# NERDY APP DOCUMENTATION

[![CI](https://github.com/eliminated/nerdyv4/actions/workflows/ci.yml/badge.svg)](https://github.com/eliminated/nerdyv4/actions/workflows/ci.yml)

> A study companion app that helps learners to plan, execute and achieve effectively with minimal or no distractions to prevent them from striving.

**TITLE**: INTRODUCING NERDY, YOUR NEW STUDY ASSISTANT FOR NEW ENDEVOUR.

**DESCRIPTION**: In this documentation, the main objective, the target audience and the end goal for this project will be described in detail. 

## OVERVIEW

The goal and objective for this project is to make an assistant app for generally to students, teachers and public users who wish to learn effectively without any distraction, and we can make most of the implementationt in order to make our objective according to their needs. 

## DOWNLOAD

Get the installer from [nerdy.nicholic.com](https://nerdy.nicholic.com/#install) or the [latest release](https://github.com/eliminated/nerdyv4/releases/latest) (`NerdyApp-win-Setup.exe`, Windows 10 2004+ or 11 on x64). It installs per user without admin rights and keeps Nerdy up to date. During the beta the installer isn't code-signed, so Windows SmartScreen asks you to confirm (**More info → Run anyway**).

## GETTING STARTED

To build Nerdy yourself you need Windows 10 version 2004 (build 19041) or later, or Windows 11 (x64 or ARM64), and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
# 1. The API, on http://localhost:5080 (uses a local SQLite database in Development)
dotnet run --project src/Nerdy.Api

# 2. The desktop app, in a second terminal
dotnet run --project src/Nerdy.App

# Tests
dotnet test tests/Nerdy.Core.Tests
dotnet test tests/Nerdy.Focus.Tests
dotnet test tests/Nerdy.Api.Tests
```

How the pieces fit together is described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); deploying to Azure is covered in [infra/README.md](infra/README.md).

## RELEASING

Releases are built by [`.github/workflows/release.yml`](.github/workflows/release.yml) with [Velopack](https://velopack.io) when a version tag is pushed:

```powershell
git tag v0.1.1
git push origin v0.1.1
```

The workflow publishes the app for win-x64, builds `NerdyApp-win-Setup.exe` (plus a full package and, from the second release on, a delta package) and publishes them as a GitHub release. Installed copies check that feed shortly after starting and every 6 hours, download the update in the background and offer a restart; an update that wasn't applied installs the next time Nerdy starts. Builds that weren't installed (development builds) never check. Versions are three-part (`major.minor.patch`); the Velopack package and the `vpk` tool in `.config/dotnet-tools.json` should stay on the same version.

## PRIVACY

What Nerdy collects and where it goes is described in the [privacy policy](https://nerdy.nicholic.com/privacy/). The website (homepage and privacy policy) lives in [`site/`](site/) and is published to GitHub Pages by [`.github/workflows/pages.yml`](.github/workflows/pages.yml); when a change affects what Nerdy collects, stores or sends, update the policy in the same pull request.

## LICENSE

Copyright (C) 2026 Nerdy contributors.

Nerdy is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; see the [LICENSE](LICENSE) file for details.

The bundled fonts (Figtree, Lexend and Atkinson Hyperlegible Next, in `src/Nerdy.App/Assets/Fonts`) are licensed separately under the SIL Open Font License 1.1; each comes with its own `-OFL.txt` file.