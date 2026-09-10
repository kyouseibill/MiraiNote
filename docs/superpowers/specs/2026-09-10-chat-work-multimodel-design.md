# Mirai Chat / Work 双模式与多模型接入详细设计

## 1. 文档目的

本文定义 MiraiNote 中 Chat（对话）与 Work（工作）两种模式的产品边界、执行架构、状态反馈、多模型配置和 MiniMax 接入方案。

本文供后续开发直接使用。它建立在现有持久化 `AgentRun` 设计之上，不替代 `2026-09-09-durable-agent-runs-design.md`。

## 2. 已确认决策

1. Chat 与 Work 是两条明确的执行路径，不再通过关键词自动决定是否进入 Agent。
2. Chat 默认不执行浏览器操作、Shell 命令和文件写入；需要这些能力时，由用户明确切换到 Work。
3. Work 使用持久化 `AgentRun`，客户端断线或切换页面不应终止后台任务。
4. Work 在没有结束时必须持续显示当前阶段和耗时，尤其是在正文已生成、正在做完成检查时，不能让用户误以为卡死。
5. 自动记忆提取不得阻塞任务完成事件。
6. 系统接入 DeepSeek 和 MiniMax，后续允许继续扩展其他模型提供商。
7. 每个会话固定一个模型。用户可以切换模型，但切换模型会创建新会话；已有消息的会话不能原地换模型。
8. API Key 只保存在服务端配置或环境变量中，不进入前端、不写入数据库、不提交 Git。

## 3. 目标与非目标

### 3.1 目标

- 让用户一眼理解 Chat 是快速回答，Work 是可持续执行的任务。
- 让 Chat 在正文结束后立即结束，不被额外完成检查拖延。
- 让 Work 的计划、执行、核验、整理交付过程可见。
- 在 Work 任务执行期间持续提供真实状态，而不是重复输出无意义文字。
- 建立统一模型提供商抽象，支持 DeepSeek 与 MiniMax 的流式输出、推理内容和工具调用。
- 保证会话使用的模型稳定、可追溯，避免同一上下文中途切换模型。
- 保持现有持久化任务的断线重连、停止、确认和恢复能力。

### 3.2 非目标

- 本次不把 Dashboard 欢迎语、周报生成等其他 AI 功能切换为用户所选模型；多模型选择只作用于 Mirai Chat。
- 本次不增加第三个 Auto 模式。
- 本次不允许用户自定义 Base URL、任意模型 ID 或直接从浏览器提交 API Key。
- 本次不做跨服务器分布式任务调度，仍沿用数据库账本加应用内后台 Worker。
- 本次不实现同一会话按消息切换模型。

## 4. 产品模式定义

### 4.1 Chat：快速对话

Chat 适用于问答、解释、讨论、润色、总结用户已提供的内容和普通创作。

行为规则：

- 始终调用普通流式对话接口。
- 模型返回正常 `finish_reason=stop` 后，保存消息并立即发送 `done`。
- 不调用 `AgentRunSupervisor.ReviewAsync` 做同步语义完成检查。
- 不创建持久化 `AgentRun`。
- 不开放浏览器、Shell、文件写入、外部系统写入等执行型工具。
- 可以处理纯文本附件；是否支持图片由当前模型能力决定。
- 若提示词明显要求登录网站、运行命令、修改数据、创建文件或持续调研，前端只给出切换建议，不自动改变模式、不自动执行。

建议提示：

> 这项请求需要访问工具或产生实际交付物，建议使用 Work。切换后才会开始执行。

用户点击“切换到 Work 并发送”后，前端保持原提示词，明确改用 Work 接口发送。

### 4.2 Work：持续执行任务

Work 适用于网站调研、登录操作、文件生成、命令执行、数据修改、多步骤分析和需要交付物的任务。

行为规则：

