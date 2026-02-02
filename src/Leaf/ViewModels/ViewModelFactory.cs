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
    private readonly IGitService _gitService;
    private readonly IDialogService _dialogService;
    private readonly IRepositoryEventHub _eventHub;

    // Note: Additional services will be added as sub-ViewModels are extracted

    public ViewModelFactory(
        IGitService gitService,
        IDialogService dialogService,
        IRepositoryEventHub eventHub)
    {
        _gitService = gitService ?? throw new ArgumentNullException(nameof(gitService));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _eventHub = eventHub ?? throw new ArgumentNullException(nameof(eventHub));
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
