using CommunityToolkit.Mvvm.ComponentModel;
using MPMS.Models;

namespace MPMS.ViewModels;

/// <summary>Выбранная услуга в этапе: количество и цена за единицу (наценка/скидка).</summary>
public sealed partial class StageServiceLineVm : ObservableObject
{
    public Guid TemplateId { get; }
    public string Name { get; }
    public string? Unit { get; }

    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _pricePerUnit;

    public decimal LineTotal => Quantity * PricePerUnit;

    public StageServiceLineVm(Guid templateId, string name, string? unit, decimal basePrice)
    {
        TemplateId = templateId;
        Name = name;
        Unit = unit;
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
    partial void OnPricePerUnitChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
}

/// <summary>Материал на этапе с количеством и ценой за единицу.</summary>
public sealed partial class StageMaterialLineVm : ObservableObject
{
    public Guid RowId { get; } = Guid.NewGuid();

    [ObservableProperty] private Guid _materialId;
    [ObservableProperty] private string _materialName = "";
    [ObservableProperty] private string? _unit;
    [ObservableProperty] private decimal _quantity = 1m;
    [ObservableProperty] private decimal _pricePerUnit;
    [ObservableProperty] private decimal _stockAvailable;

    public decimal LineTotal => Quantity * PricePerUnit;

    public StageMaterialLineVm() { }

    public StageMaterialLineVm(Guid materialId, string materialName, string? unit, decimal pricePerUnit)
    {
        _materialId = materialId;
        _materialName = materialName;
        _unit = unit;
        _pricePerUnit = pricePerUnit;
    }

    public void ApplyFrom(LocalMaterial m)
    {
        MaterialId = m.Id;
        MaterialName = m.Name;
        Unit = m.Unit;
        PricePerUnit = m.Cost ?? 0m;
        StockAvailable = Math.Max(0m, m.Quantity);
        OnPropertyChanged(nameof(LineTotal));
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
    partial void OnPricePerUnitChanged(decimal value) => OnPropertyChanged(nameof(LineTotal));
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
