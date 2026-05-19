param(
    [string]$SourceDir = "D:\eyesmanagement\pc\bin\Debug\net6.0-windows",
    [string]$OutputDir = "D:\eyesmanagement\deploy\windows\output",
    [string]$IssPath = "D:\eyesmanagement\deploy\windows\smartorder-inno.iss"
)

$ErrorActionPreference = "Stop"

function Get-InnoCompilerPath {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "ISCC.exe was not found."
}

$resolvedSourceDir = (Resolve-Path $SourceDir).Path
$resolvedIssPath = (Resolve-Path $IssPath).Path
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$resolvedOutputDir = (Resolve-Path $OutputDir).Path

$mainExePath = Join-Path $resolvedSourceDir "SmartOrder.exe"
if (-not (Test-Path $mainExePath)) {
    throw "SmartOrder.exe was not found in source dir: $resolvedSourceDir"
}

$versionInfo = (Get-Item $mainExePath).VersionInfo
$appVersion = $versionInfo.ProductVersion
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    $appVersion = $versionInfo.FileVersion
}
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    $appVersion = "1.0.0"
}

$normalizedVersion = ($appVersion -split '\+')[0].Trim()
if ([string]::IsNullOrWhiteSpace($normalizedVersion)) {
    $normalizedVersion = "1.0.0"
}

$compilerPath = Get-InnoCompilerPath

Write-Host "Source dir: $resolvedSourceDir"
Write-Host "App version: $normalizedVersion"
Write-Host "Output dir: $resolvedOutputDir"

& $compilerPath `
    "/DSourceDir=$resolvedSourceDir" `
    "/DOutputDir=$resolvedOutputDir" `
    "/DAppVersion=$normalizedVersion" `
    $resolvedIssPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compile failed with exit code $LASTEXITCODE."
}

$stableInstallerPath = Join-Path $resolvedOutputDir "SmartOrder-Setup.exe"
$versionedInstallerPath = Join-Path $resolvedOutputDir ("SmartOrder-Setup-" + $normalizedVersion + ".exe")

if (-not (Test-Path $stableInstallerPath)) {
    throw "Installer was not created: $stableInstallerPath"
}

Copy-Item -LiteralPath $stableInstallerPath -Destination $versionedInstallerPath -Force

Write-Host "Created installers:"
Write-Host "  Stable: $stableInstallerPath"
Write-Host "  Versioned: $versionedInstallerPath"
