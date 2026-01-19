using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Leaf.Models;
using Leaf.Services;

namespace Leaf.ViewModels;

public sealed partial class TerminalViewModel : ObservableObject
{
    private readonly TerminalService _terminalService;
    private readonly SettingsService _settingsService;
    private readonly IGitService _gitService;
    private readonly List<string> _history = [];
    private int _historyIndex = -1;
    private CancellationTokenSource? _commandCts;

    public event EventHandler<TerminalCommandExecutedEventArgs>? CommandExecuted;

    public TerminalViewModel(IGitService gitService, SettingsService settingsService)
    {
        _terminalService = new TerminalService();
        _settingsService = settingsService;
        _gitService = gitService;
        _gitService.GitCommandExecuted += OnGitCommandExecuted;

        Lines = new ObservableCollection<TerminalLine>();
        LoadSettings();
        SetWorkingDirectory(null);
    }

    public ObservableCollection<TerminalLine> Lines { get; }

    [ObservableProperty]
    private string _inputText = string.Empty;

    [ObservableProperty]
    private string _workingDirectory = string.Empty;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _logGitCommands = true;

    [ObservableProperty]
    private int _maxLines = 2000;

    [ObservableProperty]
    private double _fontSize = 12;

    [ObservableProperty]
    private string _shellExecutable = "cmd.exe";

    [ObservableProperty]
    private string _shellArgumentsTemplate = "/c {command}";

    public event EventHandler? CommandCompleted;

    public void ReloadSettings()
    {
        LoadSettings();
    }

    public void SetWorkingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        else
        {
            WorkingDirectory = path;
        }

