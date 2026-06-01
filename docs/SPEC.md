# MiraiNote — 项目规格文档 (SPEC.md)

> 版本：v1.2  
> 最后更新：2026-05  
> 技术栈：Vue 3 + Tailwind CSS + C# ASP.NET Core + SQL Server + DeepSeek API

---

## 1. 项目概述

**MiraiNote**（未来ノート）是一款面向个人用户的 Web 助理应用，支持电脑和手机浏览器访问。  
核心定位：**工作记录 + 生活备忘 + AI 周报生成 + 智能对话**，帮助用户沉淀每日信息、减少遗忘、提升效率。

应用分为两大板块，视觉风格明显区分：
- **工作板块**：聚焦职场事务，偏专业简洁风
- **生活板块**：聚焦个人生活，偏轻松温暖风

---

## 2. 核心功能模块

### 工作板块

#### 2.1 工作记录（WorkLog）
- 创建、编辑、删除工作记录条目
- 字段：标题、目的、内容（Markdown）、标签、项目分类、记录日期
- 支持按日期、标签、项目筛选
- 支持关键词全文搜索

#### 2.2 工作备忘（Memo - work）
- 记录工作相关待办事项，支持设置提醒时间
- 提醒方式：浏览器通知（Web Push）
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
- 功能与工作备忘相同，通过 Category 字段区分

#### 2.5 生活记录（LifeLog）
- 自由格式记录生活点滴（日记/感想/事件）
- 支持图片附件（上传到服务器）
- 字段：内容、心情标签、记录日期
- 按月份时间轴展示

### 工具

#### 2.6 AI 对话（Chat）
- 基于 DeepSeek API 的对话界面
- 支持多轮对话，保留上下文
- 可将工作记录/备忘内容注入为对话上下文（@引用）
- 对话历史持久化保存

#### 2.7 周报参考文件管理（WeeklyReportReference）
- 上传历史 Excel（.xlsx）周报文件，永久保存
- 系统自动解析 Excel 提取纯文本（使用 ClosedXML）
- 生成周报时自动注入 AI Prompt，作为格式模板和历史参考
- 支持添加备注（如"2025年Q4模板"）、标注对应周次

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

### 3.2 工作记录（WorkLog）
```
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Title     NVARCHAR(200)  NOT NULL
Purpose   NVARCHAR(500)  NULL       -- 工作目的/目标，供 AI 周报理解价值
Content   NVARCHAR(MAX)             -- Markdown
Tags      NVARCHAR(500)             -- 逗号分隔
Category  NVARCHAR(100)             -- 项目分类
LogDate   DATE           NOT NULL
+ 公共字段
```

### 3.3 备忘录（Memo）
```
Id         INT PK IDENTITY
UserId     INT FK → User.Id
Section    NVARCHAR(20)   NOT NULL   -- 'work' | 'life'
Content    NVARCHAR(1000) NOT NULL
RemindAt   DATETIME       NULL
Priority   TINYINT        NOT NULL DEFAULT 2   -- 1=低 2=中 3=高
IsPinned   BIT            NOT NULL DEFAULT 0
IsDone     BIT            NOT NULL DEFAULT 0
IsArchived BIT            NOT NULL DEFAULT 0
+ 公共字段
```

### 3.4 周报（WeeklyReport）
```
Id          INT PK IDENTITY
UserId      INT FK → User.Id
WeekStart   DATE          NOT NULL
WeekEnd     DATE          NOT NULL
Content     NVARCHAR(MAX)            -- 最终周报内容（Markdown）
GeneratedAt DATETIME      NOT NULL
IsEdited    BIT           NOT NULL DEFAULT 0
+ 公共字段
```

### 3.5 周报参考文件（WeeklyReportReference）
```
Id          INT PK IDENTITY
UserId      INT FK → User.Id
FileName    NVARCHAR(200)  NOT NULL  -- 原始文件名
FilePath    NVARCHAR(500)  NOT NULL  -- 服务器存储路径
ParsedText  NVARCHAR(MAX)  NOT NULL  -- 解析出的纯文本，供 AI 使用
WeekStart   DATE           NULL      -- 可选：标注对应周次
WeekEnd     DATE           NULL
Remark      NVARCHAR(200)  NULL      -- 备注，如"2025年Q4模板"
+ 公共字段
```

