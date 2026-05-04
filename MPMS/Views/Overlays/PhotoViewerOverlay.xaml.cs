using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MPMS.Views.Overlays;

public partial class PhotoViewerOverlay : UserControl
{
    // ── File ───────────────────────────────────────────────────────────────
    private BitmapSource? _source;
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private bool _hasUnsavedChanges;

    // ── Zoom / Pan ─────────────────────────────────────────────────────────
    private double _zoomFactor = 1.0;
    private double _panX = 0, _panY = 0;
    private bool _isPanning = false;
    private Point _panStartPoint;
    private double _panStartX = 0, _panStartY = 0;
    private bool _viewportReady = false;

    // ── Rotation ───────────────────────────────────────────────────────────
    private double _rotationAngle = 0;

    // ── Drawing ────────────────────────────────────────────────────────────
    private enum DrawTool { Pencil, Marker, Eraser }
    private DrawTool _currentTool = DrawTool.Pencil;
    private Color _currentColor = Color.FromRgb(0x1E, 0x90, 0xFF);
    private double _brushSize = 3;
    private bool _isDrawing = false;
    private bool _drawingActive = false;   // true when a draw tool is selected
    private Polyline? _currentStroke;
    private readonly Stack<UIElement> _undoStack = new();
    private readonly Stack<UIElement> _redoStack = new();

    // ── Crop ───────────────────────────────────────────────────────────────
    private bool _cropMode = false;
    private bool _isDraggingCrop = false;
    private Rect _cropRect = new(50, 50, 400, 300);
    private enum CropHandle { None, TL, TR, BL, BR, Move }
    private CropHandle _cropDragHandle = CropHandle.None;
    private Point _cropDragLast;

