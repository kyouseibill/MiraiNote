@echo off
REM Mirai 桌面端一键启动：本地 API（连开发配置指向的数据库）+ Tauri 桌面客户端
REM 用法：双击本文件，或在仓库根执行 scripts\dev-desktop.cmd
REM 注意：若 5273/5174 已被占用（之前手动启动过），先关掉旧进程。

setlocal
cd /d "%~dp0.."

echo [1/2] 启动本地 API（http://localhost:5273）...
set "ASPNETCORE_ENVIRONMENT=Development"
set "ASPNETCORE_URLS=http://localhost:5273"
set "Cors__AllowedOrigins__0=http://localhost:5174"
set "Cors__AllowedOrigins__1=http://tauri.localhost"
set "Cors__AllowedOrigins__2=tauri://localhost"
start "Mirai API" /D "%~dp0..\backend\MiraiNote.API" cmd /c "dotnet run --no-launch-profile"

echo [2/2] 启动桌面客户端（Rust 已编译过，几十秒内出窗口；首次编译需几分钟）...
cd /d "%~dp0..\desktop"
call npm run tauri dev

endlocal
