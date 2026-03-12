using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class ProjectsPage : UserControl
{
    public ProjectsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        bool canCreate = role is "Admin" or "Administrator"
                              or "ProjectManager" or "Manager" or "Project Manager";
        CreateProjectBtn.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private ProjectsViewModel? VM => DataContext as ProjectsViewModel;

    private MainViewModel? MainVM =>
        Application.Current.MainWindow?.DataContext as MainViewModel;

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var overlay = new CreateProjectOverlay();
        overlay.SetCreateMode(VM);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null) return;
        var overlay = new CreateProjectOverlay();
        overlay.SetEditMode(VM, project);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null) return;
        var result = MessageBox.Show(
            $"Удалить проект «{project.Name}»?\nВсе задачи проекта также будут удалены.",
            "Подтверждение удаления",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            await VM.DeleteProjectCommand.ExecuteAsync(project);
    }

    private void ProjectName_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb || tb.DataContext is not LocalProject project) return;
        e.Handled = true;
        MainVM?.NavigateToProject(project);
    }

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalProject project) return;
        MainVM?.NavigateToProject(project);
    }
}
