using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using MPMS.Infrastructure;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class HomePage : UserControl
{
    private DispatcherTimer? _formattingUpdateTimer;

    public HomePage()
    {
        InitializeComponent();
    }

    // ── Инициализация RichTextBox ────────────────────────────────────────────

    private void NotesRichTextBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not RichTextBox rtb) return;
        if (DataContext is not HomeViewModel vm) return;

        // View → ViewModel: только при сохранении
        vm.SyncContentFromView = () =>
        {
            vm.CurrentNoteXaml = RichTextHelper.ReadDocumentXaml(rtb);
        };

        // Отслеживание изменений без сериализации
        rtb.TextChanged += (_, _) =>
        {
            if (DataContext is HomeViewModel v)
                v.IsNoteDirty = true;
        };
    }

    // ── Форматирование ───────────────────────────────────────────────────────

    /// <summary>
    /// Debounce для SelectionChanged — обновляем кнопки форматирования не чаще раза в 100мс.
    /// Это не влияет на кнопки форматирования при клике (они вызывают UpdateFormattingButtons напрямую).
    /// </summary>
    private void NotesRTB_SelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_formattingUpdateTimer == null)
        {
            _formattingUpdateTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _formattingUpdateTimer.Tick += (_, _) =>
            {
                _formattingUpdateTimer.Stop();
                UpdateFormattingButtons();
            };
        }
        else
        {
            _formattingUpdateTimer.Stop();
        }
        _formattingUpdateTimer.Start();
    }

    private void UpdateFormattingButtons()
    {
        if (NotesRTB == null || BoldBtn == null) return;

        var selection = NotesRTB.Selection;
        if (selection == null) return;

        object fontWeight = selection.GetPropertyValue(TextElement.FontWeightProperty);
        BoldBtn.IsChecked = fontWeight != DependencyProperty.UnsetValue && (FontWeight)fontWeight == FontWeights.Bold;

        object fontStyle = selection.GetPropertyValue(TextElement.FontStyleProperty);
        ItalicBtn.IsChecked = fontStyle != DependencyProperty.UnsetValue && (FontStyle)fontStyle == FontStyles.Italic;

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

        object background = selection.GetPropertyValue(TextElement.BackgroundProperty);
        HighlightBtn.IsChecked = background != DependencyProperty.UnsetValue && background != null;

        Paragraph? p = selection.Start.Paragraph;
        BlockquoteBtn.IsChecked = p != null && p.BorderThickness.Left > 0;
        ChecklistBtn.IsChecked = false;

        UpdateListButtons(selection);
    }

    private void UpdateListButtons(TextSelection selection)
    {
        BulletsBtn.IsChecked = false;
        NumberingBtn.IsChecked = false;

        Paragraph? p = selection.Start.Paragraph;
        if (p == null) return;

        DependencyObject? parent = p.Parent;
        while (parent != null)
        {
            if (parent is List list)
            {
                var style = list.MarkerStyle;
                if (style is TextMarkerStyle.Disc or TextMarkerStyle.Circle or TextMarkerStyle.Square)
                {
                    BulletsBtn.IsChecked = true;
                }
                else
                {
                    NumberingBtn.IsChecked = true;
                }
                return;
            }
            parent = (parent as FrameworkContentElement)?.Parent;
        }
    }

    private void FormatButton_Click(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            MergeAdjacentLists(NotesRTB);
            UpdateFormattingButtons();
        }), DispatcherPriority.Input);
    }

    private void MergeAdjacentLists(RichTextBox rtb)
    {
        var doc = rtb.Document;
        if (doc == null) return;

        bool changed = false;
        for (int i = 0; i < doc.Blocks.Count - 1; i++)
        {
            if (doc.Blocks.ElementAt(i) is List list1 && doc.Blocks.ElementAt(i + 1) is List list2 &&
                list1.MarkerStyle == list2.MarkerStyle)
            {
                var items = list2.ListItems.ToList();
                foreach (var item in items)
                {
                    list2.ListItems.Remove(item);
                    list1.ListItems.Add(item);
                }
                doc.Blocks.Remove(list2);
                i--;
                changed = true;
            }
        }

        if (changed && DataContext is HomeViewModel vm)
            vm.IsNoteDirty = true;
    }

    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection.IsEmpty) return;

        selection.ClearAllProperties();

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

        UpdateFormattingButtons();

        if (BulletsBtn.IsChecked == true)
            EditingCommands.ToggleBullets.Execute(null, NotesRTB);

        if (NumberingBtn.IsChecked == true)
            EditingCommands.ToggleNumbering.Execute(null, NotesRTB);

        UpdateFormattingButtons();
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
                    newDecorations.Add(decoration);
            }
        }

        if (StrikethroughBtn.IsChecked == true)
            newDecorations.Add(TextDecorations.Strikethrough[0]);

        selection.ApplyPropertyValue(Inline.TextDecorationsProperty, newDecorations);
        UpdateFormattingButtons();
    }

    private void Highlight_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection == null) return;

        var highlightBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF9C4"));

        if (HighlightBtn.IsChecked == true)
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, highlightBrush);
        else
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, null);

        UpdateFormattingButtons();
    }

    private void Blockquote_Click(object sender, RoutedEventArgs e)
    {
        var selection = NotesRTB.Selection;
        if (selection == null) return;

        var paragraphs = GetSelectedParagraphs(selection);
        string blockId = Guid.NewGuid().ToString();

        foreach (var p in paragraphs)
        {
            if (BlockquoteBtn.IsChecked == true)
            {
                p.BorderBrush = new SolidColorBrush(Color.FromRgb(17, 17, 17));
                p.BorderThickness = new Thickness(4, 0, 0, 0);
                p.Background = new SolidColorBrush(Color.FromRgb(249, 250, 251));
                p.FontStyle = FontStyles.Italic;
                p.Foreground = new SolidColorBrush(Color.FromRgb(74, 85, 104));
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
            if (block is not Paragraph p) continue;

            var existingFloater = p.Inlines.OfType<Floater>().FirstOrDefault(f => Equals(f.Tag, "QuoteIcon"));
            if (existingFloater != null) p.Inlines.Remove(existingFloater);

            bool isQuoted = p.BorderThickness.Left > 0;
            if (!isQuoted) continue;

            bool prevIsQuoted = p.PreviousBlock is Paragraph prevP && prevP.BorderThickness.Left > 0 && Equals(prevP.Tag, p.Tag);
            bool nextIsQuoted = p.NextBlock is Paragraph nextP && nextP.BorderThickness.Left > 0 && Equals(nextP.Tag, p.Tag);

            p.Margin = new Thickness(0, prevIsQuoted ? 0 : 10, 0, nextIsQuoted ? 0 : 10);
            p.Padding = new Thickness(20, prevIsQuoted ? 2 : 8, 0, nextIsQuoted ? 2 : 8);

            if (!prevIsQuoted)
            {
                var quoteIcon = new TextBlock
                {
                    Text = "\u201c",
                    FontSize = 32,
                    FontFamily = new FontFamily("Georgia"),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
                    Margin = new Thickness(0, -8, 4, 0),
                    IsHitTestVisible = false,
                    Focusable = false
                };

                var floater = new Floater
                {
                    Tag = "QuoteIcon",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = 24,
                    Margin = new Thickness(0),
                    Padding = new Thickness(0)
                };
                floater.Blocks.Add(new BlockUIContainer(quoteIcon) { IsEnabled = false });

                if (p.Inlines.FirstInline != null)
                    p.Inlines.InsertBefore(p.Inlines.FirstInline, floater);
                else
                    p.Inlines.Add(floater);
            }
        }

        UpdateFormattingButtons();
    }

    private void Checklist_Click(object sender, RoutedEventArgs e)
    {
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
                paragraphs.Add(p);
            pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
        }

        if (paragraphs.Count == 0 && selection.Start.Paragraph != null)
            paragraphs.Add(selection.Start.Paragraph);

        return paragraphs;
    }

    private void PreventScrollBubbling(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        e.Handled = true;
    }

    private void ActivityHelpButton_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.ShowCenteredOverlay(new ActivityHelpOverlay(), 760);
    }

    private void ActivitiesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (ActivitiesScrollTopBtn is null || ActivitiesScrollViewer is null)
            return;

        ActivitiesScrollTopBtn.Visibility = ActivitiesScrollViewer.VerticalOffset > 64
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ActivitiesScrollTopBtn_Click(object sender, RoutedEventArgs e)
    {
        ActivitiesScrollViewer?.ScrollToTop();
    }
}
