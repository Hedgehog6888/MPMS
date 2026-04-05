using System.Windows;

namespace MPMS.Views.Dialogs;

public partial class WriteOffDialog : Window
{
    public string? Comment { get; private set; }

    public WriteOffDialog(string entityType, string entityName)
    {
        InitializeComponent();
        TitleText.Text = $"Списать {entityType}";
        WarningText.Text = $"Вы собираетесь списать \"{entityName}\". После списания этот элемент будет недоступен для использования. Действие нельзя отменить.";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
