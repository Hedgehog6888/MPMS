using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using System.Windows;
using System.Windows.Controls;

namespace MPMS.Views.Overlays;

public partial class ChangePasswordOverlay : UserControl
{
    private readonly Guid _userId;
    private readonly string _userName;
    private readonly Func<System.Threading.Tasks.Task>? _onSaved;

    public ChangePasswordOverlay(Guid userId, string userName, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        InitializeComponent();
        _userId = userId;
        _userName = userName;
        _onSaved = onSaved;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        var current = CurrentPasswordBox.Password;
        var next = NewPasswordBox.Password;
        var confirm = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(current))
        { ShowError("Введите текущий пароль."); return; }
        if (string.IsNullOrWhiteSpace(next))
        { ShowError("Введите новый пароль."); return; }
        if (next.Length < 6)
        { ShowError("Новый пароль должен содержать не менее 6 символов."); return; }
        if (next != confirm)
        { ShowError("Новый пароль и подтверждение не совпадают."); return; }

        SaveBtn.IsEnabled = false;
        try
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            var auth = App.Services.GetRequiredService<IAuthService>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var session = await db.AuthSessions.FindAsync(1);
            if (session is null)
            { ShowError("Сессия не найдена."); return; }
            if (!BCrypt.Net.BCrypt.Verify(current, session.LocalPasswordHash))
            { ShowError("Неверный текущий пароль."); return; }

            session.LocalPasswordHash = BCrypt.Net.BCrypt.HashPassword(next);
            var actLog = new LocalActivityLog
            {
                UserId = _userId,
                ActorRole = auth.UserRole,
                UserName = _userName,
                UserInitials = AvatarHelper.GetInitials(_userName),
                UserColor = "#0F2038",
                ActionType = ActivityActionKind.PasswordChanged,
                ActionText = "Изменил пароль своего аккаунта",
                EntityType = "User",
                EntityId = _userId,
                CreatedAt = DateTime.UtcNow
            };
            db.ActivityLogs.Add(actLog);
            await db.SaveChangesAsync();
            await App.Services.GetRequiredService<ISyncService>().QueueLocalActivityLogAsync(actLog);

            if (_onSaved is not null)
                await _onSaved();
            MainWindow.Instance?.HideDrawer();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка: {ex.Message}");
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
