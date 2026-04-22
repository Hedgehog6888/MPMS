using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Overlays;


namespace MPMS.Views.Pages;

public partial class WarehousePage : UserControl
{
    private WarehouseViewModel? Vm => DataContext as WarehouseViewModel;

    public WarehousePage()
    {
        InitializeComponent();
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
