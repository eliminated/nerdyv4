using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Focus;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;
using Nerdy.Core.Time;
using Nerdy.Focus;

namespace Nerdy.App.ViewModels;

/// <summary>Icons for well-known apps (the dock and the allowed-apps list).</summary>
public static class AppIcons
{
    public static Symbol For(string exeName) => exeName.ToLowerInvariant() switch
    {
        "calculatorapp.exe" or "calc.exe" => Symbol.Calculator,
        "msedge.exe" or "chrome.exe" or "firefox.exe" or "brave.exe" or "opera.exe" => Symbol.Globe,
        "spotify.exe" => Symbol.MusicNote2,
        "onenote.exe" => Symbol.Book,
        "discord.exe" or "slack.exe" or "teams.exe" or "ms-teams.exe" => Symbol.Chat,
        "winword.exe" => Symbol.Document,
        "excel.exe" => Symbol.Table,
        "powerpnt.exe" => Symbol.SlideLayout,
        "code.exe" or "devenv.exe" => Symbol.Code,
        "explorer.exe" => Symbol.Folder,
        "notepad.exe" => Symbol.Notepad,
        "acrobat.exe" or "acrord32.exe" => Symbol.DocumentPdf,
        _ => Symbol.Apps
    };
}

public sealed partial class AllowedAppRowViewModel(AllowedApp app, bool locked, Action<AllowedAppRowViewModel> changed, Action<AllowedAppRowViewModel> remove) : ObservableObject
{
    public AllowedApp App { get; } = app;
    public string Name { get; } = app.Name;
    public string Description { get; } = locked ? "Always available" : app.Description ?? app.ExeName;
    public Symbol Icon { get; } = locked ? Symbol.Notepad : AppIcons.For(app.ExeName);
    public bool IsLocked { get; } = locked;
    public bool CanRemove => !IsLocked;
    public double LockedOpacity => IsLocked ? 0.5 : 1;

    [ObservableProperty] public partial bool Enabled { get; set; } = locked || app.Enabled;

    partial void OnEnabledChanged(bool value) => changed(this);

    [RelayCommand]
    private void Remove() => remove(this);
}

public sealed record AddableApp(string Name, string ExeName, string? Path, string Detail);

public sealed partial class WebsiteChip(string site, Action<string> remove)
{
    public string Site { get; } = site;

    [RelayCommand]
    private void Remove() => remove(Site);
}

public sealed partial class FocusSetupViewModel : ObservableObject, INavigationAware
{
    private readonly FocusSessionService _focus;
    private readonly PreferencesService _preferences;
    private readonly DataService _data;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly IClock _clock;
    private bool _custom;

    public FocusSetupViewModel(FocusSessionService focus, PreferencesService preferences, DataService data, ThemeService theme,
        NavigationService navigation, IClock clock)
    {
        _focus = focus;
        _preferences = preferences;
        _data = data;
        _theme = theme;
        _navigation = navigation;
        _clock = clock;
    }

    public ObservableCollection<ChoiceItem> ModeCards { get; } = [];
    public ObservableCollection<ChoiceItem> DurationChips { get; } = [];
    public ObservableCollection<ChoiceItem> TopicChips { get; } = [];
    public ObservableCollection<AllowedAppRowViewModel> Apps { get; } = [];
    public ObservableCollection<WebsiteChip> Websites { get; } = [];
    public ObservableCollection<AddableApp> AddableApps { get; } = [];

    [ObservableProperty] public partial string TaskText { get; set; } = "";
    [ObservableProperty] public partial string DurationText { get; private set; } = "";
    [ObservableProperty] public partial string DurationUnit { get; private set; } = "";
    [ObservableProperty] public partial bool IsCustom { get; private set; }
    [ObservableProperty] public partial double SliderMinutes { get; set; }
    [ObservableProperty] public partial double CustomHours { get; set; }
    [ObservableProperty] public partial double CustomMinutes { get; set; }
    [ObservableProperty] public partial double CustomSeconds { get; set; }
    [ObservableProperty] public partial string Summary { get; private set; } = "";
    [ObservableProperty] public partial string Rule { get; private set; } = "";
    [ObservableProperty] public partial string StartLabel { get; private set; } = "";
    [ObservableProperty] public partial Symbol StartIcon { get; private set; }
    [ObservableProperty] public partial string BlockingText { get; private set; } = "";
    [ObservableProperty] public partial string NewWebsite { get; set; } = "";

    public void OnNavigatedTo(object? parameter)
    {
        TaskText = _focus.DraftTask;
        _theme.Changed += OnThemeChanged;
        _preferences.Changed += OnPreferencesChanged;
        Rebuild();
    }

    public void OnNavigatedFrom()
    {
        _theme.Changed -= OnThemeChanged;
        _preferences.Changed -= OnPreferencesChanged;
    }

