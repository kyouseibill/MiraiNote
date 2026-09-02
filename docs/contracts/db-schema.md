# Mirai M1 数据库规格 v1.0（已冻结）

> **只增不改**原则：不触碰现有表结构与数据。外键指向的表名（Users/ChatSession 等）以现有库实际命名为准，本 DDL 为规格，最终由 EF Core 迁移生成（BE 流）。
> 迁移执行前置：`BACKUP DATABASE MiraiNote TO DISK = ...`。所有运行时 SQL 走 EF LINQ；涉原生 SQL 一律参数绑定（验收条件）。

## 1. 新表：InboxItems（捕获收件箱）

```sql
CREATE TABLE InboxItems (
    Id             INT IDENTITY PRIMARY KEY,
    UserId         INT NOT NULL,                    -- FK Users(Id)
    Raw            NVARCHAR(2000) NOT NULL,
    Source         TINYINT NOT NULL,                -- 1热键 2今日流 3手动 4纠错重分拣
    Status         TINYINT NOT NULL DEFAULT 0,      -- 0Pending 1Triaging 2Triaged 3Dispatched 4Discarded 5Error
    AiParse        NVARCHAR(MAX) NULL,              -- TriageResult JSON
    AiModel        NVARCHAR(50) NULL,
    CorrectionNote NVARCHAR(500) NULL,
    Error          NVARCHAR(500) NULL,
    TriagedAt      DATETIME2 NULL,                  -- UTC
    -- BaseEntity 标准列（软删 + 审计），与现有表一致：
    IsDeleted      BIT NOT NULL DEFAULT 0,
    CreatedAt      DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy      INT NULL,
    UpdatedAt      DATETIME2 NULL,
    UpdatedBy      INT NULL
);
CREATE INDEX IX_InboxItems_User_Status_Created ON InboxItems (UserId, Status, CreatedAt DESC);
```

## 2. 新表：DailyBriefings（晨报缓存）

```sql
CREATE TABLE DailyBriefings (
    Id           INT IDENTITY PRIMARY KEY,
    UserId       INT NOT NULL,                      -- FK Users(Id)
    BriefDate    DATE NOT NULL,
    Content      NVARCHAR(MAX) NOT NULL,            -- Markdown
    SourcesJson  NVARCHAR(MAX) NULL,                -- List<SourceRef> JSON
    Model        NVARCHAR(50) NULL,
    GeneratedAt  DATETIME2 NOT NULL,
    IsDeleted    BIT NOT NULL DEFAULT 0,
    CreatedAt    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy    INT NULL,
    UpdatedAt    DATETIME2 NULL,
    UpdatedBy    INT NULL
);
-- 过滤唯一索引：防同一用户同日重复生成（并发占位）
CREATE UNIQUE INDEX UX_DailyBriefings_User_Date
    ON DailyBriefings (UserId, BriefDate) WHERE IsDeleted = 0;
```

## 3. 新表：AIActionLogs（AI 写操作审计）

```sql
CREATE TABLE AIActionLogs (
    Id         INT IDENTITY PRIMARY KEY,
    UserId     INT NOT NULL,                        -- FK Users(Id)
    ActionType VARCHAR(50) NOT NULL,                -- inbox_dispatch / inbox_discard / inbox_undo / briefing_regenerate / command_write
    IntentDesc NVARCHAR(500) NULL,                  -- 原始意图（raw 文本）
    TargetType VARCHAR(20) NULL,                    -- memo / worklog / lifelog / ...
    TargetId   INT NULL,
    PayloadJson NVARCHAR(MAX) NULL,                 -- 建议 diff 与用户 overrides
    Decision   VARCHAR(20) NOT NULL,                -- applied / ignored / discarded / undone
    DecidedAt  DATETIME2 NULL,
    IsDeleted  BIT NOT NULL DEFAULT 0,
    CreatedAt  DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedBy  INT NULL,
    UpdatedAt  DATETIME2 NULL,
    UpdatedBy  INT NULL
);
CREATE INDEX IX_AIActionLogs_User_Created ON AIActionLogs (UserId, CreatedAt DESC);
CREATE INDEX IX_AIActionLogs_Target ON AIActionLogs (TargetType, TargetId);
```

## 4. 既有表微调：ChatSession 加 3 可空列

```sql
ALTER TABLE ChatSession ADD SessionType VARCHAR(20) NULL;      -- legacy | command | context
ALTER TABLE ChatSession ADD AttachToType VARCHAR(20) NULL;     -- worklog | lifelog | memo | inbox | briefing
ALTER TABLE ChatSession ADD AttachToObjectId INT NULL;

-- 存量会话标记（一次性，随迁移执行）
UPDATE ChatSession SET SessionType = 'legacy' WHERE SessionType IS NULL;

CREATE INDEX IX_ChatSession_User_Type ON ChatSession (UserId, SessionType);
```

## 5. EF Core 落地要点（BE 流）

1. 三个新实体放入 `MiraiNote.Data/Entities/`，继承 BaseEntity（软删全局过滤器、审计字段自动充填沿用现有机制）。
2. `AiParse`/`SourcesJson`/`PayloadJson` 以 string 列存储，服务层负责 JSON 序列化（System.Text.Json，camelCase 与契约一致）。
3. 过滤唯一索引用 `HasIndex(...).IsUnique().HasFilter("[IsDeleted] = 0")`。
4. 迁移命令使用 MigrationConnection；**迁移前必须完成数据库备份**（人工确认项，见任务卡）。
5. 回滚策略：新表/新列对旧代码无感知，必要时 DROP 新表 + 删除新列即可，无数据损失。
