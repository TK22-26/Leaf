using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Leaf.ViewModels;

namespace Leaf.Views;

public partial class TerminalView : UserControl
{
    private int _lastLineCount;

    public TerminalView()
    {
        InitializeComponent();
        IsVisibleChanged += TerminalView_IsVisibleChanged;
        Loaded += TerminalView_Loaded;
        Unloaded += TerminalView_Unloaded;
    }

    private void TerminalView_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalViewModel viewModel)
        {
            viewModel.Lines.CollectionChanged += Lines_CollectionChanged;
            viewModel.CommandCompleted += OnCommandCompleted;
            InitializeDocument();
            RebuildDocument(viewModel);
        }
    }

    private void TerminalView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalViewModel viewModel)
        {
            viewModel.Lines.CollectionChanged -= Lines_CollectionChanged;
            viewModel.CommandCompleted -= OnCommandCompleted;
        }
    }

    private void Lines_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is not TerminalViewModel viewModel)
        {
            return;
        }

        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add &&
            e.NewItems != null &&
            e.OldItems == null &&
            viewModel.Lines.Count >= _lastLineCount)
        {
            foreach (var item in e.NewItems)
            {
                if (item is Leaf.Models.TerminalLine line)
                {
                    AppendLine(viewModel, line);
                }
            }
            _lastLineCount = viewModel.Lines.Count;
        }
        else
        {
            RebuildDocument(viewModel);
        }

        if (viewModel.AutoScroll)
        {
            TerminalOutputTextBox.ScrollToEnd();
        }
    }

    private void TerminalView_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(() => TerminalInput.Focus());
        }
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TerminalViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            viewModel.ExecuteCommandCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.InputText = string.Empty;

            e.Handled = true;
        }
    }

    private void TerminalInput_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not TerminalViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            viewModel.NavigateHistory(-1);
            MoveCaretToEnd();
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            viewModel.NavigateHistory(1);
            MoveCaretToEnd();
            e.Handled = true;
        }
    }

    private void MoveCaretToEnd()
    {
        TerminalInput.CaretIndex = TerminalInput.Text.Length;
    }

    private void OnCommandCompleted(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            TerminalInput.Focus();
            Keyboard.Focus(TerminalInput);
            MoveCaretToEnd();
        });
    }

    private void InitializeDocument()
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left
        };
        TerminalOutputTextBox.Document = document;
    }

    private void RebuildDocument(TerminalViewModel viewModel)
    {
        InitializeDocument();
        foreach (var line in viewModel.Lines)
        {
            AppendLine(viewModel, line);
        }
        _lastLineCount = viewModel.Lines.Count;
    }

    private void AppendLine(TerminalViewModel viewModel, Leaf.Models.TerminalLine line)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        var textBrush = ResolveLineBrush(line.Kind);

        if (line.Kind == Leaf.Models.TerminalLineKind.Input &&
            !string.IsNullOrWhiteSpace(viewModel.Prompt) &&
            line.Text.StartsWith(viewModel.Prompt, StringComparison.Ordinal))
        {
            var promptBrush = (Brush)FindResource("AccentFillColorDefaultBrush");
            paragraph.Inlines.Add(new Run(viewModel.Prompt) { Foreground = promptBrush });
            var remaining = line.Text[viewModel.Prompt.Length..];
            if (!string.IsNullOrEmpty(remaining))
            {
                paragraph.Inlines.Add(new Run(remaining) { Foreground = textBrush });
            }
        }
        else
        {
            paragraph.Inlines.Add(new Run(line.Text) { Foreground = textBrush });
        }

        TerminalOutputTextBox.Document.Blocks.Add(paragraph);
    }

    private Brush ResolveLineBrush(Leaf.Models.TerminalLineKind kind)
    {
        return kind switch
        {
            Leaf.Models.TerminalLineKind.Input => (Brush)FindResource("TextFillColorPrimaryBrush"),
            Leaf.Models.TerminalLineKind.Error => new SolidColorBrush(Color.FromRgb(232, 17, 35)),
            Leaf.Models.TerminalLineKind.Info => (Brush)FindResource("TextFillColorSecondaryBrush"),
            _ => (Brush)FindResource("TextFillColorPrimaryBrush")
        };
    }
}
