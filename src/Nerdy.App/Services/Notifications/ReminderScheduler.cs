using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Dispatching;
using Nerdy.App.Models;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Shell;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;

namespace Nerdy.App.Services.Notifications;

/// <summary>
/// Sends reminders while Nerdy runs (window open or in the notification area):
/// <list type="bullet">
/// <item>reminders at their due time (date-only ones at the start of the study day), with Done / Snooze;</item>
/// <item>calendar events at their reminder time;</item>
/// <item>study blocks with auto-focus: a heads-up one minute before (Start now / Skip this time), then the session
/// starts by itself at the start time; if the PC is locked or idle, or the start was missed, it asks instead;</item>
/// <item>class assignments a day before they're due.</item>
/// </list>
/// Checks every 5 s. Anything that fell due in the last 10 minutes (e.g. while the PC slept) still goes out once;
/// older ones are skipped. Sent notifications are remembered per account so restarts don't repeat them.
/// </summary>
public sealed class ReminderScheduler
{
    private const string FiredKey = "notify.fired";
    private const string SnoozedKey = "notify.snoozed";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CatchUp = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(10);
    /// <summary>No keyboard or mouse input for this long counts as away, so a study block doesn't start on its own.</summary>
    private static readonly TimeSpan AwayAfter = TimeSpan.FromMinutes(10);

    private enum Kind { Reminder, Event, AutoFocus, AutoStart, Assignment }

    private sealed record Due(string Key, DateTime At, Kind Kind, Guid Id, AgendaItem? Item);

    private sealed record Snooze(DateTime At, Guid ReminderId);

    private readonly DataService _data;
    private readonly AgendaService _agenda;
    private readonly PreferencesService _preferences;
    private readonly NotificationService _notifications;
    private readonly FocusSessionService _focus;
    private readonly NavigationService _navigation;
    private readonly AppLifetime _lifetime;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly IClock _clock;
    private readonly ILogger<ReminderScheduler> _logger;
    private readonly Dictionary<string, DateTime> _fired = [];
    private readonly List<Snooze> _snoozed = [];
    /// <summary>Study blocks the person already started or skipped from the heads-up, so they don't start on their own.</summary>
    private readonly HashSet<string> _handled = [];
    private DispatcherQueueTimer? _timer;
    private DateTime? _lastCheck;
    private bool _open;
    private IReadOnlyDictionary<string, string>? _pending;

    public ReminderScheduler(SessionController session, DataService data, AgendaService agenda, PreferencesService preferences,
        NotificationService notifications, FocusSessionService focus, NavigationService navigation, AppLifetime lifetime,
        DialogService dialogs, ToastService toasts, IClock clock, ILogger<ReminderScheduler> logger)
    {
        _data = data;
        _agenda = agenda;
        _preferences = preferences;
        _notifications = notifications;
        _focus = focus;
        _navigation = navigation;
        _lifetime = lifetime;
        _dialogs = dialogs;
        _toasts = toasts;
        _clock = clock;
        _logger = logger;
        session.SessionStarted += async (_, _) => await StartAsync();
        session.SessionEnding += (_, _) => Stop();
        notifications.Invoked += (_, args) => Handle(args);
    }

    private async Task StartAsync()
    {
        _fired.Clear();
        _snoozed.Clear();
        _handled.Clear();
        try
        {
            var json = await _data.GetMetaAsync(FiredKey);
            if (json is not null)
                foreach (var (key, at) in JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? [])
                    _fired[key] = at;
            // Snoozes survive a restart; one that came due while Nerdy was closed goes out on the first check.
            if (await _data.GetMetaAsync(SnoozedKey) is { } snoozed)
                _snoozed.AddRange(JsonSerializer.Deserialize<List<Snooze>>(snoozed) ?? []);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring unreadable notification history");
        }

        _open = true;
        _lastCheck = null;
        _timer ??= CreateTimer();
        _timer.Start();
        await CheckAsync();
        if (_pending is { } pending)
        {
            _pending = null;
            Handle(pending);
        }
    }

