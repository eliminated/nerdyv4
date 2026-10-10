using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentIcons.Common;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Api;
using Nerdy.App.Services.Auth;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels.Common;
using Nerdy.Contracts;
using Nerdy.Core.Topics;

namespace Nerdy.App.ViewModels;

/// <summary>One segment of the step indicator; the current step is three times as wide.</summary>
public sealed class ProgressDot(double width, Brush fill)
{
    public double Width { get; } = width;
    public Brush Fill { get; } = fill;
}

/// <summary>An accent colour swatch with the prototype's double ring when selected.</summary>
public sealed partial class SwatchItem(string hex, Brush fill, Brush ring, Action<string> pick)
{
    public string Hex { get; } = hex;
    public Brush Fill { get; } = fill;
    public Brush Ring { get; } = ring;

    [RelayCommand]
    private void Pick() => pick(Hex);
}

public sealed partial class WallpaperItem(string id, string label, Brush ring, Action<string> pick)
{
    public string Id { get; } = id;
    public string Label { get; } = label;
    public Brush Ring { get; } = ring;

    [RelayCommand]
    private void Pick() => pick(Id);
}

public sealed partial class OnboardingViewModel : ObservableObject, INavigationAware
{
    private static readonly (string Label, Symbol Icon)[] Subjects =
    [
        ("Biology", Symbol.LeafOne), ("Calculus", Symbol.MathFormula), ("World History", Symbol.Globe),
        ("Thesis research", Symbol.Document), ("Chemistry", Symbol.Beaker), ("Languages", Symbol.Translate),
        ("Programming", Symbol.Code), ("Literature", Symbol.Book)
    ];

    private static readonly (UserRole Role, string Label, string Description, Symbol Icon)[] Roles =
    [
        (UserRole.Student, "Student", "Classes, exams, assignments", Symbol.HatGraduation),
        (UserRole.Teacher, "Teacher", "Lessons, grading, office hours", Symbol.PersonBoard),
        (UserRole.Researcher, "Researcher", "Papers, experiments, writing", Symbol.Beaker),
        (UserRole.Learner, "Lifelong learner", "Courses and personal projects", Symbol.Lightbulb)
    ];

    private readonly AuthService _auth;
    private readonly DataService _data;
    private readonly PreferencesService _preferences;
    private readonly ThemeService _theme;
    private readonly SessionController _session;
    private readonly ToastService _toasts;
    private readonly ILogger<OnboardingViewModel> _logger;
    private readonly HashSet<string> _pickedSubjects = ["Biology", "Calculus", "Thesis research"];
    private readonly List<string> _customSubjects = [];
    private readonly RoleSuggestionDto? _suggestion;
    private UserRole _role = UserRole.Student;

    public OnboardingViewModel(AuthService auth, DataService data, PreferencesService preferences, ThemeService theme,
        SessionController session, ToastService toasts, ILogger<OnboardingViewModel> logger)
    {
        _auth = auth;
        _data = data;
        _preferences = preferences;
        _theme = theme;
        _session = session;
        _toasts = toasts;
        _logger = logger;
        // Before onboarding is done the server suggests a role from the email address; the person confirms it here.
        _suggestion = auth.User is { OnboardingCompleted: false, RoleSuggestion: { } suggestion } ? suggestion : null;
        _role = _suggestion?.Role ?? auth.User?.Role ?? UserRole.Student;
    }

    public ObservableCollection<ProgressDot> Dots { get; } = [];
    public ObservableCollection<ChoiceItem> RoleCards { get; } = [];
    public ObservableCollection<ChoiceItem> SubjectChips { get; } = [];
    public ObservableCollection<SwatchItem> Swatches { get; } = [];
    public ObservableCollection<WallpaperItem> WallpaperOptions { get; } = [];

