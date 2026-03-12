using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateProjectOverlay : UserControl
{
    private ProjectsViewModel? _vm;
    private LocalProject? _editProject;
    private Func<System.Threading.Tasks.Task>? _onSaved;

    public CreateProjectOverlay()
    {
        InitializeComponent();
    }

    public void SetCreateMode(ProjectsViewModel vm)
    {
        _vm = vm;
        TitleLabel.Text = "Создать проект";
        SaveButton.Content = "Создать проект";
        _ = LoadUsersAsync();
    }

    public void SetEditMode(ProjectsViewModel vm, LocalProject project,
        Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _vm = vm;
        _editProject = project;
        _onSaved = onSaved;
        TitleLabel.Text = "Редактировать проект";
        SaveButton.Content = "Сохранить изменения";
        StatusRow.Visibility = Visibility.Visible;

        NameBox.Text = project.Name;
        DescriptionBox.Text = project.Description ?? "";
        ClientBox.Text = project.Client ?? "";
        AddressBox.Text = project.Address ?? "";

        if (project.StartDate.HasValue)
            StartDatePicker.SelectedDate = project.StartDate.Value.ToDateTime(TimeOnly.MinValue);
        if (project.EndDate.HasValue)
            EndDatePicker.SelectedDate = project.EndDate.Value.ToDateTime(TimeOnly.MinValue);

        // Select status
        foreach (ComboBoxItem item in StatusCombo.Items)
        {
            if (item.Tag?.ToString() == project.Status.ToString())
            {
                StatusCombo.SelectedItem = item;
                break;
            }
        }

        _ = LoadUsersAsync(project.ManagerId);
    }

    private static bool IsManagerRole(string role) =>
        role is "ProjectManager" or "Manager" or "Project Manager";

    private static bool IsAdminRole(string role) =>
        role is "Admin" or "Administrator";

    private async System.Threading.Tasks.Task LoadUsersAsync(Guid? selectedId = null)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        string role = auth.UserRole ?? "";

        if (IsManagerRole(role))
        {
            // Manager can only assign themselves as project manager
            var self = await db.Users.FindAsync(auth.UserId);
            if (self is not null)
            {
                ManagerCombo.ItemsSource = new[] { self };
                ManagerCombo.SelectedIndex = 0;
                ManagerCombo.IsEnabled = false;
            }
            return;
        }

        // Admin: show only Manager-role users
        var users = await db.Users
            .Where(u => u.RoleName == "ProjectManager" || u.RoleName == "Manager"
                     || u.RoleName == "Project Manager")
            .OrderBy(u => u.Name)
            .ToListAsync();

        // Fallback: if no managers synced yet, show all users
        if (users.Count == 0)
            users = await db.Users.OrderBy(u => u.Name).ToListAsync();

        ManagerCombo.ItemsSource = users;
        if (selectedId.HasValue)
            ManagerCombo.SelectedValue = selectedId.Value;
        else if (users.Count > 0)
            ManagerCombo.SelectedIndex = 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("Введите название проекта.");
            return;
        }
        if (string.IsNullOrWhiteSpace(ClientBox.Text))
        {
            ShowError("Введите название заказчика.");
            return;
        }
        if (StartDatePicker.SelectedDate is null || EndDatePicker.SelectedDate is null)
        {
            ShowError("Выберите даты начала и завершения.");
            return;
        }
        if (ManagerCombo.SelectedValue is not Guid managerId)
        {
            ShowError("Выберите ответственного менеджера.");
            return;
        }

        var startDate = DateOnly.FromDateTime(StartDatePicker.SelectedDate.Value);
        var endDate   = DateOnly.FromDateTime(EndDatePicker.SelectedDate.Value);

        SaveButton.IsEnabled = false;
        try
        {
            if (_editProject is null)
            {
                var req = new CreateProjectRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    ClientBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                    startDate, endDate, managerId);
                await _vm!.SaveNewProjectAsync(req, Guid.NewGuid());
            }
            else
            {
                var status = GetSelectedStatus();
                var req = new UpdateProjectRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    ClientBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                    startDate, endDate, status, managerId);
                await _vm!.SaveUpdatedProjectAsync(_editProject.Id, req);
                if (_onSaved is not null) await _onSaved();
            }
            MainWindow.Instance?.HideDrawer();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private ProjectStatus GetSelectedStatus()
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "InProgress" => ProjectStatus.InProgress,
                "Completed"  => ProjectStatus.Completed,
                "Cancelled"  => ProjectStatus.Cancelled,
                _            => ProjectStatus.Planning
            };
        }
        return ProjectStatus.Planning;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