- 始终创建持久化 `AgentRun`，不再依赖关键词判断。
- 支持工具调用、危险操作确认、停止、断线重连和服务重启后的手动恢复。
- 任务创建时把会话的提供商和模型快照写入 `AgentRun`，运行中不可改变。
- 模型正文输出完成后，进入有明确提示的核验阶段。
- 所有同步核验必须有时间和次数上限。
- 回复、工具证据和交付文件持久化完成后先发 `done`；记忆提取在后台执行。

### 4.3 模式选择与会话关系

- Chat / Work 是“本次发送的执行方式”，允许同一会话先讨论再执行。
- 模型是“整个会话的固定属性”，首次发送后锁定。
- 同一会话任何时刻只允许一个生成或 Work 任务运行。
- Work 正在运行时，不能再发送 Chat 消息；用户必须等待完成或先停止当前任务。
- 模式切换只改变下一次发送路径，不改变已有消息和正在运行的任务。

## 5. 会话模型选择体验

### 5.1 新会话

新会话输入区显示模型选择器，模型按提供商分组，例如：

- DeepSeek
  - DeepSeek V4 Flash
- MiniMax
  - MiniMax M2.7
  - MiniMax M2.7 Highspeed

默认选中服务端配置的默认模型。模型列表来自后端允许列表，前端不得硬编码完整可用模型集合。

### 5.2 模型锁定

- 空会话可以自由切换模型。
- 第一条用户消息成功持久化时，会话模型正式锁定。
- 锁定后，标题区显示模型徽标；选择器仍可点击，但不直接修改当前会话。
- 用户选择其他模型时，弹出说明：“每个会话固定使用一个模型。将使用所选模型创建新会话。”
- 用户确认后创建空白新会话并切换过去。本期不复制历史，避免不同模型混用旧工具状态和推理上下文。
- 取消则保持当前会话不变。

### 5.3 临时聊天

- 临时聊天在创建时选择模型，并在第一条消息后锁定。
- 临时聊天不会写入 `ChatSession`；前端在该临时会话生命周期内保存固定的 `provider/model`。
- 临时聊天请求必须把选定模型的公开标识发送给后端，后端仍需按允许列表校验。

### 5.4 模型不可用

如果管理员禁用了某模型：

- 已有会话仍显示原模型，但发送按钮变为不可用。
- UI 显示“当前会话模型暂不可用”。
- 提供“使用默认模型创建新会话”操作。
- 系统不得静默改用另一个模型，否则会破坏“会话固定模型”的约定。

## 6. Work 状态与界面输出设计

### 6.1 状态分层

数据库中的生命周期状态保持稳定：

- `queued`
- `running`
- `awaiting_confirmation`
- `completed`
- `failed`
- `stopped`
- `recoverable`

新增独立的运行阶段 `Phase`，避免为了 UI 提示不断扩展生命周期状态：

- `queued`：任务已进入队列
- `planning`：正在分析目标和步骤
- `executing`：正在调用模型或工具
- `verifying`：正文已生成，正在检查任务完整性
- `continuing`：检查发现遗漏，正在继续完成
- `finalizing`：正在保存结果、整理交付物
- `completed`：任务已完成

`Status` 决定任务是否终止，`Phase` 解释当前正在做什么。

### 6.2 用户可见提示

阶段变化通过独立状态卡显示，不追加到最终回答正文。

推荐文案：

| 阶段 | 显示文案 |
| --- | --- |
| queued | 任务已创建，正在等待执行… |
| planning | 正在分析任务目标和执行步骤… |
| executing | 正在执行：{当前步骤或工具名称} |
| verifying | 回答已生成，正在检查任务是否完整… |
| continuing | 检查发现还有未完成项，正在继续：{nextStep} |
| finalizing | 正在保存结果并整理交付文件… |
| awaiting_confirmation | 需要你的确认后才能继续 |
| recoverable | 服务曾中断，任务可以从已保存进度继续 |

状态卡包含：

