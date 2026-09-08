<#
.SYNOPSIS
    Publishes DesktopFinance as a self-contained x64 app and builds the MSI installer.

.DESCRIPTION
    1. dotnet publish's the Finance.csproj as a self-contained, win-x64, Release
       build (this pulls in the .NET 8 runtime and the native SQLite driver
       automatically — see Finance.csproj / Microsoft.Data.Sqlite).
    2. dotnet build's the WiX installer project, which harvests every file
       under the publish output into the MSI via the <Files> glob in
       Installer\Package.wxs.

.PARAMETER ProductVersion
    Optional. Overrides the version baked into the MSI (Installer\DesktopFinanceSetup.wixproj's
    ProductVersion). Must be Major.Minor.Build, e.g. "1.2.0". Bump this for every release —
    MajorUpgrade uses it to detect and replace the previous install.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -ProductVersion 1.1.0
#>
param(
    [string]$ProductVersion
)

$ErrorActionPreference = "Stop"

$RepoRoot      = Split-Path -Parent $PSScriptRoot
$CsprojPath    = Join-Path $RepoRoot "Finance.csproj"
$PublishDir    = Join-Path $RepoRoot "publish"
$WixprojPath   = Join-Path $PSScriptRoot "DesktopFinanceSetup.wixproj"

Write-Host "== 1/2: Publishing self-contained win-x64 Release build ==" -ForegroundColor Cyan
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

dotnet publish $CsprojPath `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishDir=$PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

Write-Host "== 2/2: Building MSI with WiX ==" -ForegroundColor Cyan
$wixArgs = @("build", $WixprojPath, "-c", "Release", "-p:PublishDir=$PublishDir\")
if ($ProductVersion) {
    $wixArgs += "-p:ProductVersion=$ProductVersion"
}
dotnet @wixArgs
if ($LASTEXITCODE -ne 0) { throw "WiX build failed." }

$msi = Get-ChildItem (Join-Path $PSScriptRoot "bin") -Filter "DesktopFinanceSetup.msi" -Recurse | Select-Object -First 1
if ($msi) {
    Write-Host "`nInstaller built: $($msi.FullName)" -ForegroundColor Green
} else {
    Write-Warning "Build succeeded but DesktopFinanceSetup.msi was not found under Installer\bin\."
}
