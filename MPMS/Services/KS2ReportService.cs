using System.IO;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using MPMS.Data;
using MPMS.Models;
using MPMS.Infrastructure;
using MPMS.ViewModels;

namespace MPMS.Services;

public class KS2ReportService
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
    private readonly IAuthService _auth;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public event Action<LocalFile>? ReportGenerated;

    public KS2ReportService(
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

    public async Task<string> GenerateKS2ReportAsync(Guid projectId)
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
            .Select(wt => new { wt.Id, wt.Article, wt.Unit })
            .ToDictionaryAsync(wt => wt.Id, wt => (wt.Article, wt.Unit));

        _sidebarFooter.UpdateReportGenerationProgress(50);

        using var package = new ExcelPackage();
        var workbook = package.Workbook;

        // Create KS-2 form sheet
        AddKS2FormSheet(workbook, project, stages, stageWorkTypes, workTypeTemplates);

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

    private void AddKS2FormSheet(ExcelWorkbook workbook, LocalProject project,
        List<LocalTaskStage> stages, List<LocalStageWorkType> stageWorkTypes,
        Dictionary<Guid, (string Article, string Unit)> workTypeTemplates)
    {
        var sheet = workbook.Worksheets.Add("КС-2");

        sheet.Cells.Style.Font.Name = "Times New Roman";
        sheet.Cells.Style.Font.Size = 10;

        void SetFont(string address, int size, bool bold = false, bool italic = false, bool underline = false,
            ExcelHorizontalAlignment hAlign = ExcelHorizontalAlignment.Left,
            ExcelVerticalAlignment vAlign = ExcelVerticalAlignment.Center)
        {
            var range = sheet.Cells[address];
            range.Style.Font.Name = "Times New Roman";
            range.Style.Font.Size = size;
            range.Style.Font.Bold = bold;
            range.Style.Font.Italic = italic;
            range.Style.Font.UnderLine = underline;
            range.Style.HorizontalAlignment = hAlign;
            range.Style.VerticalAlignment = vAlign;
        }

        void SetText(string address, string text, int size, bool bold = false, bool italic = false, bool underline = false,
            ExcelHorizontalAlignment hAlign = ExcelHorizontalAlignment.Left,
            ExcelVerticalAlignment vAlign = ExcelVerticalAlignment.Center)
        {
            var range = sheet.Cells[address];
            range.Value = text;
            SetFont(address, size, bold, italic, underline, hAlign, vAlign);
        }

        void SetBorder(string address, ExcelBorderStyle style = ExcelBorderStyle.Thin, bool left = true,
            bool right = true, bool top = true, bool bottom = true)
        {
            var border = sheet.Cells[address].Style.Border;
            if (left) border.Left.Style = style;
            if (right) border.Right.Style = style;
            if (top) border.Top.Style = style;
            if (bottom) border.Bottom.Style = style;
        }

        // Set column widths according to form layout
        sheet.Column(1).Width = 3;  // A
        sheet.Column(2).Width = 5; // B
        sheet.Column(3).Width = 15; // C
        sheet.Column(4).Width = 10; // D
        sheet.Column(5).Width = 10; // E
        sheet.Column(6).Width = 5;  // F
        sheet.Column(7).Width = 8;  // G
        sheet.Column(8).Width = 5;  // H
        sheet.Column(9).Width = 5;  // I
        sheet.Column(10).Width = 5; // J
        sheet.Column(11).Width = 5; // K
        sheet.Column(12).Width = 5; // L
        sheet.Column(13).Width = 5; // M
        sheet.Column(14).Width = 5; // N
        sheet.Column(15).Width = 5; // O
        sheet.Column(16).Width = 5; // P
        sheet.Column(17).Width = 5; // Q
        sheet.Column(18).Width = 5; // R
        sheet.Column(19).Width = 5; // S
        sheet.Column(20).Width = 5; // T
        sheet.Column(21).Width = 5; // U
        sheet.Column(22).Width = 5; // V
        sheet.Column(23).Width = 5; // W
        sheet.Column(24).Width = 5; // X
        sheet.Column(25).Width = 5; // Y
        sheet.Column(26).Width = 5; // Z
        sheet.Column(27).Width = 5; // AA
        sheet.Column(28).Width = 5; // AB
        sheet.Column(29).Width = 7; // AC
        sheet.Column(30).Width = 5; // AD
        sheet.Column(31).Width = 5; // AE
        sheet.Column(32).Width = 5; // AF
        sheet.Column(33).Width = 5; // AG

        // Form header
        sheet.Cells["Y1:AG1"].Merge = true;
        SetText("Y1", "Унифицированная форма № КС- 2", 9, hAlign: ExcelHorizontalAlignment.Left);

        sheet.Cells["Y2:AG2"].Merge = true;
        SetText("Y2", "Утверждена постановлением Госкомстата России", 9, hAlign: ExcelHorizontalAlignment.Left);

        sheet.Cells["Y3:AG3"].Merge = true;
        SetText("Y3", "от 11.11.99 № 100", 9, hAlign: ExcelHorizontalAlignment.Left);

        // Code section
        sheet.Cells["AD4:AG4"].Merge = true;
        SetText("AD4", "Код", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD4:AG4", right: false);

        sheet.Cells["Y5:AC5"].Merge = true;
        SetText("Y5", "Форма по ОКУД", 10, hAlign: ExcelHorizontalAlignment.Right);

        sheet.Cells["AD5:AG5"].Merge = true;
        SetText("AD5", "0322005", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD5:AG5", right: false);

        // OKPO block row 6-7
        sheet.Cells["AD6:AG7"].Merge = true;
        SetBorder("AD6:AG7", right: false);

        // Investor section
        SetText("B7", "Инвестор", 10);

        SetText("AC7", "по ОКПО", 10, hAlign: ExcelHorizontalAlignment.Right);

        sheet.Cells["E7:AC7"].Merge = true;
        SetFont("E7:AC7", 10);
        SetBorder("E7:AC7", left: false, right: false, top: false, bottom: true);

        sheet.Cells["J8:AC8"].Merge = true;
        SetText("J8", "(организация, адрес, телефон, факс)", 8);

        sheet.Cells["AD8:AG9"].Merge = true;
        SetFont("AD8:AG9", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD8:AG9", right: false, bottom: false);

        // Customer (General Contractor) section
        SetText("B9", "Заказчик (Генподрядчик)", 10);

        sheet.Cells["E9:AA9"].Merge = true;
        SetFont("E9:AA9", 10);
        SetBorder("E9:AA9", left: false, right: false, top: false, bottom: true);

        SetText("AC9", "по ОКПО", 10, hAlign: ExcelHorizontalAlignment.Right);

        sheet.Cells["J10:AC10"].Merge = true;
        SetText("J10", "(организация, адрес, телефон, факс)", 8);

        sheet.Cells["AD10:AG11"].Merge = true;
        SetFont("AD10:AG11", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD10:AG11", right: false, bottom: false);

        // Contractor (Subcontractor) section
        SetText("B11", "Подрядчик (Субподрядчик)", 10);

        sheet.Cells["E11:AA11"].Merge = true;
        SetFont("E11:AA11", 10);
        SetBorder("E11:AA11", left: false, right: false, top: false, bottom: true);

        SetText("AC11", "по ОКПО", 10, hAlign: ExcelHorizontalAlignment.Right);

        sheet.Cells["J12:AC12"].Merge = true;
        SetText("J12", "(организация, адрес, телефон, факс)", 8);

        sheet.Cells["AD12:AG13"].Merge = true;
        SetFont("AD12:AG13", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD12:AG13", right: false, bottom: false);

        // Construction site
        SetText("B13", "Стройка", 10);

        sheet.Cells["E13:AC13"].Merge = true;
        SetFont("E13:AC13", 10);
        SetBorder("E13:AC13", left: false, right: false, top: false, bottom: true);

        sheet.Cells["J14:AC14"].Merge = true;
        SetText("J14", "(наименование, адрес)", 8);

        sheet.Cells["AD14:AG15"].Merge = true;
        SetFont("AD14:AG15", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD14:AG15", right: false, bottom: false);

        // Object
        SetText("B15", "Объект", 10);

        sheet.Cells["E15:V15"].Merge = true;
        SetFont("E15:V15", 10);
        SetBorder("E15:V15", left: false, right: false, top: false, bottom: true);

        sheet.Cells["J16:V16"].Merge = true;
        SetText("J16", "(наименование)", 8);

        // Activity type
        sheet.Cells["W16:AC16"].Merge = true;
        SetText("W16", "Вид деятельности по ОКДП", 10, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder("W16:AC16", left: false, right: false, top: true, bottom: false);

        sheet.Cells["AD16:AG17"].Merge = true;
        SetFont("AD16:AG17", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD16:AG17", right: false, bottom: false);

        // Contract section
        sheet.Cells["T18:AB18"].Merge = true;
        SetText("T18", "Договор подряда (контракт)", 10, hAlign: ExcelHorizontalAlignment.Right);

        SetText("AC18", "номер", 10, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder("AC18", left: true, right: true, top: true, bottom: true);

        sheet.Cells["AD18:AG18"].Merge = true;
        SetFont("AD18:AG18", 10, hAlign: ExcelHorizontalAlignment.Left);
        SetBorder("AD18:AG18", right: false);

        SetText("AC19", "дата", 10, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder("AC19", left: true, right: false, top: true, bottom: true);

        sheet.Cells["AD19:AE19"].Merge = true;
        SetFont("AD19:AE19", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD19:AE19", right: false);

        SetFont("AF19", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AF19");

        SetFont("AG19", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AG19", left: false);

        // Operation type
        sheet.Cells["Z20:AC20"].Merge = true;
        SetText("Z20", "Вид операции", 10, hAlign: ExcelHorizontalAlignment.Right);

        sheet.Cells["AD20:AG20"].Merge = true;
        SetBorder("AD20:AG20", right: false);

        // Document info
        sheet.Cells["N22:P22"].Merge = true;
        sheet.Cells["N22:P23"].Merge = true;
        SetText("N22", "Номер документа", 9, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("N22:P23");

        sheet.Cells["Q22:U22"].Merge = true;
        sheet.Cells["Q22:U23"].Merge = true;
        SetText("Q22", "Дата составления", 9, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("Q22:U23");

        // Report period
        sheet.Cells["W22:AD22"].Merge = true;
        SetText("W22", "Отчетный период", 9, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("W22:AD22");

        sheet.Cells["W23:Z23"].Merge = true;
        SetText("W23", "с", 9, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("W23:Z23");

        sheet.Cells["AA23:AD23"].Merge = true;
        SetText("AA23", "по", 9, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AA23:AD23");

        sheet.Cells["W24:Z24"].Merge = true;
        SetBorder("W24:Z24", style: ExcelBorderStyle.Medium);

        sheet.Cells["AA24:AD24"].Merge = true;
        SetBorder("AA24:AD24", style: ExcelBorderStyle.Medium);

        sheet.Cells["Q24:U24"].Merge = true;
        SetText("Q24", DateTime.Now.ToString("dd.MM.yyyy"), 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("Q24:U24");

        // ACT title
        SetText("L24", "АКТ", 11, bold: true, hAlign: ExcelHorizontalAlignment.Center);

        sheet.Cells["N24:P24"].Merge = true;
        SetFont("N24:P24", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("N24:P24");

        sheet.Cells["J25:S25"].Merge = true;
        SetText("J25", "О ПРИЕМКЕ ВЫПОЛНЕННЫХ РАБОТ", 11, bold: true, hAlign: ExcelHorizontalAlignment.Center);

        // Contract cost
        SetText("B27", "Сметная (договорная) стоимость в соответствии с договором подряда (субподряда)", 10);

        // Calculate total cost
        decimal totalCost = 0;
        foreach (var stage in stages)
        {
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var stageWorkTypesList = stageWorkTypes.Where(swt => swt.StageId == stage.Id).ToList();
            var stageTotal = stageWorkTypesList.Sum(swt => swt.Quantity * swt.PricePerUnit) * serviceK;
            totalCost += stageTotal;
        }

        sheet.Cells["K27:AD27"].Merge = true;
        sheet.Cells["K27"].Value = totalCost;
        sheet.Cells["K27"].Style.Numberformat.Format = "#,##0.00";
        SetFont("K27:AD27", 10, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder("K27:AD27", left: false, right: false, top: false, bottom: true);

        SetText("AE27", "руб.", 10);

        // Table header
        sheet.Cells["A29:E29"].Merge = true;
        SetText("A29", "Номер", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("A29:E29");

        sheet.Cells["F29:N30"].Merge = true;
        SetText("F29", "Наименование работ", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("F29:N30");

        sheet.Cells["O29:P30"].Merge = true;
        SetText("O29", "Номер единичной расценки", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("O29:P30", right: false, bottom: false);

        sheet.Cells["Q29:T30"].Merge = true;
        SetText("Q29", "Единица измерения", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("Q29:T30", right: false, bottom: false);

        sheet.Cells["U29:AG29"].Merge = true;
        SetText("U29", "Выполнено работ", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("U29:AG29");

        // Sub-headers
        sheet.Cells["A30:B30"].Merge = true;
        SetText("A30", "по порядку", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("A30:B30");

        sheet.Cells["C30:E30"].Merge = true;
        SetText("C30", "позиции по смете", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("C30:E30");

        sheet.Cells["U30:X30"].Merge = true;
        SetText("U30", "количество", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("U30:X30");

        sheet.Cells["Y30:AC30"].Merge = true;
        sheet.Cells["Y30"].Style.WrapText = true;
        SetText("Y30", "цена за единицу, руб.", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("Y30:AC30");

        sheet.Cells["AD30:AG30"].Merge = true;
        sheet.Cells["AD30"].Style.WrapText = true;
        SetText("AD30", "стоимость, руб.", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD30:AG30");

        // Column numbers
        sheet.Cells["A31:B31"].Merge = true;
        SetText("A31", "1", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("A31:B31");

        sheet.Cells["C31:E31"].Merge = true;
        SetText("C31", "2", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("C31:E31");

        sheet.Cells["F31:N31"].Merge = true;
        SetText("F31", "3", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("F31:N31");

        sheet.Cells["O31:P31"].Merge = true;
        SetText("O31", "4", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("O31:P31", right: false);

        sheet.Cells["Q31:T31"].Merge = true;
        SetText("Q31", "5", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("Q31:T31", right: false);

        sheet.Cells["U31:X31"].Merge = true;
        SetText("U31", "6", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("U31:X31");

        sheet.Cells["Y31:AC31"].Merge = true;
        SetText("Y31", "7", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("Y31:AC31");

        sheet.Cells["AD31:AG31"].Merge = true;
        SetText("AD31", "8", 10, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder("AD31:AG31");

        // Data rows
        int row = 32;
        int rowNum = 1;
        decimal grandTotal = 0;

        foreach (var stage in stages)
        {
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var stageWorkTypesList = stageWorkTypes.Where(swt => swt.StageId == stage.Id).ToList();

            foreach (var wt in stageWorkTypesList)
            {
                var template = workTypeTemplates.GetValueOrDefault(wt.WorkTypeTemplateId);
                var article = template.Article ?? "";
                var unit = template.Unit ?? "";
                var price = wt.PricePerUnit * serviceK;
                var cost = wt.Quantity * price;
                grandTotal += cost;

                sheet.Cells[$"A{row}:B{row}"].Merge = true;
                sheet.Cells[$"A{row}"].Value = rowNum;
                SetFont($"A{row}:B{row}", 10, hAlign: ExcelHorizontalAlignment.Center);
                SetBorder($"A{row}:B{row}");

                sheet.Cells[$"C{row}:E{row}"].Merge = true;
                sheet.Cells[$"C{row}"].Value = rowNum;
                SetFont($"C{row}:E{row}", 10, hAlign: ExcelHorizontalAlignment.Center);
                SetBorder($"C{row}:E{row}");

                sheet.Cells[$"F{row}:N{row}"].Merge = true;
                sheet.Cells[$"F{row}"].Value = wt.WorkTypeName;
                SetFont($"F{row}:N{row}", 10, hAlign: ExcelHorizontalAlignment.Left);
                SetBorder($"F{row}:N{row}");

                sheet.Cells[$"O{row}:P{row}"].Merge = true;
                sheet.Cells[$"O{row}"].Value = article;
                SetFont($"O{row}:P{row}", 10, hAlign: ExcelHorizontalAlignment.Center);
                SetBorder($"O{row}:P{row}");

                sheet.Cells[$"Q{row}:T{row}"].Merge = true;
                sheet.Cells[$"Q{row}"].Value = unit;
                SetFont($"Q{row}:T{row}", 10, hAlign: ExcelHorizontalAlignment.Center);
                SetBorder($"Q{row}:T{row}");

                sheet.Cells[$"U{row}:X{row}"].Merge = true;
                sheet.Cells[$"U{row}"].Value = wt.Quantity;
                sheet.Cells[$"U{row}"].Style.Numberformat.Format = "#,##0.00";
                SetFont($"U{row}:X{row}", 10, hAlign: ExcelHorizontalAlignment.Center);
                SetBorder($"U{row}:X{row}");

                sheet.Cells[$"Y{row}:AC{row}"].Merge = true;
                sheet.Cells[$"Y{row}"].Value = price;
                sheet.Cells[$"Y{row}"].Style.Numberformat.Format = "#,##0.00";
                SetFont($"Y{row}:AC{row}", 10, hAlign: ExcelHorizontalAlignment.Right);
                SetBorder($"Y{row}:AC{row}");

                sheet.Cells[$"AD{row}:AG{row}"].Merge = true;
                sheet.Cells[$"AD{row}"].Value = cost;
                sheet.Cells[$"AD{row}"].Style.Numberformat.Format = "#,##0.00";
                SetFont($"AD{row}:AG{row}", 10, hAlign: ExcelHorizontalAlignment.Right);
                SetBorder($"AD{row}:AG{row}");

                row++;
                rowNum++;
            }
        }

        // Total row
        SetText($"AC{row}", "Итого", 11, hAlign: ExcelHorizontalAlignment.Right);

        sheet.Cells[$"AD{row}:AG{row}"].Merge = true;
        sheet.Cells[$"AD{row}"].Value = grandTotal;
        sheet.Cells[$"AD{row}"].Style.Numberformat.Format = "#,##0.00";
        SetFont($"AD{row}:AG{row}", 11, bold: true, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder($"AD{row}:AG{row}", top: true, bottom: true, left: true, right: false, style: ExcelBorderStyle.Thin);

        row += 2;

        // VAT row
        SetText($"AC{row}", "В том числе НДС", 11, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder($"AC{row}", left: false, right: false, top: false, bottom: false);

        sheet.Cells[$"AD{row}:AG{row}"].Merge = true;
        sheet.Cells[$"AD{row}"].Value = "Без НДС";
        SetFont($"AD{row}:AG{row}", 11, bold: true, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"AD{row}:AG{row}");

        row++;

        // Total with VAT
        SetText($"AC{row}", "Всего с учётом НДС", 11, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder($"AC{row}", left: false, right: false, top: false, bottom: false);

        sheet.Cells[$"AD{row}:AG{row}"].Merge = true;
        sheet.Cells[$"AD{row}"].Value = grandTotal;
        sheet.Cells[$"AD{row}"].Style.Numberformat.Format = "#,##0.00";
        SetFont($"AD{row}:AG{row}", 11, bold: true, hAlign: ExcelHorizontalAlignment.Right);
        SetBorder($"AD{row}:AG{row}");

        row += 2;

        // Transferor (Сдал) - dynamic row
        var transferorRow = row;
        SetText($"C{transferorRow}", "Сдал", 11, bold: true);

        sheet.Cells[$"D{transferorRow}:E{transferorRow}"].Merge = true;
        SetFont($"D{transferorRow}:E{transferorRow}", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"D{transferorRow}:E{transferorRow}", bottom: true, top: false, left: false, right: false);

        sheet.Cells[$"G{transferorRow}:H{transferorRow}"].Merge = true;
        SetFont($"G{transferorRow}:H{transferorRow}", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"G{transferorRow}:H{transferorRow}", bottom: true, top: false, left: false, right: false);

        sheet.Cells[$"J{transferorRow}:AG{transferorRow}"].Merge = true;
        SetFont($"J{transferorRow}:AG{transferorRow}", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"J{transferorRow}:AG{transferorRow}", bottom: true, top: false, left: false, right: false);

        sheet.Cells[$"D{transferorRow + 1}:E{transferorRow + 1}"].Merge = true;
        SetText($"D{transferorRow + 1}", "(должность)", 8, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"D{transferorRow + 1}:E{transferorRow + 1}", top: true, bottom: false, left: false, right: false);

        sheet.Cells[$"G{transferorRow + 1}:H{transferorRow + 1}"].Merge = true;
        SetText($"G{transferorRow + 1}", "(подпись)", 8, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"G{transferorRow + 1}:H{transferorRow + 1}", top: true, bottom: false, left: false, right: false);

        sheet.Cells[$"J{transferorRow + 1}:AG{transferorRow + 1}"].Merge = true;
        SetText($"J{transferorRow + 1}", "(расшифровка подписи)", 8, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"J{transferorRow + 1}:AG{transferorRow + 1}", top: true, bottom: false, left: false, right: false);

        SetText($"D{transferorRow + 3}", "М.П.", 11, hAlign: ExcelHorizontalAlignment.Center);

        // Acceptor (Принял) - dynamic row
        var acceptorRow = transferorRow + 5;
        SetText($"C{acceptorRow}", "Принял", 11, bold: true);

        sheet.Cells[$"D{acceptorRow}:E{acceptorRow}"].Merge = true;
        SetFont($"D{acceptorRow}:E{acceptorRow}", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"D{acceptorRow}:E{acceptorRow}", bottom: true, top: false, left: false, right: false);

        sheet.Cells[$"G{acceptorRow}:H{acceptorRow}"].Merge = true;
        SetFont($"G{acceptorRow}:H{acceptorRow}", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"G{acceptorRow}:H{acceptorRow}", bottom: true, top: false, left: false, right: false);

        sheet.Cells[$"J{acceptorRow}:AG{acceptorRow}"].Merge = true;
        SetFont($"J{acceptorRow}:AG{acceptorRow}", 11, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"J{acceptorRow}:AG{acceptorRow}", bottom: true, top: false, left: false, right: false);

        sheet.Cells[$"D{acceptorRow + 1}:E{acceptorRow + 1}"].Merge = true;
        SetText($"D{acceptorRow + 1}", "(должность)", 8, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"D{acceptorRow + 1}:E{acceptorRow + 1}", top: true, bottom: false, left: false, right: false);

        sheet.Cells[$"G{acceptorRow + 1}:H{acceptorRow + 1}"].Merge = true;
        SetText($"G{acceptorRow + 1}", "(подпись)", 8, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"G{acceptorRow + 1}:H{acceptorRow + 1}", top: true, bottom: false, left: false, right: false);

        sheet.Cells[$"J{acceptorRow + 1}:AG{acceptorRow + 1}"].Merge = true;
        SetText($"J{acceptorRow + 1}", "(расшифровка подписи)", 8, hAlign: ExcelHorizontalAlignment.Center);
        SetBorder($"J{acceptorRow + 1}:AG{acceptorRow + 1}", top: true, bottom: false, left: false, right: false);

        SetText($"D{acceptorRow + 3}", "М.П.", 11, hAlign: ExcelHorizontalAlignment.Center);

        // Thick outer border around the entire OKPO code block (AD4:AG20) — perimeter only
        SetBorder("AD4:AG4", style: ExcelBorderStyle.Medium, left: false, right: false, bottom: false, top: true);
        SetBorder("AD20:AG20", style: ExcelBorderStyle.Medium, left: false, right: false, top: false, bottom: true);
        SetBorder("AD4:AD20", style: ExcelBorderStyle.Medium, top: false, right: false, bottom: false, left: true);
        SetBorder("AG4:AG20", style: ExcelBorderStyle.Medium, top: false, left: false, bottom: false, right: true);
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
            Description = $"Отчёт КС-2 по проекту: {projectName}"
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

        await LogActivityAsync(db, $"Создан отчёт КС-2 «{newFile.FileName}»", "Document", newFile.Id);

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

    private string GenerateFileName(string projectName)
    {
        var dateStr = DateTime.Now.ToString("dd.MM.yyyy");
        var safeProjectName = string.Join("_", projectName.Split(Path.GetInvalidFileNameChars()));
        var baseName = $"Отчёт КС-2_{safeProjectName}_{dateStr}";
        
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
