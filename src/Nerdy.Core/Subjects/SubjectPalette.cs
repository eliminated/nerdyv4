namespace Nerdy.Core.Subjects;

/// <summary>Colours offered for subjects and classes, starting with the prototype's subject colours.</summary>
public static class SubjectPalette
{
    public static readonly IReadOnlyList<string> Colors =
    [
        "#1F9D74", // green
        "#E8743B", // orange
        "#D6457A", // pink
        "#2F7DE1", // blue
        "#5B5BD6", // indigo
        "#8B5CF6", // violet
        "#0E9AA7", // teal
        "#C2410C", // rust
        "#CA8A04", // amber
        "#64748B"  // slate
    ];

    /// <summary>The first palette colour not used yet, cycling when all are taken.</summary>
    public static string Next(IEnumerable<string> used)
    {
        var taken = used.Select(c => c.ToUpperInvariant()).ToHashSet();
        return Colors.FirstOrDefault(c => !taken.Contains(c.ToUpperInvariant())) ?? Colors[taken.Count % Colors.Count];
    }

    /// <summary>Suggested colours for the subjects offered during onboarding.</summary>
    public static string ForSubject(string name) => name.ToLowerInvariant() switch
    {
        var n when n.Contains("bio") => "#1F9D74",
        var n when n.Contains("calc") || n.Contains("math") => "#E8743B",
        var n when n.Contains("hist") => "#D6457A",
        var n when n.Contains("thesis") || n.Contains("research") => "#2F7DE1",
        var n when n.Contains("chem") => "#8B5CF6",
        var n when n.Contains("lang") => "#0E9AA7",
        var n when n.Contains("program") || n.Contains("code") => "#5B5BD6",
        var n when n.Contains("lit") => "#C2410C",
        _ => "#64748B"
    };
}
