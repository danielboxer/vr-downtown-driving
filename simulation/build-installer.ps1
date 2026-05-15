# Usage:
#   .\build-installer.ps1           - build PyInstaller exe + run Inno Setup
#   .\build-installer.ps1 -onlyPy   - only build PyInstaller exe
#   .\build-installer.ps1 -onlyInno - only run Inno Setup
#   .\build-installer.ps1 -skipSumo - skip SUMO download (use existing file)
#
# Run AFTER Unity has exported its build to build\Windows\.

param(
    [switch]$onlyPy,
    [switch]$onlyInno,
    [switch]$skipSumo
)

$ErrorActionPreference = "Stop"
$ScriptDir = $PSScriptRoot

# ── Paths ──────────────────────────────────────────────────────────────────────
$PythonDir   = Join-Path $ScriptDir "python"
$BuildDir    = Join-Path $ScriptDir "build"
$InnoScript  = Join-Path $ScriptDir "inno_setup_script.iss"

# Inno Setup compiler - adjust if installed to a non-default location
$InnoCompiler = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"

# SUMO MSI bundled into the installer; downloaded on demand if not already present
$SumoVersion    = "1.22.0"
$SumoMsi        = Join-Path $BuildDir "sumo-win64-$SumoVersion.msi"
$SumoUrl        = "https://sumo.dlr.de/releases/$SumoVersion/sumo-win64-$SumoVersion.msi"
$SumoSha256     = "84FF7BFD5DE4E9F095C61ABE324AE9C2A018DAB84F266ACF27BFF86E766F8487"

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
            # traci is loaded at runtime via sys.path injection (not statically imported),
            # so PyInstaller's static analyzer can't detect its stdlib dependencies.
            # optparse is used by SUMO's traci/sumolib tools and must be explicitly bundled.
            --hidden-import optparse `
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

    if (-not $skipSumo) {
        # Download the SUMO MSI if it is not already cached in the build directory
        if (-not (Test-Path $SumoMsi)) {
            Write-Host "  Downloading SUMO $SumoVersion installer (~150 MB)..." -ForegroundColor DarkCyan
            New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
            # Use curl.exe for large file downloads (Invoke-WebRequest buffers in memory and is very slow for large files)
            curl.exe -L --silent --show-error -o $SumoMsi $SumoUrl
            if ($LASTEXITCODE -ne 0) {
                Remove-Item $SumoMsi -ErrorAction SilentlyContinue -Force
                Write-Error "SUMO download failed (curl exit code $LASTEXITCODE)."
            }
        }

        # Verify the MSI against the known SHA-256 to detect corruption or tampering
        $actualHash = (Get-FileHash -Algorithm SHA256 $SumoMsi).Hash
        if ($actualHash -ne $SumoSha256) {
            Remove-Item $SumoMsi -Force
            Write-Error "SUMO MSI hash mismatch. Expected: $SumoSha256`nActual:   $actualHash`nThe file has been deleted; re-run to re-download."
        }
        Write-Host "  SUMO $SumoVersion verified." -ForegroundColor DarkCyan
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
