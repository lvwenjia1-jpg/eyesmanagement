param(
    [string]$SourceDir = "D:\eyesmanagement\pc\bin\Debug\net6.0-windows",
    [string]$OutputDir = "D:\eyesmanagement\deploy\windows\output"
)

$ErrorActionPreference = "Stop"

function Write-Utf8NoBomFile {
    param(
        [string]$Path,
        [string]$Content
    )

    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

$resolvedSourceDir = (Resolve-Path $SourceDir).Path
if (-not (Test-Path (Join-Path $resolvedSourceDir "SmartOrder.exe"))) {
    throw "SourceDir does not contain SmartOrder.exe: $resolvedSourceDir"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
$resolvedOutputDir = (Resolve-Path $OutputDir).Path

$stagingDir = Join-Path ([System.IO.Path]::GetTempPath()) ("smartorder-installer-" + [guid]::NewGuid().ToString("N"))
$packageDir = Join-Path $stagingDir "package"
New-Item -ItemType Directory -Force -Path $packageDir | Out-Null

$payloadZipPath = Join-Path $packageDir "payload.zip"
$installCmdPath = Join-Path $packageDir "install.cmd"
$sedPath = Join-Path $stagingDir "smartorder-installer.sed"
$targetSetupPath = Join-Path $resolvedOutputDir "SmartOrder-Setup.exe"

try {
    $sourceItems = Get-ChildItem -Force -Path $resolvedSourceDir
    Compress-Archive -Path $sourceItems.FullName -DestinationPath $payloadZipPath -CompressionLevel Optimal

    $installCmd = @'
@echo off
setlocal
set "INSTALL_DIR=%LOCALAPPDATA%\SmartOrder"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "Expand-Archive -LiteralPath '%~dp0payload.zip' -DestinationPath '%INSTALL_DIR%' -Force"
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ws = New-Object -ComObject WScript.Shell; " ^
  "$desktop = [Environment]::GetFolderPath('Desktop'); " ^
  "$programs = [Environment]::GetFolderPath('Programs'); " ^
  "$startMenuDir = Join-Path $programs 'SmartOrder'; " ^
  "New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null; " ^
  "$targets = @((Join-Path $desktop 'SmartOrder.lnk'), (Join-Path $startMenuDir 'SmartOrder.lnk')); " ^
  "foreach ($shortcutPath in $targets) { " ^
  "  $shortcut = $ws.CreateShortcut($shortcutPath); " ^
  "  $shortcut.TargetPath = Join-Path '%INSTALL_DIR%' 'SmartOrder.exe'; " ^
  "  $shortcut.WorkingDirectory = '%INSTALL_DIR%'; " ^
  "  $shortcut.IconLocation = Join-Path '%INSTALL_DIR%' 'SmartOrder.exe'; " ^
  "  $shortcut.Save(); " ^
  "}"
if errorlevel 1 exit /b 1

start "" "%INSTALL_DIR%\SmartOrder.exe"
exit /b 0
'@
    Write-Utf8NoBomFile -Path $installCmdPath -Content $installCmd

    $sedContent = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=SmartOrder has been installed to %LOCALAPPDATA%\SmartOrder.
TargetName=$targetSetupPath
FriendlyName=SmartOrder Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
SourceFiles=SourceFiles
[SourceFiles]
SourceFiles0=$packageDir
[SourceFiles0]
payload.zip=
install.cmd=
"@
    Write-Utf8NoBomFile -Path $sedPath -Content $sedContent

    & iexpress /N $sedPath | Out-Null
    if (-not (Test-Path $targetSetupPath)) {
        throw "Installer was not created: $targetSetupPath"
    }

    Write-Host "Created installer: $targetSetupPath"
}
finally {
    if (Test-Path $stagingDir) {
        Remove-Item -LiteralPath $stagingDir -Recurse -Force
    }
}
