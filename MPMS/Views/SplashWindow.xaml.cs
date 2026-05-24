using System.Windows;

namespace MPMS.Views;

public partial class SplashWindow : Window
{
    public SplashWindow()
    {
        InitializeComponent();
    }

    public void SetLoadingText(string text)
    {
        LoadingText.Text = text;
    }

    public void CloseWithFadeOut() => Close();
}
