using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Instance = this;

        // Keep the top-bar avatar image in sync with MainViewModel.UserAvatarPath
        viewModel.PropertyChanged += OnMainViewModelPropertyChanged;
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;
        if (e.PropertyName is nameof(MainViewModel.UserAvatarPath) or nameof(MainViewModel.UserAvatarData))
            ApplyTopBarAvatar(vm.UserAvatarData, vm.UserAvatarPath);
    }

    /// <summary>
    /// Shows the best available avatar in the top bar:
    /// 1. AvatarData (bytes stored in DB) — custom photo or auto-generated initials image
    /// 2. AvatarPath (legacy file path)
    /// 3. Falls back to initials circle (shown by default in XAML)
    /// </summary>
    private void ApplyTopBarAvatar(byte[]? avatarData, string? avatarPath)
    {
        var bmp = MPMS.Services.AvatarHelper.GetImageSource(avatarData, avatarPath);
        if (bmp is not null)
        {
            TopBarAvatarImage.Source = bmp;
            TopBarAvatarBorder.Visibility = Visibility.Visible;
            return;
        }
        TopBarAvatarImage.Source = null;
        TopBarAvatarBorder.Visibility = Visibility.Collapsed;
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.ToggleSidebarCommand.Execute(null);
    }

    public void ShowDrawer(UIElement content, double width = 520)
    {
        // Detach previous content first to avoid "child must be detached from parent Visual" when the same or related element is reparented
        DrawerContentPresenter.Content = null;
        DrawerContentPresenter.Content = content;
        DrawerPanel.Width = width;

        // Clear any held animations before setting local values
        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, null);
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, null);

        DrawerPanel.Margin = new Thickness(width, 0, 0, 0);
        OverlayBackdrop.Opacity = 0;
        OverlayLayer.Visibility = Visibility.Visible;

        var slideIn = new ThicknessAnimation(
            new Thickness(width, 0, 0, 0),
            new Thickness(0),
            TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, slideIn);

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    /// <summary>Shows a dual-panel drawer: left panel (e.g. project context) + right panel (e.g. task detail).</summary>
    public void ShowDrawer(UIElement? leftContent, UIElement rightContent, double totalWidth = 900)
    {
        // Clear drawer first so leftContent/rightContent can be reparented if they are the current drawer content
        DrawerContentPresenter.Content = null;

        UIElement content;
        if (leftContent is not null)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(leftContent, 0);
            Grid.SetColumn(rightContent, 1);
            grid.Children.Add(leftContent);
            grid.Children.Add(rightContent);
            content = grid;
        }
        else
        {
            content = rightContent;
        }
        ShowDrawer(content, totalWidth);
    }

    public void HideDrawer()
    {
        double w = DrawerPanel.ActualWidth > 0 ? DrawerPanel.ActualWidth : DrawerPanel.Width;
        var currentMargin = DrawerPanel.Margin;
        var slideOut = new ThicknessAnimation(
            currentMargin,
            new Thickness(w, 0, 0, 0),
            TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        slideOut.Completed += (s, e) =>
        {
            DrawerContentPresenter.Content = null;
            OverlayLayer.Visibility = Visibility.Collapsed;
            // Обновить данные текущей страницы при закрытии drawer (проект, задачи и т.д.)
            if (DataContext is MainViewModel mainVm && mainVm.CurrentPageViewModel is ILoadable loadable)
                _ = loadable.LoadAsync();
        };
        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, slideOut);

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void Backdrop_Click(object sender, MouseButtonEventArgs e)
        => HideDrawer();

    private void UserPanel_Click(object sender, RoutedEventArgs e)
    {
        if (UserContextMenu is not null)
        {
            UserContextMenu.PlacementTarget = UserPanelBorder;
            UserContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            UserContextMenu.IsOpen = true;
        }
    }

    private void SyncStatus_Click(object sender, RoutedEventArgs e)
    {
        SyncPopup.IsOpen = !SyncPopup.IsOpen;
    }

    private void SyncNow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.SyncNowCommand.Execute(null);
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.NavigateCommand.Execute("Settings");
    }

    private void MyProfile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.NavigateCommand.Execute("Profile");
    }

    private static readonly SolidColorBrush _searchFocusBrush = new(Colors.Black);
    private static readonly SolidColorBrush _searchNormalBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush _searchFocusBg = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush _searchNormalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));

    private void GlobalSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBorder is null) return;
        SearchBorder.BorderBrush = _searchFocusBrush;
        SearchBorder.Background = _searchFocusBg;
        SearchBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 6, Opacity = 0.10, ShadowDepth = 0
        };
    }

    private void GlobalSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBorder is null) return;
        SearchBorder.BorderBrush = _searchNormalBrush;
        SearchBorder.Background = _searchNormalBg;
        SearchBorder.Effect = null;
    }

    private System.Threading.CancellationTokenSource _searchCts = new();

    private void GlobalSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = GlobalSearchBox?.Text ?? "";
        ClearSearchBtn.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

        if (string.IsNullOrWhiteSpace(text))
        {
            SearchResultsPopup.IsOpen = false;
            return;
        }
        _ = RunSearchAsync(text);
    }

    private async System.Threading.Tasks.Task RunSearchAsync(string query)
    {
        _searchCts.Cancel();
        _searchCts = new System.Threading.CancellationTokenSource();
        var ct = _searchCts.Token;

        try
        {
            await System.Threading.Tasks.Task.Delay(200, ct); // debounce

            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync(ct);

            var searchTerm = SearchHelper.Normalize(query);
            var projects = searchTerm is null
                ? new List<LocalProject>()
                : (await db.Projects.ToListAsync(ct))
                    .Where(p => SearchHelper.ContainsIgnoreCase(p.Name, searchTerm) ||
                        SearchHelper.ContainsIgnoreCase(p.Client, searchTerm))
                    .Take(5).ToList();

            var tasks = searchTerm is null
                ? new List<LocalTask>()
                : (await db.Tasks.ToListAsync(ct))
                    .Where(t => SearchHelper.ContainsIgnoreCase(t.Name, searchTerm) ||
                        SearchHelper.ContainsIgnoreCase(t.Description, searchTerm))
                    .Take(5).ToList();

            var stages = searchTerm is null
                ? new List<LocalTaskStage>()
                : (await db.TaskStages.ToListAsync(ct))
                    .Where(s => SearchHelper.ContainsIgnoreCase(s.Name, searchTerm) ||
                        SearchHelper.ContainsIgnoreCase(s.Description, searchTerm))
                    .Take(5).ToList();

            // Populate TaskName for stages
            var taskIds = stages.Select(s => s.TaskId).Distinct().ToList();
            var taskNames = await db.Tasks.Where(t => taskIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
            foreach (var s in stages)
                s.TaskName = taskNames.GetValueOrDefault(s.TaskId, "—");

            ct.ThrowIfCancellationRequested();

            await Dispatcher.InvokeAsync(() =>
            {
                bool hasProjects = projects.Count > 0;
                bool hasTasks = tasks.Count > 0;
                bool hasStages = stages.Count > 0;
                bool hasAny = hasProjects || hasTasks || hasStages;

                SearchProjectsSection.Visibility = hasProjects ? Visibility.Visible : Visibility.Collapsed;
                SearchProjectsDivider.Visibility = hasProjects && (hasTasks || hasStages) ? Visibility.Visible : Visibility.Collapsed;
                SearchTasksSection.Visibility = hasTasks ? Visibility.Visible : Visibility.Collapsed;
                SearchTasksDivider.Visibility = hasTasks && hasStages ? Visibility.Visible : Visibility.Collapsed;
                SearchStagesSection.Visibility = hasStages ? Visibility.Visible : Visibility.Collapsed;
                NoSearchResultsText.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

                SearchProjectsList.ItemsSource = projects;
                SearchTasksList.ItemsSource = tasks;
                SearchStagesList.ItemsSource = stages;

                SearchResultsPopup.IsOpen = true;
            });
        }
        catch (System.OperationCanceledException) { }
    }

    private void SearchResult_Click(object sender, MouseButtonEventArgs e)
    {
        SearchResultsPopup.IsOpen = false;
        if (DataContext is not MainViewModel vm) return;

        if (sender is FrameworkElement fe)
        {
            if (fe.Tag is LocalProject project)
            {
                vm.NavigateToProject(project);
            }
            else if (fe.Tag is LocalTask)
            {
                vm.NavigateCommand.Execute("Tasks");
            }
            else if (fe.Tag is LocalTaskStage)
            {
                vm.NavigateCommand.Execute("Stages");
            }
        }

        GlobalSearchBox.Text = "";
        vm.SearchText = string.Empty;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        GlobalSearchBox.Text = "";
        if (DataContext is MainViewModel vm) vm.SearchText = string.Empty;
        SearchResultsPopup.IsOpen = false;
        ClearSearchBtn.Visibility = Visibility.Collapsed;
    }

    private void GlobalSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is MainViewModel vm) vm.SearchText = string.Empty;
            GlobalSearchBox.Text = "";
            SearchResultsPopup.IsOpen = false;
            Keyboard.ClearFocus();
        }
    }
}
