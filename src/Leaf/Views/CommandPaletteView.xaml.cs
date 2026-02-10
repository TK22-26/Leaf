using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.ViewModels;

namespace Leaf.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private CommandPaletteViewModel? ViewModel => DataContext as CommandPaletteViewModel;

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            // Defer focus to after the layout pass
            Dispatcher.BeginInvoke(() =>
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                ViewModel?.MoveUp();
                e.Handled = true;
                break;

            case Key.Down:
                ViewModel?.MoveDown();
                e.Handled = true;
                break;

            case Key.Enter:
                ViewModel?.Confirm();
                e.Handled = true;
                break;

            case Key.Escape:
                ViewModel?.HandleEscape();
                e.Handled = true;
                break;
        }
    }

    private void Backdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        ViewModel?.Close();
        e.Handled = true;
    }

    private void Card_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent click on the card from propagating to the backdrop
        e.Handled = true;
    }

    private void ListBoxItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is CommandPaletteItem)
        {
            // The ListBox selection binding already updates SelectedResult
            ViewModel?.Confirm();
            e.Handled = true;
        }
    }
}