    partial void OnTaskTextChanged(string value) => _focus.DraftTask = value;

    partial void OnSliderMinutesChanged(double value)
    {
        if (!_custom || double.IsNaN(value)) return;
        SetSeconds((int)value * 60, fromSlider: true);
    }

    partial void OnCustomHoursChanged(double value) => ApplyFields();
    partial void OnCustomMinutesChanged(double value) => ApplyFields();
    partial void OnCustomSecondsChanged(double value) => ApplyFields();

    [RelayCommand]
    private async Task StartAsync() => await _focus.StartAsync();

    [RelayCommand]
    private void OpenSettings() => _navigation.Navigate(NavSections.Settings, "focus");

    [RelayCommand]
    private void AddWebsite()
    {
        var site = NormalizeSite(NewWebsite);
        NewWebsite = "";
        if (site.Length == 0) return;
        _preferences.Update(p => { if (!p.AllowedWebsites.Contains(site, StringComparer.OrdinalIgnoreCase)) p.AllowedWebsites.Add(site); });
    }

    /// <summary>Loads running and installed apps for the "Add app" picker (read-only queries).</summary>
    public async Task LoadAddableAppsAsync()
    {
        var apps = await FindAddableAppsAsync(_preferences.Current);
        AddableApps.Clear();
        foreach (var app in apps) AddableApps.Add(app);
    }

    public void AddApp(AddableApp app) => AddAllowedApp(_preferences, app);

