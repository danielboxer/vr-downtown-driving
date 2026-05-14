# Usage:
#   .\build-installer.ps1           - build PyInstaller exe + run Inno Setup
#   .\build-installer.ps1 -onlyPy   - only build PyInstaller exe
#   .\build-installer.ps1 -onlyInno - only run Inno Setup
#
# Run AFTER Unity has exported its build to build\Windows\.

param(
    [switch]$onlyPy,
    [switch]$onlyInno
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot

# ── Paths ──────────────────────────────────────────────────────────────────────
$PythonDir   = Join-Path $ScriptDir "python"
$BuildDir    = Join-Path $ScriptDir "build"
$InnoScript  = Join-Path $ScriptDir "inno_setup_script.iss"

# Inno Setup compiler - adjust if installed to a non-default location
$InnoCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# ── PyInstaller ────────────────────────────────────────────────────────────────
$totalSteps = if ($onlyPy -or $onlyInno) { 1 } else { 2 }
$step = 0

if (-not $onlyInno) {
    $step++
    Write-Host "`n[$step/$totalSteps] Building ScenarioManager.exe with PyInstaller..." -ForegroundColor Cyan

    if (-not (Test-Path $PythonDir)) {
        Write-Error "Python directory not found: $PythonDir"
    }

    Push-Location $PythonDir
    try {
        # uv run ensures the project's venv is used
        uv run pyinstaller `
            --onefile `
            --noconsole `
            --name ScenarioManager `
            --icon "icon.ico" `
            --add-data "..\Assets\icon.png;." `
            --distpath (Join-Path $BuildDir "") `
            --workpath (Join-Path $PythonDir "build") `
            main.py

        if ($LASTEXITCODE -ne 0) {
            Write-Error "PyInstaller failed (exit code $LASTEXITCODE)."
        }

        Write-Host "[$step/$totalSteps] ScenarioManager.exe built successfully." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} 

# ── Inno Setup ─────────────────────────────────────────────────────────────────
if (-not $onlyPy) {
    $step++
    Write-Host "`n[$step/$totalSteps] Compiling installer with Inno Setup..." -ForegroundColor Cyan

    if (-not (Test-Path $InnoCompiler)) {
        Write-Error "Inno Setup compiler not found at: $InnoCompiler`nInstall Inno Setup 6 or update the `$InnoCompiler path in build.ps1."
    }

    if (-not (Test-Path $InnoScript)) {
        Write-Error "Inno Setup script not found: $InnoScript"
    }

    & $InnoCompiler $InnoScript

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Inno Setup failed (exit code $LASTEXITCODE)."
    }

    Write-Host "[$step/$totalSteps] Installer built successfully." -ForegroundColor Green
    Write-Host "`nDone. Installer is in: $BuildDir\installer\" -ForegroundColor White
} else {
    Write-Host "`nDone. ScenarioManager.exe is in: $BuildDir\" -ForegroundColor White
}
