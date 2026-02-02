namespace Leaf.ViewModels;

/// <summary>
/// Factory for creating transient ViewModel instances.
/// Manages creation and proper dependency injection for ViewModels
/// that should be recreated rather than held as singletons.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle strategy:
/// - MainViewModel: Singleton (root orchestrator, one instance)
/// - GitGraphViewModel: Transient (per-repository, disposed on repo switch)
/// - BranchOperationsViewModel: Transient (contains state, recreate per use)
/// - GitFlowOperationsViewModel: Transient (contains dialog state, recreate per use)
/// - CommitDetailViewModel: Transient (per-commit selection)
/// - WorkingChangesViewModel: Transient (per-repository)
/// - ConflictResolutionViewModel: Transient (per-merge operation)
/// </para>
/// <para>
/// Sub-ViewModels created by this factory are NOT registered in DI directly.
/// MainViewModel calls Dispose() when repository switches or ViewModel is replaced.
/// </para>
/// </remarks>
public interface IViewModelFactory
{
    // Note: Methods will be added as sub-ViewModels are extracted in Phase 3.
    // Currently existing ViewModels (GitGraphViewModel, WorkingChangesViewModel, etc.)
    // are created directly in MainViewModel and will be migrated to use this factory.

    // Phase 3 additions:
    // BranchOperationsViewModel CreateBranchOperations();
    // GitFlowOperationsViewModel CreateGitFlowOperations();

    // Future: Add factory methods as ViewModels are extracted
}
