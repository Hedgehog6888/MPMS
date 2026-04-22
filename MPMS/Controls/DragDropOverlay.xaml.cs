using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MPMS.Controls;

public partial class DragDropOverlay : UserControl
{
    public static readonly DependencyProperty UploadCommandProperty =
        DependencyProperty.Register(nameof(UploadCommand), typeof(ICommand), typeof(DragDropOverlay));

    public ICommand UploadCommand
    {
        get => (ICommand)GetValue(UploadCommandProperty);
        set => SetValue(UploadCommandProperty, value);
    }

    private DispatcherTimer _dragTimer;
    private bool _isDragging;

    public DragDropOverlay()
    {
        InitializeComponent();
        
        _dragTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _dragTimer.Tick += (s, e) => HideOverlay();

        this.Loaded += DragDropOverlay_Loaded;
    }

    private void DragDropOverlay_Loaded(object sender, RoutedEventArgs e)
    {
        var parent = this.Parent as UIElement;
        if (parent != null)
        {
            // Убеждаемся, что родитель принимает Drop, иначе события не будут возникать
            parent.AllowDrop = true;
            
            // Подписываемся на туннельные события родителя
            parent.PreviewDragEnter += Parent_PreviewDragEnter;
            parent.PreviewDragOver += Parent_PreviewDragEnter; 
        }
    }

    private void Parent_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ShowOverlay();
            // Важно: если оверлей только что стал видимым, система Drag&Drop может 
            // не сразу понять, что теперь он - главный таргет. 
            // Устанавливаем эффекты сразу здесь для родителя.
            e.Effects = DragDropEffects.Copy;
        }
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ShowOverlay();
            e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            ShowOverlay();
            e.Effects = DragDropEffects.Copy;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        _dragTimer.Start();
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        _dragTimer.Stop();
        HideOverlay();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && UploadCommand != null)
            {
                if (UploadCommand.CanExecute(files))
                {
                    UploadCommand.Execute(files);
                }
            }
        }
        e.Handled = true;
    }

    public void ShowOverlay()
    {
        _dragTimer.Stop();
        if (!_isDragging)
        {
            _isDragging = true;
            this.Visibility = Visibility.Visible;
            
            var showAnim = (Storyboard)Resources["OverlayShowStoryboard"];
            var dashAnim = (Storyboard)Resources["DashedAnimation"];
            
            showAnim.Begin(RootGrid);
            dashAnim.Begin(DashedBorder);
        }
    }

    public void HideOverlay()
    {
        _dragTimer.Stop();
        _isDragging = false;
        this.Visibility = Visibility.Collapsed;
        RootGrid.Opacity = 0;
    }
}
