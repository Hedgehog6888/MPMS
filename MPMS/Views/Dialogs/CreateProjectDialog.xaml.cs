using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;

namespace MPMS.Views.Dialogs;

public partial class CreateProjectDialog : Window
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private bool _isEditMode;

    public CreateProjectRequest? Result { get; private set; }
    public UpdateProjectRequest? UpdateResult { get; private set; }

    public CreateProjectDialog(IDbContextFactory<LocalDbContext> dbFactory)
    {
        InitializeComponent();
        _dbFactory = dbFactory;
        StartDatePicker.SelectedDateChanged += (_, _) => RefreshEndDateBlackoutFromStart();
        Loaded += (_, _) => RefreshEndDateBlackoutFromStart();
        _ = LoadUsersAsync();
    }

    private void RefreshEndDateBlackoutFromStart()
    {
        EndDatePicker.BlackoutDates.Clear();
        if (StartDatePicker.SelectedDate is not { } start)
        {
            EndDatePicker.ClearValue(DatePicker.DisplayDateStartProperty);
            return;
        }
        var startDay = start.Date;
        DueDatePickerRestrictions.SetDisplayDateStartFirstOfMonth(EndDatePicker, startDay);

        if (EndDatePicker.SelectedDate is { } end && end.Date < startDay)
            EndDatePicker.SelectedDate = startDay;

        var minBlackoutEnd = startDay.AddDays(-1);
        if (minBlackoutEnd >= new DateTime(1900, 1, 1))
            EndDatePicker.BlackoutDates.Add(new CalendarDateRange(new DateTime(1900, 1, 1), minBlackoutEnd));
    }

    private async Task LoadUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.Users.OrderBy(u => u.Name).ToListAsync();
        ManagerCombo.ItemsSource = users;
        if (users.Count > 0)
            ManagerCombo.SelectedIndex = 0;
    }

    public void SetEditMode(LocalProject project)
    {
        _isEditMode = true;
        TitleText.Text = "Редактировать проект";
        SaveButton.Content = "Сохранить";

        NameBox.Text = project.Name;
        DescriptionBox.Text = project.Description;
        ClientBox.Text = project.Client;
        AddressBox.Text = project.Address;
        if (project.StartDate.HasValue)
            StartDatePicker.SelectedDate = project.StartDate.Value.ToDateTime(TimeOnly.MinValue);
        if (project.EndDate.HasValue)
            EndDatePicker.SelectedDate = project.EndDate.Value.ToDateTime(TimeOnly.MinValue);
        RefreshEndDateBlackoutFromStart();

        Loaded += (_, _) =>
        {
            ManagerCombo.SelectedValue = project.ManagerId;
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("Введите название проекта.");
            return;
        }

        if (ManagerCombo.SelectedItem is not LocalUser manager)
        {
            ShowError("Выберите менеджера проекта.");
            return;
        }

        DateOnly? startDate = StartDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(StartDatePicker.SelectedDate.Value) : null;
        DateOnly? endDate = EndDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(EndDatePicker.SelectedDate.Value) : null;

        if (_isEditMode)
        {
            UpdateResult = new UpdateProjectRequest(
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                string.IsNullOrWhiteSpace(ClientBox.Text) ? null : ClientBox.Text.Trim(),
                string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                startDate, endDate,
                ProjectStatus.Planning,
                manager.Id);
        }
        else
        {
            Result = new CreateProjectRequest(
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                string.IsNullOrWhiteSpace(ClientBox.Text) ? null : ClientBox.Text.Trim(),
                string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                startDate, endDate,
                manager.Id);
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
