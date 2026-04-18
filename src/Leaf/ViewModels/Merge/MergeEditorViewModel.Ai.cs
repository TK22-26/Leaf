#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models.Merge;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.Utils;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// Phase 5 partial: AI-assisted conflict resolution. Kept in its own file so
/// the core VM stays focused on state + resolution commands, and the MCP /
/// consent flow lives beside its own tests.
/// </summary>
public sealed partial class MergeEditorViewModel
{
    /// <summary>
    /// Raised when the VM wants to ask the user for AI consent before making
    /// the very first MCP call of the session. The view subscribes, shows
    /// the consent dialog, and invokes <see cref="ResumeAiRequestAfterConsentAsync"/>
    /// (or <see cref="CancelPendingAiRequest"/>) with the user's choice.
    /// An event is used rather than a direct IDialogService call so the VM
    /// remains view-agnostic and the unit tests don't need a fake dialog.
    /// </summary>
    public event EventHandler<AiConsentRequest>? AiConsentRequested;

    /// <summary>
    /// Raised when the MCP server returns a proposed resolution. The view
    /// shows the popover; the user then calls
    /// <see cref="AcceptAiResolution"/> or dismisses. An event is used for
    /// the same reason as <see cref="AiConsentRequested"/>.
    /// </summary>
    public event EventHandler<AiResolutionProposal>? AiResolutionReceived;

    /// <summary>
    /// Raised on MCP transport failures (server missing, non-zero exit,
    /// malformed JSON). The view surfaces this as a notification / toast.
    /// Keeping this on the VM (rather than letting exceptions propagate)
    /// avoids routing AI-specific failure text through generic AsyncErrorHandler.
    /// </summary>
    public event EventHandler<string>? AiError;

    /// <summary>Pending request captured while the consent dialog is open.</summary>
    private int? _pendingAiRangeIndex;

    /// <summary>Guard for concurrent AI calls; the UI disables the button while <c>true</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequestAiResolution))]
    private bool _isAiRequestInFlight;

    public bool CanRequestAiResolution => _aiAssistant is not null && Document is not null && !IsAiRequestInFlight;

    /// <summary>
    /// Invoked by the view (or keyboard shortcut) to request an AI resolution
    /// for the <em>current</em> conflict range. If consent hasn't been given
    /// yet, fires <see cref="AiConsentRequested"/> and stores the pending
    /// request. Otherwise calls the assistant straight away.
    /// </summary>
    [RelayCommand]
    private void RequestAiResolution()
    {
        var range = CurrentConflictRange();
        if (range is null) return;
        RequestAiForRange(range.Index);
    }

    /// <summary>
    /// Same as <see cref="RequestAiResolutionCommand"/> but takes an explicit
    /// range index — bound from the per-conflict "Ask AI" button so the user
    /// can invoke it from anywhere, not just the keyboard-current range.
    /// </summary>
    [RelayCommand]
    private void RequestAiResolutionForRange(int rangeIndex) => RequestAiForRange(rangeIndex);

    private void RequestAiForRange(int rangeIndex)
    {
        if (_aiAssistant is null || Document is null) return;
        if (IsAiRequestInFlight) return;

        var range = Document.Ranges.FirstOrDefault(r => r.Index == rangeIndex);
        if (range is null || !range.IsConflicting) return;

        // Consent gate: the MCP server path is what actually receives data,
        // so the dialog should show the configured path as well as the payload
        // shape. The view owns the dialog; we just ask for permission.
        if (!_aiAssistant.IsConsentGiven)
        {
            _pendingAiRangeIndex = rangeIndex;
            AiConsentRequested?.Invoke(this, new AiConsentRequest(
                FilePath: Document.FilePath,
                McpServerPath: _aiAssistant.McpServerPath ?? string.Empty,
                ContextLines: AiContextLines));
            return;
        }

        InvokeAiAsync(rangeIndex).FireAndForget(
            nameof(InvokeAiAsync), isUserAction: true);
    }

    /// <summary>
    /// Called by the view after the user clicks "Accept" in the consent
    /// dialog. Persists consent + re-fires the request that was paused.
    /// </summary>
    public void ResumeAiRequestAfterConsent()
    {
        if (_pendingAiRangeIndex is not int idx) return;
        _pendingAiRangeIndex = null;
        InvokeAiAsync(idx).FireAndForget(
            nameof(InvokeAiAsync), isUserAction: true);
    }

    /// <summary>Called by the view when the user cancels the consent dialog.</summary>
    public void CancelPendingAiRequest() => _pendingAiRangeIndex = null;

    /// <summary>
    /// Number of context lines sent on each side of the conflict. Default 20,
    /// hard-capped at 200 to match the documented privacy contract.
    /// </summary>
    private const int AiContextLines = 20;

