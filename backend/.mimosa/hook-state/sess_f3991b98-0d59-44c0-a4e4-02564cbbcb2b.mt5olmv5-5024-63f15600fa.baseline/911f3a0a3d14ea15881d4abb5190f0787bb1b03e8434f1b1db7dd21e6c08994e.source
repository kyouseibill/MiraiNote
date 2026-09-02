using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MiraiNote.CLI;
using MiraiNote.CLI.Commands;
using MiraiNote.CLI.Services;
using Spectre.Console.Cli;

// 统一控制台编码为 UTF-8（解决 PTY 和管道输出乱码）
Console.OutputEncoding = Encoding.UTF8;

// ===== 注册服务 =====
var services = new ServiceCollection();

var store = new TokenStore();
store.Load();

services.AddSingleton(store);
services.AddSingleton(sp => new ApiClient(sp.GetRequiredService<TokenStore>()));

// ===== 注册命令 =====
var registrar = new DependencyInjectionRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("mirainote");
    config.SetApplicationVersion("2.0.0");

    // ── 认证 ─────────────────────────────────────
    config.AddCommand<LoginCommand>("login")
        .WithDescription("登录 MiraiNote 账户");

    config.AddCommand<LogoutCommand>("logout")
        .WithDescription("注销当前登录");

    config.AddCommand<ConfigCommand>("config")
        .WithDescription("查看或设置 CLI 配置（如 API 服务地址）");

    // ── 工作记录 ──────────────────────────────────
    config.AddBranch("worklog", wl =>
    {
        wl.SetDescription("工作记录管理");

        wl.AddCommand<WorkLogListCommand>("list")
            .WithDescription("列出工作记录")
            .WithExample(["worklog", "list"])
            .WithExample(["worklog", "list", "--from", "2026-06-01", "--to", "2026-06-07"])
            .WithExample(["worklog", "list", "-k", "会议"]);

        wl.AddCommand<WorkLogGetCommand>("get")
            .WithDescription("按 ID 获取工作记录")
            .WithExample(["worklog", "get", "42", "--json"]);

        wl.AddCommand<WorkLogCreateCommand>("create")
            .WithDescription("新建工作记录（支持参数或 JSON 输入）")
            .WithExample(["worklog", "create", "--title", "完成 CLI 改造", "--status", "completed", "--json"]);

        // 保留旧命令名，避免现有脚本失效。
        wl.AddCommand<WorkLogCreateCommand>("add")
            .WithDescription("新建工作记录（create 的兼容别名）");

        wl.AddCommand<WorkLogUpdateCommand>("update")
            .WithDescription("更新工作记录（只修改明确提供的字段）")
            .WithExample(["worklog", "update", "42", "--status", "completed", "--json"]);

        wl.AddCommand<WorkLogDeleteCommand>("delete")
            .WithDescription("删除工作记录")
            .WithExample(["worklog", "delete", "42", "--yes", "--json"]);

        wl.AddCommand<WorkLogCategoriesCommand>("categories")
            .WithDescription("列出已有工作分类");

        wl.AddCommand<WorkLogSchemaCommand>("schema")
            .WithDescription("输出供 Codex/自动化调用的机器可读接口说明");
    });

    // ── 备忘 ──────────────────────────────────────
    config.AddBranch("memo", memo =>
    {
        memo.SetDescription("备忘/待办事项管理");

        memo.AddCommand<MemoListCommand>("list")
            .WithDescription("列出备忘事项")
            .WithExample(["memo", "list"])
            .WithExample(["memo", "list", "--section", "life"])
            .WithExample(["memo", "list", "--all"]);

        memo.AddCommand<MemoAddCommand>("add")
            .WithDescription("新建备忘（交互式）");

        memo.AddCommand<MemoDoneCommand>("done")
            .WithDescription("将备忘标记为已完成")
            .WithExample(["memo", "done", "10"])
            .WithExample(["memo", "done", "10", "--undo"]);

        memo.AddCommand<MemoDeleteCommand>("delete")
            .WithDescription("删除备忘");
    });

    // ── 生活记录 ──────────────────────────────────
    config.AddBranch("lifelog", ll =>
    {
        ll.SetDescription("生活记录/日记管理");

        ll.AddCommand<LifeLogListCommand>("list")
            .WithDescription("列出生活记录")
            .WithExample(["lifelog", "list"])
            .WithExample(["lifelog", "list", "--month", "2026-06"])
            .WithExample(["lifelog", "list", "--mood", "开心"]);

        ll.AddCommand<LifeLogAddCommand>("add")
            .WithDescription("新建生活记录（交互式）");

        ll.AddCommand<LifeLogDeleteCommand>("delete")
            .WithDescription("删除生活记录");
    });

    // ── AI 对话 ───────────────────────────────────
    config.AddCommand<ChatCommand>("chat")
        .WithDescription("与 AI 助理对话（支持查询和创建数据、互联网搜索）")
        .WithExample(["chat"])
        .WithExample(["chat", "--session", "5"]);

    // ── Agent ──────────────────────────────────────
    config.AddCommand<AgentCommand>("agent")
        .WithDescription("MiraiNote AI Agent —— 自主规划、多步骤执行、工具调用")
        .WithExample(["agent"])
        .WithExample(["agent", "--message", "帮我总结本周工作并生成周报", "--json"])
        .WithExample(["agent", "--verbose", "--message", "检查项目结构"])
        .WithExample(["agent", "--deepseek-key", "sk-xxx"]);

    // ── 周报 ──────────────────────────────────────
    config.AddBranch("weekly", w =>
    {
        w.SetDescription("工作周报管理");

        w.AddCommand<WeeklyListCommand>("list")
            .WithDescription("列出已生成的周报");

        w.AddCommand<WeeklyGenerateCommand>("generate")
            .WithDescription("AI 生成本周（或指定周）周报")
            .WithExample(["weekly", "generate"])
            .WithExample(["weekly", "generate", "--week-start", "2026-06-01"]);

        w.AddCommand<WeeklyViewCommand>("view")
            .WithDescription("查看周报详情")
            .WithExample(["weekly", "view", "3"]);
    });

    // ── 定时任务 ───────────────────────────────────
    config.AddBranch("task", t =>
    {
        t.SetDescription("定时任务管理");

        t.AddCommand<TaskListCommand>("list")
            .WithDescription("列出定时任务")
            .WithExample(["task", "list"]);

        t.AddCommand<TaskDeleteCommand>("delete")
            .WithDescription("取消定时任务")
            .WithExample(["task", "delete", "42"]);
    });
});

return app.Run(args);
