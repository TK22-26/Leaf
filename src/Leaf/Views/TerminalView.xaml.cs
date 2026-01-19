using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.ViewModels;

namespace Leaf.Views;

public partial class TerminalView : UserControl
{
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
        }
    }

    private void TerminalView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TerminalViewModel viewModel)
        {
            viewModel.Lines.CollectionChanged -= Lines_CollectionChanged;
        }
    }

    private void Lines_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DataContext is TerminalViewModel viewModel && viewModel.AutoScroll)
        {
            TerminalScrollViewer.ScrollToEnd();
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
        else if (e.Key == Key.Up)
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
        else if (e.Key == Key.Escape)
        {
            viewModel.InputText = string.Empty;

            e.Handled = true;
        }
    }

    private void MoveCaretToEnd()
    {
        TerminalInput.CaretIndex = TerminalInput.Text.Length;
    }
}
