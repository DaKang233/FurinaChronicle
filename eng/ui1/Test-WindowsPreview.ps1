# Copyright (c) 2026 DaKang233.
# SPDX-License-Identifier: MIT

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $Executable,
    [Parameter(Mandatory = $true)][string] $ReportPath,
    [string] $DataDirectory,
    [switch] $CheckAnalysis,
    [switch] $CheckProductLayout
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$executablePath = (Resolve-Path -LiteralPath $Executable).Path
$reportFullPath = [IO.Path]::GetFullPath($ReportPath)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFullPath)) | Out-Null
if (-not $DataDirectory) { $DataDirectory = Join-Path ([IO.Path]::GetDirectoryName($reportFullPath)) 'windows-data' }
$dataFullPath = [IO.Path]::GetFullPath($DataDirectory)
$checks = [Collections.Generic.List[object]]::new()
$launched = $null
$window = $null

function Find-Control([string] $id, [string] $name) {
    $property = [System.Windows.Automation.AutomationElement]::AutomationIdProperty
    $value = $id
    if (-not $id) {
        $property = [System.Windows.Automation.AutomationElement]::NameProperty
        $value = $name
    }
    $condition = New-Object System.Windows.Automation.PropertyCondition($property, $value)
    return $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Wait-Summary([string] $text, [switch] $Pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $summary = Find-Control 'Ui1Summary' ''
        $status = Find-Control 'Ui1Status' ''
        if ($summary -and ($summary.Current.Name.Contains($text) -or ($Pattern -and $summary.Current.Name -match $text)) -and
            $status -and $status.Current.Name -notmatch '正在|失败') {
            $checks.Add([pscustomobject]@{ Expected = $text; Actual = $summary.Current.Name; Status = 'Pass' })
            return
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline -and -not $launched.HasExited)
    throw "Summary did not reach expected state: $text"
}

function Invoke-Button([string] $name) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $button = Find-Control '' $name
        if ($button -and $button.Current.IsEnabled) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $button) { throw "Control not available: $name" }
    $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Select-Option([string] $id, [string] $label) {
    $controlDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $combo = Find-Control $id ''
        if (-not $combo) { $combo = Find-Control '' $id }
        if ($combo) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $controlDeadline)
    if (-not $combo) { throw "Scope control not available: $id" }
    $expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    $expand.Expand()
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        # Popups can be outside the window subtree. Match both process and exact synthetic label.
        $processCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$launched.Id)
        $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $label)
        $condition = New-Object System.Windows.Automation.AndCondition($processCondition, $nameCondition)
        $items = [System.Windows.Automation.AutomationElement]::RootElement.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants, $condition)
        foreach ($item in $items) {
            $selection = $null
            if ($item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$selection)) {
                $selection.Select()
                # SelectionItem.Select can leave the native popup open. Close it before reacquiring another picker.
                $expand.Collapse()
                return
            }
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Synthetic option not available: $label"
}

