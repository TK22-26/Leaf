namespace Leaf.Models;

/// <summary>
/// Severity-style icon shown next to the message in
/// <see cref="Leaf.Views.FluentMessageBox"/>. Mirrors the glyph the WPF
/// <c>MessageBoxImage</c> enum offers, but expressed in Leaf's own enum
/// so the dialog can pick a Fluent <c>SymbolIcon</c> rather than the
/// Win32 <c>MB_ICONxxx</c> bitmap.
/// </summary>
public enum FluentMessageBoxIcon
{
    /// <summary>No icon column rendered — the message expands to fill its space.</summary>
    None,
    /// <summary>Generic informational message — i-in-a-circle glyph.</summary>
    Information,
    /// <summary>Recoverable warning the user should attend to.</summary>
    Warning,
    /// <summary>An error has occurred and the operation could not complete.</summary>
    Error,
    /// <summary>A question the user must answer (Yes/No prompts default to this).</summary>
    Question
}
