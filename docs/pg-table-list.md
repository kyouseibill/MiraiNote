# PostgreSQL 表清单（相对 main@afad560）

新库由迁移 `InitialPostgres` 一次建成。完整建表 SQL 在 `docs/sql/initial-postgres.sql`。旧 SQL Server 库按下面的顺序逐表导入。家务周期表已在 `RemoveHousehold` 中删除，不要导入。

本文件不包含合并或删除账号的步骤。导入程序和本仓库代码都不会自动合并、自动删除重复账号。

## 表与主键

整数 / 长整型主键使用 PostgreSQL `IDENTITY BY DEFAULT`。导入后用文末 SQL 把序列拨到 `MAX(Id)`。`AgentRun.Id` 是客户端生成的 `uuid`，没有序列。

| 表 | 主键 | 序列 |
| --- | --- | --- |
| `User` | `Id` integer | 有 |
| `ChatProject` | `Id` integer | 有 |
| `ChatSession` | `Id` integer | 有 |
| `ChatMessage` | `Id` integer | 有 |
| `AgentRun` | `Id` uuid | 无 |
| `AgentRunEvent` | `Id` bigint | 有 |
| `AgentMemories` | `Id` integer | 有 |
| `AIActionLogs` | `Id` integer | 有 |
| `DailyBriefings` | `Id` integer | 有 |
| `EmailVerifyToken` | `Id` integer | 有 |
| `InboxItems` | `Id` integer | 有 |
| `LifeLog` | `Id` integer | 有 |
| `Memo` | `Id` integer | 有 |
| `RefreshToken` | `Id` integer | 有 |
| `ScheduledTask` | `Id` integer | 有 |
| `WeeklyReport` | `Id` integer | 有 |
| `WeeklyReportReference` | `Id` integer | 有 |
| `WorkLog` | `Id` integer | 有 |
| `WelcomeNewsSeen` | `Id` integer | 有 |

列类型以 `docs/sql/initial-postgres.sql` 为准。相对旧库的结构变化：

- 新增 `User.NormalizedUserName`（`character varying(50)`，未删除行上唯一）。页面仍显示 `Username` 原样。导入时写入 `lower(btrim(Username))`，不要改 `Username` 的大小写。
- `Email` 继续小写存放。导入时写成 `lower(btrim(Email))`。没有 `NormalizedEmail`。
- `bit` 改为 `boolean`。`0` → `false`，`1` → `true`。
- 从旧库导入的已有用户，`IsEmailVerified` 一律写成 `true`。新环境里种子管理员本身也是已验证。
- 不建 `WelcomeGreeting`。旧模型里有过这张表，但没有应用写入入口。旧库如果碰巧有这张表，不要导入。
- `WelcomePhrase` 已由迁移 `DropWelcomePhrase` 删除。问候（时段、下雨、周五）改在服务端代码里，小句改由 DeepSeek 现写，缓存在进程内存里。这张表不要从旧库导入。删表后授权随表消失，不必再 `REVOKE`。`DROP TABLE IF EXISTS` 对已经没有这张表的库也安全。
- 已删除、不要导入的家务表：`Household`、`HouseholdMember`、`HouseholdItem`、`HouseholdItemTemplate`、`HouseholdConsumable`、`HouseholdConsumableReminder`、`HouseholdCompletionRecord`、`HouseholdInvitation`、`HouseholdNotificationSetting`、`HouseholdReminderLog`、`HouseholdChatDraft`。

## 建议的外键导入顺序

1. `User`
2. 只依赖 `User` 的表：`AgentMemories`、`AIActionLogs`、`ChatProject`、`DailyBriefings`、`EmailVerifyToken`、`InboxItems`、`LifeLog`、`Memo`、`RefreshToken`、`ScheduledTask`、`WeeklyReport`、`WeeklyReportReference`、`WorkLog`、`WelcomeNewsSeen`
3. `ChatSession`（依赖 `User`、`ChatProject`）
4. `ChatMessage`、`AgentRun`（依赖 `ChatSession`；`AgentRun` 还依赖 `User`）
5. `AgentRunEvent`（依赖 `AgentRun`）

