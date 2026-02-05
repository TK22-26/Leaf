using System.Windows;
using System.Windows.Controls.Primitives;
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

        // Phase 0: Architecture Glue (MUST be created first)
        // NOTE: Dispatcher injected at composition root - NOT accessed inside services
        var dispatcherService = new DispatcherService(Dispatcher);
        var windowService = new WindowService();
        var repositorySessionFactory = new RepositorySessionFactory();
        var repositoryEventHub = new RepositoryEventHub(dispatcherService);

        // Phase 1: Foundation services
        var dialogService = new DialogService(dispatcherService, windowService);
        var gitCommandRunner = new GitCommandRunner();
        var clipboardService = new ClipboardService();
        var fileSystemService = new FileSystemService();

        // Original services
        var gitService = new GitService();
        var credentialService = new CredentialService();
        var settingsService = new SettingsService();

        // Migrate legacy credentials to new multi-org format
        settingsService.MigrateCredentialsIfNeeded(credentialService);

        var gitFlowService = new GitFlowService(gitService);
        var repositoryService = new RepositoryManagementService(settingsService);
        var autoFetchService = new AutoFetchService(gitService, credentialService);
        var folderWatcherService = new FolderWatcherService();

        // ViewModelFactory for transient ViewModel creation
        var viewModelFactory = new ViewModelFactory(gitService, dialogService, repositoryEventHub, clipboardService, fileSystemService);

        // Create view model with all services
        var viewModel = new MainViewModel(
            gitService,
            credentialService,
            settingsService,
            gitFlowService,
            repositoryService,
            autoFetchService,
            this,
            dispatcherService,
            repositoryEventHub,
            dialogService,
            repositorySessionFactory,
            gitCommandRunner,
            clipboardService,
            fileSystemService,
            folderWatcherService);

        DataContext = viewModel;
    }

    private void RepoPane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ToggleRepoPaneCommand.Execute(null);
        }
    }

    private void BranchNameInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(viewModel.NewBranchName))
        {
            viewModel.ConfirmCreateBranchCommand.Execute(null);
        }
        else if (e.Key == Key.Escape)
        {
            viewModel.CancelBranchInputCommand.Execute(null);
        }
    }

    private void BranchInputOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.CancelBranchInputCommand.Execute(null);
        }
    }

    private void BranchNameInput_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Git branch name invalid characters: space ~ ^ : ? * [ \ @ { }
        // Also control characters and DEL
        const string invalidChars = " ~^:?*[\\@{}";

        foreach (char c in e.Text)
        {
            // Reject invalid characters
            if (invalidChars.Contains(c) || char.IsControl(c))
            {
                e.Handled = true;
                return;
            }
        }

        // Check for invalid patterns based on current text
        if (sender is System.Windows.Controls.TextBox textBox)
        {
            string currentText = textBox.Text;
            string newText = currentText.Insert(textBox.CaretIndex, e.Text);

            // Cannot start with dot or dash
            if (newText.StartsWith('.') || newText.StartsWith('-'))
            {
                e.Handled = true;
                return;
            }

            // Cannot contain consecutive dots
            if (newText.Contains(".."))
            {
                e.Handled = true;
                return;
            }

            // Cannot contain @{
            if (newText.Contains("@{"))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void BranchNameInput_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is System.Windows.Controls.TextBox textBox)
        {
            // Use Dispatcher to ensure focus happens after the UI is fully rendered
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                textBox.Focus();
                Keyboard.Focus(textBox);
            });
        }
    }

    private void TerminalSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateTerminalHeight(TerminalRow.ActualHeight);
        }
    }

    private void RepoPaneSplitter_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            var newWidth = viewModel.RepoPaneWidth + e.HorizontalChange;
            // Clamp to min/max defined in XAML (150-400)
            newWidth = Math.Max(150, Math.Min(400, newWidth));
            viewModel.RepoPaneWidth = newWidth;
        }
    }

    private void RepoPaneSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.UpdateRepoPaneWidth(RepoPaneGrid.ActualWidth);
        }
    }
}
