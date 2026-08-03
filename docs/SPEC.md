# MiraiNote — 项目规格文档 (SPEC.md)

> 版本：v1.4  
> 最后更新：2026-06  
> 技术栈：Vue 3 + Tailwind CSS + C# ASP.NET Core + SQL Server + DeepSeek API

---

## 1. 项目概述

**MiraiNote**（未来ノート）是一款面向个人用户的 Web 助理应用，支持电脑和手机浏览器访问。  
核心定位：**工作记录 + 生活备忘 + AI 周报生成 + 智能对话**，帮助用户沉淀每日信息、减少遗忘、提升效率。

应用分为两大板块，视觉风格明显区分：
- **工作板块**：聚焦职场事务，偏专业简洁风，包含工作记录、工作备忘、周报生成
- **生活板块**：聚焦个人生活，偏轻松温暖风，包含生活备忘、生活记录
- **智能工具**：AI 对话、周报参考文件管理、快速捕获

MiraiNote 的 AI 能力不止于"被动响应"——通过 Agent 功能，系统可主动推送每日简报、协作撰写周报、感知用户情绪趋势，真正从"工具集合"进化为**主动式个人助理**。

---

## 2. 核心功能模块

### 工作板块

#### 2.1 工作记录（WorkLog）
- 创建、编辑、删除工作记录条目
- 字段：标题、目的、内容（Markdown）、标签、项目分类、记录日期
- 支持按日期、标签、项目筛选和关键词搜索
- 用于沉淀每日工作内容，供 AI 周报生成参考

#### 2.2 工作备忘（Memo - work）
- 记录工作相关待办事项，支持设置提醒时间
- 提醒方式：弹窗通知（Web UI）/ 邮件通知（可选）/ **Web Push 通知（PWA，浏览器关闭后仍可提醒）**
- 支持**周期性提醒**（每天 / 每周循环）和**提前提醒**（提前 10 分钟 / 1 小时 / 1 天）
- 支持置顶、完成标记、归档
- 字段：内容、提醒时间、优先级（高/中/低）、完成状态

#### 2.3 周报生成（WeeklyReport）
- 选择时间范围（默认本周）
- 一键调用 AI（DeepSeek）汇总本周工作记录，生成结构化周报
- AI 参考：已导入的历史 Excel 周报文件（格式风格 + 历史内容双重参考）
- 支持手动编辑、导出（复制文本 / 下载 Markdown）

### 生活板块

#### 2.4 生活备忘（Memo - life）
- 记录生活相关待办事项
- 功能与工作备忘相同，通过 Section 字段区分

#### 2.5 生活记录（LifeLog）
- 自由格式记录生活点滴（日记/感想/事件）
- 支持**多图附件**（每条记录可上传多张图片，存入独立图片表）
- 心情标签支持**结构化情绪 + 情绪强度评分（1-5）**，便于 Agent 做趋势分析
- 字段：内容、心情标签、情绪强度、记录日期
- 按月份时间轴展示，支持**年度回顾**（AI 汇总全年心情变化与生活亮点）

### 工具

#### 2.6 AI 对话（Chat）
- 基于 DeepSeek API 的对话界面
- 支持多轮对话，保留上下文
- 对话历史持久化保存
- 支持临时聊天模式：上下文仅保留在当前页面，不保存会话或消息，不出现在历史列表中
- 支持从对话生成并下载真实的 PDF、Word（DOCX）、Excel（XLSX）及常用文本文件
- 支持创建、编辑、删除多个对话会话
- 会话类型（SessionType）：`general`（普通对话）/ `report_assistant`（周报撰写助手）

#### 2.7 周报参考文件管理（WeeklyReportReference）
- 上传历史 Excel（.xlsx）周报文件，永久保存
- 系统自动解析 Excel 提取纯文本（使用 ClosedXML）
- 生成周报时自动注入 AI Prompt，作为格式模板和历史参考
- 支持添加备注（如"2025年Q4模板"）、标注对应周次

### Agent 功能

#### 2.8 快速捕获（Quick Capture）
- 提供极简输入框，用户一键记录想法，无需先选择目标模块
- AI 自动分析内容并归类（WorkLog / Memo / LifeLog），提取标题、提醒时间、项目等结构化字段
- 支持 PWA Share Target，可从手机其他 App 直接分享内容到 MiraiNote
- 示例："明天下午3点要跟客户开会" → AI 自动识别为 Memo，设置提醒时间和优先级

