using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.Models;
using Nerdy.App.Services;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class ShellPage : Page, ITitleBarHost
{
    private const double OpenWidth = 232;
    private const double ClosedWidth = 56;

    public ShellPage()
    {
        ViewModel = App.GetService<ShellViewModel>();
        Theme = App.GetService<ThemeService>();
        InitializeComponent();

        // Ctrl+, opens settings (the comma key has no named VirtualKey).
        var settings = new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = (VirtualKey)188 };
        settings.Invoked += OnSettingsAccelerator;
        KeyboardAccelerators.Add(settings);

        ViewModel.PropertyChanged += OnViewModelChanged;
        ViewModel.NavItems.CollectionChanged += (_, _) => QueuePillUpdate();
        NavList.LayoutUpdated += OnNavLayoutUpdated;
        // The profile button (Settings' target) is anchored to the bottom, so follow window resizes without animating.
        Sidebar.SizeChanged += (_, e) => { if (e.NewSize.Height != e.PreviousSize.Height) { _pillSnap = true; QueuePillUpdate(); } };
        Sidebar.Width = ViewModel.IsPaneOpen ? OpenWidth : ClosedWidth;
    }

    public ShellViewModel ViewModel { get; }
    public ThemeService Theme { get; }
    public UIElement TitleBarElement => AppTitleBar;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        App.GetService<NavigationService>().AttachContentFrame(ContentFrame);
    }

    // ----- Selection pill -----

    private bool _pillPending = true;
    private bool _pillShown;
    private bool _pillSnap;

    private void QueuePillUpdate() => _pillPending = true;

    private void OnNavLayoutUpdated(object? sender, object e)
    {
        if (!_pillPending) return;
        _pillPending = false;
        MovePill();
    }

    private void MovePill()
    {
        FrameworkElement? target = null;
        if (ViewModel.IsSettingsSelected) target = ProfileButton;
        else
        {
            var index = ViewModel.NavItems.ToList().FindIndex(i => i.IsSelected);
            if (index >= 0) target = NavList.ContainerFromIndex(index) as FrameworkElement;
        }

        if (target is null || target.ActualHeight == 0)
        {
            Fade(Pill, 0);
            return;
        }

        var y = target.TransformToVisual(Sidebar).TransformPoint(default).Y - Sidebar.Padding.Top;
        var sb = new Storyboard();
        var move = new DoubleAnimation
        {
            To = y,
            Duration = TimeSpan.FromMilliseconds(_pillShown && !_pillSnap ? 550 : 0),
            EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(move, PillTranslate);
        Storyboard.SetTargetProperty(move, "Y");
        sb.Children.Add(move);
        var height = new DoubleAnimation { To = target.ActualHeight, Duration = TimeSpan.FromMilliseconds(300), EnableDependentAnimation = true };
        Storyboard.SetTarget(height, Pill);
        Storyboard.SetTargetProperty(height, "Height");
        sb.Children.Add(height);
        sb.Begin();
        Fade(Pill, 1);
        _pillShown = true;
        _pillSnap = false;
    }

    private static void Fade(UIElement element, double to)
    {
        var sb = new Storyboard();
        var a = new DoubleAnimation { To = to, Duration = TimeSpan.FromMilliseconds(300) };
        Storyboard.SetTarget(a, element);
        Storyboard.SetTargetProperty(a, "Opacity");
        sb.Children.Add(a);
        sb.Begin();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ShellViewModel.CurrentSection):
            case nameof(ShellViewModel.IsSettingsSelected):
            case nameof(ShellViewModel.NavItemHeight):
            case "":
                QueuePillUpdate();
                break;
            case nameof(ShellViewModel.IsPaneOpen):
                AnimatePane(ViewModel.IsPaneOpen);
                break;
        }
    }

    private void AnimatePane(bool open)
    {
        var sb = new Storyboard();
        var a = new DoubleAnimation
        {
            To = open ? OpenWidth : ClosedWidth,
            Duration = TimeSpan.FromMilliseconds(420),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(a, Sidebar);
        Storyboard.SetTargetProperty(a, "Width");
        sb.Children.Add(a);
        sb.Begin();
    }

    // ----- Search -----

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) ViewModel.UpdateSearch(sender.Text);
        UpdateSearchHint();
    }

    private void OnSearchSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchResult result)
        {
            ViewModel.OpenResult(result);
            sender.Text = "";
        }
    }

    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var result = args.ChosenSuggestion as SearchResult ?? ViewModel.SearchResults.FirstOrDefault();
        if (result is null) return;
        ViewModel.OpenResult(result);
        sender.Text = "";
    }

    private void OnSearchFocusChanged(object sender, RoutedEventArgs e) => UpdateSearchHint();

    private void UpdateSearchHint() =>
        SearchHint.Visibility = SearchBox.FocusState == FocusState.Unfocused && string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible : Visibility.Collapsed;

    // ----- Keyboard shortcuts & profile menu -----

    private void OnTogglePaneAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.TogglePaneCommand.Execute(null);
        args.Handled = true;
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        SearchBox.Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void OnSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        ViewModel.OpenSettingsCommand.Execute(null);
        args.Handled = true;
    }

    private void OnProfileAccount(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.Hide();
        App.GetService<NavigationService>().Navigate(NavSections.Settings, "account");
    }

    private void OnProfileSettings(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.Hide();
        ViewModel.OpenSettingsCommand.Execute(null);
    }

    private async void OnProfileHelp(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.Hide();
        await App.GetService<DialogService>().ShowAsync(new Dialogs.HelpDialog());
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        ProfileFlyout.Hide();
        ViewModel.SignOutCommand.Execute(null);
    }
}
