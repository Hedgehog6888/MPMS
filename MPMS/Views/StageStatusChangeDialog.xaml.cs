using System.Windows;
using System.Windows.Media;

namespace MPMS.Views;

public partial class StageStatusChangeDialog : Window
{
    public bool Confirmed { get; private set; }

    public StageStatusChangeDialog()
    {
        InitializeComponent();
    }

    public void Configure(string stageName, string currentStatus, string newStatus,
        string currentStatusColor = "#F4F5F7", string newStatusColor = "#F4F5F7",
        string currentStatusTextColor = "#42526E", string newStatusTextColor = "#42526E",
        string? message = null)
    {
        StageNameText.Text = stageName;
        CurrentStatusText.Text = currentStatus;
        NewStatusText.Text = newStatus;

        CurrentStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(currentStatusColor));
        NewStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(newStatusColor));
        CurrentStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(currentStatusTextColor));
        NewStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(newStatusTextColor));

        if (!string.IsNullOrWhiteSpace(message))
        {
            MessageText.Text = message;
            MessageText.Visibility = Visibility.Visible;
        }
        else
        {
            MessageText.Visibility = Visibility.Collapsed;
        }
    }

    public static bool Show(Window owner, string stageName, string currentStatus, string newStatus,
        string currentStatusColor = "#F4F5F7", string newStatusColor = "#F4F5F7",
        string currentStatusTextColor = "#42526E", string newStatusTextColor = "#42526E",
        string? message = null)
    {
        var dialog = new StageStatusChangeDialog();
        dialog.Owner = owner;
        dialog.Configure(stageName, currentStatus, newStatus, currentStatusColor, newStatusColor,
            currentStatusTextColor, newStatusTextColor, message);
        dialog.ShowDialog();
        return dialog.Confirmed;
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
