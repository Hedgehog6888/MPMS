using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MPMS.Views;

public partial class ConfirmDeleteDialog : Window
{
    public bool Confirmed { get; private set; }
    public string? ClosureReason { get; private set; }
    public string? BlockReason { get; private set; }

    public ConfirmDeleteDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Configures the dialog for a specific deletion scenario.
    /// </summary>
    /// <param name="entityType">Human-readable entity type in Russian (e.g., "Задача", "Этап", "Проект")</param>
    /// <param name="itemName">Name of the item being deleted</param>
    /// <param name="cascadeMessage">Optional cascade warning message</param>
    public void Configure(string entityType, string itemName, string? cascadeMessage = null)
    {
        TitleText.Text = $"Удалить {entityType.ToLower()}?";
        EntityTypeText.Text = entityType;
        ItemNameText.Text = itemName;
        ConfirmBtn.Content = "Удалить";
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
        ConfirmBtn.Style = (Style)FindResource("RedDialogBtn");

        if (!string.IsNullOrWhiteSpace(cascadeMessage))
        {
            CascadeText.Text = cascadeMessage;
            CascadeWarning.Visibility = Visibility.Visible;
        }
        else
        {
            CascadeWarning.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Shows the dialog centered over the owner window and returns true if confirmed.
    /// </summary>
    public static bool Show(Window owner, string entityType, string itemName, string? cascadeMessage = null)
    {
        var dialog = new ConfirmDeleteDialog();
        dialog.Owner = owner;
        dialog.Configure(entityType, itemName, cascadeMessage);
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    /// <summary>
    /// Configures the dialog for mark-for-deletion action.
    /// </summary>
    public void ConfigureMarkForDeletion(string entityType, string itemName)
    {
        TitleText.Text = $"Пометить {entityType.ToLower()} к удалению?";
        EntityTypeText.Text = entityType;
        ItemNameText.Text = itemName;
        ConfirmBtn.Content = "Пометить";
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEF2F2"));
        ConfirmBtn.Style = (Style)FindResource("RedDialogBtn");
        CascadeWarning.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows mark-for-deletion confirmation and returns true if confirmed.
    /// </summary>
    public static bool ShowMarkForDeletion(Window owner, string entityType, string itemName)
    {
        var dialog = new ConfirmDeleteDialog
        {
            Owner = owner
        };
        dialog.ConfigureMarkForDeletion(entityType, itemName);
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    /// <summary>Sets a custom message for the admin panel operations.</summary>
    public void SetMessage(string message)
    {
        if (TitleText is not null) TitleText.Text = "Подтверждение";
        if (ItemNameText is not null) ItemNameText.Text = message;
        if (EntityTypeText is not null) EntityTypeText.Text = "Действие";
    }

    /// <summary>
    /// Configures the dialog for close project action.
    /// </summary>
    public void ConfigureCloseProject(string projectName)
    {
        TitleText.Text = "Закрыть проект?";
        EntityTypeText.Text = "Проект";
        ItemNameText.Text = projectName;
        ConfirmBtn.Content = "Закрыть";
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
        ConfirmBtn.Style = (Style)FindResource("GrayDialogBtn");
        CascadeWarning.Visibility = Visibility.Collapsed;
        ClosureReasonPanel.Visibility = Visibility.Visible;
        ClosureReasonTextBox.Text = string.Empty;
    }

    /// <summary>
    /// Shows close project confirmation and returns the closure reason if confirmed.
    /// </summary>
    public static (bool confirmed, string? reason) ShowCloseProjectConfirmation(Window owner, string projectName)
    {
        var dialog = new ConfirmDeleteDialog
        {
            Owner = owner
        };
        dialog.ConfigureCloseProject(projectName);
        dialog.ShowDialog();
        return (dialog.Confirmed, dialog.ClosureReason);
    }

    /// <summary>
    /// Configures the dialog for block user action (amber).
    /// </summary>
    public void ConfigureBlockUser(string userName)
    {
        TitleText.Text = "Заблокировать пользователя?";
        EntityTypeText.Text = "Пользователь";
        ItemNameText.Text = userName;
        ConfirmBtn.Content = "Заблокировать";
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF8E1"));
        SubtitleText.Text = "Пользователь не сможет войти в систему";
        SubtitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#92400E"));
        ConfirmBtn.Style = (Style)FindResource("AmberDialogBtn");
        CascadeWarning.Visibility = Visibility.Collapsed;
        ClosureReasonPanel.Visibility = Visibility.Collapsed;
        BlockReasonPanel.Visibility = Visibility.Visible;
        BlockReasonDisplayPanel.Visibility = Visibility.Collapsed;
        BlockReasonTextBox.Text = string.Empty;
    }

    /// <summary>
    /// Shows block user confirmation and returns the block reason if confirmed.
    /// </summary>
    public static (bool confirmed, string? reason) ShowBlockUserConfirmation(Window owner, string userName)
    {
        var dialog = new ConfirmDeleteDialog
        {
            Owner = owner
        };
        dialog.ConfigureBlockUser(userName);
        dialog.ShowDialog();
        return (dialog.Confirmed, dialog.BlockReason);
    }

    /// <summary>
    /// Configures the dialog for unblock user action (green).
    /// </summary>
    public void ConfigureUnblockUser(string userName, string? blockReason)
    {
        TitleText.Text = "Разблокировать пользователя?";
        EntityTypeText.Text = "Пользователь";
        ItemNameText.Text = userName;
        ConfirmBtn.Content = "Разблокировать";
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8E6C9"));
        SubtitleText.Text = "Пользователь снова сможет войти в систему";
        SubtitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
        ConfirmBtn.Style = (Style)FindResource("GreenDialogBtn");
        CascadeWarning.Visibility = Visibility.Collapsed;
        ClosureReasonPanel.Visibility = Visibility.Collapsed;
        BlockReasonPanel.Visibility = Visibility.Collapsed;
        BlockReasonDisplayPanel.Visibility = !string.IsNullOrWhiteSpace(blockReason) ? Visibility.Visible : Visibility.Collapsed;
        BlockReasonDisplayText.Text = blockReason ?? string.Empty;
    }

    /// <summary>
    /// Shows unblock user confirmation.
    /// </summary>
    public static bool ShowUnblockUserConfirmation(Window owner, string userName, string? blockReason)
    {
        var dialog = new ConfirmDeleteDialog
        {
            Owner = owner
        };
        dialog.ConfigureUnblockUser(userName, blockReason);
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    /// <summary>
    /// Configures the dialog for a generic non-destructive confirmation (blue).
    /// </summary>
    public void ConfigureNonDestructive(string title, string entityName, string buttonText)
    {
        TitleText.Text = title;
        EntityTypeText.Text = "Действие";
        ItemNameText.Text = entityName;
        ConfirmBtn.Content = buttonText;
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EBF2FF"));
        SubtitleText.Text = "Это действие нельзя отменить";
        SubtitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1D4ED8"));
        ConfirmBtn.Style = (Style)FindResource("BlueDialogBtn");
        CascadeWarning.Visibility = Visibility.Collapsed;
        ClosureReasonPanel.Visibility = Visibility.Collapsed;
        BlockReasonPanel.Visibility = Visibility.Collapsed;
        BlockReasonDisplayPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Shows non-destructive confirmation.
    /// </summary>
    public static bool ShowNonDestructiveConfirmation(Window owner, string title, string entityName, string buttonText)
    {
        var dialog = new ConfirmDeleteDialog
        {
            Owner = owner
        };
        dialog.ConfigureNonDestructive(title, entityName, buttonText);
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        ClosureReason = ClosureReasonTextBox.Text?.Trim();
        BlockReason = BlockReasonTextBox.Text?.Trim();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }
}
