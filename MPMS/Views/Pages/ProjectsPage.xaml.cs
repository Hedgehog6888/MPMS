using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private string _userRole = "";

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        _userRole = auth.UserRole ?? "";
        bool canCreate = _userRole is "Administrator" or "Project Manager";
        CreateProjectBtn.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private static readonly SolidColorBrush _focusBrush = new(Colors.Black);
    private static readonly SolidColorBrush _normalBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush _focusBg = new(Colors.White);
    private static readonly SolidColorBrush _normalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var parent = VisualTreeHelper.GetParent(tb);
            if (VisualTreeHelper.GetParent(parent) is Border border)
            {
                border.BorderBrush = _focusBrush;
                border.Background = _focusBg;
            }
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            var parent = VisualTreeHelper.GetParent(tb);
            if (VisualTreeHelper.GetParent(parent) is Border border)
            {
                border.BorderBrush = _normalBrush;
                border.Background = _normalBg;
            }
        }
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
        var owner = Window.GetWindow(this);
        if (MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(
                owner, "Проект", project.Name,
                "Все задачи и этапы этого проекта также будут удалены."))
            await VM.DeleteProjectCommand.ExecuteAsync(project);
    }

    private async void MarkProjectForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null) return;
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
