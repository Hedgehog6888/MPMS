using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
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
        
        // 2. Clear blockquote formatting
        var paragraphs = GetSelectedParagraphs(selection);
        foreach (var p in paragraphs)
        {
            p.ClearValue(Block.BorderBrushProperty);
            p.ClearValue(Block.BorderThicknessProperty);
            p.ClearValue(Block.PaddingProperty);
            p.ClearValue(Block.BackgroundProperty);
            p.ClearValue(Block.MarginProperty);
            p.ClearValue(TextElement.FontStyleProperty);
            p.ClearValue(TextElement.ForegroundProperty);
            p.ClearValue(FrameworkContentElement.TagProperty);
        }
        RefreshBlockquoteFormatting();

        // 3. Remove from lists
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
        Dispatcher.BeginInvoke(new Action(() => 
        {
            MergeAdjacentLists(NotesRTB);
            UpdateFormattingButtons();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void MergeAdjacentLists(RichTextBox rtb)
    {
        var doc = rtb.Document;
        if (doc == null) return;

        bool changed = false;
        // Iterate through top-level blocks to merge adjacent lists of the same type
        for (int i = 0; i < doc.Blocks.Count - 1; i++)
        {
            if (doc.Blocks.ElementAt(i) is List list1 && doc.Blocks.ElementAt(i + 1) is List list2)
            {
                // Merge if they have the same marker style (e.g., both Decimal or both Disc)
                if (list1.MarkerStyle == list2.MarkerStyle)
                {
                    // Move all items from list2 to list1
                    var items = list2.ListItems.ToList();
                    foreach (var item in items)
                    {
                        list2.ListItems.Remove(item);
                        list1.ListItems.Add(item);
                    }
                    // Remove the now-empty second list
                    doc.Blocks.Remove(list2);
                    
                    // Stay on current index to check if the next block is also a list that should be merged
                    i--;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            // Update the XAML binding to save changes
            RichTextHelper.UpdateDocumentXaml(rtb);
        }
    }

    private void Strikethrough_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection == null) return;

        var currentDecorations = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
        var newDecorations = new TextDecorationCollection();

        if (currentDecorations != null && currentDecorations != DependencyProperty.UnsetValue)
        {
            foreach (var decoration in currentDecorations)
            {
                if (decoration.Location != TextDecorationLocation.Strikethrough)
                {
                    newDecorations.Add(decoration);
                }
            }
        }

        if (StrikethroughBtn.IsChecked == true)
        {
            newDecorations.Add(TextDecorations.Strikethrough[0]);
        }

        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
        UpdateFormattingButtons();
    }

    private void Highlight_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection == null) return;

        var currentBackground = selection.GetPropertyValue(TextElement.BackgroundProperty);
        var highlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9C4"));

        if (HighlightBtn.IsChecked == true)
        {
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, highlightBrush);
        }
        else
        {
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        }
        UpdateFormattingButtons();
    }

    private void Blockquote_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection == null) return;

        var paragraphs = GetSelectedParagraphs(selection);
        
        // Generate a unique ID for this specific formatting action
        // This ensures that adjacent quotes formatted separately don't merge
        string blockId = System.Guid.NewGuid().ToString();

        foreach (var p in paragraphs)
        {
            if (BlockquoteBtn.IsChecked == true)
            {
                p.BorderBrush = new SolidColorBrush(Color.FromRgb(17, 17, 17)); // Premium Black
                p.BorderThickness = new Thickness(4, 0, 0, 0);
                p.Background = new SolidColorBrush(Color.FromRgb(249, 250, 251)); // Very light gray-blue
                p.FontStyle = FontStyles.Italic;
                p.Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104)); // Slate gray #4A5568
                p.Tag = blockId;
            }
            else
            {
                p.ClearValue(Block.BorderBrushProperty);
                p.ClearValue(Block.BorderThicknessProperty);
                p.ClearValue(Block.PaddingProperty);
                p.ClearValue(Block.BackgroundProperty);
                p.ClearValue(Block.MarginProperty);
                p.ClearValue(TextElement.FontStyleProperty);
                p.ClearValue(TextElement.ForegroundProperty);
                p.ClearValue(FrameworkContentElement.TagProperty);
            }
        }
        
        RefreshBlockquoteFormatting();
        UpdateFormattingButtons();
    }

    private void RefreshBlockquoteFormatting()
    {
        var blocks = NotesRTB.Document.Blocks.ToList();
        foreach (var block in blocks)
        {
            if (block is Paragraph p)
            {
                // 1. Always remove old quote icons to avoid duplicates or stale icons
                var existingFloater = p.Inlines.OfType<Floater>().FirstOrDefault(f => Equals(f.Tag, "QuoteIcon"));
                if (existingFloater != null) p.Inlines.Remove(existingFloater);

                bool isQuoted = p.BorderThickness.Left > 0;
                if (isQuoted)
                {
                    // Check neighbors to decide on margins and padding
                    bool prevIsQuoted = (p.PreviousBlock is Paragraph prevP) && 
                                        prevP.BorderThickness.Left > 0 && 
                                        Equals(prevP.Tag, p.Tag);
                                        
                    bool nextIsQuoted = (p.NextBlock is Paragraph nextP) && 
                                        nextP.BorderThickness.Left > 0 && 
                                        Equals(nextP.Tag, p.Tag);

                    // Slightly reduced vertical spacing around the entire block
                    double topMargin = prevIsQuoted ? 0 : 10;
                    double bottomMargin = nextIsQuoted ? 0 : 10;
                    p.Margin = new Thickness(0, topMargin, 0, bottomMargin);
                    
                    // Reduced padding for a tighter look
                    double topPadding = prevIsQuoted ? 2 : 8;
                    double bottomPadding = nextIsQuoted ? 2 : 8;
                    p.Padding = new Thickness(20, topPadding, 0, bottomPadding);

                    // 2. Add a decorative quote icon to the first paragraph of the block
                    if (!prevIsQuoted)
                    {
                        Floater floater = new Floater
                        {
                            Tag = "QuoteIcon",
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Width = 24,
                            Margin = new Thickness(0),
                            Padding = new Thickness(0)
                        };

                        TextBlock quoteIcon = new TextBlock
                        {
                            Text = "“", // Using opening quote to match the button icon
                            FontSize = 32,
                            FontFamily = new FontFamily("Georgia"),
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                            Margin = new Thickness(0, -8, 4, 0),
                            IsHitTestVisible = false,
                            Focusable = false
                        };
                        
                        var container = new BlockUIContainer(quoteIcon);
                        container.IsEnabled = false; // Try to make it non-selectable without compilation error
                        floater.Blocks.Add(container);
                        
                        if (p.Inlines.FirstInline != null)
                            p.Inlines.InsertBefore(p.Inlines.FirstInline, floater);
                        else
                            p.Inlines.Add(floater);
                    }
                }
            }
        }

        UpdateFormattingButtons();
    }

    private void Checklist_Click(object sender, RoutedEventArgs e)
    {
        // Checkbox logic removed as requested
        ChecklistBtn.IsChecked = false;
    }

    private List<Paragraph> GetSelectedParagraphs(TextSelection selection)
    {
        var paragraphs = new List<Paragraph>();
        var pointer = selection.Start.GetPositionAtOffset(0, LogicalDirection.Forward);
        
        while (pointer != null && pointer.CompareTo(selection.End) <= 0)
        {
            var p = pointer.Paragraph;
            if (p != null && !paragraphs.Contains(p))
            {
                paragraphs.Add(p);
            }
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }
        
        // If selection is empty but we are in a paragraph, add it
        if (paragraphs.Count == 0 && selection.Start.Paragraph != null)
        {
            paragraphs.Add(selection.Start.Paragraph);
        }
        
        return paragraphs;
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

        // Underline & Strikethrough
        object textDecorations = selection.GetPropertyValue(Inline.TextDecorationsProperty);
        if (textDecorations != DependencyProperty.UnsetValue && textDecorations is TextDecorationCollection coll)
        {
            UnderlineBtn.IsChecked = coll.Any(d => d.Location == TextDecorationLocation.Underline);
            StrikethroughBtn.IsChecked = coll.Any(d => d.Location == TextDecorationLocation.Strikethrough);
        }
        else
        {
            UnderlineBtn.IsChecked = false;
            StrikethroughBtn.IsChecked = false;
        }

        // Highlight
        object background = selection.GetPropertyValue(TextElement.BackgroundProperty);
        HighlightBtn.IsChecked = (background != DependencyProperty.UnsetValue && background != null);

        // Blockquote (check current paragraph)
        Paragraph p = selection.Start.Paragraph;
        if (p != null)
        {
            BlockquoteBtn.IsChecked = p.BorderThickness.Left > 0;
        }
        else
        {
            BlockquoteBtn.IsChecked = false;
        }
        ChecklistBtn.IsChecked = false;

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
    private void PreventScrollBubbling(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        // If the scrollviewer/richtextbox reached its limit, WPF doesn't mark the event as handled,
        // so it bubbles up to the parent scrollviewer. We mark it handled to stop this.
        e.Handled = true;
    }
}
