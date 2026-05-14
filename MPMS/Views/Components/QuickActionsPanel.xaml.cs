using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Components;

public partial class QuickActionsPanel : UserControl
{
    private bool _canEdit;
    private bool _canManageTeam;

    public QuickActionsPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var userRole = auth.UserRole ?? "";
        _canEdit = userRole is "Administrator" or "Project Manager" or "Foreman";
        _canManageTeam = userRole is "Administrator" or "Project Manager";

        CreateTaskQuickBtn.Visibility = _canEdit ? Visibility.Visible : Visibility.Collapsed;
        QuickTeamBtn.Visibility = _canManageTeam ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDetailViewModel vm || vm.Project is null) return;
        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        var overlay = new CreateTaskOverlay();
        overlay.SetCreateMode(tasksVm, vm.Project.Id,
            onSaved: async () =>
            {
                await vm.LoadAsync();
                UpdateButtons();
            });
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
    }

    private void OpenQuickTeamOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProjectDetailViewModel vm || vm.Project is null) return;
        var overlay = new QuickTeamMembersOverlay();
        overlay.SetProject(vm.Project.Id, onSaved: async () =>
        {
            await vm.LoadAsync();
            UpdateButtons();
        });
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 532);
    }

    private void UploadFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файлы для загрузки",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*|Изображения|*.png;*.jpg;*.jpeg|Документы|*.pdf;*.docx;*.xlsx"
        };
        if (dialog.ShowDialog() == true)
        {
            MessageBox.Show(
                $"Выбрано файлов: {dialog.FileNames.Length}\nЗагрузка будет реализована при подключении к серверу.",
                "Загрузка файлов", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void UpdateButtons()
    {
        if (DataContext is not ProjectDetailViewModel vm || vm.Project is null) return;
        bool marked = vm.Project.IsMarkedForDeletion;
        bool closed = vm.Project.IsClosed || vm.Project.Status == ProjectStatus.Closed;

        CreateTaskQuickBtn.IsEnabled = !marked && !closed;
        CreateTaskQuickBtn.Opacity = (marked || closed) ? 0.5 : 1.0;

        QuickTeamBtn.IsEnabled = !marked && !closed;
        QuickTeamBtn.Opacity = (marked || closed) ? 0.5 : 1.0;

        UploadFileQuickBtn.IsEnabled = !marked && !closed;
        UploadFileQuickBtn.Opacity = (marked || closed) ? 0.5 : 1.0;
    }
}
