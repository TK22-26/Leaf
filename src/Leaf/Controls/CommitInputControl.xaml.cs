using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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
}
