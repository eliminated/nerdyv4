using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nerdy.App.Models;

/// <summary>
/// The user's settings. Stored and synced as one JSON document (PreferencesDto), so adding a setting here
/// never needs a server change. Unknown or missing members fall back to these defaults.
/// </summary>
public sealed class Preferences
{
    // Appearance
    public string Accent { get; set; } = Accents.Default;
    public AppThemeChoice Theme { get; set; } = AppThemeChoice.System;
    public string Font { get; set; } = Fonts.Default;
    public Density Density { get; set; } = Density.Comfortable;
    public string Wallpaper { get; set; } = Wallpapers.Default;
    public HomeLayout HomeLayout { get; set; } = HomeLayout.Dashboard;
    public NavIndicator NavIndicator { get; set; } = NavIndicator.Aura;
    public string Orbit { get; set; } = "ocean";
    public List<string> NavOrder { get; set; } = [.. NavSections.DefaultOrder];
    public Dictionary<string, bool> Widgets { get; set; } = HomeWidgets.All.ToDictionary(w => w.Id, _ => true);

    // Focus
    public BlockedAppMode BlockedApps { get; set; } = BlockedAppMode.Hide;
    public List<AllowedApp> AllowedApps { get; set; } = [.. AllowedApp.Defaults];
    public bool BlockWebsites { get; set; } = true;
    public List<string> AllowedWebsites { get; set; } = ["canvas.instructure.com", "scholar.google.com", "wikipedia.org"];
    public int DefaultFocusMinutes { get; set; } = 25;
    /// <summary>Non-emergency Ultra-Focus exits are reviewed by Claude, which can ask you to stay (fails open).</summary>
    public bool AiExitReview { get; set; } = true;

    // Planning
    public int StudyDayStartHour { get; set; } = 8;
    public int StudyDayEndHour { get; set; } = 20;
    public int DefaultReminderMinutes { get; set; } = 10;

    // Notifications and background
    public bool Notifications { get; set; } = true;
    public bool KeepRunningInTray { get; set; } = true;

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public string Serialize() => JsonSerializer.Serialize(this, Json);

    public static Preferences Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Preferences();
        try
        {
            var p = JsonSerializer.Deserialize<Preferences>(json, Json) ?? new Preferences();
            p.Normalize();
            return p;
        }
        catch (JsonException)
        {
            return new Preferences();
        }
    }

    public Preferences Clone() => Deserialize(Serialize());

    /// <summary>Repairs values from older versions or other devices (new nav sections, removed widgets…).</summary>
    private void Normalize()
    {
        NavOrder = NavOrder.Where(NavSections.DefaultOrder.Contains).Distinct().ToList();
        foreach (var id in NavSections.DefaultOrder.Where(id => !NavOrder.Contains(id))) NavOrder.Add(id);
        foreach (var w in HomeWidgets.All.Where(w => !Widgets.ContainsKey(w.Id))) Widgets[w.Id] = true;
        if (StudyDayEndHour <= StudyDayStartHour) (StudyDayStartHour, StudyDayEndHour) = (8, 20);
    }
}

public enum AppThemeChoice { System, Light, Dark }
public enum Density { Comfortable, Compact }
public enum HomeLayout { Dashboard, Agenda }
public enum NavIndicator { Glide, Aura, Horizon }
public enum BlockedAppMode { Hide, Close }

public sealed class AllowedApp
{
    public string ExeName { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Path { get; set; }
    public bool Enabled { get; set; } = true;

    public static IReadOnlyList<AllowedApp> Defaults =>
    [
        new() { ExeName = "calculatorapp.exe", Name = "Calculator", Description = "Windows" },
        new() { ExeName = "msedge.exe", Name = "Microsoft Edge", Description = "Allowed websites only" },
        new() { ExeName = "spotify.exe", Name = "Spotify", Description = "Music playback" },
        new() { ExeName = "onenote.exe", Name = "OneNote", Description = "Microsoft 365", Enabled = false },
        new() { ExeName = "discord.exe", Name = "Discord", Description = "Messaging", Enabled = false }
    ];
}

public static class Accents
{
    public const string Default = "#5B5BD6";
    public static readonly IReadOnlyList<string> All = ["#5B5BD6", "#1F9D74", "#E8743B", "#D6457A", "#2F7DE1", "#6B6B78"];
}

public sealed record FontChoice(string Id, string Label, string Family);

public static class Fonts
{
    public const string Default = "segoe";

