@echo off
setlocal EnableExtensions DisableDelayedExpansion
if "%~1"=="" (
  echo Usage: RUN_CPPCHECK.cmd "D:\SDK\app" "D:\audit_cppcheck" unix32
  pause
  exit /b 1
)
set "TARGET=%~1"
set "RESULT=%~2"
if not defined RESULT set "RESULT=%~dp0..\reports_cppcheck"
set "PLATFORM=%~3"
if not defined PLATFORM set "PLATFORM=unix32"
if /i not "%PLATFORM%"=="unix32" if /i not "%PLATFORM%"=="unix64" (
  echo [ERROR] Platform should be unix32 or unix64.
  pause
  exit /b 1
)
set "CPPCHECK="
if exist "%ProgramFiles%\Cppcheck\cppcheck.exe" set "CPPCHECK=%ProgramFiles%\Cppcheck\cppcheck.exe"
if not defined CPPCHECK if defined ProgramFiles(x86) if exist "%ProgramFiles(x86)%\Cppcheck\cppcheck.exe" set "CPPCHECK=%ProgramFiles(x86)%\Cppcheck\cppcheck.exe"
if not defined CPPCHECK for /f "delims=" %%I in ('where cppcheck 2^>nul') do if not defined CPPCHECK set "CPPCHECK=%%I"
if not defined CPPCHECK (
  echo [ERROR] cppcheck.exe not found; add its directory to PATH or update RUN_CPPCHECK.cmd.
  pause
  exit /b 1
)
if not exist "%TARGET%\" (echo [ERROR] Source folder not found: "%TARGET%"& pause & exit /b 1)
if not exist "%RESULT%" mkdir "%RESULT%"
echo [INFO] Cppcheck: "%CPPCHECK%"
echo [INFO] Source: "%TARGET%"
echo [INFO] Target platform: %PLATFORM%
"%CPPCHECK%" --enable=warning,style,performance,portability --xml --xml-version=2 --platform=%PLATFORM% --library=posix --output-file="%RESULT%\cppcheck.xml" "%TARGET%"
set "EC=%ERRORLEVEL%"
echo [INFO] XML report: "%RESULT%\cppcheck.xml"
echo [INFO] Exit code: %EC%
pause
exit /b %EC%
