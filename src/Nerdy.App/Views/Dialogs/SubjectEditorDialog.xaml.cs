using Microsoft.UI.Xaml.Controls;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views.Dialogs;

public sealed partial class SubjectEditorDialog : ContentDialog
{
    public SubjectEditorDialog(SubjectEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        PrimaryButtonClick += (_, _) => viewModel.Save();
    }

    public SubjectEditorViewModel ViewModel { get; }
}