    public static readonly IReadOnlyList<FontChoice> All =
    [
        new("segoe", "Segoe UI Variable", "Segoe UI Variable Text, Segoe UI"),
        // Bundled variable fonts (SIL Open Font License; licence texts sit next to the files).
        new("figtree", "Figtree", "ms-appx:///Assets/Fonts/Figtree.ttf#Figtree"),
        new("lexend", "Lexend", "ms-appx:///Assets/Fonts/Lexend.ttf#Lexend"),
        new("atkinson", "Atkinson Hyperlegible", "ms-appx:///Assets/Fonts/AtkinsonHyperlegibleNext.ttf#Atkinson Hyperlegible Next")
    ];

    public static FontChoice Get(string id) => All.FirstOrDefault(f => f.Id == id) ?? All[0];
}

public sealed record OrbitPalette(string Id, string Label, string? Colors);

/// <summary>Colour sets for the Aura and Horizon nav indicators; "accent" follows the accent colour.</summary>
public static class Orbits
{
    public static readonly IReadOnlyList<OrbitPalette> All =
    [
        new("ocean", "Ocean", "#38BDF8,#3B82F6,#6366F1"),
        new("aurora", "Aurora", "#34D399,#38BDF8,#A78BFA"),
        new("accent", "Accent", null)
    ];

    public static string? ColorsFor(string id) => All.FirstOrDefault(o => o.Id == id)?.Colors;
}

public static class Wallpapers
{
    public const string Default = "bloom";
    public static readonly IReadOnlyList<(string Id, string Label)> All =
        [("bloom", "Bloom"), ("mist", "Mist"), ("meadow", "Meadow"), ("paper", "Paper")];
}

public sealed record NavSection(string Id, string Label, FluentIcons.Common.Symbol Icon);

public static class NavSections
{
    public const string Home = "home", Calendar = "calendar", Schedule = "schedule", Reminders = "reminders",
        Topics = "topics", Notes = "notes", Focus = "focus", Stats = "stats", Classes = "classes", Settings = "settings";

    public static readonly IReadOnlyList<string> DefaultOrder = [Home, Calendar, Schedule, Reminders, Topics, Notes, Focus, Stats, Classes];

    public static readonly IReadOnlyDictionary<string, NavSection> All = new Dictionary<string, NavSection>
    {
        [Home] = new(Home, "Home", FluentIcons.Common.Symbol.Home),
        [Calendar] = new(Calendar, "Calendar", FluentIcons.Common.Symbol.Calendar),
        [Schedule] = new(Schedule, "Schedule", FluentIcons.Common.Symbol.CalendarEdit),
        [Reminders] = new(Reminders, "Reminders", FluentIcons.Common.Symbol.Alert),
        [Topics] = new(Topics, "Topics", FluentIcons.Common.Symbol.Library),
        [Notes] = new(Notes, "Notes", FluentIcons.Common.Symbol.Notepad),
        [Focus] = new(Focus, "Focus", FluentIcons.Common.Symbol.TargetArrow),
        [Stats] = new(Stats, "Statistics & History", FluentIcons.Common.Symbol.DataTrending),
        [Classes] = new(Classes, "Classes", FluentIcons.Common.Symbol.PeopleTeam)
    };
}

public sealed record HomeWidget(string Id, string Label);

public static class HomeWidgets
{
    public static readonly IReadOnlyList<HomeWidget> All =
    [
        new("next", "Next up"), new("stats", "Focus time"), new("streak", "Focus streak"),
        new("schedule", "Today's schedule"), new("reminders", "Reminders"), new("topics", "Topic progress"),
        new("assignments", "Class assignments")
    ];
}