`ChatSession.BranchedFromSessionId` / `BranchedFromMessageId` 如果旧数据互相引用，先把这两列留空，整表插入后再回填。

## 导入前必须先查大小写重复

在 SQL Server 上执行下面两条汇总。只要任一查询有行，就停下来，把明细交给 Bill 逐个决定。不要自动合并，不要自动删除。

```sql
SELECT LOWER(LTRIM(RTRIM(Email))) AS NormalizedEmail, COUNT(*) AS Cnt
FROM [User]
WHERE IsDeleted = 0
GROUP BY LOWER(LTRIM(RTRIM(Email)))
HAVING COUNT(*) > 1;

SELECT u.Id, u.Username, u.Email, u.IsEmailVerified, u.IsActive
FROM [User] u
WHERE u.IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(u.Email))) IN (
      SELECT LOWER(LTRIM(RTRIM(Email)))
      FROM [User]
      WHERE IsDeleted = 0
      GROUP BY LOWER(LTRIM(RTRIM(Email)))
      HAVING COUNT(*) > 1
  )
ORDER BY LOWER(LTRIM(RTRIM(u.Email))), u.Id;

SELECT LOWER(LTRIM(RTRIM(Username))) AS NormalizedUserName, COUNT(*) AS Cnt
FROM [User]
WHERE IsDeleted = 0
GROUP BY LOWER(LTRIM(RTRIM(Username)))
HAVING COUNT(*) > 1;

SELECT u.Id, u.Username, u.Email, u.IsEmailVerified, u.IsActive
FROM [User] u
WHERE u.IsDeleted = 0
  AND LOWER(LTRIM(RTRIM(u.Username))) IN (
      SELECT LOWER(LTRIM(RTRIM(Username)))
      FROM [User]
      WHERE IsDeleted = 0
      GROUP BY LOWER(LTRIM(RTRIM(Username)))
      HAVING COUNT(*) > 1
  )
ORDER BY LOWER(LTRIM(RTRIM(u.Username))), u.Id;
```

## 时间列

旧库类型取自 main@afad560 的 SQL Server 模型：`DateTime` 列为 `datetime2`，`DailyBriefings.BriefDate` 为 `date`。新库瞬时列为 `timestamp with time zone`，日历日列为 `date`。

上海没有夏令时，UTC+8。导入规则只有三种：原样标记为 UTC（会话时区设为 UTC 后写入，不减 8 小时）、减 8 小时、保持 date。下面没有一列需要减 8 小时。`Memo.RemindAt` 和 `ScheduledTask.ExecuteAt` 已于 2026-10-08 确认，旧口径为 UTC，导入原样标记为 UTC，不减 8 小时。

`CreatedAt` / `UpdatedAt` 在保存时由 `MiraiNoteDbContext.ApplyAudit` 写成 `DateTime.UtcNow`（`backend/MiraiNote.Data/Context/MiraiNoteDbContext.cs:343` 起）。下表不再逐表重复这两列：旧类型 `datetime2`，新类型 `timestamp with time zone`，旧口径 UTC，导入原样标记为 UTC。依据就是这一处。聊天会话还会在 `backend/MiraiNote.Core/Services/ChatService.cs:3195` 把 `UpdatedAt` 设成 `DateTime.UtcNow`，口径相同。

