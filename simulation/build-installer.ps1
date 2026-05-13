# Usage:
#   .\build.ps1                  - build PyInstaller exe + run Inno Setup
#   .\build.ps1 -SkipPyInstaller - skip PyInstaller, only run Inno Setup
#
# Run AFTER Unity has exported its build to build\Windows\.

param(
    [switch]$SkipPyInstaller
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
if (-not $SkipPyInstaller) {
    Write-Host "`n[1/2] Building ScenarioManager.exe with PyInstaller..." -ForegroundColor Cyan

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
            --distpath (Join-Path $BuildDir "") `
            --workpath (Join-Path $PythonDir "build") `
            main.py

        if ($LASTEXITCODE -ne 0) {
            Write-Error "PyInstaller failed (exit code $LASTEXITCODE)."
        }

        Write-Host "[1/2] ScenarioManager.exe built successfully." -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
} else {
    Write-Host "`n[1/2] Skipping PyInstaller (-SkipPyInstaller flag set)." -ForegroundColor Yellow
}

# ── Inno Setup ─────────────────────────────────────────────────────────────────
Write-Host "`n[2/2] Compiling installer with Inno Setup..." -ForegroundColor Cyan

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

Write-Host "[2/2] Installer built successfully." -ForegroundColor Green
Write-Host "`nDone. Installer is in: $BuildDir\installer\" -ForegroundColor White
