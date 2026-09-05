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

Each session retains its own in-memory draft while switching conversations. No chat text is added to browser persistent storage. The composer can accept the next draft while another reply is streaming; submitting waits until generation completes. Creating or sending prevents duplicate requests. Uploads are finished before sending; unsupported images retain the existing explanation.

Enter sends, Shift+Enter inserts a newline, and IME composition never submits. Sending and switching to a conversation scrolls to its end. Incoming content follows the bottom only while the reader is near it; scrolling up exposes a return-to-latest button.

## Messages and actions

User text is escaped by Vue text interpolation. Assistant Markdown uses useMarkdown/DOMPurify. Thinking is a disclosure and message text/actions remain readable without hover. Copy acknowledges success and reports clipboard failures. Editing and regenerating preserve the existing branch behavior. Generated files retain preview/download support.

## Dialogs and recovery

AppDialog uses native showModal for inert background and focus containment, provides title and description, handles Escape/backdrop, and restores focus to the invoking control. Busy mutations keep forms open and prevent repeated submission or dismissal. Delete describes consequences and defaults focus to cancel. Rename/edit focus their field. A mobile navigation drawer contains focus and closes on Escape.

Project, rename and message edit inputs retain their contents on failure with inline feedback. Archive remains recoverable through the archive manager. High-risk tool execution retains the existing confirmation contract and arguments. No browser-native alert, confirm or prompt is added.

## Verification boundary

`scripts/verify-chat-ui.mjs` uses mock APIs in a real browser to exercise UI transitions without credentials, persistent database mutations or paid model calls. Backend tests validate the existing service contract. Neither proves live model availability or production deployment.
