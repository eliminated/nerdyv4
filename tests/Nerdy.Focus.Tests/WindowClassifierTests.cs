namespace Nerdy.Focus.Tests;

// Only pure logic and read-only queries are tested here: nothing in this suite hides or closes real windows.
public class WindowClassifierTests
{
    private const int Self = 4242;
    private static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "spotify.exe", "msedge.exe" };

    private static WindowFacts App(string exe, string cls = "Chrome_WidgetWin_1", int pid = 100) =>
        new(exe, cls, Visible: true, Cloaked: false, ToolWindow: false, HasOwner: false, ProcessId: pid);

    [Theory]
    [InlineData("spotify.exe", "Allowed")]
    [InlineData("MSEDGE.EXE", "Allowed")]
    [InlineData("discord.exe", "Block")]
    [InlineData("chrome.exe", "Block")]
    public void Apps_are_allowed_or_blocked_by_exe_name(string exe, string expected) =>
        Assert.Equal(Enum.Parse<WindowVerdict>(expected), WindowClassifier.Classify(App(exe), Self, Allowed));

    [Theory]
    [InlineData("explorer.exe", "Shell_TrayWnd")]
    [InlineData("explorer.exe", "Progman")]
    [InlineData("explorer.exe", "WorkerW")]
    [InlineData("startmenuexperiencehost.exe", "Windows.UI.Core.CoreWindow")]
    [InlineData("searchhost.exe", "Windows.UI.Core.CoreWindow")]
    [InlineData("taskmgr.exe", "TaskManagerWindow")]
    [InlineData("lockapp.exe", "Windows.UI.Core.CoreWindow")]
    [InlineData("textinputhost.exe", "Windows.UI.Core.CoreWindow")]
    public void Windows_itself_and_task_manager_are_never_touched(string exe, string cls) =>
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(App(exe, cls), Self, Allowed));

    [Fact]
    public void File_explorer_folders_count_as_an_app()
    {
        Assert.Equal(WindowVerdict.Block, WindowClassifier.Classify(App("explorer.exe", "CabinetWClass"), Self, Allowed));
    }

    [Fact]
    public void Nerdy_itself_is_ignored() =>
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(App("discord.exe", pid: Self), Self, Allowed));

    [Fact]
    public void Invisible_cloaked_tool_and_owned_windows_are_ignored()
    {
        var w = App("discord.exe");
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(w with { Visible = false }, Self, Allowed));
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(w with { Cloaked = true }, Self, Allowed));
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(w with { ToolWindow = true }, Self, Allowed));
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(w with { HasOwner = true }, Self, Allowed));
    }

    [Fact]
    public void Unidentifiable_processes_are_ignored() =>
        Assert.Equal(WindowVerdict.Ignore, WindowClassifier.Classify(App(""), Self, Allowed));

    [Fact]
    public void Allow_list_is_normalised()
    {
        var set = AppCatalog.Normalize([" Spotify.EXE ", @"C:\Program Files\App\Code.exe", ""]);

        Assert.Equal(["code.exe", "spotify.exe"], set.Order());
    }

    [Fact]
    public void Catalog_queries_are_read_only_and_do_not_throw()
    {
        var running = AppCatalog.GetRunningApps();
        var installed = AppCatalog.GetInstalledApps();

        Assert.All(running, a => Assert.False(string.IsNullOrEmpty(a.ExeName)));
        Assert.All(installed, a => Assert.EndsWith(".exe", a.ExeName));
    }
}