    /// <summary>Running apps first, then installed ones, skipping anything already on the allowed list (shared with Settings).</summary>
    internal static async Task<List<AddableApp>> FindAddableAppsAsync(Preferences prefs)
    {
        var allowed = prefs.AllowedApps.Select(a => a.ExeName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var (running, installed) = await Task.Run(() => (AppCatalog.GetRunningApps(), AppCatalog.GetInstalledApps()));
        var list = new List<AddableApp>();
        foreach (var r in running.Where(r => !allowed.Contains(r.ExeName)))
            list.Add(new AddableApp(r.DisplayName, r.ExeName, r.ExePath, "Open now"));
        foreach (var i in installed.Where(i => !allowed.Contains(i.ExeName) && running.All(r => r.ExeName != i.ExeName)))
            list.Add(new AddableApp(i.Name, i.ExeName, i.TargetPath, "Installed"));
        return list;
    }

    internal static void AddAllowedApp(PreferencesService preferences, AddableApp app) =>
        preferences.Update(p =>
        {
            if (p.AllowedApps.Any(a => a.ExeName.Equals(app.ExeName, StringComparison.OrdinalIgnoreCase))) return;
            p.AllowedApps.Add(new AllowedApp { ExeName = app.ExeName, Name = app.Name, Path = app.Path, Description = app.Detail == "Installed" ? null : app.ExeName });
        });

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();
    private void OnPreferencesChanged(object? sender, Preferences e) => RebuildAllowed();

    private void Rebuild()
    {
        var mode = _focus.DraftMode;
        ModeCards.Clear();
        ModeCards.Add(new ChoiceItem(_theme, ChoiceLook.Card, "Focus", FocusMode.Focus, PickMode, Symbol.TargetArrow,
            "Hides distracting apps. Pause or end whenever you need to.", selected: mode == FocusMode.Focus));
        ModeCards.Add(new ChoiceItem(_theme, ChoiceLook.Card, "Ultra-Focus", FocusMode.Ultra, PickMode, Symbol.ShieldLock,
            "Full-screen lock. No pausing or ending until the timer runs out. Emergency exit only.", selected: mode == FocusMode.Ultra));

        var seconds = _focus.DraftSeconds;
        _custom = _custom || seconds % 60 != 0 || !new[] { 15, 25, 45, 60 }.Contains(seconds / 60);
        DurationChips.Clear();
        foreach (var m in new[] { 15, 25, 45, 60 })
            DurationChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, $"{m}m", m, PickDuration, selected: !_custom && seconds == m * 60));
        DurationChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, "Custom", null, PickDuration, Symbol.Timer, selected: _custom));

        TopicChips.Clear();
        foreach (var t in _data.Topics)
            TopicChips.Add(new ChoiceItem(_theme, ChoiceLook.ColorChip, t.Name, t.Id, PickTopic, colorHex: t.ColorHex, selected: t.Id == _focus.DraftTopicId));

        RebuildAllowed();
        SetSeconds(seconds, fromSlider: false);
    }

    private void RebuildAllowed()
    {
        var prefs = _preferences.Current;
        Apps.Clear();
        Apps.Add(new AllowedAppRowViewModel(new AllowedApp { ExeName = "nerdy.exe", Name = "Nerdy Notes" }, true, _ => { }, _ => { }));
        foreach (var app in prefs.AllowedApps)
            Apps.Add(new AllowedAppRowViewModel(app, false, OnAppToggled, OnAppRemoved));
        Websites.Clear();
        foreach (var site in prefs.AllowedWebsites)
            Websites.Add(new WebsiteChip(site, s => _preferences.Update(p => p.AllowedWebsites.RemoveAll(x => x.Equals(s, StringComparison.OrdinalIgnoreCase)))));
        BlockingText = prefs.BlockedApps == BlockedAppMode.Close
            ? "Everything else is closed when the session starts (you'll see the list first)."
            : "Everything else is minimised until the session ends.";
        UpdateSummary();
    }

    private void OnAppToggled(AllowedAppRowViewModel row) =>
        _preferences.Update(p => { if (p.AllowedApps.FirstOrDefault(a => a.ExeName == row.App.ExeName) is { } a) a.Enabled = row.Enabled; });

    private void OnAppRemoved(AllowedAppRowViewModel row) =>
        _preferences.Update(p => p.AllowedApps.RemoveAll(a => a.ExeName == row.App.ExeName));

    private void PickMode(ChoiceItem item)
    {
        var mode = (FocusMode)item.Value!;
        _focus.DraftMode = mode;
        foreach (var c in ModeCards) c.IsSelected = c == item;
        // Ultra-Focus is for longer stretches (as in the prototype): bump short presets to an hour.
        if (mode == FocusMode.Ultra && !_custom && _focus.DraftSeconds < 45 * 60) SetDuration(60);
        UpdateSummary();
    }

    private void PickDuration(ChoiceItem item)
    {
        foreach (var c in DurationChips) c.IsSelected = c == item;
        if (item.Value is int minutes)
        {
            _custom = false;
            SetDuration(minutes);
        }
        else
        {
            _custom = true;
            SetSeconds(_focus.DraftSeconds == 60 * 60 ? 75 * 60 : _focus.DraftSeconds, fromSlider: false);
        }
    }

    private void SetDuration(int minutes)
    {
        foreach (var c in DurationChips) c.IsSelected = !_custom && c.Value is int m && m == minutes;
        SetSeconds(minutes * 60, fromSlider: false);
    }

    private bool _settingFields;

    private void SetSeconds(int seconds, bool fromSlider)
    {
        seconds = Math.Clamp(seconds, 60, 12 * 3600 + 3599);
        _focus.DraftSeconds = seconds;
        IsCustom = _custom;
        var big = seconds % 60 != 0 || seconds >= 3600;
        DurationText = big ? Fmt.Timer(seconds) : (seconds / 60).ToString();
        DurationUnit = big ? (seconds >= 3600 ? "h : m : s" : "m : s") : "min";

        _settingFields = true;
        CustomHours = seconds / 3600;
        CustomMinutes = seconds % 3600 / 60;
        CustomSeconds = seconds % 60;
        if (!fromSlider) SliderMinutes = Math.Clamp(Math.Round(seconds / 60.0), 1, 240);
        _settingFields = false;
        UpdateSummary();
    }

    private void ApplyFields()
    {
        if (_settingFields || !_custom) return;
        double V(double x) => double.IsNaN(x) ? 0 : x;
        SetSeconds((int)(V(CustomHours) * 3600 + V(CustomMinutes) * 60 + V(CustomSeconds)), fromSlider: false);
    }

    private void PickTopic(ChoiceItem item)
    {
        var id = (Guid)item.Value!;
        _focus.DraftTopicId = _focus.DraftTopicId == id ? null : id;
        foreach (var c in TopicChips) c.IsSelected = (Guid)c.Value! == _focus.DraftTopicId;
    }

    private void UpdateSummary()
    {
        var seconds = _focus.DraftSeconds;
        var ultra = _focus.DraftMode == FocusMode.Ultra;
        var allowed = _preferences.Current.AllowedApps.Count(a => a.Enabled);
        Summary = $"{(ultra ? "Ultra-Focus" : "Focus")} · {Fmt.Duration(seconds)} · ends at {Fmt.Clock(_clock.LocalNow.AddSeconds(seconds))}";
        Rule = ultra
            ? "Once started, you can't pause or end early. Emergency exit requires a reason."
            : $"{allowed} app{(allowed == 1 ? "" : "s")} allowed · you can pause or end anytime";
        StartIcon = ultra ? Symbol.LockClosed : Symbol.Play;
        StartLabel = ultra ? $"Lock in for {Fmt.Duration(seconds)}" : $"Start {Fmt.Duration(seconds)} focus";
    }

    internal static string NormalizeSite(string input)
    {
        var s = input.Trim().ToLowerInvariant();
        if (s.Contains("://")) s = s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..];
        s = s.Split('/', '?', '#')[0];
        if (s.StartsWith("www.")) s = s[4..];
        return s.Contains('.') ? s : "";
    }
}