#### 2.9 每日简报 Agent（Daily Briefing Agent）
- 每天早上定时运行（时间可配置），也可在 Dashboard 手动触发刷新
- AI 汇总今日及明日到期备忘、未完成高优先级事项、本周 WorkLog 进展
- 生成个性化早安简报，展示在首页 Dashboard 顶部
- 通过 SSE 推送实时生成内容，避免长等待

#### 2.10 周报撰写助手 Agent（Report Writing Agent）
- 在现有"一键生成"基础上，升级为**多轮对话协作**模式
- Agent 主动分析本周 WorkLog，发现记录缺口并提问补充（如"周三没有记录，有需要补充的吗？"）
- 生成草稿后支持自然语言修改指令（如"把第二段改得更简洁" / "强调技术难点"）
- 基于现有 ChatSession 扩展 SessionType = `report_assistant`，将 WorkLog 上下文注入 Prompt

#### 2.11 情绪感知 Agent（Mood Intelligence Agent）
- 定期分析用户最近 30 天的心情标签和情绪强度趋势
- 发现情绪规律（如：连续多天疲惫、工作日与周末情绪差异）
- 在 LifeLog 页面以**非打扰式洞察卡片**展示，提供改善建议
- 依赖足够的生活记录积累（建议 14 天以上才触发分析）

#### 2.12 语义搜索（Semantic Search）
- 对所有 WorkLog / LifeLog 进行向量化，支持语义相似度检索
- 用户可用自然语言提问："我上次解决 XX 问题是怎么做的？" / "去年这个季度在做什么？"
- 初期使用 DeepSeek Embedding API + SQL Server JSON 字段存储向量；后续可迁移至专用向量数据库

---

## 3. 数据模型

### 3.0 公共字段约定（所有表必须包含）

每张表都必须包含以下 6 个公共字段，在 EF Core 中通过 `BaseEntity` 抽象基类统一继承：

```
IsDeleted   BIT           NOT NULL DEFAULT 0   -- 软删除标记，1=已删除
CreatedAt   DATETIME      NOT NULL             -- 创建时间（UTC）
CreatedBy   INT           NOT NULL DEFAULT 1   -- 创建人用户ID（管理员操作默认为1）
UpdatedAt   DATETIME      NOT NULL             -- 最后更新时间（UTC）
UpdatedBy   INT           NOT NULL DEFAULT 1   -- 最后更新人用户ID
```

> **超级管理员约定**：`Id = 1` 的用户为超级管理员。系统初始化数据、管理员后台操作，`CreatedBy` / `UpdatedBy` 均默认填写 `1`。

> **软删除约定**：所有查询默认过滤 `WHERE IsDeleted = 0`，EF Core 通过全局查询过滤器（`HasQueryFilter`）统一处理，业务代码无需手动添加条件。

---

### 3.1 用户（User）
```
Id              INT PK IDENTITY
Username        NVARCHAR(50)   NOT NULL UNIQUE
PasswordHash    NVARCHAR(256)  NOT NULL
Email           NVARCHAR(200)  NOT NULL UNIQUE
IsAdmin         BIT            NOT NULL DEFAULT 0
IsEmailVerified BIT            NOT NULL DEFAULT 0
IsActive        BIT            NOT NULL DEFAULT 1
LastLoginAt     DATETIME       NULL
+ 公共字段
```

### 3.2 邮箱验证Token（EmailVerifyToken）
```
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Token     NVARCHAR(200)  NOT NULL   -- 随机生成的 UUID v4
Type      NVARCHAR(50)   NOT NULL   -- verify_email / reset_password
ExpiresAt DATETIME       NOT NULL   -- 过期时间（UTC）
IsUsed    BIT            NOT NULL DEFAULT 0
+ 公共字段
```

### 3.3 刷新令牌（RefreshToken）
```
Id        INT PK IDENTITY
UserId    INT FK → User.Id
TokenHash NVARCHAR(256)  NOT NULL   -- Token SHA256 哈希
ExpiresAt DATETIME       NOT NULL   -- 过期时间（UTC）
RevokedAt DATETIME       NULL       -- 吊销时间（NULL=未吊销）
+ 公共字段
```

