using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Controls;

/// <summary>
/// §5.15 popup picker — hosted inside <see cref="CommitInputControl"/>'s
/// popup. Renders a searchable list of <see cref="CommitTemplate"/>
/// and reports the user's choice back through the
/// <see cref="ApplyTemplateCommand"/> dependency property.
///
/// <para>Owns no template state — that lives on the parent VM. The
/// picker keeps a transient filtered view + selection in private
/// fields, both reset every time the popup opens.</para>
/// </summary>
public partial class CommitTemplatePicker : UserControl
{
    public static readonly DependencyProperty TemplatesProperty =
        DependencyProperty.Register(
            nameof(Templates),
            typeof(IEnumerable<CommitTemplate>),
            typeof(CommitTemplatePicker),
            new FrameworkPropertyMetadata(null, OnTemplatesChanged));

    public static readonly DependencyProperty ApplyTemplateCommandProperty =
        DependencyProperty.Register(
            nameof(ApplyTemplateCommand),
            typeof(ICommand),
            typeof(CommitTemplatePicker),
            new PropertyMetadata(null));

    /// <summary>Fired when the user dismisses the picker (Esc, focus loss).</summary>
    public event EventHandler? CloseRequested;

    /// <summary>Fired when the user has chosen and applied a template — caller closes the popup.</summary>
    public event EventHandler? ApplyRequested;

    public IEnumerable<CommitTemplate>? Templates
    {
        get => (IEnumerable<CommitTemplate>?)GetValue(TemplatesProperty);
        set => SetValue(TemplatesProperty, value);
    }

    public ICommand? ApplyTemplateCommand
    {
        get => (ICommand?)GetValue(ApplyTemplateCommandProperty);
        set => SetValue(ApplyTemplateCommandProperty, value);
    }

    private readonly ObservableCollection<CommitTemplateRow> _rows = [];

    public CommitTemplatePicker()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _rows;
    }

    /// <summary>
    /// Reset transient state (search text, selection) and rebind the
    /// rows from the current Templates value. Called by the host when
    /// the popup opens — gives the user a clean slate every time.
    /// </summary>
    public void PrepareForShow()
    {
        SearchBox.Text = string.Empty;
        Rebuild();
        SelectFirst();
        // Focus the search box so typing immediately filters. Defer to
        // the dispatcher because the popup is still being measured at
        // the moment PrepareForShow is called.
        Dispatcher.BeginInvoke(new Action(() => SearchBox.Focus()),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void OnTemplatesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CommitTemplatePicker picker) picker.Rebuild();
    }

    private void Rebuild()
    {
        var query = SearchBox?.Text?.Trim();
        _rows.Clear();
        if (Templates is null) return;

        foreach (var template in Templates)
        {
            if (template is null) continue;
            if (!Match(template, query)) continue;
            _rows.Add(new CommitTemplateRow
            {
                Template = template,
                Name = template.Name,
                PreviewLine = FirstNonEmptyLine(template.Body),
                Badge = template.IsBuiltIn
                    ? "built-in"
                    : (template.Scope == CommitTemplateScope.Repository ? "repo" : "custom"),
                ShowBadge = true,
            });
        }
    }

    private static bool Match(CommitTemplate template, string? query)
    {
        if (string.IsNullOrEmpty(query)) return true;
        return template.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || template.Body.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstNonEmptyLine(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        foreach (var line in body.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r').Trim();
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                return trimmed.Length > 80 ? trimmed[..80] + "…" : trimmed;
        }
        return string.Empty;
    }

    private void SelectFirst()
    {
        if (_rows.Count == 0)
        {
            ResultsList.SelectedIndex = -1;
            return;
        }
        ResultsList.SelectedIndex = 0;
        ResultsList.ScrollIntoView(_rows[0]);
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Rebuild();
        SelectFirst();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                if (_rows.Count > 0)
                {
                    var idx = Math.Min(_rows.Count - 1, ResultsList.SelectedIndex + 1);
                    ResultsList.SelectedIndex = idx;
                    ResultsList.ScrollIntoView(_rows[idx]);
                }
                e.Handled = true;
                break;
            case Key.Up:
                if (_rows.Count > 0)
                {
                    var idx = Math.Max(0, ResultsList.SelectedIndex - 1);
                    ResultsList.SelectedIndex = idx;
                    ResultsList.ScrollIntoView(_rows[idx]);
                }
                e.Handled = true;
                break;
            case Key.Enter:
                Apply(GetMode());
                e.Handled = true;
                break;
            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_KeyDown(object sender, KeyEventArgs e)
    {
        // Bubble Enter / Escape into our shared apply / close path even
        // when the list — not the search box — has focus.
        switch (e.Key)
        {
            case Key.Enter:
                Apply(GetMode());
                e.Handled = true;
                break;
            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        Apply(GetMode());
        e.Handled = true;
    }

    private static WorkingChangesViewModel.CommitTemplateApplyMode GetMode() =>
        (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
            ? WorkingChangesViewModel.CommitTemplateApplyMode.Append
            : WorkingChangesViewModel.CommitTemplateApplyMode.Replace;

    private void Apply(WorkingChangesViewModel.CommitTemplateApplyMode mode)
    {
        if (ResultsList.SelectedItem is not CommitTemplateRow row) return;
        var cmd = ApplyTemplateCommand;
        if (cmd is null) return;

        var request = new CommitTemplateApplyRequest(row.Template, mode);
        if (cmd.CanExecute(request))
            cmd.Execute(request);

        ApplyRequested?.Invoke(this, EventArgs.Empty);
    }

    private sealed class CommitTemplateRow
    {
        public CommitTemplate Template { get; init; } = null!;
        public string Name { get; init; } = string.Empty;
        public string PreviewLine { get; init; } = string.Empty;
        public string Badge { get; init; } = string.Empty;
        public bool ShowBadge { get; init; }
    }
}
