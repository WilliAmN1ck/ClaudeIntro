# Phase: Multi-Conversation Management — Spec

**Date:** 2026-06-18
**Branch:** `feature/multi-conversation` (from `origin/main` @ `f493f6e`)
**Status:** Approved — in execution

## Problem

The chat engine supports exactly one conversation. `IConversationStore` fixes the
conversation identity at construction (one `history.json`, or one Postgres row keyed by
`ChatOptions.ConversationId`). The host only offers a "resume? [Y/n]" prompt and `clear`.
`docs/feature-plan.md` lists "Multi-conversation management beyond the single
`ConversationId` key" as the next roadmap item.

## Goal

Make conversations first-class: **list, create, switch, rename, delete** named
conversations, each carrying metadata (title, created/updated timestamps, turn count).

## Decisions (from Q&A)

| Question | Decision |
| --- | --- |
| Scope | **Full** — list / new / switch / rename / delete + metadata |
| Existing history | **Migrate to `default`** — fold the current single history into a conversation named `default` (no data loss) |
| Console UX | **Slash commands** — `/list`, `/new`, `/switch`, `/rename`, `/delete`, `/help` |

## Decisions (autonomous — user delegated; revisit if wrong)

- **Conversation id = slug** of the title (`[a-z0-9-]`), deduped with `-2`/`-3`; **stable
  across rename** (rename changes only the title). `default` is reserved for migrated history.
  Empty/symbol-only titles fall back to `chat-N`.
- **Startup** opens `ChatOptions.ConversationId` (default `default`), creating it if absent.
  `--conversation <id>` now selects the startup conversation for **both** backends.
- **`clear`** empties the current conversation but keeps it; **`/delete`** removes one.
  Deleting the last conversation recreates an empty `default` (never zero conversations).
- **Switching** recreates the engine via `factory.CreateAsync(id)` (matches the existing
  factory/seed pattern) rather than mutating a live engine.

## Requirements / Acceptance Criteria

1. `IConversationStore` exposes list/get/create/load/save/rename/delete, all id-scoped.
2. File store persists one document per conversation under `…/ClaudeIntro/conversations/<id>.json`
   and migrates a pre-existing `history.json` to `default` exactly once.
3. Postgres store gains `title` + `created_at` (additive migration) and the full op set; the
   existing `default` row is preserved.
4. Engine (`StreamingChatService`, `CompactionChatService`) is bound to a conversation id and
   persists under it; `IChatService.ConversationId` is exposed; `ClearAsync` empties (keeps) it.
5. Console host parses slash commands (via a pure, tested parser) and manages conversations.
6. Pre-existing `history.json` appears as `default` with its turns intact after upgrade.
7. All network-independent logic is unit-tested (slug, parser, file store + migration,
   engine save-by-id); Postgres covered by the opt-in integration test.
8. `README.md` and `docs/feature-plan.md` updated; handoff written.

## Out of Scope

- Most-recently-used auto-open at startup (predictable `default`/configured open instead).
- Per-conversation system prompt / model overrides.
- Concurrency/locking (single-user console app).
