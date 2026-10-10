using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Nerdy.App.Services;

/// <summary>Full-window views. Everything except <see cref="Shell"/> hides the sidebar, as in the prototype.</summary>
public enum RootView { Auth, Onboarding, Shell, Focus, UltraFocus }

/// <summary>Implemented by view models that want navigation parameters and lifecycle callbacks.</summary>
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom() { }
}

/// <summary>
/// Two levels of navigation: the window's root frame (auth, onboarding, shell, focus screens) and the shell's
/// content frame (sections such as Home or Calendar). Page types are registered by the app at startup.
/// </summary>
public sealed class NavigationService
{
    private readonly Dictionary<RootView, Type> _roots = [];
    private readonly Dictionary<string, Type> _sections = [];
    private Frame? _rootFrame;
    private Frame? _contentFrame;
    private (string Section, object? Parameter)? _pending;

    public RootView? CurrentRoot { get; private set; }
    public string? CurrentSection { get; private set; }

    public event EventHandler<RootView>? RootChanged;
    public event EventHandler<string>? SectionChanged;

    public void RegisterRoot<TPage>(RootView view) where TPage : Page => _roots[view] = typeof(TPage);
    public void RegisterSection<TPage>(string section) where TPage : Page => _sections[section] = typeof(TPage);

    public void AttachRootFrame(Frame frame) => _rootFrame = frame;

    /// <summary>Called by the shell page once its content frame exists.</summary>
    public void AttachContentFrame(Frame frame)
    {
        _contentFrame = frame;
        _contentFrame.BackStack.Clear();
        // A new shell (e.g. after signing in again) shows nothing yet, so the next Navigate must not be skipped.
        if (frame.Content is null) CurrentSection = null;
        if (_pending is { } p)
        {
            _pending = null;
            Navigate(p.Section, p.Parameter);
        }
    }

    public void ShowRoot(RootView view, object? parameter = null)
    {
        if (_rootFrame is null) return;
        if (view != RootView.Shell) _contentFrame = null;
        CurrentRoot = view;
        _rootFrame.Navigate(_roots[view], parameter, new DrillInNavigationTransitionInfo());
        _rootFrame.BackStack.Clear();
        RootChanged?.Invoke(this, view);
    }

    /// <summary>Opens a shell section, showing the shell first if a full-window view is up.</summary>
    public void Navigate(string section, object? parameter = null)
    {
        if (CurrentRoot != RootView.Shell || _contentFrame is null)
        {
            _pending = (section, parameter);
            if (CurrentRoot != RootView.Shell) ShowRoot(RootView.Shell);
            return;
        }
        if (!_sections.TryGetValue(section, out var type)) return;
        if (CurrentSection == section && parameter is null) return;
        _contentFrame.Navigate(type, parameter, new EntranceNavigationTransitionInfo());
        CurrentSection = section;
        SectionChanged?.Invoke(this, section);
    }

    public bool CanGoBack => _contentFrame?.CanGoBack == true;

    public void GoBack()
    {
        if (_contentFrame is not { CanGoBack: true } frame) return;
        frame.GoBack();
        CurrentSection = _sections.FirstOrDefault(s => s.Value == frame.CurrentSourcePageType).Key ?? CurrentSection;
        if (CurrentSection is not null) SectionChanged?.Invoke(this, CurrentSection);
    }
}
