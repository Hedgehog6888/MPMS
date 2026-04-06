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

    private void FilterBar_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FormComboHelpers.IsMouseWheelOverOpenComboBox(e))
            return;
        if (MainListScroll is null) return;
        var next = MainListScroll.VerticalOffset - e.Delta;
        next = Math.Max(0, Math.Min(next, MainListScroll.ScrollableHeight));
        MainListScroll.ScrollToVerticalOffset(next);
        e.Handled = true;
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
