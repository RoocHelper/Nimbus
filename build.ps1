<#
.SYNOPSIS
    Builds Nimbus locally.

.EXAMPLE
    .\build.ps1              # build and run (Debug)
    .\build.ps1 -Publish     # self-contained build in .\publish
    .\build.ps1 -Pack 1.0.0  # publish + create Setup.exe in .\Releases (needs: dotnet tool install -g vpk)
#>
param(
    [switch]$Publish,
    [string]$Pack
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\Nimbus\Nimbus.csproj'

if (-not $Publish -and -not $Pack) {
    dotnet run --project $project
    return
}

$version = if ($Pack) { $Pack } else { '1.0.0' }
$out = Join-Path $PSScriptRoot 'publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$version -o $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
Write-Host "Published to $out" -ForegroundColor Green

if ($Pack) {
    vpk pack --packId Nimbus --packVersion $version --packDir $out --mainExe Nimbus.exe --packTitle Nimbus `
        --icon (Join-Path $PSScriptRoot 'src\Nimbus\Assets\nimbus.ico') --outputDir (Join-Path $PSScriptRoot 'Releases')
    if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }
    Write-Host "Installer created in .\Releases (Nimbus-win-Setup.exe)" -ForegroundColor Green
}
