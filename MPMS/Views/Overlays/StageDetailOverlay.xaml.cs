using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class StageDetailOverlay : UserControl
{
    private LocalTaskStage? _stage;
    private LocalTask? _task;
    private Action? _onClosed;

    public StageDetailOverlay()
    {
        InitializeComponent();
    }

    public void SetStage(StageItem item, LocalTask task, Action? onClosed = null)
    {
        _stage = item.Stage;
        _task = task;
        _onClosed = onClosed;
        StageNameText.Text = item.Stage.Name;
        DescriptionText.Text = item.Stage.Description ?? "—";
        AssigneeText.Text = item.Stage.AssignedUserName ?? "—";

        var statusBrush = StageStatusToBrushConverter.Instance.Convert(item.Stage.Status, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = statusBrush ?? Brushes.Gray;
        StatusText.Text = StageStatusToStringConverter.Instance.Convert(item.Stage.Status, typeof(string), null, CultureInfo.InvariantCulture) as string ?? "—";

        _ = LoadMaterialsAsync();
    }

    private async System.Threading.Tasks.Task LoadMaterialsAsync()
    {
        if (_stage is null) return;
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var mats = await db.StageMaterials
            .Where(sm => sm.StageId == _stage.Id)
            .ToListAsync();
        var items = mats.Select(m => $"{m.MaterialName} — {m.Quantity} {m.Unit}").ToList();
        MaterialsList.ItemsSource = items;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _onClosed?.Invoke();
        MainWindow.Instance?.HideDrawer();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_stage is null || _task is null) return;
        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(_stage, _task, async () =>
        {
            _onClosed?.Invoke();
            MainWindow.Instance?.HideDrawer();
        });
        MainWindow.Instance?.ShowDrawer(overlay);
    }
}
