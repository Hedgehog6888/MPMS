using System.Windows;
using MPMS.Models;

namespace MPMS.Views.Dialogs;

public partial class CreateMaterialDialog : Window
{
    private bool _isEditMode;

    public CreateMaterialRequest? Result { get; private set; }
    public UpdateMaterialRequest? UpdateResult { get; private set; }

    public CreateMaterialDialog()
    {
        InitializeComponent();
    }

    public void SetEditMode(LocalMaterial material)
    {
        _isEditMode = true;
        TitleText.Text = "Редактировать материал";
        SaveButton.Content = "Сохранить";

        NameBox.Text = material.Name;
        UnitBox.Text = material.Unit;
        DescriptionBox.Text = material.Description;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ErrorText.Text = "Введите название материала.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (_isEditMode)
        {
            UpdateResult = new UpdateMaterialRequest(
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(UnitBox.Text) ? null : UnitBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim());
        }
        else
        {
            Result = new CreateMaterialRequest(
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(UnitBox.Text) ? null : UnitBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim());
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
