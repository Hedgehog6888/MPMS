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

public class PriceListService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public event Action<LocalFile>? ReportGenerated;

    public PriceListService(
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

    public async Task<string> GeneratePriceListAsync(
        bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        _sidebarFooter.UpdateReportGenerationProgress(10);

        using var package = new ExcelPackage();
        var workbook = package.Workbook;

        _sidebarFooter.UpdateReportGenerationProgress(20);

        // Generate price list sheet
        var workTypes = await GetWorkTypesAsync(db, allCategories, selectedCategories);
        if (workTypes.Any())
        {
            var filterInfo = BuildFilterInfo(allCategories, selectedCategories);
            AddPriceListSheet(workbook, workTypes, filterInfo);
        }

        _sidebarFooter.UpdateReportGenerationProgress(70);

        // Generate filename
        var fileName = GenerateFileName();
        var documentsPath = MpmsDocumentPaths.GetDocumentsDirectory();
        var filePath = Path.Combine(documentsPath, fileName);

        // Save workbook
        package.SaveAs(new FileInfo(filePath));

        _sidebarFooter.UpdateReportGenerationProgress(90);

        // Add file to database
        await AddPriceListToDatabaseAsync(filePath, fileName);

        _sidebarFooter.UpdateReportGenerationProgress(100);

        return filePath;
    }

    private async Task AddPriceListToDatabaseAsync(string filePath, string fileName)
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
            Description = "Прайс лист ООО Монтажные системы"
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

        await LogActivityAsync(db, $"Создан прайс лист «{newFile.FileName}»", "Document", newFile.Id);

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

    private string BuildFilterInfo(bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        if (allCategories)
            return "Все категории";
        
        if (selectedCategories != null && selectedCategories.Count > 0)
            return $"Категории: {string.Join(", ", selectedCategories)}";
        
        return "Без фильтра";
    }

    private async Task<List<LocalWorkTypeTemplate>> GetWorkTypesAsync(
        LocalDbContext db,
        bool allCategories,
        ObservableCollection<string>? selectedCategories)
    {
        var query = db.WorkTypeTemplates.Where(w => w.IsActive).AsQueryable();

        if (!allCategories)
        {
            if (selectedCategories != null && selectedCategories.Count > 0)
            {
                var categoryNames = selectedCategories.ToList();
                query = query.Where(w => categoryNames.Contains(w.CategoryName ?? ""));
            }
            else
            {
                // If not all categories and no specific categories selected, return empty
                return new List<LocalWorkTypeTemplate>();
            }
        }

        return await query
            .OrderBy(w => w.CategoryName)
            .ThenBy(w => w.Name)
            .ToListAsync();
    }

    private void AddPriceListSheet(ExcelWorkbook workbook, List<LocalWorkTypeTemplate> workTypes, string filterInfo)
    {
        var sheet = workbook.Worksheets.Add("Прайс лист");

        // Set column widths
        sheet.Column(1).Width = 6; // №
        sheet.Column(2).Width = 25; // Категория
        sheet.Column(3).Width = 35; // Наименование
        sheet.Column(4).Width = 12; // Артикул
        sheet.Column(5).Width = 15; // Цена
        // Description column will be AutoFit after data is filled

        // Header row with title (centered, spanning 3 rows)
        var titleRange = sheet.Cells["A1:F1"];
        titleRange.Merge = true;
        titleRange.Value = "Прайс лист ООО Монтажные системы";
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.Size = 16;
        titleRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        titleRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

        // Date
        var dateRange = sheet.Cells["A2:F2"];
        dateRange.Merge = true;
        dateRange.Value = $"Дата: {DateTime.Now:dd.MM.yyyy}";
        dateRange.Style.Font.Italic = true;
        dateRange.Style.Font.Size = 10;
        dateRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        // Filter info
        var filterRange = sheet.Cells["A3:F3"];
        filterRange.Merge = true;
        filterRange.Value = $"Отбор: {filterInfo}";
        filterRange.Style.Font.Size = 10;
        filterRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        // Header row
        var headers = new[] { "№", "Категория", "Наименование", "Артикул", "Цена (руб.)", "Описание" };
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
        int rowNum = 1;

        foreach (var workType in workTypes)
        {
            sheet.Cells[row, 1].Value = rowNum;
            sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            sheet.Cells[row, 2].Value = workType.CategoryName ?? "Без категории";
            sheet.Cells[row, 3].Value = workType.Name;
            sheet.Cells[row, 4].Value = workType.Article ?? "";
            sheet.Cells[row, 5].Value = workType.BasePrice;
            sheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
            sheet.Cells[row, 6].Value = workType.Description ?? "";

            // Apply borders to data cells
            for (int col = 1; col <= 6; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            // Alternate row colors
            if (row % 2 == 0)
            {
                for (int col = 1; col <= 6; col++)
                {
                    sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                    sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252)); // Very light gray
                }
            }

            row++;
            rowNum++;
        }

        // AutoFit description column after data is filled
        sheet.Column(6).AutoFit();

        // Summary info
        row += 2;
        sheet.Cells[$"A{row}"].Value = $"Всего видов работ: {workTypes.Count}";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
    }

    private string GenerateFileName()
    {
        var dateStr = DateTime.Now.ToString("dd.MM.yyyy");
        var baseName = $"Прайс лист_{dateStr}";
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
