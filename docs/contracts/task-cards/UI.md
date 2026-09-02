# 任务卡 · UI 界面流

> 你是 Mirai M1 开发的界面子 Agent。本卡自包含。

## 背景

`desktop/`（Vue3 + TS + Tailwind 骨架）已含九页路由、mock 通路（`MIRAI_USE_MOCK=1`）、类型化 API 客户端。你负责把视觉稿实现为完整交互，先 mock 开发，联调后切真实 API。桌面原生能力（托盘/热键/通知）归 SHELL 流。

## 必读

1. `docs/m1-ui-mockups.html` — **视觉与交互基准**（七个标签页，每个下方有设计说明）
2. `docs/contracts/api-contract.md` + `desktop/src/api/types.ts`（契约副本，勿改）
3. `desktop/src/api/mirai.ts` — 现有 mock 通路
4. `frontend/src/api/agent.ts`、`sse.ts` — SSE 消费模式（复制适配）；`frontend/src/composables/useMarkdown.ts` — Markdown 渲染
5. `docs/m1-detailed-design.md` §5.3–§5.4

## 文件边界

- **允许**：`desktop/src/**`（除 `src/capture/` 属 SHELL；`src/api/types.ts` 契约副本勿改）
- **禁止**：`desktop/src-tauri/`、`backend/`、`frontend/`、`docs/contracts/`

## 任务分解（按优先级）

1. **收件箱完整交互**（视觉稿②）：建议卡字段级展示（不再是 JSON pre）；勾选/全选；overrides 行内编辑（改时间/优先级/内容）；纠错重分拣（输入一句话→retriage）；分发结果反馈 + 30s 撤销入口；discard
2. **今日流**（视觉稿①）：晨报 Markdown 渲染（marked+dompurify）+ 源 chips 点击跳转对应对象；到期卡完成/稍后（调现有 memos 状态接口）；逾期区；空态/错误态
3. **指令面板**（视觉稿③）：Ctrl+K 浮层；创建 `sessionType='command'` 会话 → agent SSE 流；工具调用 chips（完成✓/进行中转圈）；流式正文；Confirm 事件弹确认框；done 后"数据来源"折叠区（从 tool_result 收集被检索记录）
4. **侧边对话**（视觉稿⑤）：ContextPanel 完整实现（`sessionType='context'` 会话 + messages/stream）；【转为记录】【存入记忆】动作按钮
5. **工作流/生活流/任务精简页**：列表（现有 API 直读）+ 详情 + 基础编辑 + 💬 挂载侧边对话；生活流图片查看器复用 frontend 逻辑
6. **设置页**：偏好接 plugin-store（与 SHELL 协调键名）；AI 统计已有骨架，补 byActionType 明细

## 交互规范（PRD §6，必须遵守）

- AI 产出一律带"AI"角标；溯源 chips 必须可点击跳转
- 写操作确认后可撤销（inbox 有 undo；其余场景二次确认）
- DeepSeek 不可用/断网：显示友好错误，纯数据区照常
- 所有生成 >1s 的场景必须流式（SSE 已具备）

## 验收（mock 模式）

1. 视觉稿六个界面逐屏对照，布局/文案/状态还原（含空态、错误态、加载骨架）
2. 收件箱全流程：捕获→分拣→勾选改 overrides→分发→撤销，状态流转正确
3. 指令面板 mock SSE（本地模拟事件流）渲染正常；Esc/Ctrl+K 开合；Confirm 拦截
4. `npm run build`（含 vue-tsc）零错误
5. 与 SHELL 流零文件冲突

## 完成方式

输出：改动文件清单、逐屏自测记录（对照视觉稿编号）。**不要 git commit**。
