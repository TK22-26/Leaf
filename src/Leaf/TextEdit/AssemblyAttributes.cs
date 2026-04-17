#nullable disable
// Vendored from ICSharpCode.AvalonEdit under MIT license.
// See LICENSE-AvalonEdit.txt at the repository root for the full notice.

using System.Windows;
using System.Windows.Markup;

// Preserve the AvalonEdit XAML namespace URI so existing Leaf views using
// xmlns:avalonedit="http://icsharpcode.net/sharpdevelop/avalonedit" continue to
// resolve after the NuGet package is removed. The URI is a stable identifier,
// not a trademark claim — it avoids churning every Leaf XAML file that embeds
// a TextEditor.
[assembly: XmlnsPrefix("http://icsharpcode.net/sharpdevelop/avalonedit", "avalonedit")]
[assembly: XmlnsDefinition("http://icsharpcode.net/sharpdevelop/avalonedit", "Leaf.TextEdit")]
[assembly: XmlnsDefinition("http://icsharpcode.net/sharpdevelop/avalonedit", "Leaf.TextEdit.Editing")]
[assembly: XmlnsDefinition("http://icsharpcode.net/sharpdevelop/avalonedit", "Leaf.TextEdit.Rendering")]
[assembly: XmlnsDefinition("http://icsharpcode.net/sharpdevelop/avalonedit", "Leaf.TextEdit.Highlighting")]
// (XmlnsDefinition for Leaf.TextEdit.Search was removed in Phase 2b — the
// Search subsystem was stripped as unused.)

// Note: ThemeInfo is declared once in Leaf's main AssemblyInfo with
// SourceAssembly location — that covers both Leaf's own themes and the
// vendored TextEdit/themes/generic.xaml, so we don't declare it again here.
