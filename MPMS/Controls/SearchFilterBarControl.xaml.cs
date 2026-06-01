using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using MPMS.Infrastructure;

namespace MPMS.Controls;

/// <summary>
/// Переиспользуемая панель поиска с опциональными фильтрами.
/// <para>
/// Зависимые свойства:
/// <list type="bullet">
///   <item><see cref="SearchText"/>  — строка поиска (двусторонний биндинг к VM).</item>
///   <item><see cref="Placeholder"/> — текст-маска поля ввода.</item>
///   <item><see cref="Filters"/>     — произвольный UIElement (ComboBox-ы и пр.) правее поиска.</item>
///   <item><see cref="IsFloating"/>  — True → тень + Panel.ZIndex=1 (плавающий режим).</item>
///   <item><see cref="ScrollTarget"/>— ScrollViewer, которому пробрасывается PreviewMouseWheel.</item>
/// </list>
/// </para>
/// </summary>
public partial class SearchFilterBarControl : UserControl
{

    public static readonly DependencyProperty SearchTextProperty =
        DependencyProperty.Register(nameof(SearchText), typeof(string),
            typeof(SearchFilterBarControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty PlaceholderProperty =
        DependencyProperty.Register(nameof(Placeholder), typeof(string),
            typeof(SearchFilterBarControl),
            new PropertyMetadata("Поиск..."));

    public static readonly DependencyProperty FiltersProperty =
        DependencyProperty.Register(nameof(Filters), typeof(UIElement),
            typeof(SearchFilterBarControl), new PropertyMetadata(null));

    public static readonly DependencyProperty IsFloatingProperty =
        DependencyProperty.Register(nameof(IsFloating), typeof(bool),
            typeof(SearchFilterBarControl), new PropertyMetadata(false));

    public static readonly DependencyProperty ScrollTargetProperty =
        DependencyProperty.Register(nameof(ScrollTarget), typeof(ScrollViewer),
            typeof(SearchFilterBarControl),
            new PropertyMetadata(null, OnScrollTargetChanged));


    /// <summary>Текст поиска — биндится к VM.SearchText.</summary>
    public string SearchText
    {
        get => (string)GetValue(SearchTextProperty);
        set => SetValue(SearchTextProperty, value);
    }

    /// <summary>Текст-подсказка (маска поля ввода).</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Произвольный XAML-контент (ComboBox-ы и пр.) справа от поиска.</summary>
    public UIElement? Filters
    {
        get => (UIElement?)GetValue(FiltersProperty);
        set => SetValue(FiltersProperty, value);
    }

    /// <summary>
    /// Если True — панель плавающая: добавляется тень и Panel.ZIndex=1.
    /// Задаётся в XAML или code-behind родителя.
    /// </summary>
    public bool IsFloating
    {
        get => (bool)GetValue(IsFloatingProperty);
        set => SetValue(IsFloatingProperty, value);
    }

    /// <summary>
    /// ScrollViewer, которому пробрасывается PreviewMouseWheel
    /// (чтобы прокрутка работала когда мышь над плавающей панелью).
    /// </summary>
    public ScrollViewer? ScrollTarget
    {
        get => (ScrollViewer?)GetValue(ScrollTargetProperty);
        set => SetValue(ScrollTargetProperty, value);
    }


    private static readonly SolidColorBrush _focusBorderBrush = new(Colors.Black);
    private static readonly SolidColorBrush _normalBorderBrush = new(Colors.Transparent);
    private static readonly SolidColorBrush _focusBg = new(Colors.White);
    private static readonly SolidColorBrush _normalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));



    public SearchFilterBarControl()
    {
        InitializeComponent();
        PreviewMouseWheel += OnFilterBarPreviewMouseWheel;
    }

    private static void OnScrollTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SearchFilterBarControl bar) return;
        if (e.OldValue is ScrollViewer oldSv)
            oldSv.PreviewMouseWheel -= bar.OnScrollTargetPreviewMouseWheel;
        if (e.NewValue is ScrollViewer newSv)
            newSv.PreviewMouseWheel += bar.OnScrollTargetPreviewMouseWheel;
    }

    private void OnFilterBarPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FormComboHelpers.IsMouseWheelOverOpenComboBox(e)
            || FormComboHelpers.HasAnyDropDownOpen(FiltersPresenter))
            return;

        if (ScrollTarget is not { } sv) return;
        sv.ScrollToVerticalOffset(sv.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void OnScrollTargetPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (FormComboHelpers.HasAnyDropDownOpen(FiltersPresenter))
            e.Handled = true;
    }


    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.BorderBrush = _focusBorderBrush;
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.BorderBrush = _normalBorderBrush;
        SearchBorder.Effect = null;
    }


    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchText = string.Empty;
        SearchBox.Focus();
    }
}
