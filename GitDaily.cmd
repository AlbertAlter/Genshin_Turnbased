@echo off
setlocal
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$code = Get-Content -LiteralPath '%~dp0GitDaily.ps1' -Raw -Encoding UTF8; & ([ScriptBlock]::Create($code)) -ScriptDirectory '%~dp0' %*"
if errorlevel 1 pause
endlocal
