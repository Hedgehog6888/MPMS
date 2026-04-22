using System.Windows;
using System.Windows.Controls;

namespace MPMS.Controls;

/// <summary>
/// Переиспользуемый заголовок страницы (Title + Subtitle + слот Actions).
/// </summary>
public partial class PageHeaderControl : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string),
            typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string),
            typeof(PageHeaderControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(nameof(Actions), typeof(UIElement),
            typeof(PageHeaderControl), new PropertyMetadata(null));

    /// <summary>Заголовок страницы (крупный текст).</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Подзаголовок (серый мелкий текст). Скрывается если пустой.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>Произвольный UIElement справа (кнопки Добавить, Экспорт и т.д.).</summary>
    public UIElement? Actions
    {
        get => (UIElement?)GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public PageHeaderControl()
    {
        InitializeComponent();
    }
}
