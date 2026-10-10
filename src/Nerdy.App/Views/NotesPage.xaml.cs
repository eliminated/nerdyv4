using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class NotesPage : Page
{
    private readonly ThemeService _theme = App.GetService<ThemeService>();
    private bool _loading;
    private string _loadedText = "";

    public NotesPage()
    {
        ViewModel = App.GetService<NotesViewModel>();
        InitializeComponent();
        ViewModel.NoteOpened += (_, _) => LoadBody();
        var add = new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = VirtualKey.N };
        add.Invoked += (_, e) => { ViewModel.NewCommand.Execute(null); TitleBox.Focus(FocusState.Programmatic); e.Handled = true; };
        KeyboardAccelerators.Add(add);
        Loaded += (_, _) => _theme.Changed += OnThemeChanged;
        Unloaded += (_, _) => _theme.Changed -= OnThemeChanged;
    }

    public NotesViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.OnNavigatedTo(e.Parameter);
        LoadBody();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        PushBody();
        ViewModel.OnNavigatedFrom();
    }

    // ----- Body load / save -----

    private void LoadBody()
    {
        _loading = true;
        var doc = Body.Document;
        if (ViewModel.CurrentBodyRtf.Length > 0) doc.SetText(TextSetOptions.FormatRtf, ViewModel.CurrentBodyRtf);
        else doc.SetText(TextSetOptions.None, ViewModel.CurrentBodyText);
        ApplyThemeTextColor();
        doc.ClearUndoRedoHistory();
        doc.GetText(TextGetOptions.UseCrlf, out _loadedText);
        _loading = false;
    }

    private void OnBodyChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        // The editor also raises this after a note is loaded; only real edits should save (and bump "Edited").
        Body.Document.GetText(TextGetOptions.UseCrlf, out var text);
        if (text == _loadedText) return;
        _loadedText = text;
        PushBody();
    }

    private void PushBody()
    {
        if (_loading) return;
        Body.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
        Body.Document.GetText(TextGetOptions.UseCrlf, out var text);
        ViewModel.UpdateBody(rtf, text);
    }

    /// <summary>
    /// Nerdy doesn't offer text colours, so body text always uses the theme's text colour. RTF written in one theme
    /// would otherwise stay dark-on-dark (or light-on-light) after switching.
    /// </summary>
    private void ApplyThemeTextColor()
    {
        var wasLoading = _loading;
        _loading = true;
        var color = (_theme.Brush("NerdyTextPrimaryBrush") as SolidColorBrush)?.Color ?? Colors.Black;
        Body.Document.GetRange(0, TextConstants.MaxUnitCount).CharacterFormat.ForegroundColor = color;
        _loading = wasLoading;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyThemeTextColor();

    // ----- Formatting -----

    private ITextSelection Selection => Body.Document.Selection;

    private void OnBold(object sender, RoutedEventArgs e) => Toggle(f => f.Bold = f.Bold == FormatEffect.On ? FormatEffect.Off : FormatEffect.On);
    private void OnItalic(object sender, RoutedEventArgs e) => Toggle(f => f.Italic = f.Italic == FormatEffect.On ? FormatEffect.Off : FormatEffect.On);
    private void OnUnderline(object sender, RoutedEventArgs e) =>
        Toggle(f => f.Underline = f.Underline == UnderlineType.None ? UnderlineType.Single : UnderlineType.None);

    private void OnHighlight(object sender, RoutedEventArgs e)
    {
        var highlight = ColorMix.WithAlpha(_theme.Accent, 0x55);
        Toggle(f => f.BackgroundColor = f.BackgroundColor == highlight ? Colors.Transparent : highlight);
    }

    private void OnBullets(object sender, RoutedEventArgs e) => ToggleList(MarkerType.Bullet);
    private void OnNumbers(object sender, RoutedEventArgs e) => ToggleList(MarkerType.Arabic);

    private void OnClearFormatting(object sender, RoutedEventArgs e)
    {
        var f = Selection.CharacterFormat;
        f.Bold = FormatEffect.Off;
        f.Italic = FormatEffect.Off;
        f.Underline = UnderlineType.None;
        f.BackgroundColor = Colors.Transparent;
        f.Size = 15;
        Selection.ParagraphFormat.ListType = MarkerType.None;
        StyleBox.SelectedIndex = 0;
        PushBody();
    }

    private void OnStyleChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Body is null || _loading) return;
        var f = Selection.CharacterFormat;
        (f.Size, f.Bold) = StyleBox.SelectedIndex switch
        {
            1 => (22f, FormatEffect.On),
            2 => (18f, FormatEffect.On),
            _ => (15f, FormatEffect.Off)
        };
        Body.Focus(FocusState.Programmatic);
        PushBody();
    }

    private void Toggle(Action<ITextCharacterFormat> change)
    {
        change(Selection.CharacterFormat);
        Body.Focus(FocusState.Programmatic);
        PushBody();
    }

    private void ToggleList(MarkerType type)
    {
        var p = Selection.ParagraphFormat;
        p.ListType = p.ListType == type ? MarkerType.None : type;
        Body.Focus(FocusState.Programmatic);
        PushBody();
    }

    private void OnChecklistKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddChecklistItemCommand.Execute(null);
    }
}
