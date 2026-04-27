using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MPMS.Infrastructure;

namespace MPMS.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void NotesRichTextBox_Loaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is RichTextBox rtb)
        {
            RichTextHelper.RegisterRichTextBox(rtb);
        }
    }

    private void ClearFormatting_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection.IsEmpty) return;

        // 1. Clear character formatting
        selection.ClearAllProperties();
        
        // 2. Remove from lists
        // If the selection is inside a list, we toggle the active list type to turn it off
        UpdateFormattingButtons();
        
        if (BulletsBtn.IsChecked == true)
            EditingCommands.ToggleBullets.Execute(null, NotesRTB);
        
        if (NumberingBtn.IsChecked == true)
            EditingCommands.ToggleNumbering.Execute(null, NotesRTB);

        UpdateFormattingButtons();
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        // Give the command a moment to execute then update UI state
        Dispatcher.BeginInvoke(new Action(() => UpdateFormattingButtons()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void NotesRTB_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateFormattingButtons();
    }

    private void UpdateFormattingButtons()
    {
        if (NotesRTB == null || BoldBtn == null) return;

        var selection = NotesRTB.Selection;
        if (selection == null) return;

        // Bold
        object fontWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
        BoldBtn.IsChecked = (fontWeight != DependencyProperty.UnsetValue) && (FontWeight)fontWeight == FontWeights.Bold;

        // Italic
        object fontStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
        ItalicBtn.IsChecked = (fontStyle != DependencyProperty.UnsetValue) && (FontStyle)fontStyle == FontStyles.Italic;

        // Underline
        object textDecorations = selection.GetPropertyValue(Inline.TextDecorationsProperty);
        UnderlineBtn.IsChecked = (textDecorations != DependencyProperty.UnsetValue) && (textDecorations as TextDecorationCollection)?.Count > 0;

        // Lists
        UpdateListButtons(selection);
    }

    private void UpdateListButtons(TextSelection selection)
    {
        BulletsBtn.IsChecked = false;
        NumberingBtn.IsChecked = false;

        Paragraph p = selection.Start.Paragraph;
        if (p == null) return;

        DependencyObject parent = p.Parent;
        while (parent != null)
        {
            if (parent is List list)
            {
                var style = list.MarkerStyle;
                if (style == TextMarkerStyle.Disc || style == TextMarkerStyle.Circle || style == TextMarkerStyle.Square)
                {
                    BulletsBtn.IsChecked = true;
                    NumberingBtn.IsChecked = false; // Ensure mutual exclusivity
                }
                else
                {
                    NumberingBtn.IsChecked = true;
                    BulletsBtn.IsChecked = false; // Ensure mutual exclusivity
                }
                return;
            }
            parent = (parent as FrameworkContentElement)?.Parent;
        }
    }
}
