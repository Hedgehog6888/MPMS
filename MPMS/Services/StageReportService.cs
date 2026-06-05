using System.IO;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using MPMS.Data;
using MPMS.Models;
using MPMS.Infrastructure;
using MPMS.ViewModels;

namespace MPMS.Services;

public class StageReportService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public event Action<LocalFile>? ReportGenerated;

    public StageReportService(
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

    public async Task<string> GenerateStageReportAsync(Guid stageId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        _sidebarFooter.UpdateReportGenerationProgress(10);

        // Load stage with related data
        var stage = await db.TaskStages
            .Where(s => s.Id == stageId)
            .FirstOrDefaultAsync();

        if (stage == null)
            throw new ArgumentException("Stage not found");

        var task = await db.Tasks
            .Where(t => t.Id == stage.TaskId)
            .FirstOrDefaultAsync();

        LocalProject? project = null;
        if (task != null)
        {
            project = await db.Projects
                .Where(p => p.Id == task.ProjectId)
                .FirstOrDefaultAsync();
        }

        var stageWorkTypes = await db.StageWorkTypes
            .Where(swt => swt.StageId == stageId)
            .ToListAsync();

        var workTypeTemplateIds = stageWorkTypes.Select(swt => swt.WorkTypeTemplateId).Distinct().ToList();
        var workTypeTemplates = await db.WorkTypeTemplates
            .Where(wt => workTypeTemplateIds.Contains(wt.Id))
            .Select(wt => new { wt.Id, wt.Article })
            .ToDictionaryAsync(wt => wt.Id, wt => wt.Article);

        var stageMaterials = await db.StageMaterials
            .Where(sm => sm.StageId == stageId)
            .ToListAsync();

        var materialIds = stageMaterials.Select(sm => sm.MaterialId).Distinct().ToList();
        var materials = await db.Materials
            .Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.InventoryNumber })
            .ToDictionaryAsync(m => m.Id, m => m.InventoryNumber);

        _sidebarFooter.UpdateReportGenerationProgress(50);

        using var package = new ExcelPackage();
        var workbook = package.Workbook;

        // Single sheet with all information
        AddStageReportSheet(workbook, stage, task, project, stageWorkTypes, stageMaterials, workTypeTemplates, materials);

        _sidebarFooter.UpdateReportGenerationProgress(90);

        // Generate filename
        var fileName = GenerateFileName(stage.Name, project?.Name);
        var documentsPath = MpmsDocumentPaths.GetDocumentsDirectory();
        var filePath = Path.Combine(documentsPath, fileName);

        // Save workbook
        package.SaveAs(new FileInfo(filePath));

        // Add file to database
        await AddReportToDatabaseAsync(filePath, fileName, stage.Name, stageId, project?.Id);

        _sidebarFooter.UpdateReportGenerationProgress(100);

        return filePath;
    }

    private void AddStageReportSheet(ExcelWorkbook workbook, LocalTaskStage stage,
        LocalTask? task, LocalProject? project,
        List<LocalStageWorkType> stageWorkTypes, List<LocalStageMaterial> stageMaterials,
        Dictionary<Guid, string?> workTypeTemplates, Dictionary<Guid, string?> materials)
    {
        // Calculate stage-level adjustment factors
        var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
        var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;

        var sheet = workbook.Worksheets.Add("Отчёт по этапу");

        // Title
        var titleRange = sheet.Cells["A1:H1"];
        titleRange.Merge = true;
        titleRange.Value = $"Отчёт по этапу: {stage.Name}";
        titleRange.Style.Font.Bold = true;
        titleRange.Style.Font.Size = 16;
        titleRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        titleRange.Style.VerticalAlignment = ExcelVerticalAlignment.Center;

        // Date
        var dateRange = sheet.Cells["A2:H2"];
        dateRange.Merge = true;
        dateRange.Value = $"Дата: {DateTime.Now:dd.MM.yyyy}";
        dateRange.Style.Font.Italic = true;
        dateRange.Style.Font.Size = 10;
        dateRange.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;

        int row = 4;

        // Project information section
        if (project != null)
        {
            sheet.Cells[$"A{row}"].Value = "Проект";
            sheet.Cells[$"A{row}"].Style.Font.Bold = true;
            sheet.Cells[$"A{row}"].Style.Font.Size = 14;
            row++;

            sheet.Cells[$"A{row}"].Value = "Название:";
            sheet.Cells[$"B{row}"].Value = project.Name;
            row++;

            if (!string.IsNullOrWhiteSpace(project.Client))
            {
                sheet.Cells[$"A{row}"].Value = "Клиент:";
                sheet.Cells[$"B{row}"].Value = project.Client;
                row++;
            }

            if (!string.IsNullOrWhiteSpace(project.Address))
            {
                sheet.Cells[$"A{row}"].Value = "Адрес:";
                sheet.Cells[$"B{row}"].Value = project.Address;
                row++;
            }

            sheet.Cells[$"A{row}"].Value = "Менеджер:";
            sheet.Cells[$"B{row}"].Value = project.ManagerName;
            row++;

            sheet.Cells[$"A{row}"].Value = "Статус:";
            sheet.Cells[$"B{row}"].Value = GetProjectStatusDisplay(project.Status);
            row++;

            if (project.IsClosed && project.ClosedAt.HasValue)
            {
                sheet.Cells[$"A{row}"].Value = "Дата закрытия:";
                sheet.Cells[$"B{row}"].Value = project.ClosedAt.Value.ToString("dd.MM.yyyy");
                row++;
            }

            row++;
        }

        // Task information section
        if (task != null)
        {
            sheet.Cells[$"A{row}"].Value = "Задача";
            sheet.Cells[$"A{row}"].Style.Font.Bold = true;
            sheet.Cells[$"A{row}"].Style.Font.Size = 14;
            row++;

            sheet.Cells[$"A{row}"].Value = "Название:";
            sheet.Cells[$"B{row}"].Value = task.Name;
            row++;

            sheet.Cells[$"A{row}"].Value = "Статус:";
            sheet.Cells[$"B{row}"].Value = GetTaskStatusDisplay(task.Status);
            row++;

            if (task.DueDate.HasValue)
            {
                sheet.Cells[$"A{row}"].Value = "Срок:";
                sheet.Cells[$"B{row}"].Value = task.DueDate.Value.ToString("dd.MM.yyyy");
                row++;
            }

            row++;
        }

        // Stage information section
        sheet.Cells[$"A{row}"].Value = "Этап";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"A{row}"].Style.Font.Size = 14;
        row++;

        sheet.Cells[$"A{row}"].Value = "Название:";
        sheet.Cells[$"B{row}"].Value = stage.Name;
        row++;

        if (!string.IsNullOrWhiteSpace(stage.Description))
        {
            sheet.Cells[$"A{row}"].Value = "Описание:";
            sheet.Cells[$"B{row}"].Value = stage.Description;
            row++;
        }

        sheet.Cells[$"A{row}"].Value = "Исполнитель:";
        sheet.Cells[$"B{row}"].Value = stage.AssignedUserName ?? "";
        row++;

        sheet.Cells[$"A{row}"].Value = "Статус:";
        sheet.Cells[$"B{row}"].Value = GetStageStatusDisplay(stage.Status);
        row++;

        if (stage.DueDate.HasValue)
        {
            sheet.Cells[$"A{row}"].Value = "Срок:";
            sheet.Cells[$"B{row}"].Value = stage.DueDate.Value.ToString("dd.MM.yyyy");
            row++;
        }

        if (stage.WorkQuantity > 0)
        {
            sheet.Cells[$"A{row}"].Value = "Объём работ:";
            sheet.Cells[$"B{row}"].Value = $"{stage.WorkQuantity} {stage.WorkUnitSnapshot ?? ""}";
            row++;
        }

        row += 2;

        // Work Types section
        sheet.Cells[$"A{row}"].Value = "Виды работ";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"A{row}"].Style.Font.Size = 14;
        row++;

        if (stageWorkTypes.Any())
        {
            // Header
            var wtHeaders = new[] { "№", "Артикул", "Вид работы", "Количество", "Цена базовая (руб.)", "Скидка/Наценка %", "Итоговая цена (без учёта скидки) (руб.)", "Итоговая цена" };
            for (int i = 0; i < wtHeaders.Length; i++)
            {
                var cell = sheet.Cells[row, i + 1];
                cell.Value = wtHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Size = 11;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 225, 242));
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }
            row++;

            int rowNum = 1;
            decimal workTypesTotal = 0;

            foreach (var wt in stageWorkTypes)
            {
                var sum = wt.Quantity * wt.PricePerUnit;
                var finalSum = sum * serviceK;
                workTypesTotal += finalSum;
                var article = workTypeTemplates.ContainsKey(wt.WorkTypeTemplateId) ? workTypeTemplates[wt.WorkTypeTemplateId] : "";
                
                // Calculate total adjustment percent (line + stage)
                var totalAdjustmentPercent = (1m + wt.LineAdjustmentPercent / 100m) * (1m + stage.ServicesAdjustmentPercent / 100m) - 1m;
                totalAdjustmentPercent *= 100m;

                sheet.Cells[row, 1].Value = rowNum;
                sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                sheet.Cells[row, 2].Value = article;
                sheet.Cells[row, 3].Value = wt.WorkTypeName;
                sheet.Cells[row, 4].Value = wt.Quantity;
                sheet.Cells[row, 5].Value = wt.BasePricePerUnit;
                sheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                sheet.Cells[row, 6].Value = totalAdjustmentPercent;
                sheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.00";
                sheet.Cells[row, 7].Value = sum;
                sheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.00";
                sheet.Cells[row, 8].Value = finalSum;
                sheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.00";

                for (int col = 1; col <= 8; col++)
                {
                    sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                }

                if (row % 2 == 0)
                {
                    for (int col = 1; col <= 8; col++)
                    {
                        sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                    }
                }

                row++;
                rowNum++;
            }

            // Work types total
            sheet.Cells[row, 7].Value = "Итого:";
            sheet.Cells[row, 7].Style.Font.Bold = true;
            sheet.Cells[row, 8].Value = workTypesTotal;
            sheet.Cells[row, 8].Style.Font.Bold = true;
            sheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.00";

            for (int col = 1; col <= 8; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            row += 2;
        }
        else
        {
            sheet.Cells[$"A{row}"].Value = "(нет видов работ)";
            sheet.Cells[$"A{row}"].Style.Font.Italic = true;
            sheet.Cells[$"A{row}"].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(107, 119, 140));
            row += 2;
        }

        // Materials section
        sheet.Cells[$"A{row}"].Value = "Материалы";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"A{row}"].Style.Font.Size = 14;
        row++;

        if (stageMaterials.Any())
        {
            // Header
            var matHeaders = new[] { "№", "Артикул", "Материал", "Количество", "Цена базовая (руб.)", "Скидка/Наценка %", "Итоговая цена (без учёта скидки) (руб.)", "Итоговая цена" };
            for (int i = 0; i < matHeaders.Length; i++)
            {
                var cell = sheet.Cells[row, i + 1];
                cell.Value = matHeaders[i];
                cell.Style.Font.Bold = true;
                cell.Style.Font.Size = 11;
                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(217, 225, 242));
                cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
                cell.Style.Border.Top.Style = ExcelBorderStyle.Thin;
                cell.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                cell.Style.Border.Left.Style = ExcelBorderStyle.Thin;
                cell.Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }
            row++;

            int rowNum = 1;
            decimal materialsTotal = 0;

            foreach (var mat in stageMaterials)
            {
                var sum = mat.Quantity * mat.PricePerUnit;
                var finalSum = sum * materialK;
                materialsTotal += finalSum;
                var article = materials.ContainsKey(mat.MaterialId) ? materials[mat.MaterialId] : "";
                
                // Calculate total adjustment percent (line + stage)
                var totalAdjustmentPercent = (1m + mat.LineAdjustmentPercent / 100m) * (1m + stage.MaterialsAdjustmentPercent / 100m) - 1m;
                totalAdjustmentPercent *= 100m;

                sheet.Cells[row, 1].Value = rowNum;
                sheet.Cells[row, 1].Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                sheet.Cells[row, 2].Value = article;
                sheet.Cells[row, 3].Value = mat.MaterialName;
                sheet.Cells[row, 4].Value = mat.Quantity;
                sheet.Cells[row, 5].Value = mat.BasePricePerUnit;
                sheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                sheet.Cells[row, 6].Value = totalAdjustmentPercent;
                sheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.00";
                sheet.Cells[row, 7].Value = sum;
                sheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.00";
                sheet.Cells[row, 8].Value = finalSum;
                sheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.00";

                for (int col = 1; col <= 8; col++)
                {
                    sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                }

                if (row % 2 == 0)
                {
                    for (int col = 1; col <= 8; col++)
                    {
                        sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                    }
                }

                row++;
                rowNum++;
            }

            // Materials total
            sheet.Cells[row, 7].Value = "Итого:";
            sheet.Cells[row, 7].Style.Font.Bold = true;
            sheet.Cells[row, 8].Value = materialsTotal;
            sheet.Cells[row, 8].Style.Font.Bold = true;
            sheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.00";

            for (int col = 1; col <= 8; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            row += 2;
        }
        else
        {
            sheet.Cells[$"A{row}"].Value = "(нет материалов)";
            sheet.Cells[$"A{row}"].Style.Font.Italic = true;
            sheet.Cells[$"A{row}"].Style.Font.Color.SetColor(System.Drawing.Color.FromArgb(107, 119, 140));
            row += 2;
        }

        // Calculate totals with base prices and adjustments
        decimal totalWorkTypesBaseCost = stageWorkTypes.Sum(swt => swt.Quantity * swt.BasePricePerUnit);
        decimal totalWorkTypesCostBeforeStage = stageWorkTypes.Sum(swt => swt.Quantity * swt.PricePerUnit);
        decimal totalMaterialsBaseCost = stageMaterials.Sum(sm => sm.Quantity * sm.BasePricePerUnit);
        decimal totalMaterialsCostBeforeStage = stageMaterials.Sum(sm => sm.Quantity * sm.PricePerUnit);
        decimal totalBaseCost = totalWorkTypesBaseCost + totalMaterialsBaseCost;
        
        // Apply stage-level adjustments
        decimal totalWorkTypesCost = totalWorkTypesCostBeforeStage * serviceK;
        decimal totalMaterialsCost = totalMaterialsCostBeforeStage * materialK;
        decimal grandTotal = totalWorkTypesCost + totalMaterialsCost;
        decimal totalDiscount = totalBaseCost - grandTotal;

        // Totals section
        sheet.Cells[$"A{row}"].Value = "Итого по этапу:";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"A{row}"].Style.Font.Size = 14;
        row++;

        sheet.Cells[$"A{row}"].Value = "Всего (базовая цена):";
        sheet.Cells[$"B{row}"].Value = totalBaseCost;
        sheet.Cells[$"B{row}"].Style.Numberformat.Format = "#,##0.00";
        row++;

        sheet.Cells[$"A{row}"].Value = "Скидка/наценка:";
        sheet.Cells[$"B{row}"].Value = totalDiscount;
        sheet.Cells[$"B{row}"].Style.Numberformat.Format = "#,##0.00";
        row++;

        sheet.Cells[$"A{row}"].Value = "Всего (итоговая цена):";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"B{row}"].Value = grandTotal;
        sheet.Cells[$"B{row}"].Style.Font.Bold = true;
        sheet.Cells[$"B{row}"].Style.Numberformat.Format = "#,##0.00";

        // Add border around totals section
        var totalsStartRow = row - 3;
        var totalsEndRow = row;
        for (int r = totalsStartRow; r <= totalsEndRow; r++)
        {
            for (int c = 1; c <= 2; c++)
            {
                sheet.Cells[r, c].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                sheet.Cells[r, c].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                sheet.Cells[r, c].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[r, c].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }
        }

        // Auto-fit columns
        sheet.Cells.AutoFitColumns();
    }

    private async Task AddReportToDatabaseAsync(string filePath, string fileName, string stageName, Guid stageId, Guid? projectId)
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
            ProjectId = projectId,
            TaskId = null,
            StageId = stageId,
            UploadedById = _auth.UserId ?? Guid.Empty,
            UploadedByName = _auth.UserName ?? "Unknown",
            CreatedAt = DateTime.UtcNow,
            OriginalCreatedAt = fileInfo.CreationTimeUtc,
            Description = $"Отчёт по этапу: {stageName}"
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

        await LogActivityAsync(db, $"Создан отчёт по этапу «{newFile.FileName}»", "Document", newFile.Id);

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

    private string GetProjectStatusDisplay(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => "Планирование",
        ProjectStatus.InProgress => "В работе",
        ProjectStatus.Completed => "Завершён",
        ProjectStatus.Cancelled => "Отменён",
        ProjectStatus.Closed => "Закрыт",
        _ => status.ToString()
    };

    private string GetTaskStatusDisplay(MPMS.Models.TaskStatus status) => status switch
    {
        MPMS.Models.TaskStatus.Planned => "Запланирована",
        MPMS.Models.TaskStatus.InProgress => "В работе",
        MPMS.Models.TaskStatus.Completed => "Завершена",
        MPMS.Models.TaskStatus.Paused => "Приостановлена",
        _ => status.ToString()
    };

    private string GetStageStatusDisplay(StageStatus status) => status switch
    {
        StageStatus.Planned => "Запланирован",
        StageStatus.InProgress => "В работе",
        StageStatus.Completed => "Завершён",
        _ => status.ToString()
    };

    private string GenerateFileName(string stageName, string? projectName)
    {
        var dateStr = DateTime.Now.ToString("dd.MM.yyyy");
        var safeStageName = string.Join("_", stageName.Split(Path.GetInvalidFileNameChars()));
        var baseName = $"Отчёт по этапу_{safeStageName}_{dateStr}";
        
        if (!string.IsNullOrWhiteSpace(projectName))
        {
            var safeProjectName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars()));
            baseName = $"Отчёт по этапу_{safeProjectName}_{safeStageName}_{dateStr}";
        }
        
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
