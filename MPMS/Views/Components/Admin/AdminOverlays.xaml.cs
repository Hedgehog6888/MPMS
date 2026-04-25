using System.Windows.Controls;
using System.Windows.Input;
using MPMS.ViewModels;

namespace MPMS.Views.Components.Admin
{
    public partial class AdminOverlays : UserControl
    {
        public AdminOverlays()
        {
            InitializeComponent();
        }

        private void BlockOverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
            => (DataContext as AdminViewModel)?.CancelBlockOverlayCommand.Execute(null);

        private void UnblockOverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
            => (DataContext as AdminViewModel)?.CancelUnblockOverlayCommand.Execute(null);

        private void ConfirmOverlayBackdrop_Click(object sender, MouseButtonEventArgs e)
            => (DataContext as AdminViewModel)?.CancelConfirmCommand.Execute(null);
    }
}
