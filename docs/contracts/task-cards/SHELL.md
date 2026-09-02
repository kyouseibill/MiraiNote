# 任务卡 · SHELL 桌面壳流

> 你是 Mirai M1 开发的桌面壳子 Agent。本卡自包含。

## 背景

`desktop/` 是 Tauri 2 + Vue3 桌面端骨架（已建好，`npm run dev` 可跑 mock UI）。你负责 Windows 桌面原生能力：托盘、全局热键、捕获小窗、原生通知、自启动。业务页面归 UI 流，与你无文件交集。

## 必读

1. `desktop/README.md` — 结构与运行
2. `desktop/src-tauri/` 现有骨架（Cargo.toml 已声明插件依赖，`lib.rs` 为最小配置）
3. `docs/m1-detailed-design.md` §5.2（窗口与全局交互）
4. `docs/m1-ui-mockups.html` 标签④（悬浮捕获）与⑥（桌面通知）
5. Tauri 2 官方文档（global-shortcut / tray-icon / notification / autostart / store 插件）

## 文件边界

- **允许**：`desktop/src-tauri/**`、`desktop/src/capture/**`（捕获窗专用页面，可新建）、`desktop/src/router/index.ts`（仅限追加 `/capture` 路由这一行）
- **禁止**：`desktop/src/` 其余文件、`backend/`、`frontend/`、`docs/contracts/`

## 任务分解

1. `npm run tauri icon` 生成图标（用仓库内现有 logo 或生成占位图）
2. 托盘：常驻图标 + 菜单（打开主窗/快速捕获/今日概览/暂停提醒/退出）；主窗关闭按钮=最小化到托盘
3. 全局热键 `Ctrl+Shift+Space` 唤起无边框捕获小窗（WebViewWindow label=`capture`，约 720×120，失焦自动隐藏，Esc 关闭）；设置页可改键
4. 捕获窗页面 `desktop/src/capture/CaptureWindow.vue`：输入框 → 调 `@/api/mirai` 的 `createInbox`（source=HotkeyCapture）→ 气泡提示"分拣完成：N 条建议"→ 自动隐藏 + 收件箱角标事件
5. 原生通知：轮询现有 `/memos/due-popups`（30s）→ 系统通知（正文=任务名+简短上下文）→ 点击聚焦主窗跳转任务；每日 ≤5 条节流，可在设置关闭
6. autostart + plugin-store 偏好持久化（热键、通知开关、自启动）
7. `capabilities/default.json` 补齐插件权限

## 验收

1. `npm run tauri dev` 全链路：托盘在、热键在 VS Code/浏览器全屏下可唤起捕获窗、Enter 提交后气泡出现、通知点击正确聚焦
2. 热键冲突检测失败时优雅降级（仅托盘菜单捕获，给出提示）
3. `npm run tauri build` 产出 NSIS 安装包且安装后可运行
4. 与 UI 流零文件冲突（git status 确认不触及对方边界）

## 完成方式

输出：改动文件清单、热键/通知自测记录。**不要 git commit**。
