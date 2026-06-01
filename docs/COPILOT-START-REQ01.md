# Copilot 启动指令 — REQ-01 用户认证

> 将本文件内容粘贴到 Copilot Chat，作为开发启动指令。

---

## 你的任务

请根据以下两份文档，完整实现 MiraiNote 项目的用户认证模块（REQ-01）：

- 编码规范与架构约定：`.github/copilot-instructions.md`
- 功能需求：`docs/requirements/REQ-01-auth.md`

---

## 项目技术栈

- 前端：Vue 3 + TypeScript + Tailwind CSS + Pinia + Vue Router
- 后端：ASP.NET Core Web API（C#）
- 数据库：SQL Server（Entity Framework Core，Code First）
- AI：DeepSeek API（暂不涉及本模块）

---

## 数据库账户说明

本项目使用**两个独立数据库账户**，职责严格分离：

### ① 迁移账户（仅用于 EF Core Migration）
- **用途**：建表、改表结构、执行 `dotnet ef database update`
- **账户**：`Bill.Gong`
- **权限**：`db_owner`
- **密码**：见本地 `appsettings.Development.json` 的 `MigrationConnection`
- **重要**：此连接字符串**只在 Migration 时使用**，不得出现在程序运行时代码中

### ② 应用账户（程序运行时使用）
- **用途**：程序正常运行时的所有数据读写操作
- **账户**：`MiraiNote`
- **权限**：仅 `db_datareader` + `db_datawriter`（无建表权限）
- **密码**：见本地 `appsettings.Development.json` 的 `DefaultConnection`
- **重要**：这是 `MiraiNoteDbContext` 实际使用的连接字符串

### 首次部署时需执行的 SQL（由开发者以 Bill.Gong 账户执行一次）
```sql
-- 若数据库不存在则创建
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'MiraiNote')
BEGIN
    CREATE DATABASE MiraiNote;
END
GO

USE MiraiNote;
GO

-- 创建应用账户登录名（若不存在）
IF NOT EXISTS (SELECT name FROM sys.server_principals WHERE name = 'MiraiNote')
BEGIN
    CREATE LOGIN MiraiNote WITH PASSWORD = 'm^n#i|z!N_o@te';
END
GO

-- 创建数据库用户并授权（仅读写，无 DDL 权限）
IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = 'MiraiNote')
BEGIN
    CREATE USER MiraiNote FOR LOGIN MiraiNote;
    ALTER ROLE db_datareader ADD MEMBER MiraiNote;
    ALTER ROLE db_datawriter ADD MEMBER MiraiNote;
END
GO
```

---

## 请按以下顺序实现

### Step 1 — 后端项目结构初始化
1. 创建解决方案与四个项目：
   - `MiraiNote.API`（ASP.NET Core Web API）
   - `MiraiNote.Core`（Class Library，业务逻辑）
   - `MiraiNote.Data`（Class Library，数据访问）
   - `MiraiNote.Shared`（Class Library，DTO/常量）
2. 配置项目间引用关系：API → Core → Data → Shared
3. 在 `MiraiNote.Data` 安装 EF Core + SQL Server 包
4. 创建 `BaseEntity` 抽象基类（见 copilot-instructions.md）
5. 创建 `MiraiNoteDbContext`，使用 `DefaultConnection`（应用账户）
6. 创建 `MiraiNoteMigrationDbContext`，继承自 `MiraiNoteDbContext`，
   使用 `MigrationConnection`（迁移账户），仅供 EF Core CLI 使用：

```csharp
// 仅供 EF Core Migration 使用，不注册到 DI 容器
public class MiraiNoteMigrationDbContext : MiraiNoteDbContext
{
    public MiraiNoteMigrationDbContext(DbContextOptions<MiraiNoteDbContext> options)
        : base(options) { }
}

public class MigrationDbContextFactory : IDesignTimeDbContextFactory<MiraiNoteDbContext>
{
    public MiraiNoteDbContext CreateDbContext(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Development.json", optional: false)
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<MiraiNoteDbContext>();
        optionsBuilder.UseSqlServer(config.GetConnectionString("MigrationConnection"));
        return new MiraiNoteDbContext(optionsBuilder.Options);
    }
}
```

7. 配置全局软删除过滤器和审计字段自动填充（见 copilot-instructions.md）

