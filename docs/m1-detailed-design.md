# Mirai · M1（第一阶段）详细设计

| 项目 | 内容 |
|---|---|
| 阶段目标 | 助理上线：桌面壳 + 今日流 + 收件箱分拣闭环 + 指令面板 + 侧边对话 |
| 周期 | 6 周（W1–W6） |
| 上游文档 | `docs/ai-native-assistant-prd.md`（PRD v0.1） |
| 版本 | v1.0（2026-08-22） |
| 状态 | 待评审 |

---

## 1. 范围与总原则

### 1.1 三条工程总原则

1. **数据库"只增不改"**：M1 仅新增 3 张表、给 ChatSession 加 3 个可空列。现有表（Memo/WorkLog/LifeLog/ChatMessage/WeeklyReport/AgentMemory/ScheduledTask）不改名、不改语义。PRD 第 7 节的实体重命名（Task/WorkEntry 等）全部推迟到 M2/M3。收益：Web 端与桌面端并行运行互不影响，出问题随时停用桌面端即可回退，生产库无破坏性迁移。
2. **后端原地扩展**：在现有 `MiraiNote.API` 内新增 Controller/Service/Migration，不新建服务、不改部署（仍 IIS 单实例）。所有 AI 能力继续走 DeepSeek（OpenAI 兼容客户端与 SSE 链路零改动）。
3. **桌面端新建 `desktop/` 目录**：Tauri 2 + Vue3 + TS 全新信息架构，从 `frontend/` 复制可复用的组合式函数与 API 客户端（M1 不抽共享包，M2 再考虑）。`frontend/`（Web 端）冻结，仅安全修复。

### 1.2 M1 明确不做（防范围蔓延）

语音输入（M2，需接 Whisper）、任务子任务/AI 拆解（M2）、周报共创改版与 OKR 文档编写（M2；M1 仅预留导航入口，周报含历史只读）、自动更新（M2，配 Tauri updater）、离线缓存（M2）、月度回顾/情绪洞察/embedding（M3）、Redis/向量库（已决策不引入）。

---

## 2. 系统架构与部署拓扑

```
┌──────────────────────── Windows PC ────────────────────────┐
│  Mirai 桌面端（Tauri 2 壳 + Vue3 UI）                         │
│  ├ 主窗口：今天/收件箱/工作流/生活流/任务 + 侧边对话面板        │
│  ├ Ctrl+K 指令面板（全局浮层）                                │
│  ├ Ctrl+Shift+Space 悬浮捕获条（全局热键）                    │
│  ├ 系统托盘（常驻/开机自启）+ 原生通知                         │
│  └ WebView2 内 fetch 流式消费 SSE                            │
└─────────────── HTTPS ───────────────┬───────────────────────┘
                                      │ JWT Bearer
┌─────────────────────────────────────▼───────────────────────┐
│ 现有服务器（IIS，不动）                                        │
│  MiraiNote.API（.NET 9）                                     │
│  ├ 既有：Auth/WorkLogs/LifeLogs/Memos/Chat(+Agent SSE)/...   │
│  ├ 新增：MiraiController 系（Inbox/Briefing/DayOverview）      │
│  ├ 新增：InboxTriageService / BriefingService / ContextProvider│
│  └ 既有后台服务：提醒扫描/定时任务/记忆衰减                     │
└───────────────┬─────────────────────────────────────────────┘
                │ EF Core 9（LINQ，默认参数化）
       ┌────────▼─────────┐          ┌──────────────────┐
       │ SQL Server       │          │ DeepSeek API      │
       │ 211.136.180.123  │          │ (chat/completions │
       │ MiraiNote 库      │          │  FC + SSE)        │
       │ +3 新表 +3 新列    │          └──────────────────┘
       └──────────────────┘
```

仓库布局：

```
MiraiNote/
  backend/              # 原地扩展（本文档 §4）
  frontend/             # Web 端，冻结
  desktop/              # 新增 Tauri 2 项目（本文档 §5）
  docs/                 # PRD + 本设计
```

---

## 3. 数据库设计

### 3.1 新增表（均继承 BaseEntity：Id 自增 / IsDeleted / CreatedAt / CreatedBy / UpdatedAt / UpdatedBy）

**InboxItems（捕获收件箱）**

