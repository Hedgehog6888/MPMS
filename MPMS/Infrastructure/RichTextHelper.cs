using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace MPMS.Infrastructure;

/// <summary>
/// Хелпер для RichTextBox:
/// - DocumentXaml привязывается ТОЛЬКО в одну сторону: ViewModel → View (загрузка контента).
/// - Сериализация RTB → строка вызывается вручную при сохранении через ReadDocumentXaml().
/// - Никакой автоматической сериализации при вводе текста нет.
/// </summary>
public static class RichTextHelper
{
    public static readonly DependencyProperty DocumentXamlProperty =
        DependencyProperty.RegisterAttached(
            "DocumentXaml",
            typeof(string),
            typeof(RichTextHelper),
            new FrameworkPropertyMetadata(string.Empty, OnDocumentXamlChanged));

    public static string GetDocumentXaml(DependencyObject obj) => (string)obj.GetValue(DocumentXamlProperty);
    public static void SetDocumentXaml(DependencyObject obj, string value) => obj.SetValue(DocumentXamlProperty, value);

    /// <summary>Срабатывает только когда ViewModel меняет CurrentNoteXaml — загружает документ в RTB.</summary>
    private static void OnDocumentXamlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox rtb) return;

        var xaml = (string)e.NewValue;

        if (string.IsNullOrEmpty(xaml))
        {
            rtb.Document = new FlowDocument();
            return;
        }

        try
        {
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml));
            range.Load(stream, DataFormats.Xaml);
            rtb.Document = doc;
        }
        catch
        {
            rtb.Document = new FlowDocument();
        }
    }

    /// <summary>
    /// Читает текущее содержимое RTB как XAML-строку.
    /// Вызывается ТОЛЬКО при сохранении заметки — не при вводе текста.
    /// </summary>
    public static string ReadDocumentXaml(RichTextBox rtb)
    {
        var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.Xaml);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
