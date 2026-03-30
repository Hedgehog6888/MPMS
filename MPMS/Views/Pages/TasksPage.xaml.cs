using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class TasksPage : UserControl
{
    public TasksPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        bool canCreate = role is "Administrator" or "Project Manager" or "Foreman";
        CreateTaskBtn.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private TasksViewModel? VM => DataContext as TasksViewModel;

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
        var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
        while (current is not null)
        {
            if (current is Border b) return b;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
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

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetCreateMode(VM);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetEditMode(task, async () => await VM.LoadAsync());
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var owner = Window.GetWindow(this);
        if (MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(owner, "Задача", task.Name))
            await VM.DeleteTaskCommand.ExecuteAsync(task);
    }

    private async void MarkTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        await VM.MarkTaskForDeletionCommand.ExecuteAsync(task);
    }

    private void Task_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalTask task) return;
        OpenTaskDetail(task);
    }

    private async void OpenTaskDetail(LocalTask task)
    {
        var overlay = new TaskDetailOverlay();
        var vm = VM;
        UIElement? leftPanel = null;
        ProjectSummaryPanel? projectPanel = null;
        if (vm != null)
        {
            var project = await vm.GetProjectForTaskAsync(task.ProjectId);
            if (project != null)
            {
                projectPanel = new ProjectSummaryPanel();
                projectPanel.SetProject(project);
                leftPanel = projectPanel;
            }
        }

        overlay.SetTask(task, () =>
        {
            if (vm != null)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await vm.LoadAsync();
                    var project = await vm.GetProjectForTaskAsync(task.ProjectId);
                    if (project != null && projectPanel != null)
                        projectPanel.SetProject(project);
                });
            }
        });

        MainWindow.Instance?.ShowDrawer(leftPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
    }
}
