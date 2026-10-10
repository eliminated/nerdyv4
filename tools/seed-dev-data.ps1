# Seeds a local development account with a realistic week (timetable, reminders, subtopics, notes and
# eight weeks of focus history) through the real sync endpoint, the same way another device would.
# Dev only: reads tools/dev-account.local.json and targets a local API.
#   pwsh tools/seed-dev-data.ps1 [-Api http://localhost:5080]
param([string]$Api = "http://localhost:5080")
$ErrorActionPreference = "Stop"

if ($Api -notmatch '^http://(localhost|127\.0\.0\.1)(:\d+)?$') { throw "Refusing to seed a non-local API: $Api" }
$account = Get-Content "$PSScriptRoot/dev-account.local.json" | ConvertFrom-Json
$login = Invoke-RestMethod "$Api/api/auth/login" -Method Post -ContentType "application/json" `
    -Body (@{ email = $account.email; password = $account.password } | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($login.accessToken)" }

function Sync($changes, [long]$since) {
    Invoke-RestMethod "$Api/api/sync" -Method Post -Headers $headers -ContentType "application/json" `
        -Body (@{ sinceVersion = $since; changes = $changes } | ConvertTo-Json -Depth 10)
}

$now = [DateTimeOffset]::Now
$stamp = $now.ToUniversalTime().ToString("o")
$pulled = Sync @{} 0
$topics = @{}
foreach ($t in $pulled.changes.topics | Where-Object { -not $_.isDeleted }) { $topics[$t.name] = $t.id }

# Topics (reuse the ones created during onboarding).
$wanted = [ordered]@{ "Biology" = "#1F9D74"; "Calculus" = "#E8743B"; "World History" = "#D6457A"; "Thesis research" = "#2F7DE1" }
$newTopics = @()
$order = 0
foreach ($name in $wanted.Keys) {
    if (-not $topics.Contains($name)) {
        $id = [guid]::NewGuid().ToString()
        $topics[$name] = $id
        $newTopics += @{ id = $id; updatedAt = $stamp; name = $name; colorHex = $wanted[$name]; sortOrder = $order }
    }
    $order++
}
$bio = $topics["Biology"]; $calc = $topics["Calculus"]; $hist = $topics["World History"]; $thesis = $topics["Thesis research"]

$subs = @(
    @($bio, "Cell structure", 80), @($bio, "Genetics", 45), @($bio, "Evolution", 20), @($bio, "Ecology", 0),
    @($calc, "Integration techniques", 90), @($calc, "Series & sequences", 35), @($calc, "Parametric equations", 10),
    @($hist, "Industrial Revolution", 70), @($hist, "The Cold War", 25),
    @($thesis, "Literature review", 60), @($thesis, "Methods", 30), @($thesis, "Data analysis", 5)
) | ForEach-Object -Begin { $i = 0 } -Process {
    @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; topicId = $_[0]; name = $_[1]; progress = $_[2]; sortOrder = ($i++) }
}

# Weekly timetable starting this Monday (repeats every week on its weekday).
$monday = [DateTime]::Today.AddDays(-(([int][DateTime]::Today.DayOfWeek + 6) % 7))
function Ev($day, [double]$s, [double]$e, $title, $sub, $topic, [int]$repeat = 0, [bool]$milestone = $false) {
    $start = $monday.AddDays($day).AddHours($s); $end = $monday.AddDays($day).AddHours($e)
    @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; title = $title; subtitle = $sub; topicId = $topic
       startsAt = ([DateTimeOffset]$start).ToString("o"); endsAt = ([DateTimeOffset]$end).ToString("o")
       repeatDays = $repeat; isMilestone = $milestone; reminderMinutes = 10 }
}
$mon = 1; $tue = 2; $wed = 4; $thu = 8; $fri = 16; $sat = 32; $sun = 64
$events = @(
    (Ev 0 9 10.5 "Calculus II lecture" "Room 204" $calc ($mon -bor $wed -bor $fri)),
    (Ev 0 13 14 "Biology lab" "Cell structure" $bio $mon),
    (Ev 0 16 17.5 "Literature review" "Thesis research" $thesis $mon),
    (Ev 1 10 11.5 "History seminar" "Industrial Revolution" $hist $tue),
    (Ev 1 14 15 "Problem set 4" "Integration techniques" $calc $tue),
    (Ev 2 12 13 "Office hours" "Dr. Okafor" $thesis $wed),
    (Ev 2 15 16.5 "Genetics reading" "Ch. 9–10" $bio $wed),
    (Ev 3 9 10 "Cell structure review" "Flashcards" $bio $thu),
    (Ev 3 11 12.5 "Methods draft" "Thesis research" $thesis $thu),
    (Ev 3 14 15 "Series & sequences" "Ratio test practice" $calc $thu),
    (Ev 3 16.5 17.5 "History reading" "Cold War, ch. 3" $hist $thu),
    (Ev 4 13 15 "Data analysis" "Thesis research" $thesis $fri),
    (Ev 5 10 11.5 "Bio flashcards" "Organelles" $bio $sat),
    (Ev 6 17 18 "Weekly review" "Plan next week" $null $sun),
    (Ev 10 14 15 "Calculus quiz" "Room 204" $calc 0 $true),
    (Ev 16 9 11 "Biology midterm" "Hall B" $bio 0 $true)
)