### 3.6 生活记录（LifeLog）
```
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Content   NVARCHAR(MAX)  NOT NULL
Mood      NVARCHAR(50)              -- 开心/平静/疲惫/etc
ImagePath NVARCHAR(500)  NULL
LogDate   DATE           NOT NULL
+ 公共字段
```

### 3.7 AI 对话（ChatSession / ChatMessage）
```
-- ChatSession
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Title     NVARCHAR(200)  NOT NULL
+ 公共字段

-- ChatMessage
Id        INT PK IDENTITY
SessionId INT FK → ChatSession.Id
Role      NVARCHAR(20)   NOT NULL   -- user / assistant
Content   NVARCHAR(MAX)  NOT NULL
+ 公共字段
```

---

## 4. API 设计规范

- 所有接口前缀：`/api/v1/`
- 认证方式：JWT Bearer Token
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

| 模块 | 方法 | 路径 |
|---|---|---|
| 认证 | POST | /api/v1/auth/register |
| 认证 | POST | /api/v1/auth/login |
| 工作记录 | GET/POST | /api/v1/worklogs |
| 工作记录 | GET/PUT/DELETE | /api/v1/worklogs/{id} |
| 备忘录 | GET/POST | /api/v1/memos |
| 备忘录 | GET/PUT/DELETE | /api/v1/memos/{id} |
| 周报 | POST | /api/v1/reports/generate |
| 周报 | GET | /api/v1/reports |
| 周报参考文件 | GET/POST | /api/v1/report-references |
| 周报参考文件 | DELETE | /api/v1/report-references/{id} |
| 生活记录 | GET/POST | /api/v1/lifelogs |
| 生活记录 | GET/PUT/DELETE | /api/v1/lifelogs/{id} |
| AI对话 | GET/POST | /api/v1/chat/sessions |
| AI对话 | POST | /api/v1/chat/sessions/{id}/messages |
| 文件上传 | POST | /api/v1/upload |

---

## 5. 前端架构

```
frontend/src/
├── api/              # 所有后端请求封装（axios）
├── components/       # 可复用组件
├── views/
│   ├── work/
│   │   ├── WorkLogView.vue
│   │   ├── WorkMemoView.vue
│   │   └── WeeklyReportView.vue
│   ├── life/
│   │   ├── LifeMemoView.vue
│   │   └── LifeLogView.vue
│   └── chat/
│       └── ChatView.vue
├── stores/           # Pinia 状态管理
├── router/
├── types/
└── assets/
```

---

## 6. 后端架构

```
backend/
├── MiraiNote.API/
│   ├── Controllers/
│   ├── Middleware/
│   └── Program.cs
├── MiraiNote.Core/
│   └── Services/
├── MiraiNote.Data/
│   ├── Entities/
│   ├── Migrations/
│   └── Context/
└── MiraiNote.Shared/
    ├── Common/
    └── Dtos/
```

---

## 7. 非功能需求

- 响应式设计：支持 375px（手机）～ 1440px（桌面）
- 页面首屏加载 < 2秒
- API 响应时间 < 500ms（AI 接口除外）
- 密码使用 BCrypt 哈希存储
- 图片上传大小限制：5MB
- Excel 参考文件大小限制：10MB

---

## 8. 开发阶段规划

| 阶段 | 内容 | 目标 |
|---|---|---|
| Phase 1 ✅ | 用户认证（注册/登录/邮箱验证/忘记密码） | 基础可用 |
| Phase 2 | 工作记录 + 工作备忘 CRUD | 核心工作功能 |
| Phase 3 | 生活备忘 + 生活记录 CRUD | 核心生活功能 |
| Phase 4 | AI 周报生成 + 参考文件上传 | 核心亮点 |
| Phase 5 | AI 对话 | 智能助手 |
| Phase 6 | 响应式优化 + 性能调优 | 上线就绪 |


---

## 1. 项目概述

**MiraiNote**（未来ノート）是一款面向个人用户的 Web 助理应用，支持电脑和手机浏览器访问。  
核心定位：**工作记录 + 生活备忘 + AI 周报生成 + 智能对话**，帮助用户沉淀每日信息、减少遗忘、提升效率。

---

## 2. 核心功能模块

