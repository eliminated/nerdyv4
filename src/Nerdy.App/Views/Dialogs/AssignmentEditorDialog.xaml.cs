using Microsoft.UI.Xaml.Controls;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views.Dialogs;

public sealed partial class AssignmentEditorDialog : ContentDialog
{
    public AssignmentEditorDialog(AssignmentEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public AssignmentEditorViewModel ViewModel { get; }
}
