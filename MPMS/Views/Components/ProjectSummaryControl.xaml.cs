using System.Windows;
using System.Windows.Controls;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class ProjectSummaryControl
{
    public ProjectSummaryControl()
    {
        InitializeComponent();
    }

    private void SummarySectionTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } || DataContext is not ProjectDetailViewModel vm)
            return;
        vm.ProjectSummarySection = tag;
    }
}
