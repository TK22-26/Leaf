using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Leaf.Models;
using Leaf.ViewModels;

namespace Leaf.Views;

    /// <summary>
    /// Interaction logic for BranchListView.xaml
    /// </summary>
    public partial class BranchListView : UserControl
    {
        public BranchListView()
        {
            InitializeComponent();
        }

        private void Branch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is BranchInfo branch)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    _ = viewModel.CheckoutBranchAsync(branch);
                }
            }
            e.Handled = true;
        }

        private void Branch_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Just mark handled to prevent tree selection
            e.Handled = true;
        }
    }
