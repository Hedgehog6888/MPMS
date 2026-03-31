using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class AdminUserInfoOverlay : UserControl
{
    private AdminUserRow  _row;
    private AdminViewModel _adminVm;

    public AdminUserInfoOverlay(AdminUserRow row, AdminViewModel adminVm)
    {
        InitializeComponent();
        _row     = row;
        _adminVm = adminVm;
        Loaded  += async (_, _) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        // Avatar
        AvatarBorder.Background = new InitialsToBrushConverter().Convert(_row.Initials, typeof(Brush), parameter: null!, System.Globalization.CultureInfo.CurrentCulture) as Brush
                                   ?? new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2));
        if (_row.AvatarData is { Length: > 0 })
        {
            var src = AvatarHelper.GetImageSource(_row.AvatarData, _row.AvatarPath, _row.Name);
            AvatarImage.Source    = src;
            AvatarInitials.Visibility = Visibility.Collapsed;
        }
        else
        {
            AvatarImage.Source    = null;
            AvatarInitials.Text   = _row.Initials;
            AvatarInitials.Visibility = Visibility.Visible;
        }

        // Name / username / role
        FullNameText.Text   = _row.Name;
        UsernameText.Text   = $"@{_row.Username}";
        RoleBadgeText.Text  = _row.RoleDisplay;
        RoleBadge.Background   = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_row.RoleColor));
        RoleBadgeText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(_row.RoleForeground));
        var isWorker = _row.RoleName is "Worker" or "Работник";
        RoleText.Text = isWorker ? "Работник" : _row.RoleDisplay;

        BadgesRowWrap.Children.Clear();
        BadgesRowWrap.Children.Add(RoleBadge);
        if (isWorker)
        {
            var main = _row.SubRole?.Trim();
            var extras = WorkerSpecialtiesJson.Deserialize(_row.AdditionalSubRoles)
                .Where(s => !string.IsNullOrWhiteSpace(s) &&
                            !string.Equals(s.Trim(), main, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Trim())
                .ToList();
            void AddNamedBadge(string label)
            {
                var bg = WorkerSpecialtiesJson.BadgeBackgroundRgbForSpecName(label);
                var fg = WorkerSpecialtiesJson.BadgeForegroundRgbForSpecName(label);
                BadgesRowWrap.Children.Add(CreateSpecBadge(label,
                    new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B)),
                    new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B))));
            }

            if (!string.IsNullOrWhiteSpace(main))
                AddNamedBadge(main);
            else
                AddNamedBadge("Работник");

            foreach (var ex in extras)
                AddNamedBadge(ex);
        }

        // Status
        if (_row.IsBlocked)
        {
            StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            StatusDot.Fill       = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44));
            StatusText.Text      = "Заблокирован";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x99, 0x1B, 0x1B));
            BlockReasonText.Text = string.IsNullOrWhiteSpace(_row.BlockedReason) ? string.Empty : $"· {_row.BlockedReason}";
            BlockButton.Content  = "Разблокировать";
        }
        else
        {
            StatusBar.Background = new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
            StatusDot.Fill       = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81));
            StatusText.Text      = "Активен";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
            BlockReasonText.Text = string.Empty;
            BlockButton.Content  = "Заблокировать";
        }

        // Contact info
        EmailText.Text     = string.IsNullOrWhiteSpace(_row.Email) ? "—" : _row.Email;
        CreatedAtText.Text = _row.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy");

        HeaderSubtitle.Text = $"ID: {_row.Id.ToString()[..8]}…";

        // Load last login + recent activity
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var lastLogin = await db.ActivityLogs
            .Where(l => l.UserId == _row.Id && l.ActionType == ActivityActionKind.Login)
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();
        LastLoginText.Text = lastLogin is not null ? lastLogin.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : "Нет данных";

        var recentActivity = await db.ActivityLogs
            .Where(l => l.UserId == _row.Id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(5)
            .ToListAsync();
        ActivityList.ItemsSource = recentActivity;
        NoActivityText.Visibility = recentActivity.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        // Replace drawer content directly without HideDrawer (avoids animation conflict)
        var overlay = new AdminUserFormOverlay();
        overlay.SetEditMode(_adminVm, _row);
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void Block_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
        _adminVm.OpenBlockOverlayCommand.Execute(_row);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
        _adminVm.OpenDeleteUserConfirmCommand.Execute(_row);
    }

    private static Border CreateSpecBadge(string text, Brush background, Brush foreground) =>
        new()
        {
            Background     = background,
            CornerRadius   = new CornerRadius(4),
            Padding        = new Thickness(8, 3, 8, 3),
            Margin         = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text         = text,
                FontSize     = 11,
                FontWeight   = FontWeights.SemiBold,
                Foreground   = foreground,
            },
        };
}
