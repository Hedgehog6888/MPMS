using System.Windows;
using System.Windows.Media;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.Views.Dialogs;

public class CreateUserResult
{
    public string Name            { get; set; } = string.Empty;
    public string Username        { get; set; } = string.Empty;
    public string? Email          { get; set; }
    public Guid   RoleId          { get; set; }
    public string RoleName        { get; set; } = string.Empty;
    public string NewPasswordHash { get; set; } = string.Empty;
}

public partial class CreateUserDialog : Window
{
    public CreateUserResult? Result { get; private set; }

    private bool _isEditMode;
    private List<LocalRole> _roles = new();
    private Guid? _editingUserId;

    public CreateUserDialog()
    {
        InitializeComponent();
        UpdateAvatarPreview();
    }

    public void SetCreateMode(List<LocalRole> roles)
    {
        _isEditMode = false;
        _roles = roles;
        RoleCombo.ItemsSource = roles;
        if (roles.Count > 0) RoleCombo.SelectedIndex = 0;

        TitleText.Text    = "Создать пользователя";
        SubtitleText.Text = "Заполните данные для нового аккаунта";
        SaveBtnText.Text  = "Создать";
        SaveBtnIcon.Text  = "✓";
        PasswordSectionTitle.Text = "Задать пароль *";
        PasswordLabel.Text = "Пароль *";
    }

    public void SetEditMode(LocalUser user, List<LocalRole> roles)
    {
        _isEditMode    = true;
        _editingUserId = user.Id;
        _roles = roles;
        RoleCombo.ItemsSource = roles;

        NameBox.Text     = user.Name;
        UsernameBox.Text = user.Username;
        EmailBox.Text    = user.Email ?? string.Empty;
        var selectedRole = roles.FirstOrDefault(r => r.Id == user.RoleId);
        if (selectedRole is not null) RoleCombo.SelectedItem = selectedRole;

        TitleText.Text    = "Редактировать пользователя";
        SubtitleText.Text = "Измените данные аккаунта";
        SaveBtnText.Text  = "Сохранить";
        SaveBtnIcon.Text  = "✎";
        PasswordSectionTitle.Text = "Изменить пароль (необязательно)";
        PasswordLabel.Text = "Новый пароль";

        UpdateAvatarPreview();
    }

    private void Name_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateAvatarPreview();

    private void UpdateAvatarPreview()
    {
        var name = NameBox?.Text ?? string.Empty;
        var initials = AvatarHelper.GetInitials(name);
        var hexColor = AvatarHelper.GetColorForName(name);

        if (AvatarInitials is not null)
            AvatarInitials.Text = initials;

        if (AvatarCircle is not null)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                AvatarCircle.Background = new SolidColorBrush(color);
            }
            catch { /* ignore */ }
        }
    }

    private void Role_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (RoleCombo.SelectedItem is not LocalRole role) return;

        var (icon, desc, bg, fg) = role.Name switch
        {
            "Administrator" or "Admin" =>
                ("👑", "Полный доступ к системе, управление пользователями", "#FEE2E2", "#991B1B"),
            "Project Manager" or "ProjectManager" or "Manager" =>
                ("📋", "Управление проектами и задачами, назначение исполнителей", "#DBEAFE", "#1D4ED8"),
            "Foreman" =>
                ("🔧", "Управление этапами и материалами, просмотр задач", "#D1FAE5", "#166534"),
            "Worker" =>
                ("👷", "Просмотр и выполнение назначенных задач и этапов", "#EDE9FE", "#6D28D9"),
            _ =>
                ("👤", role.Description ?? "Стандартная роль", "#F1F3F5", "#4B5563")
        };

        if (RoleDescPanel is not null)
        {
            RoleDescPanel.Visibility  = Visibility.Visible;
            RoleDescPanel.Background  = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            RoleDescPanel.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
            RoleDescIcon.Text         = icon;
            RoleDescText.Text         = desc;
            RoleDescText.Foreground   = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fg));
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        var name     = NameBox.Text.Trim();
        var username = UsernameBox.Text.Trim();
        var email    = EmailBox.Text.Trim();
        var password = PasswordBox.Password;
        var confirm  = ConfirmPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(name))
        { ShowError("Введите полное имя пользователя."); return; }

        if (string.IsNullOrWhiteSpace(username))
        { ShowError("Введите логин (username)."); return; }

        if (RoleCombo.SelectedItem is not LocalRole selectedRole)
        { ShowError("Выберите роль."); return; }

        if (!_isEditMode && string.IsNullOrWhiteSpace(password))
        { ShowError("Введите пароль для нового пользователя."); return; }

        if (!string.IsNullOrEmpty(password) && password != confirm)
        { ShowError("Пароли не совпадают."); return; }

        if (!string.IsNullOrEmpty(password) && password.Length < 6)
        { ShowError("Пароль должен содержать не менее 6 символов."); return; }

        string passwordHash = string.Empty;
        if (!string.IsNullOrEmpty(password))
        {
            try { passwordHash = BCrypt.Net.BCrypt.HashPassword(password); }
            catch { passwordHash = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)); }
        }

        Result = new CreateUserResult
        {
            Name            = name,
            Username        = username,
            Email           = string.IsNullOrEmpty(email) ? null : email,
            RoleId          = selectedRole.Id,
            RoleName        = selectedRole.Name,
            NewPasswordHash = passwordHash
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Close_Click(object sender, RoutedEventArgs e)  => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
