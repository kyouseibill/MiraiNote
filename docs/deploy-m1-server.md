# M1 后端上线清单（服务器 <SERVER_IP>，站点 http://<SERVER_IP>:10090）

> 状态（2026-08-22）：数据库迁移已提前应用（有备份 `D:\webroot\MiraiNote\MiraiNote_pre_M1.bak`），本清单只需替换站点文件。发布包：`release/MiraiNote.API-M1/`。

## 一、你需要在服务器上执行的步骤

1. **备份站点应用目录**（`fileservice\` 在站外不受影响；若站点根即 D:\webroot\MiraiNote，把应用文件部分压缩留档）
2. **停应用**：IIS 对应应用程序池 Stop（避免 DLL 文件锁），或在站点根放 `app_offline.htm`
3. **覆盖文件**：把 `release/MiraiNote.API-M1/` 全部内容复制到站点根。**注意保留服务器上现有的 `appsettings.Production.json`**（发布包里只有 template，不含凭据）
4. **改 `appsettings.Production.json`**，两处：
   - `Cors:AllowedOrigins` 增加 `"http://tauri.localhost"`（桌面端 Tauri WebView2 的 origin，缺它桌面端登录报 Network Error——联调实测）
   - （可选，按设计 §3.5）`Upload:PhysicalPath=D:\webroot\MiraiNote\fileservice\uploads`、`WorkspaceRoot=D:\webroot\MiraiNote\fileservice\workspace`、新增 `ExportsRoot`/`TempRoot` 同级目录
5. **启动应用池**，验证：
   - `GET http://<SERVER_IP>:10090/api/v1/mirai/inbox` 未带 token → 应为 **401**（出现 M1 端点；404 说明还是旧版）
   - Web 端登录 + 记录增删冒烟（应与之前完全一致）
   - 生产库连接串不变，`temp 清理` 等新后台服务会随启动注册（日志可见"temp 目录清理服务已启动"）

## 二、桌面端切换到线上（部署完成后）

`desktop/.env.local`（或正式打包时的构建环境变量）：

```
MIRAI_API_BASE=http://<SERVER_IP>:10090/api/v1
MIRAI_USE_MOCK=0
```

## 三、回滚

- 应用层：还原第 1 步的站点备份即可（旧代码对 M1 新表无感知）
- 数据层：无需回滚（迁移只增不改；极端情况用备份 .bak 还原整库）

## 四、邮件（SMTP）

注册验证、找回密码和备忘提醒都走同一套 SMTP。密钥只放在服务器自己的 `appsettings.Production.json` 或环境变量里，不要提交到 git。QQ 邮箱要等 SMTP 授权码重新生成后再填到部署环境。

| 配置项 | 环境变量 |
| --- | --- |
| `Email:SmtpHost` | `Email__SmtpHost` |
| `Email:SmtpPort` | `Email__SmtpPort` |
| `Email:SmtpUser` | `Email__SmtpUser` |
| `Email:SmtpPassword` | `Email__SmtpPassword` |
| `Email:FromAddress` | `Email__FromAddress` |
| `Email:FromName` | `Email__FromName` |
| `Email:UseSsl` | `Email__UseSsl` |

`SmtpHost` 为空时不发信。`UseSsl` 为 `false` 时走 STARTTLS，为 `true` 时走 SSL 直连。

发件人优先用 `FromAddress`，为空时才回退到 `SmtpUser`。用 QQ 邮箱时 `FromAddress` 要填成和 `SmtpUser` 相同的 QQ 地址；用 Resend 这类服务时 `SmtpUser` 是固定用户名（如 `resend`），`FromAddress` 填已验证域名下的发件地址。

## 五、PostgreSQL（Ubuntu / 本机回环）

生产库只监听本机，由 Nginx 反代站点。连接串放在服务器自己的环境变量或未入库的 `appsettings.Production.json`，不要提交口令。

Npgsql 连接串占位：

```
Host=127.0.0.1;Port=5432;Database=mirainote;Username=mirainote_app;Password=<PASSWORD>
```

| 配置项 | 环境变量 |
| --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` |
| `ConnectionStrings:MigrationConnection` | `ConnectionStrings__MigrationConnection` |
| `App:FrontendBaseUrl` | `App__FrontendBaseUrl` |
| `App:PublicBaseUrl` | `App__PublicBaseUrl` |
| `App:RequireEmailVerification` | `App__RequireEmailVerification` |

`PublicBaseUrl` 有值时，验证邮件和重置邮件用它拼绝对链接；为空时回落 `FrontendBaseUrl`。生产应写成 `https://` 开头的站点地址。`RequireEmailVerification` 默认 true；设为 false 时不发验证邮件，也不拦截未验证登录。

