# Nerdy — Architecture

Nerdy is a native Windows study planner for students, teachers, and lifelong learners: calendar, recurring
study blocks with clash detection, reminders, subjects (topics and subtopics with progress), notes, Focus / Ultra-Focus sessions that
really block distractions, statistics, and teacher classes. Reference design: the Claude Design prototype
(see `docs/DESIGN.md`).

## Solution layout

```
Nerdy.slnx
src/
  Nerdy.Contracts/   net10.0               DTOs + enums shared by the API and the desktop app (wire format)
  Nerdy.Core/        net10.0               Pure client-side domain logic: recurrence, schedule planner, stats, formatting
  Nerdy.Focus/       net10.0-windows       Focus enforcement engine (Win32): finds app windows and hides or closes the ones not allowed
  Nerdy.Api/         net10.0               ASP.NET Core API (Azure App Service + Azure SQL)
  Nerdy.App/         net10.0-windows10.0.26100.0  WinUI 3 desktop app (unpackaged, self-contained Windows App SDK 2.5)
tests/
  Nerdy.Core.Tests/  Nerdy.Focus.Tests/  Nerdy.Api.Tests/
infra/               Bicep for Azure (App Service + Azure SQL + Key Vault refs)
tools/               make-icon.ps1 and other dev scripts
```

## Key decisions

| Area | Decision | Why |
|---|---|---|
| Desktop UI | WinUI 3 + Windows App SDK 2.5, MVVM (CommunityToolkit.Mvvm), Generic Host for DI | Native Fluent look of the prototype, deep OS access for focus enforcement |
| Packaging | Unpackaged, self-contained WinAppSDK | Runs with `dotnet run`, no runtime install; MSIX can come later |
| Backend | ASP.NET Core minimal APIs on Azure App Service, EF Core | C# end to end, shares `Nerdy.Contracts` with the app |
| Database | Azure SQL (SQL Server provider, EF migrations). Local dev: SQLite file by default (zero setup) or SQL Server in Docker (`docker compose up`) | Parity when needed, frictionless otherwise |
| Auth | ASP.NET Core Identity on our API. Email + password, Microsoft account, Google. JWT access tokens (15 min) + rotating refresh tokens (30 days, stored hashed) | One user store we control; external providers optional by config |
| Desktop sign-in for Microsoft/Google | System browser + loopback redirect (`http://127.0.0.1:{port}/callback`) + PKCE-style one-time code exchange | Recommended pattern for native apps (RFC 8252); no embedded web views |
| Offline | App keeps a local SQLite mirror of the user's data and works fully offline; syncs when online | Focus sessions must never depend on the network |
| Sync | Per-user version counter, push dirty rows / pull rows with `Version > since`, last-write-wins by `UpdatedAt`, soft deletes | Simple, robust, provider-agnostic |
| Classes | Server-authoritative (not synced offline-editable); app caches the last response for offline viewing | Shared data between teacher and students |
| Focus blocking | User-level and transparent: minimise or close disallowed app windows (setting), browsers are apps like any other, Ultra-Focus is a full-screen always-on-top window that reclaims the foreground. No keyboard hooks, no network rerouting, no background services | Strict enough to keep you on task, without techniques that read input or intercept traffic (and that antivirus rightly flags) |

## Data model

All personal entities are synced and share: `Id (Guid, client-generated)`, `UpdatedAt (DateTimeOffset, set by the client on every edit)`,
`IsDeleted (soft delete)`. The server also stores `UserId` and `Version (long)`.

