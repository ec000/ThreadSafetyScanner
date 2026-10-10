@echo off
setlocal
set "TOOL_DIR=%~dp0..\"
set "PATH=%TOOL_DIR%tools;%PATH%"
if "%~1"=="" (
  echo Usage: RUN_PY_RG.cmd "D:\SDK" "D:\Reports"
  pause
  exit /b 1
)
set "DST=%~2"
if not defined DST set "DST=%TOOL_DIR%reports_python_rg"
python "%TOOL_DIR%optional\scan_thread_unsafe.py" "%~1" --out "%DST%"
if errorlevel 1 echo Python-rg scan failed. Check that Python and tools\rg.exe are present.
pause
