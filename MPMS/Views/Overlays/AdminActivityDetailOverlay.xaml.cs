using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.Views.Overlays;

public partial class AdminActivityDetailOverlay : UserControl
{
    private readonly LocalActivityLog _log;

    public AdminActivityDetailOverlay(LocalActivityLog log)
    {
        InitializeComponent();
        _log = log;
        Loaded += (_, _) => LoadData();
    }

    private void LoadData()
    {
        // Avatar
        if (_log.AvatarData is { Length: > 0 })
        {
            var src = AvatarHelper.GetImageSource(_log.AvatarData, _log.AvatarPath, _log.UserName ?? "User");
            AvatarImage.Source = src;
            AvatarInitials.Visibility = Visibility.Collapsed;
        }
        else
        {
            AvatarImage.Source = null;
            AvatarInitials.Text = _log.UserInitials ?? "??";
            AvatarInitials.Visibility = Visibility.Visible;
            AvatarBorder.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(_log.UserColor ?? "#1B6EC2"));
        }

        // User info
        UserNameText.Text = _log.UserName ?? "Неизвестный пользователь";
        UserRoleText.Text = _log.ActorRole ?? "—";

        // Action type badge - use ActivityLogToAdminActivityBrushConverter for admin activity colors
        var brush = ActivityLogToAdminActivityBrushConverter.Instance.Convert(_log, typeof(SolidColorBrush), string.Empty, System.Globalization.CultureInfo.InvariantCulture) as SolidColorBrush;
        ActionBadge.Background = brush ?? new SolidColorBrush(Colors.Gray);
        
        var actionKind = _log.ActionType ?? "Unknown";
        var actionLabel = ActivityDetailsService.GetActionDisplay(actionKind);
        ActionTypeText.Text = actionLabel;
        ActionTypeText.Foreground = Brushes.White;

        // Operation info
        ActionKindText.Text = actionKind;
        CreatedAtText.Text = _log.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
        EntityTypeText.Text = _log.EntityType ?? "—";
        EntityIdText.Text = _log.EntityId != Guid.Empty 
            ? _log.EntityId.ToString()[..8] + "…" 
            : "—";

        // Description
        ActionDescriptionText.Text = _log.ActionText ?? "Нет описания";

        // Details
        if (!string.IsNullOrWhiteSpace(_log.DetailsText))
        {
            DetailsHeader.Visibility = Visibility.Visible;
            DetailsBorder.Visibility = Visibility.Visible;
            DetailsText.Text = _log.DetailsText;
        }
        else
        {
            DetailsHeader.Visibility = Visibility.Collapsed;
            DetailsBorder.Visibility = Visibility.Collapsed;
        }

        // Tooltip details
        var tooltipLines = ActivityDetailsService.GetTooltipDetailLines(_log);
        if (tooltipLines.Count > 0)
        {
            TooltipHeader.Visibility = Visibility.Visible;
            TooltipDetailLinesList.Visibility = Visibility.Visible;
            TooltipDetailLinesList.ItemsSource = tooltipLines;
        }
        else
        {
            TooltipHeader.Visibility = Visibility.Collapsed;
            TooltipDetailLinesList.Visibility = Visibility.Collapsed;
        }

        // Header subtitle
        HeaderSubtitle.Text = $"ID: {_log.Id.ToString()[..8]}…";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }
}