function Rem($title, $due, [bool]$hasTime, $topic, [bool]$star = $false, [bool]$done = $false) {
    @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; title = $title; dueAt = $(if ($due) { ([DateTimeOffset]$due).ToString("o") } else { $null })
       hasDueTime = $hasTime; topicId = $topic; isStarred = $star; isDone = $done }
}
$today = [DateTime]::Today
$reminders = @(
    (Rem "Submit Calculus problem set 4" $today.AddHours(23).AddMinutes(59) $true $calc $true),
    (Rem "Email Dr. Okafor about methods chapter" $today.AddHours(15) $true $thesis),
    (Rem "Review flashcards: organelles" $today.AddHours(20) $true $bio),
    (Rem "Return `"The Age of Revolution`" to library" $today.AddDays(1) $false $hist),
    (Rem "Book a study room for group session" $today.AddDays(3) $false $null),
    (Rem "Read chapter 7: Series tests" $today.AddDays(-1) $false $calc $false $true)
)

$notes = @(
    @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; title = "Mitosis vs. meiosis"; topicId = $bio
       bodyRtf = ""; bodyText = "Both start with DNA replication during interphase. Mitosis gives two identical diploid cells; meiosis gives four genetically different haploid cells."
       checklist = @(@{ text = "Draw both processes side by side"; done = $true }, @{ text = "Make 10 flashcards"; done = $false }, @{ text = "Watch lecture 6 recap"; done = $false }) },
    @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; title = "Integration by parts cheatsheet"; topicId = $calc
       bodyRtf = ""; bodyText = "Use LIATE to choose u: logs, inverse trig, algebraic, trig, exponential."; checklist = @() },
    @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; title = "Sources to read this week"; topicId = $thesis
       bodyRtf = ""; bodyText = "Priority: Chen (2024), Alvarez & Ito (2023), the OECD survey appendix."; checklist = @() }
)

# Eight weeks of focus history.
$rand = [Random]::new(20261008)
$taskNames = @{ $bio = @("Cell structure review", "Genetics reading", "Organelle flashcards"); $calc = @("Problem set 3", "Integration practice", "Series & sequences");
                $hist = @("Industrial Revolution reading", "Essay outline"); $thesis = @("Literature review", "Methods draft", "Data cleaning") }
$topicPool = @($calc, $calc, $thesis, $thesis, $bio, $bio, $hist)
$sessions = @()
for ($ago = 55; $ago -ge 1; $ago--) {
    $day = $today.AddDays(-$ago)
    if ($rand.NextDouble() -lt 0.18) { continue }
    $t = 8.5 + $rand.NextDouble()
    $count = 1 + $rand.Next(3)
    for ($k = 0; $k -lt $count; $k++) {
        $topic = $topicPool[$rand.Next($topicPool.Count)]
        $planned = @(25, 30, 45, 50, 60)[$rand.Next(5)] * 60
        $ultra = $rand.NextDouble() -lt 0.17
        $early = $rand.NextDouble() -lt 0.12
        $actual = if ($early) { [int]($planned * (0.3 + $rand.NextDouble() * 0.5)) } else { $planned }
        $outcome = if (-not $early) { "Completed" } elseif ($ultra) { "EmergencyExit" } else { "EndedEarly" }
        $start = [DateTimeOffset]$day.AddHours([Math]::Round($t * 4) / 4)
        $s = @{ id = [guid]::NewGuid().ToString(); updatedAt = $stamp; topicId = $topic; task = $taskNames[$topic][$rand.Next($taskNames[$topic].Count)]
                mode = $(if ($ultra) { "Ultra" } else { "Focus" }); plannedSeconds = $planned; actualSeconds = $actual
                startedAt = $start.ToString("o"); endedAt = $start.AddSeconds($actual).ToString("o"); outcome = $outcome }
        if ($outcome -eq "EmergencyExit") { $s.exitReason = "Family"; $s.exitNote = "My brother needed picking up from school early." }
        $sessions += $s
        $t += $planned / 3600 + 0.5 + $rand.NextDouble() * 1.5
    }
}

$result = Sync @{ topics = $newTopics; subtopics = $subs; events = $events; reminders = $reminders; notes = $notes; focusSessions = $sessions } $pulled.version
"Seeded: $($newTopics.Count) topics, $($subs.Count) subtopics, $($events.Count) events, $($reminders.Count) reminders, $($notes.Count) notes, $($sessions.Count) sessions (server version $($result.version))."
