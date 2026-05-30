using MPMS.Models;

namespace MPMS.ViewModels;

public sealed class StageFilterOption(Guid? id, string name)
{
    public Guid? Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class ProjectSummaryStageRowVm
{
    public Guid StageId { get; init; }
    public Guid TaskId { get; init; }
    public string StageName { get; init; } = "";
    public StageStatus Status { get; init; }
    public decimal ServicesSubtotal { get; init; }
    public decimal MaterialsSubtotal { get; init; }
    public decimal AdjustedServicesTotal { get; init; }
    public decimal AdjustedMaterialsTotal { get; init; }
    public decimal AdjustedGrandTotal { get; init; }
    public decimal ServiceAdjustmentPercent { get; init; }
    public decimal MaterialAdjustmentPercent { get; init; }
    public int ServicesCount { get; init; }
    public int MaterialsCount { get; init; }

    public bool HasServiceAdjustment => ServiceAdjustmentPercent != 0m;
    public bool HasMaterialAdjustment => MaterialAdjustmentPercent != 0m;
    public bool HasPricing => ServicesCount > 0 || MaterialsCount > 0;
    public string ServiceAdjustmentLabel => ProjectPricingSummaryBuilder.FormatAdjustmentLabel(ServiceAdjustmentPercent);
    public string MaterialAdjustmentLabel => ProjectPricingSummaryBuilder.FormatAdjustmentLabel(MaterialAdjustmentPercent);
}

public sealed class ProjectSummaryTaskGroupVm
{
    public Guid TaskId { get; init; }
    public string TaskName { get; init; } = "";
    public IReadOnlyList<ProjectSummaryStageRowVm> Stages { get; init; } = [];
    public decimal ServicesSubtotal => Stages.Sum(s => s.ServicesSubtotal);
    public decimal MaterialsSubtotal => Stages.Sum(s => s.MaterialsSubtotal);
    public decimal AdjustedServicesTotal => Stages.Sum(s => s.AdjustedServicesTotal);
    public decimal AdjustedMaterialsTotal => Stages.Sum(s => s.AdjustedMaterialsTotal);
    public decimal AdjustedGrandTotal => Stages.Sum(s => s.AdjustedGrandTotal);
    public int StagesCount => Stages.Count;
    public int StagesWithPricingCount => Stages.Count(s => s.HasPricing);
}

public sealed class ProjectSummaryCatalogLineVm
{
    public Guid ItemId { get; init; }
    public string Name { get; init; } = "";
    public string? Unit { get; init; }
    public decimal TotalQuantity { get; init; }
    public decimal Subtotal { get; init; }
    public decimal AdjustedTotal { get; init; }
    public int StageUsageCount { get; init; }
    public string UsageSummary => $"{TotalQuantity:N2} · в {StageUsageCount} этапах";
}

public sealed class ProjectSummaryReceiptStageSectionVm
{
    public Guid StageId { get; init; }
    public string StageName { get; init; } = "";
    public bool ShowStageHeader { get; init; }
    public bool ShowStageDivider { get; init; }
    public IReadOnlyList<ReceiptRowVm> ServiceRows { get; init; } = [];
    public IReadOnlyList<ReceiptRowVm> MaterialRows { get; init; } = [];
}

public sealed class ProjectSummaryReceiptResult
{
    public IReadOnlyList<ProjectSummaryReceiptStageSectionVm> ServiceSections { get; init; } = [];
    public IReadOnlyList<ProjectSummaryReceiptStageSectionVm> MaterialSections { get; init; } = [];
    public decimal ServicesSubtotal { get; init; }
    public decimal MaterialsSubtotal { get; init; }
    public decimal AdjustedServicesTotal { get; init; }
    public decimal AdjustedMaterialsTotal { get; init; }
    public decimal GrandTotal => AdjustedServicesTotal + AdjustedMaterialsTotal;
    public int FilteredStageCount { get; init; }
    public bool HasServiceAdjustment => ServicesSubtotal != AdjustedServicesTotal;
    public bool HasMaterialAdjustment => MaterialsSubtotal != AdjustedMaterialsTotal;
}

public static class ProjectPricingSummaryBuilder
{
    public static (
        IReadOnlyList<ProjectSummaryTaskGroupVm> TaskGroups,
        IReadOnlyList<ProjectSummaryCatalogLineVm> ServiceLines,
        IReadOnlyList<ProjectSummaryCatalogLineVm> MaterialLines,
        decimal ServicesSubtotal,
        decimal MaterialsSubtotal,
        decimal AdjustedServicesTotal,
        decimal AdjustedMaterialsTotal,
        int StagesWithPricingCount)
        Build(
            IReadOnlyList<LocalTask> tasks,
            IReadOnlyList<LocalTaskStage> stages,
            IReadOnlyList<LocalStageWorkType> workTypes,
            IReadOnlyList<LocalStageMaterial> materials)
    {
        var activeStages = stages.Where(s => !s.EffectiveMarkedForDeletion).ToList();
        var workTypesByStage = workTypes.GroupBy(w => w.StageId).ToDictionary(g => g.Key, g => g.ToList());
        var materialsByStage = materials.GroupBy(m => m.StageId).ToDictionary(g => g.Key, g => g.ToList());

        var stageRows = new List<ProjectSummaryStageRowVm>();
        foreach (var stage in activeStages)
        {
            var svcs = workTypesByStage.GetValueOrDefault(stage.Id) ?? [];
            var mats = materialsByStage.GetValueOrDefault(stage.Id) ?? [];
            var stageServicesSubtotal = svcs.Sum(w => w.Quantity * w.PricePerUnit);
            var stageMaterialsSubtotal = mats.Sum(m => m.Quantity * m.PricePerUnit);
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;

            stageRows.Add(new ProjectSummaryStageRowVm
            {
                StageId = stage.Id,
                TaskId = stage.TaskId,
                StageName = stage.Name,
                Status = stage.Status,
                ServicesSubtotal = stageServicesSubtotal,
                MaterialsSubtotal = stageMaterialsSubtotal,
                AdjustedServicesTotal = stageServicesSubtotal * serviceK,
                AdjustedMaterialsTotal = stageMaterialsSubtotal * materialK,
                AdjustedGrandTotal = stageServicesSubtotal * serviceK + stageMaterialsSubtotal * materialK,
                ServiceAdjustmentPercent = stage.ServicesAdjustmentPercent,
                MaterialAdjustmentPercent = stage.MaterialsAdjustmentPercent,
                ServicesCount = svcs.Count,
                MaterialsCount = mats.Count
            });
        }

        var taskGroups = tasks
            .OrderBy(t => t.Name)
            .Select(task => new ProjectSummaryTaskGroupVm
            {
                TaskId = task.Id,
                TaskName = task.Name,
                Stages = stageRows
                    .Where(s => s.TaskId == task.Id)
                    .OrderBy(s => s.StageName)
                    .ToList()
            })
            .Where(g => g.StagesCount > 0)
            .ToList();

        var serviceAgg = new Dictionary<Guid, (string Name, string? Unit, decimal Qty, decimal Sub, decimal Adj, HashSet<Guid> Stages)>();
        foreach (var stage in activeStages)
        {
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            foreach (var wt in workTypesByStage.GetValueOrDefault(stage.Id) ?? [])
            {
                var lineTotal = wt.Quantity * wt.PricePerUnit;
                if (!serviceAgg.TryGetValue(wt.WorkTypeTemplateId, out var bucket))
                {
                    bucket = (wt.WorkTypeName, wt.Unit, 0m, 0m, 0m, []);
                    serviceAgg[wt.WorkTypeTemplateId] = bucket;
                }

                bucket.Stages.Add(stage.Id);
                serviceAgg[wt.WorkTypeTemplateId] = (
                    bucket.Name,
                    bucket.Unit,
                    bucket.Qty + wt.Quantity,
                    bucket.Sub + lineTotal,
                    bucket.Adj + lineTotal * serviceK,
                    bucket.Stages);
            }
        }

        var materialAgg = new Dictionary<Guid, (string Name, string? Unit, decimal Qty, decimal Sub, decimal Adj, HashSet<Guid> Stages)>();
        foreach (var stage in activeStages)
        {
            var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;
            foreach (var mat in materialsByStage.GetValueOrDefault(stage.Id) ?? [])
            {
                var lineTotal = mat.Quantity * mat.PricePerUnit;
                if (!materialAgg.TryGetValue(mat.MaterialId, out var bucket))
                {
                    bucket = (mat.MaterialName, mat.Unit, 0m, 0m, 0m, []);
                    materialAgg[mat.MaterialId] = bucket;
                }

                bucket.Stages.Add(stage.Id);
                materialAgg[mat.MaterialId] = (
                    bucket.Name,
                    bucket.Unit,
                    bucket.Qty + mat.Quantity,
                    bucket.Sub + lineTotal,
                    bucket.Adj + lineTotal * materialK,
                    bucket.Stages);
            }
        }

        var serviceLines = serviceAgg
            .Select(p => new ProjectSummaryCatalogLineVm
            {
                ItemId = p.Key,
                Name = p.Value.Name,
                Unit = p.Value.Unit,
                TotalQuantity = p.Value.Qty,
                Subtotal = p.Value.Sub,
                AdjustedTotal = p.Value.Adj,
                StageUsageCount = p.Value.Stages.Count
            })
            .OrderBy(l => l.Name)
            .ToList();

        var materialLines = materialAgg
            .Select(p => new ProjectSummaryCatalogLineVm
            {
                ItemId = p.Key,
                Name = p.Value.Name,
                Unit = p.Value.Unit,
                TotalQuantity = p.Value.Qty,
                Subtotal = p.Value.Sub,
                AdjustedTotal = p.Value.Adj,
                StageUsageCount = p.Value.Stages.Count
            })
            .OrderBy(l => l.Name)
            .ToList();

        var servicesSubtotal = stageRows.Sum(s => s.ServicesSubtotal);
        var materialsSubtotal = stageRows.Sum(s => s.MaterialsSubtotal);
        var adjustedServicesTotal = stageRows.Sum(s => s.AdjustedServicesTotal);
        var adjustedMaterialsTotal = stageRows.Sum(s => s.AdjustedMaterialsTotal);
        var stagesWithPricing = stageRows.Count(s => s.HasPricing);

        return (
            taskGroups,
            serviceLines,
            materialLines,
            servicesSubtotal,
            materialsSubtotal,
            adjustedServicesTotal,
            adjustedMaterialsTotal,
            stagesWithPricing);
    }

