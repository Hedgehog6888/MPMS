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
using MPMS.Views.Dialogs;

#pragma warning disable CS0618

namespace MPMS.Views.Pages;

public partial class ProfilePage : UserControl
{
    private LocalUser? _user;
    private bool _isEditing;

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

        // Populate card
        var parts = _user.Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
        var initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}"
            : _user.Name.Length > 0 ? $"{_user.Name[0]}" : "?";

        AvatarInitials.Text = initials.ToUpperInvariant();
        ApplyAvatarDisplay(_user.AvatarPath);
        NameText.Text = _user.Name;
        LoginText.Text = _user.Username;
        EmailText.Text = string.IsNullOrWhiteSpace(_user.Email) ? "—" : _user.Email;
        CreatedText.Text = _user.CreatedAt.ToString("dd.MM.yyyy");

        // View mode fields
        ViewName.Text = _user.Name;
        ViewUsername.Text = _user.Username;
        ViewEmail.Text = string.IsNullOrWhiteSpace(_user.Email) ? "Не указан" : _user.Email;
        ViewCreated.Text = _user.CreatedAt.ToString("dd MMMM yyyy");

        // Edit mode pre-fill
        NameBox.Text = _user.Name;
        EmailBox.Text = _user.Email ?? "";

        // Role info
        var roleName = _user.RoleName;
        PositionText.Text = roleName;
        if (RoleInfo.TryGetValue(roleName, out var info))
        {
            RoleText.Text = info.label;
            RoleIcon.Text = info.icon;
            ViewRole.Text = info.label;
            RoleCardTitle.Text = info.label;
            RoleCardIcon.Text = info.icon;
            RoleCardDesc.Text = info.desc;
            PermissionsList.ItemsSource = info.perms;

            // Color badge by role
            var (bg, fg, border) = roleName switch
            {
                "Administrator" or "Admin"   => ("#FEF2F2", "#991B1B", "#FCA5A5"),
                "Project Manager" or "ProjectManager" or "Manager" => ("#EFF6FF", "#1D4ED8", "#BFDBFE"),
                "Foreman"                    => ("#F0FDF4", "#166534", "#86EFAC"),
                "Worker"                     => ("#FFF7ED", "#9A3412", "#FED7AA"),
                _                            => ("#F4F5F7", "#172B4D", "#DFE1E6")
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

        // Load stats
        var projectCount = await db.Projects.CountAsync();
        var taskCount = await db.Tasks.CountAsync();
        var stageCount = await db.TaskStages.CountAsync();
        var activityCount = await db.ActivityLogs.CountAsync();
        ProjectCountText.Text = projectCount.ToString();
        TaskCountText.Text = taskCount.ToString();
        StageCountText.Text = stageCount.ToString();
        ActivityCountText.Text = activityCount.ToString();

        // Load recent activity (last 8 entries)
        var activities = await db.ActivityLogs
            .OrderByDescending(a => a.CreatedAt)
            .Take(8)
            .ToListAsync();
        ActivityList.ItemsSource = activities;
        NoActivityText.Visibility = activities.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        _isEditing = true;
        ErrorPanel.Visibility = Visibility.Collapsed;
        SuccessPanel.Visibility = Visibility.Collapsed;
        ViewPanel.Visibility = Visibility.Collapsed;
        EditPanel.Visibility = Visibility.Visible;
        EditBtn.Visibility = Visibility.Collapsed;
        CancelBtn.Visibility = Visibility.Visible;
        SaveBtn.Visibility = Visibility.Visible;

        if (_user is not null)
        {
            NameBox.Text = _user.Name;
            EmailBox.Text = _user.Email ?? "";
        }
        CurrentPasswordBox.Password = "";
        NewPasswordBox.Password = "";
        ConfirmPasswordBox.Password = "";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _isEditing = false;
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

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("Введите полное имя.");
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
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();

            var session = await db.AuthSessions.FindAsync(1);
            if (session is null)
            {
                ShowError("Сессия не найдена.");
                return;
            }

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(CurrentPasswordBox.Password, session.LocalPasswordHash))
            {
                ShowError("Неверный текущий пароль.");
                return;
            }

            // Update user in local DB
            if (_user is not null)
            {
                var userEntity = await db.Users.FindAsync(_user.Id);
                if (userEntity is not null)
                {
                    userEntity.Name = NameBox.Text.Trim();
                    userEntity.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
                    userEntity.IsSynced = false;
                }
            }

            // Update password hash if changed
            if (!string.IsNullOrEmpty(newPass))
            {
                session.LocalPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPass);
            }

            // Update display name in session
            if (_user is not null)
            {
                session.UserName = NameBox.Text.Trim();
            }

            await db.SaveChangesAsync();

            // Refresh display
            if (_user is not null)
            {
                _user.Name = NameBox.Text.Trim();
                _user.Email = string.IsNullOrWhiteSpace(EmailBox.Text) ? null : EmailBox.Text.Trim();
            }

            SuccessPanel.Visibility = Visibility.Visible;

            // Update displayed name + avatar
            NameText.Text = _user?.Name ?? "";
            ViewName.Text = _user?.Name ?? "";
            ViewEmail.Text = _user?.Email ?? "Не указан";
            EmailText.Text = _user?.Email ?? "—";
            var parts = (_user?.Name ?? "").Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            var initials = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}"
                : _user?.Name.Length > 0 ? $"{_user.Name[0]}" : "?";
            AvatarInitials.Text = initials.ToUpperInvariant();
            ApplyAvatarDisplay(_user?.AvatarPath);

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

    private void ApplyAvatarDisplay(string? avatarPath)
    {
        if (!string.IsNullOrWhiteSpace(avatarPath) && File.Exists(avatarPath))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(avatarPath, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();

                AvatarImage.Source = bmp;
                AvatarImage.Visibility = Visibility.Visible;
                AvatarInitials.Visibility = Visibility.Collapsed;
                AvatarBorder.Background = Brushes.Transparent;
                return;
            }
            catch { /* fall through to initials */ }
        }

        AvatarImage.Visibility = Visibility.Collapsed;
        AvatarInitials.Visibility = Visibility.Visible;
        AvatarBorder.Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2));
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

        var cropDlg = new AvatarCropDialog(openDlg.FileName)
        {
            Owner = owner
        };

        if (cropDlg.ShowDialog() == true && cropDlg.CroppedImage != null)
        {
            _ = SaveAvatarAsync(cropDlg.CroppedImage);
        }
    }

    private async System.Threading.Tasks.Task SaveAvatarAsync(BitmapSource croppedImage)
    {
        if (_user is null) return;
        try
        {
            Directory.CreateDirectory(AvatarDirectory);
            var avatarPath = Path.Combine(AvatarDirectory, $"{_user.Id}.png");

            // Write PNG to disk
            await System.Threading.Tasks.Task.Run(() =>
            {
                using var fs = new FileStream(avatarPath, FileMode.Create);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(croppedImage));
                encoder.Save(fs);
            });

            // Persist path to DB
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var entity = await db.Users.FindAsync(_user.Id);
            if (entity is not null)
            {
                entity.AvatarPath = avatarPath;
                entity.IsSynced = false;
                await db.SaveChangesAsync();
            }

            _user.AvatarPath = avatarPath;

            // Refresh the profile page avatar display
            ApplyAvatarDisplay(avatarPath);

            // Propagate to MainViewModel so the top-bar and all other places update
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