### 3.4 工作记录（WorkLog）
```
Id         INT PK IDENTITY
UserId     INT FK → User.Id
ProjectId  INT FK → Project.Id  NULL   -- 关联项目（替代 Category 字符串，向后兼容）
Title      NVARCHAR(200)  NOT NULL
Purpose    NVARCHAR(500)  NULL       -- 工作目的/目标，供 AI 周报理解价值
Content    NVARCHAR(MAX)             -- Markdown
Tags       NVARCHAR(500)             -- 逗号分隔（旧字段，向后兼容）
Category   NVARCHAR(100)             -- 项目分类（旧字段，向后兼容）
LogDate    DATE           NOT NULL   -- 记录日期（与 CreatedAt 不同）
+ 公共字段
```
> Tags 和 Category 保留用于向后兼容；新数据通过 `Project` 和 `WorkLogTagMap` 关联表管理。

### 3.5 备忘录（Memo）
```
Id                  INT PK IDENTITY
UserId              INT FK → User.Id
Section             NVARCHAR(20)   NOT NULL     -- 'work' | 'life'
Content             NVARCHAR(1000) NOT NULL
RemindAt            DATETIME       NULL         -- 提醒时间（UTC）
RemindMethods       BYTE           NOT NULL DEFAULT 0  -- 位标志：1=弹窗 2=邮件 3=弹窗+邮件
EmailReminderSent   BIT            NOT NULL DEFAULT 0  -- 邮件提醒是否已发送
PopupAcknowledged   BIT            NOT NULL DEFAULT 0  -- 弹窗提醒是否已确认
RemindedAt          DATETIME       NULL         -- 最近一次提醒时间（UTC）
Priority            BYTE           NOT NULL DEFAULT 2  -- 1=低 2=中 3=高
IsPinned            BIT            NOT NULL DEFAULT 0
IsDone              BIT            NOT NULL DEFAULT 0
IsArchived          BIT            NOT NULL DEFAULT 0
+ 公共字段
```

### 3.6 生活记录（LifeLog）
```
Id            INT PK IDENTITY
UserId        INT FK → User.Id
Content       NVARCHAR(MAX)  NOT NULL
Mood          NVARCHAR(50)              -- 心情标签（开心/平静/疲惫等）
MoodIntensity TINYINT        NULL       -- 情绪强度评分（1-5，供 Agent 趋势分析）
ImagePath     NVARCHAR(500)  NULL       -- 主图路径（向后兼容，单图场景）
LogDate       DATE           NOT NULL   -- 记录日期
+ 公共字段
```
> 多图支持通过 `LifeLogImage` 关联表实现（见 3.13）。

### 3.7 周报（WeeklyReport）
```
Id          INT PK IDENTITY
UserId      INT FK → User.Id
WeekStart   DATE          NOT NULL
WeekEnd     DATE          NOT NULL
Content     NVARCHAR(MAX) NOT NULL   -- Markdown 格式
GeneratedAt DATETIME      NOT NULL
IsEdited    BIT           NOT NULL DEFAULT 0
+ 公共字段
```

### 3.8 周报参考文件（WeeklyReportReference）
```
Id          INT PK IDENTITY
UserId      INT FK → User.Id
FileName    NVARCHAR(200)  NOT NULL  -- 原始文件名
FilePath    NVARCHAR(500)  NOT NULL  -- 服务器存储路径
ParsedText  NVARCHAR(MAX)  NOT NULL  -- 解析出的纯文本，供 AI 使用
WeekStart   DATE           NULL      -- 可选：标注对应周次
WeekEnd     DATE           NULL
Remark      NVARCHAR(200)  NULL      -- 备注
+ 公共字段
```

### 3.9 AI 对话会话（ChatSession）
```
Id          INT PK IDENTITY
UserId      INT FK → User.Id
Title       NVARCHAR(200)  NOT NULL   -- 会话标题
SessionType NVARCHAR(30)   NOT NULL DEFAULT 'general'  -- general / report_assistant
+ 公共字段
```

### 3.10 AI 对话消息（ChatMessage）
```
Id        INT PK IDENTITY
SessionId INT FK → ChatSession.Id
Role      NVARCHAR(20)   NOT NULL   -- user / assistant
Content   NVARCHAR(MAX)  NOT NULL   -- 消息内容
+ 公共字段
```

---

### 3.11 项目（Project）
```
Id      INT PK IDENTITY
UserId  INT FK → User.Id
Name    NVARCHAR(100)  NOT NULL   -- 项目名称
Color   NVARCHAR(20)   NULL       -- 前端展示色（如 #4F46E5）
+ 公共字段
```
> 替代 WorkLog.Category 字符串字段，支持项目维度聚合统计与筛选。

