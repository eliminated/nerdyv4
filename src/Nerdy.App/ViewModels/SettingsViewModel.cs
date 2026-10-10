using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Sync;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Formatting;

namespace Nerdy.App.ViewModels;

public sealed partial class NavOrderRow(NavSection section, bool canMoveUp, bool canMoveDown, Action<string, int> move)
{
    public string Label { get; } = section.Label;
    public Symbol Icon { get; } = section.Icon;
    public bool CanMoveUp { get; } = canMoveUp;
    public bool CanMoveDown { get; } = canMoveDown;
    public double UpOpacity => CanMoveUp ? 1 : 0.3;
    public double DownOpacity => CanMoveDown ? 1 : 0.3;
    public string UpName => $"Move {Label} up";
    public string DownName => $"Move {Label} down";

    [RelayCommand]
    private void MoveUp() { if (CanMoveUp) move(section.Id, -1); }

    [RelayCommand]
    private void MoveDown() { if (CanMoveDown) move(section.Id, 1); }
}

public sealed partial class WidgetToggle : ObservableObject
{
    private readonly Action<string, bool> _changed;

    public WidgetToggle(HomeWidget widget, bool isOn, Action<string, bool> changed)
    {
        Id = widget.Id;
        Label = widget.Label;
        IsOn = isOn;
        _changed = changed; // set last so the initial value isn't saved back
    }

    public string Id { get; }
    public string Label { get; }

    [ObservableProperty] public partial bool IsOn { get; set; }

    partial void OnIsOnChanged(bool value) => _changed?.Invoke(Id, value);
}

public sealed partial class FontOption(FontChoice font, bool available, Brush border, Brush background, Action<string> pick)
{
    public string Label { get; } = font.Label;
    public FontFamily Family { get; } = new(font.Family);
    public bool IsAvailable { get; } = available;
    public string Detail => IsAvailable ? "" : "Not installed yet";
    public double ItemOpacity => IsAvailable ? 1 : 0.5;
    public Brush Border { get; } = border;
    public Brush Background { get; } = background;

    [RelayCommand]
    private void Pick() { if (IsAvailable) pick(font.Id); }
}

public sealed partial class OrbitOption(OrbitPalette palette, Brush swatch, Brush border, Brush background, Action<string> pick)
{
    public string Label { get; } = palette.Label;
    public Brush Swatch { get; } = swatch;
    public Brush Border { get; } = border;
    public Brush Background { get; } = background;

    [RelayCommand]
    private void Pick() => pick(palette.Id);
}

public sealed record SignInMethod(Symbol Icon, string Label, string Detail);

/// <summary>Settings: every change is saved (and synced) as soon as it's made, like Windows Settings.</summary>
public sealed partial class SettingsViewModel : ObservableObject, INavigationAware
{
    public const string PersonalizationTab = "personalization", FocusTab = "focus", PlanningTab = "planning",
        NotificationsTab = "notifications", AccountTab = "account";

    private static readonly (string Id, string Label)[] TabList =
    [
        (PersonalizationTab, "Personalization"), (FocusTab, "Focus"), (PlanningTab, "Planning"),
        (NotificationsTab, "Notifications"), (AccountTab, "Account")
    ];

    private static readonly (UserRole Role, string Label)[] RoleList =
        [(UserRole.Student, "Student"), (UserRole.Teacher, "Teacher"), (UserRole.Researcher, "Researcher"), (UserRole.Learner, "Lifelong learner")];

    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly NavigationService _navigation;
    private readonly AuthService _auth;
    private readonly ApiClient _api;
    private readonly SyncService _sync;
    private readonly DialogService _dialogs;
    private readonly ToastService _toasts;
    private readonly ILogger<SettingsViewModel> _logger;
    private bool _loading;