迁移命令使用 `MigrationConnection`。空库执行 `dotnet ef database update`，迁移名 `InitialPostgres`。表清单、时间列换算和导入前查重见 `docs/pg-table-list.md`。

新表必须授权给运行账户 `appuser`：表上 `GRANT SELECT, INSERT, UPDATE, DELETE`，identity 序列上 `GRANT USAGE, SELECT`。迁移账户建表后 `appuser` 默认没有这些权限，应用连得上库但读写会失败。`WelcomeNewsSeen` 的授权写在迁移 `AddWelcomeNewsSeen` 里。这条迁移若已经执行过，要单独补一次同样的 `GRANT`；东京库已经授过。

`WelcomePhrase` 不再使用。部署时跑到迁移 `DropWelcomePhrase` 就会删表。授权随表消失，不必再 `REVOKE`，也不要给这张表补 `GRANT`。问候池在代码里，小句由 DeepSeek 现写。

## 六、健康检查与转发头

`GET /health` 只表示进程还在，返回纯文本 `Healthy`。`GET /health/ready` 用数据库上下文检查 PostgreSQL，连不上返回 503。两个路径都不带 `/api`，也不需要登录。

Nginx 上这两段要写在 SPA 的 `try_files` / `index.html` 回退之前，用精确匹配把它们反代到本机 Kestrel（端口按服务器实际监听地址改），并且不记访问日志：

```nginx
location = /health {
    access_log off;
    proxy_pass http://127.0.0.1:5273;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
}

location = /health/ready {
    access_log off;
    proxy_pass http://127.0.0.1:5273;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
}
```

`$remote_addr` 会覆盖客户端自己带进来的 `X-Forwarded-For`，伪造的值不会再传到应用。主站的 `location /api/` 也用同样的两行：

```nginx
proxy_set_header X-Forwarded-For $remote_addr;
proxy_set_header X-Forwarded-Proto $scheme;
```

如果走 Cloudflare 代理，先用 `real_ip_header CF-Connecting-IP` 和 `set_real_ip_from` 把 `$remote_addr` 还原成真实客户端。`set_real_ip_from` 只列 Cloudflare 官方 IP 段（来源：<https://www.cloudflare.com/ips/>）：

```nginx
real_ip_header CF-Connecting-IP;
set_real_ip_from 173.245.48.0/20;
set_real_ip_from 103.21.244.0/22;
set_real_ip_from 103.22.200.0/22;
set_real_ip_from 103.31.4.0/22;
set_real_ip_from 141.101.64.0/18;
set_real_ip_from 108.162.192.0/18;
set_real_ip_from 190.93.240.0/20;
set_real_ip_from 188.114.96.0/20;
set_real_ip_from 197.234.240.0/22;
set_real_ip_from 198.41.128.0/17;
set_real_ip_from 162.158.0.0/15;
set_real_ip_from 104.16.0.0/13;
set_real_ip_from 104.24.0.0/14;
set_real_ip_from 172.64.0.0/13;
set_real_ip_from 131.0.72.0/22;
set_real_ip_from 2400:cb00::/32;
set_real_ip_from 2606:4700::/32;
set_real_ip_from 2803:f800::/32;
set_real_ip_from 2405:b500::/32;
set_real_ip_from 2405:8100::/32;
set_real_ip_from 2a06:98c0::/29;
set_real_ip_from 2c0f:f248::/32;
```

应用只接受来自 `127.0.0.1` 和 `::1` 的 `X-Forwarded-For`、`X-Forwarded-Proto`，并且只取一跳。启用这段代码后，服务器上的 `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` 要删掉：那个环境变量会信任任意来源的转发头，也不要和代码里的配置一起开。

验证邮件和重置邮件的链接只用 `App:PublicBaseUrl`（为空才回落 `FrontendBaseUrl`），不看请求里的 Host。登录锁定按账号，验证邮件冷却按用户，都不按套接字地址计数；转发头在认证前面，所以之后如果按 `Connection.RemoteIpAddress` 做限流，读到的是转发后的客户端地址。

## 七、安全提示（不阻塞上线，建议排期）

- `:10090` 是明文 HTTP，桌面端 JWT 与数据经公网明文传输。个人使用可接受，建议后续加 HTTPS（反向代理或证书直挂），或 M3 本地模式彻底绕开
- 本次会话中数据库口令与 DeepSeek Key 曾在明文渠道出现过，按既定计划**轮换一次**（改 SQL 登录口令 + DeepSeek Key，同步更新服务器 appsettings.Production.json）
