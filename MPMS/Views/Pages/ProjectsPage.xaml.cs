using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Dialogs;

namespace MPMS.Views.Pages;

public partial class ProjectsPage : UserControl
{
    public ProjectsPage()
    {
        InitializeComponent();
    }

    private ProjectsViewModel? VM => DataContext as ProjectsViewModel;

    private MainViewModel? MainVM =>
        Application.Current.MainWindow?.DataContext as MainViewModel;

    private async void CreateProject_Click(object sender, RoutedEventArgs e)
    {
        var dialog = App.Services.GetRequiredService<CreateProjectDialog>();
        dialog.Owner = Window.GetWindow(this);
        if (dialog.ShowDialog() == true && dialog.Result is not null && VM is not null)
        {
            var id = Guid.NewGuid();
            await VM.SaveNewProjectAsync(dialog.Result, id);
        }
    }

    private async void EditProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null)
            return;

        var dialog = App.Services.GetRequiredService<CreateProjectDialog>();
        dialog.Owner = Window.GetWindow(this);
        dialog.SetEditMode(project);

        if (dialog.ShowDialog() == true && dialog.UpdateResult is not null)
            await VM.SaveUpdatedProjectAsync(project.Id, dialog.UpdateResult);
    }

    private async void DeleteProject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalProject project || VM is null)
            return;

        var result = MessageBox.Show(
            $"Удалить проект «{project.Name}»?\nВсе задачи проекта также будут удалены.",
            "Подтверждение удаления",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            await VM.DeleteProjectCommand.ExecuteAsync(project);
    }

    private void ProjectName_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock tb || tb.DataContext is not LocalProject project)
            return;
        MainVM?.NavigateToProject(project);
    }
}
