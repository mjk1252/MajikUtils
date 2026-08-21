<#
.SYNOPSIS
Dumps what this machine's taskbar publishes about its buttons, and what MajikUtils makes of it.

.DESCRIPTION
    powershell -ExecutionPolicy Bypass -File tools\probe-taskbar-badges.ps1

There is no API for taskbar badges. What the shell publishes is an accessible name and a help text
per button, built for screen readers, and the badge is in the help text. MajikUtils reads those and
parses them -- see TaskbarButtonName in Dock.Core -- so anything it fails to notice, it failed to
notice in one of those two strings.

This script runs the same patterns against the live taskbar and prints both the raw strings and the
verdict, so a machine where the feature does nothing can say why in one command rather than after a
round of guessing. Run it with the app in question actually badged: an unbadged button says nothing
useful about how a badged one would look.

Two things it is worth checking for specifically:

  * A button that is plainly badged on screen but whose help text is empty here. That app is
    invisible to the shell's accessibility layer and there is nothing to be read.

  * Help text in a language other than English. The patterns match "notification", "unread" and
    "attention", so a non-English Windows will parse as nothing at all. The strings this prints are
    what the patterns would have to be widened to cover.

Reports nothing about the app's own window -- only what the taskbar says about it.
#>

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes

# The same two patterns TaskbarButtonName uses, kept in step with it by hand. A count wins when it
# is a count of something; failing that the wording decides, which is what catches a badge that is
# a dot rather than a number.
$countPattern = [regex]::new('(\d+)\+?\s+(?:new\s+)?(?:notification|unread|message)s?', 'IgnoreCase')
$dotPattern = [regex]::new('attention|unread|new\s+message|(?<!\bno\s)new\s+notification', 'IgnoreCase')

function Read-Badge([string]$text) {
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }

    foreach ($match in $countPattern.Matches($text)) {
        $count = [int]$match.Groups[1].Value
        if ($count -gt 0) { return $count }
    }

    if ($dotPattern.IsMatch($text)) { return 0 }
    return $null
}

Write-Host "Windows : $((Get-CimInstance Win32_OperatingSystem).Caption) build $([Environment]::OSVersion.Version.Build)"
Write-Host "Language: $((Get-Culture).Name) / UI $((Get-UICulture).Name)"
Write-Host ''

$root = [System.Windows.Automation.AutomationElement]::RootElement
$buttonCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)

# Shell_TrayWnd is the primary monitor's taskbar; Shell_SecondaryTrayWnd is every other one, and an
# app pinned to a second screen badges there and nowhere else.
$trays = @('Shell_TrayWnd', 'Shell_SecondaryTrayWnd') | ForEach-Object {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, $_)
    $root.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
}

$total = 0
$seen = @{}

foreach ($tray in $trays) {
    foreach ($button in $tray.FindAll([System.Windows.Automation.TreeScope]::Descendants, $buttonCondition)) {
        $automationId = $button.Current.AutomationId
        $name = $button.Current.Name -replace "`r?`n", ' | '
        $help = $button.Current.HelpText

        # Only buttons carrying an AppUserModelID are apps. The rest are Start, Widgets, the clock.
        if (-not $automationId.StartsWith('Appid: ')) {
            if ($name -like 'Notification*') { Write-Host "TRAY   $name" -ForegroundColor DarkGray }
            continue
        }

        $appId = $automationId.Substring(7)
        if ($seen.ContainsKey($appId)) { continue }
        $seen[$appId] = $true

        $verdict = Read-Badge $help
        if ($null -eq $verdict) { $verdict = Read-Badge $name }

        if ($null -eq $verdict) {
            $reading = 'no badge'
            $colour = 'DarkGray'
        }
        elseif ($verdict -eq 0) {
            $reading = 'BADGE (no number)'
            $colour = 'Yellow'
            $total += 1
        }
        else {
            $reading = "BADGE = $verdict"
            $colour = 'Green'
            $total += $verdict
        }

        Write-Host ("{0,-18} {1}" -f $reading, $name) -ForegroundColor $colour
        Write-Host ("                   help='{0}'" -f $help) -ForegroundColor DarkGray
    }
}

Write-Host ''
Write-Host "Island would show: $total" -ForegroundColor Cyan
Write-Host ''
Write-Host 'If an app is visibly badged on screen but reads "no badge" above, copy its help= line.'
Write-Host 'An empty help= means the shell publishes nothing and there is nothing to read.'
