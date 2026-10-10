using Nerdy.App.Models;
using Nerdy.App.Services.Data;
using Nerdy.App.Services.Theming;

namespace Nerdy.App.Services;

/// <summary>
/// Live access to the user's preferences: applies appearance through <see cref="ThemeService"/> whenever they
/// change (locally or from another device) and notifies listeners such as the shell and the home page.
/// </summary>
public sealed class PreferencesService
{
    private readonly DataService _data;
    private readonly ThemeService _theme;

    public PreferencesService(DataService data, ThemeService theme)
    {
        _data = data;
        _theme = theme;
        data.Changed += (_, e) =>
        {
            if (!e.Affects(DataKinds.Preferences)) return;
            Current = data.Preferences;
            _theme.Apply(Current);
            Changed?.Invoke(this, Current);
        };
    }

    /// <summary>A snapshot; edit through <see cref="Update"/>.</summary>
    public Preferences Current { get; private set; } = new();

    public event EventHandler<Preferences>? Changed;

    public void Update(Action<Preferences> edit)
    {
        var next = Current.Clone();
        edit(next);
        _data.SavePreferences(next); // raises DataService.Changed → applied above
    }

    /// <summary>Applies defaults before anyone is signed in (auth screen).</summary>
    public void ApplyDefaults()
    {
        Current = new Preferences();
        _theme.Apply(Current);
    }
}