### 2.1 工作记录（WorkLog）
- 创建、编辑、删除工作记录条目
- 字段：标题、内容（富文本/Markdown）、标签、项目分类、记录时间
- 支持按日期、标签、项目筛选
- 支持关键词全文搜索

### 2.2 周报生成（WeeklyReport）
- 选择时间范围（默认本周）
- 一键调用 AI（DeepSeek）汇总本周工作记录，生成结构化周报
- 周报格式可自定义（模板）
- 支持手动编辑、导出（复制文本 / 下载 Markdown）

### 2.3 备忘录（Memo）
- 创建快速备忘，支持设置提醒时间
- 提醒方式：浏览器通知（Web Push）
- 支持置顶、完成标记、归档
- 字段：内容、提醒时间、优先级（高/中/低）、完成状态

### 2.4 生活记录（LifeLog）
- 自由格式记录生活点滴（日记/感想/事件）
- 支持图片附件（上传到服务器）
- 字段：内容、心情标签、记录时间
- 按月份时间轴展示

### 2.5 AI 对话（Chat）
- 基于 DeepSeek API 的对话界面
- 支持多轮对话，保留上下文
- 可将工作记录/备忘内容注入为对话上下文（@引用）
- 对话历史持久化保存

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
Id           INT PK IDENTITY
Username     NVARCHAR(50)   NOT NULL UNIQUE
PasswordHash NVARCHAR(256)  NOT NULL
IsAdmin      BIT            NOT NULL DEFAULT 0   -- 是否管理员
-- 公共字段 --
IsDeleted    BIT            NOT NULL DEFAULT 0
CreatedAt    DATETIME       NOT NULL
CreatedBy    INT            NOT NULL DEFAULT 1
UpdatedAt    DATETIME       NOT NULL
UpdatedBy    INT            NOT NULL DEFAULT 1
```

### 3.2 工作记录（WorkLog）
```
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Title     NVARCHAR(200)  NOT NULL
Purpose   NVARCHAR(500)  NULL                    -- 工作目的/目标，供 AI 周报理解价值
Content   NVARCHAR(MAX)                          -- Markdown
Tags      NVARCHAR(500)                          -- 逗号分隔
Category  NVARCHAR(100)
LogDate   DATE           NOT NULL
-- 公共字段 --
IsDeleted BIT            NOT NULL DEFAULT 0
CreatedAt DATETIME       NOT NULL
CreatedBy INT            NOT NULL DEFAULT 1
UpdatedAt DATETIME       NOT NULL
UpdatedBy INT            NOT NULL DEFAULT 1
```

### 3.3 周报（WeeklyReport）
```
Id          INT PK IDENTITY
UserId      INT FK → User.Id
WeekStart   DATE          NOT NULL
WeekEnd     DATE          NOT NULL
Content     NVARCHAR(MAX)
GeneratedAt DATETIME      NOT NULL
IsEdited    BIT           NOT NULL DEFAULT 0
-- 公共字段 --
IsDeleted   BIT           NOT NULL DEFAULT 0
CreatedAt   DATETIME      NOT NULL
CreatedBy   INT           NOT NULL DEFAULT 1
UpdatedAt   DATETIME      NOT NULL
UpdatedBy   INT           NOT NULL DEFAULT 1
```

### 3.4 备忘录（Memo）
```
Id         INT PK IDENTITY
UserId     INT FK → User.Id
Content    NVARCHAR(1000) NOT NULL
RemindAt   DATETIME       NULL
Priority   TINYINT        NOT NULL DEFAULT 2     -- 1=低 2=中 3=高
IsPinned   BIT            NOT NULL DEFAULT 0
IsDone     BIT            NOT NULL DEFAULT 0
IsArchived BIT            NOT NULL DEFAULT 0
-- 公共字段 --
IsDeleted  BIT            NOT NULL DEFAULT 0
CreatedAt  DATETIME       NOT NULL
CreatedBy  INT            NOT NULL DEFAULT 1
UpdatedAt  DATETIME       NOT NULL
UpdatedBy  INT            NOT NULL DEFAULT 1
```

### 3.5 生活记录（LifeLog）
```
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Content   NVARCHAR(MAX)  NOT NULL
Mood      NVARCHAR(50)                           -- 开心/平静/疲惫/etc
ImagePath NVARCHAR(500)  NULL
LogDate   DATE           NOT NULL
-- 公共字段 --
IsDeleted BIT            NOT NULL DEFAULT 0
CreatedAt DATETIME       NOT NULL
CreatedBy INT            NOT NULL DEFAULT 1
UpdatedAt DATETIME       NOT NULL
UpdatedBy INT            NOT NULL DEFAULT 1
```

### 3.6 AI 对话（ChatSession / ChatMessage）
```
-- ChatSession
Id        INT PK IDENTITY
UserId    INT FK → User.Id
Title     NVARCHAR(200)  NOT NULL
-- 公共字段 --
IsDeleted BIT            NOT NULL DEFAULT 0
CreatedAt DATETIME       NOT NULL
CreatedBy INT            NOT NULL DEFAULT 1
UpdatedAt DATETIME       NOT NULL
UpdatedBy INT            NOT NULL DEFAULT 1

