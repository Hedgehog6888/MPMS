using System;
using System.Collections.Generic;
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
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS;

public partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    /// <summary>Ширина drawer только карточки задачи или этапа (без левой панели).</summary>
    public const double TaskOrStageDetailDrawerWidth = 700;

    /// <summary>Сводка слева (300) + карточка задачи/этапа (700).</summary>
    public const double TaskOrStageDetailWithLeftTotalWidth = 1000;

    /// <summary>Центрированные формы создания/редактирования (как материал/оборудование).</summary>
    public const double CenteredFormOverlayWidth = 560;

    /// <summary>Форма проекта с блоком команды — чуть шире.</summary>
    public const double CenteredProjectFormOverlayWidth = 640;

    private enum OverlayPresentationMode { None, Drawer, Modal }
    private OverlayPresentationMode _overlayMode = OverlayPresentationMode.None;

    private readonly List<UIElement> _drawerModalStack = [];

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Instance = this;
    }

    /// <summary>Открыт ли поверх drawer стековый модал (для закрытия только его, без drawer).</summary>
    public bool HasStackedModalOverDrawer => _drawerModalStack.Count > 0;

    public void ShowDrawer(UIElement content, double width = 520)
    {
        _drawerModalStack.Clear();
        // Detach previous content first to avoid "child must be detached from parent Visual" when the same or related element is reparented
        DrawerContentPresenter.Content = null;
        ModalOverlayContentPresenter.Content = null;
        DrawerContentPresenter.Content = content;
        DrawerPanel.Width = width;
        _overlayMode = OverlayPresentationMode.Drawer;

        // Clear any held animations before setting local values
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

    /// <summary>Shows a dual-panel drawer: left panel (e.g. project context) + right panel (e.g. task detail).</summary>
    public void ShowDrawer(UIElement? leftContent, UIElement rightContent, double totalWidth = 1000)
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
            // Обновить данные текущей страницы при закрытии drawer (проект, задачи и т.д.)
            if (DataContext is MainViewModel mainVm && mainVm.CurrentPageViewModel is ILoadable loadable)
                _ = loadable.LoadAsync();
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
        if (DataContext is MainViewModel mainVm && mainVm.CurrentPageViewModel is ILoadable loadable)
            _ = loadable.LoadAsync();
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

    private void Backdrop_Click(object sender, MouseButtonEventArgs e)
        => HideDrawer();
}
