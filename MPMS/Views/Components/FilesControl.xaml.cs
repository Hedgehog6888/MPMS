using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MPMS.ViewModels;

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

    private FilesControlViewModel? _vm;

    public FilesControl()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        AttachViewModel(e.NewValue as FilesControlViewModel);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachViewModel(DataContext as FilesControlViewModel);
        UpdateScrollTarget();
    }

    private void AttachViewModel(FilesControlViewModel? vm)
    {
        if (ReferenceEquals(vm, _vm)) return;
        if (_vm != null) _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = vm;
        if (_vm != null) _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Переключение вкладки меняет видимый список — обновляем цель прокрутки
        // плавающей панели поиска (на следующем проходе компоновки).
        if (e.PropertyName == nameof(FilesControlViewModel.CurrentTab))
            Dispatcher.BeginInvoke(new Action(UpdateScrollTarget), DispatcherPriority.Background);
    }

    private void UpdateScrollTarget()
    {
        var list = _vm?.CurrentTab == "Documents" ? DocumentsList : ImagesList;
        if (list == null) return;

        list.ApplyTemplate();
        var scrollViewer = FindVisualChild<ScrollViewer>(list);
        if (scrollViewer != null)
            FilesSearchBar.ScrollTarget = scrollViewer;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) return typed;
            var result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
}
