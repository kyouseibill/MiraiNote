# Chat / Work 多模型 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让用户明确选择快速 Chat 或持续 Work，并让每个会话锁定一个可配置的 DeepSeek/MiniMax 模型。

**Architecture:** 保留现有 `ChatService` 与持久化 `AgentRun` 的职责边界：Chat 走无执行工具、无完成检查的流式路径；Work 继续通过 AgentRun 在后台执行，并持久化可见阶段。新增模型注册表和 Provider 抽象，所有会话与 Run 均保存经过服务端校验的模型快照。MiniMax 以服务端 OpenAI-compatible 适配器接入，密钥只从部署配置读取。

**Tech Stack:** .NET 8 / EF Core / xUnit / Vue 3 / Pinia / TypeScript / Vite / SSE。

**Spec:** `docs/superpowers/specs/2026-09-10-chat-work-multimodel-design.md`

## Global Constraints

- Chat/Work 必须由用户本次点击的模式决定；关键词只能提示切换，不能自动切换。
- Chat 不注册浏览器、Shell、文件写入或完成检查；模型正常停止后立刻持久化并发 `done`。
- Work 继续使用持久化 AgentRun；完成检查必须有阶段提示、次数和时限。
- 会话的模型由首条消息锁定；模型变更创建新会话，不原地覆盖历史会话。
- API Key、Base URL、Authorization Header 和上游原始错误正文不得返回前端、写库或写日志。
- MiniMax 未配置 Key 时 API 仍可启动，且不出现在可选模型目录。
- 维持 `DESIGN.md` 的纸张、蓝灰墨水、克制密度与现有 `AppDialog` / `useToast` 行为；不进行无关界面重做。

---

### Task 1: 建立模型目录、会话模型锁定和迁移

**Files:**
- Create: `backend/MiraiNote.Core/Services/ChatModels/ChatModelContracts.cs`
- Create: `backend/MiraiNote.Core/Services/ChatModels/ChatModelRegistry.cs`
- Modify: `backend/MiraiNote.Data/Entities/ChatSession.cs`
- Modify: `backend/MiraiNote.Data/Entities/AgentRun.cs`
- Modify: `backend/MiraiNote.Data/Context/MiraiNoteDbContext.cs`
- Create: `backend/MiraiNote.Data/Migrations/<timestamp>_AddChatModelSelection.cs`
- Modify: `backend/MiraiNote.Shared/Dtos/Chat/ChatDtos.cs`
- Modify: `backend/MiraiNote.Core/Services/ChatService.cs`
- Modify: `backend/MiraiNote.Core/DependencyInjection.cs`
- Modify: `backend/MiraiNote.API/appsettings.json`
- Test: `backend/MiraiNote.Tests/ChatModelRegistryTests.cs`
- Test: `backend/MiraiNote.Tests/ChatSessionModelTests.cs`

**Interfaces:**
- Produces `ChatModelDescriptor(string Key, string Provider, string ModelId, string DisplayName, bool SupportsChat, bool SupportsWork, bool SupportsTools, bool IsAvailable)`.
- Produces `IChatModelRegistry.ResolveForNewSession(string? key)` and `ResolveForExistingSession(string? provider, string? model)`.
- Adds `ChatSession.AiProvider`, `ChatSession.AiModel` and `AgentRun.AiProvider`, `AgentRun.AiModel`.
- Adds `CreateSessionRequest.ModelKey`, `ChatSessionDto.ModelKey` and public model metadata DTOs; no secret fields.

- [ ] **Step 1: Write the failing registry tests**

```csharp
[Fact]
public void ResolveForNewSession_UsesConfiguredDefaultEnabledModel()
{
    var registry = TestRegistry(defaultKey: "deepseek:deepseek-test");

    var model = registry.ResolveForNewSession(null);

    Assert.Equal("deepseek:deepseek-test", model.Key);
}

[Fact]
public void Catalog_HidesEnabledProviderWithoutApiKey()
{
    var registry = TestRegistry(miniMaxApiKey: "");

    Assert.DoesNotContain(registry.GetPublicCatalog(), x => x.Provider == "minimax");
}
```