    [ObservableProperty, NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsRole), nameof(IsSubjects), nameof(IsPersonalize), nameof(ShowNav), nameof(StepText), nameof(NextLabel))]
    public partial int Step { get; set; }

    [ObservableProperty] public partial string NewSubject { get; set; } = "";
    [ObservableProperty] public partial bool IsSaving { get; set; }

    public bool IsWelcome => Step == 0;
    public bool IsRole => Step == 1;
    public bool IsSubjects => Step == 2;
    public bool IsPersonalize => Step == 3;
    public bool ShowNav => Step > 0;
    public string StepText => $"Step {Step} of 3";
    public string NextLabel => Step == 3 ? "Start using Nerdy" : "Continue";
    public string SignedInAs => _auth.User is { } u ? $"Signed in as {u.Email}" : "";
    public bool ShowSuggestion => _suggestion is { } s && s.Role == _role;

    public string SuggestionText => _suggestion switch
    {
        null => "",
        { Source: RoleSuggestionSource.Personal } => "You signed up with a personal email, so we picked Lifelong learner.",
        { Source: RoleSuggestionSource.School, Domain: var d } => $"Your email ({d}) looks like a school or university address, so we picked Student.",
        { Source: RoleSuggestionSource.SchoolStaff, Domain: var d } => $"Your email ({d}) looks like a school staff address, so we picked Teacher.",
        { Source: RoleSuggestionSource.Alumni, Domain: var d } => $"Your email ({d}) looks like an alumni address, so we picked Lifelong learner.",
        { Domain: var d } => $"Your email ({d}) looks like a work address, so we picked Researcher."
    } + " Pick another if that's not you.";
    public string Wallpaper => _preferences.Current.Wallpaper;

    public void OnNavigatedTo(object? parameter)
    {
        _theme.Changed += OnThemeChanged;
        Rebuild();
    }

    public void OnNavigatedFrom() => _theme.Changed -= OnThemeChanged;

    [RelayCommand]
    private async Task NextAsync()
    {
        if (Step < 3)
        {
            Step++;
            RebuildDots();
            return;
        }
        await FinishAsync();
    }

    [RelayCommand]
    private void Back()
    {
        if (Step == 0) return;
        Step--;
        RebuildDots();
    }

    [RelayCommand]
    private Task SignOut() => _auth.SignOutAsync();

    [RelayCommand]
    private void AddSubject()
    {
        var name = NewSubject.Trim();
        NewSubject = "";
        if (name.Length == 0) return;
        if (!Subjects.Any(s => s.Label.Equals(name, StringComparison.CurrentCultureIgnoreCase)) &&
            !_customSubjects.Contains(name, StringComparer.CurrentCultureIgnoreCase))
            _customSubjects.Add(name);
        _pickedSubjects.Add(name);
        RebuildSubjects();
    }

    private async Task FinishAsync()
    {
        IsSaving = true;
        try
        {
            // Topics for the chosen subjects (skipping ones that already exist, e.g. synced from another device).
            var existing = _data.Topics.Select(t => t.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
            var used = _data.Topics.Select(t => t.ColorHex).ToList();
            var order = _data.Topics.Count;
            var topics = new List<TopicDto>();
            foreach (var name in _pickedSubjects.Where(n => !existing.Contains(n)))
            {
                var suggested = TopicPalette.ForSubject(name);
                var color = used.Contains(suggested, StringComparer.OrdinalIgnoreCase) ? TopicPalette.Next(used) : suggested;
                used.Add(color);
                topics.Add(new TopicDto { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow, Name = name, ColorHex = color, SortOrder = order++ });
            }
            _data.SaveMany(topics);

            // Teachers get Classes near the top of the sidebar.
            _preferences.Update(p =>
            {
                if (_role == UserRole.Teacher && p.NavOrder.Remove(NavSections.Classes)) p.NavOrder.Insert(1, NavSections.Classes);
            });

            if (_auth.User?.Role != _role)
            {
                try { await _auth.UpdateProfileAsync(new UpdateProfileRequest { Role = _role }); }
                catch (Exception ex) when (ex is OfflineException or ApiException)
                {
                    _logger.LogInformation("Couldn't save the role yet: {Message}", ex.Message);
                }
            }

            await _session.CompleteOnboardingAsync();
            var first = (_auth.User?.DisplayName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            _toasts.Show(first is null ? "You're all set" : $"You're all set, {first}", ToastKind.Success);
        }
        finally
        {
            IsSaving = false;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();

    private void Rebuild()
    {
        RebuildDots();
        RoleCards.Clear();
        foreach (var r in Roles)
            RoleCards.Add(new ChoiceItem(_theme, ChoiceLook.Card, r.Label, r.Role, PickRole, r.Icon, r.Description, selected: r.Role == _role));
        RebuildSubjects();
        RebuildPersonalize();
    }

    private void RebuildDots()
    {
        // 560 px of card content, 3 gaps of 6 px, 6 width units (current step = 3).
        const double unit = (560 - 18) / 6.0;
        Dots.Clear();
        for (var i = 0; i < 4; i++)
            Dots.Add(new ProgressDot(unit * (i == Step ? 3 : 1), _theme.Brush(i <= Step ? "NerdyAccentBrush" : "NerdyTrackBrush")));
    }

    private void RebuildSubjects()
    {
        SubjectChips.Clear();
        foreach (var (label, icon) in Subjects.Concat(_customSubjects.Select(c => (c, Symbol.Tag))))
            SubjectChips.Add(new ChoiceItem(_theme, ChoiceLook.Chip, label, label, ToggleSubject, icon, selected: _pickedSubjects.Contains(label))
            {
                SelectedIcon = Symbol.Checkmark
            });
    }

    private void RebuildPersonalize()
    {
        var p = _preferences.Current;
        Swatches.Clear();
        foreach (var hex in Accents.All)
        {
            var on = hex.Equals(p.Accent, StringComparison.OrdinalIgnoreCase);
            Swatches.Add(new SwatchItem(hex, ColorMix.Brush(hex), on ? ColorMix.Brush(hex) : _theme.Brush("NerdyTransparentBrush"), PickAccent));
        }
        WallpaperOptions.Clear();
        foreach (var (id, label) in Wallpapers.All)
            WallpaperOptions.Add(new WallpaperItem(id, label, _theme.Brush(id == p.Wallpaper ? "NerdyAccentBrush" : "NerdyCardBorderBrush"), PickWallpaper));
        OnPropertyChanged(nameof(Wallpaper));
    }

    private void PickRole(ChoiceItem item)
    {
        _role = (UserRole)item.Value!;
        foreach (var c in RoleCards) c.IsSelected = c == item;
        OnPropertyChanged(nameof(ShowSuggestion));
    }

    private void ToggleSubject(ChoiceItem item)
    {
        var label = (string)item.Value!;
        if (!_pickedSubjects.Remove(label)) _pickedSubjects.Add(label);
        item.IsSelected = _pickedSubjects.Contains(label);
    }

    private void PickAccent(string hex)
    {
        _preferences.Update(p => p.Accent = hex); // applied live; ThemeService.Changed rebuilds the swatches
    }

    private void PickWallpaper(string id)
    {
        _preferences.Update(p => p.Wallpaper = id);
        RebuildPersonalize();
    }
}
