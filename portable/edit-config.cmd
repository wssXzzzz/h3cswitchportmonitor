@echo off
chcp 65001 >nul
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -STA -File "%~dp0config-editor.ps1"
if errorlevel 1 (
  echo.
  echo 配置工具启动失败。请查看上面的错误，或双击 edit-config-raw.cmd 用记事本编辑 appsettings.json。
  pause
)
