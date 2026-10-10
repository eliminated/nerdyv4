using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views;

public sealed partial class StatsPage : Page
{
    public StatsPage()
    {
        ViewModel = App.GetService<StatsViewModel>();
        InitializeComponent();
    }

    public StatsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();
}
