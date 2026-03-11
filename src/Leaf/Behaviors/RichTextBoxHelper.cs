using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace Leaf.Behaviors;

public static class RichTextBoxHelper
{
    public static readonly DependencyProperty DocumentProperty =
        DependencyProperty.RegisterAttached(
            "Document",
            typeof(FlowDocument),
            typeof(RichTextBoxHelper),
            new PropertyMetadata(null, OnDocumentChanged));

    public static FlowDocument GetDocument(DependencyObject obj) =>
        (FlowDocument)obj.GetValue(DocumentProperty);

    public static void SetDocument(DependencyObject obj, FlowDocument value) =>
        obj.SetValue(DocumentProperty, value);

    private static void OnDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBox rtb)
            rtb.Document = e.NewValue as FlowDocument ?? new FlowDocument();
    }
}
