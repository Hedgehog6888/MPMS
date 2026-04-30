using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Components
{
    public partial class MainTopBar : UserControl
    {
        private CancellationTokenSource _searchCts = new();

        public MainTopBar()
        {
            InitializeComponent();
            
            this.Loaded += (s, e) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.PropertyChanged += OnMainViewModelPropertyChanged;
                    // Initial avatar apply
                    ApplyTopBarAvatar(vm.UserAvatarData, vm.UserAvatarPath, vm.UserName);
                }
            };
        }

        private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MainViewModel vm) return;
            if (e.PropertyName is nameof(MainViewModel.UserAvatarPath) or nameof(MainViewModel.UserAvatarData))
                ApplyTopBarAvatar(vm.UserAvatarData, vm.UserAvatarPath, vm.UserName);
        }

        private void ApplyTopBarAvatar(byte[]? avatarData, string? avatarPath, string? displayName = null)
        {
            var bmp = AvatarHelper.GetImageSource(avatarData, avatarPath, displayName);
            if (bmp is not null)
            {
                TopBarAvatarImage.Source = bmp;
                TopBarAvatarBorder.Visibility = Visibility.Visible;
                return;
            }
            TopBarAvatarImage.Source = null;
            TopBarAvatarBorder.Visibility = Visibility.Collapsed;
        }

        private void UserPanel_Click(object sender, RoutedEventArgs e)
        {
            if (UserContextMenu is not null)
            {
                UserContextMenu.PlacementTarget = UserPanelBorder;
                UserContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                UserContextMenu.IsOpen = true;
            }
        }

        private void SyncStatus_Click(object sender, RoutedEventArgs e)
        {
            SyncPopup.IsOpen = !SyncPopup.IsOpen;
        }

        private void SyncNow_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.SyncNowCommand.Execute(null);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.NavigateCommand.Execute("Settings");
        }

        private void MyProfile_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.NavigateCommand.Execute("Profile");
        }

        private static readonly SolidColorBrush _searchFocusBrush = new(Colors.Black);
        private static readonly SolidColorBrush _searchNormalBrush = new(Colors.Transparent);
        private static readonly SolidColorBrush _searchFocusBg = new(Color.FromRgb(0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush _searchNormalBg = new(Color.FromRgb(0xF4, 0xF5, 0xF7));

        private void GlobalSearch_GotFocus(object sender, RoutedEventArgs e)
        {
            SearchBorder.BorderBrush = _searchFocusBrush;
            SearchBorder.Background = _searchFocusBg;
        }

        private void GlobalSearch_LostFocus(object sender, RoutedEventArgs e)
        {
            SearchBorder.BorderBrush = _searchNormalBrush;
            SearchBorder.Background = _searchNormalBg;
        }

        private void GlobalSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = GlobalSearchBox?.Text ?? "";
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;

            if (string.IsNullOrWhiteSpace(text))
            {
                SearchResultsPopup.IsOpen = false;
                return;
            }
            _ = RunSearchAsync(text);
        }

        private async Task RunSearchAsync(string query)
        {
            _searchCts.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                await Task.Delay(200, ct); // debounce

                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync(ct);

                var searchTerm = SearchHelper.Normalize(query);
                var projects = searchTerm is null
                    ? new List<LocalProject>()
                    : (await db.Projects.ToListAsync(ct))
                        .Where(p => SearchHelper.ContainsIgnoreCase(p.Name, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(p.Client, searchTerm))
                        .Take(5).ToList();

                var tasks = searchTerm is null
                    ? new List<LocalTask>()
                    : (await db.Tasks.ToListAsync(ct))
                        .Where(t => SearchHelper.ContainsIgnoreCase(t.Name, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(t.Description, searchTerm))
                        .Take(5).ToList();

                var stages = searchTerm is null
                    ? new List<LocalTaskStage>()
                    : (await db.TaskStages.ToListAsync(ct))
                        .Where(s => SearchHelper.ContainsIgnoreCase(s.Name, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(s.Description, searchTerm))
                        .Take(5).ToList();

                var materials = searchTerm is null
                    ? new List<LocalMaterial>()
                    : (await db.Materials.ToListAsync(ct))
                        .Where(m => SearchHelper.ContainsIgnoreCase(m.Name, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(m.Description, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(m.CategoryName, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(m.InventoryNumber, searchTerm))
                        .Take(5).ToList();

                var equipment = searchTerm is null
                    ? new List<LocalEquipment>()
                    : (await db.Equipments.ToListAsync(ct))
                        .Where(eq => SearchHelper.ContainsIgnoreCase(eq.Name, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(eq.Description, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(eq.CategoryName, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(eq.InventoryNumber, searchTerm))
                        .Take(5).ToList();

                var files = searchTerm is null
                    ? new List<LocalFile>()
                    : (await db.Files.ToListAsync(ct))
                        .Where(f => SearchHelper.ContainsIgnoreCase(f.FileName, searchTerm) ||
                            SearchHelper.ContainsIgnoreCase(f.UploadedByName, searchTerm))
                        .Take(5).ToList();

                // Populate TaskName for stages
                var taskIds = stages.Select(s => s.TaskId).Distinct().ToList();
                var taskNames = await db.Tasks.Where(t => taskIds.Contains(t.Id))
                    .ToDictionaryAsync(t => t.Id, t => t.Name, ct);
                foreach (var s in stages)
                    s.TaskName = taskNames.GetValueOrDefault(s.TaskId, "—");

                // Populate ProjectName and StageName for files
                var fileProjectIds = files.Where(f => f.ProjectId.HasValue).Select(f => f.ProjectId.Value).Distinct().ToList();
                var fileStageIds = files.Where(f => f.StageId.HasValue).Select(f => f.StageId.Value).Distinct().ToList();
                var fileProjectNames = await db.Projects.Where(p => fileProjectIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Name, ct);
                var fileStageNames = await db.TaskStages.Where(s => fileStageIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
                foreach (var f in files)
                {
                    if (f.ProjectId.HasValue) f.ProjectName = fileProjectNames.GetValueOrDefault(f.ProjectId.Value);
                    if (f.StageId.HasValue) f.StageName = fileStageNames.GetValueOrDefault(f.StageId.Value);
                }

                ct.ThrowIfCancellationRequested();

                await Dispatcher.InvokeAsync(() =>
                {
                    bool hasProjects = projects.Count > 0;
                    bool hasTasks = tasks.Count > 0;
                    bool hasStages = stages.Count > 0;
                    bool hasMaterials = materials.Count > 0;
                    bool hasEquipment = equipment.Count > 0;
                    bool hasFiles = files.Count > 0;
                    bool hasAny = hasProjects || hasTasks || hasStages || hasMaterials || hasEquipment || hasFiles;

                    SearchProjectsSection.Visibility = hasProjects ? Visibility.Visible : Visibility.Collapsed;
                    SearchProjectsDivider.Visibility = hasProjects && (hasTasks || hasStages || hasMaterials || hasEquipment || hasFiles) ? Visibility.Visible : Visibility.Collapsed;
                    SearchTasksSection.Visibility = hasTasks ? Visibility.Visible : Visibility.Collapsed;
                    SearchTasksDivider.Visibility = hasTasks && (hasStages || hasMaterials || hasEquipment || hasFiles) ? Visibility.Visible : Visibility.Collapsed;
                    SearchStagesSection.Visibility = hasStages ? Visibility.Visible : Visibility.Collapsed;
                    SearchStagesDivider.Visibility = hasStages && (hasMaterials || hasEquipment || hasFiles) ? Visibility.Visible : Visibility.Collapsed;
                    SearchMaterialsSection.Visibility = hasMaterials ? Visibility.Visible : Visibility.Collapsed;
                    SearchMaterialsDivider.Visibility = hasMaterials && (hasEquipment || hasFiles) ? Visibility.Visible : Visibility.Collapsed;
                    SearchEquipmentSection.Visibility = hasEquipment ? Visibility.Visible : Visibility.Collapsed;
                    SearchEquipmentDivider.Visibility = hasEquipment && hasFiles ? Visibility.Visible : Visibility.Collapsed;
                    SearchFilesSection.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
                    NoSearchResultsText.Visibility = hasAny ? Visibility.Collapsed : Visibility.Visible;

                    SearchProjectsList.ItemsSource = projects;
                    SearchTasksList.ItemsSource = tasks;
                    SearchStagesList.ItemsSource = stages;
                    SearchMaterialsList.ItemsSource = materials;
                    SearchEquipmentList.ItemsSource = equipment;
                    SearchFilesList.ItemsSource = files;

                    SearchResultsPopup.IsOpen = true;
                });
            }
            catch (OperationCanceledException) { }
        }

        private async void SearchResult_Click(object sender, MouseButtonEventArgs e)
        {
            SearchResultsPopup.IsOpen = false;
            if (DataContext is not MainViewModel vm) return;

            if (sender is FrameworkElement fe)
            {
                if (fe.Tag is LocalProject project)
                {
                    vm.NavigateToProject(project);
                }
                else if (fe.Tag is LocalTask task)
                {
                    await OpenTaskFromSearchAsync(task);
                }
                else if (fe.Tag is LocalTaskStage stage)
                {
                    await OpenStageFromSearchAsync(stage);
                }
                else if (fe.Tag is LocalMaterial material)
                {
                    await OpenMaterialFromSearchAsync(material);
                }
                else if (fe.Tag is LocalEquipment equipment)
                {
                    await OpenEquipmentFromSearchAsync(equipment);
                }
                else if (fe.Tag is LocalFile file)
                {
                    await OpenFileFromSearchAsync(file);
                }
            }

            GlobalSearchBox.Text = "";
            vm.SearchText = string.Empty;
        }

        private async Task OpenTaskFromSearchAsync(LocalTask task)
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var taskEntity = await db.Tasks.FindAsync(task.Id);
            if (taskEntity is null) return;

            var projectRow = await db.Projects
                .Where(p => p.Id == taskEntity.ProjectId)
                .Select(p => new { p.Name, p.IsMarkedForDeletion })
                .FirstOrDefaultAsync();
            taskEntity.ProjectName = projectRow?.Name ?? taskEntity.ProjectName;
            taskEntity.ProjectIsMarkedForDeletion = projectRow?.IsMarkedForDeletion ?? false;

            var stages = await db.TaskStages
                .Where(s => s.TaskId == taskEntity.Id && !s.IsArchived)
                .ToListAsync();
            foreach (var s in stages)
            {
                s.TaskIsMarkedForDeletion = taskEntity.IsMarkedForDeletion;
                s.ProjectIsMarkedForDeletion = taskEntity.ProjectIsMarkedForDeletion;
            }
            ProgressCalculator.ApplyTaskMetrics(taskEntity, stages);

            var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
            var project = await tasksVm.GetProjectForTaskAsync(taskEntity.ProjectId);

            UIElement? leftPanel = null;
            if (project is not null)
            {
                var projectPanel = new ProjectSummaryPanel();
                projectPanel.SetProject(project);
                leftPanel = projectPanel;
            }

            var overlay = new TaskDetailOverlay();
            overlay.SetTask(taskEntity);
            MainWindow.Instance?.ShowDrawer(leftPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
        }

        private async Task OpenStageFromSearchAsync(LocalTaskStage stage)
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var stageEntity = await db.TaskStages.FindAsync(stage.Id);
            if (stageEntity is null) return;

            var task = await db.Tasks.FindAsync(stageEntity.TaskId);
            if (task is null) return;

            var projInfo = await db.Projects
                .Where(p => p.Id == task.ProjectId)
                .Select(p => new { p.Name, p.IsMarkedForDeletion })
                .FirstOrDefaultAsync();
            task.ProjectName = projInfo?.Name ?? task.ProjectName;
            task.ProjectIsMarkedForDeletion = projInfo?.IsMarkedForDeletion ?? false;

            var taskStages = await db.TaskStages
                .Where(s => s.TaskId == task.Id && !s.IsArchived)
                .ToListAsync();
            foreach (var s in taskStages)
            {
                s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
                s.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;
            }
            ProgressCalculator.ApplyTaskMetrics(task, taskStages);
            stageEntity.TaskName = task.Name;
            stageEntity.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
            stageEntity.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;

            var taskPanel = new TaskSummaryPanel();
            taskPanel.SetTask(task);

            var overlay = new StageDetailOverlay();
            overlay.SetStage(new StageItem
            {
                Stage = stageEntity,
                TaskId = task.Id,
                TaskName = task.Name,
                ProjectId = task.ProjectId,
                ProjectName = task.ProjectName ?? "—"
            }, task);

            MainWindow.Instance?.ShowDrawer(taskPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
        }

        private async Task OpenMaterialFromSearchAsync(LocalMaterial material)
        {
            if (DataContext is MainViewModel vm)
                vm.NavigateCommand.Execute("Warehouse");

            var warehouseVm = App.Services.GetRequiredService<WarehouseViewModel>();
            warehouseVm.ActiveTab = "Materials";
            await warehouseVm.LoadAsync();

            var selected = warehouseVm.Materials.FirstOrDefault(m => m.Id == material.Id);
            if (selected is null) return;

            var overlay = new MaterialDetailOverlay(selected, warehouseVm);
            MainWindow.Instance?.ShowDrawer(overlay, 560);
        }

        private async Task OpenEquipmentFromSearchAsync(LocalEquipment equipment)
        {
            if (DataContext is MainViewModel vm)
                vm.NavigateCommand.Execute("Warehouse");

            var warehouseVm = App.Services.GetRequiredService<WarehouseViewModel>();
            warehouseVm.ActiveTab = "Equipment";
            await warehouseVm.LoadAsync();

            var selected = warehouseVm.Equipments.FirstOrDefault(eq => eq.Id == equipment.Id);
            if (selected is null) return;

            var overlay = new EquipmentDetailOverlay(selected, warehouseVm);
            MainWindow.Instance?.ShowDrawer(overlay, 560);
        }

        private async Task OpenFileFromSearchAsync(LocalFile file)
        {
            if (DataContext is MainViewModel vm)
                vm.NavigateCommand.Execute("Files");

            var filesVm = App.Services.GetRequiredService<FilesPageViewModel>();
            filesVm.FilesControlVM.SearchText = file.FileName;
            await filesVm.FilesControlVM.LoadFilesAsync();
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            GlobalSearchBox.Text = "";
            if (DataContext is MainViewModel vm) vm.SearchText = string.Empty;
            SearchResultsPopup.IsOpen = false;
            ClearSearchBtn.Visibility = Visibility.Collapsed;
        }

        private void GlobalSearch_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (DataContext is MainViewModel vm) vm.SearchText = string.Empty;
                GlobalSearchBox.Text = "";
                SearchResultsPopup.IsOpen = false;
                Keyboard.ClearFocus();
            }
        }
    }
}