    public static ProjectSummaryReceiptResult BuildReceiptRows(
        IReadOnlyList<LocalTaskStage> stages,
        IReadOnlyList<LocalStageWorkType> workTypes,
        IReadOnlyList<LocalStageMaterial> materials,
        Guid? taskFilter,
        Guid? stageFilter,
        bool groupServicesByStage,
        bool groupMaterialsByStage)
    {
        var filteredStages = stages
            .Where(s => !s.EffectiveMarkedForDeletion)
            .Where(s => !taskFilter.HasValue || s.TaskId == taskFilter.Value)
            .Where(s => !stageFilter.HasValue || s.Id == stageFilter.Value)
            .ToList();

        if (filteredStages.Count == 0)
            return new ProjectSummaryReceiptResult();

        var stageIds = filteredStages.Select(s => s.Id).ToHashSet();
        var workTypesByStage = workTypes
            .Where(w => stageIds.Contains(w.StageId))
            .GroupBy(w => w.StageId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var materialsByStage = materials
            .Where(m => stageIds.Contains(m.StageId))
            .GroupBy(m => m.StageId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var serviceSections = BuildServiceSections(
            filteredStages, workTypesByStage, groupServicesByStage);
        var materialSections = BuildMaterialSections(
            filteredStages, materialsByStage, groupMaterialsByStage);

        var serviceRows = serviceSections.SelectMany(s => s.ServiceRows).ToList();
        var materialRows = materialSections.SelectMany(s => s.MaterialRows).ToList();

        return new ProjectSummaryReceiptResult
        {
            ServiceSections = serviceSections,
            MaterialSections = materialSections,
            ServicesSubtotal = serviceRows.Sum(r => r.BaseTotal),
            MaterialsSubtotal = materialRows.Sum(r => r.BaseTotal),
            AdjustedServicesTotal = serviceRows.Sum(r => r.AdjustedTotal),
            AdjustedMaterialsTotal = materialRows.Sum(r => r.AdjustedTotal),
            FilteredStageCount = filteredStages.Count
        };
    }

    private static List<ProjectSummaryReceiptStageSectionVm> BuildServiceSections(
        IReadOnlyList<LocalTaskStage> filteredStages,
        IReadOnlyDictionary<Guid, List<LocalStageWorkType>> workTypesByStage,
        bool groupByStage)
    {
        var stagesWithServices = filteredStages
            .Where(s => (workTypesByStage.GetValueOrDefault(s.Id)?.Count ?? 0) > 0)
            .ToList();

        if (groupByStage || stagesWithServices.Count == 1)
        {
            var showHeaders = groupByStage && stagesWithServices.Count > 1;
            return stagesWithServices.OrderBy(s => s.Name).Select((stage, index) =>
            {
                var receipt = BuildSingleStageReceipt(
                    stage,
                    workTypesByStage.GetValueOrDefault(stage.Id) ?? [],
                    []);
                return new ProjectSummaryReceiptStageSectionVm
                {
                    StageId = stage.Id,
                    StageName = stage.Name,
                    ShowStageHeader = showHeaders,
                    ShowStageDivider = showHeaders && index > 0,
                    ServiceRows = receipt.ServiceRows
                };
            }).ToList();
        }

        return [BuildMergedServiceSection(stagesWithServices, workTypesByStage)];
    }

    private static List<ProjectSummaryReceiptStageSectionVm> BuildMaterialSections(
        IReadOnlyList<LocalTaskStage> filteredStages,
        IReadOnlyDictionary<Guid, List<LocalStageMaterial>> materialsByStage,
        bool groupByStage)
    {
        var stagesWithMaterials = filteredStages
            .Where(s => (materialsByStage.GetValueOrDefault(s.Id)?.Count ?? 0) > 0)
            .ToList();

        if (groupByStage || stagesWithMaterials.Count == 1)
        {
            var showHeaders = groupByStage && stagesWithMaterials.Count > 1;
            return stagesWithMaterials.OrderBy(s => s.Name).Select((stage, index) =>
            {
                var receipt = BuildSingleStageReceipt(
                    stage,
                    [],
                    materialsByStage.GetValueOrDefault(stage.Id) ?? []);
                return new ProjectSummaryReceiptStageSectionVm
                {
                    StageId = stage.Id,
                    StageName = stage.Name,
                    ShowStageHeader = showHeaders,
                    ShowStageDivider = showHeaders && index > 0,
                    MaterialRows = receipt.MaterialRows
                };
            }).ToList();
        }

        return [BuildMergedMaterialSection(stagesWithMaterials, materialsByStage)];
    }

    private static ProjectSummaryReceiptStageSectionVm BuildMergedServiceSection(
        IReadOnlyList<LocalTaskStage> filteredStages,
        IReadOnlyDictionary<Guid, List<LocalStageWorkType>> workTypesByStage)
    {
        var stageById = filteredStages.ToDictionary(s => s.Id);
        var serviceAgg = new Dictionary<Guid, (string Name, decimal Qty, decimal Sub, decimal Adj)>();

        foreach (var stage in filteredStages)
        {
            var k = 1m + stage.ServicesAdjustmentPercent / 100m;
            foreach (var wt in workTypesByStage.GetValueOrDefault(stage.Id) ?? [])
            {
                var lineTotal = wt.Quantity * wt.PricePerUnit;
                if (!serviceAgg.TryGetValue(wt.WorkTypeTemplateId, out var bucket))
                    bucket = (wt.WorkTypeName, 0m, 0m, 0m);
                serviceAgg[wt.WorkTypeTemplateId] = (
                    bucket.Name,
                    bucket.Qty + wt.Quantity,
                    bucket.Sub + lineTotal,
                    bucket.Adj + lineTotal * k);
            }
        }

        var serviceRows = serviceAgg
            .OrderBy(p => p.Value.Name)
            .Select(p => ReceiptRowVm.ForAggregated(
                p.Value.Name, p.Value.Qty, p.Value.Sub, p.Value.Adj, p.Key, isServiceLine: true))
            .ToList();

        return new ProjectSummaryReceiptStageSectionVm { ServiceRows = serviceRows };
    }

    private static ProjectSummaryReceiptStageSectionVm BuildMergedMaterialSection(
        IReadOnlyList<LocalTaskStage> filteredStages,
        IReadOnlyDictionary<Guid, List<LocalStageMaterial>> materialsByStage)
    {
        var materialAgg = new Dictionary<Guid, (string Name, decimal Qty, decimal Sub, decimal Adj)>();

        foreach (var stage in filteredStages)
        {
            var k = 1m + stage.MaterialsAdjustmentPercent / 100m;
            foreach (var mat in materialsByStage.GetValueOrDefault(stage.Id) ?? [])
            {
                var lineTotal = mat.Quantity * mat.PricePerUnit;
                if (!materialAgg.TryGetValue(mat.MaterialId, out var bucket))
                    bucket = (mat.MaterialName, 0m, 0m, 0m);
                materialAgg[mat.MaterialId] = (
                    bucket.Name,
                    bucket.Qty + mat.Quantity,
                    bucket.Sub + lineTotal,
                    bucket.Adj + lineTotal * k);
            }
        }

        var materialRows = materialAgg
            .OrderBy(p => p.Value.Name)
            .Select(p => ReceiptRowVm.ForAggregated(
                p.Value.Name, p.Value.Qty, p.Value.Sub, p.Value.Adj, p.Key, isServiceLine: false))
            .ToList();

        return new ProjectSummaryReceiptStageSectionVm { MaterialRows = materialRows };
    }

    private static (IReadOnlyList<ReceiptRowVm> ServiceRows, IReadOnlyList<ReceiptRowVm> MaterialRows,
        decimal ServicesSubtotal, decimal MaterialsSubtotal, decimal AdjustedServicesTotal, decimal AdjustedMaterialsTotal)
        BuildSingleStageReceipt(
        LocalTaskStage stage,
        IReadOnlyList<LocalStageWorkType> workTypes,
        IReadOnlyList<LocalStageMaterial> materials)
    {
        var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
        var materialK = 1m + stage.MaterialsAdjustmentPercent / 100m;

        var serviceRows = workTypes
            .OrderBy(w => w.WorkTypeName)
            .Select(w =>
            {
                var basePrice = w.BasePricePerUnit > 0m ? w.BasePricePerUnit : w.PricePerUnit;
                var line = new StageWorkTypeLineVm(w.WorkTypeTemplateId, w.WorkTypeName, w.Unit, basePrice)
                {
                    Quantity = w.Quantity,
                    PricePerUnit = w.PricePerUnit,
                    LineAdjustmentPercent = w.LineAdjustmentPercent
                };
                return ReceiptRowVm.ForService(line, stage.ServicesAdjustmentPercent, serviceK, stage.Id);
            })
            .ToList();

        var materialRows = materials
            .OrderBy(m => m.MaterialName)
            .Select(m =>
            {
                var basePrice = m.BasePricePerUnit > 0m ? m.BasePricePerUnit : m.PricePerUnit;
                var line = new StageMaterialLineVm(m.MaterialId, m.MaterialName, m.Unit, basePrice)
                {
                    Quantity = m.Quantity,
                    PricePerUnit = m.PricePerUnit,
                    LineAdjustmentPercent = m.LineAdjustmentPercent
                };
                return ReceiptRowVm.ForMaterial(line, stage.MaterialsAdjustmentPercent, materialK, stage.Id);
            })
            .ToList();

        var servicesSubtotal = serviceRows.Sum(r => r.BaseTotal);
        var materialsSubtotal = materialRows.Sum(r => r.BaseTotal);
        return (
            serviceRows,
            materialRows,
            servicesSubtotal,
            materialsSubtotal,
            serviceRows.Sum(r => r.AdjustedTotal),
            materialRows.Sum(r => r.AdjustedTotal));
    }

    public static string FormatAdjustmentLabel(decimal percent) =>
        percent > 0m ? $"Наценка +{percent:N0}%"
        : percent < 0m ? $"Скидка {percent:N0}%"
        : "Без наценки и скидки";
}
