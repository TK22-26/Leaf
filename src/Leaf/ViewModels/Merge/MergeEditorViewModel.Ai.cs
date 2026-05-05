#nullable enable
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models.Merge;
using Leaf.Services;
using Leaf.Services.Merge;
using Leaf.Utils;

namespace Leaf.ViewModels.Merge;

/// <summary>
/// AI-assisted conflict resolution partial. Kept in its own file so the
/// core VM stays focused on state + resolution commands, and the AI
/// provider / consent flow lives beside its own tests.
/// </summary>
public sealed partial class MergeEditorViewModel
{
    /// <summary>
    /// Raised when the VM wants to ask the user for AI consent before making
    /// the very first AI call of the session. The view subscribes, shows
    /// the consent dialog, and invokes <see cref="ResumeAiRequestAfterConsent"/>
    /// (or <see cref="CancelPendingAiRequest"/>) with the user's choice.
    /// An event is used rather than a direct IDialogService call so the VM
    /// remains view-agnostic and the unit tests don't need a fake dialog.
    /// </summary>
    public event EventHandler<AiConsentRequest>? AiConsentRequested;

    /// <summary>
    /// Raised when the AI provider returns a proposed resolution. The view
    /// shows the popover; the user then calls
    /// <see cref="AcceptAiResolution"/> or dismisses. An event is used for
    /// the same reason as <see cref="AiConsentRequested"/>.
    /// </summary>
    public event EventHandler<AiResolutionProposal>? AiResolutionReceived;

    /// <summary>
    /// Raised on AI transport failures (provider not connected, non-zero
    /// exit, malformed JSON). The view surfaces this as a notification /
    /// toast. Keeping this on the VM (rather than letting exceptions
    /// propagate) avoids routing AI-specific failure text through generic
    /// AsyncErrorHandler.
    /// </summary>
    public event EventHandler<string>? AiError;

    /// <summary>Pending request captured while the consent dialog is open.</summary>
    private int? _pendingAiRangeIndex;

    /// <summary>Guard for concurrent AI calls; the UI disables the button while <c>true</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRequestAiResolution))]
    [NotifyPropertyChangedFor(nameof(AiPendingConflictCount))]
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

        // Consent gate: the configured provider is what actually receives
        // data, so the dialog should show the provider description as well
        // as the payload shape. The view owns the dialog; we just ask for
        // permission.
        if (!_aiAssistant.IsConsentGiven)
        {
            _pendingAiRangeIndex = rangeIndex;
            AiConsentRequested?.Invoke(this, new AiConsentRequest(
                FilePath: Document.FilePath,
                ProviderDescription: _aiAssistant.ProviderDescription,
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
    /// Number of context lines sent on each side of the conflict. Fixed at
    /// 20 — small enough that the privacy contract documented in the consent
    /// dialog is truthful without a secondary enforcement cap.
    /// </summary>
    /// <remarks>
    /// If this ever becomes user-configurable, clamp the slice methods at
    /// <see cref="AiContextLinesMax"/> to keep the "never more than the
    /// documented window" guarantee.
    /// </remarks>
    private const int AiContextLines = 20;
    private const int AiContextLinesMax = 200;

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
        if (Document is null) return Array.Empty<string>();
        // LineRange.StartLine is 1-based; convert to the 0-based index of the
        // first line inside the range. Slice the `count` lines immediately
        // before it, clamped to the start of the file.
        var firstInsideRange0 = range.Base.StartLine - 1;
        return SliceBaseLines(firstInsideRange0 - CapContext(count), firstInsideRange0);
    }

    private IReadOnlyList<string> SliceContextAfter(ModifiedBaseRange range, int count)
    {
        if (Document is null) return Array.Empty<string>();
        // LineRange.EndLineExclusive is 1-based and points past the last
        // included line; subtracting 1 gives the 0-based index of the first
        // line after the range. Slice the `count` lines starting there,
        // clamped to the end of the file.
        var firstAfterRange0 = range.Base.EndLineExclusive - 1;
        return SliceBaseLines(firstAfterRange0, firstAfterRange0 + CapContext(count));
    }

    /// <summary>
    /// Enforce the documented privacy cap. Called on every slice boundary so
    /// that even if <see cref="AiContextLines"/> becomes configurable, no path
    /// can send more than <see cref="AiContextLinesMax"/> lines per side.
    /// </summary>
    private static int CapContext(int requested) =>
        requested <= 0 ? 0 : Math.Min(requested, AiContextLinesMax);

    /// <summary>
    /// Return the half-open slice of <c>BaseLines[from, toExclusive)</c> with
    /// both bounds clamped to the file. Empty result when the clamped range
    /// is empty. Pulled into a single helper so the two slice methods above
    /// don't diverge.
    /// </summary>
    private IReadOnlyList<string> SliceBaseLines(int from, int toExclusive)
    {
        if (Document is null) return Array.Empty<string>();
        var baseLines = Document.BaseLines;
        var start = Math.Max(0, from);
        var end = Math.Min(baseLines.Count, toExclusive);
        if (end <= start) return Array.Empty<string>();
        var slice = new string[end - start];
        for (int i = 0; i < slice.Length; i++) slice[i] = baseLines[start + i];
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
/// Payload fired to the view when consent is required. The view formats
/// this into the first-run dialog text — it must show the AI provider
/// description so the user knows where data is headed.
/// </summary>
public sealed record AiConsentRequest(string FilePath, string ProviderDescription, int ContextLines);

/// <summary>
/// Payload fired to the view when the AI provider returns a proposed
/// resolution. The view renders the popover and invokes
/// <see cref="MergeEditorViewModel.AcceptAiResolution"/> on user accept.
/// </summary>
public sealed record AiResolutionProposal(
    int RangeIndex,
    string ProposedText,
    string Rationale,
    AiConfidence Confidence);
