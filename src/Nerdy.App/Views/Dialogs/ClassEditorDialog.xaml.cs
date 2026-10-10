using Microsoft.UI.Xaml.Controls;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views.Dialogs;

public sealed partial class ClassEditorDialog : ContentDialog
{
    public ClassEditorDialog(ClassEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    public ClassEditorViewModel ViewModel { get; }
}
