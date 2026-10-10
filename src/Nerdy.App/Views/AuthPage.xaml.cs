using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class AuthPage : Page, ITitleBarHost
{
    public AuthPage()
    {
        ViewModel = App.GetService<AuthViewModel>();
        InitializeComponent();
        Loaded += (_, _) => EmailBox.Focus(FocusState.Programmatic);
    }

    public AuthViewModel ViewModel { get; }
    public UIElement TitleBarElement => DragRegion;

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    private void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.SubmitCommand.Execute(null);
    }
}
