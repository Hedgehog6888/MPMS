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

    /// <summary>
    /// Configures the dialog for stage status change.
    /// </summary>
    /// <param name="stageName">Name of the stage</param>
    /// <param name="currentStatus">Current status text</param>
    /// <param name="newStatus">New status text</param>
    /// <param name="currentStatusColor">Background color for current status badge</param>
    /// <param name="newStatusColor">Background color for new status badge</param>
    /// <param name="currentStatusTextColor">Text color for current status</param>
    /// <param name="newStatusTextColor">Text color for new status</param>
    /// <param name="message">Optional message to display</param>
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

    /// <summary>
    /// Shows the dialog centered over the owner window and returns true if confirmed.
    /// </summary>
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
