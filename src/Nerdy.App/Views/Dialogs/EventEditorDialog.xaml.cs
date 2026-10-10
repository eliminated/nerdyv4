using Microsoft.UI.Xaml.Controls;
using Nerdy.App.ViewModels;

namespace Nerdy.App.Views.Dialogs;

public sealed partial class EventEditorDialog : ContentDialog
{
    public EventEditorDialog(EventEditorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        if (!viewModel.IsNew)
        {
            SecondaryButtonText = viewModel.IsSeries ? "Delete series" : "Delete";
            SecondaryButtonClick += (_, _) => viewModel.Delete();
        }
    }

    public EventEditorViewModel ViewModel { get; }

    private void OnSave(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!ViewModel.Save()) args.Cancel = true;
    }
}
