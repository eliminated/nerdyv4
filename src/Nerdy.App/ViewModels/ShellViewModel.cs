using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Controls;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Sync;
using Nerdy.App.Services.Theming;
using Nerdy.Contracts;
using Windows.UI.Text;

namespace Nerdy.App.ViewModels;

public sealed partial class NavItemViewModel(NavSection section, Action<string> navigate) : ObservableObject
{
    public string Id => section.Id;
    public string Label => section.Label;
    public Symbol Icon => section.Icon;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsPaneOpen { get; set; } = true;

    [ObservableProperty]
    public partial Brush IconBrush { get; set; } = null!;

    public FontWeight Weight => IsSelected ? FontWeights.SemiBold : FontWeights.Normal;
    public string? ToolTip => IsPaneOpen ? null : Label;
    public double LabelOpacity => IsPaneOpen ? 1 : 0;

    [RelayCommand]
    private void Navigate() => navigate(Id);

    partial void OnIsSelectedChanged(bool value) => OnPropertyChanged(nameof(Weight));

    partial void OnIsPaneOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(LabelOpacity));
    }
}

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly NavigationService _navigation;
    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly AuthService _auth;
    private readonly SyncService _sync;
    private readonly SearchService _search;

    public ShellViewModel(NavigationService navigation, PreferencesService preferences, ThemeService theme,
        AuthService auth, SyncService sync, SearchService search)
    {
        _navigation = navigation;
        _preferences = preferences;
        _theme = theme;
        _auth = auth;
        _sync = sync;
        _search = search;

        navigation.SectionChanged += (_, section) => Select(section);
        preferences.Changed += (_, _) => RebuildNav();
        theme.Changed += (_, _) => RefreshColors();
        auth.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(AuthService.User)) OnUserChanged(); };
        sync.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SyncService.State)) OnPropertyChanged(nameof(StatusBrush)); };
        RebuildNav();
    }

    public ObservableCollection<NavItemViewModel> NavItems { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    [ObservableProperty]
    public partial bool IsPaneOpen { get; set; } = true;

    [ObservableProperty]
    public partial bool IsSettingsSelected { get; set; }

    [ObservableProperty]
    public partial string SearchText { get; set; } = "";

    // ----- Profile -----
    public UserDto? User => _auth.User;
    public string DisplayName => User?.DisplayName ?? "";
    public string Email => User?.Email ?? "";
    public string RoleLabel => User?.Role switch
    {
        UserRole.Teacher => "Teacher",
        UserRole.Researcher => "Researcher",
        UserRole.Learner => "Lifelong learner",
        _ => "Student"
    };
    public string Initials => Models.UserInitials.From(DisplayName);
    public bool ShowEmailBanner => User is { EmailConfirmed: false, HasPassword: true };
    public string? ProfileToolTip => IsPaneOpen ? null : DisplayName;

    /// <summary>The dot on the avatar: green when synced, grey offline, amber when sync needs attention.</summary>
    public Brush StatusBrush => _sync.State switch
    {
        SyncState.Offline => _theme.Brush("NerdyTextTertiaryBrush"),
        SyncState.Error or SyncState.UpdateRequired => _theme.Brush("NerdyWarningBrush"),
        _ => _theme.Brush("NerdyOnlineBrush")
    };

    public string StatusText => _sync.State switch
    {
        SyncState.Offline => "Offline · changes are saved on this PC",
        SyncState.Error => "Sync needs attention",
        SyncState.UpdateRequired => "Update Nerdy to keep syncing",
        SyncState.Syncing => "Syncing…",
        _ => _sync.LastSyncedAt is { } t ? $"Synced {t:t}" : "Up to date"
    };

    // ----- Selection indicator -----
    public NavIndicator Indicator => _preferences.Current.NavIndicator;
    public bool ShowOrbit => Indicator != NavIndicator.Glide;
    public bool ShowAccentBar => Indicator == NavIndicator.Glide;
    public OrbitMode OrbitMode => Indicator == NavIndicator.Horizon ? OrbitMode.Horizon : OrbitMode.Aura;
    public string OrbitColors => Orbits.ColorsFor(_preferences.Current.Orbit) ?? $"{ColorMix.ToHex(_theme.Accent)},{ColorMix.ToHex(_theme.Accent)}";
    public Brush PillBackground => Indicator switch
    {
        NavIndicator.Glide => _theme.Brush("NerdyCardBackgroundBrush"),
        NavIndicator.Horizon => _theme.Brush("NerdySubtleHoverBrush"),
        _ => _theme.Brush("NerdyPillAuraBrush")
    };
    public bool PillHasShadow => Indicator != NavIndicator.Horizon;
    public double NavItemHeight => 38 * _theme.DensityFactor;

    public string? CurrentSection => _navigation.CurrentSection;

    [RelayCommand]
    private void TogglePane() => IsPaneOpen = !IsPaneOpen;

    [RelayCommand]
    private void OpenSettings() => _navigation.Navigate(NavSections.Settings);

    [RelayCommand]
    private Task SignOut() => _auth.SignOutAsync();

    public void Navigate(string section) => _navigation.Navigate(section);

    public void UpdateSearch(string text)
    {
        SearchResults.Clear();
        foreach (var r in _search.Search(text)) SearchResults.Add(r);
    }

    public void OpenResult(SearchResult result)
    {
        SearchText = "";
        SearchResults.Clear();
        _navigation.Navigate(result.Section, result.Parameter);
    }

    partial void OnIsPaneOpenChanged(bool value)
    {
        foreach (var item in NavItems) item.IsPaneOpen = value;
        OnPropertyChanged(nameof(ProfileToolTip));
    }

    private void RebuildNav()
    {
        var order = _preferences.Current.NavOrder;
        NavItems.Clear();
        foreach (var id in order)
            if (NavSections.All.TryGetValue(id, out var section))
                NavItems.Add(new NavItemViewModel(section, Navigate) { IsPaneOpen = IsPaneOpen });
        Select(_navigation.CurrentSection);
        RefreshColors();
        OnPropertyChanged(string.Empty);
    }

    private void Select(string? section)
    {
        foreach (var item in NavItems) item.IsSelected = item.Id == section;
        IsSettingsSelected = section == NavSections.Settings;
        RefreshColors();
        OnPropertyChanged(nameof(CurrentSection));
    }

    private void RefreshColors()
    {
        foreach (var item in NavItems)
            item.IconBrush = _theme.Brush(item.IsSelected ? "NerdyAccentBrush" : "NerdyTextPrimaryBrush");
        OnPropertyChanged(nameof(PillBackground));
        OnPropertyChanged(nameof(OrbitColors));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(NavItemHeight));
    }

    private void OnUserChanged()
    {
        OnPropertyChanged(nameof(User));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(RoleLabel));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(ShowEmailBanner));
    }
}
