using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Stats;
using Nerdy.Core.Time;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels;

public sealed record KpiCard(Symbol Icon, string Label, string Value, string Delta, Symbol DeltaIcon, Brush DeltaBackground, Brush DeltaForeground, string Versus, bool HasDelta);
public sealed record BarSegment(double Height, Brush Fill, Microsoft.UI.Xaml.CornerRadius Corners);
public sealed record TipRow(string Name, Brush Color, string Time);
public sealed record ChartBar(IReadOnlyList<BarSegment> Segments, string Label, Brush LabelBrush, FontWeight LabelWeight, string Title, string Total,
    IReadOnlyList<TipRow> Rows, Microsoft.UI.Xaml.Thickness Gap)
{
    public bool HasData => Segments.Count > 0;
}
public sealed record GridLine(double Bottom, string Label);
public sealed record LegendItem(string Name, Brush Color);
public sealed record HeatCell(Brush Fill, string Tip);
public sealed record HeatRow(string Label, IReadOnlyList<HeatCell> Cells);
public sealed record Insight(Symbol Icon, Brush Background, Brush Foreground, string Text);
public sealed record ExitRow(string Reason, string Meta, string? Note)
{
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public string Quote => $"“{Note}”";
}
public sealed record HistoryRow(string Date, string Time, string Task, string Topic, Brush Dot, Symbol ModeIcon, string Mode, string Duration,
    string Outcome, Brush PillBackground, Brush PillForeground, string? Note, Brush RowBackground)
{
    public bool HasNote => Note is not null;
}

public sealed partial class TopicTimeRow(string name, Brush color, string time, string percent, double share, string sessions, Action open) : Controls.IShare
{
    public string Name { get; } = name;
    public Brush Color { get; } = color;
    public string Time { get; } = time;
    public string Percent { get; } = percent;
    public double Share { get; } = share;
    public string Sessions { get; } = sessions;

    [RelayCommand]
    private void Open() => open();
}

public sealed partial class StatsViewModel : ObservableObject, INavigationAware
{
    public const double ChartHeight = 200;

    private readonly AgendaService _agenda;
    private readonly DataService _data;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly PreferencesService _preferences;
    private readonly IClock _clock;
    private StatsRange _range = StatsRange.Week;
    private HistoryFilter _filter = HistoryFilter.All;
    private int _limit = 8;
    private StatsReport? _report;

    public StatsViewModel(AgendaService agenda, DataService data, ThemeService theme, NavigationService navigation, PreferencesService preferences, IClock clock)
    {
        _agenda = agenda;
        _data = data;
        _theme = theme;
        _navigation = navigation;
        _preferences = preferences;
        _clock = clock;
    }

    public ObservableCollection<ChoiceItem> Ranges { get; } = [];
    public ObservableCollection<ChoiceItem> Filters { get; } = [];
    public ObservableCollection<KpiCard> Kpis { get; } = [];
    public ObservableCollection<ChartBar> Bars { get; } = [];
    public ObservableCollection<GridLine> GridLines { get; } = [];
    public ObservableCollection<LegendItem> Legend { get; } = [];
    public ObservableCollection<TopicTimeRow> TopicRows { get; } = [];
    public ObservableCollection<HeatRow> Heat { get; } = [];
    public ObservableCollection<string> HourLabels { get; } = [];
    public ObservableCollection<Brush> HeatLegend { get; } = [];
    public ObservableCollection<Insight> Insights { get; } = [];
    public ObservableCollection<ExitRow> Exits { get; } = [];
    public ObservableCollection<HistoryRow> History { get; } = [];

    [ObservableProperty] public partial string RangeText { get; private set; } = "";
    [ObservableProperty] public partial double BarSpacing { get; private set; } = 18;
    [ObservableProperty] public partial string TopicSummary { get; private set; } = "";
    [ObservableProperty] public partial string UltraValue { get; private set; } = "";
    [ObservableProperty] public partial double UltraPercent { get; private set; }
    [ObservableProperty] public partial bool NoExits { get; private set; }
    [ObservableProperty] public partial string HistoryCount { get; private set; } = "";
    [ObservableProperty] public partial bool HistoryEmpty { get; private set; }
    [ObservableProperty] public partial bool HasMore { get; private set; }
    [ObservableProperty] public partial string MoreText { get; private set; } = "";
    [ObservableProperty] public partial bool HasData { get; private set; }

    public void OnNavigatedTo(object? parameter)
    {
        _data.Changed += OnDataChanged;
        _theme.Changed += OnThemeChanged;
        Refresh();
    }

