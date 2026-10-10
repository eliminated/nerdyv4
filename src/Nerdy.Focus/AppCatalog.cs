using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Nerdy.Focus;

public sealed record RunningApp(string ExeName, string ExePath, string DisplayName, int ProcessId, string? MainWindowTitle);
public sealed record InstalledApp(string Name, string ExeName, string TargetPath, string ShortcutPath);

/// <summary>Read-only views of the apps on this PC, for the allowed-apps picker and the pre-session summary.</summary>
public static class AppCatalog
{
    /// <summary>Apps that currently have a visible window (one entry per executable), excluding Windows itself and Nerdy.</summary>
    public static IReadOnlyList<RunningApp> GetRunningApps() =>
        AppWindows.Snapshot()
            .Where(x => WindowClassifier.Classify(x.Facts, AppWindows.OwnProcessId, EmptySet) != WindowVerdict.Ignore)
            .GroupBy(x => x.Window.ExeName)
            .Select(g =>
            {
                var w = g.First().Window;
                return new RunningApp(w.ExeName, w.ExePath, AppWindows.DisplayName(w.ExePath, w.ExeName), w.ProcessId, w.Title);
            })
            .OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Running apps that a session with this allow-list would hide or close.</summary>
    public static IReadOnlyList<RunningApp> GetAppsThatWouldBeBlocked(IReadOnlyCollection<string> allowedExeNames)
    {
        var allowed = Normalize(allowedExeNames);
        return GetRunningApps().Where(a => !allowed.Contains(a.ExeName)).ToList();
    }

    /// <summary>Desktop apps from the Start menu (all users + current user).</summary>
    public static IReadOnlyList<InstalledApp> GetInstalledApps()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };
        var apps = new Dictionary<string, InstalledApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> shortcuts;
            try { shortcuts = Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories).ToList(); }
            catch (UnauthorizedAccessException) { continue; }

            foreach (var lnk in shortcuts)
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (LooksLikeNoise(name)) continue;
                var target = ResolveShortcut(lnk);
                if (target is null || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                var exe = Path.GetFileName(target).ToLowerInvariant();
                if (WindowClassifier.IsSystemProcess(exe) || LooksLikeNoise(exe)) continue;
                apps.TryAdd(exe, new InstalledApp(name, exe, target, lnk));
            }
        }
        return apps.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Opens an app, shortcut or URL the way Explorer would.</summary>
    public static void Launch(string pathOrShortcut) =>
        Process.Start(new ProcessStartInfo(pathOrShortcut) { UseShellExecute = true })?.Dispose();

    internal static HashSet<string> Normalize(IEnumerable<string> exeNames) =>
        exeNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => Path.GetFileName(n.Trim()).ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> EmptySet = [];

    private static bool LooksLikeNoise(string name)
    {
        var n = name.ToLowerInvariant();
        return n.Contains("uninstall") || n.Contains("readme") || n.Contains("help") || n.Contains("license") ||
               n.Contains("documentation") || n.Contains("release notes") || n.Contains("setup") || n.Contains("updater");
    }

    // ----- .lnk resolution through the shell's IShellLink (COM) -----

    private static string? ResolveShortcut(string path)
    {
        try
        {
            var link = (IShellLinkW)new ShellLink();
            ((IPersistFile)link).Load(path, 0);
            var buffer = new StringBuilder(1024);
            link.GetPath(buffer, buffer.Capacity, 0, 0);
            Marshal.FinalReleaseComObject(link);
            var target = buffer.ToString();
            return target.Length == 0 ? null : Environment.ExpandEnvironmentVariables(target);
        }
        catch (COMException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, nint findData, uint flags);
        // The remaining IShellLinkW methods are not used; declaring only the first slot is enough for GetPath.
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
    }
}