| 表 | 列 | 旧类型 | 新类型 | 旧口径 | 转换规则 | 依据 |
| --- | --- | --- | --- | --- | --- | --- |
| `User` | `LastLoginAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `AuthService.cs:169` `DateTime.UtcNow` |
| `User` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `MiraiNoteDbContext` `ApplyAudit` |
| `EmailVerifyToken` | `ExpiresAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `AuthService.cs:327`、`AuthService.cs:404` `DateTime.UtcNow.Add` |
| `EmailVerifyToken` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `RefreshToken` | `ExpiresAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `AuthService.cs:482` `DateTime.UtcNow.AddDays` |
| `RefreshToken` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `WorkLog` | `LogDate` | datetime2 | date | 纯日期 | 保持 date，不减 8 小时 | `WorkLogService.cs:112`、`:133` 取 `request.LogDate.Date`；聊天工具传入 `yyyy-MM-dd`（`ChatService.cs:2315`） |
| `WorkLog` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `LifeLog` | `LogDate` | datetime2 | date | 纯日期 | 保持 date | `LifeLogService.cs:101`、`:121` `request.LogDate.Date` |
| `LifeLog` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `Memo` | `RemindAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC，不减 8 小时 | 接口原样保存请求值（`MemoService.cs:94`、`:114`）。旧聊天工具用 `DateTime.ToUniversalTime()`（main@afad560 的 `ChatService` / `ServerWriteTools`），无偏移字符串按服务器本地时区解释。新代码改为 `ShanghaiClock.ParseToUtc` |
| `Memo` | `RemindedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `MemoService.cs:172`、`MemoReminderBackgroundService.cs:97`、`:111` `DateTime.UtcNow` |
| `Memo` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `WeeklyReport` | `WeekStart` / `WeekEnd` | datetime2 | date | 纯日期 | 保持 date | `WeeklyReportService.cs:83-84` `request.WeekStart.Date` / `WeekEnd.Date` |
| `WeeklyReport` | `GeneratedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `WeeklyReportService.cs:118`、`:130` `DateTime.UtcNow` |
| `WeeklyReport` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `WeeklyReportReference` | `WeekStart` / `WeekEnd` | datetime2 | date | 纯日期，可空 | 保持 date；空值仍为空 | `WeeklyReportService.cs:216-217` `weekStart?.Date` |
| `WeeklyReportReference` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `DailyBriefings` | `BriefDate` | date | date | 纯日期 | 保持 date | 实体为 `DateOnly`；`BriefingService` 按调用方传入的 `DateOnly` 写入 |
| `DailyBriefings` | `GeneratedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `BriefingService.cs:74`、`:100`、`:137` `DateTime.UtcNow` |
| `DailyBriefings` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `InboxItems` | `TriagedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `InboxTriageService.cs:384` `DateTime.UtcNow` |
| `InboxItems` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `AIActionLogs` | `DecidedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `InboxTriageService.cs:238`、`:277`、`:310`、`:326`；`BriefingService.cs:151` `DateTime.UtcNow` |
| `AIActionLogs` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `ScheduledTask` | `ExecuteAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC，不减 8 小时 | `ScheduledTaskService.cs:42` 原样保存传入值；工具 `ServerScheduleTaskTools.cs:56` 对解析结果调用 `ToUniversalTime()`。带 `Z` 的字符串是 UTC。无偏移字符串取决于旧进程时区 |
| `ScheduledTask` | `ExecutedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ScheduledTaskService.cs:89`、`:99` `DateTime.UtcNow` |
| `ScheduledTask` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `AgentMemories` | `LastAccessedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `AgentMemoryService.cs:76`、`:97`、`:111`、`:128` `DateTime.UtcNow` |
| `AgentMemories` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `AgentRun` | `StartedAt` / `CompletedAt` / `LastActivityAt` / `RecoverableAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `AgentRunService.cs` 多处 `DateTime.UtcNow`（例如 `:72`、`:102-103`、`:141-142`、`:162-163`） |
| `AgentRun` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `AgentRunEvent` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |
| `ChatSession` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit`；`ChatService.cs:3195` 另写 `UpdatedAt = DateTime.UtcNow` |
| `ChatMessage` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ChatService.cs:727`、`:945` 写入 `DateTime.UtcNow`，保存时 `ApplyAudit` 保留已设置的 `CreatedAt` |
| `ChatProject` | `CreatedAt` / `UpdatedAt` | datetime2 | timestamptz | UTC | 原样标记为 UTC | `ApplyAudit` |

