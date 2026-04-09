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

    private static readonly SolidColorBrush _focusBrush = new(Colors.Black);
    private static readonly SolidColorBrush _normalBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush _focusBg = new(Colors.White);
    private static readonly SolidColorBrush _normalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));
    private static readonly System.Windows.Media.Effects.DropShadowEffect _focusShadow = new()
    {
        Color = Colors.Black, BlurRadius = 6, Opacity = 0.10, ShadowDepth = 0
    };

    private static Border? FindSearchBorder(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is Border b) return b;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && FindSearchBorder(tb) is { } border)
        {
            border.BorderBrush = _focusBrush;
            border.Background = _focusBg;
            border.Effect = _focusShadow;
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && FindSearchBorder(tb) is { } border)
        {
            border.BorderBrush = _normalBrush;
            border.Background = _normalBg;
            border.Effect = null;
        }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (VM is not null) VM.SearchText = string.Empty;
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