- 当前阶段名称；
- 简短、真实的当前动作；
- 从任务开始计算的耗时；
- 当前步骤序号（存在计划时）；
- 停止按钮；
- 需要确认或恢复时的主操作按钮。

前端每秒更新本地耗时，不需要每秒写数据库。后端在长时间没有持久化事件时，可以通过 SSE 发送不落库的心跳，携带当前 `Phase` 和 `PhaseMessage`。

### 6.3 正文已经出现但任务未结束

这是本次必须解决的重点场景：

1. 模型完成一轮正文输出。
2. 后端立即持久化 `phase=verifying` 事件。
3. UI 在正文下方显示“回答已生成，正在检查任务是否完整… 8 秒”。
4. 如果检查结果为 `continue`，后端发送 `phase=continuing`，其中包含安全截断后的 `nextStep`。
5. UI 显示“检查发现还有未完成项，正在继续：生成交接文件”。随后继续显示新的工具和正文事件。
6. 如果检查通过，进入 `finalizing`，完成消息及交付物落库后发送 `done`。
7. 收到 `done` 后，状态卡转成简短完成摘要，不再显示旋转状态。

禁止使用没有信息量的重复文案，例如持续追加“仍在处理中”。没有新事件时应更新耗时，而不是污染回答正文。

### 6.4 核验时限与降级

同步完成检查采用以下边界：

- Chat：不执行语义完成检查。
- Work：只有发生过工具调用、存在交付物要求或计划步骤时才执行语义完成检查。
- 单次语义检查默认超时 15 秒，可通过配置调整。
- 自动续跑后最多再检查两次，禁止无限循环。
- 每次判定继续必须提供非空 `nextStep`，并展示给用户。

在语义检查前先执行确定性检查：

- 没有仍在运行的工具；
- 没有待确认操作；
- 用户明确要求导出文件时，必须存在成功的导出工具结果和可访问地址；
- 助手消息能够成功保存；
- 必需的工具失败没有被正文中的“已完成”描述掩盖。

语义核验超时或服务不可用，但确定性检查通过时：

- 不继续无限等待；
- 任务以 `completed` 结束；
- `done` 数据包含 `verificationStatus=unavailable`；
- UI 显示非阻塞提示“任务已完成，自动完整性检查未能完成”。

确定性检查失败时，不得标记完成；应进入继续执行、需要输入或失败状态。

### 6.5 记忆提取

- `done` 之前不得等待 `AutoExtractAsync`。
- `done` 落库并推送后，将记忆提取请求写入应用内后台队列。
- 后台记忆任务失败只记录结构化日志，不修改已完成的 AgentRun，也不向用户追加错误。
- 记忆任务使用系统配置的辅助模型，不改变会话固定模型。

## 7. 多模型后端架构

### 7.1 核心原则

Chat 编排逻辑不得再直接依赖 `DeepSeekOptions`、`CreateClient("DeepSeek")` 或 DeepSeek 特定响应字段。

新增提供商中立接口：

```csharp
public interface IChatModelProvider
{
    string ProviderId { get; }
    IAsyncEnumerable<ModelStreamEvent> StreamAsync(
        ModelRequest request,
        CancellationToken cancellationToken);
    Task<ModelResponse> CompleteAsync(
        ModelRequest request,
        CancellationToken cancellationToken);
}
```

统一输入对象至少包含：

- provider/model；
- system 和历史消息；
- 工具定义；
- tool choice；
- temperature、最大输出长度；
- 是否拆分推理内容；
- 当前执行模式。

统一流事件至少包含：

- `ThinkingDelta`
- `TextDelta`
- `ToolCallDelta`
- `Finish`
- `Usage`
- `ProviderError`

`ChatService` 或后续拆出的 `ChatOrchestrator` 只消费统一事件，不解析各家原始 SSE。

### 7.2 组件职责

建议组件：

