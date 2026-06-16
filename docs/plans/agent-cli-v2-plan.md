# MiraiNote CLI → Agent 产品改造方案（路线 2）

## 现状分析

### 已有的 Agent 基础架构 ✅
ChatService 已经实现了完整的 **Function Calling 循环（ReAct 模式）**：
- DeepSeek API 调用 + 工具定义（14 个工具）
- 流式 SSE 解析（含 tool_call delta 拼合）
- 工具执行调度（读：search_work_logs/memos/life_logs/reports + 写：CRUD）
- 最多 8 轮 tool-use 循环
- Tavily 互联网搜索集成

### CLI 已有的"Agent 友好"特性 ✅
- 所有命令支持 `--json` 输出
- `chat --message` 单次模式（可被外部 Agent 调用）
- Token 持久化（跨调用会话保持）
- `--yes` 跳过确认

### 缺失的 Agent 能力 ❌
1. **CLI 本地 Agent 循环**：当前 chat 命令只是透传消息到 API，API 端做 tool calling。CLI 端没有自己的 Agent 循环。
2. **多步骤任务规划**：无法把"帮我总结本周工作并生成周报发邮件"分解执行。
3. **自我反思/校验**：LLM 生成的内容没有二次验证。
4. **CLI 原生工具**：没有文件操作、shell 执行、本地系统信息等工具。
5. **Agent 状态可视化**：用户看不到 Agent 的内部思考/调用过程。
6. **工具执行的确认机制**：对破坏性操作无用户确认步骤。

## 改造方案

### 阶段 1：CLI 内建 Agent 循环（核心改造）

**新增文件：**
```
MiraiNote.CLI/
├── Agent/
│   ├── AgentLoop.cs          -- ReAct 循环引擎
│   ├── AgentToolRegistry.cs  -- 工具注册表（可扩展）
│   ├── AgentPlanner.cs       -- 多步骤任务规划器
│   ├── AgentReflector.cs     -- 输出自校验
│   └── AgentDisplay.cs       -- Spectre 渲染（思考/工具调用过程可视化）
```

**改造文件：**
- `ChatCommand.cs` → 新增 `mirainote agent` 命令，替代或增强现有 chat

**核心流程：**
```
用户输入 → Planner(可选) → ReAct Loop:
  1. LLM 决策 (think)
  2. 工具调用 (act)
  3. 结果观察 (observe)
  4. 自反思 (reflect, 可选)
  5. 继续或结束
→ 最终输出
```

### 阶段 2：新增 CLI 原生工具
- `file_read` / `file_write` — 本地文件操作
- `run_shell` — 执行 shell 命令（沙箱限制）
- `system_info` — 系统信息
- `web_fetch` — 网页内容抓取（新增）
- `send_email` — 发送邮件（利用已有 SMTP）

### 阶段 3：自我改进能力
- 工具调用结果缓存
- 失败自动重试（指数退避）
- 输出质量自评分
- 上下文窗口管理（自动摘要长历史）

## 实施优先级

| 优先级 | 任务 | 工作量 |
|--------|------|--------|
| P0 | AgentLoop 核心循环（替代现有 chat 的 API 透传） | 3天 |
| P0 | Agent 过程可视化（思考/工具调用/结果展示） | 1天 |
| P1 | 多步骤 Planner | 2天 |
| P1 | 自我反思/输出校验 | 1天 |
| P2 | 本地文件/shell 工具 | 2天 |
| P2 | 确认机制（破坏性操作） | 0.5天 |
| P3 | 邮件发送工具 | 1天 |
| P3 | Web Fetch 工具 | 1天 |

## 技术决策

1. **Agent 循环放在 CLI 端还是 API 端？**
   → CLI 端。理由：CLI 可以访问本地资源（文件、shell），且不需要网络往返延迟。API 端的 tool calling 保留给 Web 前端使用。

2. **LLM 调用方式？**
   → 直接调用 DeepSeek API（复用 AppOptions 配置），不经过自己的 API 中转。
   减少一次网络跳转，降低延迟。

3. **工具注册方式？**
   → 使用接口 + 反射注册，类似 ASP.NET 的 DI 模式，方便插件扩展。
