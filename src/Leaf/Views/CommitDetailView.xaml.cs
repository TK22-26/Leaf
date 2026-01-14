using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Leaf.ViewModels;

namespace Leaf.Views;

/// <summary>
/// Interaction logic for CommitDetailView.xaml
/// </summary>
public partial class CommitDetailView : UserControl
{
    private DispatcherTimer? _copiedPopupTimer;

    public CommitDetailView()
    {
        InitializeComponent();
    }

    private void WorkingChangesBanner_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CommitDetailViewModel vm)
        {
            vm.SelectWorkingChanges();
        }
    }

    private void CommitSha_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommitDetailViewModel vm)
        {
            vm.CopySha();
            ShowCopiedPopup();
        }
    }

    private void ShowCopiedPopup()
    {
        CopiedPopup.IsOpen = true;

        _copiedPopupTimer?.Stop();
        _copiedPopupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1.5)
        };
        _copiedPopupTimer.Tick += (s, e) =>
        {
            CopiedPopup.IsOpen = false;
            _copiedPopupTimer.Stop();
        };
        _copiedPopupTimer.Start();
    }

    private void ParentSha_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommitDetailViewModel vm)
        {
            vm.NavigateToParent();
        }
    }
}
