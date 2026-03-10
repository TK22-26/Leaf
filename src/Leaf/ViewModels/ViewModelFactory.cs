using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// Factory for creating transient ViewModel instances.
/// Holds references to services needed by created ViewModels.
/// </summary>
/// <remarks>
/// ViewModelFactory is a Singleton but creates Transient instances.
/// Each factory method returns a new instance that should be disposed
/// when no longer needed.
/// </remarks>
public class ViewModelFactory : IViewModelFactory
{
    // Note: Additional services will be added as sub-ViewModels are extracted

    public ViewModelFactory(
        IGitService gitService,
        IDialogService dialogService,
        IRepositoryEventHub eventHub,
        IClipboardService clipboardService,
        IFileSystemService fileSystemService)
    {
        ArgumentNullException.ThrowIfNull(gitService);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(eventHub);
        ArgumentNullException.ThrowIfNull(clipboardService);
        ArgumentNullException.ThrowIfNull(fileSystemService);
    }

    // Phase 3 additions:
    // public BranchOperationsViewModel CreateBranchOperations()
    // {
    //     return new BranchOperationsViewModel(_branchService, _dialogService, _eventHub);
    // }
    //
    // public GitFlowOperationsViewModel CreateGitFlowOperations()
    // {
    //     return new GitFlowOperationsViewModel(_gitFlowService, _dialogService, _eventHub);
    // }
}
