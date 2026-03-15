using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

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

    // Fallback roles — IDs must match API (ApplicationDbContext seed)
    private static readonly List<RoleItem> _defaultRoles =
    [
        new() { Id = new Guid("10000000-0000-0000-0000-000000000001"), Name = "Administrator",   Display = "Администратор" },
        new() { Id = new Guid("10000000-0000-0000-0000-000000000002"), Name = "Project Manager", Display = "Менеджер" },
        new() { Id = new Guid("10000000-0000-0000-0000-000000000003"), Name = "Foreman",         Display = "Прораб" },
        new() { Id = new Guid("10000000-0000-0000-0000-000000000004"), Name = "Worker",          Display = "Работник" },
    ];

    private async Task LoadRolesAsync()
    {
        var api = App.Services.GetRequiredService<IApiService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (api.IsOnline)
        {
            var apiRoles = await api.GetRolesAsync();
            if (apiRoles is not null && apiRoles.Count > 0)
            {
                foreach (var r in apiRoles)
                {
                    var existing = await db.Roles.FindAsync(r.Id);
                    if (existing is null)
                        db.Roles.Add(new LocalRole { Id = r.Id, Name = r.Name, Description = r.Description });
                    else
                        existing.Name = r.Name;
                }
                await db.SaveChangesAsync();
            }
        }

        var dbRoles = await db.Roles.OrderBy(r => r.Name).ToListAsync();
        if (dbRoles.Count == 0)
        {
            foreach (var r in _defaultRoles)
            {
                db.Roles.Add(new LocalRole { Id = r.Id, Name = r.Name, Description = r.Display });
            }
            await db.SaveChangesAsync();
            dbRoles = await db.Roles.OrderBy(r => r.Name).ToListAsync();
        }
        var items = dbRoles.Select(r => new RoleItem { Id = r.Id, Name = r.Name, Display = GetRoleDisplay(r.Name) }).ToList();

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
            var api = App.Services.GetRequiredService<IApiService>();
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            if (_isEditMode && _editingRow is not null)
            {
                var user = await db.Users.FindAsync(_editingRow.Id);
                if (user is null) { ShowError("Пользователь не найден"); return; }

                if (await db.Users.AnyAsync(u => u.Username == username && u.Id != user.Id))
                { ShowError("Пользователь с таким логином уже существует"); return; }

                user.Name      = fullName;
                user.FirstName = firstName;
                user.LastName  = lastName;
                user.Username  = username;
                user.Email     = string.IsNullOrWhiteSpace(email) ? null : email;
                user.RoleId    = role.Id;
                user.RoleName  = role.Name;
                user.LastModifiedLocally = DateTime.UtcNow;

                if (!string.IsNullOrEmpty(password))
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                    _adminVm?.AddAdminLog(db, ActivityActionKind.PasswordChanged,
                        $"Изменил пароль пользователя {fullName}", "User", user.Id);
                }

                _adminVm?.AddAdminLog(db, ActivityActionKind.UserEdited,
                    $"Изменил данные пользователя {fullName} ({username})", "User", user.Id);

                await db.SaveChangesAsync();

                if (api.IsOnline)
                {
                    var updateReq = new UpdateUserRequest(firstName, lastName, username, string.IsNullOrWhiteSpace(email) ? null : email, role.Id, string.IsNullOrEmpty(password) ? null : password);
                    var apiResult = await api.UpdateUserAsync(user.Id, updateReq);
                    if (apiResult is null)
                    {
                        ShowError("Сохранено локально. Не удалось сохранить на сервер. Проверьте, что API запущен.");
                        return;
                    }
                }
            }
            else
            {
                if (await db.Users.AnyAsync(u => u.Username == username))
                { ShowError("Пользователь с таким логином уже существует"); return; }

                var newId = Guid.NewGuid();
                var avatarData = AvatarHelper.GenerateInitialsAvatar(fullName);
                var hash = BCrypt.Net.BCrypt.HashPassword(password);
                var newUser = new LocalUser
                {
                    Id           = newId,
                    Name         = fullName,
                    FirstName    = firstName,
                    LastName     = lastName,
                    Username     = username,
                    Email        = string.IsNullOrWhiteSpace(email) ? null : email,
                    RoleId       = role.Id,
                    RoleName     = role.Name,
                    PasswordHash = hash,
                    AvatarData   = avatarData,
                    CreatedAt    = DateTime.UtcNow,
                    LastModifiedLocally = DateTime.UtcNow
                };
                db.Users.Add(newUser);

                _adminVm?.AddAdminLog(db, ActivityActionKind.UserCreated,
                    $"Создал пользователя {fullName} ({username}) с ролью {role.Display}", "User", newUser.Id);

                await db.SaveChangesAsync();

                if (api.IsOnline)
                {
                    var createReq = new CreateUserRequest(firstName, lastName, username, string.IsNullOrWhiteSpace(email) ? null : email, password, role.Id, newId);
                    var apiResult = await api.CreateUserAsync(createReq);
                    if (apiResult is null)
                    {
                        ShowError("Сохранено локально. Не удалось сохранить на сервер. Проверьте, что API запущен.");
                        return;
                    }
                }
            }

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
