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

    public static bool Show(Window owner, string entityType, string itemName, string? cascadeMessage = null)
    {
        var dialog = new ConfirmDeleteDialog();
        dialog.Owner = owner;
        dialog.Configure(entityType, itemName, cascadeMessage);
        dialog.ShowDialog();
        return dialog.Confirmed;
    }

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

    public void SetMessage(string message)
    {
        if (TitleText is not null) TitleText.Text = "Подтверждение";
        if (ItemNameText is not null) ItemNameText.Text = message;
        if (EntityTypeText is not null) EntityTypeText.Text = "Действие";
    }
    public void ConfigureCloseProject(string projectName)
    {
        TitleText.Text = "Закрыть проект?";
        EntityTypeText.Text = "Проект";
        ItemNameText.Text = projectName;
        ConfirmBtn.Content = "Закрыть";
        HeaderBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));
        SubtitleText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B778C"));
        ConfirmBtn.Style = (Style)FindResource("GrayDialogBtn");
        CascadeWarning.Visibility = Visibility.Collapsed;
        ClosureReasonPanel.Visibility = Visibility.Visible;
        ClosureReasonTextBox.Text = string.Empty;
    }
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
