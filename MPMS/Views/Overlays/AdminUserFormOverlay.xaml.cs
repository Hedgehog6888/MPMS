using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

// Alias to avoid confusion with WPF's own PasswordBox (already imported via System.Windows.Controls)
using LocalAuthSession = MPMS.Models.AuthSession;

namespace MPMS.Views.Overlays;

public partial class AdminUserFormOverlay : UserControl
{
    private AdminViewModel? _adminVm;
    private AdminUserRow?   _editingRow;
    private bool            _isEditMode;

    public AdminUserFormOverlay()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadRolesAsync();
    }

    // ── Setup ─────────────────────────────────────────────────────────────

    public void SetCreateMode(AdminViewModel vm)
    {
        _adminVm   = vm;
        _isEditMode = false;
        TitleLabel.Text    = "Создать пользователя";
        SubtitleLabel.Text = "Заполните данные нового пользователя";
        PasswordHint.Visibility = Visibility.Collapsed;
    }

    public void SetEditMode(AdminViewModel vm, AdminUserRow row)
    {
        _adminVm    = vm;
        _isEditMode = true;
        _editingRow = row;
        TitleLabel.Text    = "Редактировать пользователя";
        SubtitleLabel.Text = $"Изменение данных: {row.Name}";
        PasswordHint.Visibility = Visibility.Visible;

        FirstNameBox.Text = row.FirstName;
        LastNameBox.Text  = row.LastName;
        UsernameBox.Text  = row.Username;
        EmailBox.Text     = row.Email;
    }

    // Fallback roles used when the local Roles table has not been populated by sync
    private static readonly List<RoleItem> _defaultRoles =
    [
        new() { Id = new Guid("10000001-0000-0000-0000-000000000000"), Name = "Administrator",  Display = "Администратор" },
        new() { Id = new Guid("10000002-0000-0000-0000-000000000000"), Name = "ProjectManager", Display = "Менеджер" },
        new() { Id = new Guid("10000003-0000-0000-0000-000000000000"), Name = "Foreman",        Display = "Прораб" },
        new() { Id = new Guid("10000004-0000-0000-0000-000000000000"), Name = "Worker",         Display = "Работник" },
    ];

    private async Task LoadRolesAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var dbRoles = await db.Roles.OrderBy(r => r.Name).ToListAsync();
        List<RoleItem> items = dbRoles.Count > 0
            ? dbRoles.Select(r => new RoleItem { Id = r.Id, Name = r.Name, Display = GetRoleDisplay(r.Name) }).ToList()
            : _defaultRoles;

        RoleCombo.ItemsSource = items;

        if (_isEditMode && _editingRow is not null)
            RoleCombo.SelectedItem = items.FirstOrDefault(r =>
                r.Id == _editingRow.RoleId ||
                string.Equals(r.Name, _editingRow.RoleName, StringComparison.OrdinalIgnoreCase));
        else
            RoleCombo.SelectedIndex = 0;
    }

    // ── Actions ───────────────────────────────────────────────────────────

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        HideError();
        SaveButton.IsEnabled = false;

        try
        {
            var firstName = FirstNameBox.Text.Trim();
            var lastName  = LastNameBox.Text.Trim();
            var username  = UsernameBox.Text.Trim();
            var email     = EmailBox.Text.Trim();
            var password  = PasswordBox.Password;
            var passConfirm = PasswordConfirmBox.Password;
            var role      = RoleCombo.SelectedItem as RoleItem;

            // Validation
            if (string.IsNullOrWhiteSpace(firstName))  { ShowError("Введите имя"); return; }
            if (string.IsNullOrWhiteSpace(lastName))   { ShowError("Введите фамилию"); return; }
            if (string.IsNullOrWhiteSpace(username))   { ShowError("Введите логин"); return; }
            if (role is null)                          { ShowError("Выберите роль"); return; }
            if (!_isEditMode && string.IsNullOrWhiteSpace(password)) { ShowError("Введите пароль"); return; }
            if (!string.IsNullOrEmpty(password) && password != passConfirm) { ShowError("Пароли не совпадают"); return; }

            var fullName = $"{firstName} {lastName}".Trim();

            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            if (_isEditMode && _editingRow is not null)
            {
                var user = await db.Users.FindAsync(_editingRow.Id);
                if (user is null) { ShowError("Пользователь не найден"); return; }

                // Check username uniqueness (excluding current user)
                if (await db.Users.AnyAsync(u => u.Username == username && u.Id != user.Id))
                { ShowError("Пользователь с таким логином уже существует"); return; }

                user.Name      = fullName;
                user.Username  = username;
                user.Email     = email;
                user.RoleId    = role.Id;
                user.RoleName  = role.Name;
                user.LastModifiedLocally = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(password))
                {
                    var hash = BCrypt.Net.BCrypt.HashPassword(password);
                    await AdminViewModel.UpdatePasswordHashAsync(db, user.Id, hash);
                    _adminVm?.AddAdminLog(db, ActivityActionKind.PasswordChanged,
                        $"Изменил пароль пользователя {fullName}", "User", user.Id);
                }

                _adminVm?.AddAdminLog(db, ActivityActionKind.UserEdited,
                    $"Изменил данные пользователя {fullName} ({username})", "User", user.Id);
            }
            else
            {
                if (await db.Users.AnyAsync(u => u.Username == username))
                { ShowError("Пользователь с таким логином уже существует"); return; }

                var avatarData = AvatarHelper.GenerateInitialsAvatar(fullName);
                var newUser = new LocalUser
                {
                    Id           = Guid.NewGuid(),
                    Name         = fullName,
                    Username     = username,
                    Email        = email,
                    RoleId       = role.Id,
                    RoleName     = role.Name,
                    AvatarData   = avatarData,
                    CreatedAt    = DateTime.UtcNow,
                    LastModifiedLocally = DateTime.UtcNow
                };
                db.Users.Add(newUser);

                var hash = BCrypt.Net.BCrypt.HashPassword(password);
                db.AuthSessions.Add(new LocalAuthSession
                {
                    UserId             = newUser.Id,
                    LocalPasswordHash  = hash,
                });

                _adminVm?.AddAdminLog(db, ActivityActionKind.UserCreated,
                    $"Создал пользователя {fullName} ({username}) с ролью {role.Display}", "User", newUser.Id);
            }

            await db.SaveChangesAsync();
            if (_adminVm is not null) await _adminVm.RefreshAfterUserChangeAsync();
            MainWindow.Instance?.HideDrawer();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка сохранения: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        ErrorText.Text     = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorBorder.Visibility = Visibility.Collapsed;

    private static string GetRoleDisplay(string roleName) => roleName switch
    {
        "Administrator" or "Admin"                          => "Администратор",
        "Project Manager" or "ProjectManager" or "Manager" => "Менеджер",
        "Foreman"                                           => "Прораб",
        "Worker"                                            => "Работник",
        _                                                   => roleName
    };
}
