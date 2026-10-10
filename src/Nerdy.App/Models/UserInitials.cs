namespace Nerdy.App.Models;

public static class UserInitials
{
    /// <summary>"Sam Kim" → "SK", "sam" → "S".</summary>
    public static string From(string? name)
    {
        var parts = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
        };
    }
}
