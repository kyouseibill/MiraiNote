# Work 模式无响应诊断（只读）

范围：已保存会话的持久化 Agent Run。入口是 `POST /api/v1/chat/sessions/{id}/messages/agent/runs`，观察流是 `GET /api/v1/chat/agent-runs/{runId}/events`。本次没有改代码。

对话模式不走这条路径。它直接 `POST /api/v1/chat/sessions/{id}/messages/stream`，在**同一个 HTTP 请求**里生成并立刻写 SSE。所以「对话正常、工作没反应」和模型密钥本身通常无关，问题出在 Work 独有的建任务、内存队列和事件订阅上。

临时聊天的工作模式也不走这条路径。`ChatView.vue` 的 `send()` 在 `isWorkMode` 时调用 `sendAgentMessageStream`，但 store 对临时会话改调 `agentApi.sendTemporaryAgentMessageStream`（`POST /chat/temporary/{id}/messages/agent/stream`），生命周期仍绑在该请求上。下面的结论只覆盖**已保存会话**。

---

## 1. 前端调用链

### 1.1 从按钮到 store

`frontend/src/views/chat/ChatView.vue` 的 `send()`：有正文或附件、且不在发送中时，工作模式调用 `store.sendAgentMessageStream`，对话模式调用 `store.sendMessageStream`。模式只存在前端 `uiMode === 'work'`，请求体里没有 mode 字段。

### 1.2 `stores/chat.ts` `sendAgentMessageStream`（约 712–975 行）

1. 没有 `currentSession` 时直接返回 `'failed'`，不发请求。
2. 打断上一次 `AbortController`，清空 `activeAgentRunId`，`sending = true`。
3. **先在本地**插入一条用户消息，并放一个空的 assistant `streamMessage`。界面立刻出现「正在思考，请稍候…」（`ChatView.vue` 约 1692–1695 行：`streaming && !answer` 时用 `currentToolCall`，否则用这句）。此时服务器可能还没有任何事件。
4. 组装 payload：`enablePlanner: false`，`enableReflector: false`，`skipConfirmation: autoMode`。
5. 已保存会话调用 `agentApi.sendAgentMessageStream`。`onRunCreated` 只把 `persistentRun.runId` 记到 `activeAgentRunId`。
6. 事件处理：
   - `user_msg`：把临时用户消息 id 换成数据库 id。
   - `token`：追加到 `streamMessage.content`。
   - `heartbeat` / `context`：只改 `currentToolCall` 文案，不产生正文。
   - `done` / `error` / `stopped`：结束发送并落消息。
   - `recoverable`：记下 `recoverableAgentRunId`，文案为「任务因服务重启中断…」。
7. `catch`：只要不是用户主动停止，toast「Agent 连接意外中断，请重试」，并清空流式占位。axios 拦截器对 POST 失败还会再 toast 一次 `response.data.message`。
8. `finally`：无论成功失败都把 `sending` 置回 false，并清掉 `activeAgentRunId`。`recoverable` 横幅因此经常一闪而过：重连循环还在 `agent.ts` 里，store 已经把 run id 清掉了。

对话模式的 `sendMessageStream`（约 465 行起）把 `fetch` 的 SSE 直接交给同一个事件开关。它不创建 run，也不轮询。

### 1.3 `frontend/src/api/agent.ts` `sendAgentMessageStream`（81–132 行）

1. `POST /chat/sessions/${sessionId}/messages/agent/runs`（axios `baseURL` 已含 `/api/v1`，超时 **15 秒**）。`unwrap` 要求 `success === true`，返回 `{ runId, sessionId, status, lastSequence, failureMessage, recoverableAt }`。
2. `onRunCreated(run)`。
3. 循环 `GET ${API_BASE_URL}/chat/agent-runs/${runId}/events?afterSequence=${afterSequence}`，用 `fetch`，**没有超时**。
4. `consumeSseResponseUntilTerminal`（`frontend/src/api/sse.ts`）把带 `id:` 的帧记成 sequence，并只把 `done` / `error` / `stopped` 当作结束。`recoverable` 不是结束事件。
5. 流在结束事件之前断开：再 `GET /chat/agent-runs/{runId}`。状态是 `recoverable` 时每 800ms 轮询，直到状态变掉再重新订阅。其他错误则 toast 式心跳「任务仍在执行，正在重连…」，退避最多 8 秒后重连。
6. 若 GET events **响应头一直不返回、body 也没有字节**，`fetch` 停在 pending，这个重连分支不会跑。界面就停在第 1.2 步的「正在思考，请稍候…」。