### 3.12 工作记录标签（WorkLogTag）
```
Id      INT PK IDENTITY
UserId  INT FK → User.Id
Name    NVARCHAR(50)   NOT NULL   -- 标签名
+ 公共字段
```
> 替代 WorkLog.Tags 逗号字段，维护用户标签库，前端支持自动补全。

**WorkLog ↔ Tag 关联表（无 BaseEntity）：**
```
WorkLogId  INT FK → WorkLog.Id   NOT NULL
TagId      INT FK → WorkLogTag.Id  NOT NULL
PRIMARY KEY (WorkLogId, TagId)
```

### 3.13 生活记录图片（LifeLogImage）
```
Id          INT PK IDENTITY
LifeLogId   INT FK → LifeLog.Id
ImagePath   NVARCHAR(500)  NOT NULL   -- 图片存储路径
SortOrder   INT            NOT NULL DEFAULT 0
+ 公共字段
```
> 支持单条生活记录挂载多张图片。LifeLog.ImagePath 字段保留向后兼容。

### 3.14 Agent 任务记录（AgentTask）
```
Id           INT PK IDENTITY
UserId       INT FK → User.Id
AgentType    NVARCHAR(50)    NOT NULL   -- daily_briefing / report_writing / mood_analysis / quick_capture
Status       NVARCHAR(20)    NOT NULL   -- pending / running / success / failed
InputJson    NVARCHAR(MAX)   NULL       -- 输入参数快照（JSON）
OutputJson   NVARCHAR(MAX)   NULL       -- 输出结果快照（JSON）
StartedAt    DATETIME        NULL
FinishedAt   DATETIME        NULL
ErrorMsg     NVARCHAR(1000)  NULL
+ 公共字段
```
> 记录 Agent 执行历史，便于审计、调试和结果缓存。

### 3.15 向量索引（ContentEmbedding）
```
Id             INT PK IDENTITY
UserId         INT FK → User.Id
SourceType     NVARCHAR(20)    NOT NULL   -- worklog / lifelog / memo
SourceId       INT             NOT NULL   -- 关联记录 ID
ContentChunk   NVARCHAR(MAX)   NOT NULL   -- 被向量化的文本片段
EmbeddingJson  NVARCHAR(MAX)   NOT NULL   -- 向量数组（JSON 存储，初期方案）
+ 公共字段
```
> 使用 DeepSeek Embedding API 生成向量，以 JSON 存储于 SQL Server。后续数据量增大可迁移至专用向量数据库（pgvector / Milvus）。

- 所有接口前缀：`/api/v1/`
- 认证方式：JWT Bearer Token（有效期 7 天）
- 请求/响应格式：JSON
- 统一响应结构：

```json
{
  "success": true,
  "data": {},
  "message": ""
}
```

### 主要接口

#### 认证接口
| 方法 | 路径 | 说明 |
|---|---|---|
| POST | /api/v1/auth/register | 注册账户 |
| POST | /api/v1/auth/login | 登录 |
| POST | /api/v1/auth/verify-email | 邮箱验证 |
| POST | /api/v1/auth/send-verify-email | 发送验证邮件 |
| POST | /api/v1/auth/request-reset-password | 请求密码重置 |
| POST | /api/v1/auth/reset-password | 重置密码 |
| POST | /api/v1/auth/refresh | 刷新 JWT Token |

#### 工作模块接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/worklogs | 获取工作记录列表（分页、筛选、搜索） |
| GET | /api/v1/worklogs/{id} | 获取工作记录详情 |
| POST | /api/v1/worklogs | 创建工作记录 |
| PUT | /api/v1/worklogs/{id} | 更新工作记录 |
| DELETE | /api/v1/worklogs/{id} | 删除工作记录 |

#### 备忘录接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/memos | 获取备忘列表（支持按 section 筛选） |
| POST | /api/v1/memos | 创建备忘 |
| PUT | /api/v1/memos/{id} | 更新备忘 |
| PATCH | /api/v1/memos/{id}/status | 切换完成/置顶/归档状态 |
| DELETE | /api/v1/memos/{id} | 删除备忘 |
| GET | /api/v1/memos/due-popups | 获取待弹窗的备忘 |
| PATCH | /api/v1/memos/{id}/acknowledge-popup | 确认弹窗 |

