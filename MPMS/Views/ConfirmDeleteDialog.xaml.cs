using System.Windows;

namespace MPMS.Views;

public partial class ConfirmDeleteDialog : Window
{
    public bool Confirmed { get; private set; }

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

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
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
