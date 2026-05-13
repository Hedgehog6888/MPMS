using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;

namespace MPMS.Views.Overlays;

public partial class PhotoViewerOverlay : UserControl
{
    // ── Файл 
    private BitmapSource? _source;
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private string? _description;
    private string? _uploadedByName;
    private Guid _uploadedById = Guid.Empty;
    private byte[]? _uploadedByAvatarData;
    private string? _uploadedByAvatarPath;
    private Guid _projectId = Guid.Empty;
    private bool _hasUnsavedChanges;
    private bool _hasImageChanges;
    private readonly Func<string, string, string?, Task>? _savedFileHandler;

    // ── Масштаб / Панорамирование
    private double _zoomFactor = 1.0;
    private double _panX = 0, _panY = 0;
    private bool _isPanning = false;
    private Point _panStartPoint;
    private double _panStartX = 0, _panStartY = 0;
    private bool _viewportReady = false;

    // ── Вращение
    private double _rotationAngle = 0;

    // ── Рисование
    private enum DrawTool { Pencil, Marker, Eraser }
    private DrawTool _currentTool = DrawTool.Pencil;
    private Color _currentColor = Color.FromRgb(0x1E, 0x90, 0xFF);
    private double _brushSize = 3;
    private bool _isDrawing = false;
    private bool _drawingActive = false; 
    private Polyline? _currentStroke;
    private readonly Stack<UIElement> _undoStack = new();
    private readonly Stack<UIElement> _redoStack = new();
    private Point? _lastEraserPoint;

    // ── Обрезка
    private bool _cropMode = false;
    private bool _isDraggingCrop = false;
    private Rect _cropRect = new(50, 50, 400, 300);
    private enum CropHandle { None, Move, New, TL, T, TR, R, BR, B, BL, L }
    private CropHandle _cropDragHandle = CropHandle.None;
    private Point _cropDragStart;
    private Rect _cropStartRect;
    private enum AspectRatio { Free, Original, Square, Ratio_9_16, Ratio_16_9, Ratio_4_5, Ratio_5_4, Ratio_3_4, Ratio_4_3, Ratio_1_1, Ratio_3_2 }
    private AspectRatio _currentAspectRatio = AspectRatio.Free;

    // ── Палитра цветов
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

    public PhotoViewerOverlay(string filePath, string? displayFileName = null, string? description = null, string? uploadedByName = null, Guid uploadedById = default, byte[]? uploadedByAvatarData = null, string? uploadedByAvatarPath = null, Guid projectId = default, Func<string, string, string?, Task>? savedFileHandler = null)
    {
        InitializeComponent();
        _filePath = filePath;
        _fileName = string.IsNullOrWhiteSpace(displayFileName) ? System.IO.Path.GetFileName(filePath) : displayFileName;
        _description = description;
        _uploadedByName = uploadedByName;
        _uploadedById = uploadedById;
        _uploadedByAvatarData = uploadedByAvatarData;
        _uploadedByAvatarPath = uploadedByAvatarPath;
        _projectId = projectId;
        _savedFileHandler = savedFileHandler;
        LoadImage(filePath);
        BuildColorPalette();
        UpdateFileInfo();
        FileNameBox.Text = System.IO.Path.GetFileNameWithoutExtension(_fileName);
        DescriptionBox.Text = _description ?? string.Empty;
        UpdateZoomDisplay();
        _hasUnsavedChanges = false;
        Loaded += (_, _) =>
        {
            FitImageToViewport();
            SyncDrawToolIcons();
        };
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
        else if (e.Key == Key.Enter && _cropMode)
        {
            ApplyCrop_Click(sender, e);
            e.Handled = true;
        }
    }

    // ── Загрузка изображения
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

            double pw = bmp.PixelWidth;
            double ph = bmp.PixelHeight;
            MainImage.Source = bmp;
            MainImage.Width = pw;
            MainImage.Height = ph;

            // ImageContainer и DrawCanvas должны совпадать, чтобы Canvas покрывал изображение.
            ImageContainer.Width = pw;
            ImageContainer.Height = ph;
            DrawCanvas.Width = pw;
            DrawCanvas.Height = ph;

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

        ResolutionText.Text = $"{_source.PixelWidth} × {_source.PixelHeight}";
        FileSizeText.Text = sizeStr;

        var info = new FileInfo(_filePath);
        CreatedText.Text = info.CreationTime.ToString("d MMMM yyyy г. HH:mm",
            new System.Globalization.CultureInfo("ru-RU"));
        ModifiedText.Text = info.LastWriteTime.ToString("d MMMM yyyy г. HH:mm",
            new System.Globalization.CultureInfo("ru-RU"));

        CameraText.Text = GetCameraInfo();

        var uploadedByText = FindName("UploadedByText") as TextBlock;
        if (uploadedByText != null)
            uploadedByText.Text = _uploadedByName ?? "—";

        var uploadedByAvatarImage = FindName("UploadedByAvatarImage") as Image;
        var uploadedByAvatarBorder = FindName("UploadedByAvatarBorder") as Border;

        if (uploadedByAvatarImage != null && uploadedByAvatarBorder != null)
        {
            var avatarSource = Services.AvatarHelper.GetImageSource(_uploadedByAvatarData, _uploadedByAvatarPath, _uploadedByName, 32);
            if (avatarSource != null)
            {
                uploadedByAvatarImage.Source = avatarSource;
                uploadedByAvatarBorder.Background = Brushes.Transparent;
            }
            else
            {
                uploadedByAvatarImage.Source = null;
                uploadedByAvatarBorder.Background = new SolidColorBrush(Color.FromRgb(0x34, 0x49, 0x5E));
            }
        }
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

