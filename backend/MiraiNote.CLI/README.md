# MiraiNote CLI

MiraiNote CLI 同时面向人工操作、Codex 和自动化脚本。工作记录命令支持稳定的 JSON 输入/输出，不依赖交互式终端。

## 本地安装

在仓库根目录执行：

```powershell
dotnet pack backend/MiraiNote.CLI/MiraiNote.CLI.csproj -c Release -o artifacts/cli
dotnet tool install --global --add-source artifacts/cli MiraiNote.CLI
```

已经安装旧版本时，使用以下命令更新：

```powershell
dotnet tool update --global --add-source artifacts/cli MiraiNote.CLI
```

首次使用需要配置服务地址并登录：

```powershell
mirainote config --api http://localhost:5273
mirainote login
```

## Codex 调用工作记录

Codex 应始终添加 `--json`，并根据进程退出码判断结果。可先读取机器可读契约：

```powershell
mirainote worklog schema --json
```

查询和读取：

```powershell
mirainote worklog list --from 2026-07-21 --to 2026-07-27 --status in-progress --json
mirainote worklog get 42 --json
mirainote worklog categories --json
```

使用参数创建：

```powershell
mirainote worklog create --title "完成 CLI 改造" --category "MiraiNote" --status completed --json
```

内容较长时，推荐通过标准输入传 JSON，避免 shell 转义问题：

```powershell
@'
{
  "title": "重构工作记录 CLI",
  "logDate": "2026-07-26",
  "purpose": "允许 Codex 直接调用",
  "content": "补齐查询、创建、局部更新、删除和分类命令。",
  "tags": "CLI,Codex",
  "category": "MiraiNote",
  "status": "completed",
  "statusRemark": "已完成本地验证"
}
'@ | mirainote worklog create --stdin --json
```

局部更新只覆盖传入字段。JSON 中的 `null` 可清空可空字段：

```powershell
'{"status":"completed","statusRemark":null}' |
  mirainote worklog update 42 --stdin --json
```

删除命令在自动化模式下必须显式传入 `--yes`：

```powershell
mirainote worklog delete 42 --yes --json
```

状态可使用名称 `unmarked`、`in-progress`、`completed`、`delayed`，也可使用数字 `0` 到 `3`。

退出码：`0` 表示成功，`1` 表示 API 或网络错误，`2` 表示输入错误，`3` 表示未登录。JSON 错误结果包含稳定的 `error.code` 和可读的 `error.message`。

`worklog add` 作为 `worklog create` 的兼容命令保留，但不会再因缺少参数自动进入交互提示。