### Step 2 — 数据库建表
1. 创建 `User` 实体（含 REQ-01 补充字段：Email、IsEmailVerified、IsActive、LastLoginAt）
2. 创建 `EmailVerifyToken` 实体
3. 使用**迁移账户**生成并执行 EF Core Migration：
   ```
   dotnet ef migrations add InitialCreate --project MiraiNote.Data --startup-project MiraiNote.API
   dotnet ef database update --project MiraiNote.Data --startup-project MiraiNote.API
   ```
4. 执行数据初始化 Seeder：创建超级管理员账户（Id=1，Username=admin，密码从配置读取）

### Step 3 — 认证核心服务
1. 实现 `ICurrentUserService` / `CurrentUserService`（从 JWT 获取当前用户ID）
2. 实现 JWT 生成与验证（Access Token + Refresh Token）
3. 实现 `IAuthService` / `AuthService`，包含：
   - 注册（Register）
   - 登录（Login）—— 含密码错误计数与锁定逻辑
   - 登出（Logout）
   - 刷新 Token（Refresh）
   - 发送验证邮件（SendVerifyEmail）
   - 验证邮箱（VerifyEmail）
   - 忘记密码（ForgotPassword）
   - 重置密码（ResetPassword）
   - 修改密码（ChangePassword）

### Step 4 — 邮件服务
1. 实现 `IEmailService` / `EmailService`（使用 MailKit）
2. SMTP 配置从 `appsettings.json` 读取
3. 实现 REQ-01 §6 中四种邮件模板

### Step 5 — API Controller
1. 实现 `AuthController`，包含 REQ-01 §7 中所有接口
2. 实现 `UsersController`（管理员功能：用户列表、创建用户、启用/禁用）
3. 配置全局异常中间件
4. 配置 JWT 认证中间件
5. 配置 CORS（允许前端域名）

### Step 6 — 前端（Vue 3）
1. 初始化 Vite + Vue 3 + TypeScript 项目
2. 安装依赖：Tailwind CSS、Pinia、Vue Router、Axios
3. 配置 Axios 实例（baseURL、JWT 拦截器、无感刷新）
4. 实现 `useAuthStore`（Pinia）
5. 实现以下页面（参考 REQ-01 §5）：
   - `/login` 登录页
   - `/register` 注册页
   - `/verify-email` 邮箱验证结果页
   - `/forgot-password` 忘记密码页
   - `/reset-password` 重置密码页
6. 实现路由守卫（未登录跳转 `/login`）

---

## 配置文件模板

### appsettings.json（提交到代码库，不含敏感信息）
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "",
    "MigrationConnection": ""
  },
  "Jwt": {
    "Secret": "",
    "Issuer": "MiraiNote",
    "Audience": "MiraiNote",
    "AccessTokenExpiryHours": 2,
    "RefreshTokenExpiryDays": 1,
    "RefreshTokenExpiryDaysRememberMe": 30
  },
  "Email": {
    "SmtpHost": "",
    "SmtpPort": 587,
    "SmtpUser": "",
    "SmtpPassword": "",
    "FromAddress": "",
    "FromName": "MiraiNote"
  },
  "Seed": {
    "AdminPassword": ""
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

### appsettings.Development.json（仅本地，已加入 .gitignore）
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=<远程服务器IP>;Database=MiraiNote;User Id=MiraiNote;Password=<应用账户密码>;TrustServerCertificate=True;",
    "MigrationConnection": "Server=<远程服务器IP>;Database=MiraiNote;User Id=Bill.Gong;Password=<迁移账户密码>;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Secret": "<随机生成32位以上字符串>"
  },
  "Email": {
    "SmtpHost": "<你的SMTP服务器>",
    "SmtpPort": 587,
    "SmtpUser": "<邮箱账号>",
    "SmtpPassword": "<邮箱密码>",
    "FromAddress": "<发件邮箱>"
  },
  "Seed": {
    "AdminPassword": "<管理员初始密码>"
  }
}
```

> **两个连接字符串的职责：**
> - `DefaultConnection`（MiraiNote 账户）：程序运行时 `MiraiNoteDbContext` 使用
> - `MigrationConnection`（Bill.Gong 账户）：`dotnet ef database update` 时使用，程序启动后不加载

---

## 完成标准

实现完成后，请确认以下内容全部可运行：
1. `dotnet ef database update` 成功建表（使用迁移账户）
2. 启动 API 项目，Swagger 可访问所有认证接口（使用应用账户）
3. 调用 `/api/v1/auth/register` 注册成功，收到验证邮件
4. 调用 `/api/v1/auth/login` 登录成功，返回 Access Token
5. 前端登录页可正常登录并跳转首页
