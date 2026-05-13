using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
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
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    private bool _photoViewerWasVisible = false;
    private bool _documentViewerWasVisible = false;
    public const double TaskOrStageDetailDrawerWidth = 700;
    public const double TaskOrStageDetailWithLeftTotalWidth = 1000;
    public const double CenteredFormOverlayWidth = 560;
    public const double CenteredProjectFormOverlayWidth = 640;

    private enum OverlayPresentationMode { None, Drawer, Modal }
    private OverlayPresentationMode _overlayMode = OverlayPresentationMode.None;

    private readonly List<UIElement> _drawerModalStack = [];

    private System.Windows.Threading.DispatcherTimer? _saveSettingsTimer;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Instance = this;

        LoadWindowSize();

        Loaded += (s, e) =>
        {
            Topmost = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                Topmost = false;
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);

            if (DataContext is MainViewModel vm)
            {
                SidebarColumn.Width = new GridLength(vm.IsSidebarExpanded ? 220 : 64, GridUnitType.Pixel);

                vm.PropertyChanged += (s, pe) =>
                {
                    if (pe.PropertyName == nameof(MainViewModel.IsSidebarExpanded))
                        AnimateSidebarWidth(vm.IsSidebarExpanded);
                };
            }
        };

        Closing += (s, e) => SaveWindowSize();
        StateChanged += (s, e) => ScheduleSaveSettings();
        LocationChanged += (s, e) => ScheduleSaveSettings();
        SizeChanged += (s, e) => ScheduleSaveSettings();
    }

    private void ScheduleSaveSettings()
    {
        _saveSettingsTimer?.Stop();
        _saveSettingsTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _saveSettingsTimer.Tick += (s, e) =>
        {
            _saveSettingsTimer?.Stop();
            SaveWindowSize();
        };
        _saveSettingsTimer.Start();
    }

    private void LoadWindowSize()
    {
        const double defaultWidth = 1280;
        const double defaultHeight = 768;

        double savedWidth = LocalSettings.GetDouble("MainWindow_Width", defaultWidth);
        double savedHeight = LocalSettings.GetDouble("MainWindow_Height", defaultHeight);

        Width = Math.Max(savedWidth, MinWidth);
        Height = Math.Max(savedHeight, MinHeight);

        string savedState = LocalSettings.Get("MainWindow_State", "Normal");
        if (Enum.TryParse<WindowState>(savedState, out var state))
        {
            WindowState = state;
        }
    }

    private void SaveWindowSize()
    {
        if (WindowState != WindowState.Minimized)
        {
            LocalSettings.Set("MainWindow_State", WindowState.ToString());
        }
        else
        {
            LocalSettings.Set("MainWindow_State", WindowState.Normal.ToString());
        }

        if (WindowState == WindowState.Normal)
        {
            LocalSettings.SetDouble("MainWindow_Width", ActualWidth);
            LocalSettings.SetDouble("MainWindow_Height", ActualHeight);
        }
    }

    private void AnimateSidebarWidth(bool isExpanded)
    {
        double targetWidth = isExpanded ? 220 : 64;
        var animation = new GridLengthAnimation
        {
            From = SidebarColumn.Width,
            To = new GridLength(targetWidth, GridUnitType.Pixel),
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
    }

    /// <summary>Открыт ли поверх drawer стековый модал (для закрытия только его, без drawer).</summary>
    public bool HasStackedModalOverDrawer => _drawerModalStack.Count > 0;

    public void ShowDrawer(UIElement content, double width = 520)
    {
        _drawerModalStack.Clear();
        DrawerContentPresenter.Content = null;
        ModalOverlayContentPresenter.Content = null;
        DrawerContentPresenter.Content = content;
        DrawerPanel.Width = width;
        _overlayMode = OverlayPresentationMode.Drawer;

        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, null);
        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, null);
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, null);

        DrawerPanel.Margin = new Thickness(width, 0, 0, 0);
        DrawerPanel.Visibility = Visibility.Visible;
        ModalOverlayPanel.Visibility = Visibility.Collapsed;
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

    public void ShowCenteredOverlay(UIElement content, double width = 920)
    {
        _drawerModalStack.Clear();
        DrawerContentPresenter.Content = null;
        ModalOverlayContentPresenter.Content = null;
        ModalOverlayContentPresenter.Content = content;
        ModalOverlayPanel.Width = width;
        _overlayMode = OverlayPresentationMode.Modal;

        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, null);
        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, null);
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, null);

        DrawerPanel.Visibility = Visibility.Collapsed;
        ModalOverlayPanel.Visibility = Visibility.Visible;
        ModalOverlayPanel.Opacity = 0;
        ModalOverlayTransform.Y = 16;
        OverlayBackdrop.Opacity = 0;
        OverlayLayer.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var slideIn = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    /// <summary>Центральная панель поверх уже открытого drawer (drawer не скрывается).</summary>
    public void ShowStackedModalOverDrawer(UIElement content, double width = 520)
    {
        if (_overlayMode != OverlayPresentationMode.Drawer
            || OverlayLayer.Visibility != Visibility.Visible
            || DrawerPanel.Visibility != Visibility.Visible)
        {
            ShowCenteredOverlay(content, width);
            return;
        }

        ModalOverlayContentPresenter.Content = null;
        _drawerModalStack.Add(content);
        ModalOverlayContentPresenter.Content = content;
        ModalOverlayPanel.Width = width;

        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, null);
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, null);

        ModalOverlayPanel.Visibility = Visibility.Visible;
        ModalOverlayPanel.Opacity = 0;
        ModalOverlayTransform.Y = 16;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220));
        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        var slideIn = new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
    }

    /// <summary>Карточка участника: админ/менеджер — любой; прораб — только работник.</summary>
    public void TryOpenUserPeek(Guid userId, Guid projectId)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        using var db = dbFactory.CreateDbContext();
        if (!UserPeekAccess.CanViewerPeekTargetUser(auth, db, userId))
            return;

        var overlay = new UserPeekOverlay();
        overlay.SetUser(userId, projectId);

        if (_overlayMode == OverlayPresentationMode.Drawer
            && OverlayLayer.Visibility == Visibility.Visible
            && DrawerPanel.Visibility == Visibility.Visible)
            ShowStackedModalOverDrawer(overlay, 480);
        else
            ShowCenteredOverlay(overlay, 480);
    }

    public void ShowDrawer(UIElement? leftContent, UIElement rightContent, double totalWidth = 1000)
    {
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
        if (_drawerModalStack.Count > 0)
        {
            HideStackedModalOnly();
            return;
        }

        void CompleteClose()
        {
            _drawerModalStack.Clear();
            DrawerContentPresenter.Content = null;
            ModalOverlayContentPresenter.Content = null;
            DrawerPanel.Visibility = Visibility.Visible;
            ModalOverlayPanel.Visibility = Visibility.Collapsed;
            OverlayLayer.Visibility = Visibility.Collapsed;
            _overlayMode = OverlayPresentationMode.None;
        }

        if (_overlayMode == OverlayPresentationMode.Modal)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
            fadeOut.Completed += (_, _) => CompleteClose();
            ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            var slideOut = new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
            return;
        }

        double w = DrawerPanel.ActualWidth > 0 ? DrawerPanel.ActualWidth : DrawerPanel.Width;
        var currentMargin = DrawerPanel.Margin;
        var drawerSlideOut = new ThicknessAnimation(
            currentMargin,
            new Thickness(w, 0, 0, 0),
            TimeSpan.FromMilliseconds(250))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.HoldEnd
        };
        drawerSlideOut.Completed += (_, _) => CompleteClose();
        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, drawerSlideOut);

        var drawerFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, drawerFadeOut);
    }

    /// <summary>Принудительно закрывает все оверлеи (drawer + stacked modal) без анимации.</summary>
    public void HideAllOverlays()
    {
        _drawerModalStack.Clear();
        DrawerContentPresenter.Content = null;
        ModalOverlayContentPresenter.Content = null;
        DrawerPanel.BeginAnimation(FrameworkElement.MarginProperty, null);
        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, null);
        OverlayBackdrop.BeginAnimation(UIElement.OpacityProperty, null);
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, null);
        DrawerPanel.Visibility = Visibility.Collapsed;
        ModalOverlayPanel.Visibility = Visibility.Collapsed;
        OverlayLayer.Visibility = Visibility.Collapsed;
        _overlayMode = OverlayPresentationMode.None;
    }

    private void HideStackedModalOnly()
    {
        if (_drawerModalStack.Count == 0)
            return;

        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180));
        fadeOut.Completed += (_, _) =>
        {
            ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, null);
            ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, null);

            if (_drawerModalStack.Count > 0)
                _drawerModalStack.RemoveAt(_drawerModalStack.Count - 1);

            if (_drawerModalStack.Count > 0)
            {
                var prev = _drawerModalStack[^1];
                ModalOverlayContentPresenter.Content = prev;
                ModalOverlayPanel.Visibility = Visibility.Visible;
                ModalOverlayPanel.Opacity = 0;
                ModalOverlayTransform.Y = 8;
                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160));
                ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                var slideIn = new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideIn);
            }
            else
            {
                ModalOverlayContentPresenter.Content = null;
                ModalOverlayPanel.Visibility = Visibility.Collapsed;
                ModalOverlayPanel.Opacity = 0;
                ModalOverlayTransform.Y = 16;
            }
        };
        ModalOverlayPanel.BeginAnimation(UIElement.OpacityProperty, fadeOut);

        var slideOut = new DoubleAnimation(0, 16, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        ModalOverlayTransform.BeginAnimation(TranslateTransform.YProperty, slideOut);
    }

    private void ModalOverlayContentClip_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ModalOverlayPanel.Visibility != Visibility.Visible)
            return;

        if (sender is not Border host) return;
        double w = host.ActualWidth;
        double h = host.ActualHeight;
        if (w <= 0 || h <= 0)
        {
            host.Clip = null;
            return;
        }

        const double radius = 12;
        host.Clip = new RectangleGeometry(new Rect(0, 0, w, h), radius, radius);
    }

    public void ShowPhotoViewer(string filePath, string? displayFileName = null, string? description = null, string? uploadedByName = null, Guid uploadedById = default, byte[]? uploadedByAvatarData = null, string? uploadedByAvatarPath = null, Guid projectId = default, Func<string, string, string?, Task>? savedFileHandler = null)
    {
        var viewer = new Views.Overlays.PhotoViewerOverlay(filePath, displayFileName, description, uploadedByName, uploadedById, uploadedByAvatarData, uploadedByAvatarPath, projectId, savedFileHandler);
        PhotoViewerLayer.Content = viewer;
        PhotoViewerLayer.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        PhotoViewerLayer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public event Action? PhotoViewerClosed;

    public void HidePhotoViewer()
    {
        _photoViewerWasVisible = false;
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) =>
        {
            PhotoViewerLayer.Visibility = Visibility.Collapsed;
            PhotoViewerLayer.Content = null;
            PhotoViewerClosed?.Invoke();
        };
        PhotoViewerLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    public void HideOverlayLayer() => OverlayLayer.Visibility = Visibility.Collapsed;
    public void ShowOverlayLayer() => OverlayLayer.Visibility = Visibility.Visible;

    public async System.Threading.Tasks.Task HideOverlayLayerAnimatedAsync()
    {
        if (OverlayLayer.Visibility != Visibility.Visible) return;

        var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) =>
        {
            OverlayLayer.Visibility = Visibility.Collapsed;
            tcs.TrySetResult(true);
        };
        OverlayLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        await tcs.Task;
    }

    public void ShowOverlayLayerAnimated()
    {
        if (OverlayLayer.Visibility == Visibility.Visible) return;

        OverlayLayer.Opacity = 0;
        OverlayLayer.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        OverlayLayer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public void HidePhotoViewerTemporarily()
    {
        if (PhotoViewerLayer.Visibility == Visibility.Visible)
        {
            _photoViewerWasVisible = true;
            PhotoViewerLayer.Visibility = Visibility.Collapsed;
        }
    }

    public void RestorePhotoViewerVisibility()
    {
        if (_photoViewerWasVisible && PhotoViewerLayer.Content != null)
        {
            PhotoViewerLayer.Visibility = Visibility.Visible;
        }
    }

    public void ShowDocumentViewer(string filePath, string? displayFileName = null, string? description = null, Func<string, string, string?, Task>? savedFileHandler = null)
    {
        var viewer = new Views.Overlays.DocumentViewerOverlay(filePath, displayFileName, description, savedFileHandler);
        DocumentViewerLayer.Content = viewer;
        DocumentViewerLayer.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        DocumentViewerLayer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
    }

    public void HideDocumentViewer()
    {
        var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        fadeOut.Completed += (_, _) =>
        {
            DocumentViewerLayer.Visibility = Visibility.Collapsed;
            DocumentViewerLayer.Content = null;
        };
        DocumentViewerLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    public void HideDocumentViewerTemporarily()
    {
        if (DocumentViewerLayer.Visibility == Visibility.Visible)
        {
            _documentViewerWasVisible = true;
            DocumentViewerLayer.Visibility = Visibility.Collapsed;
        }
    }

    public void RestoreDocumentViewerVisibility()
    {
        if (_documentViewerWasVisible && DocumentViewerLayer.Content != null)
        {
            DocumentViewerLayer.Visibility = Visibility.Visible;
        }
    }

    private void Backdrop_Click(object sender, MouseButtonEventArgs e)
        => HideDrawer();
}
