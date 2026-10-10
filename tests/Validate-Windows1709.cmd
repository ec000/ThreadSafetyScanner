@echo off
setlocal
"%~dp0ThreadSafetyScanner_Validation_x64.exe" --setup "%~dp0ThreadSafetyScanner_Offline_Setup_x64.exe"
set "validation_code=%ERRORLEVEL%"
echo.
echo Validation exit code: %validation_code%
echo See the evidence directory printed above. Manual checklist remains required.
pause
exit /b %validation_code%