| 列 | 类型 | 说明 |
|---|---|---|
| UserId | int, FK User, 索引 | 归属用户 |
| Raw | nvarchar(2000) not null | 原始输入 |
| Source | tinyint not null | 1=热键捕获 2=今日流捕获条 3=手动 4=纠错重分拣 |
| Status | tinyint not null | 0=Pending 1=Triaging 2=Triaged 3=Dispatched 4=Discarded 5=Error |
| AiParse | nvarchar(max) null | 分拣结果 JSON（结构见 §4.2） |
| AiModel | nvarchar(50) null | 分拣所用模型 |
| CorrectionNote | nvarchar(500) null | 用户纠错语（重新分拣时输入） |
| Error | nvarchar(500) null | 失败原因 |
| TriagedAt | datetime2 null | |

索引：`(UserId, Status, CreatedAt desc)`。

**DailyBriefings（晨报缓存）**

| 列 | 类型 | 说明 |
|---|---|---|
| UserId | int, FK | |
| BriefDate | date not null | 唯一约束 (UserId, BriefDate)（过滤 IsDeleted=0） |
| Content | nvarchar(max) not null | Markdown 正文 |
| SourcesJson | nvarchar(max) null | 引用的记录 Id 清单（溯源） |
| Model | nvarchar(50) | |
| GeneratedAt | datetime2 not null | |

**AIActionLogs（AI 写操作审计）**

| 列 | 类型 | 说明 |
|---|---|---|
| UserId | int, FK | |
| ActionType | varchar(50) | inbox_dispatch / briefing_regenerate / command_write |
| IntentDesc | nvarchar(500) | AI/用户的原始意图（raw 文本） |
| TargetType | varchar(20) | memo / worklog / lifelog / … |
| TargetId | int null | 落地后的实体 Id |
| PayloadJson | nvarchar(max) | 建议 diff 与用户 overrides |
| Decision | varchar(20) | applied / ignored / discarded / undone |
| DecidedAt | datetime2 null | |

### 3.2 既有表微调（加可空列，Web 端不受影响）

ChatSession 新增：

| 列 | 类型 | 说明 |
|---|---|---|
| SessionType | varchar(20) null | legacy（存量默认）/ command / context |
| AttachToType | varchar(20) null | worklog / lifelog / memo / inbox / briefing |
| AttachToObjectId | int null | 挂载对象 Id |

存量 ChatSession 迁移脚本统一置 `SessionType = 'legacy'`（参数化 UPDATE）。

### 3.3 数据访问硬性约束（验收条件）

- 全部经 EF Core LINQ 访问（默认参数化）；确需原生 SQL 的场景（如唯一索引过滤、批量 UPDATE）一律使用 `FromSqlInterpolated` / `ExecuteSqlInterpolated` 或显式 `SqlParameter`，**禁止任何字符串拼接/内插值组装 SQL**。
- 迁移步骤：先 `BACKUP DATABASE MiraiNote TO DISK`，再 `dotnet ef database update`（使用 MigrationConnection）。回滚：三张新表与三个可空列对旧代码无感知，无需回滚脚本，必要时 DROP 新表即可。

### 3.4 凭据管理（安全要求）

- 生产连接串仅存于服务器 `appsettings.Production.json`（不入库）或 IIS 环境变量；本次对话与开发配置文件中已明文出现过该数据库口令，**上线前应轮换一次**，并确认 SQL Server 仅开放必要来源 IP 的 1433 端口。
- 桌面端不接触数据库连接串，只持有 API 地址 + JWT。

### 3.5 文件存储（重新规划，W1 实施）