        var trimmedPath = WorkingDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = Path.GetFileName(trimmedPath);
        Prompt = string.IsNullOrWhiteSpace(folderName) ? $"{WorkingDirectory}>" : $"{folderName}>";
    }

    public void NavigateHistory(int delta)
    {
        if (_history.Count == 0)
        {
            return;
        }

        if (_historyIndex < 0)
        {
            _historyIndex = _history.Count;
        }

        if (delta < 0)
        {
            if (_historyIndex > 0)
            {
                _historyIndex--;
            }
        }
        else if (delta > 0)
        {
            if (_historyIndex < _history.Count)
            {
                _historyIndex++;
            }
        }

        if (_historyIndex >= 0 && _historyIndex < _history.Count)
        {
            InputText = _history[_historyIndex];
        }
        else
        {
            InputText = string.Empty;
        }
    }

    [RelayCommand]
    private void Clear()
    {
        Lines.Clear();
    }

    [RelayCommand]
    private void Cancel()
    {
        if (_commandCts != null && !_commandCts.IsCancellationRequested)
        {
            _commandCts.Cancel();
        }
    }

    [RelayCommand]
    private async Task ExecuteCommandAsync()
    {
        var command = InputText.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        AddLine(TerminalLineKind.Input, $"{Prompt} {command}");
        TrackHistory(command);
        InputText = string.Empty;

        if (TryHandleBuiltIn(command))
        {
            return;
        }

        if (IsRunning)
        {
            AddLine(TerminalLineKind.Info, "Command already running.");
            return;
        }

        IsRunning = true;
        _commandCts = new CancellationTokenSource();

        try
        {
            var exitCode = await _terminalService.RunCommandAsync(
                command,
                WorkingDirectory,
                ShellExecutable,
                ShellArgumentsTemplate,
                line => AppendOutput(TerminalLineKind.Output, line),
                line => AppendOutput(TerminalLineKind.Error, line),
                _commandCts.Token);

            if (exitCode != 0)
            {
                AddLine(TerminalLineKind.Info, $"Exit code {exitCode}");
            }

            if (IsGitCommand(command))
            {
                CommandExecuted?.Invoke(this, new TerminalCommandExecutedEventArgs(command, exitCode));
            }
        }
        catch (Exception ex)
        {
            AddLine(TerminalLineKind.Error, ex.Message);
        }
        finally
        {
            IsRunning = false;
            _commandCts.Dispose();
            _commandCts = null;
            CommandCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    partial void OnAutoScrollChanged(bool value)
    {
        PersistSettings();
    }

    partial void OnLogGitCommandsChanged(bool value)
    {
        PersistSettings();
    }

    partial void OnMaxLinesChanged(int value)
    {
        PersistSettings();
    }

    partial void OnFontSizeChanged(double value)
    {
        PersistSettings();
    }

    partial void OnShellExecutableChanged(string value)
    {
        PersistSettings();
    }

    partial void OnShellArgumentsTemplateChanged(string value)
    {
        PersistSettings();
    }

    private void TrackHistory(string command)
    {
        if (_history.Count == 0 || !_history[^1].Equals(command, StringComparison.OrdinalIgnoreCase))
        {
            _history.Add(command);
        }

        _historyIndex = _history.Count;
    }

    private bool TryHandleBuiltIn(string command)
    {
        if (command.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
            command.Equals("cls", StringComparison.OrdinalIgnoreCase))
        {
            Lines.Clear();
            return true;
        }

        if (command.Equals("pwd", StringComparison.OrdinalIgnoreCase))
        {
            AddLine(TerminalLineKind.Info, WorkingDirectory);
            return true;
        }

        if (command.Equals("cd", StringComparison.OrdinalIgnoreCase))
        {
            SetWorkingDirectory(null);
            AddLine(TerminalLineKind.Info, $"Working directory: {WorkingDirectory}");
            return true;
        }

        if (command.StartsWith("cd ", StringComparison.OrdinalIgnoreCase))
        {
            var target = command[3..].Trim().Trim('"');
            var nextPath = Path.IsPathRooted(target)
                ? target
                : Path.Combine(WorkingDirectory, target);

            if (Directory.Exists(nextPath))
            {
                SetWorkingDirectory(nextPath);
                AddLine(TerminalLineKind.Info, $"Working directory: {WorkingDirectory}");
            }
            else
            {
                AddLine(TerminalLineKind.Error, $"Directory not found: {nextPath}");
            }

            return true;
        }

        return false;
    }

    private static bool IsGitCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        var trimmed = command.TrimStart();
        return trimmed.Equals("git", StringComparison.OrdinalIgnoreCase) ||
               trimmed.StartsWith("git ", StringComparison.OrdinalIgnoreCase);
    }

    private void AddLine(TerminalLineKind kind, string text)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(() => AddLine(kind, text));
            return;
        }

        Lines.Add(new TerminalLine(kind, text));
        TrimLines();
    }

    private void AppendOutput(TerminalLineKind kind, string line)
    {
        AddLine(kind, line);
    }

    private void TrimLines()
    {
        if (MaxLines <= 0)
        {
            return;
        }

        while (Lines.Count > MaxLines)
        {
            Lines.RemoveAt(0);
        }
    }

    private void OnGitCommandExecuted(object? sender, GitCommandEventArgs e)
    {
        if (!LogGitCommands)
        {
            return;
        }

        AddLine(TerminalLineKind.Info, $"[git] {e.Arguments}");
        AppendGitOutput(e.Output, TerminalLineKind.Output);
        AppendGitOutput(e.Error, TerminalLineKind.Error);

        if (e.ExitCode != 0)
        {
            AddLine(TerminalLineKind.Info, $"[git] Exit code {e.ExitCode}");
        }
    }

    private void AppendGitOutput(string text, TerminalLineKind kind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var lines = text.Replace("\r\n", "\n").Split('\n');
        foreach (var line in lines)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                AddLine(kind, line);
            }
        }
    }

    private void LoadSettings()
    {
        var settings = _settingsService.LoadSettings();
        AutoScroll = settings.TerminalAutoScroll;
        LogGitCommands = settings.TerminalLogGitCommands;
        MaxLines = settings.TerminalMaxLines;
        FontSize = settings.TerminalFontSize;
        ShellExecutable = string.IsNullOrWhiteSpace(settings.TerminalShellExecutable)
            ? "cmd.exe"
            : settings.TerminalShellExecutable;
        ShellArgumentsTemplate = string.IsNullOrWhiteSpace(settings.TerminalShellArguments)
            ? "/c {command}"
            : settings.TerminalShellArguments;
    }

    private void PersistSettings()
    {
        var settings = _settingsService.LoadSettings();
        settings.TerminalAutoScroll = AutoScroll;
        settings.TerminalLogGitCommands = LogGitCommands;
        settings.TerminalMaxLines = MaxLines;
        settings.TerminalFontSize = FontSize;
        settings.TerminalShellExecutable = string.IsNullOrWhiteSpace(ShellExecutable) ? "cmd.exe" : ShellExecutable;
        settings.TerminalShellArguments = string.IsNullOrWhiteSpace(ShellArgumentsTemplate) ? "/c {command}" : ShellArgumentsTemplate;
        _settingsService.SaveSettings(settings);
    }
}
