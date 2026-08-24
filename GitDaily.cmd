@echo off
setlocal
if "%~1"=="" (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$code = Get-Content -LiteralPath '%~dp0GitDaily.ps1' -Raw -Encoding UTF8; & ([ScriptBlock]::Create($code)) -ScriptDirectory '%~dp0' -Action Save"
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$code = Get-Content -LiteralPath '%~dp0GitDaily.ps1' -Raw -Encoding UTF8; & ([ScriptBlock]::Create($code)) -ScriptDirectory '%~dp0' %*"
)
if errorlevel 1 pause
endlocal