    // ── Подгонка к вьюпорту
    private void FitImageToViewport()
    {
        if (_source == null) return;

        // Подождать, пока вьюпорт будет измерен.
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

    // ── Преобразование
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
        // Предотвратить рекурсивный ValueChanged
        ZoomSlider.ValueChanged -= ZoomSlider_ValueChanged;
        ZoomSlider.Value = Math.Clamp(_zoomFactor * 100, ZoomSlider.Minimum, ZoomSlider.Maximum);
        ZoomSlider.ValueChanged += ZoomSlider_ValueChanged;
    }

    // ── Закрытие
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            ConfirmPopupBorder.Visibility = Visibility.Visible;
        }
        else
        {
            MainWindow.Instance?.HidePhotoViewer();
        }
    }

    private void ConfirmSave_Click(object sender, RoutedEventArgs e)
    {
        Save_Click(sender, e);
        ConfirmPopupBorder.Visibility = Visibility.Collapsed;
        MainWindow.Instance?.HidePhotoViewer();
    }

    private void ConfirmDiscard_Click(object sender, RoutedEventArgs e)
    {
        ConfirmPopupBorder.Visibility = Visibility.Collapsed;
        MainWindow.Instance?.HidePhotoViewer();
    }

    // ── Имя файла
    private void FileNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _hasUnsavedChanges = true;
        if (SaveBtn != null) SaveBtn.IsEnabled = true;
    }

    // ── Описание 
    private void DescriptionBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _hasUnsavedChanges = true;
        if (SaveBtn != null) SaveBtn.IsEnabled = true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_filePath) ?? "";
            var ext = System.IO.Path.GetExtension(_filePath);
            var newName = FileNameBox.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;
            var newPath = System.IO.Path.Combine(dir, newName + ext);
            if (!string.Equals(newPath, _filePath, StringComparison.OrdinalIgnoreCase)) File.Move(_filePath, newPath);
            _filePath = newPath;
            _fileName = System.IO.Path.GetFileName(_filePath);
            if (_hasImageChanges)
            {
                SaveEditedImageToFile(_filePath);
                _hasImageChanges = false;
            }
            _description = DescriptionBox.Text?.Trim();
            if (_savedFileHandler is not null)
                await _savedFileHandler(_filePath, _fileName, _description);
            _hasUnsavedChanges = false;
            SaveBtn.IsEnabled = false;
            UpdateFileInfo();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Масштаб (слайдер + кнопки)
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

    // Масштабирование с центрированием на опциональной точке вьюпорта; null = центр вьюпорта
    private void ZoomBy(double factor, Point? viewportPoint)
    {
        double newZoom = Math.Clamp(_zoomFactor * factor, 0.05, 10.0);

        if (viewportPoint.HasValue && ViewportGrid.ActualWidth > 0 && _source != null)
        {
            double ratio = newZoom / _zoomFactor;

            double cx = _source.PixelWidth / 2.0;
            double cy = _source.PixelHeight / 2.0;

            double vx = viewportPoint.Value.X;
            double vy = viewportPoint.Value.Y;

            _panX = _panX * ratio + (vx - cx) * (1 - ratio);
            _panY = _panY * ratio + (vy - cy) * (1 - ratio);
        }
        else if (_source != null)
        {
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

    // ── Колёсико мыши: масштабирование к курсору
    private void DrawCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Позиция относительно ViewportGrid
        var pos = e.GetPosition(ViewportGrid);
        double factor = e.Delta > 0 ? 1.12 : 1.0 / 1.12;
        ZoomBy(factor, pos);
        e.Handled = true;
    }

    // ── Вращение
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
        _hasImageChanges = true;
        if (SaveBtn != null) SaveBtn.IsEnabled = true;
    }

    // ── Печать
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

    // ── Выбор инструмента рисования
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
        DrawCanvas.Cursor = Cursors.None;
        UpdateCursorCircle();
        UpdateToolHighlight();
        SyncDrawToolIcons();
    }

    private void DeactivateTool()
    {
        _drawingActive = false;
        DrawCanvas.Cursor = Cursors.Hand;
        CursorCircle.Visibility = Visibility.Collapsed;
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
        UpdateCursorCircle();
    }

    private void UpdateCursorCircle()
    {
        if (!_drawingActive) return;

        double size = _currentTool switch
        {
            DrawTool.Marker => _brushSize * 3,
            DrawTool.Eraser => FixedEraserSize,
            _ => _brushSize
        };
        size = Math.Max(size, 8);
        CursorCircle.Width = size;
        CursorCircle.Height = size;
        CursorCircle.Visibility = Visibility.Visible;
    }

    private void DrawCanvas_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_drawingActive)
        {
            DrawCanvas.Cursor = Cursors.None;
            CursorCircle.Visibility = Visibility.Visible;
        }
    }

    private void DrawCanvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_drawingActive)
        {
            DrawCanvas.Cursor = Cursors.Hand;
            CursorCircle.Visibility = Visibility.Collapsed;
        }
    }

    // ── События мыши на DrawCanvas
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
            if (_currentTool == DrawTool.Eraser)
            {
                _isDrawing = true;
                _redoStack.Clear();
                _lastEraserPoint = e.GetPosition(DrawCanvas);
                EraseAtPoint(_lastEraserPoint.Value);
                DrawCanvas.CaptureMouse();
                e.Handled = true;
            }
            else
            {
                _isDrawing = true;
                _redoStack.Clear();
                var pos = e.GetPosition(DrawCanvas);

                _currentStroke = new Polyline
                {
                    Stroke = new SolidColorBrush(_currentColor),
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
        }
        else
        {
            // Режим панорамирования
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

        // Обновить позицию круга курсора
        if (_drawingActive && CursorCircle != null)
        {
            var pos = e.GetPosition(DrawCanvas);
            double size = CursorCircle.Width;
            Canvas.SetLeft(CursorCircle, pos.X - size / 2);
            Canvas.SetTop(CursorCircle, pos.Y - size / 2);
        }

        if (_isDrawing)
        {
            if (_currentTool == DrawTool.Eraser && _lastEraserPoint.HasValue)
            {
                var currentPoint = e.GetPosition(DrawCanvas);
                EraseAlongLine(_lastEraserPoint.Value, currentPoint);
                _lastEraserPoint = currentPoint;
            }
            else if (_currentStroke != null)
            {
                _currentStroke.Points.Add(e.GetPosition(DrawCanvas));
            }
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
            if (_currentTool == DrawTool.Eraser)
            {
                _lastEraserPoint = null;
                MarkDirty();
            }
            else if (_currentStroke != null)
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

    // ── Логика ластика
    private const double FixedEraserSize = 50; 

    private void EraseAtPoint(Point point)
    {
        double eraserSize = FixedEraserSize;
        double eraserRadius = eraserSize / 2;
        var eraserRect = new Rect(point.X - eraserRadius, point.Y - eraserRadius, eraserSize, eraserSize);

        var strokesToProcess = new List<Polyline>();
        foreach (Polyline stroke in DrawCanvas.Children.OfType<Polyline>())
        {
            if (StrokeIntersectsEraser(stroke, eraserRect, eraserRadius))
            {
                strokesToProcess.Add(stroke);
            }
        }

        foreach (var stroke in strokesToProcess)
        {
            var newStrokes = SplitStrokeByEraser(stroke, eraserRect, eraserRadius);
            DrawCanvas.Children.Remove(stroke);
            foreach (var newStroke in newStrokes)
            {
                if (newStroke.Points.Count > 1)
                    DrawCanvas.Children.Add(newStroke);
            }
        }
    }

    private void EraseAlongLine(Point start, Point end)
    {
        double eraserSize = FixedEraserSize;
        double eraserRadius = eraserSize / 2;
        var distance = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
        if (distance < 1) return;

        int steps = Math.Min((int)Math.Ceiling(distance / (eraserRadius * 0.3)), 15); 
        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            Point point = new Point(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
            EraseAtPoint(point);
        }
    }

    private bool StrokeIntersectsEraser(Polyline stroke, Rect eraserRect, double eraserRadius)
    {
        foreach (Point pt in stroke.Points)
        {
            if (eraserRect.Contains(pt))
                return true;
        }

        // Проверить линейные сегменты
        for (int i = 0; i < stroke.Points.Count - 1; i++)
        {
            Point p1 = stroke.Points[i];
            Point p2 = stroke.Points[i + 1];
            if (LineIntersectsCircle(p1, p2, eraserRect, eraserRadius))
                return true;
        }

        return false;
    }

    private bool LineIntersectsCircle(Point p1, Point p2, Rect eraserRect, double radius)
    {
        Point circleCenter = new Point(eraserRect.X + radius, eraserRect.Y + radius);

        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double fx = p1.X - circleCenter.X;
        double fy = p1.Y - circleCenter.Y;

        double a = dx * dx + dy * dy;
        double b = 2 * (fx * dx + fy * dy);
        double c = fx * fx + fy * fy - radius * radius;

        double discriminant = b * b - 4 * a * c;

        if (discriminant < 0)
            return false;

        discriminant = Math.Sqrt(discriminant);
        double t1 = (-b - discriminant) / (2 * a);
        double t2 = (-b + discriminant) / (2 * a);

        if ((t1 >= 0 && t1 <= 1) || (t2 >= 0 && t2 <= 1))
            return true;

        return false;
    }

    private List<Polyline> SplitStrokeByEraser(Polyline stroke, Rect eraserRect, double eraserRadius)
    {
        var result = new List<Polyline>();
        var currentSegment = new PointCollection();

        foreach (Point pt in stroke.Points)
        {
            if (eraserRect.Contains(pt))
            {
                if (currentSegment.Count > 1)
                {
                    result.Add(CreateStrokeFromPoints(currentSegment, stroke));
                }
                currentSegment = new PointCollection();
            }
            else
            {
                currentSegment.Add(pt);
            }
        }

        if (currentSegment.Count > 1)
        {
            result.Add(CreateStrokeFromPoints(currentSegment, stroke));
        }

        return result;
    }

    private Polyline CreateStrokeFromPoints(PointCollection points, Polyline originalStroke)
    {
        return new Polyline
        {
            Points = points,
            Stroke = originalStroke.Stroke,
            StrokeThickness = originalStroke.StrokeThickness,
            StrokeLineJoin = originalStroke.StrokeLineJoin,
            StrokeStartLineCap = originalStroke.StrokeStartLineCap,
            StrokeEndLineCap = originalStroke.StrokeEndLineCap,
            Opacity = originalStroke.Opacity
        };
    }

    // ── Отмена / Повтор
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

    // ── Палитра цветов
    private void BuildColorPalette()
    {
        ColorPalettePanel.Children.Clear();
        foreach (var color in Palette)
        {
            var border = new Border
            {
                Width = 24,
                Height = 24,
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
                border.Width = 20;
                border.Height = 20;
                _currentColor = c;
                HighlightSelectedColor(border);
                SaveBrushColor(c);
                if (!_drawingActive) ActivateTool(DrawTool.Pencil);
            };
            border.MouseLeftButtonUp += (_, _) =>
            {
                border.Width = 24;
                border.Height = 24;
            };
            ColorPalettePanel.Children.Add(border);
        }

        LoadSavedBrushColor();
    }

    private void SaveBrushColor(Color color)
    {
        try
        {
            var colorString = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            MPMS.Properties.Settings.Default.PhotoViewerBrushColor = colorString;
            MPMS.Properties.Settings.Default.Save();
        }
        catch { }
    }

    private void LoadSavedBrushColor()
    {
        try
        {
            var savedColor = MPMS.Properties.Settings.Default.PhotoViewerBrushColor;
            if (!string.IsNullOrEmpty(savedColor))
            {
                var color = (Color)ColorConverter.ConvertFromString(savedColor);
                _currentColor = color;

                for (int i = 0; i < Palette.Length; i++)
                {
                    if (Palette[i].R == color.R && Palette[i].G == color.G && Palette[i].B == color.B)
                    {
                        SelectColorByIndex(i);
                        return;
                    }
                }
            }
        }
        catch
        {
            SelectColorByIndex(9);
        }
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

    // ── Режим обрезки
    private void ToggleCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_cropMode)
            CancelCrop_Click(sender, e);
        else
            EnterCropMode();
    }

    private void EnterCropMode()
    {
        _cropMode = true;
        _isDraggingCrop = false;
        _cropDragHandle = CropHandle.None;
        DeactivateTool();
        CropOverlayGrid.Visibility = Visibility.Visible;
        DrawingToolsPanel.Visibility = Visibility.Collapsed;
        CropToolsPanel.Visibility = Visibility.Visible;
        DrawCanvas.Cursor = Cursors.Cross;
        _currentAspectRatio = AspectRatio.Free;
        foreach (var buttonName in new[] { "Aspect9_16", "Aspect16_9", "Aspect4_5", "Aspect5_4", "Aspect3_4", "Aspect4_3", "Aspect1_1", "Aspect3_2" })
        {
            var btn = FindName(buttonName) as RadioButton;
            if (btn != null) btn.IsChecked = false;
        }
        InitCropRect();
        UpdateCropVisuals();
    }

    private void InitCropRect()
    {
        var bounds = GetCropBounds();
        _cropRect = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        ClampCropRect();
        UpdateCropVisuals();
    }

    private void UpdateCropVisuals()
    {
        if (CropOverlayGrid.Visibility != Visibility.Visible) return;
        var bounds = GetCropBounds();
        double w = bounds.Width;
        double h = bounds.Height;

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

        double x1 = _cropRect.Left + _cropRect.Width / 3.0;
        double x2 = _cropRect.Left + _cropRect.Width * 2.0 / 3.0;
        double y1 = _cropRect.Top + _cropRect.Height / 3.0;
        double y2 = _cropRect.Top + _cropRect.Height * 2.0 / 3.0;

        SetCropLine(CropGridV1, x1, _cropRect.Top, x1, _cropRect.Bottom);
        SetCropLine(CropGridV2, x2, _cropRect.Top, x2, _cropRect.Bottom);
        SetCropLine(CropGridH1, _cropRect.Left, y1, _cropRect.Right, y1);
        SetCropLine(CropGridH2, _cropRect.Left, y2, _cropRect.Right, y2);

        PlaceCropHandle(HandleTL, _cropRect.Left, _cropRect.Top);
        PlaceCropHandle(HandleT, _cropRect.Left + _cropRect.Width / 2.0, _cropRect.Top);
        PlaceCropHandle(HandleTR, _cropRect.Right, _cropRect.Top);
        PlaceCropHandle(HandleR, _cropRect.Right, _cropRect.Top + _cropRect.Height / 2.0);
        PlaceCropHandle(HandleBR, _cropRect.Right, _cropRect.Bottom);
        PlaceCropHandle(HandleB, _cropRect.Left + _cropRect.Width / 2.0, _cropRect.Bottom);
        PlaceCropHandle(HandleBL, _cropRect.Left, _cropRect.Bottom);
        PlaceCropHandle(HandleL, _cropRect.Left, _cropRect.Top + _cropRect.Height / 2.0);

        double inverseScale = 1.0 / _zoomFactor;
        HandleTLScale.ScaleX = inverseScale; HandleTLScale.ScaleY = inverseScale;
        HandleTScale.ScaleX = inverseScale; HandleTScale.ScaleY = inverseScale;
        HandleTRScale.ScaleX = inverseScale; HandleTRScale.ScaleY = inverseScale;
        HandleRScale.ScaleX = inverseScale; HandleRScale.ScaleY = inverseScale;
        HandleBRScale.ScaleX = inverseScale; HandleBRScale.ScaleY = inverseScale;
        HandleBScale.ScaleX = inverseScale; HandleBScale.ScaleY = inverseScale;
        HandleBLScale.ScaleX = inverseScale; HandleBLScale.ScaleY = inverseScale;
        HandleLScale.ScaleX = inverseScale; HandleLScale.ScaleY = inverseScale;

        CropPanelSizeText.Text = $"{(int)_cropRect.Width} × {(int)_cropRect.Height}";
    }

    private static void SetCropLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1; line.Y1 = y1; line.X2 = x2; line.Y2 = y2;
    }

    private static void PlaceCropHandle(FrameworkElement el, double x, double y)
    {
        const double half = 10.0;
        Canvas.SetLeft(el, x - half);
        Canvas.SetTop(el, y - half);
    }

    private void CropCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);
        _cropDragHandle = GetCropHandle(pos);
        if (_cropDragHandle == CropHandle.None)
        {
            var bounds = GetCropBounds();
            if (!bounds.Contains(pos)) return;
            _cropDragHandle = CropHandle.New;
            _cropRect = new Rect(pos.X, pos.Y, 1, 1);
            UpdateCropVisuals();
        }

        _cropDragStart = pos;
        _cropStartRect = _cropRect;
        _isDraggingCrop = true;
        DrawCanvas.CaptureMouse();
        DrawCanvas.Cursor = GetCursorForCropHandle(_cropDragHandle);

        // Сбросить соотношение сторон при ручной настройке обрезки
        if (_currentAspectRatio != AspectRatio.Free)
        {
            _currentAspectRatio = AspectRatio.Free;
            foreach (var buttonName in new[] { "Aspect9_16", "Aspect16_9", "Aspect4_5", "Aspect5_4", "Aspect3_4", "Aspect4_3", "Aspect1_1", "Aspect3_2" })
            {
                var btn = FindName(buttonName) as RadioButton;
                if (btn != null) btn.IsChecked = false;
            }
        }

        e.Handled = true;
    }

    private void CropCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(DrawCanvas);
        if (!_isDraggingCrop)
        {
            DrawCanvas.Cursor = GetCursorForCropHandle(GetCropHandle(pos));
            return;
        }

        _cropRect = BuildCropRect(_cropDragHandle, _cropStartRect, _cropDragStart, pos);
        ClampCropRect();
        UpdateCropVisuals();
        e.Handled = true;
    }

    private void CropCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _isDraggingCrop = false;
        _cropDragHandle = CropHandle.None;
        DrawCanvas.ReleaseMouseCapture();
        DrawCanvas.Cursor = GetCursorForCropHandle(GetCropHandle(e.GetPosition(DrawCanvas)));
        e.Handled = true;
    }

    private CropHandle GetCropHandle(Point p)
    {
        const double hit = 18.0;
        if (IsNear(p, _cropRect.TopLeft, hit)) return CropHandle.TL;
        if (IsNear(p, new Point(_cropRect.Left + _cropRect.Width / 2.0, _cropRect.Top), hit)) return CropHandle.T;
        if (IsNear(p, _cropRect.TopRight, hit)) return CropHandle.TR;
        if (IsNear(p, new Point(_cropRect.Right, _cropRect.Top + _cropRect.Height / 2.0), hit)) return CropHandle.R;
        if (IsNear(p, _cropRect.BottomRight, hit)) return CropHandle.BR;
        if (IsNear(p, new Point(_cropRect.Left + _cropRect.Width / 2.0, _cropRect.Bottom), hit)) return CropHandle.B;
        if (IsNear(p, _cropRect.BottomLeft, hit)) return CropHandle.BL;
        if (IsNear(p, new Point(_cropRect.Left, _cropRect.Top + _cropRect.Height / 2.0), hit)) return CropHandle.L;
        if (IsNearHorizontalEdge(p, _cropRect.Top, hit)) return CropHandle.T;
        if (IsNearVerticalEdge(p, _cropRect.Right, hit)) return CropHandle.R;
        if (IsNearHorizontalEdge(p, _cropRect.Bottom, hit)) return CropHandle.B;
        if (IsNearVerticalEdge(p, _cropRect.Left, hit)) return CropHandle.L;
        if (_cropRect.Contains(p)) return CropHandle.Move;
        return CropHandle.None;
    }

    private static bool IsNear(Point a, Point b, double d)
        => Math.Abs(a.X - b.X) < d && Math.Abs(a.Y - b.Y) < d;

    private bool IsNearHorizontalEdge(Point p, double y, double d)
        => p.X >= _cropRect.Left && p.X <= _cropRect.Right && Math.Abs(p.Y - y) < d;

    private bool IsNearVerticalEdge(Point p, double x, double d)
        => p.Y >= _cropRect.Top && p.Y <= _cropRect.Bottom && Math.Abs(p.X - x) < d;

    private Cursor GetCursorForCropHandle(CropHandle handle) => handle switch
    {
        CropHandle.TL or CropHandle.BR => Cursors.SizeNWSE,
        CropHandle.TR or CropHandle.BL => Cursors.SizeNESW,
        CropHandle.T or CropHandle.B => Cursors.SizeNS,
        CropHandle.L or CropHandle.R => Cursors.SizeWE,
        CropHandle.Move => Cursors.SizeAll,
        CropHandle.New => Cursors.Cross,
        _ => Cursors.Cross
    };

    private Rect BuildCropRect(CropHandle handle, Rect startRect, Point startPoint, Point currentPoint)
    {
        var bounds = GetCropBounds();
        currentPoint = ClampPoint(currentPoint, bounds);
        const double min = 48.0;

        if (handle == CropHandle.New)
        {
            double left = Math.Min(startPoint.X, currentPoint.X);
            double top = Math.Min(startPoint.Y, currentPoint.Y);
            double right = Math.Max(startPoint.X, currentPoint.X);
            double bottom = Math.Max(startPoint.Y, currentPoint.Y);

            if (_currentAspectRatio != AspectRatio.Free)
            {
                double targetRatio = GetTargetAspectRatio();
                double width = right - left;
                double height = bottom - top;

                if (width / height > targetRatio)
                {
                    double newHeight = width / targetRatio;
                    if (currentPoint.Y < startPoint.Y)
                        top = bottom - newHeight;
                    else
                        bottom = top + newHeight;
                }
                else
                {
                    double newWidth = height * targetRatio;
                    if (currentPoint.X < startPoint.X)
                        left = right - newWidth;
                    else
                        right = left + newWidth;
                }
            }

            if (right - left < min)
            {
                if (currentPoint.X < startPoint.X) left = Math.Max(bounds.Left, right - min);
                else right = Math.Min(bounds.Right, left + min);
            }
            if (bottom - top < min)
            {
                if (currentPoint.Y < startPoint.Y) top = Math.Max(bounds.Top, bottom - min);
                else bottom = Math.Min(bounds.Bottom, top + min);
            }
            return new Rect(left, top, Math.Max(min, right - left), Math.Max(min, bottom - top));
        }

        double dx = currentPoint.X - startPoint.X;
        double dy = currentPoint.Y - startPoint.Y;
        double l = startRect.Left;
        double t = startRect.Top;
        double r = startRect.Right;
        double b = startRect.Bottom;

        if (handle == CropHandle.Move)
        {
            double x = Math.Clamp(startRect.X + dx, bounds.Left, bounds.Right - startRect.Width);
            double y = Math.Clamp(startRect.Y + dy, bounds.Top, bounds.Bottom - startRect.Height);
            return new Rect(x, y, startRect.Width, startRect.Height);
        }

        if (handle is CropHandle.TL or CropHandle.L or CropHandle.BL) l += dx;
        if (handle is CropHandle.TR or CropHandle.R or CropHandle.BR) r += dx;
        if (handle is CropHandle.TL or CropHandle.T or CropHandle.TR) t += dy;
        if (handle is CropHandle.BL or CropHandle.B or CropHandle.BR) b += dy;

        if (handle is CropHandle.TL or CropHandle.L or CropHandle.BL) l = Math.Clamp(l, bounds.Left, r - min);
        if (handle is CropHandle.TR or CropHandle.R or CropHandle.BR) r = Math.Clamp(r, l + min, bounds.Right);
        if (handle is CropHandle.TL or CropHandle.T or CropHandle.TR) t = Math.Clamp(t, bounds.Top, b - min);
        if (handle is CropHandle.BL or CropHandle.B or CropHandle.BR) b = Math.Clamp(b, t + min, bounds.Bottom);

        if (_currentAspectRatio != AspectRatio.Free && handle != CropHandle.Move)
        {
            double targetRatio = GetTargetAspectRatio();
            double width = r - l;
            double height = b - t;
            double currentRatio = width / height;

            if (handle is CropHandle.TL or CropHandle.TR or CropHandle.T)
            {

                double newWidth = height * targetRatio;
                if (handle is CropHandle.TL)
                    l = r - newWidth;
                else
                    r = l + newWidth;
            }
            else if (handle is CropHandle.BL or CropHandle.BR or CropHandle.B)
            {
                double newWidth = height * targetRatio;
                if (handle is CropHandle.BL)
                    l = r - newWidth;
                else
                    r = l + newWidth;
            }
            else if (handle is CropHandle.TL or CropHandle.BL or CropHandle.L)
            {
                double newHeight = width / targetRatio;
                if (handle is CropHandle.TL)
                    t = b - newHeight;
                else
                    b = t + newHeight;
            }
            else if (handle is CropHandle.TR or CropHandle.BR or CropHandle.R)
            {
                double newHeight = width / targetRatio;
                if (handle is CropHandle.TR)
                    t = b - newHeight;
                else
                    b = t + newHeight;
            }

            // Повторно ограничить после корректировки соотношения сторон
            l = Math.Clamp(l, bounds.Left, r - min);
            r = Math.Clamp(r, l + min, bounds.Right);
            t = Math.Clamp(t, bounds.Top, b - min);
            b = Math.Clamp(b, t + min, bounds.Bottom);
        }

        return new Rect(l, t, r - l, b - t);
    }

    private static Point ClampPoint(Point point, Rect bounds)
        => new(Math.Clamp(point.X, bounds.Left, bounds.Right), Math.Clamp(point.Y, bounds.Top, bounds.Bottom));

    private double GetTargetAspectRatio()
    {
        if (_source == null) return 1.0;

        return _currentAspectRatio switch
        {
            AspectRatio.Free => 0, 
            AspectRatio.Original => (double)_source.PixelWidth / _source.PixelHeight,
            AspectRatio.Square => 1.0,
            AspectRatio.Ratio_9_16 => 9.0 / 16.0,
            AspectRatio.Ratio_16_9 => 16.0 / 9.0,
            AspectRatio.Ratio_4_5 => 4.0 / 5.0,
            AspectRatio.Ratio_5_4 => 5.0 / 4.0,
            AspectRatio.Ratio_3_4 => 3.0 / 4.0,
            AspectRatio.Ratio_4_3 => 4.0 / 3.0,
            AspectRatio.Ratio_1_1 => 1.0,
            AspectRatio.Ratio_3_2 => 3.0 / 2.0,
            _ => 0
        };
    }

    private void AspectRatioButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton btn && btn.Tag is string tag)
        {
            _currentAspectRatio = tag switch
            {
                "Free" => AspectRatio.Free,
                "Original" => AspectRatio.Original,
                "Square" => AspectRatio.Square,
                "9:16" => AspectRatio.Ratio_9_16,
                "16:9" => AspectRatio.Ratio_16_9,
                "4:5" => AspectRatio.Ratio_4_5,
                "5:4" => AspectRatio.Ratio_5_4,
                "3:4" => AspectRatio.Ratio_3_4,
                "4:3" => AspectRatio.Ratio_4_3,
                "1:1" => AspectRatio.Ratio_1_1,
                "3:2" => AspectRatio.Ratio_3_2,
                _ => AspectRatio.Free
            };

            if (_currentAspectRatio != AspectRatio.Free && _cropMode)
            {
                AdjustCropRectToAspectRatio();
                UpdateCropVisuals();
            }
        }
    }

    private void AdjustCropRectToAspectRatio()
    {
        var bounds = GetCropBounds();
        double targetRatio = GetTargetAspectRatio();
        if (targetRatio <= 0) return;

        double newWidth, newHeight;

        newWidth = bounds.Width;
        newHeight = newWidth / targetRatio;

        if (newHeight > bounds.Height)
        {
            newHeight = bounds.Height;
            newWidth = newHeight * targetRatio;
        }

        const double minSize = 48.0;
        if (newWidth < minSize || newHeight < minSize)
        {
            if (targetRatio >= 1)
            {
                newWidth = minSize;
                newHeight = minSize / targetRatio;
            }
            else
            {
                newHeight = minSize;
                newWidth = minSize * targetRatio;
            }
        }

        double centerX = bounds.X + bounds.Width / 2;
        double centerY = bounds.Y + bounds.Height / 2;
        double newX = Math.Clamp(centerX - newWidth / 2, bounds.Left, bounds.Right - newWidth);
        double newY = Math.Clamp(centerY - newHeight / 2, bounds.Top, bounds.Bottom - newHeight);

        _cropRect = new Rect(newX, newY, newWidth, newHeight);
    }

    private void ClampCropRect()
    {
        var bounds = GetCropBounds();
        const double min = 48.0;
        double w = Math.Clamp(_cropRect.Width, Math.Min(min, bounds.Width), bounds.Width);
        double h = Math.Clamp(_cropRect.Height, Math.Min(min, bounds.Height), bounds.Height);
        double x = Math.Clamp(_cropRect.X, bounds.Left, bounds.Right - w);
        double y = Math.Clamp(_cropRect.Y, bounds.Top, bounds.Bottom - h);
        _cropRect = new Rect(x, y, w, h);
    }

    private Rect GetCropBounds()
    {
        double w = DrawCanvas.ActualWidth > 1 ? DrawCanvas.ActualWidth : (_source?.PixelWidth ?? 1);
        double h = DrawCanvas.ActualHeight > 1 ? DrawCanvas.ActualHeight : (_source?.PixelHeight ?? 1);
        return new Rect(0, 0, Math.Max(1, w), Math.Max(1, h));
    }

    private void ApplyCrop_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;

        try
        {
            ClampCropRect();
            var cropArea = _cropRect;
            var cropPixels = GetCropPixelRect();
            if (cropPixels.Width < 1 || cropPixels.Height < 1) return;

            var cropped = new CroppedBitmap(_source, cropPixels);
            cropped.Freeze();

            CropDrawings(cropArea);
            ApplySourceBitmap(cropped);
            ExitCropMode();
            MarkDirty();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось обрезать изображение: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelCrop_Click(object sender, RoutedEventArgs e)
    {
        ExitCropMode();
    }

    private void ExitCropMode()
    {
        _cropMode = false;
        _isDraggingCrop = false;
        _cropDragHandle = CropHandle.None;
        CropOverlayGrid.Visibility = Visibility.Collapsed;
        DrawingToolsPanel.Visibility = Visibility.Visible;
        CropToolsPanel.Visibility = Visibility.Collapsed;
        DrawCanvas.ReleaseMouseCapture();
        DrawCanvas.Cursor = _drawingActive ? Cursors.None : Cursors.Hand;
    }

    private void ResetCrop_Click(object sender, RoutedEventArgs e)
    {
        InitCropRect();
        UpdateCropVisuals();
    }

    private void DrawCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_cropMode) UpdateCropVisuals();
    }

    private Int32Rect GetCropPixelRect()
    {
        if (_source is null) return new Int32Rect(0, 0, 0, 0);

        var bounds = GetCropBounds();
        double scaleX = _source.PixelWidth / bounds.Width;
        double scaleY = _source.PixelHeight / bounds.Height;

        int x = (int)Math.Floor(_cropRect.Left * scaleX);
        int y = (int)Math.Floor(_cropRect.Top * scaleY);
        int right = (int)Math.Ceiling(_cropRect.Right * scaleX);
        int bottom = (int)Math.Ceiling(_cropRect.Bottom * scaleY);

        x = Math.Clamp(x, 0, Math.Max(0, _source.PixelWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, _source.PixelHeight - 1));
        right = Math.Clamp(right, x + 1, _source.PixelWidth);
        bottom = Math.Clamp(bottom, y + 1, _source.PixelHeight);

        return new Int32Rect(x, y, right - x, bottom - y);
    }

    private void CropDrawings(Rect cropArea)
    {
        foreach (var stroke in DrawCanvas.Children.OfType<Polyline>().ToList())
        {
            if (stroke.Points.Count == 0)
            {
                DrawCanvas.Children.Remove(stroke);
                continue;
            }

            var bounds = GetPointBounds(stroke.Points);
            if (!bounds.IntersectsWith(cropArea))
            {
                DrawCanvas.Children.Remove(stroke);
                continue;
            }

            var shifted = new PointCollection();
            foreach (Point point in stroke.Points)
                shifted.Add(new Point(point.X - cropArea.X, point.Y - cropArea.Y));
            stroke.Points = shifted;
        }

        _undoStack.Clear();
        _redoStack.Clear();
    }

    private static Rect GetPointBounds(PointCollection points)
    {
        double minX = double.PositiveInfinity;
        double minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double maxY = double.NegativeInfinity;

        foreach (Point point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Rect(new Point(minX, minY), new Point(maxX, maxY));
    }

    private void ApplySourceBitmap(BitmapSource bitmap, bool clearDrawings = false, bool resetRotation = false)
    {
        if (clearDrawings)
            ClearDrawingStrokes();

        if (resetRotation)
            _rotationAngle = 0;

        _source = bitmap;
        MainImage.Source = bitmap;
        MainImage.Width = bitmap.PixelWidth;
        MainImage.Height = bitmap.PixelHeight;
        ImageContainer.Width = bitmap.PixelWidth;
        ImageContainer.Height = bitmap.PixelHeight;
        DrawCanvas.Width = bitmap.PixelWidth;
        DrawCanvas.Height = bitmap.PixelHeight;
        _viewportReady = false;
        FitImageToViewport();
        UpdateZoomDisplay();
        UpdateFileInfo();
    }

    private void ClearDrawingStrokes()
    {
        foreach (var stroke in DrawCanvas.Children.OfType<Polyline>().ToList())
            DrawCanvas.Children.Remove(stroke);
        _undoStack.Clear();
        _redoStack.Clear();
        _currentStroke = null;
    }

    private void SaveEditedImageToFile(string path)
    {
        bool flatten = RequiresOpaqueBackground(path);
        var rendered = RenderEditedBitmap(flatten);
        SaveBitmapToFile(path, rendered);
        ApplySourceBitmap(rendered, clearDrawings: true, resetRotation: true);
    }

    private BitmapSource RenderEditedBitmap(bool flatten)
    {
        if (_source is null) throw new InvalidOperationException("No source image");

        var baseBitmap = RenderImageAndDrawings(flatten);
        int angle = NormalizeAngle(_rotationAngle);
        return angle == 0 ? baseBitmap : RenderRotatedBitmap(baseBitmap, angle, flatten);
    }

    private BitmapSource RenderImageAndDrawings(bool flatten)
    {
        if (_source is null) throw new InvalidOperationException("No source image");

        int width = Math.Max(1, _source.PixelWidth);
        int height = Math.Max(1, _source.PixelHeight);
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            if (flatten)
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            dc.DrawImage(_source, new Rect(0, 0, width, height));

            foreach (var stroke in DrawCanvas.Children.OfType<Polyline>())
                DrawPolyline(dc, stroke);
        }

        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static void DrawPolyline(DrawingContext dc, Polyline stroke)
    {
        if (stroke.Stroke is null || stroke.Points.Count == 0) return;

        var brush = stroke.Stroke.CloneCurrentValue();
        var pen = new Pen(brush, stroke.StrokeThickness)
        {
            StartLineCap = stroke.StrokeStartLineCap,
            EndLineCap = stroke.StrokeEndLineCap,
            LineJoin = stroke.StrokeLineJoin
        };

        dc.PushOpacity(stroke.Opacity);
        if (stroke.Points.Count == 1)
        {
            double radius = Math.Max(1, stroke.StrokeThickness / 2.0);
            dc.DrawEllipse(brush, null, stroke.Points[0], radius, radius);
        }
        else
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(stroke.Points[0], false, false);
                ctx.PolyLineTo(stroke.Points.Skip(1).ToArray(), true, true);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
        dc.Pop();
    }

    private static BitmapSource RenderRotatedBitmap(BitmapSource bitmap, int angle, bool flatten)
    {
        int width = angle is 90 or 270 ? bitmap.PixelHeight : bitmap.PixelWidth;
        int height = angle is 90 or 270 ? bitmap.PixelWidth : bitmap.PixelHeight;
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            if (flatten)
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));

            dc.PushTransform(new TranslateTransform(width / 2.0, height / 2.0));
            dc.PushTransform(new RotateTransform(angle));
            dc.DrawImage(bitmap, new Rect(-bitmap.PixelWidth / 2.0, -bitmap.PixelHeight / 2.0,
                                          bitmap.PixelWidth, bitmap.PixelHeight));
            dc.Pop();
            dc.Pop();
        }

        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static int NormalizeAngle(double angle)
    {
        int normalized = ((int)Math.Round(angle) % 360 + 360) % 360;
        return normalized switch
        {
            >= 45 and < 135 => 90,
            >= 135 and < 225 => 180,
            >= 225 and < 315 => 270,
            _ => 0
        };
    }

    private static bool RequiresOpaqueBackground(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".bmp" or ".gif";
    }

    private static void SaveBitmapToFile(string path, BitmapSource bitmap)
    {
        var encoder = CreateBitmapEncoder(path);
        BitmapSource frameSource = RequiresOpaqueBackground(path)
            ? ConvertBitmapFormat(bitmap, PixelFormats.Bgr24)
            : bitmap;

        encoder.Frames.Add(BitmapFrame.Create(frameSource));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

    private static BitmapSource ConvertBitmapFormat(BitmapSource bitmap, PixelFormat format)
    {
        var converted = new FormatConvertedBitmap();
        converted.BeginInit();
        converted.Source = bitmap;
        converted.DestinationFormat = format;
        converted.EndInit();
        converted.Freeze();
        return converted;
    }

    private static BitmapEncoder CreateBitmapEncoder(string path)
    {
        string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 92 },
            ".png" => new PngBitmapEncoder(),
            ".bmp" => new BmpBitmapEncoder(),
            ".gif" => new GifBitmapEncoder(),
            ".tif" or ".tiff" => new TiffBitmapEncoder(),
            ".webp" => throw new NotSupportedException("Сохранение WEBP пока не поддерживается."),
            _ => new PngBitmapEncoder()
        };
    }
}