停止、确认、恢复分别是：

- `POST /chat/agent-runs/{runId}/stop`
- `POST /chat/agent-runs/{runId}/confirm` `{ confirmed }`
- `POST /chat/agent-runs/{runId}/resume`

`confirmToolCall` 在有 `activeAgentRunId` 时走 confirm run；失败被空 `catch` 吞掉。

---

## 2. 后端 `CreateAgentRun` 与 `ExecuteAsync`

路由前缀：`[Route("api/v1/chat")]`（`ChatController`）。

### 2.1 创建：`POST .../messages/agent/runs`

`ChatController.CreateAgentRun` 只调用 `_agentRuns.CreateAsync` 并立刻 `200` + `AgentRunDto`。它不等待模型。

`AgentRunService.CreateAsync`（`AgentRunService.cs` 45–78 行）顺序：

1. 会话必须属于当前用户，否则 `BusinessException` 404「对话不存在」。
2. 无正文且无图片附件：400「消息内容不能为空」。
3. `IChatModelRegistry.ResolveForExistingSession(session.AiProvider, session.AiModel)`。会话上两项都空时，落到配置里的默认模型（没有 `AI:DefaultModelKey` 时是 `deepseek:{DeepSeek:Model}`，默认 `deepseek-v4-flash`）。
4. **`SupportsWork` 与 `SupportsTools` 必须同时为 true**，否则抛 `ChatModelUnavailableException`：「所选模型不支持工作模式，请创建新对话并选择其他模型。」
5. `ChatImagePolicy.Validate`。DeepSeek 只有 flash 系列允许图片；`deepseek-v4-pro` 带图会在入队前抛 `BusinessException`「当前模型不支持图片…」。纯文本不受这一条影响。
6. 若解析结果和会话快照不一致，把 provider/model 写回会话。
7. 插入 `AgentRun`：`Status = queued`，`RequestJson` 为整个请求，`AiProvider` / `AiModel` 为当时快照。`SaveChanges`。
8. `_dispatcher.Enqueue(run.Id)`，返回快照。入队只是进程内 `Channel<Guid>`，**不写数据库队列**。

对话发送走 `SendMessageStreamAsync` → `ResolveSessionModelAsync(..., requiresWork: false)`，只要求 `SupportsChat`。不插入 `AgentRun`。

### 2.2 谁真正执行

`DependencyInjection.AddCoreLayer`：

- `AgentRunDispatcher`：**Singleton**，内存队列、取消令牌、确认 `TaskCompletionSource`。
- `IAgentRunService`：**Scoped**，每次执行用新的 `DbContext`。
- `AddHostedService<AgentRunBackgroundService>()`。

`AgentRunBackgroundService.ExecuteAsync`：

1. 启动时 `RecoverInterruptedRunsAsync`：把所有 `queued` / `running` / `awaiting_confirmation` 改成 `recoverable`，写一条 context 事件，**不会自动重跑**。
2. 然后 `while` 里 `DequeueAsync`，**一次只 await 一个** `ExecuteAsync`。上一个 Work 没结束，后面的 run 一直停在 `queued`。

设计说明写明这是单进程内存队列，不是独立 worker（`docs/superpowers/specs/2026-09-09-durable-agent-runs-design.md`）。多副本、IIS 多工作进程时，POST 落在哪个进程，就只有那个进程的 channel 里有这个 id。

### 2.3 `ExecuteAsync`（148–218 行）

