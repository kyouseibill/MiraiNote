# MiraiNote — GitHub Copilot 指令文件

> 本文件定义 MiraiNote 项目的编码规范与 AI 协作约定。
> Copilot 在生成代码时必须严格遵守以下所有规则。

---

## 项目概述

MiraiNote 是一个个人助理 Web 应用。
- 前端：Vue 3 + Tailwind CSS + Pinia + Vue Router
- 后端：ASP.NET Core Web API（C#）
- 数据库：SQL Server（使用 Entity Framework Core）
- AI：DeepSeek API（OpenAI 兼容格式）

---

## 前端规范（Vue 3）

### 基本语法
- 所有组件使用 `<script setup>` 语法（Composition API）
- 不使用 Options API
- 不使用 `this`

### 组件规范
- 组件文件名使用 PascalCase，如 `WorkLogEditor.vue`
- Props 必须定义类型，使用 `defineProps<{...}>()`
- Emits 必须声明，使用 `defineEmits<{...}>()`
- 每个组件职责单一，超过 200 行考虑拆分

### 样式规范
- 样式只使用 Tailwind CSS 工具类
- 不写自定义 CSS，除非 Tailwind 无法实现
- 响应式断点优先使用：`sm:` `md:` `lg:`
- 深色模式预留 `dark:` 前缀

### 状态管理
- 全局状态使用 Pinia Store
- Store 文件放在 `src/stores/` 目录
- 每个功能模块一个 Store（如 `useWorkLogStore`）
- 不在组件内直接操作 API，通过 Store action 调用

### API 调用
- 所有接口请求封装在 `src/api/` 目录
- 使用 axios，统一配置 baseURL 和 JWT 拦截器
- 每个模块一个文件（如 `src/api/worklog.ts`）
- 函数命名：`getWorklogs()` `createWorklog()` `updateWorklog()` `deleteWorklog()`

### 错误处理
- 所有 API 调用必须有 try/catch
- 错误统一通过 toast 通知组件展示
- 不使用 console.log 调试，使用注释标记待处理逻辑

### TypeScript
- 所有文件使用 TypeScript（`.ts` `.vue`）
- 为所有数据模型定义 Interface，放在 `src/types/` 目录
- 不使用 `any` 类型，除非有充分注释说明

---

## 后端规范（C# ASP.NET Core）

### 项目结构
- 严格遵守三层架构：API → Core（Service） → Data（Repository）
- Controller 只负责接收请求、调用 Service、返回响应
- 业务逻辑全部在 Service 层
- 数据库操作全部在 Repository 层

### 命名规范
- 类名：PascalCase（`WorkLogService`）
- 方法名：PascalCase（`GetByUserId`）
- 变量名：camelCase（`workLogList`）
- 接口名：`I` 前缀（`IWorkLogService`）
- 异步方法：`Async` 后缀（`GetByUserIdAsync`）

### API 设计
- 所有接口返回统一响应格式：

```csharp
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string Message { get; set; } = "";
}
```

- 使用 HTTP 状态码：200 成功 / 400 参数错误 / 401 未认证 / 404 不存在 / 500 服务器错误
- 路由前缀：`[Route("api/v1/[controller]")]`

### 认证
- 使用 JWT Bearer Token
- 从 Token 中获取 UserId，不从请求参数获取
- 所有需要认证的接口加 `[Authorize]` 特性
- 实现 `ICurrentUserService` 接口，供 DbContext 获取当前登录用户 ID

### 数据库
- 使用 Entity Framework Core
- 不写原生 SQL，除非性能有特殊需求
- 所有查询使用异步方法（`ToListAsync` `FirstOrDefaultAsync`）
- 软删除：不物理删除记录，使用 `IsDeleted` 字段标记，EF Core 全局查询过滤器自动处理

#### BaseEntity 抽象基类
所有实体类必须继承 `BaseEntity`，不得重复定义公共字段：

```csharp
public abstract class BaseEntity
{
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public int CreatedBy { get; set; } = 1;
    public DateTime UpdatedAt { get; set; }
    public int UpdatedBy { get; set; } = 1;
}
```

#### 全局查询过滤器
在 `MiraiNoteDbContext.OnModelCreating` 中为所有继承 `BaseEntity` 的实体统一注册软删除过滤器：

```csharp
// 自动为所有实体添加 IsDeleted = false 过滤
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
{
    if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
    {
        modelBuilder.Entity(entityType.ClrType)
            .HasQueryFilter(e => !EF.Property<bool>(e, "IsDeleted"));
    }
}
```

#### 自动填充审计字段
重写 `MiraiNoteDbContext.SaveChangesAsync`，自动设置审计字段，业务代码无需手动赋值：

