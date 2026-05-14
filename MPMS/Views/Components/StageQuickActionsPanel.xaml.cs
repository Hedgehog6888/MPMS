using System.Windows;
using System.Windows.Controls;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Components;

public partial class StageQuickActionsPanel : UserControl
{
    public StageQuickActionsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateButtons();
    }

    private void UploadFile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StageDetailViewModel vm)
            vm.FilesControlVM.UploadFileCommand.Execute(null);
    }

    private void RefreshComposition_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm || vm.EditStage is null) return;
        var overlay = new QuickTeamMembersOverlay();
        overlay.SetStage(vm.EditStage.Id, onSaved: async () =>
        {
            await vm.LoadAsync();
            UpdateButtons();
        });
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 532);
    }

    public void UpdateButtons()
    {
        if (DataContext is not StageDetailViewModel vm) return;
        bool marked = vm.IsStageMarkedForDeletion;

        RefreshCompositionQuickBtn.IsEnabled = !marked;
        RefreshCompositionQuickBtn.Opacity = marked ? 0.5 : 1.0;

        UploadFileQuickBtn.IsEnabled = !marked;
        UploadFileQuickBtn.Opacity = marked ? 0.5 : 1.0;
    }
}