try {
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $executablePath
    $startInfo.WorkingDirectory = [IO.Path]::GetDirectoryName($executablePath)
    $startInfo.UseShellExecute = $false
    $startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
    $startInfo.EnvironmentVariables['FURINA_UI1_DIRECTORY'] = $dataFullPath
    $launched = [Diagnostics.Process]::Start($startInfo)
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        $launched.Refresh()
        if ($launched.HasExited) { throw "Preview exited: $($launched.ExitCode)" }
        if ($launched.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [System.Windows.Automation.AutomationElement]::FromHandle($launched.MainWindowHandle)
        }
        if ($window -and (Find-Control 'Ui1Summary' '')) { break }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $window) { throw 'Preview window not available to UIA.' }
    Wait-Summary '61 条 · 第 1/3 页'
    $recordDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $recordName = Find-Control '' '合成物品 0-0-61'
        if ($recordName) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $recordDeadline)
    if (-not $recordName) { throw 'Newest synthetic record text is not exposed by the list.' }
    $checks.Add([pscustomobject]@{ Expected = '合成物品 0-0-61'; Actual = $recordName.Current.Name; Status = 'Pass' })
    Invoke-Button '下一页'
    Wait-Summary '61 条 · 第 2/3 页'
    Invoke-Button '下一页'
    Wait-Summary '61 条 · 第 3/3 页'
    Select-Option 'Ui1Account' 'B 合成账号 · 100000002 · ChinaOfficial'
    Wait-Summary '7 条 · 第 1/1 页'
    Select-Option 'Ui1Archive' '02 同 UID 独立副本'
    Wait-Summary '3 条 · 第 1/1 页'
    Select-Option 'Ui1Archive' '01 合成档案'
    Wait-Summary '7 条 · 第 1/1 页'
    Select-Option 'Ui1Account' 'C 空账号 · 100000003 · ChinaOfficial'
    Wait-Summary '0 条 · 第 1/1 页'
    Select-Option 'Ui1Archive' '03 空档案'
    Wait-Summary '0 条 · 第 1/1 页'
    $checks.Add([pscustomobject]@{ Expected = '此档案还没有游戏账号'; Actual = (Find-Control 'Ui1Status' '').Current.Name; Status = 'Pass' })
    if (-not (Find-Control 'Ui1Status' '').Current.Name.Contains('此档案还没有游戏账号')) { throw 'Empty archive message missing.' }
    if ($CheckAnalysis -or $CheckProductLayout) {
        Select-Option 'Ui1Archive' '01 合成档案'
        Wait-Summary '0 条 · 第 1/1 页'
        Invoke-Button '档案多选'
        Invoke-Button '全选'
        Invoke-Button '应用档案范围'
        Wait-Summary '68 条 · 第 1/3 页'
        Select-Option '分析视图' '总览'
        Wait-Summary '分析条目 1 条'
        if (-not (Find-Control '' '本地 68 抽 · 五星 6 次')) { throw 'The overview did not show the scoped count.' }
        Select-Option '分析视图' '历史'
        Wait-Summary '分析条目 [1-9]' -Pattern
        Select-Option '分析视图' '日历'
        Wait-Summary '分析条目 1 条'
        if ($CheckProductLayout) {
            Invoke-Button '下个月'
            Wait-Summary '分析条目 0 条'
            Invoke-Button '上个月'
            Wait-Summary '分析条目 1 条'
            Invoke-Button '2026-10-01 · 68 抽'
            Wait-Summary '68 条 · 第 1/3 页'
        }
        Select-Option '分析视图' '物品'
        Wait-Summary '分析条目 [1-9]' -Pattern
        Select-Option '分析视图' '明细'
        Wait-Summary '68 条 · 第 1/3 页'
    }
    if ($CheckProductLayout) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PreviewWindowSize {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int w, int h, uint flags);
}
'@
        if (-not [PreviewWindowSize]::SetWindowPos($launched.MainWindowHandle, [IntPtr]::Zero, 0, 0, 1600, 820, 0x16)) { throw 'Resize failed.' }
        Start-Sleep -Milliseconds 700
        if (-not (Find-Control '' '并排详情') -or -not (Find-Control '' '物品 / 账号')) { throw 'Wide layout missing.' }
        $checks.Add([pscustomobject]@{ Expected = '1600x820: table and side detail'; Status = 'Pass' })
        if (-not [PreviewWindowSize]::SetWindowPos($launched.MainWindowHandle, [IntPtr]::Zero, 0, 0, 800, 600, 0x16)) { throw 'Resize failed.' }
        Start-Sleep -Milliseconds 700
        if (Find-Control '' '并排详情') { throw 'Side detail should collapse in a narrow window.' }
        Wait-Summary '68 条 · 第 1/3 页'
    }
    $result = 'Pass'
}
catch {
    $result = 'Fail'
    $checks.Add([pscustomobject]@{ Status = 'Fail'; Error = $_.Exception.Message })
    if ($window) {
        $checks.Add([pscustomobject]@{
            Summary = (Find-Control 'Ui1Summary' '').Current.Name
            LoadStatus = (Find-Control 'Ui1Status' '').Current.Name
        })
    }
}
finally {
    # Only close the process this script started; never kill an existing application.
    if ($launched -and -not $launched.HasExited) {
        [void]$launched.CloseMainWindow()
        if (-not $launched.WaitForExit(5000)) {
            # This process was created by this run and contains only the controlled preview.
            $launched.Kill()
            [void]$launched.WaitForExit(5000)
        }
    }
    [pscustomobject]@{
        Status = $result; Executable = $executablePath; DataDirectory = $dataFullPath; TimestampUtc = [DateTime]::UtcNow.ToString('o')
        Checks = @($checks.ToArray())
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $reportFullPath -Encoding UTF8
}
if ($result -ne 'Pass') { exit 1 }