#### 生活记录接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/lifelogs | 获取生活记录列表（分页、按日期范围） |
| GET | /api/v1/lifelogs/{id} | 获取生活记录详情 |
| POST | /api/v1/lifelogs | 创建生活记录 |
| PUT | /api/v1/lifelogs/{id} | 更新生活记录 |
| DELETE | /api/v1/lifelogs/{id} | 删除生活记录 |

#### 周报接口
| 方法 | 路径 | 说明 |
|---|---|---|
| POST | /api/v1/reports/generate | 生成周报（调用 AI） |
| GET | /api/v1/reports | 获取周报列表 |
| GET | /api/v1/reports/{id} | 获取周报详情 |
| PUT | /api/v1/reports/{id} | 更新（编辑）周报 |

#### 周报参考文件接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/report-references | 获取参考文件列表 |
| POST | /api/v1/report-references | 上传参考文件 |
| DELETE | /api/v1/report-references/{id} | 删除参考文件 |

#### AI 对话接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/chat/sessions | 获取所有对话会话 |
| GET | /api/v1/chat/sessions/{sessionId} | 获取对话会话详情（包含所有消息） |
| POST | /api/v1/chat/sessions | 创建新对话会话 |
| PUT | /api/v1/chat/sessions/{sessionId} | 更新会话标题 |
| DELETE | /api/v1/chat/sessions/{sessionId} | 删除对话会话 |
| POST | /api/v1/chat/sessions/{sessionId}/messages | 发送消息（调用 AI） |
| POST | /api/v1/chat/temporary/messages/stream | 临时聊天流式消息（无状态、不持久化） |
| POST | /api/v1/chat/temporary/{temporaryId}/messages/agent/stream | 临时 Agent 聊天流式消息（无状态、不持久化） |

#### 文件上传接口
| 方法 | 路径 | 说明 |
|---|---|---|
| POST | /api/v1/upload | 上传文件（图片/Excel） |

#### Agent 接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/agent/daily-briefing | 获取今日简报（触发生成或返回当天缓存） |
| POST | /api/v1/agent/quick-capture | 快速捕获，AI 自动分类并创建记录 |
| POST | /api/v1/chat/sessions/{id}/report-mode | 将会话切换为周报撰写助手模式 |
| GET | /api/v1/agent/mood-insights | 获取最近情绪趋势分析洞察 |
| POST | /api/v1/search/semantic | 语义搜索（自然语言提问，返回相关记录） |

#### 项目与标签接口
| 方法 | 路径 | 说明 |
|---|---|---|
| GET | /api/v1/projects | 获取用户项目列表 |
| POST | /api/v1/projects | 创建项目 |
| PUT | /api/v1/projects/{id} | 更新项目 |
| DELETE | /api/v1/projects/{id} | 删除项目 |
| GET | /api/v1/worklogs/tags | 获取用户标签库 |

---

## 5. 前端架构

```
frontend/src/
├── api/                 # 所有后端请求封装（axios）
│   ├── auth.ts
│   ├── workLog.ts
│   ├── memo.ts
│   ├── lifeLog.ts
│   ├── weeklyReport.ts
│   ├── chat.ts
│   ├── agent.ts         # Agent 相关接口
│   └── search.ts        # 语义搜索接口
├── components/          # 可复用组件
├── layouts/             # 布局组件
├── views/               # 页面级组件
│   ├── HomeView.vue
│   ├── DashboardView.vue        # 首屏，展示每日简报 + 快速捕获入口
│   ├── LoginView.vue
│   ├── RegisterView.vue
│   ├── VerifyEmailView.vue
│   ├── ForgotPasswordView.vue
│   ├── ResetPasswordView.vue
│   ├── work/
│   │   ├── WorkLogView.vue      # 工作记录（含 Project / Tag 筛选）
│   │   ├── WorkMemoView.vue     # 工作备忘
│   │   └── WeeklyReportView.vue # 周报生成（含撰写助手 Agent）
│   ├── life/
│   │   ├── LifeMemoView.vue     # 生活备忘
│   │   └── LifeLogView.vue      # 生活记录（含情绪趋势卡片）
│   └── chat/
│       └── ChatView.vue         # AI 对话（支持普通对话 / 周报助手模式）
├── stores/              # Pinia 状态管理
├── router/              # Vue Router
├── types/               # TypeScript 类型定义
├── composables/         # 组合式 API
├── App.vue
└── main.ts
```

