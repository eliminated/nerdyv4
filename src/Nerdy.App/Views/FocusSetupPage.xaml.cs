using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class FocusSetupPage : Page
{
    public FocusSetupPage()
    {
        ViewModel = App.GetService<FocusSetupViewModel>();
        InitializeComponent();
    }

    public FocusSetupViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

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
