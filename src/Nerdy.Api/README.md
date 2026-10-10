# Nerdy.Api

ASP.NET Core minimal API (.NET 10) behind the Nerdy desktop app: accounts (email + password, Microsoft, Google),
offline-first sync of personal data, and teacher classes. Wire DTOs live in `src/Nerdy.Contracts`; the protocol and
endpoint list are in `docs/ARCHITECTURE.md`. Azure deployment: `infra/README.md`.

```
Program.cs                 composition root and middleware pipeline
Features/Auth/             register/login/refresh/logout, email confirmation, password reset, external sign-in
Features/Me/               profile and account deletion
Features/Sync/             POST /api/sync (validation, last-write-wins merge, pull)
Features/Classes/          classes, members, assignments, progress (one file per sub-feature, see ClassesModule)
Data/                      NerdyDbContext, entities, EF configurations, migrations (SQL Server)
Infrastructure/            problem details, validation, rate limiting, email senders, small helpers
```

## Run locally

```powershell
dotnet run --project src/Nerdy.Api
```

Listens on **http://localhost:5080** (the desktop app's development default) in the Development environment, with a
SQLite file at `src/Nerdy.Api/App_Data/nerdy-dev.db` created on first start. Emails are written to
`App_Data/emails/*.html` instead of being sent — open them to click confirmation / reset links. OpenAPI:
`http://localhost:5080/openapi/v1.json`. Health: `http://localhost:5080/health`. Delete `App_Data` to start over.

### SQL Server in Docker (optional)

```powershell
Copy-Item .env.example .env
docker compose up -d
$env:Database__Provider = "SqlServer"
$env:ConnectionStrings__Nerdy = "Server=localhost,1433;Database=nerdy;User Id=sa;Password=Nerdy-Dev-Passw0rd;TrustServerCertificate=True"
dotnet run --project src/Nerdy.Api
```

With SQL Server the API applies the EF migrations in `Data/Migrations` on startup.

### Tests

```powershell
dotnet test tests/Nerdy.Api.Tests
```

Integration tests run the real pipeline against in-memory SQLite (one database per test class).

## Configuration

