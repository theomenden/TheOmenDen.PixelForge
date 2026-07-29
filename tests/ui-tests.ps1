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

Test-UI 'Window is titled Pixel Forge' {
    $windows = winapp ui list-windows -a $AppPid --json 2>$null | ConvertFrom-Json
    if (-not ($windows | Where-Object { $_.title -eq 'Pixel Forge' })) {
        throw "expected a window titled 'Pixel Forge', got: $(($windows | ForEach-Object { $_.title }) -join ', ')"
    }
}

# Only rendered while the pane is open — a compact pane would clip it to "© 20…".
Test-UI 'Copyright notice is in the pane footer' {
    $notice = "$(winapp ui get-value 'CopyrightNotice' -a $AppPid 2>&1)".Trim()
    if ($notice -ne '© 2026 - The Omen Den') { throw "unexpected copyright text: '$notice'" }
    $global:LASTEXITCODE = 0
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
# Segmented items get generated ids (itm-light-XXXX), so resolve them at runtime.
# SegmentedItem exposes no invoke pattern — focus it and press Enter. Mouse `click`
# would work too but needs the window in the foreground, which a test run cannot assume.
function Select-Segment {
    param([string]$Match)
    $tree = winapp ui inspect -a $AppPid --interactive 2>&1
    $id = ($tree | Select-String -Pattern $Match -AllMatches).Matches.Value | Select-Object -First 1
    if (-not $id) { throw "segment '$Match' not found" }
    winapp ui focus $id -a $AppPid | Out-Null
    winapp ui send-keys 'enter' --target $id -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 600
    $global:LASTEXITCODE = 0
}

Test-UI 'Settings page opens' {
    winapp ui invoke 'SettingsItem' -a $AppPid
    Start-Sleep -Milliseconds 800
    winapp ui wait-for 'ThemeSelector' -a $AppPid -t 3000
}
Test-UI 'Switch to Light theme' { Select-Segment 'itm-light-\w+' }

# ─── Palette page ────────────────────────────────────────────────────────────
Test-UI 'Nav: Palette exists' { winapp ui wait-for 'NavPalette' -a $AppPid -t 3000 }

Test-UI 'Palette lists at least the seven built-in ramps' {
    winapp ui invoke 'NavPalette' -a $AppPid
    Start-Sleep -Milliseconds 900
    $rows = @((winapp ui inspect 'RampList' -a $AppPid 2>&1 | Select-String -Pattern '^\s+itm-\S+ ListItem').Matches)
    if ($rows.Count -lt 7) { throw "expected at least the 7 built-in ramps, got $($rows.Count)" }
    $global:LASTEXITCODE = 0
}

# Built-ins sort ahead of customs, so row 0 is always one. Selecting it explicitly rather
# than trusting the initial selection keeps this true on a second run against the same app,
# where the singleton view model still holds whatever the previous run selected.
Test-UI 'A built-in ramp is read-only and says so' {
    winapp ui invoke 'NavPalette' -a $AppPid
    Start-Sleep -Milliseconds 900
    $first = ((winapp ui inspect 'RampList' -a $AppPid 2>&1 | Select-String -Pattern '\s(itm-\S+) ListItem').Matches | Select-Object -First 1).Groups[1].Value
    if (-not $first) { throw 'ramp list is empty' }
    winapp ui invoke $first -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 700
    $enabled = winapp ui get-property 'RampName' --property IsEnabled -a $AppPid 2>&1
    if ("$enabled" -notmatch 'IsEnabled:\s*False') { throw "name box should be disabled for a built-in: $enabled" }
    $bar = winapp ui get-property 'BuiltInRampInfoBar' --property IsOffscreen -a $AppPid 2>&1
    if ("$bar" -notmatch 'IsOffscreen:\s*False') { throw "built-in InfoBar should be visible: $bar" }
    $global:LASTEXITCODE = 0
}

# Screen readers get the record's ToString unless the container is named — the swatch
# strip carries no text, so the name is the only thing announced.
Test-UI 'Ramp rows announce their name, not the record ToString' {
    winapp ui invoke 'NavPalette' -a $AppPid
    Start-Sleep -Milliseconds 900
    $rows = winapp ui inspect 'RampList' -a $AppPid 2>&1
    if ("$rows" -match 'ListItem "SkinRamp \{') { throw 'ramp rows announce the raw record ToString' }
    $global:LASTEXITCODE = 0
}

Test-UI 'A new ramp is editable and its hex commits on keystroke' {
    winapp ui invoke 'NavPalette' -a $AppPid
    Start-Sleep -Milliseconds 900
    winapp ui invoke 'BtnNewRamp' -a $AppPid
    Start-Sleep -Milliseconds 700
    winapp ui set-value 'HexStep1' '#102030' -a $AppPid
    Start-Sleep -Milliseconds 500
    $hex = winapp ui get-value 'HexStep1' -a $AppPid 2>&1
    if ("$hex".Trim() -ne '#102030') { throw "hex did not commit: $hex" }

    # Ramps persist to LocalState, so without this every run leaves another "New Ramp"
    # behind in the user's real store.
    winapp ui invoke 'BtnDeleteRamp' -a $AppPid | Out-Null
    Start-Sleep -Milliseconds 500

    $global:LASTEXITCODE = 0
}

# ─── Batch export page ───────────────────────────────────────────────────────
Test-UI 'Batch export page has its controls' {
    winapp ui invoke 'NavPipeline' -a $AppPid
    Start-Sleep -Milliseconds 900
    winapp ui wait-for 'ExportModeSegmented' -a $AppPid -t 3000
    winapp ui wait-for 'BodySheetList' -a $AppPid -t 3000
    winapp ui wait-for 'HairSheetList' -a $AppPid -t 3000
    winapp ui wait-for 'ExportProgress' -a $AppPid -t 3000
}

# The output folder is session state, never persisted, so a freshly launched app always
# starts with it unset — and an export with nowhere to write must not be reachable.
Test-UI 'Export is blocked until an output folder is chosen' {
    winapp ui invoke 'NavPipeline' -a $AppPid
    Start-Sleep -Milliseconds 900
    $folder = winapp ui get-value 'OutputFolderText' -a $AppPid 2>&1
    $enabled = winapp ui get-property 'BtnExport' --property IsEnabled -a $AppPid 2>&1
    if ([string]::IsNullOrWhiteSpace("$folder") -and "$enabled" -notmatch 'IsEnabled:\s*False') {
        throw "no output folder but Export is enabled: $enabled"
    }
    $global:LASTEXITCODE = 0
}

Test-UI 'Export mode description and planned count follow the selection' {
    winapp ui invoke 'NavPipeline' -a $AppPid
    Start-Sleep -Milliseconds 900

    Select-Segment 'itm-flattened-\w+'
    $flattened = "$(winapp ui get-value 'ExportModeDescription' -a $AppPid 2>&1)"
    $flatCount = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)"
    if ($flattened -notmatch 'composited into one texture') { throw "flattened description not shown: $flattened" }

    Select-Segment 'itm-layered-\w+'
    $layered = "$(winapp ui get-value 'ExportModeDescription' -a $AppPid 2>&1)"
    $layerCount = "$(winapp ui get-value 'PlannedCountText' -a $AppPid 2>&1)"
    if ($layered -notmatch 'One file per sheet') { throw "layered description not shown: $layered" }
    if ($flatCount -eq $layerCount) { throw "planned count did not change with mode: $flatCount" }

    $global:LASTEXITCODE = 0
}

