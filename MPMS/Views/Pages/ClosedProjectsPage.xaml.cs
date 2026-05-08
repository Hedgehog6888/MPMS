using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Pages
{
    public partial class ClosedProjectsPage : UserControl
    {
        public ClosedProjectsPage()
        {
            InitializeComponent();
        }

        private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not LocalProject project) return;
            var vm = MainWindow.Instance?.DataContext as MainViewModel
                     ?? App.Services.GetService<MainViewModel>();
            vm?.NavigateToProject(project);
        }
    }
}