| Key | Default | Meaning |
|---|---|---|
| `ConnectionStrings:Nerdy` | Dev: `Data Source=App_Data/nerdy-dev.db` | Database connection string (relative SQLite paths resolve against the content root) |
| `Database:Provider` | `SqlServer` (Dev: `Sqlite`) | `Sqlite` uses `EnsureCreated`; `SqlServer` runs migrations and retries transient failures |
| `Jwt:Issuer` / `Jwt:Audience` | `nerdy-api` / `nerdy-app` | Access token issuer and audience |
| `Jwt:SigningKey` | Dev: fixed key | HMAC-SHA256 key, ≥ 32 bytes. Startup fails without it |
| `Jwt:AccessTokenMinutes` | `15` | Access token lifetime |
| `Jwt:RefreshTokenDays` | `30` | Refresh token lifetime (sliding: every refresh issues a new 30-day token) |
| `Authentication:Microsoft:ClientId` / `ClientSecret` | empty | Microsoft sign-in, enabled only when the client id is set |
| `Authentication:Google:ClientId` / `ClientSecret` | empty | Google sign-in, enabled only when the client id is set |
| `App:PublicBaseUrl` | Dev: `http://localhost:5080` | Origin used in email links (falls back to the request's origin) |
| `Email:AcsConnectionString` / `Email:From` | empty | Azure Communication Services email; when empty, emails go to `App_Data/emails` |
| `RateLimits:AuthPerMinute` | `20` | Requests per client IP per minute to `/api/auth/*` |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | empty | Enables OpenTelemetry export to Application Insights |

Environment variables use `__` for `:` (e.g. `Jwt__SigningKey`). For local secrets use
`dotnet user-secrets set "<key>" "<value>" --project src/Nerdy.Api`.

## For the desktop app

- **Base URL**: `http://localhost:5080` in development, `https://<webAppName>.azurewebsites.net` in production.
  Every endpoint is under `/api` except `GET /health`. JSON is camelCase and enums are strings. A JSON `null`
  for a non-nullable DTO member is rejected with 400.
- **Errors** are RFC 7807 `application/problem+json`: `{ type, title, status, detail?, traceId, code? }`.
  `code` (constants in `Nerdy.Contracts.ApiErrorCodes`) is set on every problem the API raises itself; framework
  ones (401 without or with an expired token, 404 for an unknown route, 405) have none. Validation failures are 400 with
  `code: "validation_failed"` and `errors: { "changes.notes[2].bodyRtf": ["Must be at most 2097152 characters."] }`.
  Unreadable bodies are 400 with the JSON error in `detail`. 429 responses carry `Retry-After`.
- **Tokens**: `Authorization: Bearer <accessToken>`. Access tokens last 15 minutes (claims `sub`, `email`, `name`,
  `role`, `jti`); on a 401, call `POST /api/auth/refresh` once and retry. Each refresh returns a **new** refresh
  token — store it and drop the old one. Presenting an already-used refresh token revokes the whole sign-in
  (theft detection), so serialize refreshes: two parallel refreshes with the same token sign the device out.
  A 401 from refresh (`invalid_refresh_token`) means "sign in again".
- **Login**: 401 `invalid_credentials`; 5 wrong passwords lock the account for 5 minutes (403 `locked_out`).
  Register: 409 `email_taken`, password rules (≥ 8 characters with a digit) under `errors.password`.
  `UserDto.EmailConfirmed` drives the "confirm your email" banner; `POST /api/auth/resend-confirmation` resends.
  After `PATCH /api/me` changes the role, refresh to get a token with the new `role` claim (the server always
  checks the database, so this only matters if the app reads the claim).
- **Password reset**: `forgot-password` always answers 202. The email has a link to a small page served by the API
  (`GET /api/auth/reset-password#email=…&token=…`) and the same reset code for pasting into the app, which then
  calls `POST /api/auth/reset-password { email, token, newPassword }` (204). A reset signs out every device.
- **External sign-in** (`GET /api/auth/providers` lists what is configured):
  1. Create a code verifier (43–128 chars of `A-Z a-z 0-9 - . _ ~`) and `code_challenge = base64url(SHA-256(verifier))`
     without padding (S256 only), plus a random `state`.
  2. Listen on `http://127.0.0.1:{port}/…` (or `localhost`, explicit port required) and open the system browser at
     `/api/auth/external/{Microsoft|Google}/start?redirect_uri=…&state=…&code_challenge=…`.
  3. The browser comes back to `redirect_uri?code=…&state=…` or `redirect_uri?error=…&state=…` with
     `error` ∈ `access_denied`, `provider_error`, `email_unavailable`, `email_not_verified`, `server_error`.
     Check `state`.
  4. Within 2 minutes, `POST /api/auth/external/exchange { code, codeVerifier, redirectUri }` (same `redirectUri`
     string) → `AuthResponse`. Codes are single-use; failures are 400 `invalid_code`.
- **Sync**: see `docs/ARCHITECTURE.md`. Limits per request: 2,000 rows per entity type, and per row: names/titles/
  subtitles/task ≤ 200 characters, `BodyRtf` ≤ 2 MiB, `BodyText` ≤ 200 KiB, checklist ≤ 200 items (each ≤ 1,000),
  reminder notes ≤ 10,000, exit note ≤ 2,000, `Progress` 0–100, `RepeatDays` 0–127, `EndsAt ≥ StartsAt`,
  `EndedAt ≥ StartedAt`, colours `#RRGGBB`, `Preferences.Json` ≤ 64 KiB of valid JSON, request body ≤ 100 MB.
  Lengths count UTF-16 characters. Rows are rejected (and the server copy returned) when the server's `UpdatedAt`
  is newer; equal timestamps are applied. A row whose id belongs to another account is silently dropped. 409
  `sync_conflict` means concurrent syncs of the same account collided three times — retry. Timestamps come back in
  UTC (`+00:00`), so compare `UpdatedAt` as instants, not strings.
- **Classes**: only accounts with `Role = Teacher` create classes (403 `teacher_required`). Within a class,
  permissions follow the membership role (403 `not_class_teacher` / `students_only`); classes you aren't in are 404.
  Join codes are 7 characters, case-insensitive, spaces/dashes ignored. Archived classes are read-only (409
  `class_archived` for joining, creating/editing/deleting assignments, marking done) and their assignments drop out
  of `GET /api/assignments`; `PATCH { isArchived: false }` restores. Leaving/removal deletes that student's
  completions in the class; the owner can't leave (409 `owner_cannot_leave`). Students listing members see the
  teacher(s) and themselves, never emails.
- **Progress**: `GET /api/classes/{id}/progress?from=&to=&utcOffsetMinutes=` — `from` inclusive, `to` exclusive,
  defaults to the last 7 days, at most 120 days; URL-encode the timestamps (`+` in an offset otherwise becomes a
  space). `DailyMinutes[i]` is local day `i` counted from `from` in the given offset (sessions are bucketed by start
  time; minutes are whole minutes of `ActualSeconds`). Focus numbers are null unless the student shares with that
  class (`PUT /api/classes/{id}/sharing { share }`).
