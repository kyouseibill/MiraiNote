---
version: alpha
name: MiraiNote
description: A quiet notebook for work, life and conversations, with warm paper and restrained blue-grey ink.
colors:
  primary: "#4c6178"
  primary-dark: "#384b60"
  background: "#f6f3ec"
  surface: "#fcfbf8"
  border: "#ddd8cf"
  text: "#262521"
  muted: "#7f7a72"
  danger: "#b4493f"
typography:
  sans:
    fontFamily: "Noto Sans SC, PingFang SC, Microsoft YaHei, sans-serif"
  serif:
    fontFamily: "Noto Serif SC, Songti SC, SimSun, serif"
  mono:
    fontFamily: "ui-monospace, Consolas, monospace"
rounded:
  DEFAULT: "7px"
  sm: "5px"
  lg: "13px"
spacing:
  conversation-max: "812px"
  chat-sidebar: "248px"
components:
  button: {}
  dialog: {}
  composer: {}
---

# MiraiNote Design System

## Overview

### Creative North Star

A well-used notebook: calm paper, readable ink, and enough room for a thought to develop. Existing DashboardView and MainLayout establish the visual identity. The user selected continuity with that identity on 2026-09-05.

### Product context and register

MiraiNote is a Chinese-language personal workspace for work records, life notes, reminders and AI conversations. The chat redesign serves frequent desktop use and phone access. This is a product interface; controls and content take precedence over decorative expression. The Japanese brand name is retained; it does not establish a Japanese-market business requirement. ChatController, chat.ts and the existing domain types own the data behavior.

The chat signature is an open, notebook-like answer column beside quiet, dated conversation rows. Avoid marketing panels, gradients, decorative statistics and bright disconnected accents.

Runtime ownership: `frontend/src/assets/main.css` owns existing `--mn-*` values; this document mirrors them, it does not generate CSS. The established Tailwind adapter is `frontend/tailwind.config.js`. Chat's scoped `frontend/src/views/chat/chat.css` consumes those variables, with local secondary text and selected-surface refinements. Shared AppDialog consumes the same variables. No global palette or navigation dimensions change in this redesign.

## Colors

Paper is `--mn-paper`, the reading surface is `--mn-paper-light`, dividers are `--mn-line`, body text is `--mn-ink`, actions are `--mn-indigo` and `--mn-indigo-dark`, and destructive actions are `--mn-red`. The chat secondary text uses #68665f for small readable labels; #7f7a72 remains the established global muted value. User messages use a subdued #edf0f2 surface and #34495c ink. Status must also use text or an icon. The application currently exposes a light theme only.

## Typography

Reuse the bundled Noto fonts. Noto Sans SC owns controls and prose. Noto Serif SC is reserved for the welcome sentence and existing brand headings. Body answers are 14px desktop/13px phone with 1.95 line height; utility text is 10–12px. Code uses the system monospace stack. User content remains literal text, while assistant Markdown is sanitized.

## Layout

The global navigation stays intact. Chat owns a 100dvh panel on desktop, subtracting the existing 56px app header below 1024px. Its sidebar, messages and modal bodies each own their scroll. The conversation and composer share an 812px outer width. The sidebar is 248px (220px for smaller desktops), collapsible on desktop, and a drawer below 768px. Composer remains visible; its textarea grows with content and has an explicit expansion button. Phone padding respects safe-area insets.

## Elevation & Depth

Use surface contrast and thin borders for hierarchy. Answers have no card or shadow. Small elevation is reserved for the composer, popovers and modal overlays. Native dialog top-layer behavior keeps modals above the application shell.

## Shapes

Controls use 5–7px radii; the composer and user bubble use 12–13px. User messages have a quieter 3px trailing corner. Maintain Tabler's single-weight outline language.

## Components

### Foundational visual states

Every action has visible focus, hover and disabled states. Selected conversations have a background and an ink side marker. Loaders occupy the owning panel and use status text. Reduced motion removes animation.

### Buttons and actions

Blue-grey solid buttons identify new conversation, send and modal commit. Secondary actions are outlined or plain. Delete is separated and named explicitly. Busy buttons retain geometry and prevent repeated submission.

### Navigation and data display

Conversations retain project filtering, date grouping, pinning, search snippets, rename, branch and archive actions. Message actions stay visible on touch and keyboard; users can copy text without opening a menu. Long Markdown tables and code blocks scroll within their own width.

### Forms and overlays

`AppDialog.vue` owns confirmation, edit, project, archive and file overlays for this workflow. Native project selects intentionally retain platform popup and keyboard behavior. Forms use labels and inline errors. Search clears immediately and respects IME composition. `useToast` remains the app's feedback owner.

### Iconography

Reuse `@tabler/icons-vue` with 15–20px outline icons and the existing favicon for Mirai. Icon-only controls have Chinese accessible names. Keep text on unfamiliar actions.

### Motion

Use 150ms interaction transitions and a modest generation spinner. Streaming is functional progress, not decorative typing. Honor `prefers-reduced-motion`; never force-scroll while the reader is browsing earlier messages.

### Content and data visualization

Keep Chinese labels direct: 新对话、重命名、归档、还原、删除、对话文件. Short, original welcome copy supports the notebook personality. Context usage is clearly labeled, not an unexplained percentage. No invented model selector or online-status claim.

## Do's and Don'ts

- Keep existing project, temporary-chat, attachment and streaming semantics.
- Use the shared toast and dialog owners; preserve drafts on recoverable failures.
- Do not add raw HTML rendering for user content or browser alert/confirm/prompt calls.
- Do not let a chat redesign alter sibling screens or expose production-only configuration.
