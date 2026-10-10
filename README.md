# NERDY APP DOCUMENTATION

[![CI](https://github.com/eliminated/nerdyv4/actions/workflows/ci.yml/badge.svg)](https://github.com/eliminated/nerdyv4/actions/workflows/ci.yml)

> A study companion app that helps learners to plan, execute and achieve effectively with minimal or no distractions to prevent them from striving.

**TITLE**: INTRODUCING NERDY, YOUR NEW STUDY ASSISTANT FOR NEW ENDEVOUR.

**DESCRIPTION**: In this documentation, the main objective, the target audience and the end goal for this project will be described in detail. 

## OVERVIEW

The goal and objective for this project is to make an assistant app for generally to students, teachers and public users who wish to learn effectively without any distraction, and we can make most of the implementationt in order to make our objective according to their needs. 

## GETTING STARTED

You need Windows 10 version 2004 (build 19041) or later, or Windows 11 (x64 or ARM64), and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

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

## LICENSE

Copyright (C) 2026 Nerdy contributors.

Nerdy is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version. It is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; see the [LICENSE](LICENSE) file for details.

The bundled fonts (Figtree, Lexend and Atkinson Hyperlegible Next, in `src/Nerdy.App/Assets/Fonts`) are licensed separately under the SIL Open Font License 1.1; each comes with its own `-OFL.txt` file.