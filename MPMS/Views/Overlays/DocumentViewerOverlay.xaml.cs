using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;

namespace MPMS.Views.Overlays;

public partial class DocumentViewerOverlay : UserControl
{
    // ── File ───────────────────────────────────────────────────────────────
    private string _filePath = string.Empty;
    private string _fileName = string.Empty;
    private string? _description;
    private bool _hasUnsavedChanges;
    private readonly Func<string, string, string?, Task>? _savedFileHandler;
    private string _fileExtension = string.Empty;

    // ── Document content ─────────────────────────────────────────────────────
    private enum DocumentType { Text, Word, Excel, Unsupported }
    private DocumentType _docType = DocumentType.Unsupported;
    private int _currentPage = 1;
    private int _totalPages = 1;

    public DocumentViewerOverlay(string filePath, string? displayFileName = null, string? description = null, Func<string, string, string?, Task>? savedFileHandler = null)
    {
        InitializeComponent();
        _filePath = filePath;
        _fileName = string.IsNullOrWhiteSpace(displayFileName) ? System.IO.Path.GetFileName(filePath) : displayFileName;
        _description = description;
        _savedFileHandler = savedFileHandler;
        _fileExtension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

        DocumentNameText.Text = _fileName;
        LoadDocument(filePath);
        _hasUnsavedChanges = false;
        UpdateOpenInAppButtonText();

        // Enable mouse wheel scroll (faster, like Word)
        DocumentScrollViewer.PreviewMouseWheel += (s, e) =>
        {
            // Scroll by 3 lines per wheel tick for faster scrolling (similar to Word)
            const int scrollLines = 3;
            if (e.Delta > 0)
            {
                for (int i = 0; i < scrollLines; i++)
                    DocumentScrollViewer.LineUp();
            }
            else
            {
                for (int i = 0; i < scrollLines; i++)
                    DocumentScrollViewer.LineDown();
            }
            e.Handled = true;
        };
    }

    // ── Document loading ─────────────────────────────────────────────────────
    private void LoadDocument(string path)
    {
        try
        {
            _docType = DetectDocumentType(_fileExtension);

            switch (_docType)
            {
                case DocumentType.Text:
                    LoadTextFile(path);
                    break;
                case DocumentType.Word:
                    LoadWordDocument(path);
                    break;
                case DocumentType.Excel:
                    ShowExcelNotViewable();
                    break;
                default:
                    ShowUnsupportedFormat();
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError($"Не удалось загрузить документ: {ex.Message}");
        }
    }

    private DocumentType DetectDocumentType(string extension)
    {
        return extension switch
        {
            ".txt" or ".csv" or ".log" or ".json" or ".xml" or ".md" or ".html" or ".htm" => DocumentType.Text,
            ".doc" or ".docx" or ".docm" or ".dot" or ".dotx" => DocumentType.Word,
            ".xls" or ".xlsx" or ".xlsm" or ".xlsb" or ".csv" => DocumentType.Excel,
            _ => DocumentType.Unsupported
        };
    }

    private void LoadTextFile(string path)
    {
        var text = File.ReadAllText(path);

        var excelDocumentBorder = (System.Windows.Controls.Border)FindName("ExcelDocumentBorder");
        var pagesContainer = (ItemsControl)FindName("PagesContainer");

        excelDocumentBorder!.Visibility = Visibility.Collapsed;
        pagesContainer!.Visibility = Visibility.Visible;
        FallbackCard.Visibility = Visibility.Collapsed;

        pagesContainer.Items.Clear();

        var flowDocument = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            TextAlignment = TextAlignment.Left
        };

        var paragraph = new Paragraph(new Run(text));
        flowDocument.Blocks.Add(paragraph);

        var flowDocViewer = new FlowDocumentScrollViewer
        {
            Document = flowDocument,
            Zoom = 100,
            IsToolBarVisible = false,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(197, 201, 208)) // #C5C9D0
        };

