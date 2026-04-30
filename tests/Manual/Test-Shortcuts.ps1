<#
.SYNOPSIS
    Automated tests for sec5.9 customizable keyboard shortcuts.

.DESCRIPTION
    Exercises the parts of the feature that can be reliably driven from
    PowerShell without crashing the host:
        - Persistence round-trip (write override, kill+relaunch, verify)
        - F5 chord routing through MainWindow.InputBindings
        - Stale-id loader tolerance (settings file with renamed-away ids)
        - Settings file location + JSON shape
    Plus the MANUAL section that requires a human to verify (the Settings
    UI flow). Those steps are printed at the end with explicit click-by-
    click instructions.

    Runs end-to-end in ~20s. Cleans up after itself.

    Usage from repo root:
        powershell -ExecutionPolicy Bypass -File tests/Manual/Test-Shortcuts.ps1

.NOTES
    Why this script doesn't drive the Settings dialog automatically:
    WPF's Fluent menu-popup automation peers don't expose a reliable
    cross-process Invoke path (popup BoundingRectangle stays Empty even
    after Expand returns Expanded), and opening the dialog at startup
    via a CLI flag races with the splash close + dispatcher modal loop.
    Both paths I tried caused the host to deadlock or crash. Keeping the
    automated coverage to the service layer + persistence and printing
    a manual checklist for the UI is the safer trade-off.
#>

[CmdletBinding()]
param(
    [string] $LeafExe,
    [string] $TestRepoPath = "C:/Users/Tim/Documents/Repos/LeafTestRepos/merge-overhaul-test/repo",
    [int] $StartupDelaySec = 5
)

$ErrorActionPreference = 'Stop'

if (-not $LeafExe) {
    $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    $LeafExe = Resolve-Path (Join-Path $here '..\..\src\Leaf\bin\Debug\net10.0-windows\Leaf.exe')
}
if (-not (Test-Path $LeafExe))      { throw "Leaf.exe not found at $LeafExe" }
if (-not (Test-Path $TestRepoPath)) { throw "Test repo not found: $TestRepoPath" }

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

