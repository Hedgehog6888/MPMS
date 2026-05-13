using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace MPMS.Controls;


/// <summary>Одна вкладка для TabBarControl.</summary>
public class TabItemModel
{
    public string Text { get; set; } = "";

    public string Tag { get; set; } = "";

    public bool IsVisible { get; set; } = true;

    internal TabBarControl? Owner { get; set; }

    public bool IsSelected => Owner?.SelectedTab == Tag;
}


/// <summary>
/// Переиспользуемая панель вкладок.
/// Вкладки добавляются прямо в XAML через свойство Tabs.
/// </summary>
[System.Windows.Markup.ContentProperty(nameof(Tabs))]
public partial class TabBarControl : UserControl
{

    public static readonly DependencyProperty SelectedTabProperty =
        DependencyProperty.Register(nameof(SelectedTab), typeof(string),
            typeof(TabBarControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedTabChanged));

    public static readonly DependencyProperty GroupNameProperty =
        DependencyProperty.Register(nameof(GroupName), typeof(string),
            typeof(TabBarControl), new PropertyMetadata("TabGroup"));


    /// <summary>Вызывается при смене выбранной вкладки.</summary>
    public event EventHandler<string>? SelectedTabChanged;


    public ObservableCollection<TabItemModel> Tabs { get; } = new();


    public string SelectedTab
    {
        get => (string)GetValue(SelectedTabProperty);
        set => SetValue(SelectedTabProperty, value);
    }

    public string GroupName
    {
        get => (string)GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }


    public TabBarControl()
    {
        InitializeComponent();

        Loaded += (_, _) => RefreshItems();
    }


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


    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string tag)
            SelectedTab = tag;
    }
}