| Entity | Fields |
|---|---|
| Subject | Name, ColorHex, DueTitle?, DueAt?, SortOrder. Everything that links to a subject uses its colour |
| Topic | SubjectId, Name, SortOrder. Progress = average of its subtopics |
| Subtopic | TopicId, Name, Progress (0–100), SortOrder. A subject's progress is the average of all its subtopics |
| CalendarEvent | Title, Subtitle?, SubjectId?, TopicId? (a topic of that subject), StartsAt, EndsAt, IsAllDay, IsMilestone (exam/deadline), RepeatDays (bitmask Mon=1 … Sun=64, 0 = once), RepeatUntil?, ReminderMinutes?, AutoFocus, AutoFocusMode, SeriesId? (sessions created together by one study block) |
| Reminder | Title, DueAt?, HasDueTime, SubjectId?, TopicId?, IsStarred, IsDone, CompletedAt?, Notes? |
| Note | Title, SubjectId?, TopicId?, SubtopicId?, BodyRtf, BodyText (plain text for search/snippets), Checklist [{Text, Done}] |
| FocusSession | SubjectId?, TopicId?, Task, Mode (Focus/Ultra), PlannedSeconds, ActualSeconds, StartedAt, EndedAt, Outcome (Completed/EndedEarly/EmergencyExit/Interrupted), ExitReason?, ExitNote? |
| Preferences | One JSON document per user (`PreferencesDto.Json`) + UpdatedAt. Schema owned by the app so new settings need no server change |

Server-only entities: `AppUser` (Identity: DisplayName, Role, OnboardingCompleted), `RefreshToken`, `ExternalLoginCode`,
`UserSyncState (UserId, Version)`, `Classroom`, `ClassMembership`, `Assignment`, `AssignmentCompletion`.

## Sync protocol

`POST /api/sync` with `SyncRequest { Protocol, SinceVersion, Changes }` → `SyncResponse { Protocol, Version, Changes, ServerTime }`.

`Protocol` is the sync format (`SyncProtocol.Current`; 2 since subjects were added in Nerdy 0.1.1). The server answers an
older format with 426 `update_required` and applies nothing, and the app then looks for an update straight away; its
unsynced edits wait on the PC. The app likewise ignores a response in an older format than its own. A change to the
format also needs the same change to rows already stored: an EF migration on the server and a `LocalSchema` upgrade in
the app (`src/Nerdy.Core/Storage`), both keeping row versions so nothing has to be downloaded again.

Server, in one transaction:
1. Increment the user's `UserSyncState.Version` once (optimistic concurrency; retry on conflict) → `v`.
2. For each incoming row: if no server row exists, insert it. If one exists and `incoming.UpdatedAt >= existing.UpdatedAt`, overwrite it.
   Otherwise reject it (server copy is newer). Applied rows get `Version = v`. A row whose `Id` belongs to another user is rejected.
3. Respond with every row of the user where `Version > SinceVersion`, plus the current server copy of each rejected row.
   `Version` in the response is the user's latest version; the client stores it as its next `SinceVersion`.

Client: marks local edits dirty, pushes all dirty rows, upserts everything returned (clearing dirty only for rows whose
`UpdatedAt` still equals what was pushed), runs on start, ~2 s after local edits, every 5 minutes, and when the network returns.

## API surface (all JSON, `/api` prefix, errors are RFC 7807 problem details)

Auth (anonymous unless noted):
- `POST /auth/register` `{email, password, displayName}` → `AuthResponse`
- `POST /auth/login` `{email, password}` → `AuthResponse`
- `POST /auth/refresh` `{refreshToken}` → `AuthResponse` (rotates the refresh token; reuse of a revoked token revokes the whole family)
- `POST /auth/logout` `{refreshToken}` → 204
- `POST /auth/forgot-password` `{email}` → 202 (always, no user enumeration)
- `POST /auth/reset-password` `{email, token, newPassword}` → 204
- `GET  /auth/confirm-email?userId=&token=` → small HTML page
- `POST /auth/resend-confirmation` (authorized) → 202
- `GET  /auth/providers` → `["Microsoft","Google"]` (only those configured)
- `GET  /auth/external/{provider}/start?redirect_uri=&state=&code_challenge=` → 302 to provider. `redirect_uri` must be `http://127.0.0.1:{port}/...` or `http://localhost:{port}/...`
- `GET  /auth/external/callback` (internal) → 302 to `redirect_uri?code=&state=` (or `?error=`)
- `POST /auth/external/exchange` `{code, codeVerifier, redirectUri}` → `AuthResponse` (code is single-use, expires in 2 minutes, `SHA256(codeVerifier)` base64url must equal `code_challenge`)

