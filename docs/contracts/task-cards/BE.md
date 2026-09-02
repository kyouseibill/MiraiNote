# 任务卡 · BE 后端流

> 你是 Mirai（MiraiNote 桌面版）M1 开发的后端子 Agent。本卡自包含，无需其他上下文。

## 背景

MiraiNote（ASP.NET Core 9 + EF Core 9 + SQL Server，部署于 IIS）正在扩展为 AI 助理桌面产品 Mirai。M1 新增：捕获收件箱（AI 分拣）、晨报、今日流聚合、AI 写操作审计。数据库策略为**只增不改**。

## 必读（仓库根相对路径）

1. `docs/contracts/api-contract.md` — 端点契约（**唯一事实**）
2. `docs/contracts/db-schema.md` — 表结构规格
3. `docs/contracts/MiraiDtos.cs` — DTO 定义（拷入 `backend/MiraiNote.Shared/Dtos/Mirai/` 后按解决方案约定适配）
4. `docs/prompts/triage-v1.md`、`docs/prompts/briefing-v1.md` — prompt（以 C# 常量内嵌）
5. `docs/m1-detailed-design.md` §3–§4 — 设计细节
6. 现有代码参照：`backend/MiraiNote.Core/Services/ChatService.cs`（DeepSeek 客户端用法）、`WeeklyReportService.cs`（prompt 约束体系）、`MiraiNote.Data/Entities/`（实体基类约定）

## 文件边界

- **允许**：`backend/**` 全部（新文件为主；`ChatController`/会话创建 DTO 允许加三个可空字段的最小改动）
- **禁止**：`frontend/`、`desktop/`、`docs/contracts/`、`docs/prompts/`（发现契约不合理 → 报告，勿自行绕过）

## 任务分解

1. 三个新实体（InboxItem/DailyBriefing/AIActionLog，继承 BaseEntity）+ ChatSession 三列 + EF 迁移；过滤唯一索引见 db-schema.md
2. `InboxTriageService`：装配 prompt（recentTags 取近 50 条 WorkLog 的 tag 频次 top10）、调 DeepSeek（`response_format=json_object`，超时 25s，失败重试 1 次→Status=Error）、AiParse JSON 存取
3. `MiraiController`（或拆 Inbox/Briefing/DayOverview/Stats）：契约 §2.1–2.8、2.11 全部端点；dispatch 单事务创建实体 + AIActionLog + remindAtLocal→UTC（tzOffsetMinutes）
4. `BriefingService`：事实聚合（EF LINQ）→ prompt → 落库；GET overview 触发 + 占位行防并发；regenerate 限 3 次/日
5. `ContextProvider`：context 会话发消息前注入对象快照（映射 worklog/lifelog/memo/inbox/briefing → 实体查询）
6. `AppOptions` 扩展：`ExportsRoot`/`TempRoot`（缺省回落 fileservice 子目录）；`export_file` 工具落点改 `exports\yyyy\MM\`；temp 每日清理挂现有后台服务体系
7. Chat sessions 端点扩展（sessionType/attachTo*，校验 context 必带对象存在）

## 硬性约束（验收条件）

- 所有数据库访问经 EF LINQ；确需原生 SQL 一律 `SqlParameter`/`FromSqlInterpolated`，**禁止拼接 SQL 字符串**
- 任何凭据/连接串不写入代码与新配置文件模板之外的位置
- 迁移仅在本地开发库执行验证；**生产库迁移由人工执行**（你产出迁移步骤清单即可）
- 遵循 `.github/copilot-instructions.md` 编码规范

## 验收（自测后在报告中列出证据）

1. 契约 11 个端点行为全部符合（含错误码 400/404/409/422/429）
2. 分拣：双意图样本拆分正确；相对日期按 tzOffsetMinutes 换算零时区错误（写单测覆盖 ±12h 区）
3. dispatch 事务性：构造两条建议其一失败 → 全部回滚
4. briefing 并发：并发两次 GET 只生成一条（唯一索引生效）
5. 单测通过 + `dotnet build` 零警告新增

## 完成方式

输出：改动文件清单、迁移步骤、自测结果摘要。**不要 git commit**，等待主 Agent review。
