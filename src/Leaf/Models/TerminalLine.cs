using System;

namespace Leaf.Models;

public enum TerminalLineKind
{
    Input,
    Output,
    Error,
    Info
}

public sealed class TerminalLine
{
    public TerminalLine(TerminalLineKind kind, string text, DateTime? timestamp = null)
    {
        Kind = kind;
        Text = text ?? string.Empty;
        Timestamp = timestamp ?? DateTime.Now;
    }

    public TerminalLineKind Kind { get; }
    public string Text { get; }
    public DateTime Timestamp { get; }
}
