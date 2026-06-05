using System.IO;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using MPMS.Data;
using MPMS.Models;
using MPMS.Infrastructure;
using MPMS.ViewModels;

namespace MPMS.Services;

public class ProjectReportService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public event Action<LocalFile>? ReportGenerated;

    public ProjectReportService(
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

    public async Task<string> GenerateProjectReportAsync(Guid projectId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        _sidebarFooter.UpdateReportGenerationProgress(10);

        // Load project with all related data
        var project = await db.Projects
            .Where(p => p.Id == projectId)
            .FirstOrDefaultAsync();

        if (project == null)
            throw new ArgumentException("Project not found");

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == projectId && !t.IsArchived)
            .OrderBy(t => t.Name)
            .ToListAsync();

        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages
            .Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived)
            .OrderBy(s => s.TaskId)
            .ThenBy(s => s.Name)
            .ToListAsync();

        var stageIds = stages.Select(s => s.Id).ToList();
        var stageWorkTypes = await db.StageWorkTypes
            .Where(swt => stageIds.Contains(swt.StageId))
            .ToListAsync();

        var workTypeTemplateIds = stageWorkTypes.Select(swt => swt.WorkTypeTemplateId).Distinct().ToList();
        var workTypeTemplates = await db.WorkTypeTemplates
            .Where(wt => workTypeTemplateIds.Contains(wt.Id))
            .Select(wt => new { wt.Id, wt.Article })
            .ToDictionaryAsync(wt => wt.Id, wt => wt.Article);

        var stageMaterials = await db.StageMaterials
            .Where(sm => stageIds.Contains(sm.StageId))
            .ToListAsync();

        var materialIds = stageMaterials.Select(sm => sm.MaterialId).Distinct().ToList();
        var materials = await db.Materials
            .Where(m => materialIds.Contains(m.Id))
            .Select(m => new { m.Id, m.InventoryNumber })
            .ToDictionaryAsync(m => m.Id, m => m.InventoryNumber);

        var stageAssignees = await db.StageAssignees
            .Where(sa => stageIds.Contains(sa.StageId))
            .ToListAsync();
        var stageAssigneesDict = stageAssignees.GroupBy(sa => sa.StageId)
            .ToDictionary(g => g.Key, g => g.Select(sa => sa.UserName).ToList());

        _sidebarFooter.UpdateReportGenerationProgress(30);

        using var package = new ExcelPackage();
        var workbook = package.Workbook;

        // Sheet 1: Project Info with tasks and stages
        AddProjectInfoSheet(workbook, project, tasks, stages, stageWorkTypes, stageMaterials, workTypeTemplates, materials, stageAssigneesDict);

        _sidebarFooter.UpdateReportGenerationProgress(60);

        // Sheet 2: Work Types by Stage
        AddWorkTypesByStageSheet(workbook, stages, stageWorkTypes, workTypeTemplates);

        _sidebarFooter.UpdateReportGenerationProgress(80);

        // Sheet 3: Materials by Stage
        AddMaterialsByStageSheet(workbook, stages, stageMaterials, materials);

        _sidebarFooter.UpdateReportGenerationProgress(90);

        // Generate filename
        var fileName = GenerateFileName(project.Name);
        var documentsPath = MpmsDocumentPaths.GetDocumentsDirectory();
        var filePath = Path.Combine(documentsPath, fileName);

        // Save workbook
        package.SaveAs(new FileInfo(filePath));

        // Add file to database
        await AddReportToDatabaseAsync(filePath, fileName, project.Name, projectId);

        _sidebarFooter.UpdateReportGenerationProgress(100);

        return filePath;
    }

    private void AddProjectInfoSheet(ExcelWorkbook workbook, LocalProject project,
        List<LocalTask> tasks, List<LocalTaskStage> stages,
        List<LocalStageWorkType> stageWorkTypes, List<LocalStageMaterial> stageMaterials,
        Dictionary<Guid, string?> workTypeTemplates, Dictionary<Guid, string?> materials,
        Dictionary<Guid, List<string>> stageAssigneesDict)
    {
        var sheet = workbook.Worksheets.Add("Общая информация");

        // Title
        var titleRange = sheet.Cells["A1:H1"];
        titleRange.Merge = true;
        titleRange.Value = $"Отчёт по проекту: {project.Name}";
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
        sheet.Cells[$"A{row}"].Value = "Информация о проекте";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"A{row}"].Style.Font.Size = 14;
        row++;

        sheet.Cells[$"A{row}"].Value = "Название:";
        sheet.Cells[$"B{row}"].Value = project.Name;
        row++;

        if (!string.IsNullOrWhiteSpace(project.Description))
        {
            sheet.Cells[$"A{row}"].Value = "Описание:";
            sheet.Cells[$"B{row}"].Value = project.Description;
            row++;
        }

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

        if (project.StartDate.HasValue)
        {
            sheet.Cells[$"A{row}"].Value = "Дата начала:";
            sheet.Cells[$"B{row}"].Value = project.StartDate.Value.ToString("dd.MM.yyyy");
            row++;
        }

        if (project.EndDate.HasValue)
        {
            sheet.Cells[$"A{row}"].Value = "Дата окончания:";
            sheet.Cells[$"B{row}"].Value = project.EndDate.Value.ToString("dd.MM.yyyy");
            row++;
        }

        if (project.IsClosed && project.ClosedAt.HasValue)
        {
            sheet.Cells[$"A{row}"].Value = "Дата закрытия:";
            sheet.Cells[$"B{row}"].Value = project.ClosedAt.Value.ToString("dd.MM.yyyy");
            row++;
        }

        // Add border around project info section
        var infoStartRow = 4;
        var infoEndRow = row - 1;
        for (int r = infoStartRow; r <= infoEndRow; r++)
        {
            for (int c = 1; c <= 2; c++)
            {
                sheet.Cells[r, c].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                sheet.Cells[r, c].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                sheet.Cells[r, c].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[r, c].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }
        }

        row += 2;

        // Calculate totals with base prices and adjustments
        decimal totalWorkTypesBaseCost = stageWorkTypes.Sum(swt => swt.Quantity * swt.BasePricePerUnit);
        decimal totalMaterialsBaseCost = stageMaterials.Sum(sm => sm.Quantity * sm.BasePricePerUnit);
        decimal totalBaseCost = totalWorkTypesBaseCost + totalMaterialsBaseCost;
        
        // Group by stage to apply stage-level adjustments
        var workTypesByStage = stageWorkTypes.GroupBy(swt => swt.StageId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var materialsByStage = stageMaterials.GroupBy(sm => sm.StageId)
            .ToDictionary(g => g.Key, g => g.ToList());
        
        var stagesDict = stages.ToDictionary(s => s.Id);
        
        decimal totalWorkTypesCost = 0m;
        decimal totalMaterialsCost = 0m;
        
        foreach (var stage in stages)
        {
            var stageWorkTypesList = workTypesByStage.GetValueOrDefault(stage.Id, []);
            var stageMaterialsList = materialsByStage.GetValueOrDefault(stage.Id, []);
            
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;
            
            totalWorkTypesCost += stageWorkTypesList.Sum(swt => swt.Quantity * swt.PricePerUnit) * serviceK;
            totalMaterialsCost += stageMaterialsList.Sum(sm => sm.Quantity * sm.PricePerUnit) * materialK;
        }
        
        decimal grandTotal = totalWorkTypesCost + totalMaterialsCost;
        decimal totalDiscount = totalBaseCost - grandTotal;

        // Totals section
        sheet.Cells[$"A{row}"].Value = "Итого по проекту:";
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

        row += 2;

        // Tasks and stages section
        sheet.Cells[$"A{row}"].Value = "Задачи и этапы";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"A{row}"].Style.Font.Size = 14;
        row++;

        // Header for tasks/stages
        var headers = new[] { "Задача", "Этап", "Исполнитель", "Статус", "Виды работ (руб.)", "Материалы (руб.)", "Итого (руб.)" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = sheet.Cells[row, i + 1];
            cell.Value = headers[i];
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

        // Data rows
        foreach (var task in tasks)
        {
            var taskStages = stages.Where(s => s.TaskId == task.Id).ToList();
            
            if (taskStages.Any())
            {
                foreach (var stage in taskStages)
                {
                    var stageWorkTypesCostBefore = stageWorkTypes
                        .Where(swt => swt.StageId == stage.Id)
                        .Sum(swt => swt.Quantity * swt.PricePerUnit);
                    var stageMaterialsCostBefore = stageMaterials
                        .Where(sm => sm.StageId == stage.Id)
                        .Sum(sm => sm.Quantity * sm.PricePerUnit);
                    
                    var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
                    var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;
                    
                    var stageWorkTypesCost = stageWorkTypesCostBefore * serviceK;
                    var stageMaterialsCost = stageMaterialsCostBefore * materialK;
                    var stageTotal = stageWorkTypesCost + stageMaterialsCost;

                    var assignees = stageAssigneesDict.ContainsKey(stage.Id) && stageAssigneesDict[stage.Id].Any()
                        ? string.Join(", ", stageAssigneesDict[stage.Id])
                        : (stage.AssignedUserName ?? "");

                    sheet.Cells[row, 1].Value = task.Name;
                    sheet.Cells[row, 2].Value = stage.Name;
                    sheet.Cells[row, 3].Value = assignees;
                    sheet.Cells[row, 4].Value = GetStageStatusDisplay(stage.Status);
                    sheet.Cells[row, 5].Value = stageWorkTypesCost;
                    sheet.Cells[row, 5].Style.Numberformat.Format = "#,##0.00";
                    sheet.Cells[row, 6].Value = stageMaterialsCost;
                    sheet.Cells[row, 6].Style.Numberformat.Format = "#,##0.00";
                    sheet.Cells[row, 7].Value = stageTotal;
                    sheet.Cells[row, 7].Style.Numberformat.Format = "#,##0.00";
                    sheet.Cells[row, 7].Style.Font.Bold = true;

                    for (int col = 1; col <= 7; col++)
                    {
                        sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                        sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                        sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                        sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                    }

                    if (row % 2 == 0)
                    {
                        for (int col = 1; col <= 7; col++)
                        {
                            sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                            sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                        }
                    }

                    row++;
                }
            }
            else
            {
                // Task with no stages
                sheet.Cells[row, 1].Value = task.Name;
                sheet.Cells[row, 2].Value = "(нет этапов)";
                sheet.Cells[row, 3].Value = task.AssignedUserName ?? "";
                sheet.Cells[row, 4].Value = GetTaskStatusDisplay(task.Status);
                sheet.Cells[row, 5].Value = 0;
                sheet.Cells[row, 6].Value = 0;
                sheet.Cells[row, 7].Value = 0;

                for (int col = 1; col <= 7; col++)
                {
                    sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                    sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
                }

                if (row % 2 == 0)
                {
                    for (int col = 1; col <= 7; col++)
                    {
                        sheet.Cells[row, col].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        sheet.Cells[row, col].Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(248, 250, 252));
                    }
                }

                row++;
            }
        }

        // Auto-fit columns
        sheet.Cells.AutoFitColumns();
    }

    private void AddWorkTypesByStageSheet(ExcelWorkbook workbook, List<LocalTaskStage> stages,
        List<LocalStageWorkType> stageWorkTypes, Dictionary<Guid, string?> workTypeTemplates)
    {
        var sheet = workbook.Worksheets.Add("Виды работ");

        // Title
        var titleRange = sheet.Cells["A1:H1"];
        titleRange.Merge = true;
        titleRange.Value = "Виды работ по этапам";
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

        int row = 4;
        decimal grandTotal = 0;

        foreach (var stage in stages)
        {
            // Stage header
            sheet.Cells[$"A{row}"].Value = $"Этап: {stage.Name}";
            sheet.Cells[$"A{row}"].Style.Font.Bold = true;
            sheet.Cells[$"A{row}"].Style.Font.Size = 12;
            row++;

            // Header
            var headers = new[] { "№", "Артикул", "Вид работы", "Количество", "Цена базовая (руб.)", "Скидка/Наценка %", "Итоговая цена (без учёта скидки) (руб.)", "Итоговая цена" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = sheet.Cells[row, i + 1];
                cell.Value = headers[i];
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

            var workTypes = stageWorkTypes.Where(swt => swt.StageId == stage.Id).ToList();
            int rowNum = 1;
            decimal stageTotal = 0;
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;

            foreach (var wt in workTypes)
            {
                var sum = wt.Quantity * wt.PricePerUnit;
                var finalSum = sum * serviceK;
                stageTotal += finalSum;
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

            // Stage total
            sheet.Cells[row, 7].Value = "Итого по этапу:";
            sheet.Cells[row, 7].Style.Font.Bold = true;
            sheet.Cells[row, 8].Value = stageTotal;
            sheet.Cells[row, 8].Style.Font.Bold = true;
            sheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.00";

            for (int col = 1; col <= 8; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            grandTotal += stageTotal;
            row += 2;
        }

        // Grand total
        sheet.Cells[$"A{row}"].Value = "Всего по проекту:";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"H{row}"].Value = grandTotal;
        sheet.Cells[$"H{row}"].Style.Font.Bold = true;
        sheet.Cells[$"H{row}"].Style.Numberformat.Format = "#,##0.00";

        // Auto-fit columns
        sheet.Cells.AutoFitColumns();
    }

    private void AddMaterialsByStageSheet(ExcelWorkbook workbook, List<LocalTaskStage> stages,
        List<LocalStageMaterial> stageMaterials, Dictionary<Guid, string?> materials)
    {
        var sheet = workbook.Worksheets.Add("Материалы");

        // Title
        var titleRange = sheet.Cells["A1:H1"];
        titleRange.Merge = true;
        titleRange.Value = "Материалы по этапам";
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

        int row = 4;
        decimal grandTotal = 0;

        foreach (var stage in stages)
        {
            // Stage header
            sheet.Cells[$"A{row}"].Value = $"Этап: {stage.Name}";
            sheet.Cells[$"A{row}"].Style.Font.Bold = true;
            sheet.Cells[$"A{row}"].Style.Font.Size = 12;
            row++;

            // Header
            var headers = new[] { "№", "Артикул", "Материал", "Количество", "Цена базовая (руб.)", "Скидка/Наценка %", "Итоговая цена (без учёта скидки) (руб.)", "Итоговая цена" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = sheet.Cells[row, i + 1];
                cell.Value = headers[i];
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

            var stageMaterialsList = stageMaterials.Where(sm => sm.StageId == stage.Id).ToList();
            int rowNum = 1;
            decimal stageTotal = 0;
            var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;

            foreach (var mat in stageMaterialsList)
            {
                var sum = mat.Quantity * mat.PricePerUnit;
                var finalSum = sum * materialK;
                stageTotal += finalSum;
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

            // Stage total
            sheet.Cells[row, 7].Value = "Итого по этапу:";
            sheet.Cells[row, 7].Style.Font.Bold = true;
            sheet.Cells[row, 8].Value = stageTotal;
            sheet.Cells[row, 8].Style.Font.Bold = true;
            sheet.Cells[row, 8].Style.Numberformat.Format = "#,##0.00";

            for (int col = 1; col <= 8; col++)
            {
                sheet.Cells[row, col].Style.Border.Top.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Bottom.Style = ExcelBorderStyle.Medium;
                sheet.Cells[row, col].Style.Border.Left.Style = ExcelBorderStyle.Thin;
                sheet.Cells[row, col].Style.Border.Right.Style = ExcelBorderStyle.Thin;
            }

            grandTotal += stageTotal;
            row += 2;
        }

        // Grand total
        sheet.Cells[$"A{row}"].Value = "Всего по проекту:";
        sheet.Cells[$"A{row}"].Style.Font.Bold = true;
        sheet.Cells[$"H{row}"].Value = grandTotal;
        sheet.Cells[$"H{row}"].Style.Font.Bold = true;
        sheet.Cells[$"H{row}"].Style.Numberformat.Format = "#,##0.00";

        // Auto-fit columns
        sheet.Cells.AutoFitColumns();
    }

    private async Task AddReportToDatabaseAsync(string filePath, string fileName, string projectName, Guid projectId)
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
            StageId = null,
            UploadedById = _auth.UserId ?? Guid.Empty,
            UploadedByName = _auth.UserName ?? "Unknown",
            CreatedAt = DateTime.UtcNow,
            OriginalCreatedAt = fileInfo.CreationTimeUtc,
            Description = $"Отчёт по проекту: {projectName}"
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

        await LogActivityAsync(db, $"Создан отчёт по проекту «{newFile.FileName}»", "Document", newFile.Id);

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

    private string GenerateFileName(string projectName)
    {
        var dateStr = DateTime.Now.ToString("dd.MM.yyyy");
        var safeName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars()));
        var baseName = $"Отчёт по проекту_{safeName}_{dateStr}";
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