    private async Task InvokeAiAsync(int rangeIndex)
    {
        if (_aiAssistant is null || Document is null) return;
        var range = Document.Ranges.FirstOrDefault(r => r.Index == rangeIndex);
        if (range is null) return;

        IsAiRequestInFlight = true;
        try
        {
            var contextBefore = SliceContextBefore(range, AiContextLines);
            var contextAfter = SliceContextAfter(range, AiContextLines);
            var request = new AiResolutionRequest(
                FilePath: Document.FilePath,
                Language: InferLanguage(Document.FilePath),
                BaseLines: range.BaseLines,
                OursLines: range.OursLines,
                TheirsLines: range.TheirsLines,
                ContextBefore: contextBefore,
                ContextAfter: contextAfter);

            AiResolution? result;
            try
            {
                result = await _aiAssistant.RequestResolutionAsync(request, SessionToken)
                    .ConfigureAwait(true);
            }
            catch (AiMergeAssistantException ex)
            {
                AiError?.Invoke(this, ex.Message);
                return;
            }

            if (result is null)
            {
                // Null means feature disabled / consent missing — both handled
                // in the caller, so if we reach here the assistant turned
                // itself off mid-flight. Quietly stop; no error.
                return;
            }

            AiResolutionReceived?.Invoke(this, new AiResolutionProposal(
                RangeIndex: rangeIndex,
                ProposedText: result.ProposedText,
                Rationale: result.Rationale,
                Confidence: result.Confidence));
        }
        finally
        {
            IsAiRequestInFlight = false;
        }
    }

    /// <summary>
    /// Accept the AI's proposed text for the given range. The proposal becomes
    /// a <see cref="ResolutionState.Manual"/> entry (identical machinery to a
    /// hand-typed resolution) — composition, commit gate, and undo all work
    /// without any AI-specific code paths downstream.
    /// </summary>
    public void AcceptAiResolution(int rangeIndex, string proposedText)
    {
        ArgumentNullException.ThrowIfNull(proposedText);
        ApplyManualText(rangeIndex, proposedText);
    }

    private IReadOnlyList<string> SliceContextBefore(ModifiedBaseRange range, int count)
    {
        if (Document is null || count <= 0) return Array.Empty<string>();
        var baseLines = Document.BaseLines;
        var startInclusive = range.Base.StartLine - 1; // 1-based → 0-based
        if (startInclusive <= 0) return Array.Empty<string>();
        var fromInclusive = Math.Max(0, startInclusive - count);
        var length = startInclusive - fromInclusive;
        if (length <= 0) return Array.Empty<string>();
        var slice = new string[length];
        for (int i = 0; i < length; i++) slice[i] = baseLines[fromInclusive + i];
        return slice;
    }

    private IReadOnlyList<string> SliceContextAfter(ModifiedBaseRange range, int count)
    {
        if (Document is null || count <= 0) return Array.Empty<string>();
        var baseLines = Document.BaseLines;
        // LineRange.EndLineExclusive is 1-based and points past the last
        // included line. Convert to 0-based "first line after the range"
        // by subtracting 1. If the range consumes the rest of the file,
        // there's no context to include.
        var afterInclusive0 = range.Base.EndLineExclusive - 1;
        if (afterInclusive0 >= baseLines.Count) return Array.Empty<string>();
        var length = Math.Min(count, baseLines.Count - afterInclusive0);
        if (length <= 0) return Array.Empty<string>();
        var slice = new string[length];
        for (int i = 0; i < length; i++) slice[i] = baseLines[afterInclusive0 + i];
        return slice;
    }

    private static string InferLanguage(string filePath)
    {
        var ext = System.IO.Path.GetExtension(filePath)?.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "cs" => "csharp",
            "xaml" or "xml" or "csproj" or "props" or "targets" => "xml",
            "json" => "json",
            "md" => "markdown",
            "py" => "python",
            "ts" or "tsx" => "typescript",
            "js" or "jsx" => "javascript",
            "go" => "go",
            "rs" => "rust",
            "java" => "java",
            "yml" or "yaml" => "yaml",
            "sql" => "sql",
            "sh" or "bash" => "shell",
            "" or null => "plaintext",
            _ => ext,
        };
    }
}

/// <summary>
/// Payload fired to the view when consent is required. The view formats this
/// into the first-run dialog text — it must show the MCP server path so the
/// user knows where data is headed.
/// </summary>
public sealed record AiConsentRequest(string FilePath, string McpServerPath, int ContextLines);

/// <summary>
/// Payload fired to the view when the MCP server returns a proposed resolution.
/// The view renders the popover and invokes <see cref="MergeEditorViewModel.AcceptAiResolution"/>
/// on user accept.
/// </summary>
public sealed record AiResolutionProposal(
    int RangeIndex,
    string ProposedText,
    string Rationale,
    AiConfidence Confidence);
