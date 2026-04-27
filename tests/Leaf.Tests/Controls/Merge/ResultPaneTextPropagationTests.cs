#nullable enable
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Leaf.Controls.Merge;
using Xunit;

namespace Leaf.Tests.Controls.Merge;

/// <summary>
/// Diagnoses Text DP -> AvalonEdit Document.Text propagation in the
/// merge ResultPane. The user reported the Result pane appearing empty
/// even with a valid Document; this test pins the contract that setting
/// the Text DP populates the inner editor's Document.Text.
/// </summary>
public class ResultPaneTextPropagationTests
{
    [StaFact]
    public void SettingTextDP_PropagatesToInnerEditorDocument()
    {
        MergePaletteTestFixture.Ensure();
        var pane = new ResultPane();
        pane.Text = "line one\nline two\nline three";

        // Reflect onto _editor to inspect inner state. The DP setter is
        // expected to push Text into _editor.Document.Text.
        var editorField = typeof(ResultPane).GetField("_editor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        editorField.Should().NotBeNull(because: "ResultPane caches its inner TextEditor in a private _editor field");
        var editor = editorField!.GetValue(pane);
        editor.Should().NotBeNull();

        var documentProperty = editor!.GetType().GetProperty("Document");
        var document = documentProperty!.GetValue(editor);
        document.Should().NotBeNull(because: "AvalonEdit's TextEditor self-initialises Document in its ctor");

        var textProperty = document!.GetType().GetProperty("Text");
        var text = textProperty!.GetValue(document) as string;
        text.Should().Be("line one\nline two\nline three",
            because: "Text DP -> _editor.Document.Text is the single load-bearing propagation path");
    }

    [StaFact]
    public void SettingTextDP_WithEmptyString_DoesNotCrash()
    {
        MergePaletteTestFixture.Ensure();
        var pane = new ResultPane();
        var act = () => { pane.Text = string.Empty; };
        act.Should().NotThrow();
    }

    [StaFact]
    public void Foreground_PropagatesToInnerEditor()
    {
        MergePaletteTestFixture.Ensure();
        var pane = new ResultPane();
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Magenta);
        pane.SetValue(Control.ForegroundProperty, brush);

        var editorField = typeof(ResultPane).GetField("_editor",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var editor = editorField!.GetValue(pane);
        var foregroundProperty = editor!.GetType().GetProperty("Foreground");
        var actualBrush = foregroundProperty!.GetValue(editor);
        actualBrush.Should().Be(brush,
            because: "ResultPane.OnForegroundChanged forwards Foreground assignments into _editor.Foreground");
    }
}
