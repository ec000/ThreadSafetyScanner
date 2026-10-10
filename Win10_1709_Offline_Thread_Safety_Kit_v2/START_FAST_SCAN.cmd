@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "ROOT=%~dp0"
set "PATH=%ROOT%tools;%PATH%"
if "%~1"=="" (
    echo Enter SDK source folder, e.g. D:\SDK
    set /p "SRC=Source folder: "
) else (
    set "SRC=%~1"
)
if not defined SRC (echo [ERROR] Empty source folder.& pause & exit /b 1)
if not exist "%SRC%\" (echo [ERROR] Source directory not found: "%SRC%"& pause & exit /b 1)
if "%~2"=="" (set "REPORT=%ROOT%reports_fast") else (set "REPORT=%~2")
if not exist "%ROOT%tools\rg.exe" (
    where rg >nul 2>nul
    if errorlevel 1 (echo [ERROR] rg.exe missing. Put it in "%ROOT%tools\rg.exe"& pause & exit /b 1)
)
where python >nul 2>nul
if errorlevel 1 (echo [ERROR] Python missing from PATH.& pause & exit /b 1)
echo [INFO] Source: "%SRC%"
echo [INFO] Reports: "%REPORT%"
echo [INFO] ripgrep and Python scanning (read-only)...
python "%ROOT%optional\scan_thread_unsafe.py" "%SRC%" --out "%REPORT%"
set "EC=%ERRORLEVEL%"
echo.
if "%EC%"=="0" (
    echo [OK] Scan complete.
) else (
    echo [WARN] Scanner returned errorlevel %EC%. Check rg_stderr.log.
)
echo CSV: "%REPORT%\findings.csv"
echo Summary: "%REPORT%\summary.md"
echo JSON: "%REPORT%\findings.json"
pause
exit /b %EC%