1. 按 id 重载。`run == null` 或 **`Status != queued` 时直接 return**。不写 `error` 事件，也不改状态。
2. `RequestJson` 反序列化失败才 `MarkFailed`「任务参数无法恢复」。
3. `dispatcher.Begin`。同一 id 已有取消源时抛「任务已在执行」。这行在 `try` **外面**。
4. 内存中改为 `running`，追加 context「任务开始执行」，`SaveChanges`。这两步也在 `try` 外面。这里失败会冒泡到后台循环的 `catch`，只打日志「Agent 后台执行器循环失败」，**不会把 run 标成 failed，也不会重新入队**。数据库里可以永远是 `queued` 且零事件。
5. `try` 内调用 `IChatService.SendMessageAgentStreamAsync`。回调把每种 SSE 事件先插入 `AgentRunEvent`（`Sequence = MAX+1`）再 `SaveChanges`。`done` / `error` / `stopped` 才把 run 收成终态。
6. 模型方法返回后若仍非终态：`MarkFailed`「任务未返回完成状态」。
7. 宿主正在关闭：保持非终态，留给下次启动收成 `recoverable`。用户取消：`stopped`。其他异常：日志「Agent run {RunId} 执行失败」，再 `MarkFailed`「任务执行失败，请稍后重试」。

`SendMessageAgentStreamAsync` 在调模型之前还会经回调写入 heartbeat：「正在规划执行步骤…」「正在执行任务并收集结果…」。前端把 `enablePlanner` / `enableReflector` 设成 false，所以不会多一次规划/反思模型调用，但这两条 heartbeat 仍会入库。接着才是带**全量工具**的 `CallDeepSeekStreamWithToolsAgentAsync`。完成检查 `AgentRunSupervisor.ReviewAsync` 只对部分「像在干活」的 prompt 触发，超时默认 8 秒（配置 5–60 秒），失败会变成 interrupted 文案，不应无限卡住。

DeepSeek 适配器 `UsesReasoningSplit = false`，请求体仍是 `tools` + `tool_choice: auto`。命名 HttpClient `"ChatModel"` / `"DeepSeek"` 的 `Timeout` 都是 `InfiniteTimeSpan`。读流空闲超时是 5 分钟（`ParseDeepSeekStreamAsync`）。对话模式用同一套超时，但 token 直接写到当前响应，不经过这张事件表。

### 2.4 订阅：`GET .../events?afterSequence=`

`SubscribeAgentRunEvents`：

- 设置 SSE 头，并 `X-Accel-Buffering: no`。
- 从 SQL 拉 `Sequence > cursor` 的事件，写成 `id` / `event` / `data` 帧后 flush。
- 终态（`completed` / `failed` / `stopped`）就关闭。`recoverable` 时写一帧 `event: recoverable` 后关闭。
- 否则 `Task.Delay(750ms)` 再查。`dispatcher.Signal` **没有被这个接口使用**。

在第一行事件写入之前，这个 action **一个字节都不写**。对比对话模式 `RunSseStreamAsync`：先写 `: connected`，之后每 10 秒写 heartbeat。Work 的观察连接在 run 仍是 `queued` 时是一条空闲长连接。反代 `proxy_read_timeout`（常见 60 秒）会把它掐断；直连 Kestrel 则可以一直挂着。

### 2.5 失败模式对照

| 条件 | 用户看到什么 | 数据库 |
| --- | --- | --- |
| 没有 `AgentRun` 表，或没有 `AiProvider`/`AiModel` 列 | POST 500。生产环境正文是「服务器内部错误」，不是 SQL 原文。启动时 `RecoverInterruptedRunsAsync` 同样会查这张表；.NET 默认 `BackgroundService` 未捕获异常会停宿主，严重时对话也会跟着挂。 | `__EFMigrationsHistory` 缺行；或 `Invalid object name 'AgentRun'` / `Invalid column name 'AiProvider'` |
| `SupportsWork` 或 `SupportsTools` 为 false，或会话快照对不上目录 | POST 500。异常类型是 `ChatModelUnavailableException`（不是 `BusinessException`），生产环境同样只返回「服务器内部错误」，友好中文被吃掉。对话只检查 `SupportsChat`，所以同一模型对话仍可用。 | 没有新 run |
| 图片 + 非 flash DeepSeek | POST 400，消息含「不支持图片」 | 没有新 run |
| POST 已 200，后台没捞到队列（进程内 channel、多实例、启动恢复把状态改成了非 queued、`Begin`/首次 `SaveChanges` 在 try 外抛） | GET events 无字节。界面停在「正在思考，请稍候…」。15 秒内 POST 本身是成功的。 | `Status = queued`（或 `recoverable`），`AgentRunEvent` 为 0 行 |
| 上一个 Work 还卡在模型/工具里 | 新消息同样零事件。对话不受这个单线程循环影响。 | 新行 `queued`；旧行 `running` 且 `LastActivityAt` 很久不更新 |
| 模型或工具最终抛错 | 事件流里有 `error`，toast 该文案 | `Status = failed`，`FailureMessage` 有值 |
| 进程重启 | 旧 run 变 `recoverable`。需要再 `POST .../resume` 才会重新入队。 | `RecoverableAt` 非空 |

