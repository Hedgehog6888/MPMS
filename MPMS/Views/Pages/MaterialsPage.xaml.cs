using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Dialogs;

namespace MPMS.Views.Pages;

public partial class MaterialsPage : UserControl
{
    public MaterialsPage()
    {
        InitializeComponent();
    }

    private MaterialsViewModel? VM => DataContext as MaterialsViewModel;


    private async void CreateMaterial_Click(object sender, RoutedEventArgs e)
    {
        var dialog = App.Services.GetRequiredService<CreateMaterialDialog>();
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true && dialog.Result is not null && VM is not null)
        {
            var id = Guid.NewGuid();
            await VM.SaveNewMaterialAsync(dialog.Result, id);
        }
    }

    private async void EditMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalMaterial material || VM is null)
            return;

        var dialog = App.Services.GetRequiredService<CreateMaterialDialog>();
        dialog.Owner = Window.GetWindow(this);
        dialog.SetEditMode(material);

        if (dialog.ShowDialog() == true && dialog.UpdateResult is not null)
            await VM.SaveUpdatedMaterialAsync(material.Id, dialog.UpdateResult);
    }

    private async void DeleteMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalMaterial material || VM is null)
            return;

        var owner = Window.GetWindow(this);
        if (MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(owner, "Материал", material.Name))
            await VM.DeleteMaterialCommand.ExecuteAsync(material);
    }
}