生产文件统一收敛到 `D:\webroot\MiraiNote\fileservice\`，目录规划如下（原工作区文件少且已备份，允许重排）：

```
fileservice\
├─ uploads\          # 用户上传媒体（现有，位置不动）：生活记录图片，URL /uploads/ 静态直出
├─ workspace\        # Agent 工作区（自站点 {ContentRoot}\workspace 迁入）：private\ 草稿 / public\ 发布共享
├─ exports\          # 新增：成品文档（指令面板/回顾导出的 DOCX/PDF/XLSX），按 {userId}\yyyy\MM 子目录存放
└─ temp\             # 新增：即弃文件（Chat 附件解析、M2 语音转写缓存），后台任务每日清理
```

实施要点：

- **配置**：`WorkspaceRoot = D:\webroot\MiraiNote\fileservice\workspace`；新增 `ExportsRoot` / `TempRoot` 两个配置项（AppOptions 扩展，缺省回落 fileservice 对应子目录）。
- **exports 与 workspace 的分工**：workspace 是 Agent 的草稿纸（中间产物），exports 是交给用户的成品；`export_file` 工具落点改为 `exports\{userId}\yyyy\MM\`（文件名带时间戳），经鉴权下载端点（仅本人，按 userId 路径段校验）而非静态公开——成品文档可能含隐私。
- **迁移三步**（低峰执行）：① 暂停写入；② 旧 workspace 文件搬至新位置；③ 核对数据库引用——`WeeklyReportReference.FilePath` 等若含旧 workspace 绝对路径则改为新路径（参数化 UPDATE）。`uploads` 不动，`LifeLog.ImagePath` 不受影响。
- **备份基线**：`fileservice\` 全目录与 SQL Server 备份纳入同一周期（DB 中的路径是磁盘引用，只备库不备文件则引用全裂）。
- **发布安全**：确认站点发布/更新流程不触及 `fileservice\`（它在站点目录之外侧，风险低，但列入 W1 检查项）。

---

## 4. 后端详细设计

### 4.1 新增/扩展 API（前缀 `api/v1`，均 JWT 鉴权）

| 方法与路径 | 说明 |
|---|---|
| `POST /mirai/inbox` | 创建捕获项并**同步分拣**。Body：`{raw, source, localTime, tzOffsetMinutes}`。返回含 AiParse 的完整项。超时 25s，失败落 Status=Error 可重试 |
| `GET /mirai/inbox?status=&page=` | 收件箱列表（含 AiParse） |
| `POST /mirai/inbox/{id}/retriage` | 重新分拣，Body：`{correction?}`（纠错语写入 CorrectionNote 并注入 prompt） |
| `POST /mirai/inbox/{id}/dispatch` | 确认分发。Body：`{items:[{suggestionId, overrides{...}}]}`，事务内创建目标实体 + 写 AIActionLog + 置 Dispatched，返回创建结果引用 |
| `POST /mirai/inbox/{id}/discard` | 丢弃（软删 + AIActionLog Decision=discarded） |
| `POST /mirai/inbox/{id}/undo` | 撤销分发：软删本次创建的实体，AIActionLog 置 undone |
| `GET /mirai/day/overview?date=` | 今日流聚合：当日晨报（无则触发生成）、今日/逾期到期 Memo、今日新增记录摘要、收件箱待处理数 |
| `POST /mirai/briefing/regenerate` | 强制重生成当日晨报 |
| `POST /chat/sessions`（扩展） | 新增可选字段 `sessionType / attachToType / attachToObjectId` |
| `GET /chat/sessions?type=`（扩展） | 按类型过滤（指令历史检索） |

复用不动：`messages/stream`、`messages/agent/stream`、所有 CRUD、Auth、附件解析。

### 4.2 InboxTriageService（分拣）

**输入装配**：raw + 用户时区偏移 +（可选）纠错语 + 用户近期高频 Tags/Category（取最近 50 条 WorkLog 的 tag 频次 top10，帮助对齐命名）。

**输出 JSON Schema（AiParse）**：

```json
{
  "items": [
    {
      "suggestionId": "s1",
      "type": "task | worklog | lifelog | knowledge | ignore",
      "confidence": 0.0,
      "rationale": "一句话依据",
      "fields": {
        "task":    { "content": "", "remindAtLocal": "ISO8601或null", "priority": 1, "section": "work|life" },
        "worklog": { "title": "", "content": "Markdown草稿", "tags": [], "category": "" },
        "lifelog": { "content": "", "mood": "" }
      }
    }
  ],
  "uncertain": ["不确定之处的说明"]
}
```

**Prompt（草案，中文，结构化输出 + 强约束）**：

```
你是个人助理的收件箱分拣器。用户随手丢入一段话，你要把它拆解为 0~N 个结构化条目。
规则：
1. 只依据原文，禁止编造细节；原文未提及的字段留空/null。
2. 一句话可能包含多个意图，逐一拆分（如"提醒+记录"出两条）。
3. 时间解析：以用户当地时区 {localTime, tzOffset} 为准，"周三"指最近的未来周三；
   无法确定的模糊时间（"有空时"）不填 remindAt。
4. 分类标准：有行动+有期限→task；工作事实/过程→worklog；生活事件/心情→lifelog；
   无行动的感想/资料→knowledge；无意义/测试→ignore。
