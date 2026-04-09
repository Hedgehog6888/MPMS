using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class WriteOffOverlay : UserControl
{
    private readonly Func<string?, Task> _fullWriteOffAction;
    private readonly Func<decimal, string?, Task>? _partialWriteOffAction;
    private readonly decimal _currentQuantity;
    private readonly string? _unit;
    private bool _isPartialMode;

    /// <summary>
    /// For equipment (no partial option): pass only fullWriteOffAction.
    /// For materials: also pass partialWriteOffAction, currentQuantity and unit.
    /// </summary>
    public WriteOffOverlay(
        string entityType,
        string entityName,
        Func<string?, Task> fullWriteOffAction,
        decimal currentQuantity = 0,
        string? unit = null,
        Func<decimal, string?, Task>? partialWriteOffAction = null)
    {
        InitializeComponent();
        _fullWriteOffAction = fullWriteOffAction;
        _partialWriteOffAction = partialWriteOffAction;
        _currentQuantity = currentQuantity;
        _unit = unit;

        TitleLabel.Text = $"Списать {entityType}";
        EntityTypeLabel.Text = entityType.Length > 0
            ? char.ToUpper(entityType[0]) + entityType[1..]
            : entityType;
        EntityNameText.Text = entityName;

        if (partialWriteOffAction is not null)
        {
            var unitLabel = string.IsNullOrWhiteSpace(unit) ? string.Empty : $" {unit}";
            QuantityInfoText.Text = $"Текущий остаток: {currentQuantity:G}{unitLabel}";
            QuantityInfoText.Visibility = Visibility.Visible;

            FullQtyHint.Text = $"{currentQuantity:G}{unitLabel} (полное)";
            ModePanel.Visibility = Visibility.Visible;

            var qtyLabel = string.IsNullOrWhiteSpace(unit)
                ? "Количество *"
                : $"Количество * ({unit})";
            QuantityLabel.Text = qtyLabel;
            QuantityBox.Text = "1";

            SetMode(false);
        }
    }

    private void SetMode(bool partial)
    {
        _isPartialMode = partial;

        ModeFull.Background = partial ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xE6));
        ModeFull.BorderBrush = partial
            ? new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6))
            : new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B));

        ModePartial.Background = partial ? new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xE6)) : Brushes.White;
        ModePartial.BorderBrush = partial
            ? new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B))
            : new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));

        QuantityPanel.Visibility = partial ? Visibility.Visible : Visibility.Collapsed;
        ConfirmButton.Content = partial ? "Списать часть" : "Списать всё";
    }

    private void ModeFull_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SetMode(false);

    private void ModePartial_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SetMode(true);

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (_isPartialMode && _partialWriteOffAction is not null)
        {
            var qty = MaterialUnits.ParseQuantity(QuantityBox.Text, _unit);
            if (qty is null || qty <= 0)
            {
                var isInt = MaterialUnits.IsIntegerUnit(_unit);
                ErrorText.Text = isInt
                    ? "Введите целое положительное число"
                    : "Введите корректное положительное количество";
                ErrorPanel.Visibility = Visibility.Visible;
                return;
            }
            if (qty > _currentQuantity)
            {
                ErrorText.Text = $"Нельзя списать больше, чем есть в наличии ({_currentQuantity:G})";
                ErrorPanel.Visibility = Visibility.Visible;
                return;
            }

            var comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
            MainWindow.Instance?.HideDrawer();
            _ = _partialWriteOffAction(qty.Value, comment);
        }
        else
        {
            var comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();
            MainWindow.Instance?.HideAllOverlays();
            _ = _fullWriteOffAction(comment);
        }
    }
}