Me (authorized): `GET /me` → `UserDto`, `PATCH /me` `UpdateProfileRequest`, `DELETE /me` (deletes the account and all data)
- Until onboarding is done, `UserDto.roleSuggestion` carries a role guessed from the email domain (`RoleSuggester`):
  school/university address → Student, or Teacher when the address hints at staff (`staff.uni.edu`, `prof.lee@…`);
  alumni address → Lifelong learner; personal provider (gmail.com…) → Lifelong learner; other organisations →
  Researcher; reserved/test domains → none. Onboarding pre-selects it and says why; the person confirms or changes it.
  Students and staff usually share a domain, so this is a hint, never a classifier.

Sync (authorized): `POST /sync`

Classes (authorized):
- `GET /classes` → `ClassroomDto[]` (classes I teach or joined)
- `POST /classes` (Role = Teacher) `CreateClassroomRequest` → `ClassroomDto`
- `GET|PATCH|DELETE /classes/{id}` (PATCH/DELETE teacher only)
- `POST /classes/join` `{joinCode}` → `ClassroomDto`
- `POST /classes/{id}/join-code` (teacher) → `ClassroomDto` with a new code
- `GET /classes/{id}/members` → `ClassMemberDto[]` (teacher: everyone; student: teacher + count)
- `DELETE /classes/{id}/members/{userId}` (teacher removes anyone; a student may remove themself = leave)
- `PUT /classes/{id}/sharing` `{share}` (student opts in/out of sharing focus stats with this class)
- `GET /classes/{id}/assignments`, `POST /classes/{id}/assignments` (teacher)
- `GET /assignments` → all assignments from every class I'm a student in (feeds Reminders + Calendar)
- `PATCH|DELETE /assignments/{id}` (teacher), `PUT /assignments/{id}/done` `{isDone}` (student)
- `GET /classes/{id}/progress?from=&to=` (teacher) → `ClassProgressDto` (focus numbers only for students who opted in)

Focus (authorized):
- `POST /focus/exit-review` `ExitReviewRequest {reason, explanation, task?, minutesLeft, plannedMinutes}` →
  `ExitReviewResponse {allowed, message?, reviewed}`. Rate-limited per account (`ExitReview:PerUserPerHour`, default 20/h).

Health: `GET /health` (anonymous).

## Desktop app structure (`src/Nerdy.App`)

```
App.xaml(.cs)            Generic Host, DI, startup (crash recovery, sign-in, onboarding routing)
MainWindow.xaml          Custom title bar (back, search Ctrl+K), Mica, hosts ShellPage / AuthPage / FocusPages
Views/                   One page per prototype screen (Home A/B, Calendar, Schedule, Reminders, Subjects, Notes,
                         Focus, FocusActive, UltraFocus, Stats, Settings, Classes, Onboarding, Auth)
ViewModels/              CommunityToolkit.Mvvm view models
Controls/                Reusable visuals (timer ring, calendar grids, heatmap, stacked bars, toggle chips…)
Services/                ApiClient, AuthService, TokenStore (DPAPI), SyncService, DataService + LocalDatabase (SQLite),
                         PreferencesService, ThemeService, NavigationService, ToastService, DialogService,
                         FocusSessionService (wraps Nerdy.Focus), SearchService, AgendaService, ClassesService,
                         Notifications/ (NotificationService, ReminderScheduler), Shell/ (AppLifetime, TrayIcon)
Themes/                  Colours, typography, control styles matching the prototype
Assets/Fonts/            Figtree, Lexend, Atkinson Hyperlegible Next (variable TTFs, SIL Open Font License; licence
                         texts ship next to each font)
```

### Notifications and running in the background

- **Toasts** use the built-in `Windows.UI.Notifications` API. The Windows App SDK's `AppNotificationManager` needs the
  separately installed Windows App Runtime (its registration fails in this self-contained build), so Nerdy registers
  its app id `Nerdy.Desktop` per user under `HKCU\Software\Classes\AppUserModelId` (display name + icon) and handles
  clicks in-process. There is no COM activator yet, so clicking an old toast after Nerdy has quit does nothing.
