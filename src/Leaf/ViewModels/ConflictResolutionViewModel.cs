using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

/// <summary>
/// ViewModel for the enhanced merge conflict resolution view.
/// Supports per-hunk and per-line conflict resolution with auto-merge.
/// </summary>
public partial class ConflictResolutionViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly IClipboardService _clipboardService;
    private readonly IThreeWayMergeService _mergeService;
    private readonly string _repoPath;
    private int _currentRegionIndex = -1;

    public event EventHandler<int>? RequestScrollToRegion;

    [ObservableProperty]
    private string _sourceBranch = string.Empty;

    [ObservableProperty]
    private string _targetBranch = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflicts = [];

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _conflictedConflicts = [];

    [ObservableProperty]
    private ObservableCollection<ConflictInfo> _resolvedConflicts = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConflict))]
    [NotifyPropertyChangedFor(nameof(HasUnresolvedConflicts))]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    private ConflictInfo? _selectedConflict;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnresolvedConflicts))]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    private FileMergeResult? _currentMergeResult;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMarkResolved))]
    private string _mergedContent = string.Empty;

    [ObservableProperty]
    private ObservableCollection<MergedLine> _mergedLines = [];

    [ObservableProperty]
    private ObservableCollection<ConflictDisplayLine> _oursDisplayLines = [];

    [ObservableProperty]
    private ObservableCollection<ConflictDisplayLine> _theirsDisplayLines = [];

    private readonly HashSet<SelectableLine> _wiredSelectableLines = [];
    private readonly HashSet<MergedLine> _wiredMergedLines = [];
    private readonly HashSet<SelectableLine> _wiredDisplayLines = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isResolving;

    /// <summary>
    /// Number of resolved conflicts.
    /// </summary>
    public int ResolvedCount => Conflicts.Count(c => c.IsResolved);

    /// <summary>
    /// Total number of conflicts.
    /// </summary>
    public int TotalCount => Conflicts.Count;

    /// <summary>
    /// Number of remaining (unresolved) conflicts.
    /// </summary>
    public int RemainingCount => TotalCount - ResolvedCount;

    /// <summary>
    /// True if all conflicts have been resolved.
    /// </summary>
    public bool CanCompleteMerge => Conflicts.Count > 0 && Conflicts.All(c => c.IsResolved);

    /// <summary>
    /// True if a conflict is selected.
    /// </summary>
    public bool HasSelectedConflict => SelectedConflict != null;

    /// <summary>
    /// True if there are unresolved conflict regions in the current file.
    /// </summary>
    public bool HasUnresolvedConflicts => CurrentMergeResult?.UnresolvedCount > 0;

    /// <summary>
    /// True if the current file can be marked as resolved.
    /// </summary>
    public bool CanMarkResolved => (CurrentMergeResult?.IsFullyResolved == true) || IsMergedContentResolved;

    private bool IsMergedContentResolved => !string.IsNullOrWhiteSpace(MergedContent) && !ContainsConflictMarkers(MergedContent);

    /// <summary>
    /// Event raised when the merge is completed.
    /// </summary>
    public event EventHandler<bool>? MergeCompleted;

    public ConflictResolutionViewModel(IGitService gitService, IClipboardService clipboardService, string repoPath)
        : this(gitService, clipboardService, new ThreeWayMergeService(), repoPath)
    {
    }

    public ConflictResolutionViewModel(
        IGitService gitService,
        IClipboardService clipboardService,
        IThreeWayMergeService mergeService,
        string repoPath)
    {
        _gitService = gitService;
        _clipboardService = clipboardService;
        _mergeService = mergeService;
        _repoPath = repoPath;
    }

    /// <summary>
    /// Load conflicts from the repository.
    /// </summary>
    public async Task LoadConflictsAsync(bool showLoading = true)
    {
        try
        {
            if (showLoading)
            {
                IsLoading = true;
            }
            System.Diagnostics.Debug.WriteLine($"[ConflictVM] LoadConflictsAsync repo={_repoPath}");
            var latestConflicts = await _gitService.GetConflictsAsync(_repoPath);
            foreach (var conflict in latestConflicts)
            {
                conflict.IsResolved = false;
            }

            var resolvedFiles = await _gitService.GetResolvedMergeFilesAsync(_repoPath);
            System.Diagnostics.Debug.WriteLine($"[ConflictVM] conflicts={latestConflicts.Count} resolved={resolvedFiles.Count}");

            var latestByPath = new Dictionary<string, ConflictInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var conflict in latestConflicts)
            {
                latestByPath[conflict.FilePath] = conflict;
            }

            foreach (var resolved in resolvedFiles)
            {
                if (!latestByPath.ContainsKey(resolved.FilePath))
                {
                    latestByPath[resolved.FilePath] = resolved;
                }
            }
            var existingByPath = Conflicts.ToDictionary(c => c.FilePath, StringComparer.OrdinalIgnoreCase);

            foreach (var latest in latestByPath.Values)
            {
                if (existingByPath.TryGetValue(latest.FilePath, out var existing))
                {
                    existing.FilePath = latest.FilePath;
                    existing.BaseContent = latest.BaseContent;
                    existing.OursContent = latest.OursContent;
                    existing.TheirsContent = latest.TheirsContent;
                    existing.IsResolved = latest.IsResolved;
                }
                else
                {
                    Conflicts.Add(latest);
                }
            }

            foreach (var existing in Conflicts)
            {
                if (!latestByPath.ContainsKey(existing.FilePath))
                {
                    existing.IsResolved = true;
                }
            }

            if (SelectedConflict == null || !Conflicts.Contains(SelectedConflict))
            {
                SelectedConflict = Conflicts.FirstOrDefault(c => !c.IsResolved) ?? Conflicts.FirstOrDefault();
            }

            if (SelectedConflict != null)
            {
                await BuildMergeResultForSelectedConflict();
            }

            UpdateCounts();
            await _gitService.SaveStoredMergeConflictFilesAsync(_repoPath, latestByPath.Keys);
        }
        finally
        {
            if (showLoading)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// Build the merge result for the currently selected conflict.
    /// </summary>
    private async Task BuildMergeResultForSelectedConflict()
    {
        if (SelectedConflict == null)
        {
            CurrentMergeResult = null;
            MergedContent = string.Empty;
            MergedLines.Clear();
            return;
        }

        await Task.Run(() =>
        {
            var result = _mergeService.PerformMerge(
                SelectedConflict.FilePath,
                SelectedConflict.BaseContent,
                SelectedConflict.OursContent,
                SelectedConflict.TheirsContent);

            Application.Current.Dispatcher.Invoke(() =>
            {
                CurrentMergeResult = result;
                _currentRegionIndex = result.GetFirstUnresolvedConflictIndex();
                UpdateResolutionProperties();
                WireConflictLineEvents(result);
                BuildDisplayLines(result);

                if (SelectedConflict.IsResolved && !string.IsNullOrWhiteSpace(SelectedConflict.MergedContent))
                {
                    MergedLines.Clear();
                    UpdateMergedLinesFromText(SelectedConflict.MergedContent);
                }
                else
                {
                    RefreshMergedLines();
                }
            });
        });
    }

    partial void OnSelectedConflictChanged(ConflictInfo? value)
    {
        if (value != null)
        {
            _ = BuildMergeResultForSelectedConflict();
        }
    }

    /// <summary>
    /// Accept all "ours" for all unresolved conflicts in current file.
    /// </summary>
    [RelayCommand]
    private void AcceptAllOurs()
    {
        if (CurrentMergeResult == null) return;

        foreach (var region in CurrentMergeResult.Regions.Where(r => r.IsConflict && !r.IsResolved))
        {
            region.SelectAllOurs();
        }

        UpdateResolutionProperties();
    }

    /// <summary>
    /// Accept all "theirs" for all unresolved conflicts in current file.
    /// </summary>
    [RelayCommand]
    private void AcceptAllTheirs()
    {
        if (CurrentMergeResult == null) return;

        foreach (var region in CurrentMergeResult.Regions.Where(r => r.IsConflict && !r.IsResolved))
        {
            region.SelectAllTheirs();
        }

        UpdateResolutionProperties();
    }

    /// <summary>
    /// Navigate to the next unresolved conflict region.
    /// </summary>
    [RelayCommand]
    private void NextRegionConflict()
    {
        if (CurrentMergeResult == null) return;

        var nextIndex = CurrentMergeResult.GetNextUnresolvedConflictIndex(_currentRegionIndex);
        if (nextIndex >= 0)
        {
            _currentRegionIndex = nextIndex;
            RequestScrollToRegion?.Invoke(this, nextIndex);
        }
    }

    /// <summary>
    /// Navigate to the previous unresolved conflict region.
    /// </summary>
    [RelayCommand]
    private void PreviousRegionConflict()
    {
        if (CurrentMergeResult == null) return;

        var prevIndex = CurrentMergeResult.GetPreviousUnresolvedConflictIndex(_currentRegionIndex);
        if (prevIndex >= 0)
        {
            _currentRegionIndex = prevIndex;
            RequestScrollToRegion?.Invoke(this, prevIndex);
        }
    }

    /// <summary>
    /// Copy the merged result to clipboard.
    /// </summary>
    [RelayCommand]
    private void CopyMergedResult()
    {
        if (CurrentMergeResult == null) return;

        var content = CurrentMergeResult.GetMergedContent();
        _clipboardService.SetText(content);
    }

    /// <summary>
    /// Accept the "ours" (current branch) version for the entire file.
    /// </summary>
    [RelayCommand]
    private async Task UseOursAsync()
    {
        if (SelectedConflict == null) return;

        try
        {
            IsResolving = true;
            await _gitService.ResolveConflictWithOursAsync(_repoPath, SelectedConflict.FilePath);

            SelectedConflict.MergedContent = SelectedConflict.OursContent;
            SelectedConflict.IsResolved = true;
            UpdateCounts();

        }
        finally
        {
            IsResolving = false;
        }
    }

    /// <summary>
    /// Accept the "theirs" (incoming branch) version for the entire file.
    /// </summary>
    [RelayCommand]
    private async Task UseTheirsAsync()
    {
        if (SelectedConflict == null) return;

        try
        {
            IsResolving = true;
            await _gitService.ResolveConflictWithTheirsAsync(_repoPath, SelectedConflict.FilePath);

            SelectedConflict.MergedContent = SelectedConflict.TheirsContent;
            SelectedConflict.IsResolved = true;
            UpdateCounts();

        }
        finally
        {
            IsResolving = false;
        }
    }

    /// <summary>
    /// Mark the selected conflict as resolved with the merged content.
    /// </summary>
    [RelayCommand]
    private async Task MarkResolvedAsync()
    {
        if (SelectedConflict == null || !CanMarkResolved)
            return;

        try
        {
            IsResolving = true;

            var mergedContent = MergedContent;
            if (string.IsNullOrWhiteSpace(mergedContent) && CurrentMergeResult != null)
            {
                mergedContent = CurrentMergeResult.GetMergedContent();
            }

            // Write the merged content to the file
            await System.IO.File.WriteAllTextAsync(
                System.IO.Path.Combine(_repoPath, SelectedConflict.FilePath),
                mergedContent);

            // Stage the file to mark conflict as resolved
            await _gitService.MarkConflictResolvedAsync(_repoPath, SelectedConflict.FilePath);

            SelectedConflict.MergedContent = mergedContent;
            SelectedConflict.IsResolved = true;
            UpdateCounts();

        }
        finally
        {
            IsResolving = false;
        }
    }

    /// <summary>
    /// Navigate to the previous conflict file.
    /// </summary>
    [RelayCommand]
    private void PreviousConflict()
    {
        if (SelectedConflict == null || Conflicts.Count == 0) return;

        var currentIndex = Conflicts.IndexOf(SelectedConflict);
        if (currentIndex > 0)
        {
            SelectedConflict = Conflicts[currentIndex - 1];
        }
    }

    /// <summary>
    /// Navigate to the next conflict file.
    /// </summary>
    [RelayCommand]
    private void NextConflict()
    {
        if (SelectedConflict == null || Conflicts.Count == 0) return;

        var currentIndex = Conflicts.IndexOf(SelectedConflict);
        if (currentIndex < Conflicts.Count - 1)
        {
            SelectedConflict = Conflicts[currentIndex + 1];
        }
    }

    /// <summary>
    /// Complete the merge after all conflicts are resolved.
    /// </summary>
    [RelayCommand]
    private async Task CompleteMergeAsync()
    {
        if (!CanCompleteMerge) return;

        try
        {
            IsResolving = true;

            var commitMessage = $"Merge branch '{SourceBranch}' into {TargetBranch}";
            await _gitService.CompleteMergeAsync(_repoPath, commitMessage);

            MergeCompleted?.Invoke(this, true);
        }
        catch (Exception)
        {
            MergeCompleted?.Invoke(this, false);
            throw;
        }
        finally
        {
            IsResolving = false;
        }
    }

    /// <summary>
    /// Abort the merge and return to pre-merge state.
    /// </summary>
    [RelayCommand]
    private async Task AbortMergeAsync()
    {
        try
        {
            IsResolving = true;
            await _gitService.AbortMergeAsync(_repoPath);
            MergeCompleted?.Invoke(this, false);
        }
        finally
        {
            IsResolving = false;
        }
    }

    /// <summary>
    /// Select the next unresolved conflict, or stay on current if none.
    /// </summary>
    private void SelectNextUnresolvedConflict()
    {
        var nextUnresolved = Conflicts.FirstOrDefault(c => !c.IsResolved);
        if (nextUnresolved != null)
        {
            SelectedConflict = nextUnresolved;
        }
    }

    /// <summary>
    /// Update the conflict count properties.
    /// </summary>
    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(ResolvedCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(RemainingCount));
        OnPropertyChanged(nameof(CanCompleteMerge));
        RefreshConflictBuckets();
        _ = _gitService.SaveStoredMergeConflictFilesAsync(_repoPath, Conflicts.Select(c => c.FilePath));
    }

    /// <summary>
    /// Update resolution-related properties.
    /// </summary>
    private void UpdateResolutionProperties()
    {
        CurrentMergeResult?.NotifyResolutionChanged();
        OnPropertyChanged(nameof(HasUnresolvedConflicts));
        OnPropertyChanged(nameof(CanMarkResolved));
        RefreshMergedLines();
    }

    private void RefreshMergedLines()
    {
        if (CurrentMergeResult == null)
        {
            MergedContent = string.Empty;
            MergedLines.Clear();
            return;
        }

        MergedLines.Clear();
        _wiredMergedLines.Clear();

        foreach (var region in CurrentMergeResult.Regions)
        {
            var lines = GetRegionLines(region);
            foreach (var (line, source) in lines)
            {
                MergedLines.Add(new MergedLine { Content = line, Source = source });
            }
        }

        UpdateMergedContentFromLines();
    }

    private List<(string line, MergedLineSource source)> GetRegionLines(MergeRegion region)
    {
        if (region.Type != MergeRegionType.Conflict)
        {
            var contentLines = SplitLines(region.Content);
            var source = region.Type switch
            {
                MergeRegionType.OursOnly => MergedLineSource.Ours,
                MergeRegionType.TheirsOnly => MergedLineSource.Theirs,
                _ => MergedLineSource.None
            };
            return contentLines.Select(l => (l, source)).ToList();
        }

        return region.Resolution switch
        {
            ConflictResolution.UseOurs => region.OursLines.Select(l => (l, MergedLineSource.Ours)).ToList(),
            ConflictResolution.UseTheirs => region.TheirsLines.Select(l => (l, MergedLineSource.Theirs)).ToList(),
            ConflictResolution.UseCustom => GetCustomSelectedLines(region),
            ConflictResolution.UseManual => SplitLines(region.ManualEditContent)
                .Select(l => (l, MergedLineSource.Manual)).ToList(),
            _ => GetEmptyConflictLines(region).Select(l => (l, MergedLineSource.None)).ToList()
        };
    }

    private static List<(string line, MergedLineSource source)> GetCustomSelectedLines(MergeRegion region)
    {
        var lines = new List<(string line, MergedLineSource source)>();
        if (region.OursSelectableLines != null)
        {
            lines.AddRange(region.OursSelectableLines
                .Where(l => l.IsSelected)
                .Select(l => (l.Content, MergedLineSource.Ours)));
        }
        if (region.TheirsSelectableLines != null)
        {
            lines.AddRange(region.TheirsSelectableLines
                .Where(l => l.IsSelected)
                .Select(l => (l.Content, MergedLineSource.Theirs)));
        }
        return lines;
    }

    private static List<string> GetEmptyConflictLines(MergeRegion region)
    {
        var count = Math.Max(region.OursLines.Count, region.TheirsLines.Count);
        if (count <= 0) count = 1;
        return Enumerable.Repeat(string.Empty, count).ToList();
    }

    private static List<string> SplitLines(string content)
    {
        if (content == null)
        {
            return [];
        }

        if (content.Length == 0)
        {
            return [string.Empty];
        }

        return content.Split('\n').ToList();
    }

    private void WireConflictLineEvents(FileMergeResult result)
    {
        foreach (var region in result.Regions.Where(r => r.IsConflict))
        {
            region.InitializeSelectableLines();

            if (region.OursSelectableLines != null)
            {
                foreach (var line in region.OursSelectableLines)
                {
                    if (_wiredSelectableLines.Add(line))
                    {
                        line.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(SelectableLine.IsSelected))
                            {
                                region.UpdateResolutionFromSelection();
                                UpdateResolutionProperties();
                            }
                        };
                    }
                }
            }

            if (region.TheirsSelectableLines != null)
            {
                foreach (var line in region.TheirsSelectableLines)
                {
                    if (_wiredSelectableLines.Add(line))
                    {
                        line.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(SelectableLine.IsSelected))
                            {
                                region.UpdateResolutionFromSelection();
                                UpdateResolutionProperties();
                            }
                        };
                    }
                }
            }
        }
    }

    [RelayCommand]
    private async Task UnresolveConflictAsync(ConflictInfo? conflict)
    {
        if (conflict == null || !conflict.IsResolved)
            return;

        try
        {
            IsResolving = true;
            await _gitService.ReopenConflictAsync(_repoPath, conflict.FilePath, conflict.BaseContent, conflict.OursContent, conflict.TheirsContent);

            conflict.IsResolved = false;
            UpdateCounts();

            if (SelectedConflict != conflict)
            {
                SelectedConflict = conflict;
            }
            else
            {
                await BuildMergeResultForSelectedConflict();
            }
        }
        finally
        {
            IsResolving = false;
        }
    }

    private void UpdateMergedContentFromLines()
    {
        foreach (var line in MergedLines)
        {
            if (_wiredMergedLines.Add(line))
            {
                line.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MergedLine.Content))
                    {
                        if (line.Source != MergedLineSource.Manual)
                        {
                            line.Source = MergedLineSource.Manual;
                        }
                        MergedContent = string.Join("\n", MergedLines.Select(l => l.Content));
                        OnPropertyChanged(nameof(CanMarkResolved));
                    }
                };
            }
        }

        MergedContent = string.Join("\n", MergedLines.Select(l => l.Content));
        OnPropertyChanged(nameof(CanMarkResolved));
    }

    public void UpdateMergedLinesFromText(string text)
    {
        var lines = text.Split('\n');
        if (lines.Length == 0)
        {
            lines = [string.Empty];
        }

        for (int i = 0; i < lines.Length; i++)
        {
            if (i < MergedLines.Count)
            {
                if (!string.Equals(MergedLines[i].Content, lines[i], StringComparison.Ordinal))
                {
                    MergedLines[i].Content = lines[i];
                    if (MergedLines[i].Source != MergedLineSource.Manual)
                    {
                        MergedLines[i].Source = MergedLineSource.Manual;
                    }
                }
            }
            else
            {
                MergedLines.Add(new MergedLine
                {
                    Content = lines[i],
                    Source = MergedLineSource.Manual
                });
            }
        }

        while (MergedLines.Count > lines.Length)
        {
            MergedLines.RemoveAt(MergedLines.Count - 1);
        }

        MergedContent = text;
        OnPropertyChanged(nameof(CanMarkResolved));
    }

    private void BuildDisplayLines(FileMergeResult result)
    {
        OursDisplayLines.Clear();
        TheirsDisplayLines.Clear();
        var oursLineNumber = 1;
        var theirsLineNumber = 1;

        foreach (var region in result.Regions)
        {
            if (!region.IsConflict)
            {
                var lines = SplitLines(region.Content);
                if (region.Type != MergeRegionType.TheirsOnly)
                {
                    foreach (var line in lines)
                    {
                        OursDisplayLines.Add(new ConflictDisplayLine
                        {
                            Content = line,
                            IsSelectable = false,
                            LineNumber = oursLineNumber++
                        });
                    }
                }

                if (region.Type != MergeRegionType.OursOnly)
                {
                    foreach (var line in lines)
                    {
                        TheirsDisplayLines.Add(new ConflictDisplayLine
                        {
                            Content = line,
                            IsSelectable = false,
                            LineNumber = theirsLineNumber++
                        });
                    }
                }

                continue;
            }

            region.InitializeSelectableLines();

            if (region.OursSelectableLines != null)
            {
                foreach (var line in region.OursSelectableLines)
                {
                    var displayLine = new ConflictDisplayLine
                    {
                        Content = line.Content,
                        IsSelectable = true,
                        IsSelected = line.IsSelected,
                        SourceLine = line,
                        LineNumber = oursLineNumber++
                    };

                    if (_wiredDisplayLines.Add(line))
                    {
                        line.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(SelectableLine.IsSelected))
                            {
                                displayLine.IsSelected = line.IsSelected;
                                UpdateResolutionProperties();
                            }
                        };
                    }

                    OursDisplayLines.Add(displayLine);
                }
            }

            if (region.TheirsSelectableLines != null)
            {
                foreach (var line in region.TheirsSelectableLines)
                {
                    var displayLine = new ConflictDisplayLine
                    {
                        Content = line.Content,
                        IsSelectable = true,
                        IsSelected = line.IsSelected,
                        SourceLine = line,
                        LineNumber = theirsLineNumber++
                    };

                    if (_wiredDisplayLines.Add(line))
                    {
                        line.PropertyChanged += (s, args) =>
                        {
                            if (args.PropertyName == nameof(SelectableLine.IsSelected))
                            {
                                displayLine.IsSelected = line.IsSelected;
                                UpdateResolutionProperties();
                            }
                        };
                    }

                    TheirsDisplayLines.Add(displayLine);
                }
            }
        }
    }

    [RelayCommand]
    private void TakeOursHunk(MergeRegion? region)
    {
        if (region == null) return;
        region.SelectAllOurs();
        UpdateResolutionProperties();
    }

    [RelayCommand]
    private void TakeTheirsHunk(MergeRegion? region)
    {
        if (region == null) return;
        region.SelectAllTheirs();
        UpdateResolutionProperties();
    }

    private static bool ContainsConflictMarkers(string content)
    {
        return content.Contains("<<<<<<<", StringComparison.Ordinal) ||
               content.Contains("=======", StringComparison.Ordinal) ||
               content.Contains(">>>>>>>", StringComparison.Ordinal);
    }

    private void RefreshConflictBuckets()
    {
        ConflictedConflicts.Clear();
        ResolvedConflicts.Clear();

        foreach (var conflict in Conflicts)
        {
            if (conflict.IsResolved)
            {
                ResolvedConflicts.Add(conflict);
            }
            else
            {
                ConflictedConflicts.Add(conflict);
            }
        }
    }
}