-- ChatMessage
Id        INT PK IDENTITY
SessionId INT FK → ChatSession.Id
Role      NVARCHAR(20)   NOT NULL               -- user / assistant
Content   NVARCHAR(MAX)  NOT NULL
-- 公共字段 --
IsDeleted BIT            NOT NULL DEFAULT 0
CreatedAt DATETIME       NOT NULL
CreatedBy INT            NOT NULL DEFAULT 1
UpdatedAt DATETIME       NOT NULL
UpdatedBy INT            NOT NULL DEFAULT 1
```

---

## 4. API 设计规范

- 所有接口前缀：`/api/v1/`
- 认证方式：JWT Bearer Token
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

| 模块 | 方法 | 路径 |
|---|---|---|
| 认证 | POST | /api/v1/auth/login |
| 认证 | POST | /api/v1/auth/register |
| 工作记录 | GET/POST | /api/v1/worklogs |
| 工作记录 | GET/PUT/DELETE | /api/v1/worklogs/{id} |
| 周报 | POST | /api/v1/reports/generate |
| 周报 | GET | /api/v1/reports |
| 备忘录 | GET/POST | /api/v1/memos |
| 备忘录 | PUT/DELETE | /api/v1/memos/{id} |
| 生活记录 | GET/POST | /api/v1/lifelogs |
| 生活记录 | PUT/DELETE | /api/v1/lifelogs/{id} |
| AI对话 | GET/POST | /api/v1/chat/sessions |
| AI对话 | POST | /api/v1/chat/sessions/{id}/messages |
| 文件上传 | POST | /api/v1/upload |

---

## 5. 前端架构

```
frontend/
├── src/
│   ├── api/              # 所有后端请求封装（axios）
│   ├── components/       # 可复用组件
│   ├── views/            # 页面级组件
│   │   ├── WorkLog/
│   │   ├── Report/
│   │   ├── Memo/
│   │   ├── LifeLog/
│   │   └── Chat/
│   ├── stores/           # Pinia 状态管理
│   ├── router/           # Vue Router
│   ├── utils/            # 工具函数
│   └── assets/
├── public/
└── index.html
```

---

## 6. 后端架构

```
backend/
├── MiraiNote.API/          # ASP.NET Core Web API 项目
│   ├── Controllers/
│   ├── Middleware/
│   └── Program.cs
├── MiraiNote.Core/         # 业务逻辑层
│   ├── Services/
│   ├── Interfaces/
│   └── Models/
├── MiraiNote.Data/         # 数据访问层
│   ├── Repositories/
│   ├── Entities/
│   └── MiraiNoteDbContext.cs
└── MiraiNote.Shared/       # 共享 DTO / 常量
```

---

## 7. 非功能需求

- 响应式设计：支持 375px（手机）～ 1440px（桌面）
- 页面首屏加载 < 2秒
- API 响应时间 < 500ms（AI接口除外）
- JWT Token 有效期：7天，支持刷新
- 密码使用 BCrypt 哈希存储
- 图片上传大小限制：5MB

---

## 8. 开发阶段规划

| 阶段 | 内容 | 目标 |
|---|---|---|
| Phase 1 | 用户认证 + 工作记录 CRUD | 基础可用 |
| Phase 2 | 备忘录 + 提醒 + 生活记录 | 功能完整 |
| Phase 3 | AI 周报生成 + AI 对话 | 核心亮点 |
| Phase 4 | 响应式优化 + 性能调优 | 上线就绪 |
