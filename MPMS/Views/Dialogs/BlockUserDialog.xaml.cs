using System.Windows;

namespace MPMS.Views.Dialogs;

public partial class BlockUserDialog : Window
{
    public string Reason { get; private set; } = string.Empty;

    public BlockUserDialog()
    {
        InitializeComponent();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Reason = ReasonBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
