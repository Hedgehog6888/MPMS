using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MPMS.Views.Overlays;

public partial class AvatarCropOverlay : UserControl
{
    private readonly Func<BitmapSource, Task>? _onApplied;
    private BitmapSource? _source;

    private double _imgLeft, _imgTop, _imgW, _imgH;
    private double _cx, _cy, _r;

    private enum DragMode { None, Move, ResizeN, ResizeS, ResizeW, ResizeE }
    private DragMode _drag = DragMode.None;
    private Point _dragLast;

    private const double HandleHit = 14.0;

    public AvatarCropOverlay(string imagePath, Func<BitmapSource, Task>? onApplied = null)
    {
        InitializeComponent();
        _onApplied = onApplied;
        LoadImage(imagePath);
    }

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
            MainWindow.Instance?.HideDrawer();
        }
    }

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

        if (_r < 1)
        {
            _r = Math.Min(_imgW, _imgH) * 0.42;
            _cx = _imgLeft + _imgW / 2;
            _cy = _imgTop + _imgH / 2;
        }
    }

    private void UpdateCropVisuals()
    {
        double canvasW = CropCanvas.ActualWidth;
        double canvasH = CropCanvas.ActualHeight;
        if (canvasW < 1 || canvasH < 1) return;

        var fullRect = new RectangleGeometry(new Rect(0, 0, canvasW, canvasH));
        var cropEllipse = new EllipseGeometry(new Point(_cx, _cy), _r, _r);
        DimOverlay.Data = new CombinedGeometry(GeometryCombineMode.Exclude, fullRect, cropEllipse);

        Canvas.SetLeft(CropRing, _cx - _r);
        Canvas.SetTop(CropRing, _cy - _r);
        CropRing.Width = 2 * _r;
        CropRing.Height = 2 * _r;

        double x1 = _cx - _r + 2 * _r / 3;
        double x2 = _cx - _r + 4 * _r / 3;
        double y1 = _cy - _r + 2 * _r / 3;
        double y2 = _cy - _r + 4 * _r / 3;

        SetLine(GridH1, _cx - _r, y1, _cx + _r, y1);
        SetLine(GridH2, _cx - _r, y2, _cx + _r, y2);
        SetLine(GridV1, x1, _cy - _r, x1, _cy + _r);
        SetLine(GridV2, x2, _cy - _r, x2, _cy + _r);

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

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(CropCanvas);

        if (HitHandle(pos, _cx, _cy - _r))      { _drag = DragMode.ResizeN; }
        else if (HitHandle(pos, _cx, _cy + _r)) { _drag = DragMode.ResizeS; }
        else if (HitHandle(pos, _cx - _r, _cy)) { _drag = DragMode.ResizeW; }
        else if (HitHandle(pos, _cx + _r, _cy)) { _drag = DragMode.ResizeE; }
        else if (Distance(pos, _cx, _cy) < _r)  { _drag = DragMode.Move; }

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

    private static double Distance(Point p, double cx, double cy)
        => Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

    private static bool HitHandle(Point p, double hx, double hy)
        => Distance(p, hx, hy) <= HandleHit;

    private void ClampCropCircle()
    {
        double maxR = Math.Min(_imgW, _imgH) / 2.0;
        _r = Math.Min(_r, maxR);

        _cx = Math.Max(_imgLeft + _r, Math.Min(_imgLeft + _imgW - _r, _cx));
        _cy = Math.Max(_imgTop + _r, Math.Min(_imgTop + _imgH - _r, _cy));
    }

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
        catch
        {
        }
    }

    private BitmapSource RenderCrop(int outputSize)
    {
        if (_source is null) throw new InvalidOperationException("No source image");

        double scaleX = _source.PixelWidth / _imgW;
        double scaleY = _source.PixelHeight / _imgH;

        double cropCxImg = (_cx - _imgLeft) * scaleX;
        double cropCyImg = (_cy - _imgTop) * scaleY;
        double cropRImg = _r * Math.Min(scaleX, scaleY);

        int srcSize = Math.Max(1, (int)Math.Round(cropRImg * 2));
        int srcX = (int)Math.Round(cropCxImg - cropRImg);
        int srcY = (int)Math.Round(cropCyImg - cropRImg);

        srcX = Math.Max(0, Math.Min(_source.PixelWidth - srcSize, srcX));
        srcY = Math.Max(0, Math.Min(_source.PixelHeight - srcSize, srcY));
        srcSize = Math.Min(srcSize, Math.Min(_source.PixelWidth - srcX, _source.PixelHeight - srcY));

        if (srcSize < 1) throw new InvalidOperationException("Crop region is empty");

        var cropped = new CroppedBitmap(_source, new Int32Rect(srcX, srcY, srcSize, srcSize));

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

    private void BrowseAgain_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выберите фото профиля",
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|Все файлы|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            _r = 0;
            LoadImage(dlg.FileName);
            LayoutImage();
            UpdateCropVisuals();
            UpdatePreview();
        }
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_source is null) return;
        try
        {
            var croppedImage = RenderCrop(512);
            if (_onApplied is not null)
                await _onApplied(croppedImage);
            MainWindow.Instance?.HideDrawer();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Не удалось обрезать изображение: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();
}
