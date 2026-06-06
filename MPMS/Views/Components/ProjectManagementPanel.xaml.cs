using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Components;

public partial class ProjectManagementPanel : UserControl
{
    private readonly IAuthService _auth;

    public ProjectManagementPanel()
    {
        InitializeComponent();
        _auth = App.Services.GetRequiredService<IAuthService>();
    }

    private bool IsWorker() =>
        string.Equals(_auth.UserRole, "Worker", StringComparison.OrdinalIgnoreCase)
        || string.Equals(_auth.UserRole, "Работник", StringComparison.OrdinalIgnoreCase);

    private bool IsForeman() =>
        _auth.UserRole is "Foreman" or "Прораб";

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (IsWorker() || IsForeman()) return;
        if (DataContext is not ProjectDetailViewModel vm || vm.Project is null) return;
        var projVm = App.Services.GetRequiredService<ProjectsViewModel>();
        var overlay = new CreateProjectOverlay();
        overlay.SetEditMode(projVm, vm.Project,
            onSaved: async () =>
            {
                await vm.LoadAsync();
                UpdateButtons();
            });
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
    }

    private async void MarkProject_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDetailViewModel vm) return;
        if (vm.Project is { IsMarkedForDeletion: false } projectToMark)
        {
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner is null || !ConfirmDeleteDialog.ShowMarkForDeletion(owner, "проект", projectToMark.Name))
                return;
        }
        await vm.MarkProjectForDeletionCommand.ExecuteAsync(null);
        UpdateButtons();
    }

    private async void CloseProject_Click(object sender, RoutedEventArgs e)
    {
        if (IsWorker() || IsForeman()) return;
        if (DataContext is not ProjectDetailViewModel vm || vm.Project is null) return;
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
        if (owner is null) return;

        var (confirmed, reason) = ConfirmDeleteDialog.ShowCloseProjectConfirmation(owner, vm.Project.Name);
        if (confirmed)
        {
            await vm.CloseProjectAsync(reason);
            UpdateButtons();
        }
    }

    public void UpdateButtons()
    {
        if (DataContext is not ProjectDetailViewModel vm || vm.Project is null) return;
        bool marked = vm.Project.IsMarkedForDeletion;
        bool closed = vm.Project.IsClosed || vm.Project.Status == ProjectStatus.Closed;
        bool isWorkerOrForeman = IsWorker() || IsForeman();

        MarkProjectBtn.ApplyTemplate();
        if (MarkProjectBtn.Template?.FindName("MarkBtnText", MarkProjectBtn) is System.Windows.Controls.TextBlock tb)
            tb.Text = marked ? "Снять пометку удаления" : "Пометить к удалению";

        EditProjectBtn.IsEnabled = !marked && !closed && !isWorkerOrForeman;
        EditProjectBtn.Opacity = (marked || closed || isWorkerOrForeman) ? 0.5 : 1.0;
        MarkProjectBtn.Visibility = closed ? Visibility.Collapsed : Visibility.Visible;
        MarkProjectBtn.IsEnabled = !isWorkerOrForeman;
        MarkProjectBtn.Opacity = isWorkerOrForeman ? 0.5 : 1.0;
        CloseProjectBtn.IsEnabled = !marked && !closed && !isWorkerOrForeman;
        CloseProjectBtn.Opacity = (marked || closed || isWorkerOrForeman) ? 0.5 : 1.0;
    }
}
