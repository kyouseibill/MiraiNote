# MiraiNote interaction contract

This records the chat migration of 2026-09-05. API authorization, persistence and project rules remain owned by the existing backend. Other legacy screens are not migrated by this change.

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
|---|---|---|---|---|
| Select/Listbox | Native select in ChatView | Existing project API + DESIGN.md | Platform popup for project selection and moving conversations | Keyboard and project workflow browser checks |
| Form | ChatView form state and AppDialog | Chat API validation + this contract | Project form, rename, branch edit | Empty validation, failures, double-submit checks |
| Scrollbar | frontend/src/assets/main.css | Existing global baseline | Chat scroll containers own only geometry | Desktop and phone overflow checks |
| Toast | useToast + ToastContainer | Existing app feedback | Success, warning, error | Browser text feedback |
| CRUD | useChatStore + chatApi | ChatController + chat types | Session/project/branch/archive | Mock API browser regression and backend tests |

## Conversations

Ordinary messages persist through the existing server APIs. Temporary chat remains unsaved and clearly explains loss on close/switch. Search terms are transient in-memory state because message searches can contain private content; they are not written into URLs. Superseded list/detail requests must not overwrite the user's newer selection.

Chat and Work share conversations, attachments and the session-fixed model, but not execution authority. Chat may search the web and records, fetch webpages, and read or list workspace files; it does not expose write, shell, login, send, delete or other side-effect tools, and it does not run completion verification. Work exposes the full authorized tool set and completion checks for action requests. A direct question answered in Work without tool execution finishes immediately instead of entering completion verification. The header and composer helper name these differences without changing the established layout.

Each session retains its own in-memory draft while switching conversations. No chat text is added to browser persistent storage. The composer can accept the next draft while another reply is streaming; submitting waits until generation completes. Creating or sending prevents duplicate requests. Uploads are finished before sending; unsupported images retain the existing explanation.

Enter sends, Shift+Enter inserts a newline, and IME composition never submits. Sending and switching to a conversation scrolls to its end. Incoming content follows the bottom only while the reader is near it; scrolling up exposes a return-to-latest button.

## Messages and actions

User text is escaped by Vue text interpolation. Assistant Markdown uses useMarkdown/DOMPurify. Thinking is a disclosure and message text/actions remain readable without hover. Opening an overflowing thinking panel positions its inner scroll at the newest content without moving the outer conversation. Copy acknowledges success and reports clipboard failures. Editing and regenerating preserve the existing branch behavior. Generated files retain preview/download support.

## Dialogs and recovery

AppDialog uses native showModal for inert background and focus containment, provides title and description, handles Escape/backdrop, and restores focus to the invoking control. Busy mutations keep forms open and prevent repeated submission or dismissal. Delete describes consequences and defaults focus to cancel. Rename/edit focus their field. A mobile navigation drawer contains focus and closes on Escape.

Project, rename and message edit inputs retain their contents on failure with inline feedback. Archive remains recoverable through the archive manager. High-risk tool execution retains the existing confirmation contract and arguments. No browser-native alert, confirm or prompt is added.

The visible stream reaches its terminal state before noncritical memory extraction begins. A failed or malformed completion review ends with an explanatory status instead of repeatedly regenerating the same answer. A review may request at most one automatic continuation without new tool evidence. Provider quota errors that are known to be permanent for the current request are surfaced directly and are not retried as transient throttling.

## Skills

Skill 管理页读取当前用户私有工作区 `skills/<name>/SKILL.md`，保留原始 Markdown 与附属文件；编辑失败保留输入内容。列表显示无效文件的原因，启停状态由 Skill 目录内的 MiraiNote 设置文件控制。删除经 AppDialog 确认后移至 `skills/.trash/`，不是硬删除。

Chat 与 Work 的输入区共用 Skill 选择器，选择后仅插入 `$名称` 草稿，不自动发送。显式提及时服务端加载完整步骤；自动调用只暴露已启用且允许自动调用的名称与描述，模型必须通过只读 `load_skill` 再读取正文。Skill 不提升 Chat 的工具权限，也不绕过 Work 的危险操作确认。相关存储与授权约束以 `WorkspacePaths` 和 `FileSkillService` 为准。

## Verification boundary

`scripts/verify-chat-ui.mjs` uses mock APIs in a real browser to exercise UI transitions without credentials, persistent database mutations or paid model calls. Backend tests validate the existing service contract. Neither proves live model availability or production deployment.
