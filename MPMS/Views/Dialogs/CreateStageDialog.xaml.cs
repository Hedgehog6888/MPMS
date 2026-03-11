using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Views.Dialogs;

public partial class CreateStageDialog : Window
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private bool _isEditMode;
    private Guid _taskId;

    public CreateStageRequest? Result { get; private set; }
    public UpdateStageRequest? UpdateResult { get; private set; }

    public CreateStageDialog(IDbContextFactory<LocalDbContext> dbFactory)
    {
        InitializeComponent();
        _dbFactory = dbFactory;
        _ = LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.Users.OrderBy(u => u.Name).ToListAsync();
        AssigneeCombo.ItemsSource = users;
    }

    public void SetTask(Guid taskId) => _taskId = taskId;

    public void SetEditMode(LocalTaskStage stage)
    {
        _isEditMode = true;
        _taskId = stage.TaskId;
        TitleText.Text = "Редактировать этап";
        SaveButton.Content = "Сохранить";
        StatusPanel.Visibility = Visibility.Visible;

        NameBox.Text = stage.Name;
        DescriptionBox.Text = stage.Description;

        Loaded += (_, _) =>
        {
            if (stage.AssignedUserId.HasValue)
                AssigneeCombo.SelectedValue = stage.AssignedUserId.Value;

            foreach (ComboBoxItem item in StatusCombo.Items)
            {
                if (item.Tag?.ToString() == stage.Status.ToString())
                {
                    item.IsSelected = true;
                    break;
                }
            }
        };
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Введите название этапа.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        var assignedUser = AssigneeCombo.SelectedItem as LocalUser;

        if (_isEditMode)
        {
            var statusTag = (StatusCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Planned";
            Enum.TryParse<StageStatus>(statusTag, out var status);

            UpdateResult = new UpdateStageRequest(
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                assignedUser?.Id,
                status);
        }
        else
        {
            Result = new CreateStageRequest(
                _taskId,
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                assignedUser?.Id);
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