    // ── Color palette ──────────────────────────────────────────────────────
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xFF,0xD7,0x00), Color.FromRgb(0xFF,0xA5,0x00),
        Color.FromRgb(0xFF,0x45,0x00), Color.FromRgb(0xDC,0x14,0x3C),
        Color.FromRgb(0x80,0x00,0x80), Color.FromRgb(0xC7,0x15,0x85),
        Color.FromRgb(0x00,0x80,0x00), Color.FromRgb(0x00,0x64,0x00),
        Color.FromRgb(0x00,0x00,0xFF), Color.FromRgb(0x1E,0x90,0xFF),
        Color.FromRgb(0x00,0xCE,0xD1), Color.FromRgb(0xFF,0xFF,0xFF),
        Color.FromRgb(0xC0,0xC0,0xC0), Color.FromRgb(0x40,0x40,0x40),
        Color.FromRgb(0xFF,0x69,0xB4), Color.FromRgb(0x8A,0x2B,0xE2),
    ];

    public PhotoViewerOverlay(string filePath)
    {
        InitializeComponent();
        _filePath = filePath;
        _fileName = System.IO.Path.GetFileName(filePath);
        LoadImage(filePath);
        BuildColorPalette();
        UpdateFileInfo();
        FileNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(_fileName);
        UpdateZoomDisplay();
        // Fit after first layout pass (viewport size is available)
        Loaded += (_, _) =>
        {
            FitImageToViewport();
            SyncDrawToolIcons();
        };
        // Handle Escape key to deactivate tools
        KeyDown += PhotoViewerOverlay_KeyDown;
    }

    private void PhotoViewerOverlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_drawingActive)
            {
                DeactivateTool();
                e.Handled = true;
            }
            else if (_cropMode)
            {
                CancelCrop_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    // ── Image loading ──────────────────────────────────────────────────────
    private void LoadImage(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _source = bmp;

            // Pin the image element to the exact pixel dimensions so the
            // ScaleTransform on ImageContainer operates predictably.
            double pw = bmp.PixelWidth;
            double ph = bmp.PixelHeight;
            MainImage.Source  = bmp;
            MainImage.Width   = pw;
            MainImage.Height  = ph;

            // ImageContainer and DrawCanvas must match so the Canvas covers the image.
            ImageContainer.Width  = pw;
            ImageContainer.Height = ph;
            DrawCanvas.Width      = pw;
            DrawCanvas.Height     = ph;

            _viewportReady = false;
        }
        catch
        {
            MessageBox.Show("Не удалось загрузить изображение.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            MainWindow.Instance?.HidePhotoViewer();
        }
    }

    private void UpdateFileInfo()
    {
        if (_source == null) return;
        long sizeBytes = 0;
        try { sizeBytes = new FileInfo(_filePath).Length; } catch { }
        string sizeStr = sizeBytes > 1024 * 1024
            ? $"{sizeBytes / 1024.0 / 1024.0:F1} МБ"
            : $"{sizeBytes / 1024.0:F1} КБ";

        ResolutionText.Text    = $"{_source.PixelWidth} × {_source.PixelHeight}";
        FileSizeText.Text      = sizeStr;

        var info = new FileInfo(_filePath);
        CreatedText.Text  = info.CreationTime.ToString("d MMMM yyyy г. HH:mm",
            new System.Globalization.CultureInfo("ru-RU"));
        ModifiedText.Text = info.LastWriteTime.ToString("d MMMM yyyy г. HH:mm",
            new System.Globalization.CultureInfo("ru-RU"));

        // Read EXIF camera info
        CameraText.Text = GetCameraInfo();
    }

    private string GetCameraInfo()
    {
        try
        {
            using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read);
            var decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            if (decoder.Frames[0].Metadata == null) return "—";

            var metadata = (BitmapMetadata)decoder.Frames[0].Metadata;
            string camera = "";
            string model = "";

            if (metadata.ContainsQuery("System.Photo.CameraManufacturer"))
                camera = metadata.GetQuery("System.Photo.CameraManufacturer")?.ToString() ?? "";

            if (metadata.ContainsQuery("System.Photo.CameraModel"))
                model = metadata.GetQuery("System.Photo.CameraModel")?.ToString() ?? "";

            if (!string.IsNullOrEmpty(camera) && !string.IsNullOrEmpty(model))
                return $"{camera} {model}";
            else if (!string.IsNullOrEmpty(model))
                return model;
            else if (!string.IsNullOrEmpty(camera))
                return camera;

            return "—";
        }
        catch
        {
            return "—";
        }
    }

    // ── Fit to viewport ────────────────────────────────────────────────────
    private void FitImageToViewport()
    {
        if (_source == null) return;

        // Wait until the viewport has been measured.
        double vw = ViewportGrid.ActualWidth;
        double vh = ViewportGrid.ActualHeight;
        if (vw < 1 || vh < 1) return;

        const double padding = 32;
        double availW = vw - padding;
        double availH = vh - padding;

        double scale = Math.Min(availW / _source.PixelWidth,
                                availH / _source.PixelHeight);
        scale = Math.Max(0.01, scale);

        _zoomFactor = scale;
        
        // Exact formula to perfectly center a visual that has RenderTransformOrigin="0.5,0.5"
        _panX = (vw - _source.PixelWidth) / 2.0;
        _panY = (vh - _source.PixelHeight) / 2.0;

        ApplyTransform();
        UpdateZoomDisplay();
        _viewportReady = true;
    }

    private void ViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_source != null)
            FitImageToViewport();
    }

    // ── Transform ──────────────────────────────────────────────────────────
    private void ApplyTransform()
    {
        ImgScale.ScaleX = _zoomFactor;
        ImgScale.ScaleY = _zoomFactor;
        ImgRotate.Angle = _rotationAngle;
        ImgTranslate.X = _panX;
        ImgTranslate.Y = _panY;
    }

    private void UpdateZoomDisplay()
    {
        if (ZoomText == null || ZoomSlider == null) return;
        ZoomText.Text = $"{(int)(_zoomFactor * 100)} %";
        // Prevent recursive ValueChanged
        ZoomSlider.ValueChanged -= ZoomSlider_ValueChanged;
        ZoomSlider.Value = Math.Clamp(_zoomFactor * 100, ZoomSlider.Minimum, ZoomSlider.Maximum);
        ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
    }

    // ── Close ──────────────────────────────────────────────────────────────
    private void Close_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HidePhotoViewer();

    // ── File name ──────────────────────────────────────────────────────────
    private void FileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _hasUnsavedChanges = true;
        if (SaveBtn != null) SaveBtn.IsEnabled = true;
    }

    // ── Description ────────────────────────────────────────────────────────
    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _hasUnsavedChanges = true;
        if (SaveBtn != null) SaveBtn.IsEnabled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_filePath) ?? "";
            var ext = System.IO.Path.GetExtension(_filePath);
            var newName = FileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            var newPath = System.IO.Path.Combine(dir, newName + ext);
            if (newPath != _filePath) File.Move(_filePath, newPath);
            _filePath = newPath;
            _hasUnsavedChanges = false;
            SaveBtn.IsEnabled = false;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Zoom (slider + buttons) ────────────────────────────────────────────
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ZoomText == null) return;
        _zoomFactor = e.NewValue / 100.0;
        _panX = 0; _panY = 0;
        ApplyTransform();
        UpdateZoomDisplay();
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e)
        => ZoomBy(1.15, null);

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
        => ZoomBy(1.0 / 1.15, null);

    // Zoom centred on an optional viewport point; null = viewport centre
    private void ZoomBy(double factor, Point? viewportPoint)
    {
        double newZoom = Math.Clamp(_zoomFactor * factor, 0.05, 10.0);

        if (viewportPoint.HasValue && ViewportGrid.ActualWidth > 0 && _source != null)
        {
            double ratio = newZoom / _zoomFactor;
            
            // Image center before transform (because RenderTransformOrigin="0.5,0.5")
            double cx = _source.PixelWidth / 2.0;
            double cy = _source.PixelHeight / 2.0;
            
            // Target point on the viewport
            double vx = viewportPoint.Value.X;
            double vy = viewportPoint.Value.Y;

            // Pan shifts the center. Scale pushes everything outward from the center.
            // This mathematically preserves the point 'vx, vy' on the screen.
            _panX = _panX * ratio + (vx - cx) * (1 - ratio);
            _panY = _panY * ratio + (vy - cy) * (1 - ratio);
        }
        else if (_source != null)
        {
             // If zooming by buttons (no mouse point), zoom to viewport center
             double ratio = newZoom / _zoomFactor;
             double cx = _source.PixelWidth / 2.0;
             double cy = _source.PixelHeight / 2.0;
             double vx = ViewportGrid.ActualWidth / 2.0;
             double vy = ViewportGrid.ActualHeight / 2.0;
             
             _panX = _panX * ratio + (vx - cx) * (1 - ratio);
             _panY = _panY * ratio + (vy - cy) * (1 - ratio);
        }

        _zoomFactor = newZoom;
        ApplyTransform();
        UpdateZoomDisplay();
    }

    // ── Mouse wheel: zoom to cursor ────────────────────────────────────────
    private void DrawCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Position relative to ViewportGrid
        var pos = e.GetPosition(ViewportGrid);
        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        ZoomBy(factor, pos);
        e.Handled = true;
    }

    // ── Rotation ───────────────────────────────────────────────────────────
    private void RotateCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle + 90) % 360;
        ApplyTransform();
        MarkDirty();
    }

    private void RotateCCW_Click(object sender, RoutedEventArgs e)
    {
        _rotationAngle = (_rotationAngle - 90 + 360) % 360;
        ApplyTransform();
        MarkDirty();
    }

    private void MarkDirty()
    {
        _hasUnsavedChanges = true;
        if (SaveBtn != null) SaveBtn.IsEnabled = true;
    }

    // ── Print ──────────────────────────────────────────────────────────────
    private void Print_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(_filePath)
            {
                Verb = "print",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка печати: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Drawing tool selection ─────────────────────────────────────────────
    private void SelectPencil_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTool == DrawTool.Pencil && _drawingActive)
            DeactivateTool();
        else
            ActivateTool(DrawTool.Pencil);
    }
    private void SelectMarker_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTool == DrawTool.Marker && _drawingActive)
            DeactivateTool();
        else
            ActivateTool(DrawTool.Marker);
    }
    private void SelectEraser_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTool == DrawTool.Eraser && _drawingActive)
            DeactivateTool();
        else
            ActivateTool(DrawTool.Eraser);
    }

    private void ActivateTool(DrawTool tool)
    {
        _drawingActive = true;
        _currentTool = tool;
        DrawCanvas.Cursor = Cursors.Cross;
        UpdateToolHighlight();
        SyncDrawToolIcons();
    }

    private void DeactivateTool()
    {
        _drawingActive = false;
        DrawCanvas.Cursor = Cursors.Hand;
        UpdateToolHighlight();
        SyncDrawToolIcons();
    }

    private void UpdateToolHighlight()
    {
        var pencilBtn = FindName("PencilBtn") as RadioButton;
        var markerBtn = FindName("MarkerBtn") as RadioButton;
        var eraserBtn = FindName("EraserBtn") as RadioButton;

        if (pencilBtn != null) pencilBtn.IsChecked = (_drawingActive && _currentTool == DrawTool.Pencil);
        if (markerBtn != null) markerBtn.IsChecked = (_drawingActive && _currentTool == DrawTool.Marker);
        if (eraserBtn != null) eraserBtn.IsChecked = (_drawingActive && _currentTool == DrawTool.Eraser);
    }

    private void SyncDrawToolIcons()
    {
        // Не вызывать из Checked/Unchecked радиокнопки: при IsChecked из XAML событие идёт до IComponentConnector для Image.
        var pencilBtn = FindName("PencilBtn") as RadioButton;
        var markerBtn = FindName("MarkerBtn") as RadioButton;
        var eraserBtn = FindName("EraserBtn") as RadioButton;
        var pencilIconDark = FindName("PencilIconDark") as Image;
        var pencilIconLight = FindName("PencilIconLight") as Image;
        var markerIconDark = FindName("MarkerIconDark") as Image;
        var markerIconLight = FindName("MarkerIconLight") as Image;
        var eraserIconDark = FindName("EraserIconDark") as Image;
        var eraserIconLight = FindName("EraserIconLight") as Image;

        if (pencilBtn is null || pencilIconDark is null || pencilIconLight is null
            || markerBtn is null || markerIconDark is null || markerIconLight is null
            || eraserBtn is null || eraserIconDark is null || eraserIconLight is null)
            return;

        bool pencilOn = pencilBtn.IsChecked == true;
        pencilIconDark.Visibility = pencilOn ? Visibility.Collapsed : Visibility.Visible;
        pencilIconLight.Visibility = pencilOn ? Visibility.Visible : Visibility.Collapsed;

        bool markerOn = markerBtn.IsChecked == true;
        markerIconDark.Visibility = markerOn ? Visibility.Collapsed : Visibility.Visible;
        markerIconLight.Visibility = markerOn ? Visibility.Visible : Visibility.Collapsed;

        bool eraserOn = eraserBtn.IsChecked == true;
        eraserIconDark.Visibility = eraserOn ? Visibility.Collapsed : Visibility.Visible;
        eraserIconLight.Visibility = eraserOn ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _brushSize = e.NewValue;
        if (BrushSizeLabel != null)
            BrushSizeLabel.Text = ((int)_brushSize).ToString();
    }

    // ── Mouse events on DrawCanvas ─────────────────────────────────────────
    private void DrawCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        if (_cropMode)
        {
            CropCanvas_MouseDown(sender, e);
            return;
        }

        if (_drawingActive)
        {
            // Start drawing stroke
            _isDrawing = true;
            _redoStack.Clear();
            var pos = e.GetPosition(DrawCanvas);

            _currentStroke = new Polyline
            {
                Stroke = _currentTool == DrawTool.Eraser
                    ? new SolidColorBrush(Color.FromRgb(244, 245, 247)) // bg colour (#F4F5F7)
                    : new SolidColorBrush(_currentColor),
                StrokeThickness = _currentTool == DrawTool.Marker ? _brushSize * 3 : _brushSize,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Opacity = _currentTool == DrawTool.Marker ? 0.5 : 1.0,
            };
            _currentStroke.Points.Add(pos);
            DrawCanvas.Children.Add(_currentStroke);
            DrawCanvas.CaptureMouse();
            e.Handled = true;
        }
        else
        {
            // Pan mode
            _isPanning = true;
            _panStartPoint = e.GetPosition(ViewportGrid);
            _panStartX = _panX;
            _panStartY = _panY;
            DrawCanvas.CaptureMouse();
            DrawCanvas.Cursor = Cursors.ScrollAll;
            e.Handled = true;
        }
    }

    private void DrawCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_cropMode) { CropCanvas_MouseMove(sender, e); return; }

        if (_isDrawing && _currentStroke != null)
        {
            _currentStroke.Points.Add(e.GetPosition(DrawCanvas));
        }
        else if (_isPanning)
        {
            var pos = e.GetPosition(ViewportGrid);
            _panX = _panStartX + (pos.X - _panStartPoint.X);
            _panY = _panStartY + (pos.Y - _panStartPoint.Y);
            ApplyTransform();
        }
    }

    private void DrawCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_cropMode) { CropCanvas_MouseUp(sender, e); return; }

        if (_isDrawing)
        {
            _isDrawing = false;
            DrawCanvas.ReleaseMouseCapture();
            if (_currentStroke != null)
            {
                _undoStack.Push(_currentStroke);
                MarkDirty();
            }
            _currentStroke = null;
        }
        else if (_isPanning)
        {
            _isPanning = false;
            DrawCanvas.ReleaseMouseCapture();
            DrawCanvas.Cursor = _drawingActive ? Cursors.Cross : Cursors.Hand;
        }
    }

    // ── Undo / Redo ────────────────────────────────────────────────────────
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;
        var el = _undoStack.Pop();
        DrawCanvas.Children.Remove(el);
        _redoStack.Push(el);
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;
        var el = _redoStack.Pop();
        DrawCanvas.Children.Add(el);
        _undoStack.Push(el);
    }

    // ── Color palette ──────────────────────────────────────────────────────
    private void BuildColorPalette()
    {
        ColorPalettePanel.Children.Clear();
        foreach (var color in Palette)
        {
            var border = new Border
            {
                Width = 24, Height = 24,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(color),
                Margin = new Thickness(1),
                Cursor = Cursors.Hand,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            };
            var c = color;
            border.MouseLeftButtonDown += (_, _) =>
            {
                // Animate shrink
                border.Width = 20;
                border.Height = 20;
                _currentColor = c;
                HighlightSelectedColor(border);
                // Auto-activate pencil on colour pick
                if (!_drawingActive) ActivateTool(DrawTool.Pencil);
            };
            border.MouseLeftButtonUp += (_, _) =>
            {
                // Animate back to normal
                border.Width = 24;
                border.Height = 24;
            };
            ColorPalettePanel.Children.Add(border);
        }
        SelectColorByIndex(9); // default blue
    }

    private void SelectColorByIndex(int idx)
    {
        if (idx < ColorPalettePanel.Children.Count)
            HighlightSelectedColor((Border)ColorPalettePanel.Children[idx]);
    }

    private void HighlightSelectedColor(Border selected)
    {
        foreach (Border b in ColorPalettePanel.Children.OfType<Border>())
        {
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200));
        }
        selected.BorderThickness = new Thickness(2);
        selected.BorderBrush = new SolidColorBrush(Colors.Black);
    }

    // ── Crop Mode ──────────────────────────────────────────────────────────
    private void ToggleCrop_Click(object sender, RoutedEventArgs e)
    {
        _cropMode = !_cropMode;
        CropOverlayGrid.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        DrawingToolsPanel.Visibility = _cropMode ? Visibility.Collapsed : Visibility.Visible;
        CropToolsPanel.Visibility = _cropMode ? Visibility.Visible : Visibility.Collapsed;
        if (_cropMode)
        {
            DeactivateTool();
            InitCropRect();
            UpdateCropVisuals();
        }
    }

    private void InitCropRect()
    {
        double w = DrawCanvas.ActualWidth;
        double h = DrawCanvas.ActualHeight;
        _cropRect = new Rect(w * 0.1, h * 0.1, w * 0.8, h * 0.8);
    }

    private void UpdateCropVisuals()
    {
        if (CropOverlayGrid.Visibility != Visibility.Visible) return;
        double w = DrawCanvas.ActualWidth;
        double h = DrawCanvas.ActualHeight;

        DimTop.Height = _cropRect.Top; DimTop.Width = w;
        Canvas.SetLeft(DimTop, 0); Canvas.SetTop(DimTop, 0);

        DimBottom.Height = Math.Max(0, h - _cropRect.Bottom); DimBottom.Width = w;
        Canvas.SetLeft(DimBottom, 0); Canvas.SetTop(DimBottom, _cropRect.Bottom);

        DimLeft.Width = _cropRect.Left; DimLeft.Height = _cropRect.Height;
        Canvas.SetLeft(DimLeft, 0); Canvas.SetTop(DimLeft, _cropRect.Top);

        DimRight.Width = Math.Max(0, w - _cropRect.Right); DimRight.Height = _cropRect.Height;
        Canvas.SetLeft(DimRight, _cropRect.Right); Canvas.SetTop(DimRight, _cropRect.Top);

        Canvas.SetLeft(CropBorder, _cropRect.Left); Canvas.SetTop(CropBorder, _cropRect.Top);
        CropBorder.Width = _cropRect.Width; CropBorder.Height = _cropRect.Height;

        PlaceCropHandle(HandleTL, _cropRect.Left - 5, _cropRect.Top - 5);
        PlaceCropHandle(HandleTR, _cropRect.Right - 5, _cropRect.Top - 5);
        PlaceCropHandle(HandleBL, _cropRect.Left - 5, _cropRect.Bottom - 5);
        PlaceCropHandle(HandleBR, _cropRect.Right - 5, _cropRect.Bottom - 5);

        CropSizeText.Text = $"{(int)_cropRect.Width} × {(int)_cropRect.Height}";
    }

    private static void PlaceCropHandle(UIElement el, double x, double y)
    {
        Canvas.SetLeft(el, x); Canvas.SetTop(el, y);
    }

    private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);
        _cropDragHandle = GetCropHandle(pos);
        _cropDragLast = pos;
        _isDraggingCrop = true;
        DrawCanvas.CaptureMouse();
    }

    private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingCrop) return;
        var pos = e.GetPosition(DrawCanvas);
        double dx = pos.X - _cropDragLast.X, dy = pos.Y - _cropDragLast.Y;

        _cropRect = _cropDragHandle switch
        {
            CropHandle.Move => new Rect(_cropRect.X + dx, _cropRect.Y + dy, _cropRect.Width, _cropRect.Height),
            CropHandle.TL   => new Rect(_cropRect.X + dx, _cropRect.Y + dy, _cropRect.Width - dx, _cropRect.Height - dy),
            CropHandle.TR   => new Rect(_cropRect.X, _cropRect.Y + dy, _cropRect.Width + dx, _cropRect.Height - dy),
            CropHandle.BL   => new Rect(_cropRect.X + dx, _cropRect.Y, _cropRect.Width - dx, _cropRect.Height + dy),
            CropHandle.BR   => new Rect(_cropRect.X, _cropRect.Y, _cropRect.Width + dx, _cropRect.Height + dy),
            _               => _cropRect
        };

        ClampCropRect();
        _cropDragLast = pos;
        UpdateCropVisuals();
    }

    private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingCrop = false;
        DrawCanvas.ReleaseMouseCapture();
    }

    private CropHandle GetCropHandle(Point p)
    {
        const double hit = 18;
        if (IsNear(p, _cropRect.TopLeft, hit))     return CropHandle.TL;
        if (IsNear(p, _cropRect.TopRight, hit))    return CropHandle.TR;
        if (IsNear(p, _cropRect.BottomLeft, hit))  return CropHandle.BL;
        if (IsNear(p, _cropRect.BottomRight, hit)) return CropHandle.BR;
        if (_cropRect.Contains(p))                 return CropHandle.Move;
        return CropHandle.None;
    }

    private static bool IsNear(Point a, Point b, double d)
        => Math.Abs(a.X - b.X) < d && Math.Abs(a.Y - b.Y) < d;

    private void ClampCropRect()
    {
        double w = Math.Max(40, _cropRect.Width);
        double h = Math.Max(40, _cropRect.Height);
        double x = Math.Max(0, Math.Min(DrawCanvas.ActualWidth - w, _cropRect.X));
        double y = Math.Max(0, Math.Min(DrawCanvas.ActualHeight - h, _cropRect.Y));
        _cropRect = new Rect(x, y, w, h);
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e) => ToggleCrop_Click(sender, e);

    private void CancelCrop_Click(object sender, RoutedEventArgs e)
    {
        _cropMode = false;
        CropOverlayGrid.Visibility = Visibility.Collapsed;
        DrawingToolsPanel.Visibility = Visibility.Visible;
        CropToolsPanel.Visibility = Visibility.Collapsed;
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e) => InitCropRect();

    private void DrawCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_cropMode) UpdateCropVisuals();
    }
}
