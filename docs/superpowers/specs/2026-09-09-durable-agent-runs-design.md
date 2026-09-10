# Durable Agent Runs — Design

## Goal

An Agent task must keep running when its browser SSE connection closes. The UI must be able to reconnect and recover the complete event history. A process restart must never silently replay a partly completed task: the run becomes recoverable and waits for the user to resume it.

## Current failure

`ChatController.RunSseStreamAsync` links the Agent execution token to the HTTP request token. A client, proxy, or IIS disconnect therefore cancels the agent together with its SSE response. The frontend correctly identifies EOF without `done`/`error`/`stopped`, but has no run identifier with which it can reconnect.

## Chosen approach

Use a database-backed run ledger and an in-process hosted worker. It is deliberately not an external queue/worker deployment: MiraiNote is currently deployed as a single API process. The database is the durable source of truth while the hosted worker owns only live cancellation tokens and in-memory scheduling.

## Persistence model

### AgentRun

- `Id` (GUID), `UserId`, `SessionId`, `RequestJson`, `Status`.
- `CheckpointJson` persists the completed tool-call fingerprints and any resumable agent state.
- `AssistantMessageId` links the run to its progressively persisted assistant message.
- `CreatedAt`, `StartedAt`, `CompletedAt`, `LastActivityAt`, `RecoverableAt`, `FailureMessage`.

Statuses are `queued`, `running`, `awaiting_confirmation`, `completed`, `failed`, `stopped`, and `recoverable`.

### AgentRunEvent

- `RunId`, monotonic `Sequence`, `Type`, `DataJson`, `CreatedAt`.
- Unique `(RunId, Sequence)` index and an index on `(RunId, Sequence)` for replay.
- Terminal events are retained instead of inferred from a closed HTTP response.

The existing `ChatMessage` remains the user-visible durable conversation record. Events are execution telemetry and reconnect data; they do not replace chat history.

## API contract

1. `POST /chat/sessions/{sessionId}/messages/agent/runs` creates an AgentRun and returns `runId` immediately.
2. `GET /chat/agent-runs/{runId}` returns status and recovery metadata after ownership checks.
3. `GET /chat/agent-runs/{runId}/events?afterSequence=N` is an SSE subscription. It replays persisted events newer than `N`, then waits for new ones until a persisted terminal event.
4. `POST /chat/agent-runs/{runId}/stop` cancels the job-specific cancellation token and persists `stopped`.
5. `POST /chat/agent-runs/{runId}/confirm` supplies a pending dangerous-operation decision.
6. `POST /chat/agent-runs/{runId}/resume` accepts only `recoverable` runs and schedules execution from their checkpoint.

The old streaming endpoint stays temporarily compatible by creating a run and subscribing to it. The frontend migrates to the explicit create-then-subscribe flow.

## Execution lifecycle

1. The create request validates session ownership, writes the user message and queued run in one database transaction, then signals the worker.
2. `AgentRunBackgroundService` claims a queued run in a fresh DI scope, marks it running, and invokes the existing Agent orchestration with a job token that is not derived from `RequestAborted`.
3. The callback writes each event and updates `LastActivityAt`; it also publishes a lightweight in-memory wake signal so active SSE subscribers need not poll.
4. Tool completion updates the checkpoint transactionally before the next model turn. Resume instructions include the checkpoint so completed calls are not replayed.
5. A normal, failure, or user-stop terminal state is persisted first, then its terminal event is appended.
6. On application startup, any `queued`, `running`, or `awaiting_confirmation` record becomes `recoverable`; no tools are called automatically. The user must call resume.

## SSE and frontend behavior

- Event frames include `id: <sequence>` so the last accepted sequence survives reconnects.
- A transient connection failure does not create a failed assistant draft. The store keeps the run active and retries subscription with bounded backoff.
- The assistant text and tool-event projection are rebuilt idempotently from replayed events.
- The UI presents `recoverable` as “任务因服务重启中断，可继续执行”, with an explicit Continue action.
- Stop operates on `runId`, rather than relying on fetch abort or a session-wide request token.

## Non-goals

- Automatic replay after service restart.
- Cross-process execution leasing or a separate message broker.
- Making arbitrary shell/browser tools transactional. The checkpoint prevents replay only when a tool reports successful completion.
- Persisting temporary (non-session) chats in this iteration; their existing request-bound semantics remain explicit.

## Verification

- Unit tests for state transition rules, sequence allocation, replay boundaries, restart recovery, and duplicate resume prevention.
- Controller/integration tests for ownership, persisted terminal event ordering, stop, confirm, and event replay.
- Frontend tests for reconnect and no generic failure draft on a non-terminal EOF.
- Manual browser test: start a long Agent task, terminate the SSE subscription, verify it completes, then reconnect and receive the final report.
