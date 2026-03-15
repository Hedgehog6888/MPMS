using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class ArchiveItemInfoOverlay : UserControl
{
    private readonly ArchiveRow    _row;
    private readonly AdminViewModel _vm;

    public ArchiveItemInfoOverlay(ArchiveRow row, AdminViewModel vm)
    {
        InitializeComponent();
        _row = row;
        _vm  = vm;
        Loaded += async (_, _) => { PopulateUI(); await LoadRelatedAsync(); };
    }

    private void PopulateUI()
    {
        TitleText.Text    = _row.Name;
        SubtitleText.Text = $"Архивный элемент · {GetTypeLabel(_row.EntityType)}";
        StatusText.Text   = _row.StatusText;
        ParentText.Text   = string.IsNullOrWhiteSpace(_row.ParentName) ? "—" : _row.ParentName;
        DeletedByText.Text = string.IsNullOrWhiteSpace(_row.DeletedBy) ? "—" : _row.DeletedBy;
        DeletedAtText.Text = _row.DeletedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        if (!string.IsNullOrWhiteSpace(_row.Description))
        {
            DescriptionText.Text       = _row.Description;
            DescriptionPanel.Visibility = Visibility.Visible;
        }

        // Type badge color
        (TypeBadge.Background, TypeBadgeText.Text, TypeBadgeText.Foreground) = _row.EntityType switch
        {
            "Project" => (
                new SolidColorBrush(Color.FromRgb(0xDB, 0xEA, 0xFF)),
                "Проект",
                (Brush)new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8))),
            "Task" => (
                new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5)),
                "Задача",
                (Brush)new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46))),
            "Stage" => (
                new SolidColorBrush(Color.FromRgb(0xED, 0xE9, 0xFE)),
                "Этап",
                (Brush)new SolidColorBrush(Color.FromRgb(0x5B, 0x21, 0xB6))),
            _ => (
                new SolidColorBrush(Color.FromRgb(0xF1, 0xF3, 0xF5)),
                _row.EntityType,
                (Brush)new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)))
        };

        // Restore info & related panel
        switch (_row.EntityType)
        {
            case "Project":
                ParentLabel.Text     = "Клиент";
                RestoreInfoText.Text = "При восстановлении будут восстановлены все задачи и этапы проекта.";
                RelatedPanel.Visibility  = Visibility.Visible;
                SubTabTasks.Visibility   = Visibility.Visible;
                SubTabStages.Visibility  = Visibility.Visible;
                break;
            case "Task":
                ParentLabel.Text     = "Проект";
                RestoreInfoText.Text = "При восстановлении будут восстановлены все этапы задачи.";
                RelatedPanel.Visibility  = Visibility.Visible;
                SubTabBar.Visibility     = Visibility.Collapsed;
                RelatedTasksList.Visibility  = Visibility.Collapsed;
                RelatedStagesList.Visibility = Visibility.Visible;
                RelatedSectionTitle.Text = "СВЯЗАННЫЕ ЭТАПЫ";
                break;
            default:
                RestoreInfoText.Text = "Этап будет восстановлен без связанных элементов.";
                break;
        }
    }

    private async Task LoadRelatedAsync()
    {
        if (_row.EntityType != "Project" && _row.EntityType != "Task") return;

        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (_row.EntityType == "Project")
        {
            var tasks = await db.Tasks
                .Where(t => t.ProjectId == _row.Id && t.IsArchived)
                .OrderBy(t => t.Name)
                .ToListAsync();

            var taskIds    = tasks.Select(t => t.Id).ToList();
            var stages     = await db.TaskStages
                .Where(s => taskIds.Contains(s.TaskId) && s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            var taskNamesById = tasks.ToDictionary(t => t.Id, t => t.Name);

            Dispatcher.Invoke(() =>
            {
                RelatedTasksList.Children.Clear();
                foreach (var t in tasks)
                    RelatedTasksList.Children.Add(CreateRelatedRow("Задача", t.Name, t.Status.ToString(),
                        Color.FromRgb(0xD1, 0xFA, 0xE5), Color.FromRgb(0x06, 0x5F, 0x46)));

                RelatedStagesList.Children.Clear();
                foreach (var s in stages)
                {
                    var taskName = taskNamesById.GetValueOrDefault(s.TaskId, "—");
                    RelatedStagesList.Children.Add(CreateRelatedRow("Этап", s.Name, taskName,
                        Color.FromRgb(0xED, 0xE9, 0xFE), Color.FromRgb(0x5B, 0x21, 0xB6)));
                }

                if (tasks.Count == 0 && stages.Count == 0)
                    RelatedEmptyText.Visibility = Visibility.Visible;
            });
        }
        else if (_row.EntityType == "Task")
        {
            var stages = await db.TaskStages
                .Where(s => s.TaskId == _row.Id && s.IsArchived)
                .OrderBy(s => s.Name)
                .ToListAsync();

            Dispatcher.Invoke(() =>
            {
                RelatedStagesList.Visibility = Visibility.Visible;
                RelatedTasksList.Visibility  = Visibility.Collapsed;
                RelatedStagesList.Children.Clear();
                foreach (var s in stages)
                    RelatedStagesList.Children.Add(CreateRelatedRow("Этап", s.Name, s.Status.ToString(),
                        Color.FromRgb(0xED, 0xE9, 0xFE), Color.FromRgb(0x5B, 0x21, 0xB6)));

                if (stages.Count == 0)
                    RelatedEmptyText.Visibility = Visibility.Visible;
            });
        }
    }

    private static Border CreateRelatedRow(string type, string name, string sub, Color badgeBg, Color badgeFg)
    {
        var badge = new Border
        {
            Background  = new SolidColorBrush(badgeBg),
            CornerRadius = new CornerRadius(4),
            Padding     = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text       = type,
                FontSize   = 10,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(badgeFg)
            }
        };

        var nameText = new TextBlock
        {
            Text       = name,
            FontSize   = 13,
            Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x4D)),
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin     = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var subText = new TextBlock
        {
            Text       = sub,
            FontSize   = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin     = new Thickness(8, 0, 0, 0)
        };

        var sp = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(nameText);
        sp.Children.Add(subText);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(badge, 0);
        Grid.SetColumn(sp, 1);
        row.Children.Add(badge);
        row.Children.Add(sp);

        return new Border
        {
            Background   = new SolidColorBrush(Color.FromRgb(0xF4, 0xF5, 0xF7)),
            CornerRadius = new CornerRadius(8),
            Padding      = new Thickness(12, 10, 12, 10),
            Margin       = new Thickness(0, 0, 0, 4),
            Child        = row
        };
    }

    private void SubTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb)
        {
            bool showTasks = rb == SubTabTasks;
            RelatedTasksList.Visibility  = showTasks ? Visibility.Visible : Visibility.Collapsed;
            RelatedStagesList.Visibility = showTasks ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
        switch (_row.EntityType)
        {
            case "Project": _vm.OpenRestoreProjectConfirmCommand.Execute(_row); break;
            case "Task":    _vm.OpenRestoreTaskConfirmCommand.Execute(_row);    break;
            case "Stage":   _vm.OpenRestoreStageConfirmCommand.Execute(_row);   break;
        }
    }

    private static string GetTypeLabel(string entityType) => entityType switch
    {
        "Project" => "Проект",
        "Task"    => "Задача",
        "Stage"   => "Этап",
        _         => entityType
    };
}
