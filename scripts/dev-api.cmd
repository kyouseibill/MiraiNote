@echo off
REM Mirai 本地 API 独立启动（连开发配置指向的数据库，端口 5273）
REM 双击本文件即可；窗口显示日志，关闭窗口即停止。

cd /d "%~dp0..\backend\MiraiNote.API"

set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://localhost:5273"
set "Cors__AllowedOrigins__0=http://localhost:5174"
set "Cors__AllowedOrigins__1=http://tauri.localhost"
set "Cors__AllowedOrigins__2=tauri://localhost"

echo Mirai API starting on http://localhost:5273 ...
dotnet run --no-launch-profile
