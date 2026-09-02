# Mirai M1 API 契约 v1.1（已冻结）

> **变更控制**：本文件与 `types.ts`、`MiraiDtos.cs` 三处同改，仅由主 Agent 执行。子 Agent 禁止修改契约；发现不合理处 → 上报，由主 Agent 统一变更并通知所有流。
> 上游：`docs/m1-detailed-design.md` §4。UI 视觉基准：`docs/m1-ui-mockups.html`。
>
> **v1.1 变更记录（2026-08-22，主 Agent 追认 BE 流实现）**：§2.7/§2.8 增加可选 `tzOffsetMinutes`（默认 0，用于本地日边界换算，补契约缺口）；§2.8 限额按 UTC 日统计；§2.10 分页参数可选（兼容存量客户端）；新增 §2.12 成品文档鉴权下载端点，导出物理路径含 `{userId}` 段。

## 0. 全局约定

| 项 | 约定 |
|---|---|
| Base URL | `{API_BASE}/api/v1`，本地开发默认 `http://localhost:5273/api/v1` |
| 信封 | 沿用现有 `ApiResponse<T>`：`{ "success": bool, "data": T \| null, "message": string \| null }`；业务失败 `success=false` 且 HTTP 状态码语义化（400/404/409/422/429） |
| 分页 | 沿用 `PagedResult<T>`：`{ page, pageSize, total, items }` |
| 认证 | JWT Bearer（现有体系）；Refresh 走 HttpOnly Cookie `/auth/refresh`，前端 401 自动刷新（参照 `frontend/src/api/auth.ts`） |
| 时间 | 服务端存储与返回一律 UTC ISO8601（`2026-08-22T01:20:00Z`）；分拣建议中的本地时间字段以 `Local` 后缀命名，由客户端随请求提供 `localTime` + `tzOffsetMinutes`，服务端负责 Local→UTC 换算 |
| 命名 | JSON camelCase；所有数据库访问经 EF Core LINQ，原生 SQL 一律参数化（验收条件） |

## 1. 枚举

| 枚举 | 值 |
|---|---|
| InboxSource | 1=HotkeyCapture（全局热键）2=TodayBar（今日流捕获条）3=Manual（收件箱手输）4=Retriage（纠错重分拣） |
| InboxStatus | 0=Pending 1=Triaging 2=Triaged 3=Dispatched 4=Discarded 5=Error |
| TriageSuggestionType | `task` / `worklog` / `lifelog` / `knowledge` / `ignore` |
| SessionType | `legacy` / `command` / `context` |
| AttachToType | `worklog` / `lifelog` / `memo` / `inbox` / `briefing` |
| MemoSection | `work` / `life`（沿用现有） |
| TaskPriority | 1=低 2=中 3=高（沿用现有 Memo.Priority） |

## 2. 端点

### 2.1 POST `/mirai/inbox` — 创建捕获项并同步分拣

请求：
```json
{ "raw": "重构方案要过安全评审，老王周三前要排期", "source": 1, "localTime": "2026-08-22T09:20:00", "tzOffsetMinutes": 480 }
```
- `raw`：1..2000 字符，必填；`localTime`：客户端本地时间（无时区后缀）；`tzOffsetMinutes`：UTC 偏移分钟（东八区=480）。

行为：插入 InboxItems(Pending) → 调 DeepSeek 分拣（prompt 见 `docs/prompts/triage-v1.md`，`response_format=json_object`，超时 25s，失败重试 1 次）→ 更新 AiParse/Status。**分拣失败不报错**：仍返回 200，`status=5`、`error` 有值，客户端提供"重试"。

响应 200 `InboxItem`（信封内，下同）：
```json
{
  "id": 101, "raw": "重构方案要过安全评审，老王周三前要排期", "source": 1,
  "status": 2, "aiModel": "deepseek-v4-flash", "correctionNote": null, "error": null,
  "triagedAt": "2026-08-22T01:20:08Z", "createdAt": "2026-08-22T01:20:00Z",
  "aiParse": {
    "items": [
      { "suggestionId": "s1", "type": "task", "confidence": 0.92,
        "rationale": "原文含行动+期限「周三前要排期」",
        "fields": { "content": "推动安全评审排期（老王）", "remindAtLocal": "2026-08-26T09:00", "priority": 2, "section": "work" } }
    ],
    "uncertain": ["「给妈买礼物」的具体日期原文未提及"]
  }
}
```
错误：400（raw 长度/字段缺失）。

### 2.2 GET `/mirai/inbox?status=&page=&pageSize=` — 列表

- `status` 可选（单值枚举过滤）；`page`≥1 默认 1；`pageSize` 1..200 默认 50；默认排除 Discarded；按 `createdAt desc`。
- 响应 200：`PagedResult<InboxItem>`。

### 2.3 POST `/mirai/inbox/{id}/retriage` — 重新分拣

请求：`{ "correction": "第二条不是任务，是想法" }`（可选，≤500，存入 CorrectionNote 并注入 prompt）
响应 200：`InboxItem`（重新分拣后的完整项）。错误：404。

### 2.4 POST `/mirai/inbox/{id}/dispatch` — 确认分发

请求（overrides 仅允许对应类型 fields 的白名单键，深合并覆盖建议值）：
```json
{ "items": [ { "suggestionId": "s1", "overrides": { "priority": 3 } } ] }
```
行为：**单事务**创建全部目标实体（task→Memo、worklog→WorkLog、lifelog→LifeLog），每条写 AIActionLog(Decision=applied)，InboxItem 置 Dispatched。`remindAtLocal` 在此换算为 UTC 存入 Memo.RemindAt，remindMethods 默认 1（弹窗）。

