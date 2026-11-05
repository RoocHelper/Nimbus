<#
.SYNOPSIS
    Pulls the latest tweak data and PowerShell functions from ChrisTitusTech/winutil into Nimbus.

.DESCRIPTION
    Run this before a release when you want the newest tweaks. Review the git diff afterwards:
    if a new tweak calls a WinUtil helper that Nimbus does not ship yet, add that helper to
    src\Nimbus\Scripts\winutil (or a stand-in to Scripts\NimbusShim.ps1) and add an Indonesian
    translation for it in src\Nimbus\Data\i18n\id.json.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$raw = 'https://raw.githubusercontent.com/ChrisTitusTech/winutil/main'

$configFiles = 'tweaks.json', 'dns.json', 'preset.json', 'appx.json'
foreach ($file in $configFiles) {
    Invoke-WebRequest "$raw/config/$file" -OutFile (Join-Path $root "src\Nimbus\Data\$file")
    Write-Host "config/$file"
}

$scriptDir = Join-Path $root 'src\Nimbus\Scripts\winutil'
foreach ($existing in Get-ChildItem $scriptDir -Filter '*.ps1') {
    $folder = if ($existing.Name -like 'Invoke-WPF*') { 'public' } else { 'private' }
    try {
        Invoke-WebRequest "$raw/functions/$folder/$($existing.Name)" -OutFile $existing.FullName
        Write-Host "functions/$folder/$($existing.Name)"
    } catch {
        Write-Warning "Could not update $($existing.Name): $($_.Exception.Message)"
    }
}

Invoke-WebRequest "$raw/LICENSE" -OutFile (Join-Path $scriptDir 'LICENSE-winutil.txt')

# List helper calls in tweaks.json that Nimbus might not provide yet.
$known = (Get-ChildItem $scriptDir -Filter '*.ps1').BaseName + @(
    'Write-WinUtilLog', 'Step-WinUtilJob', 'Show-WinUtilMessage', 'Measure-WinUtilStep',
    'Invoke-WPFRunspace', 'Invoke-WinutilThemeChange', 'Invoke-WinUtilExplorerUpdate')
$used = Select-String -Path (Join-Path $root 'src\Nimbus\Data\tweaks.json') -Pattern '\b[A-Z][a-z]+-(WinUtil|WPF)[A-Za-z]*' -AllMatches |
    ForEach-Object { $_.Matches.Value } | Sort-Object -Unique
$missing = $used | Where-Object { $known -notcontains $_ }
if ($missing) {
    Write-Warning "tweaks.json calls helpers Nimbus doesn't ship: $($missing -join ', ')"
} else {
    Write-Host "All helper functions used by tweaks.json are available." -ForegroundColor Green
}
