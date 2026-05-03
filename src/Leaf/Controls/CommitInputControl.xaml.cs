using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;

namespace Leaf.Controls;

/// <summary>
/// A control for entering commit messages with AI auto-fill support.
/// </summary>
public partial class CommitInputControl : UserControl
{
    public static readonly DependencyProperty CommitMessageProperty =
        DependencyProperty.Register(
            nameof(CommitMessage),
            typeof(string),
            typeof(CommitInputControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CommitDescriptionProperty =
        DependencyProperty.Register(
            nameof(CommitDescription),
            typeof(string),
            typeof(CommitInputControl),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty MaxMessageLengthProperty =
        DependencyProperty.Register(
            nameof(MaxMessageLength),
            typeof(int),
            typeof(CommitInputControl),
            new PropertyMetadata(72));

    public static readonly DependencyProperty RemainingCharsProperty =
        DependencyProperty.Register(
            nameof(RemainingChars),
            typeof(int),
            typeof(CommitInputControl),
            new PropertyMetadata(72));

    public static readonly DependencyProperty IsLoadingProperty =
        DependencyProperty.Register(
            nameof(IsLoading),
            typeof(bool),
            typeof(CommitInputControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CanCommitProperty =
        DependencyProperty.Register(
            nameof(CanCommit),
            typeof(bool),
            typeof(CommitInputControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty AutoFillCommandProperty =
        DependencyProperty.Register(
            nameof(AutoFillCommand),
            typeof(ICommand),
            typeof(CommitInputControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty CommitCommandProperty =
        DependencyProperty.Register(
            nameof(CommitCommand),
            typeof(ICommand),
            typeof(CommitInputControl),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IsAiAvailableProperty =
        DependencyProperty.Register(
            nameof(IsAiAvailable),
            typeof(bool),
            typeof(CommitInputControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty IsAmendModeProperty =
        DependencyProperty.Register(
            nameof(IsAmendMode),
            typeof(bool),
            typeof(CommitInputControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty CanAmendProperty =
        DependencyProperty.Register(
            nameof(CanAmend),
            typeof(bool),
            typeof(CommitInputControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty CommitButtonLabelProperty =
        DependencyProperty.Register(
            nameof(CommitButtonLabel),
            typeof(string),
            typeof(CommitInputControl),
            new PropertyMetadata("Commit"));

    public static readonly DependencyProperty IsOptionsExpandedProperty =
        DependencyProperty.Register(
            nameof(IsOptionsExpanded),
            typeof(bool),
            typeof(CommitInputControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    // §5.15 — template list shown in the picker popup. Driven by the
    // VM's CommitTemplates collection; the picker control reads it via
    // its own Templates DP, which is bound here.
    public static readonly DependencyProperty TemplatesProperty =
        DependencyProperty.Register(
            nameof(Templates),
            typeof(IEnumerable<CommitTemplate>),
            typeof(CommitInputControl),
            new PropertyMetadata(null));

    // §5.15 — command invoked when the picker fires. Parameter is a
    // CommitTemplateApplyRequest (template + replace/append mode).
    public static readonly DependencyProperty ApplyTemplateCommandProperty =
        DependencyProperty.Register(
            nameof(ApplyTemplateCommand),
            typeof(ICommand),
            typeof(CommitInputControl),
            new PropertyMetadata(null));

    // §5.15 caret-target DPs. The VM bumps these after writing
    // CommitMessage/CommitDescription so the control can move focus
    // and the cursor to where the {cursor} token resolved.
    public static readonly DependencyProperty TemplateCaretIndexProperty =
        DependencyProperty.Register(
            nameof(TemplateCaretIndex),
            typeof(int),
            typeof(CommitInputControl),
            new PropertyMetadata(-1));

    public static readonly DependencyProperty TemplateCaretInDescriptionProperty =
        DependencyProperty.Register(
            nameof(TemplateCaretInDescription),
            typeof(bool),
            typeof(CommitInputControl),
            new PropertyMetadata(false));

    public static readonly DependencyProperty TemplateApplyTickProperty =
        DependencyProperty.Register(
            nameof(TemplateApplyTick),
            typeof(int),
            typeof(CommitInputControl),
            new PropertyMetadata(0, OnTemplateApplyTickChanged));

    // §5.15 Phase 4 — when true, replace the freeform subject/description
    // pair with a structured Conventional Commits form. Two-way bound to
    // the VM toggle so the persisted-across-launches flip lives in one
    // place.
    public static readonly DependencyProperty UseConventionalCommitsFormProperty =
        DependencyProperty.Register(
            nameof(UseConventionalCommitsForm),
            typeof(bool),
            typeof(CommitInputControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    // §5.15 master toggle. When false, the templates icon button + popup
    // are hidden and the Ctrl+T command becomes a no-op (the popup just
    // never opens). True by default so existing users see no change
    // until they explicitly toggle it off in Settings → Commit Templates.
    public static readonly DependencyProperty IsTemplatesEnabledProperty =
        DependencyProperty.Register(
            nameof(IsTemplatesEnabled),
            typeof(bool),
            typeof(CommitInputControl),
            new PropertyMetadata(true));

    public CommitInputControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The commit message text.
    /// </summary>
    public string CommitMessage
    {
        get => (string)GetValue(CommitMessageProperty);
        set => SetValue(CommitMessageProperty, value);
    }

    /// <summary>
    /// The commit description text.
    /// </summary>
    public string CommitDescription
    {
        get => (string)GetValue(CommitDescriptionProperty);
        set => SetValue(CommitDescriptionProperty, value);
    }

    /// <summary>
    /// Maximum allowed characters for the commit message.
    /// </summary>
    public int MaxMessageLength
    {
        get => (int)GetValue(MaxMessageLengthProperty);
        set => SetValue(MaxMessageLengthProperty, value);
    }

    /// <summary>
    /// Remaining characters before reaching the max message length.
    /// </summary>
    public int RemainingChars
    {
        get => (int)GetValue(RemainingCharsProperty);
        set => SetValue(RemainingCharsProperty, value);
    }

    /// <summary>
    /// Whether an AI auto-fill operation is in progress.
    /// </summary>
    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>
    /// Whether a commit can be made (has staged files and valid message).
    /// </summary>
    public bool CanCommit
    {
        get => (bool)GetValue(CanCommitProperty);
        set => SetValue(CanCommitProperty, value);
    }

    /// <summary>
    /// Command to auto-fill the commit message using AI.
    /// </summary>
    public ICommand? AutoFillCommand
    {
        get => (ICommand?)GetValue(AutoFillCommandProperty);
        set => SetValue(AutoFillCommandProperty, value);
    }

    /// <summary>
    /// Command to perform the commit.
    /// </summary>
    public ICommand? CommitCommand
    {
        get => (ICommand?)GetValue(CommitCommandProperty);
        set => SetValue(CommitCommandProperty, value);
    }

    /// <summary>
    /// Whether any AI provider is connected (controls sparkle button visibility).
    /// </summary>
    public bool IsAiAvailable
    {
        get => (bool)GetValue(IsAiAvailableProperty);
        set => SetValue(IsAiAvailableProperty, value);
    }

    /// <summary>
    /// Whether the next commit should amend HEAD instead of creating a
    /// new commit. Two-way binding — the user toggles the checkbox; the
    /// VM loads HEAD's message into the input when the flag flips.
    /// </summary>
    public bool IsAmendMode
    {
        get => (bool)GetValue(IsAmendModeProperty);
        set => SetValue(IsAmendModeProperty, value);
    }

    /// <summary>
    /// Whether amend is currently allowed: HEAD must exist and must not
    /// already be pushed to the remote. Drives the checkbox's IsEnabled
    /// state and its tooltip.
    /// </summary>
    public bool CanAmend
    {
        get => (bool)GetValue(CanAmendProperty);
        set => SetValue(CanAmendProperty, value);
    }

    /// <summary>
    /// Label on the primary button — "Commit" normally, "Amend" when
    /// <see cref="IsAmendMode"/> is true.
    /// </summary>
    public string CommitButtonLabel
    {
        get => (string)GetValue(CommitButtonLabelProperty);
        set => SetValue(CommitButtonLabelProperty, value);
    }

    /// <summary>
    /// Whether the collapsible "Options" row is expanded, revealing the
    /// amend checkbox. Two-way — persisted at the VM layer so the choice
    /// survives across launches.
    /// </summary>
    public bool IsOptionsExpanded
    {
        get => (bool)GetValue(IsOptionsExpandedProperty);
        set => SetValue(IsOptionsExpandedProperty, value);
    }

    /// <summary>Commit templates available right now (built-ins + user + repo).</summary>
    public IEnumerable<CommitTemplate>? Templates
    {
        get => (IEnumerable<CommitTemplate>?)GetValue(TemplatesProperty);
        set => SetValue(TemplatesProperty, value);
    }

    /// <summary>Command fired by the picker. Parameter is a <see cref="ViewModels.CommitTemplateApplyRequest"/>.</summary>
    public ICommand? ApplyTemplateCommand
    {
        get => (ICommand?)GetValue(ApplyTemplateCommandProperty);
        set => SetValue(ApplyTemplateCommandProperty, value);
    }

    /// <summary>Where the caret should land after a template apply.</summary>
    public int TemplateCaretIndex
    {
        get => (int)GetValue(TemplateCaretIndexProperty);
        set => SetValue(TemplateCaretIndexProperty, value);
    }

    public bool TemplateCaretInDescription
    {
        get => (bool)GetValue(TemplateCaretInDescriptionProperty);
        set => SetValue(TemplateCaretInDescriptionProperty, value);
    }

    /// <summary>Tick bumped by the VM on every successful template apply — DP change drives the caret-restore code path.</summary>
    public int TemplateApplyTick
    {
        get => (int)GetValue(TemplateApplyTickProperty);
        set => SetValue(TemplateApplyTickProperty, value);
    }

    /// <summary>Whether the Conventional Commits structured form replaces the freeform input.</summary>
    public bool UseConventionalCommitsForm
    {
        get => (bool)GetValue(UseConventionalCommitsFormProperty);
        set => SetValue(UseConventionalCommitsFormProperty, value);
    }

    /// <summary>Master toggle for the §5.15 templates UI — controls templates button visibility and the Ctrl+T no-op.</summary>
    public bool IsTemplatesEnabled
    {
        get => (bool)GetValue(IsTemplatesEnabledProperty);
        set => SetValue(IsTemplatesEnabledProperty, value);
    }

    private static void OnTemplateApplyTickChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CommitInputControl ctl) return;
        // Defer the focus + caret move to the next dispatcher pass so the
        // bound CommitMessage/CommitDescription updates are flushed first
        // — otherwise CaretIndex would land in the *previous* string.
        ctl.Dispatcher.BeginInvoke(new Action(ctl.RestoreCaretAfterTemplateApply),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RestoreCaretAfterTemplateApply()
    {
        var idx = TemplateCaretIndex;
        if (idx < 0) return;

        if (TemplateCaretInDescription)
        {
            CommitDescriptionBox.Focus();
            CommitDescriptionBox.CaretIndex = Math.Min(idx, CommitDescriptionBox.Text?.Length ?? 0);
        }
        else
        {
            CommitMessageBox.Focus();
            CommitMessageBox.CaretIndex = Math.Min(idx, CommitMessageBox.Text?.Length ?? 0);
        }
    }

    // ---- §5.15 popup wiring ------------------------------------------

    private void TemplatesButton_Click(object sender, RoutedEventArgs e)
    {
        OpenTemplatesPicker();
    }

    /// <summary>
    /// Open the §5.15 picker. Public because the host window's Ctrl+T
    /// shortcut binding routes here through a command — see
    /// <see cref="OpenTemplatePickerCommand"/>. No-op when the master
    /// toggle is off so the keystroke doesn't surface the popup behind
    /// the user's back even though the icon button is hidden.
    /// </summary>
    public void OpenTemplatesPicker()
    {
        if (!IsTemplatesEnabled) return;
        TemplatesPicker.PrepareForShow();
        TemplatesPopup.IsOpen = true;
    }

    private void TemplatesPicker_CloseRequested(object? sender, EventArgs e)
    {
        TemplatesPopup.IsOpen = false;
    }

    private void TemplatesPicker_ApplyRequested(object? sender, EventArgs e)
    {
        TemplatesPopup.IsOpen = false;
    }

    private void TemplatesPopup_Closed(object sender, EventArgs e)
    {
        // Return focus to the commit message box so the user can continue
        // typing without an extra click. The VM's caret-restore path runs
        // independently; this is only the no-template-applied close.
        if (TemplateCaretIndex < 0)
            CommitMessageBox.Focus();
    }

    /// <summary>
    /// ICommand surface so the host's Ctrl+T <see cref="KeyBinding"/>
    /// can open the picker without reaching into the control's code-
    /// behind. Always-executable; the picker handles "no templates"
    /// internally (renders an empty list).
    /// </summary>
    public ICommand OpenTemplatePickerCommand =>
        _openTemplatePickerCommand ??= new RelayActionCommand(OpenTemplatesPicker);
    private RelayActionCommand? _openTemplatePickerCommand;

    private sealed class RelayActionCommand : ICommand
    {
        private readonly Action _execute;
        public RelayActionCommand(Action execute) { _execute = execute; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged
        {
            add { /* always executable — no source to subscribe to */ }
            remove { }
        }
    }
}
