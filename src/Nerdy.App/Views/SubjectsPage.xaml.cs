using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class SubjectsPage : Page
{
    public SubjectsPage()
    {
        ViewModel = App.GetService<SubjectsViewModel>();
        InitializeComponent();
        var add = new KeyboardAccelerator { Modifiers = VirtualKeyModifiers.Control, Key = VirtualKey.N };
        add.Invoked += (_, e) => { ViewModel.AddSubjectCommand.Execute(null); e.Handled = true; };
        KeyboardAccelerators.Add(add);
        ViewModel.TopicRequested += OnTopicRequested;
    }

    public SubjectsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e) => ViewModel.OnNavigatedFrom();

    // The card may have only just been created, so wait for layout before scrolling to it.
    private void OnTopicRequested(object? sender, TopicCardViewModel card) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (TopicCards.ContainerFromItem(card) is UIElement container)
                container.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, VerticalOffset = -12, AnimationDesired = true });
        });

    private void OnNewSubtopicKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter || (sender as FrameworkElement)?.DataContext is not TopicCardViewModel card) return;
        e.Handled = true;
        card.AddSubtopicCommand.Execute(null);
    }

    private void OnNewTopicKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddTopicCommand.Execute(null);
    }
}