1. `AiModelRegistry`
   - 从配置读取允许的模型。
   - 校验 provider/model 是否启用。
   - 返回不含秘密的前端模型清单。
   - 解析会话默认模型和旧会话兼容模型。

2. `IChatModelProvider`
   - 屏蔽认证、请求字段、SSE 格式和错误格式差异。
   - DeepSeek 与 MiniMax 分别实现适配器，必要时共享 OpenAI-compatible 基础解析器。

3. `ChatOrchestrator`
   - 根据 Chat 或 Work 构造系统提示和工具集合。
   - 驱动模型、工具循环、完成检查和导出兜底。
   - 不直接读取 API Key。

4. `WorkCompletionVerifier`
   - 执行确定性检查和有界语义检查。
   - 产生 `completed/continue/needs_input/blocked` 决策。
   - 负责超时和最大续跑次数。

5. `AgentMemoryBackgroundQueue`
   - 在终态之后异步执行记忆提取。

6. `AgentRunService`
   - 保持现有任务账本、事件重放、确认、停止和恢复职责。
   - 保存 provider/model 快照及当前 phase。

### 7.3 DeepSeek 适配

- 首阶段保持现有 DeepSeek 请求与响应行为不变，作为重构回归基线。
- 将当前流解析、工具调用拼装和非流式完成调用迁移到 `DeepSeekChatModelProvider`。
- 先通过适配层跑通现有测试，再接入 MiniMax，避免把“架构重构”和“新提供商协议差异”混在一起排查。

### 7.4 MiniMax 适配

MiniMax 官方提供 OpenAI-compatible 接口：

- Base URL：`https://api.minimax.io/v1`
- Chat Completions：`POST /chat/completions`
- Models：`GET /models`
- Bearer API Key 认证
- 支持流式输出与工具调用

实现要求：

- 使用配置允许列表展示模型，不在每次页面加载时直接把官方 `/models` 结果全部暴露给用户。
- 默认预留 `MiniMax-M2.7` 和 `MiniMax-M2.7-highspeed`，实际启用项由部署配置决定。
- 使用 `reasoning_split=true` 时，把 MiniMax 的 `reasoning_details` 归一化为 `ThinkingDelta`。
- 如果返回内容包含 `<think>` 标签，适配器必须拆分推理内容与正式回答，不能把标签直接显示在正文。
- 多轮工具调用必须保留完整 assistant 响应，包括 `tool_calls` 和模型要求保留的推理字段，再把工具结果加入下一轮历史。
- MiniMax 特有的敏感内容标记和错误体应转换为统一、可理解的错误消息，原始响应不得包含 API Key。

官方参考：

- https://platform.minimax.io/docs/api-reference/text-openai-api
- https://platform.minimax.io/docs/api-reference/text-chat-openai
- https://platform.minimax.io/docs/api-reference/models/openai/list-models

## 8. 配置设计

推荐新增统一 `AI` 配置，并在过渡期兼容现有 `DeepSeek` 配置：

```json
{
  "AI": {
    "DefaultModelKey": "deepseek:deepseek-v4-flash",
    "CompletionCheckTimeoutSeconds": 15,
    "MaxContinuationReviews": 2,
    "AuxiliaryModelKey": "deepseek:deepseek-v4-flash",
    "Providers": {
      "deepseek": {
        "DisplayName": "DeepSeek",
        "BaseUrl": "https://api.deepseek.com",
        "ApiKey": "",
        "Enabled": true
      },
      "minimax": {
        "DisplayName": "MiniMax",
        "BaseUrl": "https://api.minimax.io/v1",
        "ApiKey": "",
        "Enabled": false
      }
    },
    "Models": [
      {
        "Key": "deepseek:deepseek-v4-flash",
        "Provider": "deepseek",
        "ModelId": "deepseek-v4-flash",
        "DisplayName": "DeepSeek V4 Flash",
        "Enabled": true,
        "SupportsTools": true,
        "SupportsReasoning": true,
        "SupportsImages": false,
        "AllowedModes": ["chat", "work"]
      },
      {
        "Key": "minimax:MiniMax-M2.7",
        "Provider": "minimax",
        "ModelId": "MiniMax-M2.7",
        "DisplayName": "MiniMax M2.7",
        "Enabled": false,
        "SupportsTools": true,
        "SupportsReasoning": true,
        "SupportsImages": false,
        "AllowedModes": ["chat", "work"]
      }
    ]
  }
}
```