程序**不会**在启动时 `Database.Migrate()`。`DatabaseSeeder.SeedAsync` 只在用户表为空时建管理员。`AgentRun` 依赖两次手工迁移：

- `20260909155300_AddDurableAgentRuns`：建 `AgentRun`、`AgentRunEvent`
- `20260910120000_AddChatModelSelection`：给 `AgentRun` 和 `ChatSession` 加 `AiProvider`、`AiModel`

当前 EF 模型读 `ChatSession` 也会选 `AiProvider`。若第二次迁移没上，对话列表同样会 SQL 失败。因此：**对话完全正常时，表多半已经在；Work 仍无正文，优先看 run 是否停在 `queued` 且零事件。** 只有有人手改过 `ChatSession` 列、却没建 `AgentRun` 时，才会「对话正常 + POST 500 缺表」。

`SupportsWork` / `SupportsTools` 默认都是 true（`AiModelOptions`）。旧配置只有 `DeepSeek:ApiKey` + `DeepSeek:Model`、没有 `AI:Providers` 时，注册表会合成一条 DeepSeek，两个旗标都是 true。要复现「对话可以、工作被拒绝」，需要目录里该模型 `SupportsWork: false` 或 `SupportsTools: false`，或会话里的 `AiProvider`/`AiModel` 已经对不上当前目录（两条路径都会 `ResolveForExistingSession`，对不上时对话也会 500。真正只伤 Work 的是两个旗标）。

---

## 3. 最可能的三个生产原因

### 1. 任务已创建，但内存里的后台执行器没有写出任何事件

这是「对话有字、工作一直转圈」的最贴合路径。

证据：

- 创建接口在 `Enqueue` 后马上返回。执行只发生在 `AgentRunBackgroundService` 从 Singleton `Channel` 读到 id 之后。
- 该循环是单消费者。设计文档明确不支持跨进程队列。第二个 API 进程、或 POST 打到正在回收的旧进程，channel 里的 id 会丢。
- `ExecuteAsync` 在状态不是 `queued` 时静默 return。`Begin` 和第一次 `SaveChanges` 在 `try` 之外；失败只被后台循环吃掉，run 留在 `queued` 且不会再次入队。
- `GET events` 在第一行 `AgentRunEvent` 出现之前不写 body，也没有对话那条 10 秒 heartbeat。前端 `fetch` 不设超时，占位气泡又是本地先画的，所以看起来像没反应。
- 对话走 `RunSseStreamAsync`，首包就是 `: connected`，不经过这张表和这个 channel。

现场应看到：POST **200**，`runId` 有值；`AgentRun.Status = queued`；该 `RunId` 的 `AgentRunEvent` 行数为 0；`StartedAt` 为空。日志里没有「任务开始执行」对应的后续模型日志，或有「Agent 后台执行器循环失败」。

### 2. 生产库没有 `AgentRun` 迁移

证据：

- 全仓库没有 `Database.Migrate()` / `MigrateAsync()`。发布新 DLL 不会建表。
- `CreateAsync` 的第一次 `SaveChanges` 就插入 `AgentRun`（含 `AiProvider`、`AiModel`）。缺表或缺列时异常不是 `BusinessException`，`GlobalExceptionMiddleware` 在生产只返回 500「服务器内部错误」。
- 前端 axios 会 toast 这句，`sendAgentMessageStream` 的 `catch` 再 toast「Agent 连接意外中断」。占位气泡被清掉。若用户没注意到 toast，体感也是「点了没反应」。
- 对话 SQL 不碰 `AgentRun`。只要 `ChatSession` 的列还在，对话可以继续。启动恢复查询失败时，还可能把整个宿主停掉（那时对话也会挂，用来和原因 1 区分）。

