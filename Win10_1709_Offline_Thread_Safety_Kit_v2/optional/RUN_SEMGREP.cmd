@echo off
setlocal
if "%~1"=="" (echo Usage: RUN_SEMGREP.cmd "D:\SDK\app" "D:\Reports"&pause&exit /b 1)
set "TARGET=%~1"
set "RESULT=%~2"
if not defined RESULT set "RESULT=%~dp0..\reports_semgrep"
if not exist "%RESULT%" mkdir "%RESULT%"
set "SEMGREP_SEND_METRICS=off"
python -m semgrep scan --config "%~dp0thread_safety_semgrep.yml" --metrics=off --json --output "%RESULT%\semgrep.json" "%TARGET%"
echo Result: %RESULT%\semgrep.json
pause