    public void OnNavigatedFrom()
    {
        _data.Changed -= OnDataChanged;
        _theme.Changed -= OnThemeChanged;
    }

    [RelayCommand]
    private void ShowMore()
    {
        _limit += 8;
        BuildHistory();
    }

    [RelayCommand]
    private void StartFocus() => _navigation.Navigate(NavSections.Focus);

    private void OnDataChanged(object? sender, DataChangedEventArgs e)
    {
        if (e.Affects(DataKinds.Sessions | DataKinds.Topics)) Refresh();
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        Ranges.Clear();
        foreach (var (range, label) in new[] { (StatsRange.Week, "Week"), (StatsRange.Month, "Month"), (StatsRange.Term, "Term") })
            Ranges.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, range, c => { _range = (StatsRange)c.Value!; _limit = 8; Refresh(); }, selected: range == _range));
        Filters.Clear();
        foreach (var (f, label) in new[] { (HistoryFilter.All, "All"), (HistoryFilter.Focus, "Focus"), (HistoryFilter.Ultra, "Ultra-Focus"), (HistoryFilter.EndedEarly, "Ended early") })
            Filters.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, f, c => { _filter = (HistoryFilter)c.Value!; _limit = 8; Refresh(); }, selected: f == _filter));

        var prefs = _preferences.Current;
        var sessions = _agenda.SessionRecords();
        HasData = sessions.Count > 0;
        _report = FocusStats.Build(sessions, _range, _clock.Today, prefs.StudyDayStartHour, prefs.StudyDayEndHour);
        var r = _report;
        RangeText = $"{r.From:MMM d} – {r.To:MMM d, yyyy}";

        BuildKpis(r);
        BuildChart(r);
        BuildTopics(r);
        BuildHeatmap(r);
        BuildInsights(r);
        BuildUltra(r);
        BuildHistory();
    }

    private string Versus => _range switch { StatsRange.Week => "vs previous week", StatsRange.Month => "vs previous 30 days", _ => "vs previous 8 weeks" };

    private void BuildKpis(StatsReport r)
    {
        KpiCard Card(Symbol icon, string label, string value, Kpi kpi)
        {
            var d = kpi.Delta;
            var up = d is null or >= 0;
            var unit = kpi.Unit == DeltaUnit.Points ? " pts" : "%";
            return new KpiCard(icon, label, value,
                d is null ? "New" : $"{(d > 0 ? "+" : "")}{d}{unit}",
                up ? Symbol.ArrowUp : Symbol.ArrowDown,
                _theme.Brush(d is null ? "NerdySubtleFillBrush" : up ? "NerdySuccessSoftBrush" : "NerdyDangerSoftBrush"),
                _theme.Brush(d is null ? "NerdyTextSecondaryBrush" : up ? "NerdySuccessTextBrush" : "NerdyDangerTextBrush"),
                d is null ? "no earlier data" : Versus, d is not null);
        }

        Kpis.Clear();
        Kpis.Add(Card(Symbol.Timer, "Focus time", Fmt.HoursMinutes(r.FocusMinutes.Value), r.FocusMinutes));
        Kpis.Add(Card(Symbol.TargetArrow, "Sessions", r.SessionCount.Value.ToString("0"), r.SessionCount));
        Kpis.Add(Card(Symbol.Clock, "Average session", $"{r.AverageMinutes.Value:0} min", r.AverageMinutes));
        Kpis.Add(Card(Symbol.CheckmarkCircle, "Completed as planned", $"{r.CompletedAsPlanned.Value:0}%", r.CompletedAsPlanned));
    }

    private void BuildChart(StatsReport r)
    {
        BarSpacing = _range switch { StatsRange.Month => 4, StatsRange.Term => 14, _ => 18 };
        GridLines.Clear();
        for (var v = 0; v <= r.AxisMaxMinutes; v += r.AxisStepMinutes)
            GridLines.Add(new GridLine(v / (double)r.AxisMaxMinutes * ChartHeight, v == 0 ? "0" : $"{v / 60.0:0.#}h"));

        Legend.Clear();
        foreach (var t in _data.Topics) Legend.Add(new LegendItem(t.Name, ColorMix.Brush(t.ColorHex)));

        Bars.Clear();
        for (var i = 0; i < r.Bars.Count; i++)
        {
            var bar = r.Bars[i];
            var label = _range switch
            {
                StatsRange.Week => $"{bar.Start:ddd} {bar.Start.Day}",
                StatsRange.Month => (r.Bars.Count - 1 - i) % 5 == 0 ? bar.Start.Day.ToString() : "",
                _ => $"{bar.Start:MMM d}"
            };
            // Segments stack bottom-up; the top one gets the rounded corners.
            var segments = bar.Segments.Reverse().Select((s, index) => new BarSegment(
                s.Minutes / (double)r.AxisMaxMinutes * ChartHeight,
                ColorMix.Brush(_agenda.TopicColor(s.TopicId)),
                index == 0 ? new Microsoft.UI.Xaml.CornerRadius(6, 6, 0, 0) : default)).ToList();
            if (segments.Count > 0) segments[^1] = segments[^1] with { Corners = new Microsoft.UI.Xaml.CornerRadius(segments.Count == 1 ? 6 : 0, segments.Count == 1 ? 6 : 0, 3, 3) };
            Bars.Add(new ChartBar(segments, label,
                _theme.Brush(bar.IsCurrent ? "NerdyAccentBrush" : "NerdyTextSecondaryBrush"),
                bar.IsCurrent ? FontWeights.SemiBold : FontWeights.Normal,
                _range == StatsRange.Term ? $"Week of {bar.Start:MMM d}" : $"{bar.Start:ddd, MMM d}",
                $"{Fmt.HoursMinutes(bar.TotalMinutes)} focused",
                bar.Segments.Select(s => new TipRow(_agenda.TopicName(s.TopicId), ColorMix.Brush(_agenda.TopicColor(s.TopicId)), Fmt.HoursMinutes(s.Minutes))).ToList(),
                new Microsoft.UI.Xaml.Thickness(BarSpacing / 2, 0, BarSpacing / 2, 0)));
        }
    }

    private void BuildTopics(StatsReport r)
    {
        TopicRows.Clear();
        foreach (var t in r.ByTopic)
        {
            var id = t.TopicId;
            TopicRows.Add(new TopicTimeRow(_agenda.TopicName(id), ColorMix.Brush(_agenda.TopicColor(id)), Fmt.HoursMinutes(t.Minutes), $"{t.Percent}%",
                t.Percent, $"{t.Sessions} session{(t.Sessions == 1 ? "" : "s")} · avg {t.AverageMinutes} min",
                () => { if (id is { } topicId) _navigation.Navigate(NavSections.Topics, topicId); }));
        }
        var label = _range switch { StatsRange.Week => "this week", StatsRange.Month => "30 days", _ => "term" };
        TopicSummary = $"{Fmt.HoursMinutes(r.TotalMinutes)} across {r.ByTopic.Count} topic{(r.ByTopic.Count == 1 ? "" : "s")} · {label}";
    }

    private void BuildHeatmap(StatsReport r)
    {
        var hours = r.Heatmap.GetLength(1);
        Brush Shade(double v) => v <= 0 ? _theme.Brush("NerdySubtleFillBrush") : ColorMix.Brush(_theme.Tint(_theme.Accent, 14 + v * 80));
        Heat.Clear();
        for (var d = 0; d < 7; d++)
        {
            var day = WeekDays.MondayFirst[d];
            var cells = Enumerable.Range(0, hours).Select(h =>
            {
                var minutes = r.Heatmap[d, h];
                return new HeatCell(Shade(r.HeatmapMax == 0 ? 0 : minutes / (double)r.HeatmapMax),
                    $"{day}, {Fmt.HourLabel(r.HeatmapStartHour + h)} · {minutes} min");
            }).ToList();
            Heat.Add(new HeatRow(day.ToString()[..3], cells));
        }
        HourLabels.Clear();
        for (var h = 0; h < hours; h++) HourLabels.Add(h % 2 == 0 ? Fmt.HourTick(r.HeatmapStartHour + h) : "");
        HeatLegend.Clear();
        foreach (var v in new[] { 0, 0.25, 0.5, 0.75, 1 }) HeatLegend.Add(Shade(v));
    }

    private void BuildInsights(StatsReport r)
    {
        Insight Make(Symbol icon, Windows.UI.Color c, string text) =>
            new(icon, ColorMix.Brush(_theme.Tint(c, 14)), ColorMix.Brush(_theme.Ink(c)), text);

        Insights.Clear();
        if (r.TotalMinutes == 0)
        {
            Insights.Add(Make(Symbol.Lightbulb, _theme.Accent, "Finish a focus session and your patterns will show up here."));
            return;
        }
        if (r.Peak is { } peak)
            Insights.Add(Make(Symbol.Clock, _theme.Accent, $"Most of your focus starts between {Fmt.HourLabel(peak.StartHour)} and {Fmt.HourLabel(peak.StartHour + 2)}, {peak.SharePercent}% of the total."));
        if (r.Strongest is { } strong)
            Insights.Add(Make(Symbol.Calendar, ColorMix.Parse("#2F7DE1"), $"{strong.Day} is your strongest day, averaging {Fmt.HoursMinutes(strong.AverageMinutes)} of focus."));
        var endedEarly = r.Sessions.Count(s => !s.RanAsPlanned);
        Insights.Add(r.EarlyEnder is { } early
            ? Make(Symbol.Warning, ColorMix.Parse("#E8743B"), $"{_agenda.TopicName(early.TopicId)} sessions end early most often ({early.EndedEarly} of {early.Sessions}).")
            : endedEarly > 0
                // Too few sessions per topic to single one out, but don't claim everything ran to plan.
                ? Make(Symbol.Warning, ColorMix.Parse("#E8743B"), $"{endedEarly} of {r.Sessions.Count} sessions ended before their planned length.")
                : Make(Symbol.CheckmarkCircle, ColorMix.Parse("#1F9D74"), "Every session in this period ran to its planned length."));
        var streak = r.Streak;
        Insights.Add(Make(Symbol.Fire, ColorMix.Parse("#E8743B"),
            streak.Current == 0 ? $"Start a session today to begin a new streak. Your best is {streak.Best} days."
            : $"You've focused {streak.Current} day{(streak.Current == 1 ? "" : "s")} in a row.{(streak.Current >= streak.Best ? " That's your longest streak yet." : $" Your best is {streak.Best}.")}"));
    }

    private void BuildUltra(StatsReport r)
    {
        UltraValue = $"{r.Ultra.Held} of {r.Ultra.Total}";
        UltraPercent = r.Ultra.Total == 0 ? 0 : r.Ultra.Held * 100.0 / r.Ultra.Total;
        Exits.Clear();
        foreach (var x in r.Ultra.RecentExits)
            Exits.Add(new ExitRow(ReasonText(x.ExitReason), $"{x.Start:ddd, MMM d} · left after {x.ActualMinutes} of {x.PlannedMinutes} min", x.ExitNote));
        NoExits = Exits.Count == 0;
    }

    private void BuildHistory()
    {
        if (_report is null) return;
        var list = FocusStats.Filter(_report.Sessions, _filter).ToList();
        HistoryCount = $"{list.Count} session{(list.Count == 1 ? "" : "s")} · {Fmt.HoursMinutes(list.Sum(s => s.ActualMinutes))}";
        HistoryEmpty = list.Count == 0;
        var today = _clock.Today;
        History.Clear();
        foreach (var s in list.Take(_limit))
        {
            var (outcome, bg, fg) = s.Outcome switch
            {
                FocusOutcome.Completed => ("Completed", "NerdySuccessSoftBrush", "NerdySuccessTextBrush"),
                FocusOutcome.EmergencyExit => ("Emergency exit", "NerdyDangerSoftBrush", "NerdyDangerTextBrush"),
                FocusOutcome.Interrupted => ("Interrupted", "NerdySubtleFillBrush", "NerdyTextSecondaryBrush"),
                _ => ("Ended early", "NerdyWarningSoftBrush", "NerdyWarningTextBrush")
            };
            var day = DateOnly.FromDateTime(s.Start);
            History.Add(new HistoryRow(
                day == today ? "Today" : day == today.AddDays(-1) ? "Yesterday" : $"{day:ddd, MMM d}",
                Fmt.Clock(s.Start), s.Task.Length == 0 ? "Focus session" : s.Task,
                _agenda.TopicName(s.TopicId), ColorMix.Brush(_agenda.TopicColor(s.TopicId)),
                s.Mode == FocusMode.Ultra ? Symbol.ShieldLock : Symbol.TargetArrow,
                s.Mode == FocusMode.Ultra ? "Ultra-Focus" : "Focus",
                s.RanAsPlanned ? $"{s.ActualMinutes} min" : $"{s.ActualMinutes} / {s.PlannedMinutes} min",
                outcome, _theme.Brush(bg), _theme.Brush(fg),
                s.Outcome == FocusOutcome.EmergencyExit ? $"{ReasonText(s.ExitReason)}{(string.IsNullOrEmpty(s.ExitNote) ? "" : $" · “{s.ExitNote}”")}" : null,
                _theme.Brush("NerdyTransparentBrush")));
        }
        HasMore = list.Count > _limit;
        MoreText = $"Show {Math.Min(8, list.Count - _limit)} more";
    }

    private static string ReasonText(ExitReason? reason) => reason switch
    {
        ExitReason.Medical => "Medical or safety emergency",
        ExitReason.Family => "Family emergency",
        ExitReason.Urgent => "Urgent school or work matter",
        ExitReason.Other => "Something else",
        _ => "Emergency exit"
    };
}