生产环境通过环境变量或受保护配置注入 Key，例如：

```text
AI__Providers__minimax__ApiKey=<由部署环境注入>
```

要求：

- 仓库中的配置模板保留空字符串，不写真实 Key。
- Provider 缺少 Key 时不导致整个 API 启动失败；该 Provider 标记为不可用，并从新会话模型列表隐藏。
- 如果默认模型不可用，启动健康检查应明确告警；创建新会话时返回可理解错误，不能静默选择任意模型。
- 日志禁止记录 Authorization Header、完整请求配置或 API Key。
- 前端模型接口只返回模型公开元数据和可用状态。

## 9. 数据模型

### 9.1 ChatSession

新增：

- `AiProvider`：`nvarchar(32)`，旧会话允许为空。
- `AiModel`：`nvarchar(100)`，旧会话允许为空。

不必额外保存 `ModelLockedAt`。只要会话存在用户消息，即视为模型已锁定。

旧会话兼容：

- 数据库迁移不强行写死当前部署模型。
- 读取旧会话时，空值解析为配置中的 Legacy/Default 模型。
- 旧会话第一次再次发送时，在同一事务内写入解析后的 provider/model，然后永久锁定。

### 9.2 AgentRun

新增：

- `Mode`：固定为 `work`，便于审计与未来扩展。
- `AiProvider`：任务创建时的提供商快照。
- `AiModel`：任务创建时的模型快照。
- `Phase`：当前运行阶段。
- `PhaseMessage`：当前阶段的人类可读说明，限制长度。
- `VerificationStatus`：`not_required/pending/passed/unavailable`。

恢复任务必须使用快照中的 provider/model，不能读取会话后来可能发生的配置变化。

### 9.3 DTO

`ChatSessionDto` 和 `ChatSessionDetailDto` 新增：

- `modelKey`
- `modelDisplayName`
- `modelLocked`
- `modelAvailable`

`CreateSessionRequest` 新增 `modelKey`。为空时由服务端使用默认模型。

`AgentRunDto` 新增：

- `modelKey`
- `phase`
- `phaseMessage`
- `verificationStatus`

普通持久化会话发送请求不接受模型覆盖，以会话记录为准，防止客户端绕过固定模型约束。

## 10. API 设计

### 10.1 模型目录

`GET /api/v1/ai/models`

返回示例：

```json
{
  "items": [
    {
      "key": "minimax:MiniMax-M2.7",
      "provider": "minimax",
      "providerDisplayName": "MiniMax",
      "modelId": "MiniMax-M2.7",
      "displayName": "MiniMax M2.7",
      "supportsTools": true,
      "supportsReasoning": true,
      "supportsImages": false,
      "allowedModes": ["chat", "work"]
    }
  ],
  "defaultModelKey": "deepseek:deepseek-v4-flash"
}
```

不得返回 Base URL、API Key、内部错误或未启用模型。

### 10.2 创建会话

沿用：

`POST /api/v1/chat/sessions`

请求新增：

```json
{
  "title": "新对话",
  "modelKey": "minimax:MiniMax-M2.7"
}
```

后端校验模型存在、启用且至少允许 Chat 或 Work。

### 10.3 修改空会话模型

新增：

`PUT /api/v1/chat/sessions/{sessionId}/model`

仅允许会话没有任何用户消息且没有活动 AgentRun 时修改。否则返回 `409 MODEL_LOCKED`。

### 10.4 发送 Chat