# The control applies its own initial index after the page's bindings are live, so a mode
# picked straight after navigation used to be silently overwritten back to Layered.
Test-UI 'A mode picked right after navigation survives' {
    winapp ui invoke 'NavPalette' -a $AppPid
    Start-Sleep -Milliseconds 700
    winapp ui invoke 'NavPipeline' -a $AppPid
    Select-Segment 'itm-both-\w+'
    Start-Sleep -Seconds 2
    $description = "$(winapp ui get-value 'ExportModeDescription' -a $AppPid 2>&1)"
    if ($description -notmatch '^Both:') { throw "mode reverted after navigation: $description" }
    $global:LASTEXITCODE = 0
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

# ─── Screenshots for visual review (UIA assertions can't see clipping/theming) ─
# Look at these. Every assertion above passes on a page that is clipped, overlapping,
# truncated or rendering the wrong theme.
winapp ui screenshot -a $AppPid -o (Join-Path $outDir '01-initial.png') 2>$null
winapp ui invoke 'NavPalette' -a $AppPid 2>$null; Start-Sleep -Milliseconds 900
winapp ui screenshot -a $AppPid -o (Join-Path $outDir '02-palette.png') 2>$null
winapp ui invoke 'NavPipeline' -a $AppPid 2>$null; Start-Sleep -Milliseconds 900
winapp ui screenshot -a $AppPid -o (Join-Path $outDir '03-pipeline.png') 2>$null

# ─── Results ─────────────────────────────────────────────────────────────────
Write-Host "`nPassed: $pass | Failed: $fail"
$results | Where-Object { $_.status -eq 'FAIL' } | ForEach-Object {
    Write-Host "  FAIL: $($_.name) — $($_.detail)" -ForegroundColor Red
}
$results | ConvertTo-Json | Out-File (Join-Path $outDir 'test-results.json')
if ($fail -gt 0) { exit 1 } else { exit 0 }
