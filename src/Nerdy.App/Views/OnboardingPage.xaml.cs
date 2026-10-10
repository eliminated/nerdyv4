using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class OnboardingPage : Page, ITitleBarHost
{
    public OnboardingPage()
    {
        ViewModel = App.GetService<OnboardingViewModel>();
        Theme = App.GetService<ThemeService>();
        InitializeComponent();
    }

    public OnboardingViewModel ViewModel { get; }
    public ThemeService Theme { get; }
    public UIElement TitleBarElement => DragRegion;

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    private void OnSubjectKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddSubjectCommand.Execute(null);
    }
}