    public SettingsViewModel(PreferencesService preferences, ThemeService theme, NavigationService navigation, AuthService auth, ApiClient api,
        SyncService sync, DialogService dialogs, ToastService toasts, ILogger<SettingsViewModel> logger)
    {
        _preferences = preferences;
        _theme = theme;
        _navigation = navigation;
        _auth = auth;
        _api = api;
        _sync = sync;
        _dialogs = dialogs;
        _toasts = toasts;
        _logger = logger;
    }

    /// <summary>Asks the page to bring a named card into view (e.g. "widgets" from Home's Customize).</summary>
    public event EventHandler<string>? ScrollRequested;

    public ObservableCollection<ChoiceItem> Tabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TabTitle), nameof(IsPersonalization), nameof(IsFocus), nameof(IsPlanning), nameof(IsNotifications), nameof(IsAccount))]
    public partial string Tab { get; private set; } = PersonalizationTab;

    public string TabTitle => TabList.First(t => t.Id == Tab).Label;
    public bool IsPersonalization => Tab == PersonalizationTab;
    public bool IsFocus => Tab == FocusTab;
    public bool IsPlanning => Tab == PlanningTab;
    public bool IsNotifications => Tab == NotificationsTab;
    public bool IsAccount => Tab == AccountTab;

    // ----- Personalization -----
    public ObservableCollection<ChoiceItem> ThemeSegments { get; } = [];
    public ObservableCollection<ChoiceItem> LayoutSegments { get; } = [];
    public ObservableCollection<SwatchItem> Swatches { get; } = [];
    public ObservableCollection<ChoiceItem> IndicatorSegments { get; } = [];
    public ObservableCollection<OrbitOption> OrbitOptions { get; } = [];
    public ObservableCollection<FontOption> FontOptions { get; } = [];
    public ObservableCollection<ChoiceItem> DensitySegments { get; } = [];
    public ObservableCollection<WallpaperItem> WallpaperOptions { get; } = [];
    public ObservableCollection<NavOrderRow> NavRows { get; } = [];
    public ObservableCollection<WidgetToggle> WidgetRows { get; } = [];
    [ObservableProperty] public partial bool ShowOrbitOptions { get; private set; }

    // ----- Focus -----
    public ObservableCollection<ChoiceItem> BlockedAppCards { get; } = [];
    public ObservableCollection<ChoiceItem> LengthChips { get; } = [];
    public ObservableCollection<AllowedAppRowViewModel> Apps { get; } = [];
    public ObservableCollection<AddableApp> AddableApps { get; } = [];
    public ObservableCollection<WebsiteChip> Websites { get; } = [];
    [ObservableProperty] public partial bool AiExitReview { get; set; }
    [ObservableProperty] public partial string NewWebsite { get; set; } = "";

    // ----- Planning -----
    public IReadOnlyList<Option<int>> StartHourOptions { get; } = Enumerable.Range(5, 8).Select(h => new Option<int>(Fmt.HourLabel(h), h)).ToList();
    public IReadOnlyList<Option<int>> EndHourOptions { get; } = Enumerable.Range(14, 10).Select(h => new Option<int>(Fmt.HourLabel(h), h)).ToList();
    public IReadOnlyList<Option<int>> ReminderOptions { get; } =
        [new("At start time", 0), new("5 minutes before", 5), new("10 minutes before", 10), new("15 minutes before", 15), new("30 minutes before", 30), new("1 hour before", 60)];
    [ObservableProperty] public partial Option<int>? StudyStart { get; set; }
    [ObservableProperty] public partial Option<int>? StudyEnd { get; set; }
    [ObservableProperty] public partial Option<int>? DefaultReminder { get; set; }

    // ----- Notifications -----
    [ObservableProperty] public partial bool Notifications { get; set; }
    [ObservableProperty] public partial bool KeepRunningInTray { get; set; }

    // ----- Account -----
    public ObservableCollection<ChoiceItem> RoleSegments { get; } = [];
    public ObservableCollection<SignInMethod> SignInMethods { get; } = [];
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSaveName))]
    public partial string DisplayName { get; set; } = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(CanSaveName))]
    public partial bool IsBusy { get; private set; }
    [ObservableProperty] public partial string SyncText { get; private set; } = "";

    public string Initials => UserInitials.From(_auth.User?.DisplayName);
    public string Email => _auth.User?.Email ?? "";
    public bool EmailUnconfirmed => _auth.User is { EmailConfirmed: false };
    public bool CanSaveName => !IsBusy && DisplayName.Trim().Length > 0 && DisplayName.Trim() != (_auth.User?.DisplayName ?? "");
    public string MemberSince => _auth.User is { } u ? $"Member since {u.CreatedAt.ToLocalTime():MMMM yyyy}" : "";

    public void OnNavigatedTo(object? parameter)
    {
        _theme.Changed += OnThemeChanged;
        _preferences.Changed += OnPreferencesChanged;
        _auth.PropertyChanged += OnAuthChanged;
        _sync.PropertyChanged += OnSyncChanged;
        var target = parameter as string;
        Tab = target switch
        {
            AccountTab => AccountTab,
            FocusTab => FocusTab,
            PlanningTab => PlanningTab,
            NotificationsTab => NotificationsTab,
            _ => PersonalizationTab
        };
        Rebuild();
        LoadAccount();
        UpdateSyncText();
        if (target == "home") ScrollRequested?.Invoke(this, "widgets");
    }

    public void OnNavigatedFrom()
    {
        _theme.Changed -= OnThemeChanged;
        _preferences.Changed -= OnPreferencesChanged;
        _auth.PropertyChanged -= OnAuthChanged;
        _sync.PropertyChanged -= OnSyncChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();
    private void OnPreferencesChanged(object? sender, Preferences e) => Rebuild();

    private void OnAuthChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AuthService.User)) LoadAccount();
    }

    private void OnSyncChanged(object? sender, PropertyChangedEventArgs e) => UpdateSyncText();

    private void Rebuild()
    {
        _loading = true;
        try
        {
            var p = _preferences.Current;
            Tabs.Clear();
            foreach (var (id, label) in TabList)
                Tabs.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, id, c => { Tab = (string)c.Value!; Rebuild(); }, selected: id == Tab));

            RebuildPersonalization(p);
            RebuildFocus(p);

            StudyStart = StartHourOptions.FirstOrDefault(o => o.Value == p.StudyDayStartHour) ?? StartHourOptions[3];
            StudyEnd = EndHourOptions.FirstOrDefault(o => o.Value == p.StudyDayEndHour) ?? EndHourOptions[6];
            DefaultReminder = ReminderOptions.FirstOrDefault(o => o.Value == p.DefaultReminderMinutes) ?? ReminderOptions[2];
            Notifications = p.Notifications;
            KeepRunningInTray = p.KeepRunningInTray;

            RoleSegments.Clear();
            foreach (var (role, label) in RoleList)
                RoleSegments.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, role, PickRole, selected: _auth.User?.Role == role));
        }
        finally
        {
            _loading = false;
        }
    }

    private void RebuildPersonalization(Preferences p)
    {
        Segments(ThemeSegments, [("System", AppThemeChoice.System), ("Light", AppThemeChoice.Light), ("Dark", AppThemeChoice.Dark)], p.Theme,
            v => _preferences.Update(x => x.Theme = v));
        Segments(LayoutSegments, [("Dashboard", HomeLayout.Dashboard), ("Agenda", HomeLayout.Agenda)], p.HomeLayout,
            v => _preferences.Update(x => x.HomeLayout = v));
        Segments(IndicatorSegments, [("Glide", NavIndicator.Glide), ("Aura", NavIndicator.Aura), ("Horizon", NavIndicator.Horizon)], p.NavIndicator,
            v => _preferences.Update(x => x.NavIndicator = v));
        Segments(DensitySegments, [("Comfortable", Density.Comfortable), ("Compact", Density.Compact)], p.Density,
            v => _preferences.Update(x => x.Density = v));

        Swatches.Clear();
        foreach (var hex in Accents.All)
        {
            var on = hex.Equals(p.Accent, StringComparison.OrdinalIgnoreCase);
            Swatches.Add(new SwatchItem(hex, ColorMix.Brush(hex), on ? ColorMix.Brush(hex) : _theme.Brush("NerdyTransparentBrush"),
                h => _preferences.Update(x => x.Accent = h)));
        }

        ShowOrbitOptions = p.NavIndicator != NavIndicator.Glide;
        OrbitOptions.Clear();
        foreach (var o in Orbits.All)
        {
            var on = o.Id == p.Orbit;
            OrbitOptions.Add(new OrbitOption(o, OrbitSwatch(o.Colors),
                _theme.Brush(on ? "NerdyAccentBrush" : "NerdyChipBorderBrush"),
                _theme.Brush(on ? "NerdyAccentSoftBrush" : "NerdyTransparentBrush"),
                id => _preferences.Update(x => x.Orbit = id)));
        }

        FontOptions.Clear();
        foreach (var f in Fonts.All)
        {
            var on = f.Id == p.Font;
            FontOptions.Add(new FontOption(f, IsFontAvailable(f),
                _theme.Brush(on ? "NerdyAccentBrush" : "NerdyChipBorderBrush"),
                _theme.Brush(on ? "NerdyAccentSoftBrush" : "NerdyCardBackgroundBrush"),
                id => _preferences.Update(x => x.Font = id)));
        }

        WallpaperOptions.Clear();
        foreach (var (id, label) in Wallpapers.All)
            WallpaperOptions.Add(new WallpaperItem(id, label, _theme.Brush(id == p.Wallpaper ? "NerdyAccentBrush" : "NerdyCardBorderBrush"),
                w => _preferences.Update(x => x.Wallpaper = w)));

        NavRows.Clear();
        for (var i = 0; i < p.NavOrder.Count; i++)
            if (NavSections.All.TryGetValue(p.NavOrder[i], out var section))
                NavRows.Add(new NavOrderRow(section, i > 0, i < p.NavOrder.Count - 1, MoveSection));

        WidgetRows.Clear();
        foreach (var w in HomeWidgets.All)
            WidgetRows.Add(new WidgetToggle(w, p.Widgets.GetValueOrDefault(w.Id, true),
                (id, on) => _preferences.Update(x => x.Widgets[id] = on)));
    }

    private void RebuildFocus(Preferences p)
    {
        BlockedAppCards.Clear();
        BlockedAppCards.Add(new ChoiceItem(_theme, ChoiceLook.Card, "Minimize them", BlockedAppMode.Hide, PickBlockedMode, Symbol.ArrowMinimize,
            "Other apps are minimized when they open and come back when the session ends.", selected: p.BlockedApps == BlockedAppMode.Hide));
        BlockedAppCards.Add(new ChoiceItem(_theme, ChoiceLook.Card, "Close them", BlockedAppMode.Close, PickBlockedMode, Symbol.BatterySaver,
            "Saves power. Apps that aren't allowed are closed when a session starts; you see the list first and can cancel.",
            selected: p.BlockedApps == BlockedAppMode.Close));

        LengthChips.Clear();
        foreach (var m in new[] { 15, 25, 45, 60, 90 })
            LengthChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, $"{m} min", m, PickLength, selected: p.DefaultFocusMinutes == m));

        AiExitReview = p.AiExitReview;

        Apps.Clear();
        foreach (var app in p.AllowedApps)
            Apps.Add(new AllowedAppRowViewModel(app, false,
                row => _preferences.Update(x => { if (x.AllowedApps.FirstOrDefault(a => a.ExeName == row.App.ExeName) is { } a) a.Enabled = row.Enabled; }),
                row => _preferences.Update(x => x.AllowedApps.RemoveAll(a => a.ExeName == row.App.ExeName))));

        Websites.Clear();
        foreach (var site in p.AllowedWebsites)
            Websites.Add(new WebsiteChip(site, s => _preferences.Update(x => x.AllowedWebsites.RemoveAll(w => w.Equals(s, StringComparison.OrdinalIgnoreCase)))));
    }

    private void Segments<T>(ObservableCollection<ChoiceItem> target, (string Label, T Value)[] options, T current, Action<T> pick) where T : struct, Enum
    {
        target.Clear();
        foreach (var (label, value) in options)
            target.Add(new ChoiceItem(_theme, ChoiceLook.Segment, label, value, c => pick((T)c.Value!), selected: value.Equals(current)));
    }

    private Brush OrbitSwatch(string? colors)
    {
        var stops = (colors ?? $"{ColorMix.ToHex(_theme.Accent)},{ColorMix.ToHex(_theme.Tint(_theme.Accent, 55))}").Split(',');
        var brush = new LinearGradientBrush { StartPoint = new(0, 0), EndPoint = new(1, 1) };
        for (var i = 0; i < stops.Length; i++)
            brush.GradientStops.Add(new GradientStop { Color = ColorMix.Parse(stops[i]), Offset = stops.Length == 1 ? 0 : i / (double)(stops.Length - 1) });
        return brush;
    }

    /// <summary>Bundled fonts ship as files next to the app; Segoe UI Variable is part of Windows.</summary>
    private static bool IsFontAvailable(FontChoice font)
    {
        const string scheme = "ms-appx:///";
        if (!font.Family.StartsWith(scheme, StringComparison.Ordinal)) return true;
        var relative = font.Family[scheme.Length..].Split('#')[0];
        return File.Exists(Path.Combine(AppContext.BaseDirectory, relative.Replace('/', Path.DirectorySeparatorChar)));
    }

    private void MoveSection(string id, int delta) =>
        _preferences.Update(p =>
        {
            var i = p.NavOrder.IndexOf(id);
            var j = i + delta;
            if (i < 0 || j < 0 || j >= p.NavOrder.Count) return;
            (p.NavOrder[i], p.NavOrder[j]) = (p.NavOrder[j], p.NavOrder[i]);
        });

    [RelayCommand]
    private void ReplayTour() => _navigation.ShowRoot(RootView.Onboarding);

    // ----- Focus -----

    private void PickBlockedMode(ChoiceItem item) => _preferences.Update(p => p.BlockedApps = (BlockedAppMode)item.Value!);

    private void PickLength(ChoiceItem item) => _preferences.Update(p => p.DefaultFocusMinutes = (int)item.Value!);

    partial void OnAiExitReviewChanged(bool value)
    {
        if (!_loading) _preferences.Update(p => p.AiExitReview = value);
    }

    public async Task LoadAddableAppsAsync()
    {
        var apps = await FocusSetupViewModel.FindAddableAppsAsync(_preferences.Current);
        AddableApps.Clear();
        foreach (var app in apps) AddableApps.Add(app);
    }

    public void AddApp(AddableApp app) => FocusSetupViewModel.AddAllowedApp(_preferences, app);

    [RelayCommand]
    private void AddWebsite()
    {
        var site = FocusSetupViewModel.NormalizeSite(NewWebsite);
        NewWebsite = "";
        if (site.Length == 0) return;
        _preferences.Update(p => { if (!p.AllowedWebsites.Contains(site, StringComparer.OrdinalIgnoreCase)) p.AllowedWebsites.Add(site); });
    }

    // ----- Planning and notifications -----

    partial void OnStudyStartChanged(Option<int>? value)
    {
        if (_loading || value is null) return;
        if (value.Value >= _preferences.Current.StudyDayEndHour)
        {
            _toasts.Show("The study day has to start before it ends.", ToastKind.Warning);
            Rebuild();
            return;
        }
        _preferences.Update(p => p.StudyDayStartHour = value.Value);
    }

    partial void OnStudyEndChanged(Option<int>? value)
    {
        if (_loading || value is null) return;
        if (value.Value <= _preferences.Current.StudyDayStartHour)
        {
            _toasts.Show("The study day has to end after it starts.", ToastKind.Warning);
            Rebuild();
            return;
        }
        _preferences.Update(p => p.StudyDayEndHour = value.Value);
    }

    partial void OnDefaultReminderChanged(Option<int>? value)
    {
        if (!_loading && value is not null) _preferences.Update(p => p.DefaultReminderMinutes = value.Value);
    }

    partial void OnNotificationsChanged(bool value)
    {
        if (!_loading) _preferences.Update(p => p.Notifications = value);
    }

    partial void OnKeepRunningInTrayChanged(bool value)
    {
        if (!_loading) _preferences.Update(p => p.KeepRunningInTray = value);
    }

    // ----- Account -----

    private void LoadAccount()
    {
        DisplayName = _auth.User?.DisplayName ?? "";
        SignInMethods.Clear();
        if (_auth.User is { } u)
        {
            if (u.HasPassword) SignInMethods.Add(new SignInMethod(Symbol.Key, "Email and password", u.Email));
            foreach (var provider in u.ExternalProviders)
                SignInMethods.Add(new SignInMethod(Symbol.PersonAccounts, provider == AuthProviders.Microsoft ? "Microsoft account" : $"{provider} account", "Linked"));
        }
        foreach (var r in RoleSegments) r.IsSelected = Equals(r.Value, _auth.User?.Role);
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(EmailUnconfirmed));
        OnPropertyChanged(nameof(CanSaveName));
        OnPropertyChanged(nameof(MemberSince));
    }

    private void UpdateSyncText() => SyncText = _sync.State switch
    {
        SyncState.Syncing => "Syncing…",
        SyncState.Offline => "Offline. Changes are saved on this PC and sync when you're back online.",
        SyncState.Error => "The last sync didn't finish. Nerdy will try again shortly.",
        _ => _sync.LastSyncedAt is { } at ? $"Last synced {Fmt.Clock(at.LocalDateTime)}" : "Not synced yet"
    };

    [RelayCommand]
    private async Task SaveNameAsync()
    {
        var name = DisplayName.Trim();
        await RunAsync(() => _auth.UpdateProfileAsync(new UpdateProfileRequest { DisplayName = name }), "Name updated");
    }

    private async void PickRole(ChoiceItem item)
    {
        var role = (UserRole)item.Value!;
        if (_auth.User?.Role == role) return;
        foreach (var r in RoleSegments) r.IsSelected = r == item;
        await RunAsync(() => _auth.UpdateProfileAsync(new UpdateProfileRequest { Role = role }), "Role updated");
        LoadAccount(); // reverts the selection if the update failed
    }

    [RelayCommand]
    private Task ResendConfirmationAsync() =>
        RunAsync(() => _api.ResendConfirmationAsync(), $"Confirmation email sent to {Email}");

    [RelayCommand]
    private Task SyncNowAsync() => _sync.SyncNowAsync();

    [RelayCommand]
    private Task SignOutAsync() => _auth.SignOutAsync();

    [RelayCommand]
    private async Task DeleteAccountAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Delete your Nerdy account?",
            "This permanently deletes your account and everything in it: topics, notes, reminders, focus history and any classes you teach. It can't be undone.",
            "Delete account", destructive: true);
        if (!confirmed) return;
        await RunAsync(() => _auth.DeleteAccountAsync(), null);
    }

    private async Task RunAsync(Func<Task> action, string? success)
    {
        IsBusy = true;
        try
        {
            await action();
            if (success is not null) _toasts.Show(success, ToastKind.Success);
        }
        catch (ApiException ex)
        {
            _toasts.Show(ex.UserMessage, ToastKind.Error);
        }
        catch (OfflineException ex)
        {
            _logger.LogInformation("Settings action failed offline: {Message}", ex.Message);
            _toasts.Show("You're offline. Try again when you're connected.", ToastKind.Warning);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
