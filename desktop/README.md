# Mirai 桌面端（MiraiNote 桌面版）

Tauri 2 + Vue 3 + TypeScript + Tailwind。M1 详细设计见 `../docs/m1-detailed-design.md`，UI 视觉基准见 `../docs/m1-ui-mockups.html`，API 契约见 `../docs/contracts/api-contract.md`。

## 运行

```bash
# 纯 UI 开发（mock 数据，无需后端）
cp .env.example .env.local   # 保持 MIRAI_USE_MOCK=1
npm install
npm run dev                   # http://localhost:5174

# 完整桌面端（需要 Rust 工具链）
npm run tauri icon path/to/logo.png   # 首次必做：生成 src-tauri/icons
npm run tauri dev
npm run tauri build                   # 产出 NSIS 安装包
```

联调真实后端：`.env.local` 设 `MIRAI_USE_MOCK=0` + `MIRAI_API_BASE=http://localhost:5273/api/v1`（本地跑 backend/MiraiNote.API）。

## 目录与子 Agent 文件边界

```
desktop/
├─ src/                # ← UI 流独占（views / components / stores / api 客户端）
│  ├─ api/types.ts     #   契约副本：由主 Agent 从 docs/contracts/types.ts 同步，勿手改
│  ├─ api/mirai.ts     #   类型化 API + mock 开关（MIRAI_USE_MOCK）
│  └─ router/          #   九页路由（今天/收件箱/工作流/生活流/任务/周报/OKR/记忆/设置）
└─ src-tauri/          # ← SHELL 流独占（托盘 / 全局热键 / 捕获小窗 / 通知 / 自启动）
```

- 两个流的文件边界零交集；`package.json` 根脚本变更归主 Agent。
- 契约（`docs/contracts/`）三件套只有主 Agent 能改；发现不合理 → 上报，勿绕过。

## 约定

- 认证：JWT + HttpOnly Cookie 刷新（`src/api/client.ts`，对齐 frontend 同名模式）。
- 时间：服务端一律 UTC ISO；本地时间仅在分拣建议（`*Local` 字段）出现。
- 样式令牌：`tailwind.config.js`（brand 青绿 / ai 紫 / paper 纸面 / warn 警示），对齐视觉稿。
- 提交规范遵循仓库 `.github/copilot-instructions.md`。
