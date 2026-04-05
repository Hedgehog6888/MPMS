using System.Windows;

namespace MPMS.Views.Dialogs;

public partial class AddQuantityDialog : Window
{
    public decimal Amount { get; private set; }
    public string? Comment { get; private set; }

    public AddQuantityDialog()
    {
        InitializeComponent();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorText.Text = "Введите корректное положительное количество";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Amount = amount;
        Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