沿用：

`POST /api/v1/chat/sessions/{sessionId}/messages/stream`

约束：

- 从会话读取固定模型。
- 不接受客户端模型覆盖。
- 不调用执行型工具。
- 不创建 AgentRun。
- 正常 `stop` 后立即 `done`。

### 10.5 发送 Work

沿用：

`POST /api/v1/chat/sessions/{sessionId}/messages/agent/runs`

约束：

- 从会话读取固定模型并写入 AgentRun 快照。
- 只有模型声明 `SupportsTools=true` 且允许 Work 才能创建。
- 通过持久化事件提供阶段、工具、核验、确认和终态数据。

## 11. SSE 事件协议

保留现有事件：

- `user_msg`
- `token`
- `thinking`
- `tool_call`
- `tool_progress`
- `tool_result`
- `confirm`
- `context`
- `done`
- `error`
- `stopped`
- `recoverable`

新增 `phase`：

```json
{
  "phase": "verifying",
  "message": "回答已生成，正在检查任务是否完整…",
  "startedAt": "2026-09-10T08:00:00Z",
  "step": 3,
  "totalSteps": 4
}
```

新增或规范化 `heartbeat`：

```json
{
  "phase": "verifying",
  "message": "正在检查任务是否完整…",
  "elapsedSeconds": 9
}
```

规则：

- `phase` 必须持久化并带序号，断线重连后可重放。
- `heartbeat` 不持久化，避免事件表膨胀。
- `done/error/stopped` 仍是唯一终止 SSE 等待的事件。
- 收到正文 token 不等于任务完成。
- `done` 必须在数据库终态和最终消息持久化后发送。

## 12. 前端设计

### 12.1 模式路由

移除 `shouldUseAgent(text)` 作为发送路径决策来源。

```text
uiMode=chat -> sendMessageStream
uiMode=work -> sendAgentMessageStream
```

关键词检测可以保留，但只能用于显示“建议切换 Work”，不能自动发送、不能改变权限。

### 12.2 Chat 界面

- 保持简洁对话布局。
- 工具执行区不出现，因为 Chat 不提供执行型工具。
- 输入区提示“提问、讨论或处理已有内容”。
- 正文结束立即恢复发送按钮。
- 对需要实际操作的请求显示一次非阻塞转换提示。

### 12.3 Work 界面

- 输入区提示“描述需要完成的任务”。
- 工具、计划、确认和交付物默认展开。
- 流式正文下方常驻任务状态卡，直到收到终态。
- 页面刷新后通过 runId 和 sequence 重放事件，恢复 phase、工具卡、正文和交付物。
- 完成后状态卡变为完成摘要：总耗时、工具成功/失败数、交付文件数量、核验状态。

### 12.4 模型选择器

- 新会话输入区或标题区显示模型选择器。
- 已锁定会话显示只读模型徽标。
- 点击其他模型进入“创建新会话”确认，而不是修改当前会话。
- 模型不支持当前模式时，该模式在发送前给出清晰说明；不得发送后才失败。

## 13. 错误处理

### 13.1 Provider 错误归一化

统一分类：

- `AUTHENTICATION_FAILED`
- `MODEL_UNAVAILABLE`
- `RATE_LIMITED`
- `PROVIDER_TIMEOUT`
- `PROVIDER_BAD_RESPONSE`
- `CONTENT_REJECTED`
- `TOOL_CALL_INVALID`

用户看到简短可操作信息；日志记录 provider、model、HTTP 状态、runId 和阶段，但不记录 Key。

### 13.2 Chat 错误

- 连接断开且没有终态时，显示重试，不伪装完成。
- 已保存用户消息时不重复提交。
- 模型错误后立即结束发送状态。

### 13.3 Work 错误

- 客户端 SSE 断开不取消任务。
- Provider、工具或核验失败必须转换为持久化事件。
- 核验失败不覆盖已经产生的有效回答；按第 6.4 节规则结束或继续。
- 服务重启将非终态任务标记为 `recoverable`，不得自动重放有副作用的工具。