- [ ] **Step 2: Run the tests and verify RED**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~ChatModelRegistryTests`

Expected: FAIL because `IChatModelRegistry` and `ChatModelDescriptor` do not exist.

- [ ] **Step 3: Implement the minimal model options, registry and DI wiring**

```csharp
public interface IChatModelRegistry
{
    ChatModelDescriptor ResolveForNewSession(string? key);
    ChatModelDescriptor ResolveForExistingSession(string? provider, string? model);
    IReadOnlyList<ChatModelDescriptor> GetPublicCatalog();
}
```

Read only the `AI` allow-list from options. Treat a provider without a configured secret as unavailable. Keep `DeepSeek` option fallback while deployments migrate.

- [ ] **Step 4: Run the registry tests and verify GREEN**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~ChatModelRegistryTests`

Expected: PASS.

- [ ] **Step 5: Write failing session-lock tests**

```csharp
[Fact]
public async Task ChangeModel_RejectsSessionThatAlreadyHasMessages()
{
    var session = await CreateSessionWithOneUserMessageAsync();

    var result = await service.ChangeEmptySessionModelAsync(1, session.Id, "minimax:MiniMax-M2.7");

    Assert.Equal("MODEL_LOCKED", result.ErrorCode);
}
```

- [ ] **Step 6: Run the session-lock tests and verify RED**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~ChatSessionModelTests`

Expected: FAIL because session model fields and model-change service contract do not exist.

- [ ] **Step 7: Add entity fields, EF mapping/migration and session service behavior**

Set provider/model when creating a session. Permit changing only an empty owned session. Persist the model snapshot before the first user message and use the selected default for legacy null fields without bulk-rewriting old rows.

- [ ] **Step 8: Run focused tests and create the migration**

Run: `dotnet test backend/MiraiNote.Tests --filter "FullyQualifiedName~ChatModelRegistryTests|FullyQualifiedName~ChatSessionModelTests"`

Expected: PASS.

- [ ] **Step 9: Commit Task 1**

```bash
git add backend/MiraiNote.Core/Services/ChatModels backend/MiraiNote.Data backend/MiraiNote.Shared/Dtos/Chat/ChatDtos.cs backend/MiraiNote.Core/Services/ChatService.cs backend/MiraiNote.Core/DependencyInjection.cs backend/MiraiNote.API/appsettings.json backend/MiraiNote.Tests/ChatModel*
git commit -m "feat(chat): lock model selection per session"
```

### Task 2: 分离 Chat 与 Work 的后端执行路径

**Files:**
- Modify: `backend/MiraiNote.Core/Services/ChatService.cs`
- Modify: `backend/MiraiNote.Core/Services/AgentRuns/AgentRunService.cs`
- Modify: `backend/MiraiNote.Core/Services/AgentRunSupervisor.cs`
- Modify: `backend/MiraiNote.API/Controllers/ChatController.cs`
- Test: `backend/MiraiNote.Tests/ChatModeRoutingTests.cs`
- Test: `backend/MiraiNote.Tests/AgentRunSupervisorTests.cs`

**Interfaces:**
- Produces `SendChatMessageStreamAsync(...)` for no-tool fast Chat.
- Work creation records `AgentRun.AiProvider/AiModel` from its session and emits persisted `phase` events.
- `AgentRunDecision` carries `NextStep` when it requests continuation.

- [ ] **Step 1: Write the failing Chat fast-path test**

```csharp
[Fact]
public async Task ChatStream_OnNormalStop_DoesNotInvokeCompletionVerifier()
{
    var service = CreateServiceWithCompletionVerifierThrowing();

    await service.SendMessageStreamAsync(1, 7, new SendMessageRequest { Content = "解释这个概念" }, Capture, default);

    Assert.Contains(events, x => x.Type == "done");
}
```

- [ ] **Step 2: Run it and verify RED**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~ChatModeRoutingTests`