现场应看到：POST **500**；SQL 找不到表，或 `__EFMigrationsHistory` 没有 `20260909155300_AddDurableAgentRuns` / `20260910120000_AddChatModelSelection`；日志 `未处理异常` 且异常含 `Invalid object name 'AgentRun'` 或 `Invalid column name 'AiProvider'`。

### 3. 模型被判成不能做 Work，或 DeepSeek 的 Work 调用在出第一个事件之前就失败/空转

证据：

- 闸门在 `CreateAsync`：`!model.SupportsWork || !model.SupportsTools`。对话只看 `SupportsChat`。默认旗标是 true，旧 DeepSeek 单模型回退也是 true；生产 `AI:Providers[].Models[]` 若把 Work/Tools 关掉，就会只坏工作模式。
- 抛出的是 `ChatModelUnavailableException : InvalidOperationException`。生产 500 文案固定为「服务器内部错误」，日志里才有「所选模型不支持工作模式…」或「当前会话模型暂不可用…」。
- 通过闸门之后，Work 使用全量工具（含 shell、写文件），对话只用只读工具子集。DeepSeek 客户端超时是无限的，空闲读超时 5 分钟。事件订阅又没有独立 keepalive。若后台在写出「任务开始执行」之前就抛在 `try` 外，表现和原因 1 一样；若已经写出 heartbeat 再卡在首 token，界面会停在「正在执行任务并收集结果…」而不是空白。每个 token 还会 `MAX(Sequence)` + `INSERT` + `SaveChanges`（回调在读下一行模型输出之前 await），推理模型的思考 token 会被数据库放大，对话路径没有这个每 token 写库。

现场应看到：要么 POST 500 且日志是模型不可用（表结构正常、没有新 run）；要么 POST 200 后很快有 `failed` 和 `FailureMessage`（例如 `AI 服务错误 4xx`）。一直 `queued` 且零事件则仍是原因 1，不是 DeepSeek 慢。

---

## 4. Rajendra 在服务器上的最小核对

按这个顺序。先分清是 500 还是 200 后挂起，再看表。

### 4.1 SQL

在 MiraiNote 库执行：

```sql
SELECT MigrationId
FROM dbo.__EFMigrationsHistory
WHERE MigrationId IN (
  '20260909155300_AddDurableAgentRuns',
  '20260910120000_AddChatModelSelection'
);

SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME IN ('AgentRun', 'AgentRunEvent');

SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'AgentRun'
  AND COLUMN_NAME IN ('AiProvider', 'AiModel', 'Status', 'RequestJson');

SELECT TOP 20
  Id, SessionId, Status, AiProvider, AiModel,
  FailureMessage, CreatedAt, StartedAt, LastActivityAt, RecoverableAt
FROM dbo.AgentRun
ORDER BY CreatedAt DESC;

SELECT e.RunId, COUNT(*) AS EventCount, MIN(e.Type) AS FirstType, MAX(e.CreatedAt) AS LastEventAt
FROM dbo.AgentRunEvent e
WHERE e.RunId IN (SELECT TOP 5 Id FROM dbo.AgentRun ORDER BY CreatedAt DESC)
GROUP BY e.RunId;
```

判读：

- 两行迁移都没有，或没有 `AgentRun`：原因 2。先备份，再用迁移账户 `dotnet ef database update`（运行时用的 `DefaultConnection` 往往没有 DDL 权限）。不要只重启站点。
- 表在，最新行 `Status = queued` 且 `EventCount` 为空或 0，超过十几秒 `StartedAt` 仍为 NULL：原因 1。重启只能把这些行收成 `recoverable`，不会自动跑。要确认站点是**单进程**，并看托管服务有没有在启动时异常退出。
- `Status = failed`：读 `FailureMessage` 和同 run 的 `Type = error` 事件。
- `Status = recoverable`：前端需要再调 resume；只刷新页面不会继续。
- 根本没有新行，而用户刚点过工作模式：POST 在插入前就失败了（原因 2 或 3）。

