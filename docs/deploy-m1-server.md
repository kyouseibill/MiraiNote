# M1 后端上线清单（服务器 211.136.180.123，站点 http://211.136.180.123:10090）

> 状态（2026-08-22）：数据库迁移已提前应用（有备份 `D:\webroot\MiraiNote\MiraiNote_pre_M1.bak`），本清单只需替换站点文件。发布包：`release/MiraiNote.API-M1/`。

## 一、你需要在服务器上执行的步骤

1. **备份站点应用目录**（`fileservice\` 在站外不受影响；若站点根即 D:\webroot\MiraiNote，把应用文件部分压缩留档）
2. **停应用**：IIS 对应应用程序池 Stop（避免 DLL 文件锁），或在站点根放 `app_offline.htm`
3. **覆盖文件**：把 `release/MiraiNote.API-M1/` 全部内容复制到站点根。**注意保留服务器上现有的 `appsettings.Production.json`**（发布包里只有 template，不含凭据）
4. **改 `appsettings.Production.json`**，两处：
   - `Cors:AllowedOrigins` 增加 `"http://tauri.localhost"`（桌面端 Tauri WebView2 的 origin，缺它桌面端登录报 Network Error——联调实测）
   - （可选，按设计 §3.5）`Upload:PhysicalPath=D:\webroot\MiraiNote\fileservice\uploads`、`WorkspaceRoot=D:\webroot\MiraiNote\fileservice\workspace`、新增 `ExportsRoot`/`TempRoot` 同级目录
5. **启动应用池**，验证：
   - `GET http://211.136.180.123:10090/api/v1/mirai/inbox` 未带 token → 应为 **401**（出现 M1 端点；404 说明还是旧版）
   - Web 端登录 + 记录增删冒烟（应与之前完全一致）
   - 生产库连接串不变，`temp 清理` 等新后台服务会随启动注册（日志可见"temp 目录清理服务已启动"）

## 二、桌面端切换到线上（部署完成后）

`desktop/.env.local`（或正式打包时的构建环境变量）：

```
MIRAI_API_BASE=http://211.136.180.123:10090/api/v1
MIRAI_USE_MOCK=0
```

## 三、回滚

- 应用层：还原第 1 步的站点备份即可（旧代码对 M1 新表无感知）
- 数据层：无需回滚（迁移只增不改；极端情况用备份 .bak 还原整库）

## 四、安全提示（不阻塞上线，建议排期）

- `:10090` 是明文 HTTP，桌面端 JWT 与数据经公网明文传输。个人使用可接受，建议后续加 HTTPS（反向代理或证书直挂），或 M3 本地模式彻底绕开
- 本次会话中数据库口令与 DeepSeek Key 曾在明文渠道出现过，按既定计划**轮换一次**（改 SQL 登录口令 + DeepSeek Key，同步更新服务器 appsettings.Production.json）