Expected: FAIL because the current normal stream path calls `ReviewAsync`.

- [ ] **Step 3: Implement the Chat fast path**

Build Chat messages with no execution tools, stream provider content, save the assistant message, then emit `done`. Do not invoke `AgentRunSupervisor.ReviewAsync`, memory extraction, or task continuation on this path.

- [ ] **Step 4: Run the Chat routing test and verify GREEN**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~ChatModeRoutingTests`

Expected: PASS.

- [ ] **Step 5: Write the failing Work phase test**

```csharp
[Fact]
public async Task WorkRun_WhenCheckingCompletion_PersistsPhaseWithNextStep()
{
    var events = await ExecuteWorkWithIncompleteReviewAsync();

    Assert.Contains(events, x => x.Type == "phase" && x.DataJson.Contains("nextStep"));
}
```

- [ ] **Step 6: Run it and verify RED**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~ChatModeRoutingTests`

Expected: FAIL because phase events are not persisted for completion review.

- [ ] **Step 7: Implement bounded Work verification phases**

Emit `planning`, `executing`, `verifying`, `finalizing`, and terminal phase events through the AgentRun ledger. Apply the configured review timeout and maximum continuation count. A continuation must contain a non-empty next-step message; otherwise end with an explicit, user-visible review result.

- [ ] **Step 8: Run mode-routing and supervisor tests**

Run: `dotnet test backend/MiraiNote.Tests --filter "FullyQualifiedName~ChatModeRoutingTests|FullyQualifiedName~AgentRunSupervisorTests"`

Expected: PASS.

- [ ] **Step 9: Commit Task 2**

```bash
git add backend/MiraiNote.Core/Services/ChatService.cs backend/MiraiNote.Core/Services/AgentRuns backend/MiraiNote.Core/Services/AgentRunSupervisor.cs backend/MiraiNote.API/Controllers/ChatController.cs backend/MiraiNote.Tests/ChatModeRoutingTests.cs backend/MiraiNote.Tests/AgentRunSupervisorTests.cs
git commit -m "feat(chat): route chat and work independently"
```

### Task 3: 提供商抽象与 MiniMax 服务端适配器

**Files:**
- Create: `backend/MiraiNote.Core/Services/ChatModels/IChatModelProvider.cs`
- Create: `backend/MiraiNote.Core/Services/ChatModels/DeepSeekChatModelProvider.cs`
- Create: `backend/MiraiNote.Core/Services/ChatModels/MiniMaxChatModelProvider.cs`
- Modify: `backend/MiraiNote.Core/Services/ChatService.cs`
- Modify: `backend/MiraiNote.Core/DependencyInjection.cs`
- Test: `backend/MiraiNote.Tests/MiniMaxChatModelProviderTests.cs`
- Test: `backend/MiraiNote.Tests/DeepSeekChatModelProviderTests.cs`

**Interfaces:**
- Produces `IChatModelProvider.ProviderKey`, `Supports(...)`, and `StreamAsync(ChatModelRequest, Func<ChatModelEvent,Task>, CancellationToken)`.
- Normalizes token, thinking, tool call, tool result request and terminal events before orchestration consumes them.

- [ ] **Step 1: Write the failing MiniMax stream-normalization test**

```csharp
[Fact]
public async Task StreamAsync_MapsReasoningDetailsToThinkingAndContentToToken()
{
    var events = await StreamFixtureAsync("data: {\\\"choices\\\":[{\\\"delta\\\":{\\\"reasoning_content\\\":\\\"分析\\\",\\\"content\\\":\\\"答案\\\"}}]}");

    Assert.Collection(events,
        x => Assert.Equal(ChatModelEventType.ThinkingDelta, x.Type),
        x => Assert.Equal(ChatModelEventType.TokenDelta, x.Type));
}
```

