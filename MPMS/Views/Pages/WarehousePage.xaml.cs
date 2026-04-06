using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class WarehousePage : UserControl
{
    private readonly ScrollViewer? _mainListScroll;

    private WarehouseViewModel? Vm => DataContext as WarehouseViewModel;

    public WarehousePage()
    {
        InitializeComponent();
        _mainListScroll = FindName("MainListScroll") as ScrollViewer;
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag && Vm is { } vm)
            vm.ActiveTab = tag;
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
        if (Vm is { } vm) vm.SearchText = string.Empty;
    }

    private void FilterBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FormComboHelpers.IsMouseWheelOverOpenComboBox(e))
            return;
        if (_mainListScroll is null) return;
        var next = _mainListScroll.VerticalOffset - e.Delta;
        next = Math.Max(0, Math.Min(next, _mainListScroll.ScrollableHeight));
        _mainListScroll.ScrollToVerticalOffset(next);
        e.Handled = true;
    }

    private void MaterialRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (sender is Border { Tag: LocalMaterial material } && MainWindow.Instance is { } mw)
        {
            var overlay = new MaterialDetailOverlay(material, vm);
            mw.ShowDrawer(overlay, 560);
        }
    }

    private void EquipmentRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (sender is Border { Tag: LocalEquipment equipment } && MainWindow.Instance is { } mw)
        {
            var overlay = new EquipmentDetailOverlay(equipment, vm);
            mw.ShowDrawer(overlay, 560);
        }
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (MainWindow.Instance is not { } mw) return;

        var overlay = new CreateWarehouseItemOverlay(
            vm.ActiveTab == "Equipment" ? "Equipment" : "Material",
            vm,
            vm.MaterialCategories.ToList(),
            vm.EquipmentCategories.ToList());

        mw.ShowCenteredOverlay(overlay, 560);
    }
}
