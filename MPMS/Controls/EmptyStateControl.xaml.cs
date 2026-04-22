using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MPMS.Controls;

/// <summary>
/// Блок «пустого состояния» — иконка + заголовок + подзаголовок.
/// Устанавливайте Visibility через конвертер IntToVisInv на Count коллекции.
/// </summary>
public partial class EmptyStateControl : UserControl
{
    public static readonly DependencyProperty IconPathProperty =
        DependencyProperty.Register(nameof(IconPath), typeof(string),
            typeof(EmptyStateControl), new PropertyMetadata(string.Empty, OnIconPathChanged));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string),
            typeof(EmptyStateControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string),
            typeof(EmptyStateControl), new PropertyMetadata(string.Empty));

    /// <summary>
    /// Путь к ресурсу-иконке (например "/icons/check.svg").
    /// Если пустой — иконка не отображается.
    /// </summary>
    public string IconPath
    {
        get => (string)GetValue(IconPathProperty);
        set => SetValue(IconPathProperty, value);
    }

    /// <summary>Основной текст (крупный).</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Вспомогательный текст (мелкий). Скрывается если пустой.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public EmptyStateControl()
    {
        InitializeComponent();
    }

    private static void OnIconPathChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // SVG иконки подключаются через SharpVectors, но его нельзя использовать в коде напрямую.
        // Иконка задаётся в XAML через svgc:SvgImage, поэтому мы здесь ничего не делаем.
        // Для простоты — IconImage остаётся без Source если путь SVG;
        // страницы могут переопределить через IconImage.Source или использовать BitmapImage для PNG.
    }
}