5. confidence<0.6 时必须写入 uncertain 说明原因，不要硬猜。
6. tags 从候选列表优先选用：{recentTags}。
7. 只输出 JSON，不要输出任何其他文字。
```

**执行细节**：`response_format: json_object`（DeepSeek 支持）；解析失败重试 1 次（附错误提示），再失败置 Status=Error。`remindAtLocal` 由服务端用 tzOffset 转 UTC 存入 Memo.RemindAt。分拣是**建议**，不直接写业务表。

### 4.3 BriefingService（晨报）

**触发**：`GET /mirai/day/overview` 发现当日无晨报时生成（每用户每日一次，DB 唯一约束 + 先插占位行防并发重复）；显式 regenerate 覆盖。

**输入**：今日/逾期到期 Memo（含优先级）、昨日 WorkLog/LifeLog 标题列表、未完成 Memo 计数、本周记录数与项目分布、收件箱积压数。全部来自 SQL 查询（LINQ）。

**Prompt 骨架**（沿用周报"只用事实"约束体系）：

```
你是助理，为用户生成今日晨报（Markdown，200字以内正文）。
只准使用【给定事实】中的内容，禁止虚构；每一条结论后标注来源编号。
结构：① 今日到期 N 件事（按优先级排，每件附一句来自历史记录的背景，若事实中有）；
② 昨日一句话回顾；③ 收件箱积压提醒（仅当积压>0）。
语气：简洁、像同事，不用感叹号，不喊口号。
```

输出存 DailyBriefings，SourcesJson 记录引用的记录 Id 供前端渲染溯源 chips。

### 4.4 指令面板（Command Bar）——复用 Agent 流

不新建推理链路。`Ctrl+K` 每次创建 `SessionType='command'` 会话，走**现有** `messages/agent/stream`。在现有主 system prompt 基础上追加面板场景段：

```
当前处于指令面板模式：用户用一句话下达操作/查询/创建/对话意图。
- 涉及用户数据的创建/修改：先调用工具前给出一句将要做什么的说明，执行后报告结果与实体Id。
- 查询类回答末尾列出用到的记录（标题+Id），格式【来源: 标题 #Id】。
- 纯闲聊/通用问题直接回答，不强行调工具。
```

工具链复用现有 40+ FC 工具与危险操作 Confirm 机制。面板 UI 消费既有 SSE 事件（token/tool_call/tool_result/confirm/done）。

### 4.5 ContextProvider（侧边对话上下文）

`SessionType='context'` 的会话，每次发消息前由服务端按 `AttachToType/AttachToObjectId` 拉取对象快照注入 system prompt：

```
【当前讨论对象】类型：工作记录 #12《XXX》
内容：<Markdown 全文>
创建于 …，标签 …
用户会针对这个对象提问，回答时优先依据该对象内容。
```

新映射一处注册：`worklog → WorkLogs / lifelog → LifeLogs / memo → Memos / inbox → InboxItems`。对话结论【转为记录】【存入记忆】复用现有 create 工具（M1 由 UI 按钮触发一次普通调用实现）。

### 4.6 错误与降级

- DeepSeek 超时/5xx：分拣→Status=Error（UI 提供"重试/手动处理"）；晨报→今日流显示"晨报生成失败"占位卡；指令面板→流内 error 事件（现状）。
- 业务功能不依赖 AI 存活：收件箱允许手动"作为记录/任务"直接创建（跳过分拣结果）；今日流除晨报卡外全部为纯数据渲染。

---

## 5. 桌面端详细设计（desktop/，Tauri 2）

### 5.1 技术底座

| 项 | 选择 |
|---|---|
| 壳 | Tauri 2（Rust 壳 + WebView2） |
| UI | Vue 3.5 + TS + Vite + Pinia + Tailwind（与现有前端同栈，样式令牌沿用） |
| 插件 | global-shortcut（Ctrl+Shift+Space）、tray-icon、notification、autostart、store（偏好） |
| 标识 | `com.mirainote.mirai`，产品名 Mirai（全称 MiraiNote） |
| API 基址 | 构建期 env `MIRAI_API_BASE`，运行时可在设置页覆盖（存 plugin-store） |

从 `frontend/src` 复制适配：`api/`（axios 实例 + agent SSE 客户端）、`composables/useMarkdown.ts`（marked+dompurify）、类型定义。登录页复用现有认证流程，token 存 plugin-store（M2 升级 OS 凭据管理器）。

### 5.2 窗口与全局交互

| 元素 | 行为 |
|---|---|
| 主窗口 | 常规带框窗口，导航：今天/收件箱/工作流/生活流/任务/周报/OKR/记忆/设置；周报、OKR、记忆为 M2 预留入口（M1 可见但为占位空态，周报含历史只读） |
| 悬浮捕获窗 | 无边框小窗（约 720×120），全局热键唤起/隐藏，Esc 关闭；Enter 提交→POST /mirai/inbox→"分拣完成 N 条建议"气泡，点击跳收件箱 |
| 指令面板 | 应用内浮层（路由级组件，非 OS 全局），Ctrl+K 唤起；OS 级仅热键捕获条一个，避免热键冲突 |
| 托盘 | 菜单：打开主窗/快速捕获/今日概览（打开主窗定位今天）/暂停提醒/退出；关闭按钮=最小化到托盘 |
| 原生通知 | 到期 Memo（复用现有 due-popups 轮询接口，桌面端拉取后转系统通知，点击聚焦并跳转）；M1 通知不带内联按钮（Tauri 通知插件限制），完成/稍后在应用内操作 |

### 5.3 路由与页面

| 路由 | M1 内容 |
|---|---|
| `/today` | 晨报卡（含重新生成、溯源 chips）、到期任务卡（优先级+提醒方式）、快速捕获条、今日动态时间线、收件箱积压提示 |
| `/inbox` | 左列表（状态过滤）+ 右分拣预览：建议卡片（type 图标、字段 diff、置信度、rationale）、逐条 ✓/✗、全部采纳、纠错重分拣（输入一句话）、分发成功后的撤销入口 |
| `/work` `/life` | 精简流：列表 + 详情 + 基础编辑（沿用现有表单组件）+ 侧边对话入口 |
| `/tasks` | Memo 列表（今日/逾期/全部）+ 状态操作 + 侧边对话入口 |
| `/reports` | 周报预留入口：M2 上线横幅 + **历史周报只读列表**（直读现有 API）；新版生成暂引导回 Web 端 |
| `/okr` | OKR 预留入口：占位空态（产品规划说明，无功能） |
| `/settings` | API 基址、热键、开机自启、通知开关、AI 调用统计（读 AIActionLog 计数） |
| `/login` | 复用现有流程 |

### 5.4 关键组件

`CommandPalette.vue`（全局浮层：输入框 + 流式回答区 + 工具轨迹折叠区"数据来源" + Confirm 弹窗）、`ContextPanel.vue`（右侧抽屉：挂载对话、【转为记录】【存入记忆】动作）、`CaptureBar.vue`、`TriagePreview.vue`、`BriefingCard.vue`、`DueTaskCard.vue`。

Pinia stores：`auth`、`inbox`、`today`、`ui`（面板/抽屉开合）。

---

## 6. 关键流程时序

### 6.1 捕获 → 分拣 → 分发（核心闭环）

```
用户        捕获窗          API                    DeepSeek        SQL Server
 │ 热键+输入  │              │                        │               │
 ├──────────▶│ POST /mirai/inbox {raw, tz}            │               │
 │           ├─────────────▶│ 插 InboxItems(Pending)  │               │
 │           │              ├─分拣 prompt────────────▶│               │
 │           │              │◀────────JSON items──────┤               │
 │           │              ├─更新 AiParse/Triaged──────────────────▶│
 │           │◀─完整项(含建议)─┤                        │               │
 │ 点气泡→收件箱│              │                        │               │
 ├ 勾选+确认  │ POST /dispatch {suggestionId, overrides}              │
 │           ├─────────────▶│ 事务：创建 Memo/WorkLog… + AIActionLog ▶│
 │           │◀─创建结果引用──┤                        │               │
 │ (后悔)撤销 │ POST /undo ──▶│ 软删实体, log=undone ─────────────────▶│
