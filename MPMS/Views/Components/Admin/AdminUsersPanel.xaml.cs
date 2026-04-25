using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MPMS.ViewModels;

namespace MPMS.Views.Components.Admin
{
    public partial class AdminUsersPanel : UserControl
    {
        private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x11, 0x11, 0x11));
        private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

        public AdminUsersPanel()
        {
            InitializeComponent();
        }

        private void UserSearch_GotFocus(object s, RoutedEventArgs e) => UserSearchBorder.BorderBrush = FocusBrush;
        private void UserSearch_LostFocus(object s, RoutedEventArgs e) => UserSearchBorder.BorderBrush = ClearBrush;

        private void ClearUserSearch_Click(object s, RoutedEventArgs e)
        {
            if (DataContext is AdminViewModel vm)
                vm.UserSearchText = string.Empty;
        }

        private void CreateUser_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdminViewModel vm)
                vm.OpenCreateFormCommand.Execute(null);
        }

        private void UserRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is AdminUserRow row)
            {
                if (DataContext is AdminViewModel vm)
                    vm.ViewUserInfoCommand.Execute(row);
            }
        }
    }
}
