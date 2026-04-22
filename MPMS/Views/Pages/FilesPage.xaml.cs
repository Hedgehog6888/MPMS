using System.Windows;
using System.Windows.Controls;

namespace MPMS.Views.Pages;

public partial class FilesPage : UserControl
{
    public FilesPage()
    {
        InitializeComponent();
    }

    private void UploadFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.FilesPageViewModel vm)
        {
            vm.FilesControlVM.UploadFileCommand.Execute(null);
        }
    }
}