响应 200：
```json
{ "inboxItemId": 101, "created": [ { "suggestionId": "s1", "type": "task", "id": 501, "title": "推动安全评审排期" } ] }
```
`title`：task 取 content、worklog 取 title、lifelog 取 content，截前 50 字。
错误：404；409（非 Triaged 态，如已分发）；422（suggestionId 不在 aiParse 中 / type=knowledge|ignore 不可分发）。

### 2.5 POST `/mirai/inbox/{id}/discard` — 丢弃

无请求体。软删 + AIActionLog(discarded)。响应 204。错误：404；409（已 Dispatched）。

### 2.6 POST `/mirai/inbox/{id}/undo` — 撤销分发

无请求体。软删本次创建的全部实体，AIActionLog 置 undone，InboxItem 回 Triaged。响应 204。错误：404；409（非 Dispatched 态）。UI 展示 30 秒快捷入口，服务端不强制时间窗。

### 2.7 GET `/mirai/day/overview?date=2026-08-22&tzOffsetMinutes=480` — 今日流聚合

- `date` 客户端本地日期（yyyy-MM-dd），必传；`tzOffsetMinutes` 可选（默认 0）——"今日到期/逾期"的本地日边界按它换算。
- 行为：当日无晨报则触发生成（DailyBriefings 唯一索引 + 占位行防并发；生成失败不抛错，置 `briefingError`）。

响应 200 `DayOverview`：
```json
{
  "date": "2026-08-22",
  "briefing": { "id": 12, "date": "2026-08-22", "content": "## 晨报\n今天有 **3 件到期事项**…", "sources": [ { "type": "worklog", "id": 231, "title": "安全评审要求" } ], "model": "deepseek-v4-flash", "generatedAt": "2026-08-21T23:30:00Z" },
  "briefingError": null,
  "dueTasks": [ { "id": 501, "content": "推动安全评审排期", "remindAt": "2026-08-22T06:00:00Z", "priority": 3, "section": "work", "isDone": false, "isPinned": true } ],
  "overdueTasks": [],
  "todayFeed": [ { "time": "2026-08-22T01:41:00Z", "kind": "worklog", "title": "迁移方案 v3 修订完成", "refId": 238, "aiSummary": null } ],
  "inboxPendingCount": 3,
  "weekEntryCount": 23
}
```
- `dueTasks`：今日 00:00–24:00（本地日，按 tzOffset 换算）到期的未完成 Memo；`overdueTasks`：此前逾期未完成。
- `todayFeed.kind`：`capture | worklog | lifelog | memo | task | briefing`，按 time 升序；`aiSummary` M1 恒为 null（字段为 M2 写后提炼预留）。

### 2.8 POST `/mirai/briefing/regenerate` — 重生成晨报

请求：`{ "date": "2026-08-22", "tzOffsetMinutes": 480 }`（date 必传；tzOffsetMinutes 可选默认 0）。
响应 200：`Briefing`。错误：429（每用户每日 regenerate ≤3 次，**按 UTC 日统计**）。

### 2.9 POST `/chat/sessions` — 扩展（现有端点加可选字段）

请求在现有基础上新增可选：`sessionType`、`attachToType`、`attachToObjectId`。
响应：现有会话 DTO + 上述三个可空字段回显。校验：`sessionType='context'` 时 `attachToType/attachToObjectId` 必填且对象必须存在（400）。

### 2.10 GET `/chat/sessions?type=context&page=&pageSize=` — 扩展过滤

现有端点加可选 `type` 过滤（映射 SessionType；`legacy` 可检索存量）。分页参数可选：未传 `pageSize` 时维持存量全量 List 响应（兼容现有客户端）。响应沿用现有结构；DTO 中 null SessionType 统一回显为 `legacy`。

### 2.11 GET `/mirai/stats/ai-actions` — AI 调用统计（设置页）

响应 200：
```json
{ "total": 128, "byActionType": [ { "actionType": "inbox_dispatch", "count": 96 } ], "last7Days": [ { "date": "2026-08-16", "count": 12 } ] }
```

### 2.12 GET `/mirai/exports/{*relativePath}` — 成品文档鉴权下载（v1.1 追认）

- 指令面板导出的成品文档（DOCX/PDF/XLSX）下载。物理路径 `exports\{userId}\yyyy\MM\{file}`：服务端强制校验路径首段为当前用户 Id，**仅本人可下载**（落实设计 §3.5"经鉴权下载而非静态公开"）。
- 响应：文件流（含 Content-Type）。错误：401 / 403（非本人路径）/ 404。

## 3. SSE 流（复用，不重定义）

| 用途 | 端点 | 事件 |
|---|---|---|
| 指令面板（Agent 模式） | 现有 `POST /chat/sessions/{id}/messages/agent/stream` | `user_msg/token/tool_call/tool_progress/tool_result/heartbeat/plan/reflection/confirm/context/done/error` |
| 侧边对话（普通模式） | 现有 `POST /chat/sessions/{id}/messages/stream` | 现有事件集 |

客户端实现参照 `frontend/src/api/agent.ts` + `sse.ts`（含 `consumeSseResponseUntilTerminal` 断流保护），复制到 `desktop/src/api/`。上下文注入（context 会话自动携带对象快照）为服务端行为，客户端无感。

## 4. 错误码汇总

| 码 | 场景 |
|---|---|
| 400 | 请求字段校验失败 |
| 401 | 未认证（触发前端刷新流程） |
| 404 | 资源不存在或无权访问 |
| 409 | 状态冲突（重复分发、撤销非分发态等） |
| 422 | 分发建议 ID/类型不合法 |
| 429 | 晨报重生成超每日限额 |