```

### 6.2 打开今日流

```
桌面端 → GET /mirai/day/overview
  API: 查 DailyBriefings(今日) ──无──▶ BriefingService 生成（占位行防并发）→ 落库
       查 Memo(今日/逾期到期) + 今日 WorkLog/LifeLog + Inbox 积压
  ← 聚合 DTO（晨报 + 到期 + 动态 + 积压数），纯数据部分不触发任何 AI 调用
```

### 6.3 侧边对话

```
用户在某 WorkLog 详情点💬 → POST /chat/sessions {type:'context', attachTo:{worklog,12}}
  → 会话列表中打开 ContextPanel，消息走既有 messages/stream
  服务端每次 send 前：ContextProvider 取 #12 快照注入 system prompt
```

---

## 7. 非功能要求

| 项 | 要求 |
|---|---|
| 性能 | 分拣 P95 < 8s（捕获窗内 spinner+流式感）；晨报生成 < 15s；今日流纯数据区 < 500ms 首屏 |
| 时区 | RemindAt 沿用 UTC 存储；所有展示经桌面端本地时区转换；分拣的相对日期解析必须用客户端提供的 tzOffset（服务端不做时区假设） |
| 成本 | 分拣单次 1 次调用（多意图一次拆完）；晨报每用户每日 ≤2 次调用（含重生成）；指令面板按需 |
| 安全 | JWT 沿用；SQL 全参数化（§3.3，验收条件）；dompurify 沿用防 XSS；凭据管理见 §3.4 |
| CORS | API 白名单需包含桌面端来源：dev 为 `http://localhost:5174`；**打包 Tauri 后 Windows WebView2 的 origin 是 `http://tauri.localhost`**（联调 2026-08-22 实测：来源不在白名单时浏览器端报 Network Error）。服务器生产配置 `Cors:AllowedOrigins` 部署时必须加上它 |
| 兼容 | 迁移后 Web 端回归：登录/记录 CRUD/Chat/周报冒烟通过 |

