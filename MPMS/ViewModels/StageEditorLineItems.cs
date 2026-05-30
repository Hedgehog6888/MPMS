using CommunityToolkit.Mvvm.ComponentModel;
using MPMS.Models;

namespace MPMS.ViewModels;

/// <summary>Выбранный вид работ в этапе: количество и цена за единицу (наценка/скидка).</summary>
public sealed partial class StageWorkTypeLineVm : ObservableObject
{
    public Guid TemplateId { get; }
    public string Name { get; }
    public string? Unit { get; }

    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _basePricePerUnit;
    [ObservableProperty] private decimal _pricePerUnit;
    [ObservableProperty] private decimal _lineAdjustmentPercent;

    public decimal LineTotal => Quantity * PricePerUnit;
    public bool HasPriceOverride => Math.Abs(PricePerUnit - BasePricePerUnit) > 0.005m;

    public StageWorkTypeLineVm(Guid templateId, string name, string? unit, decimal basePrice)
    {
        TemplateId = templateId;
        Name = name;
        Unit = unit;
        _basePricePerUnit = basePrice;
        _pricePerUnit = basePrice;
    }

    partial void OnQuantityChanged(decimal value)
    {
        if (value < 1m)
        {
            Quantity = 1m;
            return;
        }
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnPricePerUnitChanged(decimal value)
    {
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(HasPriceOverride));
    }

    partial void OnBasePricePerUnitChanged(decimal value) => OnPropertyChanged(nameof(HasPriceOverride));

    partial void OnLineAdjustmentPercentChanged(decimal value) => OnPropertyChanged(nameof(HasPriceOverride));

    public void ApplyStagePricing(decimal adjustmentPercent, decimal pricePerUnit, decimal quantity)
    {
        ApplyStagePricing(adjustmentPercent, pricePerUnit);
        Quantity = Math.Max(1m, Math.Round(quantity, 2, MidpointRounding.AwayFromZero));
    }

    public void ApplyStagePricing(decimal adjustmentPercent, decimal pricePerUnit)
    {
        LineAdjustmentPercent = ClampPercent(adjustmentPercent);
        PricePerUnit = Math.Max(0m, Math.Round(pricePerUnit, 2, MidpointRounding.AwayFromZero));
    }

    public void ResetStagePricing()
    {
        LineAdjustmentPercent = 0m;
        PricePerUnit = BasePricePerUnit;
    }

    public static decimal PriceFromPercent(decimal basePrice, decimal adjustmentPercent) =>
        Math.Max(0m, Math.Round(basePrice * (1m + ClampPercent(adjustmentPercent) / 100m), 2, MidpointRounding.AwayFromZero));

    public static decimal PercentFromPrice(decimal basePrice, decimal pricePerUnit) =>
        basePrice <= 0m ? 0m : Math.Round((pricePerUnit / basePrice - 1m) * 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal ClampPercent(decimal value) => Math.Clamp(value, -999m, 999m);
}

/// <summary>Материал на этапе с количеством и ценой за единицу.</summary>
public sealed partial class StageMaterialLineVm : ObservableObject
{
    public Guid RowId { get; } = Guid.NewGuid();

    [ObservableProperty] private Guid _materialId;
    [ObservableProperty] private string _materialName = "";
    [ObservableProperty] private string? _unit;
    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _basePricePerUnit;
    [ObservableProperty] private decimal _pricePerUnit;
    [ObservableProperty] private decimal _lineAdjustmentPercent;
    [ObservableProperty] private decimal _stockAvailable;

    public decimal LineTotal => Quantity * PricePerUnit;
    public bool HasPriceOverride => Math.Abs(PricePerUnit - BasePricePerUnit) > 0.005m;

    public StageMaterialLineVm() { }

    public StageMaterialLineVm(Guid materialId, string materialName, string? unit, decimal pricePerUnit)
    {
        _materialId = materialId;
        _materialName = materialName;
        _unit = unit;
        _basePricePerUnit = pricePerUnit;
        _pricePerUnit = pricePerUnit;
    }

    public void ApplyFrom(LocalMaterial m)
    {
        MaterialId = m.Id;
        MaterialName = m.Name;
        Unit = m.Unit;
        var cost = m.Cost ?? 0m;
        BasePricePerUnit = cost;
        PricePerUnit = cost;
        LineAdjustmentPercent = 0m;
        StockAvailable = Math.Max(0m, m.Quantity);
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(HasPriceOverride));
    }

    partial void OnQuantityChanged(decimal value)
    {
        if (value < 1m)
        {
            Quantity = 1m;
            return;
        }
        if (StockAvailable > 0m && value > StockAvailable)
        {
            Quantity = StockAvailable;
            return;
        }
        OnPropertyChanged(nameof(LineTotal));
    }

    partial void OnStockAvailableChanged(decimal value)
    {
        if (value > 0m && Quantity > value)
            Quantity = value;
    }

    partial void OnPricePerUnitChanged(decimal value)
    {
        OnPropertyChanged(nameof(LineTotal));
        OnPropertyChanged(nameof(HasPriceOverride));
    }

    partial void OnBasePricePerUnitChanged(decimal value) => OnPropertyChanged(nameof(HasPriceOverride));

    partial void OnLineAdjustmentPercentChanged(decimal value) => OnPropertyChanged(nameof(HasPriceOverride));

    public void ApplyStagePricing(decimal adjustmentPercent, decimal pricePerUnit, decimal quantity)
    {
        ApplyStagePricing(adjustmentPercent, pricePerUnit);
        var qty = Math.Max(1m, Math.Round(quantity, 2, MidpointRounding.AwayFromZero));
        if (StockAvailable > 0m && qty > StockAvailable)
            qty = StockAvailable;
        Quantity = qty;
    }

    public void ApplyStagePricing(decimal adjustmentPercent, decimal pricePerUnit)
    {
        LineAdjustmentPercent = Math.Clamp(adjustmentPercent, -999m, 999m);
        PricePerUnit = Math.Max(0m, Math.Round(pricePerUnit, 2, MidpointRounding.AwayFromZero));
    }

    public void ResetStagePricing()
    {
        LineAdjustmentPercent = 0m;
        PricePerUnit = BasePricePerUnit;
    }
}

/// <summary>Оборудование в этапе (пока без стоимости в итогах этапа).</summary>
public sealed partial class StageEquipmentLineVm : ObservableObject
{
    [ObservableProperty] private Guid _equipmentId;
    [ObservableProperty] private string _equipmentName = "";
    [ObservableProperty] private string? _inventoryNumber;
    [ObservableProperty] private decimal _quantity = 1m;

    public void ApplyFrom(LocalEquipment e)
    {
        EquipmentId = e.Id;
        EquipmentName = e.Name;
        InventoryNumber = e.InventoryNumber;
    }
}
