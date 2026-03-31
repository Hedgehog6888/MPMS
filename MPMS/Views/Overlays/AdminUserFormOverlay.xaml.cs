using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private bool            _loadingRoles;
    private bool            _suppressSubRoleRebuild;

    /// <summary>Состояние выбора доп. специализаций (как в списке работников проекта).</summary>
    private readonly Dictionary<string, bool> _additionalSpecSelected = new(StringComparer.OrdinalIgnoreCase);

    private static bool IsDbWorkerRole(string? roleName) =>
        roleName is "Worker" or "Работник";

    /// <summary>Совпадает с <see cref="WorkerSpecialtiesJson.CanonicalWorkerSpecialties"/> (цвета привязаны к порядку).</summary>
    public static readonly IReadOnlyList<string> WorkerSubRoles = WorkerSpecialtiesJson.CanonicalWorkerSpecialties;

    public AdminUserFormOverlay()
    {
        InitializeComponent();
        SubRoleCombo.ItemsSource = WorkerSubRoles.ToList();
        BuildAdditionalRows(null, null);
        Loaded += async (_, _) => await LoadRolesAsync();
    }

    private void BuildAdditionalRows(string? excludeMain, IEnumerable<string>? restoreChecked)
    {
        var restore = restoreChecked?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var mainNorm = excludeMain?.Trim();
        _additionalSpecSelected.Clear();
        AdditionalSpecsPanel.Children.Clear();
        foreach (var spec in WorkerSubRoles)
        {
            if (!string.IsNullOrEmpty(mainNorm) && string.Equals(spec, mainNorm, StringComparison.OrdinalIgnoreCase))
                continue;
            _additionalSpecSelected[spec] = restore.Contains(spec);
            AdditionalSpecsPanel.Children.Add(CreateAdditionalSpecPickRow(spec));
        }
    }

    private Style? TryFindPickerStyle(string key) =>
        TryFindResource(key) as Style ?? Application.Current.TryFindResource(key) as Style;

    private Border CreateAdditionalSpecPickRow(string spec)
    {
        const double checkColW = 18;
        var selected = _additionalSpecSelected.GetValueOrDefault(spec);
        var checkMark = new Border
        {
            Width = 16,
            Height = 16,
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = selected ? Visibility.Visible : Visibility.Hidden,
            Child = new TextBlock
            {
                Text = "✓",
                FontSize = 8,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var nameBlock = new TextBlock
        {
            Text = spec,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(checkColW) });
        Grid.SetColumn(nameBlock, 0);
        Grid.SetColumn(checkMark, 1);
        grid.Children.Add(nameBlock);
        grid.Children.Add(checkMark);

        var row = new Border
        {
            Style = TryFindPickerStyle("UserPickerItem"),
            Child = grid,
            Tag = spec
        };

        row.MouseEnter += (_, _) => row.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA));
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonDown += (_, e) =>
        {
            var on = !_additionalSpecSelected.GetValueOrDefault(spec);
            _additionalSpecSelected[spec] = on;
            checkMark.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            e.Handled = true;
        };

        return row;
    }

    private void SpecsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer nested)
            return;
        var atTop = nested.VerticalOffset <= 0;
        var atBottom = nested.VerticalOffset >= nested.ScrollableHeight;
        var up = e.Delta > 0;
        var down = e.Delta < 0;
        if ((atTop && up) || (atBottom && down))
        {
            FormMainScrollViewer.ScrollToVerticalOffset(FormMainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private HashSet<string> GetCheckedAdditional() =>
        _additionalSpecSelected.Where(kv => kv.Value).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    private string? GetMainSubRoleFromCombo()
    {
        if (SubRoleCombo.SelectedItem is string s && !string.IsNullOrWhiteSpace(s))
            return s.Trim();
        return null;
    }

    private void SubRoleCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSubRoleRebuild)
            return;
        var main = GetMainSubRoleFromCombo();
        var saved = GetCheckedAdditional();
        BuildAdditionalRows(main, saved);
    }

    private string? CollectAdditionalSubRolesJson(string? mainTrimmed)
    {
        var list = GetCheckedAdditional()
            .Where(s => string.IsNullOrEmpty(mainTrimmed)
                || !string.Equals(s, mainTrimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return WorkerSpecialtiesJson.Serialize(list);
    }

    // ── Setup ─────────────────────────────────────────────────────────────

    public void SetCreateMode(AdminViewModel vm)
    {
        _adminVm    = vm;
        _isEditMode = false;
        _editingRow = null;
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
        BirthDatePicker.SelectedDate = row.BirthDate?.ToDateTime(TimeOnly.MinValue);
        HomeAddressBox.Text = row.HomeAddress ?? string.Empty;
    }

    private void RoleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (RoleCombo.SelectedItem is not RoleItem role)
            return;
        SubRolePanel.Visibility = role.Name == "Worker" ? Visibility.Visible : Visibility.Collapsed;
        if (_loadingRoles || role.Name != "Worker")
            return;
        if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is RoleItem prev && prev.Name != "Worker")
        {
            SubRoleCombo.ItemsSource = WorkerSubRoles.ToList();
            SubRoleCombo.SelectedIndex = -1;
            BuildAdditionalRows(null, null);
        }
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
        _loadingRoles = true;
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
        {
            RoleCombo.SelectedItem = items.FirstOrDefault(r =>
                r.Id == _editingRow.RoleId ||
                string.Equals(r.Name, _editingRow.RoleName, StringComparison.OrdinalIgnoreCase));
            if (IsDbWorkerRole(_editingRow.RoleName))
                SubRolePanel.Visibility = Visibility.Visible;
        }
        else
            RoleCombo.SelectedIndex = 0;

        if (_isEditMode && _editingRow is not null && IsDbWorkerRole(_editingRow.RoleName))
        {
            var rolesList = WorkerSubRoles.ToList();
            var sub = _editingRow.SubRole?.Trim();
            if (!string.IsNullOrWhiteSpace(sub) &&
                !rolesList.Any(s => string.Equals(s, sub, StringComparison.OrdinalIgnoreCase)))
                rolesList.Insert(0, sub);
            SubRoleCombo.ItemsSource = rolesList;
            _suppressSubRoleRebuild = true;
            try
            {
                SubRoleCombo.SelectedItem = rolesList.FirstOrDefault(s =>
                    string.Equals(s, sub, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                _suppressSubRoleRebuild = false;
            }
            var main = GetMainSubRoleFromCombo() ?? sub;
            BuildAdditionalRows(main, WorkerSpecialtiesJson.Deserialize(_editingRow.AdditionalSubRoles));
        }
        else
        {
            SubRoleCombo.ItemsSource = WorkerSubRoles.ToList();
            _suppressSubRoleRebuild = true;
            try
            {
                SubRoleCombo.SelectedIndex = -1;
            }
            finally
            {
                _suppressSubRoleRebuild = false;
            }
            BuildAdditionalRows(null, null);
        }

        _loadingRoles = false;
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
            var role = RoleCombo.SelectedItem as RoleItem;
            DateOnly? birthDate = BirthDatePicker.SelectedDate is { } bd ? DateOnly.FromDateTime(bd) : null;
            var homeAddress = string.IsNullOrWhiteSpace(HomeAddressBox.Text) ? null : HomeAddressBox.Text.Trim();

            // Validation
            if (string.IsNullOrWhiteSpace(firstName))  { ShowError("Введите имя"); return; }
            if (string.IsNullOrWhiteSpace(lastName))   { ShowError("Введите фамилию"); return; }
            if (string.IsNullOrWhiteSpace(username))   { ShowError("Введите логин"); return; }
            if (role is null)                          { ShowError("Выберите роль"); return; }
            if (role.Name == "Worker" && string.IsNullOrWhiteSpace(GetMainSubRoleFromCombo()))
            { ShowError("Выберите основную специализацию работника."); return; }
            if (!_isEditMode && string.IsNullOrWhiteSpace(password)) { ShowError("Введите пароль"); return; }
            if (!string.IsNullOrEmpty(password) && password != passConfirm) { ShowError("Пароли не совпадают"); return; }

            var subRole = role.Name == "Worker" ? GetMainSubRoleFromCombo()?.Trim() : null;
            var additionalJson = role.Name == "Worker" ? CollectAdditionalSubRolesJson(subRole) : null;

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
                user.BirthDate = birthDate;
                user.HomeAddress = homeAddress;
                user.RoleId    = role.Id;
                user.RoleName  = role.Name;
                user.SubRole             = subRole;
                user.AdditionalSubRoles  = additionalJson;
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
                    var updateReq = new UpdateUserRequest(
                        firstName,
                        lastName,
                        username,
                        string.IsNullOrWhiteSpace(email) ? null : email,
                        role.Id,
                        string.IsNullOrEmpty(password) ? null : password,
                        subRole,
                        additionalJson,
                        birthDate,
                        homeAddress);
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
                    BirthDate    = birthDate,
                    HomeAddress  = homeAddress,
                    RoleId       = role.Id,
                    RoleName     = role.Name,
                    SubRole             = subRole,
                    AdditionalSubRoles  = additionalJson,
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
                    var createReq = new CreateUserRequest(
                        firstName,
                        lastName,
                        username,
                        string.IsNullOrWhiteSpace(email) ? null : email,
                        password,
                        role.Id,
                        newId,
                        avatarData,
                        subRole,
                        additionalJson,
                        birthDate,
                        homeAddress);
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
