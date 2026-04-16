@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -Command "Restart-Service -Name 'H3CSwitchPortMonitor' -ErrorAction Stop; Write-Host '服务已重启：H3CSwitchPortMonitor'"
echo.
pause