    private async Task SaveSnoozesAsync()
    {
        try { await _data.SetMetaAsync(SnoozedKey, JsonSerializer.Serialize(_snoozed)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Couldn't save snoozed reminders"); }
    }

    private void Stop()
    {
        _open = false;
        _timer?.Stop();
    }

    private DispatcherQueueTimer CreateTimer()
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = Interval;
        timer.Tick += async (_, _) => await CheckAsync();
        return timer;
    }

    private async Task CheckAsync()
    {
        if (!_open) return;
        var now = _clock.LocalNow;
        var from = _lastCheck is { } last && last > now - CatchUp ? last : now - CatchUp;
        _lastCheck = now;

        var sent = false;
        foreach (var due in Collect(from, now).OrderBy(d => d.At))
        {
            if (!_fired.TryAdd(due.Key, due.At)) continue;
            Send(due);
            sent = true;
        }
        var dueSnoozes = _snoozed.Where(s => s.At <= now).ToList();
        foreach (var snooze in dueSnoozes)
        {
            _snoozed.Remove(snooze);
            if (_data.Reminder(snooze.ReminderId) is { IsDone: false } r) SendReminder(r, $"snooze:{r.Id}");
        }
        if (dueSnoozes.Count > 0) await SaveSnoozesAsync();

        if (!sent) return;
        foreach (var key in _fired.Where(f => f.Value < now.AddDays(-2)).Select(f => f.Key).ToList()) _fired.Remove(key);
        try { await _data.SetMetaAsync(FiredKey, JsonSerializer.Serialize(_fired)); }
        catch (Exception ex) { _logger.LogWarning(ex, "Couldn't save notification history"); }
    }

    /// <summary>
    /// Everything whose notification time falls in (from, to]. An event whose notification time has passed but which
    /// hasn't started yet (saved inside its reminder window, or Nerdy wasn't running) still gets it, late.
    /// </summary>
    private IEnumerable<Due> Collect(DateTime from, DateTime to)
    {
        bool InWindow(DateTime at) => at > from && at <= to;
        bool DueBefore(DateTime at, DateTime start) => InWindow(at) || (at <= to && start > to);
        var prefs = _preferences.Current;

        foreach (var r in _data.Reminders)
        {
            if (r.IsDone || r.DueAt is not { } dueAt) continue;
            var local = dueAt.LocalDateTime;
            var at = r.HasDueTime ? local : local.Date.AddHours(prefs.StudyDayStartHour);
            if (InWindow(at)) yield return new Due($"reminder:{r.Id}:{at:O}", at, Kind.Reminder, r.Id, null);
        }

        // Event reminders can be up to a day ahead, so look a little further than the window.
        foreach (var item in _agenda.Between(from, to.AddDays(1).AddHours(1)))
        {
            if (item.Source == OccurrenceSource.Assignment)
            {
                var at = item.End.AddDays(-1);
                if (InWindow(at)) yield return new Due($"assignment:{item.Id}:{item.End:O}", at, Kind.Assignment, item.Id, item);
                continue;
            }
            if (item.IsAllDay) continue;
            if (item.ReminderMinutes is { } minutes && DueBefore(item.Start.AddMinutes(-minutes), item.Start))
                yield return new Due($"event:{item.Id}:{item.Start:O}", item.Start.AddMinutes(-minutes), Kind.Event, item.Id, item);
            if (!item.AutoFocus) continue;
            if (DueBefore(item.Start.AddMinutes(-1), item.Start))
                yield return new Due(HeadsUpKey(item), item.Start.AddMinutes(-1), Kind.AutoFocus, item.Id, item);
            // A block saved just after its start (the editor's "Now") still starts on its own.
            if ((InWindow(item.Start) || (item.Start <= to && item.Start > to.AddMinutes(-1))) && item.End > to)
                yield return new Due($"autostart:{item.Id}:{item.Start:O}", item.Start, Kind.AutoStart, item.Id, item);
        }
    }

    private static string HeadsUpKey(AgendaItem block) => $"autofocus:{block.Id}:{block.Start:O}";

    private static string BlockKey(Guid id, DateTime start) => $"{id}:{start.Ticks}";

    private void Send(Due due)
    {
        switch (due.Kind)
        {
            case Kind.Reminder when _data.Reminder(due.Id) is { } r:
                SendReminder(r, due.Key);
                break;
            case Kind.Event when due.Item is { } e:
                var minutes = (int)Math.Round((e.Start - _clock.LocalNow).TotalMinutes);
                // Stays on screen until dismissed (a reminder needs at least one button for that).
                Notify(due.Key, e.Title,
                    $"{(minutes <= 0 ? "Starting now" : $"Starts in {Fmt.HoursMinutes(minutes)}")} · {Fmt.ClockRange(e.Start, e.End)}",
                    Open(NavSections.Calendar),
                    [new NotificationButton("Open calendar", Open(NavSections.Calendar)), new NotificationButton("Dismiss", Dismiss)],
                    persistent: true);
                break;
            case Kind.AutoFocus when due.Item is { } block:
                SendHeadsUp(due.Key, block);
                break;
            case Kind.AutoStart when due.Item is { } block:
                AutoStart(block);
                break;
            case Kind.Assignment when due.Item is { } a:
                Notify(due.Key, $"Due tomorrow: {a.Title}", $"{a.Subtitle?.Replace("Due · ", "")} · {Fmt.Clock(a.End)}", Open(NavSections.Classes));
                break;
        }
    }

    private void SendReminder(ReminderDto r, string key)
    {
        var topic = r.SubjectId is not null ? _agenda.LinkLabel(r.SubjectId, r.TopicId) : null;
        var when = r.HasDueTime && r.DueAt is { } d ? $"Due {Fmt.Clock(d.LocalDateTime)}" : "Due today";
        var id = r.Id.ToString();
        Notify(key, r.Title, topic is null ? when : $"{when} · {topic}", Open(NavSections.Reminders),
            [
                new NotificationButton("Mark done", new Dictionary<string, string> { ["action"] = "done", ["id"] = id }),
                new NotificationButton("Snooze 10 min", new Dictionary<string, string> { ["action"] = "snooze", ["id"] = id })
            ], persistent: true);
    }

    /// <summary>One minute before an auto-focus block: says the session is about to start, with Start now / Skip.</summary>
    private void SendHeadsUp(string key, AgendaItem block)
    {
        // Already focusing, or it has already started (then AutoStart decides what to do).
        if (_focus.IsActive || block.Start <= _clock.LocalNow) return;
        var minutes = (int)Math.Round((block.End - block.Start).TotalMinutes);
        var title = $"{block.Title} starts in 1 minute";
        var body = $"Nerdy will start a {Fmt.HoursMinutes(minutes)} {ModeName(block)} session at {Fmt.Clock(block.Start)}.";
        var skip = BlockArgs("skip", block);

        if (!_preferences.Current.Notifications || !_notifications.IsAvailable)
        {
            // Starting on its own needs a warning, so still give one inside Nerdy.
            _toasts.Show($"{title}. {body}", "Skip this time", () => Handle(skip));
            return;
        }
        _notifications.Show(key, title, body, Open(NavSections.Calendar),
            [
                new NotificationButton("Start now", BlockArgs("focus", block)),
                new NotificationButton("Skip this time", skip)
            ], persistent: true);
    }

    /// <summary>At the block's start: starts the session, unless it was skipped or nobody seems to be at the PC.</summary>
    private async void AutoStart(AgendaItem block)
    {
        _notifications.Remove(HeadsUpKey(block));
        if (!_handled.Add(BlockKey(block.Id, block.Start)) || _focus.IsActive) return;

        var missed = _clock.LocalNow - block.Start > TimeSpan.FromMinutes(1); // e.g. the PC was asleep
        if (missed || UserPresence.IsAway(AwayAfter))
        {
            OfferMissedBlock(block);
            return;
        }
        try
        {
            _logger.LogInformation("Starting the scheduled focus session for a study block");
            await StartBlockAsync(block.Id, block.Start);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't start a scheduled focus session");
        }
    }

    private void OfferMissedBlock(AgendaItem block)
    {
        var now = _clock.LocalNow;
        var minutes = (int)Math.Round((block.End - now).TotalMinutes);
        var title = $"{block.Title} started while you were away";
        var body = $"Start a {Fmt.HoursMinutes(minutes)} {ModeName(block)} session now?";
        var start = BlockArgs("focus", block);

        if (!_preferences.Current.Notifications || !_notifications.IsAvailable)
        {
            _toasts.Show($"{title}. {body}", "Start focus", () => Handle(start));
            return;
        }
        _notifications.Show($"missed:{block.Id}:{block.Start:O}", title, body, start,
            [
                new NotificationButton("Start focus", start),
                new NotificationButton("Not now", Dismiss)
            ], persistent: true);
    }

    private static string ModeName(AgendaItem block) => block.AutoFocusMode == FocusMode.Ultra ? "Ultra-Focus" : "Focus";

    private static Dictionary<string, string> BlockArgs(string action, AgendaItem block) =>
        new() { ["action"] = action, ["id"] = block.Id.ToString(), ["start"] = block.Start.Ticks.ToString() };

    private void Notify(string key, string title, string body, IReadOnlyDictionary<string, string> click,
        IReadOnlyList<NotificationButton>? buttons = null, bool persistent = false)
    {
        if (!_preferences.Current.Notifications) return;
        if (_notifications.IsAvailable) _notifications.Show(key, title, body, click, buttons, persistent);
        else _toasts.Show($"{title} · {body}");
    }

    private static Dictionary<string, string> Open(string section) => new() { ["action"] = "open", ["section"] = section };

    private static readonly Dictionary<string, string> Dismiss = new() { ["action"] = "dismiss" };

    // ----- Clicks -----

    private async void Handle(IReadOnlyDictionary<string, string> args)
    {
        if (!_open)
        {
            _pending = args; // e.g. Nerdy was started by the click; act once the account's data is open
            return;
        }
        var action = args.GetValueOrDefault("action");
        Guid.TryParse(args.GetValueOrDefault("id"), out var id);
        DateTime? blockStart = long.TryParse(args.GetValueOrDefault("start"), out var ticks) ? new DateTime(ticks) : null;
        try
        {
            switch (action)
            {
                case "open":
                    _lifetime.ShowMainWindow();
                    _navigation.Navigate(args.GetValueOrDefault("section") ?? NavSections.Home);
                    break;
                case "done" when _data.Reminder(id) is { } r:
                    _data.Save(r with { IsDone = true, CompletedAt = DateTimeOffset.UtcNow });
                    break;
                case "snooze":
                    _snoozed.Add(new Snooze(_clock.LocalNow + SnoozeFor, id));
                    await SaveSnoozesAsync();
                    break;
                case "focus":
                    if (blockStart is { } starts) _handled.Add(BlockKey(id, starts));
                    await StartBlockAsync(id, blockStart);
                    break;
                case "skip" when blockStart is { } skipped:
                    _handled.Add(BlockKey(id, skipped));
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Notification action {Action} failed", action);
        }
    }

    private async Task StartBlockAsync(Guid eventId, DateTime? start)
    {
        _lifetime.ShowMainWindow();
        await _dialogs.CloseOpenAsync(); // the session takes over the window
        if (_focus.IsActive)
        {
            _toasts.Show("A focus session is already running.");
            return;
        }
        var now = _clock.LocalNow;
        var around = start ?? now;
        var block = _agenda.Between(around.AddMinutes(-1), around.AddHours(1))
            .FirstOrDefault(i => i.Id == eventId && (start is null || i.Start == start));
        if (block is null || block.End <= now)
        {
            _toasts.Show("That study block has already ended.", ToastKind.Info);
            return;
        }
        var seconds = (int)(block.End - (block.Start > now ? block.Start : now)).TotalSeconds;
        _focus.DraftMode = block.AutoFocusMode;
        _focus.DraftTask = block.Title;
        _focus.DraftSubjectId = block.SubjectId;
        _focus.DraftTopicId = block.TopicId;
        _focus.DraftSeconds = Math.Max(5 * 60, seconds / 60 * 60);
        await _focus.StartAsync();
    }
}
