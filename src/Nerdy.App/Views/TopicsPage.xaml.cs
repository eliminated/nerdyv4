using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class TopicsPage : Page
{
    public TopicsPage()
    {
        ViewModel = App.GetService<TopicsViewModel>();
        InitializeComponent();
        var add = new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = VirtualKey.N };
        add.Invoked += (_, e) => { ViewModel.AddTopicCommand.Execute(null); e.Handled = true; };
        KeyboardAccelerators.Add(add);
    }

    public TopicsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    private void OnNewSubtopicKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddSubtopicCommand.Execute(null);
    }
}
