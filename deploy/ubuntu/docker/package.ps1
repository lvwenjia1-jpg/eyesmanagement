param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "package-output"),
    [string]$ImageTag = "eyesmanagement/mainapi:latest"
)

$ErrorActionPreference = "Stop"

function Write-Step([string]$Message) {
    Write-Host $Message -ForegroundColor Cyan
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..\..")).Path
$packageRoot = Join-Path $OutputDir "mainapi-docker-package"
$deployDir = Join-Path $packageRoot "deploy\ubuntu\docker"
$tarPath = Join-Path $deployDir "mainapi-latest.tar"
$zipPath = Join-Path $OutputDir "mainapi-docker-package.zip"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker CLI was not found. Please install Docker Desktop or Docker Engine first."
}

Write-Step "Cleaning output directory..."
if (Test-Path $packageRoot) {
    Remove-Item -Recurse -Force $packageRoot
}
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

New-Item -ItemType Directory -Force -Path $deployDir | Out-Null

Write-Step "Building Docker image..."
& docker build -f (Join-Path $repoRoot "MainApi\Dockerfile") -t $ImageTag $repoRoot
if ($LASTEXITCODE -ne 0) {
    throw "Docker image build failed."
}

Write-Step "Saving Docker image tar..."
& docker save -o $tarPath $ImageTag
if ($LASTEXITCODE -ne 0) {
    throw "Docker image export failed."
}

Write-Step "Copying deployment files..."
Copy-Item (Join-Path $PSScriptRoot "docker-compose.yml") $deployDir
Copy-Item (Join-Path $PSScriptRoot "docker-compose.external-mysql.yml") $deployDir
Copy-Item (Join-Path $PSScriptRoot "deploy.sh") $deployDir
Copy-Item (Join-Path $PSScriptRoot "deploy-external-mysql.sh") $deployDir
Copy-Item (Join-Path $PSScriptRoot "run-from-publish.sh") $deployDir
Copy-Item (Join-Path $PSScriptRoot "Dockerfile.publish") $deployDir
Copy-Item (Join-Path $PSScriptRoot "mainapi.docker.env.example") $deployDir
Copy-Item (Join-Path $PSScriptRoot "mainapi.external-mysql.env.example") $deployDir
Copy-Item (Join-Path $PSScriptRoot "README.md") $deployDir

$manifest = @"
MainApi Docker Deployment Package
CreatedUtc: $(Get-Date -AsUTC -Format "yyyy-MM-dd HH:mm:ss")
ImageTag: $ImageTag
RepositoryRoot: $repoRoot

Contents:
- deploy\ubuntu\docker\mainapi-latest.tar
- deploy\ubuntu\docker\docker-compose.yml
- deploy\ubuntu\docker\docker-compose.external-mysql.yml
- deploy\ubuntu\docker\deploy.sh
- deploy\ubuntu\docker\deploy-external-mysql.sh
- deploy\ubuntu\docker\run-from-publish.sh
- deploy\ubuntu\docker\Dockerfile.publish
- deploy\ubuntu\docker\mainapi.docker.env.example
- deploy\ubuntu\docker\mainapi.external-mysql.env.example
- deploy\ubuntu\docker\README.md

Usage:
1. Copy the package to Ubuntu.
2. Unzip it.
3. Enter deploy/ubuntu/docker.
4. Copy mainapi.docker.env.example to mainapi.docker.env and edit secrets.
5. Run ./deploy.sh
6. For external MySQL, copy mainapi.external-mysql.env.example to mainapi.external-mysql.env and run ./deploy-external-mysql.sh
7. Or run ./run-from-publish.sh /path/to/publish/mainapi
"@
Set-Content -Path (Join-Path $packageRoot "PACKAGE-INFO.txt") -Value $manifest -Encoding UTF8

Write-Step "Compressing delivery package..."
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -Force

Write-Host ""
Write-Host "Done: $zipPath" -ForegroundColor Green
