using System.Collections.ObjectModel;
using System.IO;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using MPMS.Data;
using MPMS.Models;
using MPMS.Infrastructure;
using MPMS.ViewModels;

namespace MPMS.Services;

public class WarehouseReportService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public event Action<LocalFile>? ReportGenerated;

    public WarehouseReportService(
        IDbContextFactory<LocalDbContext> dbFactory,
        ISyncService sync,
        IAuthService auth,
        SidebarFooterViewModel sidebarFooter)
    {
        _dbFactory = dbFactory;
        _sync = sync;
        _auth = auth;
        _sidebarFooter = sidebarFooter;
    }

    public async Task<string> GenerateWarehouseReportAsync(
        bool includeMaterials,
        bool includeEquipment,
        bool allCategories,
        ObservableCollection<string>? selectedMaterialCategories,
        ObservableCollection<string>? selectedEquipmentCategories)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        _sidebarFooter.UpdateReportGenerationProgress(10);

        using var package = new ExcelPackage();
        var workbook = package.Workbook;

        // Determine if report is full or partial
        bool isFullReport = includeMaterials && includeEquipment && allCategories;
        string reportType = isFullReport ? "полный" : "неполный";

        _sidebarFooter.UpdateReportGenerationProgress(20);

        // Generate materials sheet if needed
        if (includeMaterials)
        {
            var materials = await GetMaterialsAsync(db, allCategories, selectedMaterialCategories);
            if (materials.Any())
            {
                var materialsFilterInfo = BuildMaterialsFilterInfo(allCategories, selectedMaterialCategories);
                AddMaterialsSheet(workbook, materials, materialsFilterInfo);
            }
        }

        _sidebarFooter.UpdateReportGenerationProgress(50);

        // Generate equipment sheet if needed
        if (includeEquipment)
        {
            var equipment = await GetEquipmentAsync(db, allCategories, selectedEquipmentCategories);
            if (equipment.Any())
            {
                var equipmentFilterInfo = BuildEquipmentFilterInfo(allCategories, selectedEquipmentCategories);
                AddEquipmentSheet(workbook, equipment, equipmentFilterInfo);
            }
        }

        _sidebarFooter.UpdateReportGenerationProgress(70);

        // Generate filename
        var fileName = GenerateFileName(reportType);
        var documentsPath = MpmsDocumentPaths.GetDocumentsDirectory();
        var filePath = Path.Combine(documentsPath, fileName);

        // Save workbook
        package.SaveAs(new FileInfo(filePath));

        _sidebarFooter.UpdateReportGenerationProgress(90);

        // Add file to database
        await AddReportToDatabaseAsync(filePath, fileName, reportType);

        _sidebarFooter.UpdateReportGenerationProgress(100);

        return filePath;
    }

    private async Task AddReportToDatabaseAsync(string filePath, string fileName, string reportType)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        
        var fileData = await File.ReadAllBytesAsync(filePath);
        var fileInfo = new FileInfo(filePath);

        var newFile = new LocalFile
        {
            Id = Guid.NewGuid(),
            FileName = fileName,
            FileSize = fileInfo.Length,
            FileType = ".xlsx",
            FilePath = filePath,
            FileData = fileData,
            ProjectId = null,
            TaskId = null,
            StageId = null,
            UploadedById = _auth.UserId ?? Guid.Empty,
            UploadedByName = _auth.UserName ?? "Unknown",
            CreatedAt = DateTime.UtcNow,
            OriginalCreatedAt = fileInfo.CreationTimeUtc,
            Description = $"Отчёт остатки по складу ({reportType})"
        };

        db.Files.Add(newFile);
        await db.SaveChangesAsync();

        // Create sync payload manually
        var syncPayload = new
        {
            Id = newFile.Id,
            FileName = newFile.FileName,
            FileType = newFile.FileType,
            FileSize = newFile.FileSize,
            UploadedById = newFile.UploadedById,
            UploadedByName = newFile.UploadedByName,
            ProjectId = newFile.ProjectId,
            TaskId = newFile.TaskId,
            StageId = newFile.StageId,
            CreatedAt = newFile.CreatedAt,
            OriginalCreatedAt = newFile.OriginalCreatedAt
        };
        await _sync.QueueOperationAsync("File", newFile.Id, SyncOperation.Create, syncPayload);

        await LogActivityAsync(db, $"Создан отчёт «{newFile.FileName}»", "Document", newFile.Id);

        // Notify UI that a new file was added
        ReportGenerated?.Invoke(newFile);
    }

    private async Task LogActivityAsync(LocalDbContext db, string text, string entityType, Guid entityId)
    {
        var log = new LocalActivityLog
        {
            Id = Guid.NewGuid(),
            UserId = _auth.UserId ?? Guid.Empty,
            UserName = _auth.UserName ?? "Unknown",
            ActorRole = _auth.UserRole,
            EntityType = entityType,
            EntityId = entityId,
            ActionType = "Created",
            ActionText = text,
            DetailsText = text,
            CreatedAt = DateTime.UtcNow
        };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
    }

    private string BuildMaterialsFilterInfo(bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        if (allCategories)
            return "Все категории";
        
        if (selectedCategories != null && selectedCategories.Count > 0)
            return $"Категории: {string.Join(", ", selectedCategories)}";
        
        return "Без фильтра";
    }

    private string BuildEquipmentFilterInfo(bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        if (allCategories)
            return "Все категории";
        
        if (selectedCategories != null && selectedCategories.Count > 0)
            return $"Категории: {string.Join(", ", selectedCategories)}";
        
        return "Без фильтра";
    }

    private async Task<List<LocalMaterial>> GetMaterialsAsync(
        LocalDbContext db,
        bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        var query = db.Materials.Where(m => !m.IsArchived).AsQueryable();

        if (!allCategories)
        {
            if (selectedCategories != null && selectedCategories.Count > 0)
            {
                var categoryNames = selectedCategories.ToList();
                query = query.Where(m => categoryNames.Contains(m.CategoryName ?? ""));
            }
            else
            {
                // If not all categories and no specific categories selected, return empty
                return new List<LocalMaterial>();
            }
        }

        return await query
            .OrderBy(m => m.CategoryName)
            .ThenBy(m => m.Name)
            .ToListAsync();
    }

    private async Task<List<LocalEquipment>> GetEquipmentAsync(
        LocalDbContext db,
        bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        var query = db.Equipments.Where(e => !e.IsArchived).AsQueryable();

        if (!allCategories)
        {
            if (selectedCategories != null && selectedCategories.Count > 0)
            {
                var categoryNames = selectedCategories.ToList();
                query = query.Where(e => categoryNames.Contains(e.CategoryName ?? ""));
            }
            else
            {
                // If not all categories and no specific categories selected, return empty
                return new List<LocalEquipment>();
            }
        }

        return await query
            .OrderBy(e => e.CategoryName)
            .ThenBy(e => e.Name)
            .ToListAsync();
    }

    private void AddMaterialsSheet(ExcelWorkbook workbook, List<LocalMaterial> materials, string filterInfo)
    {
        var sheet = workbook.Worksheets.Add("Материалы");

        // Set column widths
        sheet.Column(1).Width = 6; // №
        sheet.Column(2).Width = 20; // Категория
        sheet.Column(3).Width = 30; // Наименование
        sheet.Column(4).Width = 15; // Инв. номер
        sheet.Column(5).Width = 12; // Единица
        sheet.Column(6).Width = 12; // Количество
        sheet.Column(7).Width = 15; // Цена
        sheet.Column(8).Width = 15; // Сумма
        sheet.Column(9).AutoFit(); // Описание - автоширина

        // Title
        var titleRange = sheet.Cells["A1:I1"];
        titleRange.Merge = true;
        titleRange.Value = "Отчёт остатки по складу - Материалы";
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.Size = 14;
        titleRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        titleRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

        // Date
        var dateRange = sheet.Cells["A2:I2"];
        dateRange.Merge = true;
        dateRange.Value = $"Дата: {DateTime.Now:dd.MM.yyyy}";
        dateRange.Style.Font.Italic = true;
        dateRange.Style.Font.Size = 10;
        dateRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        // Filter info
        var filterRange = sheet.Cells["A3:I3"];
        filterRange.Merge = true;
        filterRange.Value = $"Отбор: {filterInfo}";
        filterRange.Style.Font.Size = 10;
        filterRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        // Header row
        var headers = new[] { "№", "Категория", "Наименование", "Инв. номер", "Единица", "Количество", "Цена (руб.)", "Сумма (руб.)", "Описание" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cells[5, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.Size = 11;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 225, 242)); // Light blue header
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        // Enable auto-filter for Excel sorting/filtering
        sheet.Cells[5, 1, 5, headers.Length].AutoFilter = true;

        // Data rows
        int row = 6;
        decimal totalSum = 0;
        int rowNum = 1;

        foreach (var material in materials)
        {
            var sum = (material.Quantity * (material.Cost ?? 0));
            totalSum += sum;

            sheet.Cells[row, 1].Value = rowNum;
            sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Cells[row, 2].Value = material.CategoryName ?? "Без категории";
            sheet.Cells[row, 3].Value = material.Name;
            sheet.Cells[row, 4].Value = material.InventoryNumber ?? "";
            sheet.Cells[row, 5].Value = material.Unit ?? "";
            sheet.Cells[row, 6].Value = material.Quantity;
            
            if (material.Cost.HasValue)
            {
                sheet.Cells[row, 7].Value = material.Cost.Value;
            }
            
            sheet.Cells[row, 8].Value = sum;
            sheet.Cells[row, 9].Value = material.Description ?? "";

            // Apply borders to data cells
            for (int col = 1; col <= 9; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            // Alternate row colors
            if (row % 2 == 0)
            {
                for (int col = 1; col <= 9; col++)
                {
                    sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252)); // Very light gray
                }
            }

            row++;
            rowNum++;
        }

        // AutoFit description column after data is filled
        sheet.Column(9).AutoFit();

        // Total row
        sheet.Cells[row, 1].Value = "Итого:";
        sheet.Cells[row, 1].Style.Font.Bold = true;
        sheet.Cells[row, 8].Value = totalSum;
        sheet.Cells[row, 8].Style.Font.Bold = true;

        for (int col = 1; col <= 9; col++)
        {
            sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Medium;
            sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
            sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
            sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        // Summary info
        row += 2;
        sheet.Cells[$"A{row}"].Value = $"Всего материалов: {materials.Count}";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
    }

    private void AddEquipmentSheet(ExcelWorkbook workbook, List<LocalEquipment> equipment, string filterInfo)
    {
        var sheet = workbook.Worksheets.Add("Оборудование");

        // Set column widths
        sheet.Column(1).Width = 6; // №
        sheet.Column(2).Width = 20; // Категория
        sheet.Column(3).Width = 30; // Наименование
        sheet.Column(4).Width = 15; // Инв. номер
        sheet.Column(5).Width = 18; // Статус
        sheet.Column(6).Width = 15; // Состояние
        sheet.Column(7).Width = 25; // Проект
        sheet.Column(8).AutoFit(); // Описание - автоширина

        // Title
        var titleRange = sheet.Cells["A1:H1"];
        titleRange.Merge = true;
        titleRange.Value = "Отчёт остатки по складу - Оборудование";
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.Size = 14;
        titleRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        titleRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

        // Date
        var dateRange = sheet.Cells["A2:H2"];
        dateRange.Merge = true;
        dateRange.Value = $"Дата: {DateTime.Now:dd.MM.yyyy}";
        dateRange.Style.Font.Italic = true;
        dateRange.Style.Font.Size = 10;
        dateRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        // Filter info
        var filterRange = sheet.Cells["A3:H3"];
        filterRange.Merge = true;
        filterRange.Value = $"Отбор: {filterInfo}";
        filterRange.Style.Font.Size = 10;
        filterRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        // Header row
        var headers = new[] { "№", "Категория", "Наименование", "Инв. номер", "Статус", "Состояние", "Проект", "Описание" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cells[5, i + 1];
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.Size = 11;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 225, 242)); // Light blue header
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
            cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
        }

        // Enable auto-filter for Excel sorting/filtering
        sheet.Cells[5, 1, 5, headers.Length].AutoFilter = true;

        // Data rows
        int row = 6;
        int availableCount = 0;
        int inUseCount = 0;
        int unavailableCount = 0;
        int writtenOffCount = 0;
        int rowNum = 1;

        foreach (var eq in equipment)
        {
            var statusDisplay = GetStatusDisplay(eq.Status);
            var conditionDisplay = GetConditionDisplay(eq.Condition);

            // Count by status
            switch (eq.Status)
            {
                case "Available":
                    availableCount++;
                    break;
                case "InUse":
                case "CheckedOut":
                    inUseCount++;
                    break;
                case "Unavailable":
                case "3":
                    unavailableCount++;
                    break;
                case "Retired":
                    writtenOffCount++;
                    break;
            }

            sheet.Cells[row, 1].Value = rowNum;
            sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Cells[row, 2].Value = eq.CategoryName ?? "Без категории";
            sheet.Cells[row, 3].Value = eq.Name;
            sheet.Cells[row, 4].Value = eq.InventoryNumber ?? "";
            sheet.Cells[row, 5].Value = statusDisplay;
            sheet.Cells[row, 6].Value = conditionDisplay;
            sheet.Cells[row, 7].Value = ""; // Project name would need to be loaded separately
            sheet.Cells[row, 8].Value = eq.Description ?? "";

            // Apply borders to data cells
            for (int col = 1; col <= 8; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            // Alternate row colors
            if (row % 2 == 0)
            {
                for (int col = 1; col <= 8; col++)
                {
                    sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252)); // Very light gray
                }
            }

            row++;
            rowNum++;
        }

        // AutoFit description column after data is filled
        sheet.Column(8).AutoFit();

        // Summary info
        row += 2;
        sheet.Cells[$"A{row}"].Value = $"Всего оборудования: {equipment.Count}";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        row++;
        sheet.Cells[$"A{row}"].Value = $"Доступно: {availableCount}";
        row++;
        sheet.Cells[$"A{row}"].Value = $"Используется: {inUseCount}";
        row++;
        sheet.Cells[$"A{row}"].Value = $"Недоступно: {unavailableCount}";
        row++;
        sheet.Cells[$"A{row}"].Value = $"Списано: {writtenOffCount}";
    }

    private string GetStatusDisplay(string status) => status switch
    {
        "Available" => "Доступно",
        "Unavailable" or "3" => "Недоступно",
        "InUse" or "CheckedOut" => "Используется",
        "Retired" => "Списано",
        _ => status
    };

    private string GetConditionDisplay(string condition) => condition switch
    {
        "Good" => "Исправно",
        "NeedsMaintenance" => "Требует обслуживания",
        "Faulty" => "Неисправно",
        _ => condition
    };

    private string GenerateFileName(string reportType)
    {
        var dateStr = DateTime.Now.ToString("dd.MM.yyyy");
        var baseName = $"Отчёт остатки по складу_{dateStr}_{reportType}";
        var documentsPath = MpmsDocumentPaths.GetDocumentsDirectory();
        var filePath = Path.Combine(documentsPath, $"{baseName}.xlsx");

        // Check if file exists and add number if needed
        int counter = 1;
        while (File.Exists(filePath))
        {
            counter++;
            filePath = Path.Combine(documentsPath, $"{baseName}_{counter}.xlsx");
        }

        return Path.GetFileName(filePath);
    }
}
