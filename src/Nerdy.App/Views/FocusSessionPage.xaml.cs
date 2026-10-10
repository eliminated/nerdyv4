using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Nerdy.App.Services.Theming;
using Nerdy.App.ViewModels;
using Windows.System;

namespace Nerdy.App.Views;

public sealed partial class FocusSessionPage : Page, ITitleBarHost
{
    private readonly Storyboard _pulse = new() { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };

    public FocusSessionPage()
    {
        ViewModel = App.GetService<FocusSessionViewModel>();
        Theme = App.GetService<ThemeService>();
        InitializeComponent();
        BuildPulse();
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(FocusSessionViewModel.IsPaused)) return;
            if (ViewModel.IsPaused) _pulse.Begin();
            else { _pulse.Stop(); RingTime.Opacity = 1; BigTime.Opacity = 1; }
        };
    }

    public FocusSessionViewModel ViewModel { get; }
    public ThemeService Theme { get; }
    public UIElement TitleBarElement => DragRegion;

    protected override void OnNavigatedTo(NavigationEventArgs e) => ViewModel.OnNavigatedTo(e.Parameter);

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _pulse.Stop();
        ViewModel.OnNavigatedFrom();
    }

    /// <summary>The paused timer gently pulses, as in the prototype.</summary>
    private void BuildPulse()
    {
        foreach (var target in new UIElement[] { RingTime, BigTime })
        {
            var a = new DoubleAnimation { From = 1, To = 0.45, Duration = TimeSpan.FromSeconds(0.8) };
            Storyboard.SetTarget(a, target);
            Storyboard.SetTargetProperty(a, "Opacity");
            _pulse.Children.Add(a);
        }
    }

    private void OnPlanKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        e.Handled = true;
        ViewModel.AddPlanItemCommand.Execute(null);
    }

    private void OnSaveQuickNote(object sender, RoutedEventArgs e) => ViewModel.SaveQuickNote();
}
