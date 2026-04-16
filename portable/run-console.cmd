@echo off
chcp 65001 >nul
cd /d "%~dp0"
echo H3C 交换机端口监控程序 - 前台运行模式
echo.
echo 请先确认 appsettings.json 已配置飞书机器人和交换机信息。
echo 如果程序启动失败，请查看 logs\startup-error.log。
echo 按 Ctrl+C 可停止前台运行。
echo.
H3CSwitchPortMonitor.exe
echo.
echo 程序已退出。
pause
