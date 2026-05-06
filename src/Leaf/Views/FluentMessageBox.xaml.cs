using System.Windows;
using Leaf.Models;
using Leaf.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Leaf.Views;

/// <summary>
/// Fluent-styled drop-in replacement for <see cref="MessageBox"/>. Use the
/// <see cref="Show(string, string, MessageBoxButton, FluentMessageBoxIcon, Window?)"/>
/// overloads anywhere the codebase currently calls
/// <see cref="MessageBox.Show(string)"/>; the API surface is intentionally
/// the same shape so the migration is mechanical.
/// </summary>
/// <remarks>
/// <para><b>Suppression:</b> when called with a non-null
/// <c>suppressionKey</c>, the box surfaces a "Don't show this again"
/// checkbox. If the user previously checked it, subsequent calls bypass
/// the dialog entirely and return the remembered answer. Persisted via
/// <see cref="Services.SettingsService.SetSuppressedAnswer"/>.</para>
///
/// <para><b>Owner resolution:</b> when no owner is supplied we fall back
/// to <see cref="Application.MainWindow"/>; matches the contract of the
/// stock WPF <see cref="MessageBox"/>. Tests run without an Application
/// instance, so the static helper is a thin wrapper that
/// <see cref="ViewModels.FluentMessageBoxViewModel"/> tests bypass.</para>
/// </remarks>
public partial class FluentMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    public FluentMessageBox()
    {
        InitializeComponent();
    }

    /// <summary>VM accessor — exposed so the static helper can read DoNotShowAgainChecked after ShowDialog.</summary>
    private FluentMessageBoxViewModel Vm => (FluentMessageBoxViewModel)DataContext;

    private void Ok_Click(object sender, RoutedEventArgs e) => Close(MessageBoxResult.OK);
    private void Yes_Click(object sender, RoutedEventArgs e) => Close(MessageBoxResult.Yes);
    private void No_Click(object sender, RoutedEventArgs e) => Close(MessageBoxResult.No);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close(MessageBoxResult.Cancel);

    private void Close(MessageBoxResult result)
    {
        _result = result;
        DialogResult = result is MessageBoxResult.OK or MessageBoxResult.Yes;
        base.Close();
    }

    // ─── Static API ───────────────────────────────────────────────────────

    /// <summary>
    /// Fluent-styled equivalent of <see cref="MessageBox.Show(string)"/>.
    /// Marshals to the UI thread and blocks until the user dismisses it;
    /// matches the synchronous contract callers expect from WPF's
    /// MessageBox so the migration of the 50+ callsites is a one-token swap.
    /// </summary>
    public static MessageBoxResult Show(
        string message,
        string title = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.None,
        Window? owner = null)
        => ShowCore(owner, message, title, buttons, icon, suppressionKey: null);

    /// <summary>
    /// Owner-first overload mirroring
    /// <see cref="MessageBox.Show(Window, string)"/>. Many callsites pass
    /// <c>this</c> or <see cref="Window.GetWindow(System.Windows.DependencyObject)"/>
    /// as the first arg; this overload keeps that pattern.
    /// </summary>
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.None)
        => ShowCore(owner, message, title, buttons, icon, suppressionKey: null);

    /// <summary>
    /// Show with a "Don't show this again" checkbox. When the user has
    /// previously checked it, returns the remembered answer immediately
    /// without showing UI. Use stable keys (e.g. <c>"branch.forceDelete"</c>)
    /// — they end up in <c>%APPDATA%\Leaf\settings.json</c>.
    /// </summary>
    public static MessageBoxResult ShowSuppressible(
        string message,
        string title,
        string suppressionKey,
        MessageBoxButton buttons = MessageBoxButton.YesNo,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.Question,
        Window? owner = null)
        => ShowCore(owner, message, title, buttons, icon, suppressionKey);

    /// <summary>
    /// Async wrapper around <see cref="ShowCore"/> for callsites that
    /// already <c>await</c> a dialog. The work runs on the dispatcher
    /// thread either way — this is sugar for the IDialogService bridge,
    /// not a real I/O optimisation.
    /// </summary>
    public static Task<MessageBoxResult> ShowAsync(
        string message,
        string title = "",
        MessageBoxButton buttons = MessageBoxButton.OK,
        FluentMessageBoxIcon icon = FluentMessageBoxIcon.None,
        Window? owner = null,
        string? suppressionKey = null)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return Task.FromResult(ShowCore(owner, message, title, buttons, icon, suppressionKey));
        }
        return dispatcher.InvokeAsync(() => ShowCore(owner, message, title, buttons, icon, suppressionKey)).Task;
    }

    private static MessageBoxResult ShowCore(
        Window? owner,
        string message,
        string title,
        MessageBoxButton buttons,
        FluentMessageBoxIcon icon,
        string? suppressionKey)
    {
        // Honour a previously-set suppression: the user already told us
        // their answer, so don't re-prompt. Returning the remembered
        // value lets the caller treat the call as a no-op user-confirm.
        if (!string.IsNullOrWhiteSpace(suppressionKey))
        {
            var remembered = TryGetSettings()?.GetSuppressedAnswer(suppressionKey);
            if (remembered.HasValue)
            {
                return ResolveSuppressedResult(buttons, remembered.Value);
            }
        }

        var vm = new FluentMessageBoxViewModel
        {
            Title = string.IsNullOrEmpty(title) ? "Leaf" : title,
            Message = message ?? string.Empty,
            Icon = icon,
            ShowDoNotShowAgain = !string.IsNullOrWhiteSpace(suppressionKey),
        };
        vm.ApplyButtons(buttons);

        var window = new FluentMessageBox
        {
            DataContext = vm,
            Owner = owner ?? ResolveOwner(),
        };
        window.ShowDialog();

        // Persist the user's answer when they checked the box. We treat
        // OK / Yes as "true" and No / Cancel as "false"; the caller's
        // ResolveSuppressedResult mapping above mirrors this.
        if (vm.DoNotShowAgainChecked && !string.IsNullOrWhiteSpace(suppressionKey))
        {
            var positive = window._result is MessageBoxResult.OK or MessageBoxResult.Yes;
            TryGetSettings()?.SetSuppressedAnswer(suppressionKey, positive);
        }

        return window._result;
    }

    /// <summary>
    /// Translate a stored Yes/No answer back into the right
    /// <see cref="MessageBoxResult"/> for the configured button set —
    /// e.g. an OK-only dialog with a "true" memory still returns OK,
    /// not Yes, so the calling code's switch statement matches the
    /// shape of the dialog it asked to show.
    /// </summary>
    private static MessageBoxResult ResolveSuppressedResult(MessageBoxButton buttons, bool positive)
    {
        return buttons switch
        {
            MessageBoxButton.OK => MessageBoxResult.OK,
            MessageBoxButton.OKCancel => positive ? MessageBoxResult.OK : MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => positive ? MessageBoxResult.Yes : MessageBoxResult.No,
            MessageBoxButton.YesNoCancel => positive ? MessageBoxResult.Yes : MessageBoxResult.No,
            _ => positive ? MessageBoxResult.OK : MessageBoxResult.Cancel,
        };
    }

    private static Window? ResolveOwner()
    {
        var app = Application.Current;
        if (app == null) return null;
        // MainWindow may not be assignable yet during very early startup;
        // fall back to whichever window is currently active so the dialog
        // always anchors somewhere visible.
        return app.MainWindow ?? app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
    }

    /// <summary>
    /// Resolve the SettingsService from the live DI provider, returning
    /// null when no provider is built (test contexts, very early startup).
    /// Suppression silently no-ops in that case — better than throwing
    /// from a UI helper.
    /// </summary>
    private static Services.SettingsService? TryGetSettings()
    {
        try
        {
            return App.Services.GetService<Services.SettingsService>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
