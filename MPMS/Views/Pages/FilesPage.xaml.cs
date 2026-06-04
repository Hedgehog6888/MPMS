using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class FilesPage : UserControl
{
    private DispatcherTimer? _toastHideTimer;
    private bool _toastActive;

    public FilesPage()
    {
        InitializeComponent();
        DataContextChanged += FilesPage_DataContextChanged;
        IsVisibleChanged += FilesPage_IsVisibleChanged;
    }

    private void FilesPage_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ViewModels.FilesPageViewModel vm)
        {
            vm.FilesControlVM.ShowToastRequested += ShowToast;
        }
    }

    private void FilesPage_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible) return;

        if (DataContext is ViewModels.FilesPageViewModel vm)
            vm.FilesControlVM.CancelSelectionModeCommand.Execute(null);
    }

    private void UploadFile_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is ViewModels.FilesPageViewModel vm)
        {
            vm.FilesControlVM.UploadFileCommand.Execute(null);
        }
    }

    private void CreateReport_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ReportPopup.IsOpen = true;
    }

    private void MaterialStockReport_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ReportPopup.IsOpen = false;
        var overlay = new ReportSelectionOverlay("MaterialStock");
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 600);
    }

    private void WorkTypeReport_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        ReportPopup.IsOpen = false;
        var overlay = new ReportSelectionOverlay("WorkType");
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 600);
    }

    public void ShowToast(string message)
    {
        if (_toastActive) return;

        var toast = FindName("Toast") as Border;
        var toastText = FindName("ToastText") as TextBlock;

        if (toast == null || toastText == null) return;

        _toastActive = true;
        toastText.Text = message;
        toast.Visibility = Visibility.Visible;
        _toastHideTimer?.Stop();
        _toastHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _toastHideTimer.Tick += (_, _) =>
        {
            _toastHideTimer!.Stop();
            toast.Visibility = Visibility.Collapsed;
            _toastActive = false;
        };
        _toastHideTimer.Start();
    }
}