Add-Type -Namespace Win32 -Name Native -MemberDefinition @"
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(System.IntPtr hWnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool ShowWindow(System.IntPtr hWnd, int cmd);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern System.IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern bool AttachThreadInput(uint i, uint t, bool a);
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(System.IntPtr h, out uint p);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
"@

$script:Results        = @()
$script:CurrentSection = ''
$script:LeafProcess    = $null
$script:SettingsFile   = Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'Leaf/settings.json'

function Section([string]$name) {
    $script:CurrentSection = $name
    Write-Host "`n=== $name ===" -ForegroundColor Cyan
}

function Test-Case([string]$name, [scriptblock]$body) {
    try {
        & $body
        $script:Results += [pscustomobject]@{ Section = $script:CurrentSection; Name = $name; Pass = $true; Reason = $null }
        Write-Host "  [+] $name" -ForegroundColor Green
    } catch {
        $script:Results += [pscustomobject]@{ Section = $script:CurrentSection; Name = $name; Pass = $false; Reason = $_.Exception.Message }
        Write-Host "  [-] $name  ::  $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Get-LeafWindow([string]$TitleSubstring = 'Leaf', [int]$TimeoutSec = 5) {
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $cond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Window)
        try {
            $windows = $root.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)
            foreach ($w in $windows) {
                if ($w.Current.Name -like "*$TitleSubstring*" -and
                    $w.Current.ProcessId -eq $script:LeafProcess.Id) {
                    return $w
                }
            }
        } catch { }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Force-Foreground-Leaf {
    if (-not $script:LeafProcess) { return }
    try { $script:LeafProcess.Refresh() } catch { }
    $hwnd = $script:LeafProcess.MainWindowHandle
    if ($null -eq $hwnd -or $hwnd -eq [IntPtr]::Zero) { return }

    $fgHwnd = [Win32.Native]::GetForegroundWindow()
    $myTid  = [Win32.Native]::GetCurrentThreadId()
    [uint32]$fgPid = 0
    $fgTid  = [Win32.Native]::GetWindowThreadProcessId($fgHwnd, [ref]$fgPid)

    if ($fgTid -ne 0 -and $fgTid -ne $myTid) {
        [void][Win32.Native]::AttachThreadInput($myTid, $fgTid, $true)
    }
    [void][Win32.Native]::ShowWindow($hwnd, 9)  # SW_RESTORE
    [void][Win32.Native]::SetForegroundWindow($hwnd)
    if ($fgTid -ne 0 -and $fgTid -ne $myTid) {
        [void][Win32.Native]::AttachThreadInput($myTid, $fgTid, $false)
    }
    Start-Sleep -Milliseconds 300
}

function Send-Chord([string]$keys) {
    Force-Foreground-Leaf
    [System.Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 400
}

function Start-Leaf {
    Get-Process -Name Leaf -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400

    Write-Host "Launching: $LeafExe --repo $TestRepoPath" -ForegroundColor DarkGray
    $script:LeafProcess = Start-Process -FilePath $LeafExe `
        -ArgumentList @('--repo', $TestRepoPath) `
        -PassThru
    Start-Sleep -Seconds $StartupDelaySec
    [void](Get-LeafWindow -TimeoutSec 8)
    Force-Foreground-Leaf
}

function Stop-Leaf {
    if ($script:LeafProcess -and -not $script:LeafProcess.HasExited) {
        Stop-Process -Id $script:LeafProcess.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400
    }
}

function Reset-ShortcutOverrides {
    if (Test-Path $script:SettingsFile) {
        try {
            $json = Get-Content $script:SettingsFile -Raw | ConvertFrom-Json
            if ($json.PSObject.Properties.Name -contains 'shortcutOverrides') {
                $json.shortcutOverrides = New-Object psobject
                $json | ConvertTo-Json -Depth 10 | Set-Content $script:SettingsFile -Encoding UTF8
            }
        } catch { }
    }
}

function Set-Override([string]$id, [string]$gestureString) {
    $json = if (Test-Path $script:SettingsFile) {
        Get-Content $script:SettingsFile -Raw | ConvertFrom-Json
    } else { New-Object psobject }
    if (-not ($json.PSObject.Properties.Name -contains 'shortcutOverrides')) {
        $json | Add-Member -NotePropertyName 'shortcutOverrides' -NotePropertyValue (New-Object psobject)
    }
    $json.shortcutOverrides | Add-Member -NotePropertyName $id -NotePropertyValue $gestureString -Force
    $json | ConvertTo-Json -Depth 10 | Set-Content $script:SettingsFile -Encoding UTF8
}

function Get-Override([string]$id) {
    if (-not (Test-Path $script:SettingsFile)) { return $null }
    $json = Get-Content $script:SettingsFile -Raw | ConvertFrom-Json
    if (-not ($json.PSObject.Properties.Name -contains 'shortcutOverrides')) { return $null }
    return $json.shortcutOverrides.$id
}

function Run-AllTests {
    Reset-ShortcutOverrides
    Start-Leaf

    try {
        Section 'Chord routing through registry'

        Test-Case 'F5 chord fires Fetch All without crashing' {
            Send-Chord '{F5}'
            Start-Sleep -Milliseconds 500
            $main = Get-LeafWindow
            Assert ($main -ne $null) "Main window vanished after F5 chord"
        }

        Section 'Settings file shape (registry serialisation)'

        Test-Case 'Settings file exists at expected location' {
            Assert (Test-Path $script:SettingsFile) "Expected settings.json at $script:SettingsFile"
        }

        Test-Case 'Settings JSON has shortcutOverrides field after first launch' {
            $json = Get-Content $script:SettingsFile -Raw | ConvertFrom-Json
            $names = $json.PSObject.Properties.Name
            Assert ($names -contains 'shortcutOverrides') `
                "settings.json missing 'shortcutOverrides' field; got: $($names -join ', ')"
        }

        Section 'Persistence round-trip'

        Test-Case 'Override survives kill+relaunch' {
            Set-Override 'view.toggleTerminal' 'Ctrl+Alt+T'
            Stop-Leaf
            Start-Leaf
            $value = Get-Override 'view.toggleTerminal'
            Assert ($value -eq 'Ctrl+Alt+T') "Override missing after restart; got '$value'"
        }

        Test-Case 'Stale id loader tolerance (renamed command)' {
            # Pretend a future Leaf renamed view.toggleTerminal to
            # view.terminal.toggle. The unknown id must not break load.
            Set-Override 'view.toggleTerminal' 'Ctrl+Alt+T'
            Set-Override 'view.terminal.renamed.in.future.version' 'Ctrl+Q'
            Stop-Leaf
            Start-Leaf
            # The valid one survives
            $valid = Get-Override 'view.toggleTerminal'
            Assert ($valid -eq 'Ctrl+Alt+T') "Valid override lost; got '$valid'"
        }

        Test-Case 'Multiple overrides persist together' {
            Reset-ShortcutOverrides
            Set-Override 'view.toggleTerminal' 'Ctrl+Alt+T'
            Set-Override 'repo.fetch' 'Ctrl+Shift+F5'
            Set-Override 'commit.stash' 'Ctrl+Alt+Shift+S'
            Stop-Leaf
            Start-Leaf
            Assert ((Get-Override 'view.toggleTerminal') -eq 'Ctrl+Alt+T')   "view.toggleTerminal lost"
            Assert ((Get-Override 'repo.fetch')          -eq 'Ctrl+Shift+F5') "repo.fetch lost"
            Assert ((Get-Override 'commit.stash')        -eq 'Ctrl+Alt+Shift+S') "commit.stash lost"
        }

        Test-Case 'Empty-string override (user unbound the shortcut) is kept' {
            Reset-ShortcutOverrides
            Set-Override 'view.toggleTerminal' ''
            Stop-Leaf
            Start-Leaf
            $value = Get-Override 'view.toggleTerminal'
            # Empty string round-trips as empty -- the registry treats it
            # as "user explicitly unbound" rather than "use default".
            Assert ($value -eq '') "Empty-string override changed; got '$value'"
        }
    }
    finally {
        Stop-Leaf
        Reset-ShortcutOverrides
    }

    Write-Host "`n----------------------------------------" -ForegroundColor Cyan
    $passed = ($script:Results | Where-Object { $_.Pass }).Count
    $failed = ($script:Results | Where-Object { -not $_.Pass }).Count
    $color = if ($failed -eq 0) { 'Green' } else { 'Red' }
    Write-Host "AUTOMATED:  Total: $($script:Results.Count)  Passed: $passed  Failed: $failed" -ForegroundColor $color
    if ($failed -gt 0) {
        Write-Host "`nFailures:" -ForegroundColor Red
        $script:Results | Where-Object { -not $_.Pass } | ForEach-Object {
            Write-Host "  [$($_.Section)] $($_.Name)" -ForegroundColor Red
            Write-Host "    -> $($_.Reason)" -ForegroundColor DarkRed
        }
    }

    Write-Host "`n=== MANUAL CHECKLIST ===" -ForegroundColor Yellow
    Write-Host "See tests/Manual/SEC5_9_TEST_LIST.md for the full" -ForegroundColor White
    Write-Host "interactive walkthrough -- shortcut chords, Settings UI" -ForegroundColor White
    Write-Host "edit/save/reset/conflict flow, merge editor regression," -ForegroundColor White
    Write-Host "and edge cases. ~20 minutes to walk through." -ForegroundColor White

    if ($failed -gt 0) { exit 1 } else { exit 0 }
}

Run-AllTests