**PWA 要求（Phase 5 实现）：**
- `vite-plugin-pwa` 生成 `manifest.json` + Service Worker
- 支持"添加到主屏幕"，接近原生 App 体验
- Service Worker 实现 **Web Push Notification**，解决浏览器关闭后提醒失效问题
- 离线模式：可查看已加载的记录列表（读缓存）

---

## 6. 后端架构

```
backend/
├── MiraiNote.API/                      # ASP.NET Core Web API 项目
│   ├── Controllers/                    # 处理 HTTP 请求
│   │   ├── AuthController.cs
│   │   ├── WorkLogsController.cs
│   │   ├── MemosController.cs
│   │   ├── LifeLogsController.cs
│   │   ├── WeeklyReportsController.cs
│   │   ├── ChatController.cs
│   │   ├── UploadController.cs
│   │   ├── UsersController.cs
│   │   ├── AgentController.cs          # Phase 6：Agent 触发 / 结果查询
│   │   └── SearchController.cs        # Phase 7：语义搜索
│   ├── Infrastructure/                 # 基础设施
│   │   └── UtcDateTimeJsonConverters.cs
│   ├── Middleware/                     # 中间件
│   │   └── GlobalExceptionMiddleware.cs
│   ├── Services/                       # 数据访问辅助
│   │   └── CurrentUserService.cs
│   └── Program.cs
│
├── MiraiNote.Core/                     # 业务逻辑层
│   ├── Services/                       # 业务服务（IService 实现）
│   │   ├── AuthService.cs
│   │   ├── WorkLogService.cs
│   │   ├── MemoService.cs
│   │   ├── LifeLogService.cs
│   │   ├── WeeklyReportService.cs
│   │   ├── ChatService.cs
│   │   ├── JwtTokenService.cs
│   │   ├── DatabaseSeeder.cs
│   │   ├── SmtpEmailService.cs
│   │   ├── MemoReminderBackgroundService.cs
│   │   └── UserAdminService.cs
│   └── Agents/                         # Phase 6：Agent 模块
│       ├── IDailyBriefingAgent.cs
│       ├── DailyBriefingAgent.cs
│       ├── IReportWritingAgent.cs
│       ├── ReportWritingAgent.cs
│       ├── IMoodIntelligenceAgent.cs
│       ├── MoodIntelligenceAgent.cs
│       └── ISemanticSearchService.cs   # Phase 7：语义搜索
│
├── MiraiNote.Data/                     # 数据访问层
│   ├── Context/
│   │   ├── MiraiNoteDbContext.cs       # EF Core DbContext
│   │   └── MiraiNoteMigrationDbContext.cs
│   ├── Entities/                       # 数据模型
│   │   ├── BaseEntity.cs
│   │   ├── User.cs
│   │   ├── EmailVerifyToken.cs
│   │   ├── RefreshToken.cs
│   │   ├── WorkLog.cs
│   │   ├── Memo.cs
│   │   ├── LifeLog.cs
│   │   ├── WeeklyReport.cs
│   │   ├── WeeklyReportReference.cs
│   │   ├── ChatSession.cs
│   │   ├── ChatMessage.cs
│   │   ├── Project.cs                  # Phase 5
│   │   ├── WorkLogTag.cs               # Phase 5
│   │   ├── WorkLogTagMap.cs            # Phase 5
│   │   ├── LifeLogImage.cs             # Phase 5
│   │   ├── AgentTask.cs                # Phase 6
│   │   └── ContentEmbedding.cs        # Phase 7
│   └── Migrations/                     # EF Core 迁移文件
│
└── MiraiNote.Shared/                   # 共享库
    ├── Common/                         # 通用类/接口
    │   ├── ApiResponse.cs
    │   ├── ICurrentUserService.cs
    │   ├── BusinessException.cs
    │   ├── JwtOptions.cs
    │   ├── EmailOptions.cs
    │   ├── CorsOptions.cs
    │   └── ...
    └── Dtos/                           # 数据传输对象
        ├── Auth/
        ├── WorkLogs/
        ├── Memos/
        ├── LifeLogs/
        ├── WeeklyReports/
        └── Chat/
```

---

## 7. 非功能需求

- **响应式设计**：支持 375px（手机）～ 1440px（桌面）
- **性能**：
  - 页面首屏加载 < 2 秒
  - API 响应时间 < 500ms（AI 接口除外）
- **安全性**：
  - 密码使用 BCrypt 哈希存储
  - JWT Token 有效期：7 天，支持刷新
  - 使用 HTTPS（生产环境）
