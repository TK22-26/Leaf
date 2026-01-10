using System.Windows;
using System.Windows.Input;
using Leaf.Services;
using Leaf.ViewModels;

namespace Leaf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Set up services
        var gitService = new GitService();
        var credentialService = new CredentialService();
        var settingsService = new SettingsService();

        // Create view model with all services
        var viewModel = new MainViewModel(gitService, credentialService, settingsService, this);

        DataContext = viewModel;
    }

    private void RepoPane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleRepoPaneCommand.Execute(null);
        }
    }
}
