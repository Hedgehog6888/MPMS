using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
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

    private string _userRole = "";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        _userRole = auth.UserRole ?? "";
        bool canCreate = _userRole is "Administrator" or "Project Manager";
        CreateProjectBtn.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private ProjectsViewModel? VM => DataContext as ProjectsViewModel;

    private MainViewModel? MainVM
    {
        get
        {
            // After login, Application.Current.MainWindow may still point to LoginWindow.
            // Resolve MainViewModel from the actual main shell window.
            if (MainWindow.Instance?.DataContext is MainViewModel vm)
                return vm;

            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is { DataContext: MainViewModel fallbackVm })
                return fallbackVm;

            return App.Services.GetService<MainViewModel>();
        }
    }

    private void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var overlay = new CreateProjectOverlay();
        overlay.SetCreateMode(VM);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredProjectFormOverlayWidth);
    }

    private void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null) return;
        var overlay = new CreateProjectOverlay();
        overlay.SetEditMode(VM, project);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredProjectFormOverlayWidth);
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null) return;
        var owner = Window.GetWindow(this);
        if (MPMS.Views.ConfirmDeleteDialog.Show(
                owner, "Проект", project.Name,
                "Все задачи и этапы этого проекта также будут удалены."))
            await VM.DeleteProjectCommand.ExecuteAsync(project);
    }

    private async void MarkProjectForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null) return;
        if (!project.IsMarkedForDeletion)
        {
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner is null || !MPMS.Views.ConfirmDeleteDialog.ShowMarkForDeletion(owner, "проект", project.Name))
                return;
        }
        await VM.MarkProjectForDeletionCommand.ExecuteAsync(project);
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