- [ ] **Step 2: Run it and verify RED**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~MiniMaxChatModelProviderTests`

Expected: FAIL because the MiniMax provider does not exist.

- [ ] **Step 3: Implement the provider contract and MiniMax adapter**

Use a named HTTP client configured with the deployment base URL. Set Bearer authorization only on the outbound request. Parse incremental OpenAI-compatible SSE, normalize `reasoning_details`/reasoning content, strip `<think>` from final visible content, and convert provider failures to safe user messages.

- [ ] **Step 4: Run the MiniMax provider tests and verify GREEN**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~MiniMaxChatModelProviderTests`

Expected: PASS.

- [ ] **Step 5: Write a failing DeepSeek compatibility test**

```csharp
[Fact]
public async Task DeepSeekProvider_PreservesToolCallsAcrossContinuation()
{
    var result = await StreamDeepSeekToolFixtureAsync();

    Assert.Single(result.ToolCalls);
    Assert.Equal("search_notes", result.ToolCalls[0].Name);
}
```

- [ ] **Step 6: Run it and verify RED**

Run: `dotnet test backend/MiraiNote.Tests --filter FullyQualifiedName~DeepSeekChatModelProviderTests`

Expected: FAIL until current DeepSeek-specific parsing is moved behind the common event contract.

- [ ] **Step 7: Move existing DeepSeek parsing behind the provider and select by session snapshot**

Keep existing request shape and tool semantics. Let orchestration choose exactly one provider from `AiProvider/AiModel`; no controller request can override a persisted session model.

- [ ] **Step 8: Run provider regression tests**

Run: `dotnet test backend/MiraiNote.Tests --filter "FullyQualifiedName~MiniMaxChatModelProviderTests|FullyQualifiedName~DeepSeekChatModelProviderTests|FullyQualifiedName~ChatContinuationTests"`

Expected: PASS.

- [ ] **Step 9: Commit Task 3**

```bash
git add backend/MiraiNote.Core/Services/ChatModels backend/MiraiNote.Core/Services/ChatService.cs backend/MiraiNote.Core/DependencyInjection.cs backend/MiraiNote.Tests/*ChatModelProviderTests.cs
git commit -m "feat(ai): add model provider abstraction and minimax adapter"
```

### Task 4: 模式与模型选择的前端体验

**Files:**
- Create: `frontend/src/api/aiModels.ts`
- Modify: `frontend/src/types/chat.ts`
- Modify: `frontend/src/api/chat.ts`
- Modify: `frontend/src/api/agent.ts`
- Modify: `frontend/src/stores/chat.ts`
- Modify: `frontend/src/views/chat/ChatView.vue`
- Modify: `frontend/src/views/chat/chat.css`
- Test: `frontend/src/stores/chat.spec.ts`
- Test: `frontend/src/views/chat/ChatView.spec.ts`

**Interfaces:**
- Produces `ChatMode = 'chat' | 'work'` and `AiModelCatalogItem` client types.
- `sendMessageStream` always maps Chat to `/messages/stream`; `sendAgentMessageStream` maps Work to `/messages/agent/runs`.
- Switching an already-locked session's model opens the existing app dialog and, after confirmation, creates a new session using that model.

- [ ] **Step 1: Write the failing routing test**

```ts
it('sends Work through the persistent run API instead of keyword detection', async () => {
  await wrapper.getByRole('button', { name: 'Work' }).click()
  await wrapper.getByRole('button', { name: '发送' }).click()

  expect(agentApi.sendAgentMessageStream).toHaveBeenCalledOnce()
  expect(chatApi.sendMessageStream).not.toHaveBeenCalled()
})
```

- [ ] **Step 2: Run it and verify RED**

