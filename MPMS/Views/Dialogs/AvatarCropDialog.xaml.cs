using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MPMS.Views.Dialogs;

/// <summary>
/// Avatar upload dialog with interactive circular crop.
/// Use <see cref="CroppedImage"/> after ShowDialog() returns true.
/// </summary>
public partial class AvatarCropDialog : Window
{
    // ── State ──────────────────────────────────────────────────────────────
    private BitmapSource? _source;

    // Image position/size within the canvas (letterboxed)
    private double _imgLeft, _imgTop, _imgW, _imgH;

    // Crop circle in canvas coordinates
    private double _cx, _cy, _r;

    // Drag state
    private enum DragMode { None, Move, ResizeN, ResizeS, ResizeW, ResizeE }
    private DragMode _drag = DragMode.None;
    private Point _dragLast;

    private const double HandleHit = 14.0; // px radius for handle hit-test

    // ── Result ─────────────────────────────────────────────────────────────
    public BitmapSource? CroppedImage { get; private set; }

    // ── Constructor ────────────────────────────────────────────────────────
    public AvatarCropDialog(string imagePath)
    {
        InitializeComponent();
        LoadImage(imagePath);
    }

    // ── Image Loading ──────────────────────────────────────────────────────
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
            SourceImage.Source = _source;
        }
        catch
        {
            MessageBox.Show("Не удалось загрузить изображение.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            Close();
        }
    }

    // ── Canvas Layout ──────────────────────────────────────────────────────
    private void CropCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_source is null || CropCanvas.ActualWidth < 1 || CropCanvas.ActualHeight < 1) return;
        LayoutImage();
        UpdateCropVisuals();
        UpdatePreview();
    }

    private void LayoutImage()
    {
        if (_source is null) return;
        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;
        double imgAspect = (double)_source.PixelWidth / _source.PixelHeight;
        double canvasAspect = canvasW / canvasH;

        if (imgAspect > canvasAspect)
        {
            _imgW = canvasW;
            _imgH = canvasW / imgAspect;
        }
        else
        {
            _imgH = canvasH;
            _imgW = canvasH * imgAspect;
        }

        _imgLeft = (canvasW - _imgW) / 2;
        _imgTop = (canvasH - _imgH) / 2;

        Canvas.SetLeft(SourceImage, _imgLeft);
        Canvas.SetTop(SourceImage, _imgTop);
        SourceImage.Width = _imgW;
        SourceImage.Height = _imgH;

        // Initialize crop circle only once (first layout)
        if (_r < 1)
        {
            _r = Math.Min(_imgW, _imgH) * 0.42;
            _cx = _imgLeft + _imgW / 2;
            _cy = _imgTop + _imgH / 2;
        }
    }

    // ── Crop Visual Update ─────────────────────────────────────────────────
    private void UpdateCropVisuals()
    {
        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;
        if (canvasW < 1 || canvasH < 1) return;

        // Dim overlay: full rect minus the crop circle hole
        var fullRect = new RectangleGeometry(new Rect(0, 0, canvasW, canvasH));
        var cropEllipse = new EllipseGeometry(new Point(_cx, _cy), _r, _r);
        DimOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cropEllipse);

        // Dashed ring
        Canvas.SetLeft(CropRing, _cx - _r);
        Canvas.SetTop(CropRing, _cy - _r);
        CropRing.Width = 2 * _r;
        CropRing.Height = 2 * _r;

        // Rule-of-thirds grid lines
        double x1 = _cx - _r + 2 * _r / 3;
        double x2 = _cx - _r + 4 * _r / 3;
        double y1 = _cy - _r + 2 * _r / 3;
        double y2 = _cy - _r + 4 * _r / 3;

        SetLine(GridH1, _cx - _r, y1, _cx + _r, y1);
        SetLine(GridH2, _cx - _r, y2, _cx + _r, y2);
        SetLine(GridV1, x1, _cy - _r, x1, _cy + _r);
        SetLine(GridV2, x2, _cy - _r, x2, _cy + _r);

        // Handles (centered on the circle edge midpoints)
        const double h = 7;
        PlaceHandle(HandleN, _cx, _cy - _r, h);
        PlaceHandle(HandleS, _cx, _cy + _r, h);
        PlaceHandle(HandleW, _cx - _r, _cy, h);
        PlaceHandle(HandleE, _cx + _r, _cy, h);
    }

    private static void SetLine(Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1; line.Y1 = y1; line.X2 = x2; line.Y2 = y2;
    }

    private static void PlaceHandle(Ellipse handle, double cx, double cy, double halfSize)
    {
        Canvas.SetLeft(handle, cx - halfSize);
        Canvas.SetTop(handle, cy - halfSize);
    }

    // ── Mouse Events ───────────────────────────────────────────────────────
    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(CropCanvas);

        // Check handles first (priority over body drag)
        if (HitHandle(pos, _cx, _cy - _r))      { _drag = DragMode.ResizeN; }
        else if (HitHandle(pos, _cx, _cy + _r)) { _drag = DragMode.ResizeS; }
        else if (HitHandle(pos, _cx - _r, _cy)) { _drag = DragMode.ResizeW; }
        else if (HitHandle(pos, _cx + _r, _cy)) { _drag = DragMode.ResizeE; }
        else
        {
            double dist = Distance(pos, _cx, _cy);
            if (dist < _r) _drag = DragMode.Move;
        }

        if (_drag != DragMode.None)
        {
            _dragLast = pos;
            CropCanvas.CaptureMouse();
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var pos = e.GetPosition(CropCanvas);

        // Update cursor based on hover (when not dragging)
        if (_drag == DragMode.None)
        {
            if (HitHandle(pos, _cx, _cy - _r) || HitHandle(pos, _cx, _cy + _r))
                CropCanvas.Cursor = Cursors.SizeNS;
            else if (HitHandle(pos, _cx - _r, _cy) || HitHandle(pos, _cx + _r, _cy))
                CropCanvas.Cursor = Cursors.SizeWE;
            else if (Distance(pos, _cx, _cy) < _r)
                CropCanvas.Cursor = Cursors.SizeAll;
            else
                CropCanvas.Cursor = Cursors.Arrow;
            return;
        }

        double dx = pos.X - _dragLast.X;
        double dy = pos.Y - _dragLast.Y;

        switch (_drag)
        {
            case DragMode.Move:
                _cx += dx;
                _cy += dy;
                break;

            case DragMode.ResizeN:
                // Moving N handle up = bigger circle; use perpendicular delta (Y only)
                _cy += dy / 2;
                _r = Math.Max(30, _r - dy / 2);
                break;

            case DragMode.ResizeS:
                _cy += dy / 2;
                _r = Math.Max(30, _r + dy / 2);
                break;

            case DragMode.ResizeW:
                _cx += dx / 2;
                _r = Math.Max(30, _r - dx / 2);
                break;

            case DragMode.ResizeE:
                _cx += dx / 2;
                _r = Math.Max(30, _r + dx / 2);
                break;
        }

        ClampCropCircle();
        _dragLast = pos;

        UpdateCropVisuals();
        UpdatePreview();
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _drag = DragMode.None;
        CropCanvas.ReleaseMouseCapture();
    }

    private void Canvas_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_drag == DragMode.None)
            CropCanvas.Cursor = Cursors.Arrow;
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private static double Distance(Point p, double cx, double cy)
        => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

    private static bool HitHandle(Point p, double hx, double hy)
        => Distance(p, hx, hy) <= HandleHit;

    private void ClampCropCircle()
    {
        // Clamp radius to fit within the image area
        double maxR = Math.Min(_imgW, _imgH) / 2.0;
        _r = Math.Min(_r, maxR);

        // Clamp center so circle stays inside the image
        _cx = Math.Max(_imgLeft + _r, Math.Min(_imgLeft + _imgW - _r, _cx));
        _cy = Math.Max(_imgTop + _r, Math.Min(_imgTop + _imgH - _r, _cy));
    }

    // ── Live Preview ───────────────────────────────────────────────────────
    private void UpdatePreview()
    {
        if (_source is null || _imgW < 1 || _r < 1) return;

        try
        {
            var preview = RenderCrop(128);
            PreviewLarge.Source = preview;
            PreviewMedium.Source = preview;
            PreviewSmall.Source = preview;
        }
        catch { /* silently ignore preview errors during drag */ }
    }

    // ── Crop Rendering ─────────────────────────────────────────────────────
    /// <summary>Renders the crop selection as a square <paramref name="outputSize"/> px PNG with circle clip.</summary>
    private BitmapSource RenderCrop(int outputSize)
    {
        if (_source is null) throw new InvalidOperationException("No source image");

        // Scale factors from canvas-px to source-image-px
        double scaleX = _source.PixelWidth  / _imgW;
        double scaleY = _source.PixelHeight / _imgH;

        // Crop rect in source image pixels
        double cropCxImg = (_cx - _imgLeft) * scaleX;
        double cropCyImg = (_cy - _imgTop)  * scaleY;
        double cropRImg  = _r * Math.Min(scaleX, scaleY); // uniform because circle stays in image

        int srcX = Math.Max(0, (int)(cropCxImg - cropRImg));
        int srcY = Math.Max(0, (int)(cropCyImg - cropRImg));
        int srcW = Math.Min(_source.PixelWidth  - srcX, (int)(cropRImg * 2));
        int srcH = Math.Min(_source.PixelHeight - srcY, (int)(cropRImg * 2));

        if (srcW < 1 || srcH < 1) throw new InvalidOperationException("Crop region is empty");

        var cropped = new CroppedBitmap(_source, new Int32Rect(srcX, srcY, srcW, srcH));

        var rtb = new RenderTargetBitmap(outputSize, outputSize, 96, 96, PixelFormats.Pbgra32);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double half = outputSize / 2.0;
            dc.PushClip(new EllipseGeometry(new Point(half, half), half, half));
            dc.DrawImage(cropped, new Rect(0, 0, outputSize, outputSize));
            dc.Pop();
        }
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ── Footer Buttons ─────────────────────────────────────────────────────
    private void BrowseAgain_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите фото профиля",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы|*.*"
        };
        if (dlg.ShowDialog(this) == true)
        {
            _r = 0; // reset crop to auto-init on next layout
            LoadImage(dlg.FileName);
            LayoutImage();
            UpdateCropVisuals();
            UpdatePreview();
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) { DialogResult = false; return; }
        try
        {
            CroppedImage = RenderCrop(512); // high-res for storage
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось обрезать изображение: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