- **ReminderScheduler** checks every 5 s while someone is signed in: reminders at their due time (date-only ones at
  the start of the study day, with *Mark done* / *Snooze 10 min*), events at their reminder time, and class
  assignments a day before they're due. Auto-focus study blocks (Focus or Ultra) get a heads-up one minute before
  (*Start now* / *Skip this time*) and then start full screen on their own at the start time, closing any open dialog.
  If the PC is locked (LogonUI running) or had no input for 10 minutes, or the start was missed (asleep, not running),
  Nerdy asks instead ("started while you were away — Start focus?"). Reminders, event reminders, heads-ups and those
  prompts use the toast *reminder* scenario, so they stay on screen until acted on. Tags are hashed to Windows' 16-character limit.
  Anything that fell due in the last 10 minutes still goes out once, and an event whose reminder time passed before it
  was saved (but which hasn't started) gets it straight away; sent keys and pending snoozes are kept per account (`notify.fired`, `notify.snoozed`).
  With *Settings → Notifications* off nothing is sent, except that auto-focus blocks still ask inside Nerdy.
- **Notification area** (`TrayIcon`, Shell_NotifyIcon on a hidden window of the UI thread): with *Keep running in the
  notification area* on and someone signed in, closing the window hides it; a normal Focus session keeps running and
  the icon's tooltip shows the time left. Click = open; right-click = Open / Start a focus session / Quit. Ultra-Focus
  can't be closed or quit. A second launch of Nerdy.exe brings the window back (single instance).

## Focus enforcement (`src/Nerdy.Focus`)

Only ever active during a session the user started, always visible, and fully reverted when it ends.

- **Apps**: every visible top-level window whose process is not on the allowed list is minimised (setting: *Hide*) or
  closed (setting: *Close*: the app asks it to close, then ends the process after a grace period; Nerdy lists what will
  close and asks for confirmation before the session starts). A WinEvent hook (window shown / brought to front — no
  input is observed) re-applies the rule when something opens mid-session. Shell surfaces (taskbar, desktop, Start,
  input/IME hosts, lock screen, Task Manager) and elevated apps are never touched. Hidden windows are restored at the end.
- **Websites**: browsers are apps like any other — blocked unless allowed. Allowed websites are one-click links in the
  session dock. Planned: an Edge/Chrome extension that blocks non-allowed sites only while a Nerdy session runs.
- **Ultra-Focus**: full-screen, always-on-top window that takes the foreground back whenever a non-allowed window gets
  it; no pause/end; emergency exit needs a reason, an explanation of ≥ 15 characters (not for medical or safety emergencies) and a
  3-second hold, may be reviewed by AI (below), and is logged.
  Nerdy never captures keystrokes; Ctrl+Alt+Del / Task Manager always work. If Nerdy is closed mid-session, the next
  launch records it as *Interrupted*.

### AI review of early Ultra-Focus exits

The person chose Ultra-Focus to be hard to leave, so a non-emergency exit can be checked by Claude before it goes
through. Safety comes first: nobody is ever trapped.

- **What is reviewed**: only *Urgent school or work matter* and *Something else*. *Medical or safety emergency* needs only
  the 3-second hold (no explanation); *Family emergency* needs the explanation. Neither is ever sent for review.
- **Flow**: when the hold completes, the app posts the reason, explanation, task name and minutes left to
  `/api/focus/exit-review`. The API asks `claude-haiku-5-5` (structured JSON output: `allow` or `stay` plus a short,
  kind message). On `stay` the dialog shows the message ("Reviewed by Claude (AI)") and the person can add detail and
  hold again, or pick another reason.
- **Fails open**: no API key, timeout (8 s server / 12 s app), rate limit, refusal, unreadable answer, offline app or any
  server error → the exit is allowed. The student's text is wrapped in `<explanation>` tags and treated as data only.
- **Privacy and control**: the Anthropic key lives only on the server (Key Vault in Azure). The exit dialog says when an
  explanation will be reviewed, and *Settings → Focus → AI review of early Ultra-Focus exits* turns it off
  (`Preferences.AiExitReview`, on by default).