Run: `npm run test -- --run frontend/src/views/chat/ChatView.spec.ts`

Expected: FAIL because the current code derives Agent mode from message keywords and attachments.

- [ ] **Step 3: Implement explicit mode selection and Work suggestion**

Use native `<button>` controls with pressed state and Chinese accessible names. Keep keyword detection only as a non-blocking suggestion. On suggestion confirmation, retain the draft and send it through Work. Reserve fixed space for status so choosing a mode never shifts the composer.

- [ ] **Step 4: Run routing test and verify GREEN**

Run: `npm run test -- --run frontend/src/views/chat/ChatView.spec.ts`

Expected: PASS.

- [ ] **Step 5: Write the failing locked-model test**

```ts
it('creates a new session when changing a locked conversation model', async () => {
  await wrapper.getByRole('button', { name: '切换模型' }).click()
  await wrapper.getByRole('button', { name: '使用 MiniMax M2.7 新建对话' }).click()

  expect(chatApi.createSession).toHaveBeenCalledWith(expect.objectContaining({ modelKey: 'minimax:MiniMax-M2.7' }))
})
```

- [ ] **Step 6: Run it and verify RED**

Run: `npm run test -- --run frontend/src/views/chat/ChatView.spec.ts`

Expected: FAIL because the catalog and locked-session transition are not implemented.

- [ ] **Step 7: Implement the model catalog, locked badge and phase/elapsed status**

Load public catalog data only. Empty sessions use the selector. Locked sessions show a readable badge; another selection uses the existing `AppDialog` to explain that it creates a new conversation. Work phase events set a concise persistent status such as “正在完成检查 · 12 秒”，and the elapsed clock updates locally once per second without writing events.

- [ ] **Step 8: Run focused frontend tests and build**

Run: `npm run test -- --run frontend/src/stores/chat.spec.ts frontend/src/views/chat/ChatView.spec.ts && npm run build`

Expected: PASS and successful Vite build.

- [ ] **Step 9: Commit Task 4**

```bash
git add frontend/src/api/aiModels.ts frontend/src/types/chat.ts frontend/src/api/chat.ts frontend/src/api/agent.ts frontend/src/stores/chat.ts frontend/src/views/chat/ChatView.vue frontend/src/views/chat/chat.css frontend/src/stores/chat.spec.ts frontend/src/views/chat/ChatView.spec.ts
git commit -m "feat(chat): add explicit work mode and model chooser"
```

### Task 5: 集成验证、迁移检查和浏览器验收

**Files:**
- Modify: `scripts/verify-chat-ui.mjs` (only if model/mode coverage cannot be expressed with its existing fixtures)
- Test: `backend/MiraiNote.Tests/ChatModeRoutingTests.cs`
- Test: `frontend/src/views/chat/ChatView.spec.ts`

- [ ] **Step 1: Run the full backend test suite**

Run: `dotnet test backend/MiraiNote.Tests -c Release --no-restore`

Expected: PASS with zero failed tests.

- [ ] **Step 2: Run frontend unit tests and production build**

Run: `npm run test -- --run && npm run build`

Expected: PASS with a successful Vite build.

- [ ] **Step 3: Verify the database migration can be generated and inspected**

Run: `dotnet ef migrations list --project backend/MiraiNote.Data --startup-project backend/MiraiNote.API`

Expected: includes `AddChatModelSelection` and no pending-model build failure.

- [ ] **Step 4: Browser-check the changed workflow**

Run: `node scripts/verify-chat-ui.mjs`

Expected: explicit Chat sends through the normal stream, Work through the run endpoint, locked model creates a new session, and verification phase text stays visible after the answer body.

- [ ] **Step 5: Commit verification coverage and review diff**

```bash
git add scripts/verify-chat-ui.mjs backend/MiraiNote.Tests frontend/src
git commit -m "test(chat): cover chat work and model selection flow"
git diff origin/main...HEAD --check
```