---

## 8. 六周计划

| 周 | 交付 | 验收 |
|---|---|---|
| W1 | DB 迁移（3 表 + 3 列，先备份）；Tauri 项目骨架 + 登录 + 主窗导航（含周报/OKR 占位入口）+ 托盘/热键/通知打通 | 备份完成；热键全局唤起捕获窗；托盘常驻；登录可用；占位入口可见且空态正常 |
| W2 | InboxTriageService + inbox 五个端点；分拣金标准集（30 样本）调 prompt | JSON 解析成功率 ≥95%，样本分类准确率 ≥80% |
| W3 | 收件箱 UI 全流程：列表/预览/确认分发/纠错重分拣/撤销 | 一条双意图样本从捕获到分发 < 30s 且全可用 |
| W4 | 指令面板（command 会话 + Agent 流 + 来源折叠区）；今日流聚合 + 晨报 | 指令面板可完成"查上周记录并建任务"；晨报含溯源 |
| W5 | 侧边对话（ContextProvider + ContextPanel + 转记录/存记忆）；work/life/tasks 精简页 | 任一对象可挂对话且上下文正确注入 |
| W6 | 打磨（空态/错误态/降级路径）、Web 端回归、性能核对、打包 v0.1 安装包 | 全部 §9 验收项通过 |

---

## 9. 测试与验收标准

1. **分拣质量**：30 条金标准样本（会议记录、双意图、相对日期、纯想法、垃圾输入各若干），类型准确率 ≥80%，时间解析零时区错误；解析失败自动重试后仍失败时正确落 Error 态。
2. **闭环完整性**：捕获→分发→撤销全链路数据一致（实体创建又软删，AIActionLog 三态完整）。
3. **溯源**：晨报每条结论可点开来源记录；指令面板查询类回答的"数据来源"区列出真实被检索记录。
4. **降级**：断网/DeepSeek 不可用时，今日流纯数据区、收件箱手动创建、任务列表全部可用。
5. **并行兼容**：迁移后 Web 端冒烟（登录、三流 CRUD、Chat SSE、周报生成）全通过。
6. **桌面体验**：开机自启后托盘在；全局热键与常见软件（VS Code/微信/游戏全屏）无冲突冲突项可改键；通知点击正确聚焦跳转。

---

## 10. 风险与预案

| 风险 | 预案 |
|---|---|
| WebView2 SSE 流式异常 | W1 即做 SSE 通路验证（拿现有 agent 流实测）；异常时降级轮询 |
| 分拣 JSON 漂移 | response_format + 重试 + 金标准集纳入回归；prompt 版本化存代码常量并记录变更 |
| 热键冲突 | 可配置热键；冲突检测失败时仅保留托盘菜单捕获 |
| 生产库迁移事故 | 强制先备份；只增不改策略保证可无损回退 |
| 凭据暴露（已明文流传） | 上线前轮换数据库口令；生产配置不入库；桌面端不接触连接串 |
| 单周交付溢出 | W3/W4 为关键路径，收件箱闭环优先于指令面板打磨；侧边对话可压缩为仅 WorkLog 挂载 |
