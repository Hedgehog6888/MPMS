using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public sealed class StageLinePricingOptions
{
    public bool IsMaterial { get; init; }
    public string? Unit { get; init; }
    public decimal StockAvailable { get; init; }
}

public partial class StageLinePricingOverlay : UserControl
{
    private readonly decimal _basePrice;
    private readonly bool _isMaterial;
    /// <summary>Максимум для строки этапа (остаток склада + количество при открытии).</summary>
    private readonly decimal _maxLineQuantity;
    private readonly decimal _openingQuantity;
    private readonly string? _unit;
    private readonly Func<decimal, decimal, decimal, Task<bool>> _onSave;
    private bool _syncing;
    private bool _isInitialized;

    public StageLinePricingOverlay(
        string itemName,
        decimal basePrice,
        decimal quantity,
        decimal currentPrice,
        decimal currentAdjustmentPercent,
        StageLinePricingOptions? options,
        Func<decimal, decimal, decimal, Task<bool>> onSave)
    {
        InitializeComponent();
        _basePrice = basePrice;
        _onSave = onSave;
        _isMaterial = options?.IsMaterial ?? false;
        _maxLineQuantity = options?.StockAvailable ?? 0m;
        _openingQuantity = quantity;
        _unit = options?.Unit;

        SubtitleText.Text = itemName;
        BasePriceText.Text = $"{basePrice:N2} ₽";

        if (_isMaterial)
            StockInfoText.Visibility = Visibility.Visible;

        _syncing = true;
        QuantityBox.Text = quantity.ToString("0.##", CultureInfo.CurrentCulture);
        PercentBox.Text = currentAdjustmentPercent.ToString("0.##", CultureInfo.CurrentCulture);
        PriceBox.Text = currentPrice.ToString("0.##", CultureInfo.CurrentCulture);
        _syncing = false;
        _isInitialized = true;
        UpdatePreview();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        PercentBox.Text = "0";
        PriceBox.Text = _basePrice.ToString("0.##", CultureInfo.CurrentCulture);
        _syncing = false;
        UpdatePreview();
    }

    private void DecQty_Click(object sender, RoutedEventArgs e) => AdjustQuantity(-1m);

    private void IncQty_Click(object sender, RoutedEventArgs e) => AdjustQuantity(1m);

    private void AdjustQuantity(decimal delta)
    {
        if (!TryParseDecimal(QuantityBox.Text, out var current))
            current = 1m;
        SetQuantity(Math.Round(current + delta, 2, MidpointRounding.AwayFromZero));
    }

    private void QuantityBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _syncing) return;
        UpdatePreview();
    }

    private void Discount5_Click(object sender, RoutedEventArgs e) => ApplyPercentDelta(-5m);
    private void Discount10_Click(object sender, RoutedEventArgs e) => ApplyPercentDelta(-10m);
    private void Markup5_Click(object sender, RoutedEventArgs e) => ApplyPercentDelta(5m);
    private void Markup10_Click(object sender, RoutedEventArgs e) => ApplyPercentDelta(10m);

    private void ApplyPercentDelta(decimal delta)
    {
        if (!TryParseDecimal(PercentBox.Text, out var current))
            current = 0m;
        _syncing = true;
        var next = Math.Clamp(current + delta, -999m, 999m);
        PercentBox.Text = next.ToString("0.##", CultureInfo.CurrentCulture);
        var price = StageWorkTypeLineVm.PriceFromPercent(_basePrice, next);
        PriceBox.Text = price.ToString("0.##", CultureInfo.CurrentCulture);
        _syncing = false;
        UpdatePreview();
    }

    private void PercentBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _syncing || PriceBox is null) return;
        if (!TryParseDecimal(PercentBox.Text, out var percent)) return;
        percent = Math.Clamp(percent, -999m, 999m);
        _syncing = true;
        var price = StageWorkTypeLineVm.PriceFromPercent(_basePrice, percent);
        PriceBox.Text = price.ToString("0.##", CultureInfo.CurrentCulture);
        _syncing = false;
        UpdatePreview();
    }

    private void PriceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized || _syncing || PercentBox is null) return;
        if (!TryParseDecimal(PriceBox.Text, out var price)) return;
        price = Math.Max(0m, price);
        _syncing = true;
        PercentBox.Text = StageWorkTypeLineVm.PercentFromPrice(_basePrice, price)
            .ToString("0.##", CultureInfo.CurrentCulture);
        _syncing = false;
        UpdatePreview();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (!TryParseDecimal(QuantityBox.Text, out var quantity) || quantity < 1m)
        {
            ShowError("Количество не может быть меньше 1");
            return;
        }

        quantity = Math.Round(quantity, 2, MidpointRounding.AwayFromZero);

        if (_isMaterial && _maxLineQuantity > 0m && quantity > _maxLineQuantity)
        {
            var unitSuffix = string.IsNullOrWhiteSpace(_unit) ? string.Empty : $" {_unit}";
            var available = Math.Max(0m, _maxLineQuantity - _openingQuantity);
            ShowError($"Недостаточно на складе. Доступно: {available:N2}{unitSuffix}");
            return;
        }

        if (!TryParseDecimal(PercentBox.Text, out var percent))
        {
            ShowError("Введите корректный процент скидки или наценки");
            return;
        }

        if (!TryParseDecimal(PriceBox.Text, out var price) || price < 0m)
        {
            ShowError("Введите корректную цену");
            return;
        }

        percent = Math.Clamp(percent, -999m, 999m);
        price = Math.Round(price, 2, MidpointRounding.AwayFromZero);

        var saved = await _onSave(percent, price, quantity);
        if (!saved)
        {
            ShowError("Не удалось сохранить изменения. Проверьте данные и попробуйте снова.");
            return;
        }

        MainWindow.Instance?.HideDrawer();
    }

    private void SetQuantity(decimal quantity)
    {
        quantity = Math.Max(1m, Math.Round(quantity, 2, MidpointRounding.AwayFromZero));
        if (_isMaterial && _maxLineQuantity > 0m && quantity > _maxLineQuantity)
            quantity = _maxLineQuantity;

        _syncing = true;
        QuantityBox.Text = quantity.ToString("0.##", CultureInfo.CurrentCulture);
        _syncing = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (!TryParseDecimal(QuantityBox?.Text, out var quantity))
            quantity = 1m;

        if (PreviewTotalText is not null)
        {
            if (!TryParseDecimal(PriceBox?.Text, out var price))
                price = _basePrice;
            PreviewTotalText.Text = $"{price * quantity:N2} ₽";
        }

        UpdateStockInfo(quantity);
    }

    private void UpdateStockInfo(decimal lineQuantity)
    {
        if (!_isMaterial || StockInfoText is null) return;

        if (_maxLineQuantity <= 0m)
        {
            StockInfoText.Text = "Остаток на складе не ограничен";
            return;
        }

        var warehouseRemaining = Math.Max(0m, _maxLineQuantity - lineQuantity);
        var unitSuffix = string.IsNullOrWhiteSpace(_unit) ? string.Empty : $" {_unit}";
        StockInfoText.Text = $"Остаток на складе: {warehouseRemaining:N2}{unitSuffix}";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private static bool TryParseDecimal(string? text, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(text)) return false;
        return decimal.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out value);
    }
}
