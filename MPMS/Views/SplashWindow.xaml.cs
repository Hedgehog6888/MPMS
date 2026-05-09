using System.Windows;
using System.Windows.Media.Animation;

namespace MPMS.Views;

public partial class SplashWindow : Window
{
    private readonly DoubleAnimation _progressAnimation;

    public SplashWindow()
    {
        InitializeComponent();

        // Animate progress bar
        _progressAnimation = new DoubleAnimation
        {
            From = 0,
            To = 220,
            Duration = TimeSpan.FromSeconds(1.5),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            RepeatBehavior = RepeatBehavior.Forever,
            AutoReverse = true
        };

        Loaded += (s, e) =>
        {
            ProgressBar.BeginAnimation(WidthProperty, _progressAnimation);
        };
    }

    public void SetLoadingText(string text)
    {
        LoadingText.Text = text;
    }

    public void CloseWithFadeOut()
    {
        var fadeOut = new DoubleAnimation
        {
            From = 1,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        fadeOut.Completed += (s, e) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }
}
