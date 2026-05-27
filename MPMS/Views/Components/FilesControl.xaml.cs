using System.Windows;
using System.Windows.Controls;

namespace MPMS.Views.Components;

public enum FilesControlLayoutMode
{
    Full,
    TabsOnly,
    ScrollOnly
}

public partial class FilesControl : UserControl
{
    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(
            nameof(LayoutMode),
            typeof(FilesControlLayoutMode),
            typeof(FilesControl),
            new PropertyMetadata(FilesControlLayoutMode.Full));

    public FilesControlLayoutMode LayoutMode
    {
        get => (FilesControlLayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public FilesControl()
    {
        InitializeComponent();
    }
}
