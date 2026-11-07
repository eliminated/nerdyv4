namespace Nerdy.App.Services;

/// <summary>Where Nerdy keeps its files on this device (%LOCALAPPDATA%\Nerdy).</summary>
public static class AppPaths
{
    /// <summary>
    /// Development only: NERDY_DATA_DIR=&lt;folder&gt; keeps a test build's sign-in, data and logs in that folder, so it can
    /// run next to the installed Nerdy without signing it out or touching its data (see <see cref="IsSeparate"/>).
    /// </summary>
    private static readonly string? Override =
        Environment.GetEnvironmentVariable("NERDY_DATA_DIR") is { Length: > 0 } dir ? Path.GetFullPath(dir) : null;

    public static string Root { get; } = Directory.CreateDirectory(
        Override ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nerdy")).FullName;

    /// <summary>True when running from a NERDY_DATA_DIR folder: then it's a separate instance from the installed Nerdy.</summary>
    public static bool IsSeparate => Override is not null;

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
