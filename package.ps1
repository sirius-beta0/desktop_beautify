<#
.SYNOPSIS
    Package DesktopBeautify into a portable release zip.
.DESCRIPTION
    Publishes the WPF launcher as a self-contained win-x64 app (no .NET install
    needed on the target machine), then compresses it into
    Output/DesktopBeautify-v<version>.zip. Extract and run on any
    Windows 10/11 x64 machine.
#>

param(
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

# Repo root: explicit param wins; otherwise the script's own directory (works on
# normal double-click / command-line runs).
if (-not $RepoRoot) {
    if ($PSScriptRoot) {
        $RepoRoot = $PSScriptRoot
    } else {
        $RepoRoot = [System.IO.Directory]::GetCurrentDirectory()
    }
}
if (-not $RepoRoot) {
    throw "Could not determine repo root. Pass it explicitly: .\package.ps1 -RepoRoot <path>"
}

$ProjectFile = Join-Path $RepoRoot "src/Launcher.App/Launcher.App.csproj"
$OutputDir   = Join-Path $RepoRoot "Output"
$PublishDir  = Join-Path $OutputDir ".publish-tmp"

# Release version (change here when shipping)
$Version  = "0.1.0"
$ZipName  = "DesktopBeautify-v$Version.zip"
$ZipPath  = Join-Path $OutputDir $ZipName

# --- 1. clean previous artifacts ---
if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

# --- 0. locate dotnet (fall back to common install paths if not on PATH) ---
$dotnetExe = $null
$resolved = Get-Command dotnet -ErrorAction SilentlyContinue
if ($resolved) {
    $dotnetExe = $resolved.Source
} else {
    $candidates = @(
        "$env:ProgramFiles\dotnet\dotnet.exe",
        "${env:ProgramFiles(x86)}\dotnet\dotnet.exe",
        "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"
    )
    foreach ($c in $candidates) {
        if (Test-Path $c) {
            $dotnetExe = $c
            break
        }
    }
}
if (-not $dotnetExe) {
    throw "dotnet not found. Install the .NET SDK or add it to PATH."
}
Write-Host "Using dotnet: $dotnetExe" -ForegroundColor DarkGray

# --- 2. self-contained publish win-x64 ---
Write-Host "[1/3] Publishing self-contained win-x64 ..." -ForegroundColor Cyan
& $dotnetExe publish $ProjectFile -c Release -r win-x64 --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed (exit $LASTEXITCODE)"
}

# --- 2.5 copy license & third-party notices next to the exe (合规：随分发附带开源声明) ---
foreach ($doc in @("LICENSE", "THIRD-PARTY-NOTICES.md")) {
    $src = Join-Path $RepoRoot $doc
    if (Test-Path $src) {
        Copy-Item $src $PublishDir -Force
        Write-Host "  included $doc" -ForegroundColor DarkGray
    } else {
        Write-Warning "Missing $doc at repo root; open-source notice will not ship in the zip."
    }
}

# --- 3. zip (contents at archive root so it runs after extract) ---
Write-Host "[2/3] Packaging $ZipName ..." -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $ZipPath -Force

# --- 4. clean temp publish dir ---
Remove-Item $PublishDir -Recurse -Force

$sizeMB = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host "[3/3] Done: $ZipPath ($sizeMB MB)" -ForegroundColor Green
