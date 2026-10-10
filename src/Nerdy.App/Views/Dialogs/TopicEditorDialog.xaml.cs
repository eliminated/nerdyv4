using Microsoft.UI.Xaml.Controls;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views.Dialogs;

public sealed partial class TopicEditorDialog : ContentDialog
{
    public TopicEditorDialog(TopicEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += (_, _) => viewModel.Save();
    }

    public TopicEditorViewModel ViewModel { get; }
}
