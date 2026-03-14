using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
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
        if (SyncPopupMenu is not null)
        {
            SyncPopupMenu.PlacementTarget = SyncStatusBorder;
            SyncPopupMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            SyncPopupMenu.IsOpen = true;
        }
    }

    private static readonly SolidColorBrush _searchFocusBrush = new(Color.FromRgb(0x1B, 0x6E, 0xC2));
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
            Color = Color.FromRgb(0x1B, 0x6E, 0xC2),
            BlurRadius = 6, Opacity = 0.18, ShadowDepth = 0
        };
    }

    private void GlobalSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBorder is null) return;
        SearchBorder.BorderBrush = _searchNormalBrush;
        SearchBorder.Background = _searchNormalBg;
        SearchBorder.Effect = null;
    }

    private void GlobalSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is MainViewModel vm)
        {
            // Navigate to the page most relevant to the search query
            if (!string.IsNullOrWhiteSpace(vm.SearchText))
                vm.NavigateCommand.Execute("Projects");
        }
        else if (e.Key == Key.Escape)
        {
            if (DataContext is MainViewModel vm2) vm2.SearchText = string.Empty;
            Keyboard.ClearFocus();
        }
    }
}
