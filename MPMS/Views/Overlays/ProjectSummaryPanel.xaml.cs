using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;
using MPMS.Infrastructure;

namespace MPMS.Views.Overlays;

public partial class ProjectSummaryPanel : UserControl
{
    public ProjectSummaryPanel()
    {
        InitializeComponent();
    }

    public void SetProject(LocalProject? project)
    {
        if (project is null)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        ProjectNameText.Text = project.Name;
        ClientText.Text = project.Client ?? "—";
        AddressText.Text = project.Address ?? "—";
        ProgressBar.Value = project.ProgressPercent;
        ProgressText.Text = $"{project.ProgressPercent}%";
        ManagerInitialsText.Text = project.ManagerInitials;
        ManagerNameText.Text = project.ManagerName ?? "—";

        var statusBrush = ProjectStatusToBrushConverter.Instance.Convert(project.Status, typeof(Brush), null, CultureInfo.InvariantCulture) as SolidColorBrush;
        StatusBadge.Background = statusBrush ?? Brushes.Gray;
        StatusText.Text = ProjectStatusToStringConverter.Instance.Convert(project.Status, typeof(string), null, CultureInfo.InvariantCulture) as string ?? "—";
    }
}
