using System;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;

namespace Leaf.ViewModels;

/// <summary>
/// MainViewModel partial - Branch filtering operations (hide/solo/filter).
/// </summary>
public partial class MainViewModel
{
    private void ApplyBranchFiltersForRepo(RepositoryInfo repo)
    {
        if (GitGraphViewModel == null)
        {
            return;
        }

        var branchTips = repo.LocalBranches
            .Concat(repo.RemoteBranches)
            .GroupBy(GetBranchFilterName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().TipSha, StringComparer.OrdinalIgnoreCase);

        GitGraphViewModel.ApplyBranchFilters(repo.HiddenBranchNames, repo.SoloBranchNames, branchTips);
        UpdateBranchFilterFlags(repo);
        IsBranchFilterActive = repo.HiddenBranchNames.Count > 0 || repo.SoloBranchNames.Count > 0;
    }

    private static string GetBranchFilterName(BranchInfo branch)
    {
        if (branch.IsRemote && !string.IsNullOrWhiteSpace(branch.RemoteName))
        {
            if (branch.Name.StartsWith($"{branch.RemoteName}/", StringComparison.OrdinalIgnoreCase))
            {
                return branch.Name;
            }

            return $"{branch.RemoteName}/{branch.Name}";
        }

        return branch.Name;
    }

    private static IEnumerable<BranchInfo> GetAllBranchItems(RepositoryInfo repo)
    {
        var seen = new HashSet<BranchInfo>();

        foreach (var branch in repo.LocalBranches)
        {
            if (seen.Add(branch))
            {
                yield return branch;
            }
        }

        foreach (var branch in repo.RemoteBranches)
        {
            if (seen.Add(branch))
            {
                yield return branch;
            }
        }

        foreach (var category in repo.BranchCategories)
        {
            foreach (var branch in category.Branches)
            {
                if (seen.Add(branch))
                {
                    yield return branch;
                }
            }

            foreach (var group in category.RemoteGroups)
            {
                foreach (var branch in group.Branches)
                {
                    if (seen.Add(branch))
                    {
                        yield return branch;
                    }
                }
            }
        }
    }

    private void UpdateBranchFilterFlags(RepositoryInfo repo)
    {
        var hidden = new HashSet<string>(repo.HiddenBranchNames, StringComparer.OrdinalIgnoreCase);
        var solo = new HashSet<string>(repo.SoloBranchNames, StringComparer.OrdinalIgnoreCase);

        foreach (var branch in GetAllBranchItems(repo))
        {
            var filterName = GetBranchFilterName(branch);
            branch.IsHidden = hidden.Contains(filterName);
            branch.IsSolo = solo.Contains(filterName);
        }
    }

    [RelayCommand]
    public void HideSelectedBranches()
    {
        if (SelectedRepository == null || SelectedRepository.SelectedBranches.Count == 0)
        {
            return;
        }

        foreach (var branch in SelectedRepository.SelectedBranches)
        {
            var filterName = GetBranchFilterName(branch);
            if (!SelectedRepository.HiddenBranchNames.Contains(filterName, StringComparer.OrdinalIgnoreCase))
            {
                SelectedRepository.HiddenBranchNames.Add(filterName);
            }
            SelectedRepository.SoloBranchNames.RemoveAll(n => n.Equals(filterName, StringComparison.OrdinalIgnoreCase));
        }

        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }

    [RelayCommand]
    public void SoloSelectedBranches()
    {
        if (SelectedRepository == null || SelectedRepository.SelectedBranches.Count == 0)
        {
            return;
        }

        foreach (var branch in SelectedRepository.SelectedBranches)
        {
            var filterName = GetBranchFilterName(branch);
            if (!SelectedRepository.SoloBranchNames.Contains(filterName, StringComparer.OrdinalIgnoreCase))
            {
                SelectedRepository.SoloBranchNames.Add(filterName);
            }
            SelectedRepository.HiddenBranchNames.RemoveAll(n => n.Equals(filterName, StringComparison.OrdinalIgnoreCase));
        }

        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }

    [RelayCommand]
    public void ClearHiddenBranches()
    {
        if (SelectedRepository == null)
        {
            return;
        }

        SelectedRepository.HiddenBranchNames.Clear();
        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }

    [RelayCommand]
    public void ClearSoloBranches()
    {
        if (SelectedRepository == null)
        {
            return;
        }

        SelectedRepository.SoloBranchNames.Clear();
        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }

    [RelayCommand]
    public void ClearBranchFilters()
    {
        if (SelectedRepository == null)
        {
            return;
        }

        SelectedRepository.HiddenBranchNames.Clear();
        SelectedRepository.SoloBranchNames.Clear();
        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }

    [RelayCommand]
    public void ToggleHideBranch(BranchInfo branch)
    {
        if (SelectedRepository == null)
        {
            return;
        }

        var hidden = SelectedRepository.HiddenBranchNames;
        var filterName = GetBranchFilterName(branch);
        if (hidden.RemoveAll(n => n.Equals(filterName, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            hidden.Add(filterName);
            SelectedRepository.SoloBranchNames.RemoveAll(n => n.Equals(filterName, StringComparison.OrdinalIgnoreCase));
        }

        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }

    [RelayCommand]
    public void ToggleSoloBranch(BranchInfo branch)
    {
        if (SelectedRepository == null)
        {
            return;
        }

        var solo = SelectedRepository.SoloBranchNames;
        var filterName = GetBranchFilterName(branch);
        if (solo.RemoveAll(n => n.Equals(filterName, StringComparison.OrdinalIgnoreCase)) == 0)
        {
            solo.Add(filterName);
            SelectedRepository.HiddenBranchNames.RemoveAll(n => n.Equals(filterName, StringComparison.OrdinalIgnoreCase));
        }

        _repositoryService.SaveRepositories();
        ApplyBranchFiltersForRepo(SelectedRepository);
    }
}
