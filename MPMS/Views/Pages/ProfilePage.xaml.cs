using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.Views.Overlays;

#pragma warning disable CS0618

namespace MPMS.Views.Pages;

public partial class ProfilePage : UserControl
{
    private LocalUser? _user;
    private DispatcherTimer? _copyToastHideTimer;
    private bool _copyToastActive;
    private bool _isBackgroundSyncInProgress;

    private static string AvatarDirectory =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MPMS", "Avatars");

    public ProfilePage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await LoadUserAsync();
        };
        EmailRow.MouseLeftButtonDown += (_, e) => CopyToClipboard(EmailText.Text, e);
        LoginRow.MouseLeftButtonDown += (_, e) => CopyToClipboard(LoginText.Text, e);
    }

    private static readonly Dictionary<string, (string label, string desc, string[] perms)> RoleInfo = new()
    {
        ["Administrator"] = ("Администратор",
            "Полный доступ ко всем функциям системы",
            ["Создание и удаление проектов", "Управление пользователями", "Просмотр всех данных", "Управление ролями"]),
        ["Admin"] = ("Администратор",
            "Полный доступ ко всем функциям системы",
            ["Создание и удаление проектов", "Управление пользователями", "Просмотр всех данных", "Управление ролями"]),
        ["Project Manager"] = ("Менеджер",
            "Управление проектами и командой исполнителей",
            ["Создание проектов", "Назначение задач", "Просмотр прогресса", "Добавление членов команды"]),
        ["ProjectManager"] = ("Менеджер",
            "Управление проектами и командой исполнителей",
            ["Создание проектов", "Назначение задач", "Просмотр прогресса", "Добавление членов команды"]),
        ["Manager"] = ("Менеджер",
            "Управление проектами и командой исполнителей",
            ["Создание проектов", "Назначение задач", "Просмотр прогресса", "Добавление членов команды"]),
        ["Foreman"] = ("Прораб",
            "Руководство монтажными работами на объекте",
            ["Просмотр своих проектов", "Управление этапами", "Отчёт о выполнении", "Добавление материалов"]),
        ["Worker"] = ("Работник",
            "Выполнение монтажных работ на объекте",
            ["Просмотр назначенных задач", "Обновление статусов этапов", "Добавление материалов"]),
    };

    private async System.Threading.Tasks.Task LoadUserAsync()
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var sync = App.Services.GetRequiredService<ISyncService>();
        var api = App.Services.GetRequiredService<IApiService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync();

        if (auth.UserId.HasValue)
            _user = await db.Users.FindAsync(auth.UserId.Value);

        if (_user is null && auth.UserId.HasValue && api.IsOnline && !string.IsNullOrEmpty(auth.Token))
        {
            var me = await api.GetCurrentUserAsync();
            if (me is not null)
            {
                var fullName = $"{me.FirstName} {me.LastName}".Trim();
                db.Users.Add(new LocalUser
                {
                    Id = me.Id,
                    Name = fullName,
                    FirstName = me.FirstName,
                    LastName = me.LastName,
                    Username = me.Username,
                    Email = me.Email,
                    RoleName = me.Role,
                    RoleId = me.RoleId,
                    SubRole = me.SubRole,
                    AdditionalSubRoles = me.AdditionalSubRoles,
                    BirthDate = me.BirthDate,
                    HomeAddress = me.HomeAddress,
                    AvatarData = me.AvatarData,
                    IsSynced = true,
                    CreatedAt = me.CreatedAt,
                    IsBlocked = me.IsBlocked,
                    BlockedAt = me.BlockedAt,
                    BlockedReason = me.BlockedReason
                });
                await db.SaveChangesAsync();
                _user = await db.Users.FindAsync(auth.UserId.Value);
            }
        }

        if (_user is null) return;

        var displayName = !string.IsNullOrWhiteSpace(_user.Name)
            ? _user.Name
            : $"{_user.FirstName} {_user.LastName}".Trim();

        var initials = AvatarHelper.GetInitials(displayName);
        AvatarInitials.Text = initials;
        ApplyAvatarDisplay(_user.AvatarData, _user.AvatarPath);
        NameText.Text = displayName;
        LoginText.Text = _user.Username;
        EmailText.Text = string.IsNullOrWhiteSpace(_user.Email) ? "—" : _user.Email;
        CreatedText.Text = _user.CreatedAt.ToString("dd.MM.yyyy");
        LastDeviceText.Text = $"Устройство: {Environment.MachineName}";

        // View mode fields
        ViewFirstName.Text = _user.FirstName;
        ViewLastName.Text  = _user.LastName;
        ViewUsername.Text  = _user.Username;
        ViewEmail.Text     = string.IsNullOrWhiteSpace(_user.Email) ? "Не указан" : _user.Email;
        ViewBirthDate.Text = _user.BirthDate.HasValue ? _user.BirthDate.Value.ToString("dd.MM.yyyy") : "Не указана";
        ViewAddress.Text = string.IsNullOrWhiteSpace(_user.HomeAddress) ? "Не указан" : _user.HomeAddress;
        ViewCreated.Text   = _user.CreatedAt.ToString("dd.MM.yyyy");

        // Edit mode pre-fill
        FirstNameBox.Text = _user.FirstName;
        LastNameBox.Text  = _user.LastName;
        EmailBox.Text     = _user.Email ?? "";
        BirthDatePicker.SelectedDate = _user.BirthDate?.ToDateTime(TimeOnly.MinValue);
        AddressBox.Text = _user.HomeAddress ?? "";

        // Role info
        var roleName = _user.RoleName;
        string positionLabel = roleName;

        if (RoleInfo.TryGetValue(roleName, out var info))
        {
            var roleLabel = info.label;
            positionLabel = roleLabel;
            ViewRole.Text = roleLabel;
            RoleCardTitle.Text = roleLabel;
            RoleCardDesc.Text = info.desc;
            PermissionsList.ItemsSource = info.perms;
        }
        else
        {
            ViewRole.Text = roleName;
            RoleCardTitle.Text = roleName;
            RoleCardDesc.Text = "";
        }

        // Specialties for workers (основная + дополнительные)
        if (roleName == "Worker")
        {
            var specLine = WorkerSpecialtiesJson.FormatWorkerLine(_user.SubRole, _user.AdditionalSubRoles);
            var hasSpecs = !string.IsNullOrWhiteSpace(_user.SubRole)
                           || WorkerSpecialtiesJson.Deserialize(_user.AdditionalSubRoles).Count > 0;
            SubRoleBadge.Visibility = hasSpecs ? Visibility.Visible : Visibility.Collapsed;
            SubRoleText.Text = specLine;
            PositionText.Text = hasSpecs ? specLine : positionLabel;
        }
        else
        {
            SubRoleBadge.Visibility = Visibility.Collapsed;
            PositionText.Text = positionLabel;
        }

        // Load stats
        var projectCount = await db.Projects.CountAsync();
        var taskCount = await db.Tasks.CountAsync();
        var stageCount = await db.TaskStages.CountAsync();
        ProjectCountText.Text = projectCount.ToString();
        TaskCountText.Text = taskCount.ToString();
        StageCountText.Text = stageCount.ToString();

        // Activity count
        var activityCount = await ActivityFilterService.GetFilteredActivityCountAsync(db, auth);
        ActivityCountText.Text = activityCount.ToString();

        var recent = await db.RecentAccounts
            .Where(a => a.Username == _user.Username)
            .OrderByDescending(a => a.LastLoginAt)
            .FirstOrDefaultAsync();
        LastLoginText.Text = recent is null
            ? "Нет данных"
            : recent.LastLoginAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

        if (!_isBackgroundSyncInProgress)
            _ = RunBackgroundSyncAndRefreshAsync();
    }

    private async System.Threading.Tasks.Task RunBackgroundSyncAndRefreshAsync()
    {
        _isBackgroundSyncInProgress = true;
        try
        {
            var sync = App.Services.GetRequiredService<ISyncService>();
            await sync.SyncAsync();
            await LoadUserAsync();
        }
        catch
        {
            // Тихо игнорируем ошибки фонового синка, чтобы не блокировать профиль.
        }
        finally
        {
            _isBackgroundSyncInProgress = false;
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        ViewPanel.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
        EditBtn.Visibility = Visibility.Collapsed;
        CancelBtn.Visibility = Visibility.Visible;
        SaveBtn.Visibility = Visibility.Visible;
        ChangePasswordBtn.Visibility = Visibility.Collapsed;

        if (_user is not null)
        {
            FirstNameBox.Text = _user.FirstName;
            LastNameBox.Text  = _user.LastName;
            EmailBox.Text     = _user.Email ?? "";
            BirthDatePicker.SelectedDate = _user.BirthDate?.ToDateTime(TimeOnly.MinValue);
            AddressBox.Text = _user.HomeAddress ?? "";
        }
        CurrentPasswordBox.Password = "";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ViewPanel.Visibility = Visibility.Visible;
        EditPanel.Visibility = Visibility.Collapsed;
        EditBtn.Visibility = Visibility.Visible;
        CancelBtn.Visibility = Visibility.Collapsed;
        SaveBtn.Visibility = Visibility.Collapsed;
        ChangePasswordBtn.Visibility = Visibility.Visible;
        ErrorPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;

        var firstName = FirstNameBox.Text.Trim();
        var lastName  = LastNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(firstName))
        {
            ShowError("Введите имя.");
            return;
        }

        if (string.IsNullOrWhiteSpace(CurrentPasswordBox.Password))
        {
            ShowError("Введите текущий пароль для подтверждения изменений.");
            return;
        }

        SaveBtn.IsEnabled = false;
        try
        {
            var auth = App.Services.GetRequiredService<IAuthService>();
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var session = await db.AuthSessions.FindAsync(1);
            if (session is null)
            {
                ShowError("Сессия не найдена.");
                return;
            }

            if (!BCrypt.Net.BCrypt.Verify(CurrentPasswordBox.Password, session.LocalPasswordHash))
            {
                ShowError("Неверный текущий пароль.");
                return;
            }

            var fullName = $"{firstName} {lastName}".Trim();

            LocalUser? userEntity = null;
            if (_user is not null)
            {
                userEntity = await db.Users.FindAsync(_user.Id);
                if (userEntity is not null)
                {
                    userEntity.FirstName = firstName;
                    userEntity.LastName  = lastName;
                    userEntity.Name      = fullName;
                    userEntity.Email     = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
                    userEntity.BirthDate = BirthDatePicker.SelectedDate is { } bd ? DateOnly.FromDateTime(bd) : null;
                    userEntity.HomeAddress = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
                    userEntity.IsSynced  = false;
                    userEntity.LastModifiedLocally = DateTime.UtcNow;
                }
            }

            if (_user is not null)
                session.UserName = fullName;

            await db.SaveChangesAsync();

            if (_user is not null && userEntity is not null && auth.Token is not null)
            {
                var roleId = userEntity.RoleId;
                if (roleId == Guid.Empty)
                {
                    var roleRow = await db.Roles.AsNoTracking()
                        .FirstOrDefaultAsync(r => r.Name == userEntity.RoleName);
                    if (roleRow is not null)
                        roleId = roleRow.Id;
                }

                if (roleId != Guid.Empty)
                {
                    var updateReq = new UpdateUserRequest(
                        userEntity.FirstName,
                        userEntity.LastName,
                        userEntity.Username,
                        userEntity.Email,
                        roleId,
                        null,
                        userEntity.SubRole,
                        userEntity.AdditionalSubRoles,
                        userEntity.BirthDate,
                        userEntity.HomeAddress);

                    var api = App.Services.GetRequiredService<IApiService>();
                    var sync = App.Services.GetRequiredService<ISyncService>();

                    await api.ProbeAsync();
                    var remote = api.IsOnline ? await api.UpdateUserAsync(userEntity.Id, updateReq) : null;

                    if (remote is not null)
                    {
                        userEntity.IsSynced = true;
                        userEntity.RoleId = remote.RoleId;
                        await db.SaveChangesAsync();
                    }
                    else
                        await sync.QueueOperationAsync("UserProfile", userEntity.Id, SyncOperation.Update, updateReq);
                }
            }

            if (_user is not null)
            {
                _user.FirstName = firstName;
                _user.LastName  = lastName;
                _user.Name      = fullName;
                _user.Email     = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
                _user.BirthDate = BirthDatePicker.SelectedDate is { } bd ? DateOnly.FromDateTime(bd) : null;
                _user.HomeAddress = string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim();
                if (userEntity is not null)
                    _user.IsSynced = userEntity.IsSynced;
                if (userEntity is not null && userEntity.RoleId != Guid.Empty)
                    _user.RoleId = userEntity.RoleId;
            }

            SuccessPanel.Visibility = Visibility.Visible;

            NameText.Text      = _user?.Name ?? "";
            ViewFirstName.Text = _user?.FirstName ?? "";
            ViewLastName.Text  = _user?.LastName  ?? "";
            ViewEmail.Text     = _user?.Email ?? "Не указан";
            ViewBirthDate.Text = _user?.BirthDate?.ToString("dd.MM.yyyy") ?? "Не указана";
            ViewAddress.Text = string.IsNullOrWhiteSpace(_user?.HomeAddress) ? "Не указан" : _user!.HomeAddress!;
            EmailText.Text     = _user?.Email ?? "—";
            AvatarInitials.Text = AvatarHelper.GetInitials(_user?.Name ?? "");
            ApplyAvatarDisplay(_user?.AvatarData, _user?.AvatarPath);

            await System.Threading.Tasks.Task.Delay(1500);
            Cancel_Click(sender, e);
        }
        catch (System.Exception ex)
        {
            ShowError($"Ошибка: {ex.Message}");
        }
        finally
        {
            SaveBtn.IsEnabled = true;
        }
    }

    // ── Avatar helpers ──────────────────────────────────────────────────────

    private void ApplyAvatarDisplay(byte[]? avatarData, string? avatarPath)
    {
        var bmp = AvatarHelper.GetImageSource(avatarData, avatarPath, _user?.Name);
        if (bmp is not null)
        {
            AvatarImage.Source = bmp;
            AvatarImage.Visibility = Visibility.Visible;
            AvatarInitials.Visibility = Visibility.Collapsed;
            AvatarBorder.Background = Brushes.Transparent;
            return;
        }

        AvatarImage.Visibility = Visibility.Collapsed;
        AvatarInitials.Visibility = Visibility.Visible;
        var hexColor = AvatarHelper.GetColorForName(_user?.Name ?? "");
        try { AvatarBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)); }
        catch { AvatarBorder.Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x20, 0x38)); }
    }

    private void AvatarUpload_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        var openDlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите фото профиля",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Все файлы|*.*"
        };

        var owner = Window.GetWindow(this);
        if (openDlg.ShowDialog(owner) != true) return;

        var overlay = new AvatarCropOverlay(openDlg.FileName, SaveAvatarAsync);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 840);
    }

    private async System.Threading.Tasks.Task SaveAvatarAsync(BitmapSource croppedImage)
    {
        if (_user is null) return;
        try
        {
            Directory.CreateDirectory(AvatarDirectory);
            var avatarPath = Path.Combine(AvatarDirectory, $"{_user.Id}.png");

            byte[] avatarBytes = await System.Threading.Tasks.Task.Run(() =>
            {
                using var ms = new System.IO.MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(croppedImage));
                encoder.Save(ms);
                var bytes = ms.ToArray();

                using var fs = new FileStream(avatarPath, FileMode.Create);
                fs.Write(bytes, 0, bytes.Length);
                return bytes;
            });

            var auth = App.Services.GetRequiredService<IAuthService>();
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var entity = await db.Users.FindAsync(_user.Id);
            if (entity is not null)
            {
                entity.AvatarPath = avatarPath;
                entity.AvatarData = avatarBytes;
                entity.IsSynced   = false;
                entity.LastModifiedLocally = DateTime.UtcNow;

                var actLog = new LocalActivityLog
                {
                    UserId      = _user.Id,
                    ActorRole   = auth.UserRole,
                    UserName    = _user.Name,
                    UserInitials = AvatarHelper.GetInitials(_user.Name),
                    UserColor   = "#0F2038",
                    ActionType  = ActivityActionKind.AvatarChanged,
                    ActionText  = "Изменил фото профиля",
                    EntityType  = "User",
                    EntityId    = _user.Id,
                    CreatedAt   = DateTime.UtcNow
                };
                db.ActivityLogs.Add(actLog);

                await db.SaveChangesAsync();
                await App.Services.GetRequiredService<ISyncService>().QueueLocalActivityLogAsync(actLog);
            }

            _user.AvatarPath = avatarPath;
            _user.AvatarData = avatarBytes;

            var sync = App.Services.GetRequiredService<ISyncService>();
            await sync.QueueOperationAsync("User", _user.Id, SyncOperation.Update, new UploadAvatarRequest(avatarBytes));

            ApplyAvatarDisplay(_user.AvatarData, _user.AvatarPath);

            var mainVm = App.Services.GetService(typeof(MPMS.ViewModels.MainViewModel))
                         as MPMS.ViewModels.MainViewModel;
            if (mainVm is not null)
                await mainVm.RefreshAvatarAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось сохранить аватар: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void AvatarBtn_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
        AvatarHoverOverlay.BeginAnimation(OpacityProperty, anim);
    }

    private void AvatarBtn_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(150));
        AvatarHoverOverlay.BeginAnimation(OpacityProperty, anim);
    }

    // ── Error/Success ───────────────────────────────────────────────────────

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        if (_user is null) return;
        var overlay = new ChangePasswordOverlay(_user.Id, _user.Name, onSaved: async () => await LoadUserAsync());
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 520);
    }

    private void CopyToClipboard(string? text, MouseButtonEventArgs e)
    {
        if (_copyToastActive) return;
        if (string.IsNullOrWhiteSpace(text) || text == "—") return;
        if (!TrySetClipboardText(text))
            return;
        _copyToastActive = true;
        CopyToastText.Text = "Скопировано";
        var p = e.GetPosition(CopyToastLayer);
        PositionCopyToastAboveClick(p);
        CopyToast.BeginAnimation(UIElement.OpacityProperty, null);
        CopyToast.Opacity = 1;
        CopyToast.Visibility = Visibility.Visible;
        _copyToastHideTimer?.Stop();
        _copyToastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _copyToastHideTimer.Tick += (_, _) =>
        {
            _copyToastHideTimer!.Stop();
            CopyToast.Visibility = Visibility.Collapsed;
            _copyToastActive = false;
        };
        _copyToastHideTimer.Start();
    }

    /// <summary>
    /// OpenClipboard часто занят при быстрых повторных кликах или сторонних программах (CLIPBRD_E_CANT_OPEN).
    /// </summary>
    private static bool TrySetClipboardText(string text, int maxAttempts = 15, int delayMs = 25)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x800401D0) && attempt < maxAttempts - 1)
            {
                Thread.Sleep(delayMs);
            }
            catch (COMException) when (attempt < maxAttempts - 1)
            {
                Thread.Sleep(delayMs);
            }
        }

        return false;
    }

    private void PositionCopyToastAboveClick(Point clickInLayer)
    {
        try
        {
            CopyToast.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var toastW = Math.Max(CopyToast.DesiredSize.Width, 1);
            var toastH = Math.Max(CopyToast.DesiredSize.Height, 1);
            const double pad = 4;
            var layerW = CopyToastLayer.ActualWidth;
            var x = clickInLayer.X - toastW / 2.0;
            if (x < pad) x = pad;
            if (layerW > 1 && x + toastW > layerW - pad)
                x = Math.Max(pad, layerW - toastW - pad);
            // Всегда над точкой нажатия (не переносим под палец)
            var y = clickInLayer.Y - toastH - 8;
            if (y < pad) y = pad;
            CopyToast.Margin = new Thickness(x, y, 0, 0);
        }
        catch
        {
            CopyToast.Margin = new Thickness(8, 8, 0, 0);
        }
    }
}
