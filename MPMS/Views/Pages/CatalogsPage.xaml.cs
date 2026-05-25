using System.Windows;
using System.Windows.Controls;

namespace MPMS.Views.Pages;

public partial class CatalogsPage : UserControl
{
    public CatalogsPage()
    {
        InitializeComponent();
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;

        // Скрываем все панели
        WorkTypesPanel.Visibility = Visibility.Collapsed;
        WorkTypeCategoriesPanel.Visibility = Visibility.Collapsed;
        EquipmentCategoriesPanel.Visibility = Visibility.Collapsed;
        MaterialCategoriesPanel.Visibility = Visibility.Collapsed;

        // Показываем выбранную панель
        switch (rb.Tag?.ToString())
        {
            case "WorkTypes":
                WorkTypesPanel.Visibility = Visibility.Visible;
                break;
            case "WorkTypeCategories":
                WorkTypeCategoriesPanel.Visibility = Visibility.Visible;
                break;
            case "EquipmentCategories":
                EquipmentCategoriesPanel.Visibility = Visibility.Visible;
                break;
            case "MaterialCategories":
                MaterialCategoriesPanel.Visibility = Visibility.Visible;
                break;
        }
    }
}
