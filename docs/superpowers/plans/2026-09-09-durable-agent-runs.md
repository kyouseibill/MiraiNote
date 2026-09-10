# Durable Agent Runs — Implementation Plan

> Execute with test-driven development. Preserve the existing uncommitted continuation changes; do not fold unrelated files into commits.

## 1. Persisted run ledger

**Files:**
- Add `backend/MiraiNote.Data/Entities/AgentRun.cs`
- Add `backend/MiraiNote.Data/Entities/AgentRunEvent.cs`
- Modify `backend/MiraiNote.Data/Context/MiraiNoteDbContext.cs`
- Add EF migration and update `MiraiNoteDbContextModelSnapshot.cs`

Write failing EF-backed tests for status transitions, ordered event allocation, user/session ownership fields, and query by sequence. Implement the entities, indexes, relations and migration. Keep `ChatMessage` as the conversation source of truth.

## 2. Run coordinator and worker

**Files:**
- Add `backend/MiraiNote.Core/Services/AgentRuns/AgentRunCoordinator.cs`
- Add `backend/MiraiNote.Core/Services/AgentRuns/AgentRunBackgroundService.cs`
- Add `backend/MiraiNote.Core/Services/AgentRuns/AgentRunContracts.cs`
- Modify `backend/MiraiNote.Core/DependencyInjection.cs`

Add failing tests proving that request cancellation does not cancel a claimed run, explicit stop does, terminal events are persisted before observable completion, and startup recovery changes incomplete runs to `recoverable`. Implement a singleton coordinator for scheduling/live cancellation/confirmation signals and a hosted service that uses fresh scopes for database and `IChatService` work.

## 3. Agent orchestration adapter and checkpoints

**Files:**
- Modify `backend/MiraiNote.Core/Services/ChatService.cs`
- Modify `backend/MiraiNote.Core/Services/AgentRunSupervisor.cs`
- Add or extend `backend/MiraiNote.Tests/AgentRun*Tests.cs`

Add tests for event persistence callback mapping and completed-tool checkpoint reuse. Introduce an adapter that invokes the existing Agent flow with a job cancellation token and persists callbacks; do not link it to HTTP cancellation. Persist the assistant message reference and checkpoint immediately after successful tool results. Retain the existing stream method during migration.

## 4. Run-oriented controller and SSE replay

**Files:**
- Modify `backend/MiraiNote.API/Controllers/ChatController.cs`
- Modify `backend/MiraiNote.Shared/Dtos/Chat/ChatDtos.cs`
- Add controller tests in `backend/MiraiNote.Tests`

Add create/status/subscribe/stop/confirm/resume endpoints with session ownership checks. Write SSE frames with `id: <sequence>`, replay events after `afterSequence`, then stream live notifications. Keep disconnect handling local to the subscription; it must not cancel the run. On startup, convert incomplete runs to `recoverable`.

## 5. Frontend create-then-subscribe and reconnect

**Files:**
- Modify `frontend/src/api/agent.ts`
- Modify `frontend/src/api/sse.ts`
- Modify `frontend/src/stores/chat.ts`
- Modify `frontend/src/views/chat/ChatView.vue` only for recovery/reconnect affordances
- Extend `frontend/scripts/test-chat-store.mjs`

Write a failing store/API test that simulates non-terminal EOF followed by replay, then implement bounded retry, sequence tracking, idempotent event projection, status inspection, stop-by-run-id and resume action. The initial disconnect must show “任务仍在执行，正在重连” rather than adding a generic failure draft.

## 6. Verification and delivery

Run targeted .NET tests, then the full backend suite; run the frontend store test and production build. Verify migration generation is clean and inspect the final diff to ensure only scoped files changed. Manually exercise a long Agent task by dropping a subscription and reconnecting before packaging a deployment artifact.
