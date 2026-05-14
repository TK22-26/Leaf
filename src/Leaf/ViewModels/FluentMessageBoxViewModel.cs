using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using FluentIcons.Common;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// Backs <see cref="Leaf.Views.FluentMessageBox"/>. Exposes the message
/// text, button-set selection (mirroring WPF's <see cref="MessageBoxButton"/>
/// enum so callsites translate cleanly), the severity icon glyph, and the
/// optional "Don't show this again" checkbox state.
/// </summary>
/// <remarks>
/// <para>The view binds button visibility off three booleans
/// (<see cref="ShowOk"/>, <see cref="ShowYes"/>, <see cref="ShowNo"/>,
/// <see cref="ShowCancel"/>) rather than parsing the
/// <see cref="MessageBoxButton"/> enum in XAML. This keeps the view file
/// declarative and avoids a converter just for two-way visibility logic.
/// </para>
/// <para>The checkbox is hidden by default — set
/// <see cref="ShowDoNotShowAgain"/> to <c>true</c> to surface it. The
/// dialog code-behind reads <see cref="DoNotShowAgainChecked"/> after
/// the user clicks a button so the host can persist the answer.</para>
/// </remarks>
public partial class FluentMessageBoxViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IconSymbol))]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private FluentMessageBoxIcon _icon = FluentMessageBoxIcon.None;

    [ObservableProperty]
    private bool _showOk;

    [ObservableProperty]
    private bool _showYes;

    [ObservableProperty]
    private bool _showNo;

    [ObservableProperty]
    private bool _showCancel;

    /// <summary>
    /// True when the dialog should expose the "Don't show this again"
    /// checkbox. The default is <c>false</c> — even with a suppression
    /// key, the host can opt out of surfacing the checkbox if it wants
    /// the dialog to always re-prompt.
    /// </summary>
    [ObservableProperty]
    private bool _showDoNotShowAgain;

    /// <summary>The checkbox's current state. Read by the host after the dialog closes.</summary>
    [ObservableProperty]
    private bool _doNotShowAgainChecked;

    /// <summary>
    /// True when an icon column should render. Lets the view collapse
    /// the icon's grid column entirely when no icon is configured so
    /// the message text gets the full width.
    /// </summary>
    public bool HasIcon => Icon != FluentMessageBoxIcon.None;

    /// <summary>
    /// Maps the typed icon onto a FluentIcons <see cref="Symbol"/>. Picked
    /// to mirror the visual semantics of the standard Win32 message-box
    /// glyphs; Question reuses the same i-circle as Information when the
    /// FluentIcons set has no dedicated question glyph.
    /// </summary>
    public Symbol IconSymbol => Icon switch
    {
        FluentMessageBoxIcon.Information => Symbol.Info,
        FluentMessageBoxIcon.Warning => Symbol.Warning,
        FluentMessageBoxIcon.Error => Symbol.ErrorCircle,
        FluentMessageBoxIcon.Question => Symbol.QuestionCircle,
        _ => Symbol.Info,
    };

    /// <summary>
    /// Pre-populate the visibility flags + icon from the WPF enum the
    /// callsite handed in. Centralising the mapping keeps the static
    /// helper terse and lets tests construct the VM directly without
    /// repeating the table.
    /// </summary>
    public void ApplyButtons(MessageBoxButton buttons)
    {
        ShowOk = buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel;
        ShowYes = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel;
        ShowNo = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel;
        ShowCancel = buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel;
    }
}
