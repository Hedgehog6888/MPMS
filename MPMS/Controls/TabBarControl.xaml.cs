using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MPMS.Controls;

// ─── Модель одной вкладки ─────────────────────────────────────────────────────

/// <summary>Одна вкладка для TabBarControl.</summary>
public class TabItemModel
{
    /// <summary>Отображаемый текст кнопки.</summary>
    public string Text { get; set; } = "";

    /// <summary>Строковый ключ, который возвращается в TabBarControl.SelectedTab.</summary>
    public string Tag  { get; set; } = "";

    // Внутреннее — устанавливается контролом
    internal TabBarControl? Owner { get; set; }

    /// <summary>Привязывается к RadioButton.IsChecked.</summary>
    public bool IsSelected => Owner?.SelectedTab == Tag;
}

// ─── UserControl ──────────────────────────────────────────────────────────────

/// <summary>
/// Переиспользуемая панель вкладок.
/// Вкладки добавляются прямо в XAML:
/// <code>
///   &lt;controls:TabBarControl SelectedTab="{Binding ActiveTab, Mode=TwoWay}"&gt;
///       &lt;controls:TabItemModel Text="Материалы"   Tag="Materials"/&gt;
///       &lt;controls:TabItemModel Text="Оборудование" Tag="Equipment"/&gt;
///   &lt;/controls:TabBarControl&gt;
/// </code>
/// </summary>
[System.Windows.Markup.ContentProperty(nameof(Tabs))]
public partial class TabBarControl : UserControl
{
    // ── DependencyProperties ─────────────────────────────────────────

    public static readonly DependencyProperty SelectedTabProperty =
        DependencyProperty.Register(nameof(SelectedTab), typeof(string),
            typeof(TabBarControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedTabChanged));

    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.Register(nameof(GroupName), typeof(string),
            typeof(TabBarControl), new PropertyMetadata("TabGroup"));

    // ── Событие ──────────────────────────────────────────────────────

    /// <summary>Вызывается при смене выбранной вкладки.</summary>
    public event EventHandler<string>? SelectedTabChanged;

    // ── CLR коллекция (не DP, иначе XAML не может добавлять дочерние элементы) ─

    /// <summary>Список вкладок — добавляется в XAML как дочерние элементы.</summary>
    public ObservableCollection<TabItemModel> Tabs { get; } = new();

    // ── Properties ───────────────────────────────────────────────────

    /// <summary>Tag выбранной вкладки — двусторонний биндинг к VM.</summary>
    public string SelectedTab
    {
        get => (string)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    /// <summary>GroupName для RadioButton, чтобы на одной странице могли быть два TabBar.</summary>
    public string GroupName
    {
        get => (string)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    // ── Ctor ─────────────────────────────────────────────────────────

    public TabBarControl()
    {
        InitializeComponent();

        // Когда коллекция заполнена (после InitializeComponent) — привязать Owner
        Loaded += (_, _) => RefreshItems();
    }

    // ── Callbacks ────────────────────────────────────────────────────

    private static void OnSelectedTabChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var ctrl = (TabBarControl)d;
        ctrl.RefreshItems();
        ctrl.SelectedTabChanged?.Invoke(ctrl, (string)e.NewValue);
    }

    private void RefreshItems()
    {
        foreach (var tab in Tabs)
            tab.Owner = this;

        ItemsList.ItemsSource = null;
        ItemsList.ItemsSource = Tabs;
    }

    // ── Click handler ────────────────────────────────────────────────

    /// <summary>Вызывается при клике на RadioButton-вкладку.</summary>
    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            SelectedTab = tag;
    }
}
