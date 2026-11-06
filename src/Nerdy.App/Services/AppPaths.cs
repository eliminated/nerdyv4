namespace Nerdy.App.Services;

/// <summary>Where Nerdy keeps its files on this device (%LOCALAPPDATA%\Nerdy).</summary>
public static class AppPaths
{
    public static string Root { get; } = Directory.CreateDirectory(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nerdy")).FullName;

    /// <summary>DPAPI-protected sign-in session (tokens + cached user).</summary>
    public static string SessionFile => Path.Combine(Root, "session.dat");

    /// <summary>Settings that belong to this device rather than the account (window placement etc.).</summary>
    public static string DeviceSettingsFile => Path.Combine(Root, "device.json");

    public static string LogsDirectory => Directory.CreateDirectory(Path.Combine(Root, "logs")).FullName;

    /// <summary>Everything stored for one account on this PC, so several people can use Nerdy on a shared PC.</summary>
    public static string UserDirectory(Guid userId) => Path.Combine(Root, "users", userId.ToString("N"));

    /// <summary>The local database of one account.</summary>
    public static string UserDatabase(Guid userId) =>
        Path.Combine(Directory.CreateDirectory(UserDirectory(userId)).FullName, "nerdy.db");
}
