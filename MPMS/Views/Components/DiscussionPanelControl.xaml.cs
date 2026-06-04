using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MPMS.Views.Components;

public partial class DiscussionPanelControl : UserControl
{
    public DiscussionPanelControl()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(DiscussionPanelControl), new PropertyMetadata(null));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public static readonly DependencyProperty IsClosedProperty =
        DependencyProperty.Register(nameof(IsClosed), typeof(bool), typeof(DiscussionPanelControl), new PropertyMetadata(false));

    public bool IsClosed
    {
        get => (bool)GetValue(IsClosedProperty);
        set => SetValue(IsClosedProperty, value);
    }

    public event EventHandler<string>? SendRequested;
    public event EventHandler<Guid>? UserPeekRequested;

    private void Send_Click(object sender, RoutedEventArgs e)
    {
        if (MessageInput is null) return;
        var text = MessageInput.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        MessageInput.Text = string.Empty;
        SendRequested?.Invoke(this, text);
        ScrollToBottom();
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Send_Click(sender, e);
            e.Handled = true;
        }
    }

    private void UserAvatar_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MPMS.Models.LocalMessage msg) return;
        e.Handled = true;
        UserPeekRequested?.Invoke(this, msg.UserId);
    }

    private void UserName_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not MPMS.Models.LocalMessage msg) return;
        e.Handled = true;
        UserPeekRequested?.Invoke(this, msg.UserId);
    }

    public void ScrollToBottom()
    {
        if (MessagesScrollViewer is null) return;
        MessagesScrollViewer.ScrollToBottom();
    }
}