```csharp
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var now = DateTime.UtcNow;
    var userId = _currentUserService.UserId; // 从 ICurrentUserService 获取当前登录用户ID

    foreach (var entry in ChangeTracker.Entries<BaseEntity>())
    {
        if (entry.State == EntityState.Added)
        {
            entry.Entity.CreatedAt = now;
            entry.Entity.CreatedBy = userId > 0 ? userId : 1; // 未登录时默认超级管理员
            entry.Entity.UpdatedAt = now;
            entry.Entity.UpdatedBy = userId > 0 ? userId : 1;
        }
        else if (entry.State == EntityState.Modified)
        {
            entry.Entity.UpdatedAt = now;
            entry.Entity.UpdatedBy = userId > 0 ? userId : 1;
        }
    }
    return await base.SaveChangesAsync(ct);
}
```

#### 数据库迁移与初始化（Migration）
Copilot 负责生成并维护以下内容，无需人工手动操作数据库：

1. **EF Core Migration 文件**：每次数据模型变更后，生成对应 Migration
   ```
   dotnet ef migrations add <MigrationName> --project MiraiNote.Data --startup-project MiraiNote.API
   dotnet ef database update --project MiraiNote.Data --startup-project MiraiNote.API
   ```

2. **数据库连接账户**：使用独立的数据库账户（非 sa），配置在 `appsettings.json`：
   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "Server=.;Database=MiraiNote;User Id=mirai_user;Password=YOUR_PASSWORD;TrustServerCertificate=True;"
     }
   }
   ```
   首次部署时，Copilot 生成以下 SQL 用于创建数据库账户（由开发者以 sa 身份执行一次）：
   ```sql
   -- 创建数据库
   CREATE DATABASE MiraiNote;
   GO

   -- 创建登录账户
   CREATE LOGIN mirai_user WITH PASSWORD = 'YOUR_STRONG_PASSWORD';
   GO

   -- 切换到目标数据库，创建用户并授权
   USE MiraiNote;
   GO
   CREATE USER mirai_user FOR LOGIN mirai_user;
   ALTER ROLE db_owner ADD MEMBER mirai_user;
   GO
   ```
   > 密码由开发者自行设置，不得提交到代码库。使用 `appsettings.Development.json` 存储本地密码（已加入 .gitignore）。

3. **数据初始化 Seeder**：在 `Program.cs` 启动时自动执行，幂等设计（可重复执行不报错）：
   - 超级管理员账户：`Id = 1`，`Username = admin`，密码从配置读取
   - 仅在 `User` 表为空时执行初始化

```csharp
// Program.cs 启动时调用
public static async Task SeedAsync(MiraiNoteDbContext db, IConfiguration config)
{
    if (!await db.Users.AnyAsync())
    {
        var adminPassword = config["Seed:AdminPassword"] ?? throw new Exception("未配置初始管理员密码");
        db.Users.Add(new User
        {
            // Id 由 IDENTITY 自动生成，第一条记录即为 1
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
            IsAdmin = true,
            CreatedBy = 1,
            UpdatedBy = 1,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
```

### DeepSeek API 调用
- DeepSeek 使用 OpenAI 兼容格式
- BaseUrl：`https://api.deepseek.com`
- 封装在独立 Service：`IDeepSeekService`
- 支持流式响应（Streaming）
- API Key 从配置文件读取，不硬编码

### 错误处理
- 使用全局异常中间件统一捕获
- 不在 Controller 层写 try/catch
- 记录日志使用 `ILogger<T>`

---

## 数据库规范（SQL Server）

- 表名：PascalCase 单数（`WorkLog` `Memo`）
- 主键：`Id`（INT IDENTITY，自增）
- 外键：`{表名}Id`（如 `UserId`）
- 时间字段统一使用 UTC 时间（`DateTime.UtcNow`）
- 字符串字段使用 `NVARCHAR`（支持中文）
- 所有表必须包含公共字段：`IsDeleted` `CreatedAt` `CreatedBy` `UpdatedAt` `UpdatedBy`
- `Id = 1` 的 User 为超级管理员，系统初始化数据 `CreatedBy` / `UpdatedBy` 默认为 `1`

---

## 通用约定

- 注释语言：中文
- 提交信息格式：`feat: 添加工作记录列表` / `fix: 修复周报生成空值问题`
- 每个功能分支开发，命名：`feature/worklog-crud`
- 不提交敏感信息（API Key、连接字符串、密码）到代码库
- 配置信息放在 `appsettings.json`，本地覆盖用 `appsettings.Development.json`（加入 .gitignore）
- 敏感配置项示例（仅存于本地）：
  ```json
  {
    "ConnectionStrings": { "DefaultConnection": "..." },
    "Jwt": { "Secret": "..." },
    "DeepSeek": { "ApiKey": "..." },
    "Seed": { "AdminPassword": "..." }
  }
  ```

---

## Copilot 协作原则

1. **严格按 SPEC.md 实现**，不自行增减功能
2. **生成完整可运行代码**，不留 TODO 占位符
3. **遵守命名规范**，不使用缩写（除通用缩写如 `id` `dto`）
4. **每次只实现一个明确的功能点**，保持变更最小化
5. **生成代码后说明关键设计决策**，便于 Review
6. **数据库变更必须同步生成 Migration**，不允许直接修改数据库
7. **审计字段（CreatedAt/By、UpdatedAt/By）由 DbContext 自动填充**，业务代码不得手动赋值
