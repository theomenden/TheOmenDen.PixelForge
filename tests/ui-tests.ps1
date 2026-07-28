<#
.SYNOPSIS
    UI automation tests for TheOmenDen.PixelForge, driven by the `winapp ui` CLI.

.DESCRIPTION
    Launch the app first, then pass its PID:
        dotnet run --project src\TheOmenDen.PixelForge      # prints the PID
        .\tests\ui-tests.ps1 -AppPid <PID>

    Add a Test-UI block per requirement. Assertion reference:
        winapp ui wait-for "Id" -a $AppPid -t 3000                     # exists
        winapp ui wait-for "Id" -a $AppPid --value "expected" -t 3000  # value
        winapp ui invoke "Id" -a $AppPid                               # click
        winapp ui set-value "Id" "text" -a $AppPid                     # type
#>
param([Parameter(Mandatory)][int]$AppPid)   # NOT $Pid — that name is read-only in PowerShell

$ErrorActionPreference = 'Continue'
$pass = 0; $fail = 0; $results = @()
$outDir = Join-Path $PSScriptRoot 'ui-results'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Test-UI {
    param([string]$Name, [scriptblock]$Script)
    # Inside $Script use `throw` to fail — `exit` would kill the whole run.
    try {
        $output = & $Script 2>&1
        if ($LASTEXITCODE -eq 0) {
            $script:pass++; $script:results += @{ name = $Name; status = 'PASS' }
        } else {
            $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$output" }
        }
    } catch {
        $script:fail++; $script:results += @{ name = $Name; status = 'FAIL'; detail = "$_" }
    }
}

# ─── Smoke: the app window is up ─────────────────────────────────────────────
Test-UI 'App window is present' {
    $windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
    if (-not ($windows | Where-Object { $_.title -ne 'PopupHost' })) { throw 'no app window found' }
}

# ─── Navigation ──────────────────────────────────────────────────────────────
Test-UI 'Nav: Canvas exists' { winapp ui wait-for 'NavCanvas' -a $AppPid -t 3000 }
Test-UI 'Nav: Assets exists' { winapp ui wait-for 'NavAssets' -a $AppPid -t 3000 }
Test-UI 'Nav: Pipeline exists' { winapp ui wait-for 'NavPipeline' -a $AppPid -t 3000 }
# NavigationView creates its own settings entry; "SettingsItem" is the control's built-in
# AutomationId and cannot be overridden from XAML.
Test-UI 'Nav: Settings exists' { winapp ui wait-for 'SettingsItem' -a $AppPid -t 3000 }

Test-UI 'Navigate to Assets' { winapp ui invoke 'NavAssets' -a $AppPid }
Test-UI 'Navigate to Pipeline' { winapp ui invoke 'NavPipeline' -a $AppPid }
Test-UI 'Navigate to Canvas' { winapp ui invoke 'NavCanvas' -a $AppPid }

# ─── Canvas tools ────────────────────────────────────────────────────────────
Test-UI 'Tool: Pencil' { winapp ui wait-for 'BtnToolPencil' -a $AppPid -t 3000 }
Test-UI 'Tool: Fill' { winapp ui wait-for 'BtnToolFill' -a $AppPid -t 3000 }
Test-UI 'Tool: Eraser' { winapp ui wait-for 'BtnToolEraser' -a $AppPid -t 3000 }

# ─── Theme switching ─────────────────────────────────────────────────────────
# RadioButtons items get generated ids (rdo-light-XXXX), so resolve them at runtime.
Test-UI 'Settings page opens' {
    winapp ui invoke 'SettingsItem' -a $AppPid
    Start-Sleep -Milliseconds 800
    winapp ui wait-for 'ThemeSelector' -a $AppPid -t 3000
}
Test-UI 'Switch to Light theme' {
    $tree = winapp ui inspect -a $AppPid --interactive 2>&1
    $id = ($tree | Select-String -Pattern 'rdo-light\S*' -AllMatches).Matches.Value | Select-Object -First 1
    if (-not $id) { throw 'Light radio button not found' }
    winapp ui invoke $id -a $AppPid
}

# ─── Accessibility: every interactive control needs an AutomationId ──────────
$allElements = (winapp ui inspect -a $AppPid --interactive --json 2>$null | ConvertFrom-Json).elements
$appElements = @($allElements | Where-Object {
    $_.type -match 'Button|TextBox|ComboBox|CheckBox|ToggleSwitch|TabItem|Edit|Slider|ListItem' -and
    $_.name -notmatch 'Minimize|Maximize|Close|System' -and
    $_.className -notmatch 'PickerHost|#32770|CabinetWClass'
})
$missingId = @($appElements | Where-Object { -not $_.automationId })
if ($missingId.Count -eq 0) {
    $pass++; $results += @{ name = 'All app controls have AutomationId'; status = 'PASS' }
} else {
    $fail++
    $names = ($missingId | ForEach-Object { "$($_.type) '$($_.name)'" }) -join ', '
    $results += @{ name = 'AutomationId coverage'; status = 'FAIL'; detail = "Missing: $names" }
}

# ─── Screenshot for visual review (UIA assertions can't see clipping/theming) ─
winapp ui screenshot -a $AppPid -o (Join-Path $outDir '01-initial.png') 2>$null

# ─── Results ─────────────────────────────────────────────────────────────────
Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) — $($_.detail)" -ForegroundColor Red
}
$results | ConvertTo-Json | Out-File (Join-Path $outDir 'test-results.json')
if ($fail -gt 0) { exit 1 } else { exit 0 }
