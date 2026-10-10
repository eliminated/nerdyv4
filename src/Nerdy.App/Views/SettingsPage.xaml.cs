using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        ViewModel.ScrollRequested += OnScrollRequested;
        ViewModel.OnNavigatedTo(e.Parameter);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ViewModel.ScrollRequested -= OnScrollRequested;
        ViewModel.OnNavigatedFrom();
    }

    private void OnScrollRequested(object? sender, string anchor)
    {
        if (anchor != "widgets") return;
        // Wait for the first layout so the card has a position to scroll to.
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => WidgetsCard.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = true, VerticalAlignmentRatio = 0.1 }));
    }

    private async void OnAddAppClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAddableAppsAsync();

    private void OnAddableClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AddableApp app) return;
        ViewModel.AddApp(app);
        AddAppFlyout.Hide();
    }

    private void OnWebsiteKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddWebsiteCommand.Execute(null);
    }
}
