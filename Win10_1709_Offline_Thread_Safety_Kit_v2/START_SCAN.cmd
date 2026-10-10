@echo off
setlocal EnableExtensions DisableDelayedExpansion
set "TOOL_DIR=%~dp0"
if "%~1"=="" (
  echo Enter LOCAL source directory, e.g. D:\RealtekSDK
  set /p "SRC=Source folder: "
) else (
  set "SRC=%~1"
)
if not defined SRC (echo ERROR: Empty source path.& pause & exit /b 1)
if "%~2"=="" (set "RESULT=%~dp0reports") else (set "RESULT=%~2")
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%TOOL_DIR%scan_win1709.ps1" -Source "%SRC%" -Out "%RESULT%"
set "EC=%ERRORLEVEL%"
echo.
echo Exit code: %EC%
echo Results: %RESULT%
pause
exit /b %EC%