没有 `DateTimeOffset` 列。`ChatService.cs:1190` 的 `DateTime.Now` 只用于导出文件名，不入库。

收件箱分发在解析不到本地日期时，会把工作记录日期写成 `DateTime.UtcNow.Date`（`InboxTriageService.cs:213-214`）。那一列仍然是纯日期，导入保持 date。

## 导入后对齐序列

在目标库、数据已经写入之后执行。空表时 `setval` 到 1，并让下一次 `nextval` 仍从 1 开始（第三参数 `false` 只在表空时需要；有数据时用默认的 `true`）。

```sql
SELECT setval(pg_get_serial_sequence('"User"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "User"), 1), 1), (SELECT COUNT(*) > 0 FROM "User"));
SELECT setval(pg_get_serial_sequence('"ChatProject"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "ChatProject"), 1), 1), (SELECT COUNT(*) > 0 FROM "ChatProject"));
SELECT setval(pg_get_serial_sequence('"ChatSession"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "ChatSession"), 1), 1), (SELECT COUNT(*) > 0 FROM "ChatSession"));
SELECT setval(pg_get_serial_sequence('"ChatMessage"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "ChatMessage"), 1), 1), (SELECT COUNT(*) > 0 FROM "ChatMessage"));
SELECT setval(pg_get_serial_sequence('"AgentRunEvent"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "AgentRunEvent"), 1), 1), (SELECT COUNT(*) > 0 FROM "AgentRunEvent"));
SELECT setval(pg_get_serial_sequence('"AgentMemories"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "AgentMemories"), 1), 1), (SELECT COUNT(*) > 0 FROM "AgentMemories"));
SELECT setval(pg_get_serial_sequence('"AIActionLogs"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "AIActionLogs"), 1), 1), (SELECT COUNT(*) > 0 FROM "AIActionLogs"));
SELECT setval(pg_get_serial_sequence('"DailyBriefings"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "DailyBriefings"), 1), 1), (SELECT COUNT(*) > 0 FROM "DailyBriefings"));
SELECT setval(pg_get_serial_sequence('"EmailVerifyToken"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "EmailVerifyToken"), 1), 1), (SELECT COUNT(*) > 0 FROM "EmailVerifyToken"));
SELECT setval(pg_get_serial_sequence('"InboxItems"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "InboxItems"), 1), 1), (SELECT COUNT(*) > 0 FROM "InboxItems"));
SELECT setval(pg_get_serial_sequence('"LifeLog"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "LifeLog"), 1), 1), (SELECT COUNT(*) > 0 FROM "LifeLog"));
SELECT setval(pg_get_serial_sequence('"Memo"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "Memo"), 1), 1), (SELECT COUNT(*) > 0 FROM "Memo"));
SELECT setval(pg_get_serial_sequence('"RefreshToken"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "RefreshToken"), 1), 1), (SELECT COUNT(*) > 0 FROM "RefreshToken"));
SELECT setval(pg_get_serial_sequence('"ScheduledTask"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "ScheduledTask"), 1), 1), (SELECT COUNT(*) > 0 FROM "ScheduledTask"));
SELECT setval(pg_get_serial_sequence('"WeeklyReport"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "WeeklyReport"), 1), 1), (SELECT COUNT(*) > 0 FROM "WeeklyReport"));
SELECT setval(pg_get_serial_sequence('"WeeklyReportReference"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "WeeklyReportReference"), 1), 1), (SELECT COUNT(*) > 0 FROM "WeeklyReportReference"));
SELECT setval(pg_get_serial_sequence('"WorkLog"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "WorkLog"), 1), 1), (SELECT COUNT(*) > 0 FROM "WorkLog"));
SELECT setval(pg_get_serial_sequence('"WelcomeNewsSeen"', 'Id'), GREATEST(COALESCE((SELECT MAX("Id") FROM "WelcomeNewsSeen"), 1), 1), (SELECT COUNT(*) > 0 FROM "WelcomeNewsSeen"));
```
