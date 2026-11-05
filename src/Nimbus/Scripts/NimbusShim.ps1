# -----------------------------------------------------------------------------
# NimbusShim.ps1
# Provides the small runtime that the bundled WinUtil functions expect ($sync,
# logging, job progress, message boxes) and then loads them. WinUtil is MIT
# licensed: https://github.com/ChrisTitusTech/winutil (see winutil\LICENSE-winutil.txt)
#
# Nimbus (C#) reads standard output. Lines that start with "##NIMBUS:" are
# protocol messages:
#   ##NIMBUS:PROGRESS|<percent>|<status>
#   ##NIMBUS:LOG|<LEVEL>|<component>|<message>
#   ##NIMBUS:APPX|<package name>
#   ##NIMBUS:ERROR|<message>
# -----------------------------------------------------------------------------

if (-not $NimbusData) { $NimbusData = Join-Path (Split-Path $PSScriptRoot -Parent) 'Data' }
if (-not $NimbusWork) { $NimbusWork = Join-Path $env:LOCALAPPDATA 'NimbusWork' }
if (-not (Test-Path $NimbusWork)) { New-Item -ItemType Directory -Path $NimbusWork -Force | Out-Null }

function Read-NimbusJson {
    param([string]$Name)
    $path = Join-Path $NimbusData $Name
    if (Test-Path -LiteralPath $path) {
        return (Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json)
    }
    return $null
}

$global:sync = [hashtable]::Synchronized(@{})
$global:sync.configs = @{}
$global:sync.configs.tweaks = Read-NimbusJson 'tweaks.json'
$global:sync.configs.dns = Read-NimbusJson 'dns.json'
$global:sync.configs.appx = Read-NimbusJson 'appx.json'
$global:sync.ThemeButton = [pscustomobject]@{ Content = '' }
$global:sync.winutildir = $NimbusWork
$global:WinUtilJobErrorCount = 0

# ---- Replacements for WinUtil's UI/job helpers ------------------------------

function Write-WinUtilLog {
    param(
        [string]$Level = 'INFO',
        [string]$Component = '',
        [string]$Message = ''
    )
    if ($Level -eq 'ERROR') { $global:WinUtilJobErrorCount++ }
    Write-Host "##NIMBUS:LOG|$($Level.ToUpperInvariant())|$Component|$Message"
}

function Write-NimbusProgress {
    param([int]$Percent = -1, [string]$Status = '')
    Write-Host "##NIMBUS:PROGRESS|$Percent|$Status"
}

function Step-WinUtilJob {
    param(
        [string]$Status,
        [int]$Percent = -1,
        [string]$State,
        [string]$Overlay,
        [switch]$Hide
    )
    if ($Status -or $Percent -ge 0) {
        Write-Host "##NIMBUS:PROGRESS|$Percent|$Status"
    }
}

function Show-WinUtilMessage {
    param($Message, $Title, $Button, $Icon)
    # Nimbus asks for confirmation in its own UI before running anything.
    if ($Message) { Write-Host $Message }
    return 'Yes'
}

function Measure-WinUtilStep {
    param($Scope, $Name, [scriptblock]$ScriptBlock)
    & $ScriptBlock
}

function Invoke-WPFRunspace {
    param([scriptblock]$ScriptBlock, $ArgumentList, $ParameterList, [switch]$Wait)
    & $ScriptBlock
}

function Invoke-WinutilThemeChange {
    param($theme)
}

function Invoke-WinUtilExplorerUpdate {
    param([string]$action = 'refresh')

    if ($action -eq 'refresh') {
        if (-not ([System.Management.Automation.PSTypeName]'NimbusWin32').Type) {
            Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class NimbusWin32 {
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = false)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
}
"@
        }
        $result = [IntPtr]::Zero
        $null = [NimbusWin32]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [IntPtr]::Zero, 'ImmersiveColorSet', 0x2, 100, [ref]$result)
    } elseif ($action -eq 'restart') {
        taskkill.exe /F /IM 'explorer.exe' | Out-Null
        Start-Process 'explorer.exe'
    }
}

# ---- Load the bundled WinUtil functions --------------------------------------

foreach ($file in (Get-ChildItem -LiteralPath (Join-Path $NimbusScripts 'winutil') -Filter '*.ps1')) {
    . $file.FullName
}

# ---- Nimbus helpers -----------------------------------------------------------

function Invoke-NimbusTweak {
    param([string]$Key, [switch]$Undo)
    if ($null -eq $global:sync.configs.tweaks.$Key) {
        Write-Warning "Tweak $Key was not found."
        return
    }
    Invoke-WinUtilTweaks -CheckBox $Key -undo ([bool]$Undo)
}

function Set-NimbusComboState {
    param([string]$Key, [string]$State)
    $registry = @($global:sync.configs.tweaks.$Key.registry)
    Set-WinUtilRegistryComboState -Registry $registry -State $State
    Invoke-WinUtilExplorerUpdate
}

function Set-NimbusDns {
    param([string]$Provider)
    $result = @(Set-WinUtilDNS -DNSProvider $Provider)
    if ($result.Count -eq 0 -or $result[-1] -ne $true) {
        throw "DNS change to $Provider failed."
    }
}

function Get-NimbusInstalledAppx {
    $names = @(Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name -Unique)
    foreach ($name in $names) {
        Write-Host "##NIMBUS:APPX|$name"
    }
}

function Remove-NimbusAppx {
    param([string[]]$PackageIds)
    $i = 0
    foreach ($id in $PackageIds) {
        $i++
        Write-NimbusProgress -Percent ([int](($i - 1) * 100 / $PackageIds.Count)) -Status $id
        Remove-WinUtilAPPX -Name $id
    }
    Remove-WinUtilProvisionedAPPX -PackageList $PackageIds
}

function Install-NimbusAppx {
    param([string]$PackageId, [string]$StoreId)

    # 1) Re-register a local manifest if Windows still has the package files.
    $manifest = @(
        Get-AppxPackage -AllUsers -Name $PackageId -ErrorAction SilentlyContinue |
            Where-Object { $_.InstallLocation } |
            ForEach-Object { Join-Path $_.InstallLocation 'AppxManifest.xml' }
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    if ($manifest) {
        Add-AppxPackage -Register $manifest -DisableDevelopmentMode -ErrorAction Stop
        Write-Host "Registered $PackageId from $manifest"
        return
    }

    # 2) Otherwise install it from the Microsoft Store with WinGet.
    if ([string]::IsNullOrWhiteSpace($StoreId)) {
        throw "No local copy or Microsoft Store ID for $PackageId."
    }
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "WinGet is not available, so $PackageId cannot be installed from the Store."
    }
    & $winget.Source install --id $StoreId --source msstore --accept-package-agreements --accept-source-agreements --silent
    if ($LASTEXITCODE -ne 0) {
        throw "WinGet could not install $PackageId (exit code $LASTEXITCODE)."
    }
}
