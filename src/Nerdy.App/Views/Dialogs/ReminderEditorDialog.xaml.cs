using Microsoft.UI.Xaml.Controls;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views.Dialogs;

public sealed partial class ReminderEditorDialog : ContentDialog
{
    public ReminderEditorDialog(ReminderEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += (_, _) => viewModel.Save();
        SecondaryButtonClick += (_, _) => viewModel.Delete();
    }

    public ReminderEditorViewModel ViewModel { get; }
}