### 4.2 日志

日志在 API 内容目录 `logs/log-yyyyMMdd.txt`（Serilog，按天滚动）。`web.config` 里 `stdoutLogEnabled` 是 false，IIS stdout 默认没有。

在一次复现前后：

```text
rg -n "Agent run|Agent 后台执行器循环失败|未处理异常 /api/v1/chat/sessions/.*/messages/agent/runs|Invalid object name 'AgentRun'|Invalid column name 'AiProvider'|所选模型不支持工作模式|当前会话模型暂不可用|不支持图片|任务已在执行|AI 服务错误" logs
```

| 日志 | 含义 |
| --- | --- |
| `Invalid object name 'AgentRun'` 或 `Invalid column name 'AiProvider'` | 迁移没上全 |
| `所选模型不支持工作模式` / `当前会话模型暂不可用` | 原因 3，用户看到的却是「服务器内部错误」 |
| `Agent 后台执行器循环失败` 且之后该 id 仍是 queued | 执行器把任务丢了（原因 1） |
| `Agent run {guid} 执行失败` | 已进入 `try`，应有 `failed` 行和 `error` 事件 |
| 只有 POST 200 访问日志，没有上述任何行 | 队列里的 id 没有被本进程捞到 |

同时看进程模型：IIS 应用程序池「最大工作进程数」必须是 1。前面若有 nginx/Caddy，`/api/v1/chat/agent-runs/` 需要关闭缓冲（响应已带 `X-Accel-Buffering: no`，还要 `proxy_buffering off` 和足够大的 `proxy_read_timeout`）。

### 4.3 curl

用浏览器里当前用户的 access token。把 `HOST`、`SESSION`、`TOKEN` 换成现场值。会话必须是已保存 id，不要用临时聊天。

```bash
# A. 建任务。期望 HTTP 200，body.data.status 为 queued，并拿到 runId。
curl -sS -D - \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content":"只回复一个字：好","enablePlanner":false,"enableReflector":false,"skipConfirmation":true}' \
  "https://HOST/api/v1/chat/sessions/SESSION/messages/agent/runs"

# B. 立刻看状态。每 2 秒一次，最多看 20 秒。
curl -sS -H "Authorization: Bearer $TOKEN" \
  "https://HOST/api/v1/chat/agent-runs/RUNID"

# C. 订阅事件，最多等 20 秒。健康时几秒内应出现
#    event: context / heartbeat，随后有 token，最后是 done 或 error。
curl -N --max-time 20 \
  -H "Authorization: Bearer $TOKEN" \
  -H "Accept: text/event-stream" \
  "https://HOST/api/v1/chat/agent-runs/RUNID/events?afterSequence=0"
```

对照：

- A 是 500，B/C 不用做。看响应 `message` 和 4.2 的异常。生产环境 message 为「服务器内部错误」时，以日志为准，区分缺表和 `ChatModelUnavailableException`。
- A 是 200，B 一直 `queued` 且 `lastSequence = 0`，C 在 20 秒内零字节：原因 1。查进程数和「Agent 后台执行器循环失败」。
- A 是 200，C 很快有 `event: error`：模型或工具失败，读 `data.message`。DeepSeek 常见 `AI 服务错误 4xx`。
- A 是 200，C 有 heartbeat「正在执行任务并收集结果…」但长期没有 `token`：执行器是活的，卡在 DeepSeek 首包（全量工具 + 无限 HttpClient 超时，空闲上限 5 分钟）。这和「完全没反应」不是同一档。
- 同一 token 再打一次对话流，确认对照组成立：

```bash
curl -N --max-time 30 \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"content":"只回复一个字：好"}' \
  "https://HOST/api/v1/chat/sessions/SESSION/messages/stream"
```

这条应马上看到 `: connected`，然后是 `token` / `done`。它出现而 C 为空，就可以认定差在持久化 run 的执行器，而不是 DeepSeek 密钥或登录态。