## 14. 安全与权限

- Chat 的工具集合在后端固定为空或只包含明确批准的无副作用能力，不能只靠前端隐藏。
- Work 的工具权限继续由服务端 ToolRegistry、风险等级和确认流程决定。
- `skipConfirmation` 不应由普通客户端任意提交后直接生效；应由用户权限或服务端策略决定。
- 模型 Key、Provider 配置和上游错误正文不得进入 SSE。
- 日志、数据库 RequestJson 和诊断导出需要检查是否包含密码、Token 或用户在提示词中提供的凭据。
- MiniMax 和 DeepSeek 请求均使用服务端 HTTPS，设置连接、首包和总时限。
- 模型允许列表由服务端控制，防止客户端请求未授权或意外高成本模型。

## 15. 可观测性

每个请求或 AgentRun 记录以下结构化指标：

- sessionId/runId；
- mode；
- provider/model；
- 首 token 延迟；
- 正文完成时间；
- verifying 耗时；
- finalizing 耗时；
- 总耗时；
- 工具调用数量和结果状态；
- 续跑次数；
- verificationStatus；
- 最终状态。

不得记录提示词全文、回复全文、Authorization Header 或 API Key。

这组指标要能直接回答：用户看到最后一个 token 后，为什么还等待了多久。

## 16. 迁移与兼容策略

### 阶段一：提供商抽象

- 引入统一 `AI` 配置、模型注册表和 Provider 接口。
- 用 DeepSeek 适配器保持现有功能和输出不变。
- 现有 `DeepSeek` 配置保留兼容读取，避免一次部署中断服务。

### 阶段二：Chat / Work 真正分流

- 移除关键词自动路由。
- Chat 不再调用完成检查或执行型工具。
- Work 保持持久化 AgentRun。
- 增加 phase 事件和状态卡。

### 阶段三：有界核验与异步记忆

- 引入确定性检查。
- 给语义检查添加超时和最大次数。
- 将 Auto Memory 移到后台队列。

### 阶段四：MiniMax

- 增加 MiniMax Provider 和配置占位。
- 使用测试 Key 做流式文本、推理内容、工具调用和错误映射测试。
- 验证后才在模型允许列表中启用。

### 阶段五：模型锁定与前端切换

- 数据库迁移增加会话和 AgentRun 模型字段。
- 新会话选择模型，首次消息后锁定。
- 切换模型创建新会话。

## 17. 测试要求

### 17.1 后端单元测试

- 模型注册表默认选择、禁用模型和未知模型校验。
- DeepSeek 与 MiniMax SSE 分片解析。
- MiniMax `reasoning_details`、`<think>` 和工具调用归一化。
- 完整 assistant 工具调用响应进入下一轮历史。
- Chat 正常 stop 后不调用完成检查。
- Work phase 状态转换。
- 完成检查 15 秒超时和最多两次续跑。
- 记忆队列失败不改变已完成状态。
- 会话存在消息后禁止修改模型。
- AgentRun 使用创建时模型快照恢复。

### 17.2 API 集成测试

- 模型目录不泄露 Key 和 Base URL。
- 创建不同模型的会话。
- 空会话可改模型，已有消息返回 `MODEL_LOCKED`。
- Chat 请求不能调用执行型工具。
- Work 创建、订阅、断线重连、停止、确认和恢复。
- phase 可重放，heartbeat 不落库。
- `done` 一定晚于最终消息和终态持久化。

### 17.3 前端测试

- Chat / Work 按钮直接决定 API 路由。
- 关键词仅显示建议，不自动切换或发送。
- 新会话模型选择和已开始会话模型锁定。
- 切换模型创建新会话。
- verifying、continuing、finalizing 状态卡及耗时更新。
- 正文已出现但未 done 时，仍清楚显示当前阶段。
- 收到 done/error/stopped 后一定清除运行状态。
- 刷新后恢复 Work 任务状态与事件。