        var pageBorder = new System.Windows.Controls.Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Width = 794,
            Margin = new Thickness(0, 0, 0, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0),
                Direction = 315,
                ShadowDepth = 2,
                Opacity = 0.15,
                BlurRadius = 8
            },
            Child = flowDocViewer
        };

        pagesContainer.Items.Add(pageBorder);
        _totalPages = 1;
    }

    private void LoadWordDocument(string path)
    {
        // Use Syncfusion DocIO to read Word document
        using var doc = new WordDocument();
        doc.Open(path);

        var excelDocumentBorder = (System.Windows.Controls.Border)FindName("ExcelDocumentBorder");
        var pagesContainer = (ItemsControl)FindName("PagesContainer");

        excelDocumentBorder!.Visibility = Visibility.Collapsed;
        pagesContainer!.Visibility = Visibility.Visible;
        FallbackCard.Visibility = Visibility.Collapsed;

        pagesContainer.Items.Clear();

        var allBlocks = new List<Block>();
        var blockSizes = new List<double>(); // Estimated height in pixels
        List? currentList = null;

        foreach (IWSection section in doc.Sections)
        {
            // Process tables first
            foreach (WTable table in section.Tables)
            {
                // End current list before table
                if (currentList != null)
                {
                    allBlocks.Add(currentList);
                    blockSizes.Add(EstimateListHeight(currentList));
                    currentList = null;
                }
                allBlocks.Add(ConvertWordTableToFlowTable(table));
                blockSizes.Add(EstimateTableHeight(table));
            }

            // Process paragraphs
            foreach (IWParagraph paragraph in section.Paragraphs)
            {
                var para = new Paragraph();

                // Apply paragraph formatting
                ApplyParagraphFormatting(para, paragraph);

                // Check if this is a list item by checking if paragraph has list formatting
                bool isListItem = paragraph.ListFormat != null &&
                                  paragraph.ListFormat.ListLevelNumber > 0;

                if (isListItem)
                {
                    if (currentList == null)
                    {
                        currentList = new List();
                        currentList.MarkerStyle = System.Windows.TextMarkerStyle.Disc;
                    }

                    var listItem = new ListItem(para);
                    currentList.ListItems.Add(listItem);
                }
                else
                {
                    // End current list
                    if (currentList != null)
                    {
                        allBlocks.Add(currentList);
                        blockSizes.Add(EstimateListHeight(currentList));
                        currentList = null;
                    }

                    bool hasImage = false;
                    int textLength = 0;
                    foreach (var entity in paragraph.ChildEntities)
                    {
                        if (entity is IWTextRange textRange)
                        {
                            var run = new Run(textRange.Text);
                            ApplyCharacterFormatting(run, textRange);
                            para.Inlines.Add(run);
                            textLength += textRange.Text.Length;
                        }
                        else if (entity is WPicture picture)
                        {
                            var image = ConvertWordPictureToImage(picture);
                            if (image != null)
                            {
                                para.Inlines.Add(image);
                                hasImage = true;
                            }
                        }
                    }

                    if (paragraph.ChildEntities.Count == 0)
                    {
                        para.Inlines.Add(new Run(""));
                    }

                    allBlocks.Add(para);
                    blockSizes.Add(EstimateParagraphHeight(hasImage, textLength));
                }
            }

            // End current list at end of section
            if (currentList != null)
            {
                allBlocks.Add(currentList);
                blockSizes.Add(EstimateListHeight(currentList));
                currentList = null;
            }
        }

        // Split into pages based on estimated height
        SplitIntoPagesByHeight(allBlocks, blockSizes, pagesContainer);
    }

    private double EstimateParagraphHeight(bool hasImage, int textLength)
    {
        if (hasImage)
        {
            // Paragraph with image: estimate based on typical image size
            return 300; // Large image takes significant space
        }
        else if (textLength > 0)
        {
            // Text paragraph: estimate based on line count (~90 chars per line at 14pt with 1.5 line spacing)
            int lineCount = (textLength / 90) + 1;
            return lineCount * 28; // 28px per line (14pt font + 1.5 line spacing + margins)
        }
        else
        {
            // Empty paragraph
            return 20; // Spacing for empty paragraph with margins
        }
    }

    private double EstimateTableHeight(WTable table)
    {
        // Table height: header (40) + data rows (25 each)
        int rowCount = table.Rows.Count;
        return 40 + Math.Max(0, rowCount - 1) * 25;
    }

    private double EstimateListHeight(List list)
    {
        // List height: 35 per list item (including numbering and margins)
        return list.ListItems.Count * 35;
    }

    private void SplitIntoPagesByHeight(List<Block> blocks, List<double> sizes, ItemsControl pagesContainer)
    {
        const double pageHeight = 1050; // Available height per page (1123 - 73 padding)

        var currentPageBlocks = new List<Block>();
        double currentHeight = 0;
        int pageCount = 0;

        for (int i = 0; i < blocks.Count; i++)
        {
            double blockSize = sizes[i];

            // If block is too large for a single page, put it on its own page
            if (blockSize > pageHeight && currentPageBlocks.Count > 0)
            {
                CreatePageWithBlocks(currentPageBlocks, pageCount + 1, pagesContainer);
                pageCount++;
                currentPageBlocks = new List<Block> { blocks[i] };
                currentHeight = 0;
            }
            // If adding block would exceed page height and we have content, start new page
            else if (currentHeight + blockSize > pageHeight && currentPageBlocks.Count > 0)
            {
                CreatePageWithBlocks(currentPageBlocks, pageCount + 1, pagesContainer);
                pageCount++;
                currentPageBlocks = new List<Block>();
                currentHeight = 0;
                currentPageBlocks.Add(blocks[i]);
                currentHeight += blockSize;
            }
            else
            {
                currentPageBlocks.Add(blocks[i]);
                currentHeight += blockSize;
            }
        }

        // Add last page if it has content
        if (currentPageBlocks.Count > 0)
        {
            CreatePageWithBlocks(currentPageBlocks, pageCount + 1, pagesContainer);
            pageCount++;
        }

        _totalPages = pageCount;
    }

    private void ApplyParagraphFormatting(Paragraph para, IWParagraph wordPara)
    {
        // Text alignment
        var format = wordPara.ParagraphFormat;
        para.TextAlignment = format.HorizontalAlignment switch
        {
            Syncfusion.DocIO.DLS.HorizontalAlignment.Left => TextAlignment.Left,
            Syncfusion.DocIO.DLS.HorizontalAlignment.Center => TextAlignment.Center,
            Syncfusion.DocIO.DLS.HorizontalAlignment.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };

        // Indentation
        para.Margin = new Thickness(
            format.LeftIndent > 0 ? format.LeftIndent : 0,
            format.BeforeSpacing > 0 ? format.BeforeSpacing / 2 : 0,
            format.RightIndent > 0 ? format.RightIndent : 0,
            format.AfterSpacing > 0 ? format.AfterSpacing / 2 : 0
        );

        // First line indent
        if (format.FirstLineIndent != 0)
        {
            para.TextIndent = format.FirstLineIndent;
        }

        // Line spacing
        if (format.LineSpacing > 0)
        {
            para.LineHeight = format.LineSpacing;
        }
    }

    private void ApplyCharacterFormatting(Run run, IWTextRange textRange)
    {
        var format = textRange.CharacterFormat;

        if (format.Bold)
            run.FontWeight = FontWeights.Bold;
        if (format.Italic)
            run.FontStyle = FontStyles.Italic;

        run.FontSize = format.FontSize > 0 ? format.FontSize : 14;

        // Apply font family from Word document
        if (!string.IsNullOrEmpty(format.FontName))
        {
            run.FontFamily = new FontFamily(format.FontName);
        }

        // Text color
        var textColor = format.TextColor;
        if (textColor.ToArgb() != System.Drawing.Color.Black.ToArgb())
        {
            run.Foreground = new SolidColorBrush(Color.FromRgb(
                textColor.R, textColor.G, textColor.B));
        }

        // Highlight color
        var highlightColor = format.HighlightColor;
        if (highlightColor.ToArgb() != System.Drawing.Color.Empty.ToArgb())
        {
            run.Background = new SolidColorBrush(Color.FromRgb(
                highlightColor.R, highlightColor.G, highlightColor.B));
        }
    }

    private Table ConvertWordTableToFlowTable(WTable wordTable)
    {
        var table = new Table();
        table.BorderBrush = Brushes.Gray;
        table.BorderThickness = new Thickness(1);

        // Get row and column counts
        int rowCount = wordTable.Rows.Count;
        if (rowCount == 0)
            return table;
        int colCount = wordTable.Rows[0].Cells.Count;

        // Add columns
        for (int c = 0; c < colCount; c++)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(100) });
        }

        // Add rows
        for (int r = 0; r < rowCount; r++)
        {
            var rowGroup = new TableRowGroup();
            var row = new TableRow();

            int currentColCount = wordTable.Rows[r].Cells.Count;
            for (int c = 0; c < Math.Min(colCount, currentColCount); c++)
            {
                var cell = wordTable.Rows[r].Cells[c];
                var cellPara = new Paragraph();

                // Apply cell paragraph formatting
                if (cell.Paragraphs.Count > 0)
                {
                    ApplyParagraphFormatting(cellPara, cell.Paragraphs[0]);

                    foreach (var entity in cell.Paragraphs[0].ChildEntities)
                    {
                        if (entity is IWTextRange textRange)
                        {
                            var run = new Run(textRange.Text);
                            ApplyCharacterFormatting(run, textRange);
                            cellPara.Inlines.Add(run);
                        }
                    }
                }

                var tableCell = new TableCell(cellPara)
                {
                    BorderBrush = Brushes.LightGray,
                    BorderThickness = new Thickness(0.5),
                    Padding = new Thickness(4)
                };

                // Apply cell formatting
                ApplyCellFormatting(tableCell, cell, rowIndex: r);

                row.Cells.Add(tableCell);
            }

            rowGroup.Rows.Add(row);
            table.RowGroups.Add(rowGroup);
        }

        // Add spacing after table
        table.Margin = new Thickness(0, 8, 0, 16);

        return table;
    }

    private void ApplyCellFormatting(TableCell tableCell, WTableCell wordCell, int rowIndex)
    {
        // Cell background color
        var cellFormat = wordCell.CellFormat;
        if (cellFormat != null && cellFormat.BackColor.ToArgb() != System.Drawing.Color.Empty.ToArgb())
        {
            tableCell.Background = new SolidColorBrush(Color.FromRgb(
                cellFormat.BackColor.R, cellFormat.BackColor.G, cellFormat.BackColor.B));
        }

        // Bold header row
        if (rowIndex == 0)
        {
            tableCell.FontWeight = FontWeights.Bold;
            if (tableCell.Background == null)
            {
                tableCell.Background = Brushes.LightGray;
            }
        }
    }

    private System.Windows.Controls.Image? ConvertWordPictureToImage(WPicture picture)
    {
        try
        {
            // Get image bytes from Word picture
            var imageBytes = picture.ImageBytes;
            if (imageBytes == null || imageBytes.Length == 0)
                return null;

            // Create BitmapImage from bytes
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = new MemoryStream(imageBytes);
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            // Create Image control
            var image = new System.Windows.Controls.Image
            {
                Source = bitmapImage,
                Stretch = Stretch.Uniform,
                MaxWidth = 600,
                MaxHeight = 400,
                Margin = new Thickness(0, 8, 0, 8)
            };

            return image;
        }
        catch
        {
            return null;
        }
    }

    private void CreatePageWithBlocks(List<Block> blocks, int pageNumber, ItemsControl pagesContainer)
    {
        var flowDocument = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            TextAlignment = TextAlignment.Left,
            PageWidth = 794,
            PageHeight = 1123,
            PagePadding = new Thickness(48)
        };

        foreach (var block in blocks)
        {
            flowDocument.Blocks.Add(block);
        }

        var flowDocViewer = new FlowDocumentScrollViewer
        {
            Document = flowDocument,
            Zoom = 100,
            IsToolBarVisible = false,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(197, 201, 208)) // #C5C9D0
        };

        var pageBorder = new System.Windows.Controls.Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(200, 200, 200)),
            BorderThickness = new Thickness(1),
            Width = 794,
            Margin = new Thickness(0, 0, 0, 16),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Color.FromRgb(0, 0, 0),
                Direction = 315,
                ShadowDepth = 2,
                Opacity = 0.15,
                BlurRadius = 8
            },
            Child = flowDocViewer
        };

        pagesContainer.Items.Add(pageBorder);
    }

    private void ShowExcelNotViewable()
    {
        var excelDocumentBorder = (System.Windows.Controls.Border)FindName("ExcelDocumentBorder");
        excelDocumentBorder!.Visibility = Visibility.Collapsed;
        var pagesContainer = (ItemsControl)FindName("PagesContainer");
        pagesContainer!.Visibility = Visibility.Collapsed;
        FallbackCard.Visibility = Visibility.Visible;
        FallbackMessage.Text = $"Просмотр Excel файлов в приложении не поддерживается.\n\nВы можете открыть этот файл в Excel, нажав кнопку «Открыть в Excel».";
    }

    private void ShowUnsupportedFormat()
    {
        var excelDocumentBorder = (System.Windows.Controls.Border)FindName("ExcelDocumentBorder");
        excelDocumentBorder!.Visibility = Visibility.Collapsed;
        var pagesContainer = (ItemsControl)FindName("PagesContainer");
        pagesContainer!.Visibility = Visibility.Collapsed;
        FallbackCard.Visibility = Visibility.Visible;
        FallbackMessage.Text = $"Формат файла '{_fileExtension}' не поддерживается для просмотра.\nПоддерживаемые форматы: TXT, Word (.doc, .docx), Excel (.xls, .xlsx)";
    }

    private void ShowError(string message)
    {
        var excelDocumentBorder = (System.Windows.Controls.Border)FindName("ExcelDocumentBorder");
        excelDocumentBorder!.Visibility = Visibility.Collapsed;
        var pagesContainer = (ItemsControl)FindName("PagesContainer");
        pagesContainer!.Visibility = Visibility.Collapsed;
        FallbackCard.Visibility = Visibility.Visible;
        FallbackMessage.Text = message;
    }

    // ── Close ───────────────────────────────────────────────────────────────
    private void Close_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDocumentViewer();
    }

    // ── Open in App ────────────────────────────────────────────────────────
    private void UpdateOpenInAppButtonText()
    {
        OpenInAppBtnText.Text = _docType switch
        {
            DocumentType.Word => "Открыть в Word",
            DocumentType.Excel => "Открыть в Excel",
            DocumentType.Text => "Открыть в блокноте",
            _ => "Открыть"
        };
    }

    private void OpenInApp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(_filePath)
            {
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка открытия файла: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Download ────────────────────────────────────────────────────────────
    private void Download_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = _fileName,
                DefaultExt = _fileExtension,
                Filter = $"{_fileExtension.ToUpperInvariant()} files|*{_fileExtension}|All files|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                File.Copy(_filePath, dialog.FileName, true);
                MessageBox.Show("Файл успешно сохранен.", "Успешно",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