- **文件限制**：
  - 图片上传大小：≤ 5MB
  - Excel 参考文件大小：≤ 10MB
- **数据库**：
  - 使用 SQL Server，支持软删除
  - 所有时间字段使用 UTC 时区

---

## 8. 开发阶段规划

| 阶段 | 内容 | 目标 | 状态 |
|---|---|---|---|
| Phase 1 | 用户认证（注册/登录/邮箱验证/忘记密码） | 基础可用 | ✅ 完成 |
| Phase 2 | 工作记录 + 工作备忘 CRUD + 项目 / 标签结构化 | 核心工作功能 | 🔄 进行中 |
| Phase 3 | 生活备忘 + 生活记录 CRUD（多图 + 情绪强度） | 核心生活功能 | 🔄 进行中 |
| Phase 4 | AI 周报生成 + 参考文件上传 + AI 对话 | 核心亮点功能 | 🔄 进行中 |
| Phase 5 | PWA + Web Push 提醒 + 快速捕获 + 多图上传 | 体验升级 | 📋 规划中 |
| Phase 6 | Agent 模块（每日简报 / 周报助手 / 情绪感知） | AI 主动化 | 📋 规划中 |
| Phase 7 | 语义搜索（Embedding 向量检索） | 智能检索 | 📋 规划中 |
| Phase 8 | 性能调优 + 安全加强 + 上线准备 | 生产就绪 | 📋 规划中 |

---

## 9. 已实现功能清单

### 后端
- ✅ 用户认证（注册、登录、邮箱验证、密码重置）
- ✅ JWT Token 管理（生成、刷新、验证）
- ✅ 工作记录 CRUD
- ✅ 备忘录 CRUD（含提醒机制、弹窗管理）
- ✅ 生活记录 CRUD
- ✅ 周报生成（AI 集成）
- ✅ 周报参考文件管理
- ✅ AI 对话会话管理
- ✅ 文件上传处理
- ✅ 全局异常处理中间件
- ✅ 软删除 + 审计字段自动填充

### 前端
- ✅ 登录/注册/邮箱验证/密码重置页面
- ✅ 周报生成视图
- ✅ 生活记录视图
- ✅ AI 对话视图
- 🔄 工作记录视图（开发中）
- 🔄 备忘录视图（开发中）

---

## 10. 技术选型与依赖

### 后端
- **框架**：ASP.NET Core 8 / 9
- **ORM**：Entity Framework Core 8/9
- **数据库**：SQL Server
- **认证**：JWT Bearer Token
- **API 调用**：HttpClient (DeepSeek Chat API + Embedding API)
- **密码加密**：BCrypt.Net-Next
- **后台任务**：Hosted Services
- **Excel 解析**：ClosedXML
- **邮件**：SMTP

### 前端
- **框架**：Vue 3 (Composition API)
- **UI 框架**：Tailwind CSS
- **状态管理**：Pinia
- **路由**：Vue Router 4
- **HTTP 客户端**：Axios
- **TypeScript**：最新版本
- **打包工具**：Vite
- **PWA**：vite-plugin-pwa + Service Worker（Phase 5 引入）

---

## 11. 环境配置

### 后端配置文件（appsettings.json）
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.;Database=MiraiNote;User Id=mirai_user;Password=...;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Secret": "your-secret-key-min-32-chars",
    "ExpirationMinutes": 10080,
    "RefreshExpirationDays": 30
  },
  "DeepSeek": {
    "ApiKey": "your-deepseek-api-key",
    "BaseUrl": "https://api.deepseek.com"
  },
  "EmailOptions": {
    "SmtpServer": "smtp.gmail.com",
    "SmtpPort": 587,
    "FromEmail": "noreply@mirainote.com",
    "FromName": "MiraiNote",
    "Username": "...",
    "Password": "..."
  },
  "CORS": {
    "AllowedOrigins": ["http://localhost:5173", "https://mirainote.com"]
  }
}
```

### 前端配置（.env 文件）
```
VITE_API_BASE_URL=http://localhost:5000
VITE_APP_NAME=MiraiNote
```

---

## 12. 部署与运维

- **前端**：Vite 生成静态资源，部署到 CDN / Web 服务器
- **后端**：Docker 容器化，部署到云平台或自托管服务器
- **数据库**：SQL Server 集群部署（生产环境）
- **监控**：错误日志、性能指标收集与分析