### 17.4 人工验收场景

1. Chat 提问普通知识：正文结束后立即结束，不出现“完成检查中”。
2. Chat 输入“登录网站生成报告”：不自动执行，显示切换 Work 建议。
3. Work 执行网站调研：页面显示规划、执行、核验、整理交付和完成阶段。
4. Work 正文输出后核验耗时 10 秒：用户持续看到核验状态和耗时。
5. 核验判定继续：显示具体 nextStep，并继续执行，而不是静默等待。
6. 核验超时：任务不无限挂起，按确定性检查结果完成或失败。
7. Work 中刷新浏览器：任务继续，回来后恢复完整事件。
8. DeepSeek 和 MiniMax 分别完成一次 Chat、一次带工具的 Work。
9. 会话已有消息后切换模型：创建新会话，原会话模型不变。
10. 检查源码、发布目录和日志，确认不存在真实 MiniMax Key。

## 18. 验收标准

- Chat 与 Work 的后端路径由用户模式选择直接决定。
- Chat 正文结束到 `done` 的应用内额外耗时目标小于 500ms，不包含数据库或网络异常。
- Work 任意连续 5 秒无 token/tool 事件时，界面仍显示准确阶段和递增耗时。
- Work 完成检查单次不超过配置时限，续跑核验次数不超过配置上限。
- 自动记忆不阻塞 `done`。
- 每个持久化会话有唯一固定的 provider/model；已有消息后不能原地修改。
- AgentRun 持久化 provider/model 快照，恢复时不漂移。
- DeepSeek 与 MiniMax 均通过流式、推理内容、工具调用和错误处理测试。
- API、SSE、日志和前端均不暴露 API Key。
- 未配置 MiniMax Key 时系统仍可正常启动，MiniMax 不出现在可创建会话的模型列表中。

## 19. 建议改动范围

后端重点文件或目录：

- `MiraiNote.Core/Services/AppOptions.cs`
- `MiraiNote.Core/Services/ChatService.cs`
- `MiraiNote.Core/Services/AgentRunSupervisor.cs`
- `MiraiNote.Core/Services/AgentRuns/`
- 新增 `MiraiNote.Core/Services/AI/Providers/`
- `MiraiNote.Core/DependencyInjection.cs`
- `MiraiNote.API/Controllers/ChatController.cs`
- 新增 `MiraiNote.API/Controllers/AiModelsController.cs`
- `MiraiNote.Data/Entities/ChatSession.cs`
- `MiraiNote.Data/Entities/AgentRun.cs`
- `MiraiNote.Shared/Dtos/Chat/ChatDtos.cs`
- EF Core Migration 与 ModelSnapshot

前端重点文件：

- `frontend/src/views/chat/ChatView.vue`
- `frontend/src/stores/chat.ts`
- `frontend/src/api/chat.ts`
- `frontend/src/api/agent.ts`
- `frontend/src/api/sse.ts`
- `frontend/src/types/chat.ts`

配置：

- `backend/MiraiNote.API/appsettings.json` 只增加空 Key 模板。
- 实际 MiniMax Key 在部署时通过安全配置注入。

## 20. 开发交接注意事项

- 当前工作区存在较多未提交改动，开发前必须确认继续使用的分支和这些改动的归属，不能覆盖或清理。
- 先把 DeepSeek 迁移到 Provider 抽象并跑通回归，再接 MiniMax；不要一次重写全部 ChatService。
- MiniMax 模型名称和能力以部署时官方文档及账号实际可用列表为准，配置允许列表是最终控制面。
- 用户提供 Key 后，只写入服务器安全配置，不回显、不提交、不写测试快照。
- 发布前分别验证源码构建、数据库迁移、发布目录文件和实际运行服务来源，不能只凭复制成功判断线上已生效。
