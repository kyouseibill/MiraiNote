# MiraiNote Agent 阶段 2 方案

## 阶段 1 回顾

测试中 Agent 已经能自主完成多步骤任务（20+ 次工具调用、递归探索、排错重试），
但缺少以下能力：

| 缺失能力 | 具体表现 |
|----------|----------|
| 显式规划 | LLM 直接行动，没有先输出"我将做以下几步..." |
| 输出自检 | 没有对最终结果做质量评估和修正 |
| 破坏性确认 | Shell/文件写入/删除操作没有用户确认步骤 |
| 上下文压缩 | 长对话历史会撑爆 token 窗口 |

## 阶段 2：三个核心模块

### 模块 A：Planner（任务规划器）

**目标**：让 Agent 在行动之前先做显式规划。

**实现方式**：双阶段执行
```
用户输入 → Planner prompt（只输出计划，不可调工具）
         → 展示计划给用户
         → Executor（按计划逐步执行，可调工具）
```

**新增文件**：`Agent/AgentPlanner.cs`
```csharp
public class Plan
{
    public List<PlanStep> Steps { get; set; }     // 执行步骤
    public string Goal { get; set; }               // 目标摘要
    public List<string> Risks { get; set; }         // 风险提示
}

public class PlanStep
{
    public int Order { get; set; }
    public string Action { get; set; }              // 做什么
    public List<string> Tools { get; set; }         // 预计用哪些工具
    public string ExpectedOutput { get; set; }      // 预期产出
}
```

**用户体验**：
```
你: 检查项目代码质量，修复发现的低级问题

MiraiAgent 计划：
  1. 用 list_files 扫描项目结构
  2. 用 read_file 读取关键代码文件
  3. 用 run_shell dotnet build 检查编译
  4. 分析发现的问题
  5. 用 write_file 修复问题
  6. 重新编译验证

风险：修改代码可能引入新问题，修复后需验证
确认执行？[Y/n]
```

### 模块 B：Reflector（自我反思器）

**目标**：每次任务完成后，Agent 对自己的输出做质量检查。

**实现方式**：执行完成后追加一次"反思回合"
```
Executor 完成 → Reflector 检查：
  1. 任务目标是否达成？
  2. 输出是否有遗漏？
  3. 是否有更好的方案？
  → 如发现问题，自动追加执行或给出改进建议
```

**新增文件**：`Agent/AgentReflector.cs`

**反思维度**：

| 维度 | 检查项 |
|------|--------|
| 完整性 | 原始需求的所有点都满足了吗？ |
| 正确性 | 工具返回的结果是否被正确解读？ |
| 安全性 | 是否执行了可能有风险的操作？ |
| 可改进 | 有没有更优的实现方式？ |

**样例**：
```
任务完成 → 反思中...

✓ 完整性：3/3 需求满足
✓ 正确性：编译通过，无新错误
⚠ 安全性：修改了 Program.cs，建议人工 review
💡 建议：第2步可用 dotnet format 自动格式化
```

### 模块 C：确认机制（Guard）

**目标**：对破坏性操作（文件覆写、Shell 执行、数据删除）增加用户确认。

**实现方式**：工具分级
```csharp
public enum ToolRiskLevel
{
    Safe,      // 只读操作，无需确认
    Write,     // 写入操作，提示但不阻塞
    Dangerous  // 破坏性操作，必须确认
}
```

工具风险分级：

| 风险等级 | 工具 |
|----------|------|
| Safe | search_work_logs, search_memos, search_life_logs, read_file, list_files, system_info |
| Write | create_work_log, create_memo, write_file, generate_weekly_report |
| Dangerous | delete_work_log, delete_memo, run_shell, write_file (覆写) |

## 实施计划

### 第 1 步：Planner（1 天）

新增文件：
- `Agent/AgentPlanner.cs` — 规划逻辑
- 修改 `AgentCommand.cs` — 交互模式集成 Planner

关键改动：
- 新增 `--plan` 模式：先展示计划，用户确认后再执行
- 默认 `--auto` 模式：自动规划+执行（当前行为）

### 第 2 步：Reflector（半天）

新增文件：
- `Agent/AgentReflector.cs` — 反思逻辑

关键改动：
- `AgentLoop.RunAsync` 完成后的反思回合
- `AgentDisplay` 新增反思结果渲染

### 第 3 步：确认机制（半天）

改动文件：
- `IAgentTool` 新增 `RiskLevel` 属性
- `AgentLoop` 执行工具前检查风险等级
- 高风险工具触发用户确认

### 第 4 步：上下文管理（1 天）

新增文件：
- `Agent/AgentContextManager.cs`

功能：
- 自动检测 token 用量
- 超阈值时对历史消息做摘要压缩
- 保留关键信息（用户偏好、当前任务上下文）

## 不纳入阶段 2 的内容

以下功能留到阶段 3：
- 向量数据库 RAG 记忆
- 多轮任务调度（定时执行、依赖链）
- 多模态支持（图片理解）
- 本地 LLM 支持（Ollama）
- 技能插件市场
