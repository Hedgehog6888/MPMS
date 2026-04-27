using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

namespace MPMS.Infrastructure;

public static class RichTextHelper
{
    public static readonly DependencyProperty DocumentXamlProperty =
        DependencyProperty.RegisterAttached(
            "DocumentXaml",
            typeof(string),
            typeof(RichTextHelper),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnDocumentXamlChanged));

    public static string GetDocumentXaml(DependencyObject obj) => (string)obj.GetValue(DocumentXamlProperty);
    public static void SetDocumentXaml(DependencyObject obj, string value) => obj.SetValue(DocumentXamlProperty, value);

    private static void OnDocumentXamlChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not RichTextBox richTextBox) return;

        string xaml = (string)e.NewValue;
        
        // Avoid infinite loop
        if (GetIsUpdating(richTextBox)) return;

        if (string.IsNullOrEmpty(xaml))
        {
            richTextBox.Document = new FlowDocument();
            return;
        }

        try
        {
            SetIsUpdating(richTextBox, true);
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(xaml)))
            {
                range.Load(stream, DataFormats.Xaml);
            }
            
            richTextBox.Document = doc;
        }
        catch
        {
            richTextBox.Document = new FlowDocument();
        }
        finally
        {
            SetIsUpdating(richTextBox, false);
        }
    }

    // IsUpdating attached property to prevent re-entry
    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached("IsUpdating", typeof(bool), typeof(RichTextHelper), new PropertyMetadata(false));

    private static bool GetIsUpdating(DependencyObject obj) => (bool)obj.GetValue(IsUpdatingProperty);
    private static void SetIsUpdating(DependencyObject obj, bool value) => obj.SetValue(IsUpdatingProperty, value);

    // Command to handle text change
    public static void RegisterRichTextBox(RichTextBox rtb)
    {
        rtb.TextChanged += (s, e) => UpdateDocumentXaml(rtb);
    }

    public static void UpdateDocumentXaml(RichTextBox rtb)
    {
        if (GetIsUpdating(rtb)) return;

        SetIsUpdating(rtb, true);
        try
        {
            var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            using (var stream = new MemoryStream())
            {
                range.Save(stream, DataFormats.Xaml);
                string xaml = Encoding.UTF8.GetString(stream.ToArray());
                SetDocumentXaml(rtb, xaml);
            }
        }
        finally
        {
            SetIsUpdating(rtb, false);
        }
    }
}
