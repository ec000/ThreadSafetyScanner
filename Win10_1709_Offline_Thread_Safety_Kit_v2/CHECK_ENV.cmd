@echo off
setlocal EnableExtensions DisableDelayedExpansion
chcp 65001 >nul 2>nul
set "ROOT=%~dp0"
echo ===== Windows / PowerShell =====
ver
powershell.exe -NoLogo -NoProfile -Command "$PSVersionTable.PSVersion.ToString(); [Environment]::Is64BitOperatingSystem"
echo.
echo ===== Python =====
where python 2>nul
if errorlevel 1 (
    echo [WARN] Python not found in PATH.
) else (
    python --version
)
echo.
echo ===== ripgrep =====
set "RG_EXE="
if exist "%ROOT%tools\rg.exe" set "RG_EXE=%ROOT%tools\rg.exe"
if not defined RG_EXE for /f "delims=" %%I in ('where rg 2^>nul') do if not defined RG_EXE set "RG_EXE=%%I"
if defined RG_EXE (
    echo [OK] "%RG_EXE%"
    "%RG_EXE%" --version
) else (
    echo [WARN] rg.exe not found in tools folder or PATH.
)
echo.
echo ===== Cppcheck =====
set "CPP_EXE="
if exist "%ProgramFiles%\Cppcheck\cppcheck.exe" set "CPP_EXE=%ProgramFiles%\Cppcheck\cppcheck.exe"
if not defined CPP_EXE if exist "%ProgramFiles(x86)%\Cppcheck\cppcheck.exe" set "CPP_EXE=%ProgramFiles(x86)%\Cppcheck\cppcheck.exe"
if not defined CPP_EXE for /f "delims=" %%I in ('where cppcheck 2^>nul') do if not defined CPP_EXE set "CPP_EXE=%%I"
if defined CPP_EXE (
    echo [OK] "%CPP_EXE%"
    "%CPP_EXE%" --version
) else (
    echo [WARN] cppcheck.exe not found automatically. Locate its install folder.
)
echo.
echo Semgrep: optional; not required for basic scanning.
echo.
echo First-pass Python+rg scan: START_FAST_SCAN.cmd "D:\SDK" "D:\scan_results"
echo Optional standalone PowerShell scan: START_SCAN.cmd "D:\SDK" "D:\scan_ps"
echo.
pause
