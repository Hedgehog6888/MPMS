using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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

    private static string AvatarDirectory =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MPMS", "Avatars");

    public ProfilePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadUserAsync();
    }

    private static readonly Dictionary<string, (string label, string icon, string desc, string[] perms)> RoleInfo = new()
    {
        ["Administrator"] = ("Администратор", "👑",
            "Полный доступ ко всем функциям системы",
            ["Создание и удаление проектов", "Управление пользователями", "Просмотр всех данных", "Управление ролями"]),
        ["Admin"] = ("Администратор", "👑",
            "Полный доступ ко всем функциям системы",
            ["Создание и удаление проектов", "Управление пользователями", "Просмотр всех данных", "Управление ролями"]),
        ["Project Manager"] = ("Менеджер проектов", "🗂️",
            "Управление проектами и командой исполнителей",
            ["Создание проектов", "Назначение задач", "Просмотр прогресса", "Добавление членов команды"]),
        ["ProjectManager"] = ("Менеджер проектов", "🗂️",
            "Управление проектами и командой исполнителей",
            ["Создание проектов", "Назначение задач", "Просмотр прогресса", "Добавление членов команды"]),
        ["Manager"] = ("Менеджер проектов", "🗂️",
            "Управление проектами и командой исполнителей",
            ["Создание проектов", "Назначение задач", "Просмотр прогресса", "Добавление членов команды"]),
        ["Foreman"] = ("Прораб", "🦺",
            "Руководство монтажными работами на объекте",
            ["Просмотр своих проектов", "Управление этапами", "Отчёт о выполнении", "Добавление материалов"]),
        ["Worker"] = ("Работник", "🔧",
            "Выполнение монтажных работ на объекте",
            ["Просмотр назначенных задач", "Обновление статусов этапов", "Добавление материалов"]),
    };

    private async System.Threading.Tasks.Task LoadUserAsync()
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (auth.UserId.HasValue)
            _user = await db.Users.FindAsync(auth.UserId.Value);

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

        // View mode fields
        ViewFirstName.Text = _user.FirstName;
        ViewLastName.Text  = _user.LastName;
        ViewUsername.Text  = _user.Username;
        ViewEmail.Text     = string.IsNullOrWhiteSpace(_user.Email) ? "Не указан" : _user.Email;
        ViewCreated.Text   = _user.CreatedAt.ToString("dd MMMM yyyy");

        // Edit mode pre-fill
        FirstNameBox.Text = _user.FirstName;
        LastNameBox.Text  = _user.LastName;
        EmailBox.Text     = _user.Email ?? "";

        // Role info
        var roleName = _user.RoleName;
        string positionLabel = roleName;

        if (RoleInfo.TryGetValue(roleName, out var info))
        {
            var roleLabel = info.label;
            positionLabel = roleLabel;
            RoleText.Text = roleLabel;
            RoleIcon.Text = info.icon;
            ViewRole.Text = roleLabel;
            RoleCardTitle.Text = roleLabel;
            RoleCardIcon.Text = info.icon;
            RoleCardDesc.Text = info.desc;
            PermissionsList.ItemsSource = info.perms;

            var (bg, fg, border) = roleName switch
            {
                "Administrator" or "Admin"   => ("#FEF2F2", "#991B1B", "#FCA5A5"),
                "Project Manager" or "ProjectManager" or "Manager" => ("#EFF6FF", "#1D4ED8", "#BFDBFE"),
                "Foreman"                    => ("#F0FDF4", "#166534", "#86EFAC"),
                "Worker"                     => ("#FFF7ED", "#9A3412", "#FED7AA"),
                _                            => ("#F4F5F7", "#000000", "#DFE1E6")
            };
            RoleBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)!);
            RoleBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(border)!);
            RoleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg)!);
        }
        else
        {
            RoleText.Text = roleName;
            ViewRole.Text = roleName;
            RoleCardTitle.Text = roleName;
            RoleCardIcon.Text = "👤";
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
            ViewSubRolePanel.Visibility = hasSpecs ? Visibility.Visible : Visibility.Collapsed;
            ViewSubRole.Text = specLine;
            PositionText.Text = hasSpecs ? specLine : positionLabel;
        }
        else
        {
            SubRoleBadge.Visibility = Visibility.Collapsed;
            ViewSubRolePanel.Visibility = Visibility.Collapsed;
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

        if (_user is not null)
        {
            FirstNameBox.Text = _user.FirstName;
            LastNameBox.Text  = _user.LastName;
            EmailBox.Text     = _user.Email ?? "";
        }
        CurrentPasswordBox.Password = "";
        NewPasswordBox.Password = "";
        ConfirmPasswordBox.Password = "";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        ViewPanel.Visibility = Visibility.Visible;
        EditPanel.Visibility = Visibility.Collapsed;
        EditBtn.Visibility = Visibility.Visible;
        CancelBtn.Visibility = Visibility.Collapsed;
        SaveBtn.Visibility = Visibility.Collapsed;
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

        var newPass = NewPasswordBox.Password;
        var confirmPass = ConfirmPasswordBox.Password;
        if (!string.IsNullOrEmpty(newPass) && newPass != confirmPass)
        {
            ShowError("Пароли не совпадают.");
            return;
        }

        if (!string.IsNullOrEmpty(newPass) && newPass.Length < 6)
        {
            ShowError("Пароль должен содержать не менее 6 символов.");
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

            if (_user is not null)
            {
                var userEntity = await db.Users.FindAsync(_user.Id);
                if (userEntity is not null)
                {
                    userEntity.FirstName = firstName;
                    userEntity.LastName  = lastName;
                    userEntity.Name      = fullName;
                    userEntity.Email     = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
                    userEntity.IsSynced  = false;
                }
            }

            if (!string.IsNullOrEmpty(newPass))
                session.LocalPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPass);

            if (_user is not null)
                session.UserName = fullName;

            await db.SaveChangesAsync();

            if (_user is not null)
            {
                _user.FirstName = firstName;
                _user.LastName  = lastName;
                _user.Name      = fullName;
                _user.Email     = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
            }

            if (!string.IsNullOrEmpty(newPass) && _user is not null)
            {
                db.ActivityLogs.Add(new LocalActivityLog
                {
                    UserId      = _user.Id,
                    ActorRole   = auth.UserRole,
                    UserName    = _user.Name,
                    UserInitials = AvatarHelper.GetInitials(_user.Name),
                    UserColor   = "#1B6EC2",
                    ActionType  = ActivityActionKind.PasswordChanged,
                    ActionText  = "Изменил пароль своего аккаунта",
                    EntityType  = "User",
                    EntityId    = _user.Id,
                    CreatedAt   = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            SuccessPanel.Visibility = Visibility.Visible;

            NameText.Text      = _user?.Name ?? "";
            ViewFirstName.Text = _user?.FirstName ?? "";
            ViewLastName.Text  = _user?.LastName  ?? "";
            ViewEmail.Text     = _user?.Email ?? "Не указан";
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
        catch { AvatarBorder.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2)); }
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

                db.ActivityLogs.Add(new LocalActivityLog
                {
                    UserId      = _user.Id,
                    ActorRole   = auth.UserRole,
                    UserName    = _user.Name,
                    UserInitials = AvatarHelper.GetInitials(_user.Name),
                    UserColor   = "#1B6EC2",
                    ActionType  = ActivityActionKind.AvatarChanged,
                    ActionText  = "Изменил фото профиля",
                    EntityType  = "User",
                    EntityId    = _user.Id,
                    CreatedAt   = DateTime.UtcNow
                });

                await db.SaveChangesAsync();
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
}
