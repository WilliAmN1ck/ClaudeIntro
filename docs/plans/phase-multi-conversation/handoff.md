# Phase: Multi-Conversation Management — Handoff

- **Date:** 2026-06-18
- **Branch:** `feature/multi-conversation` (from `origin/main` @ `f493f6e`)
- **Plan:** `docs/plans/phase-multi-conversation/{spec.md,plan.md}`
- **Status:** Implementation complete; build + tests green; host smoke-tested. Pending: two `/code-review` passes.

## What Was Built

**Pure helpers (Core)**
- `ConversationInfo` — per-conversation metadata (id, title, created/updated, turn count).
- `ConversationSlug` — `Slugify` (title → lowercase ASCII slug) + `MakeUnique` (dedup with `-2`/`-3`, blank → `chat-N`).
- `ChatCommand` + `ChatCommandParser` — parse a console line into a send-or-manage intent (pure, no I/O).

**Store layer (Core)**
- `IConversationStore` rewritten to be id-scoped: `ListAsync`/`GetAsync`/`CreateAsync`/`LoadAsync(id)`/`SaveAsync(id, turns)`/`RenameAsync`/`DeleteAsync`.
- `FileConversationStore` — one JSON document per conversation under `<base>/conversations/<id>.json`; lazy one-time migration of a legacy `history.json` into `default` (guarded on the directory's first creation, plus a `_migrated` flag + `SemaphoreSlim`). Ids are slugified at the boundary for path safety.
- `PostgresConversationStore` — additive schema migration (`title`, `created_at`); full op set; existing row preserved; `COALESCE(title, id)` on read.
- `FakeConversationStore` (tests) — in-memory implementation of the new contract with `Seed`/`SavedFor` helpers.

**Engine + factory (Core)**
- `IChatService.ConversationId` added; `ClearAsync` now empties the active conversation (persists empty) but keeps it.
- `StreamingChatService` + `CompactionChatService` take a `conversationId` and persist under it.
- `IChatServiceFactory.CreateAsync(string conversationId, …)` seeds from that conversation.

**Host (`src/ChatBot/Program.cs`)**
- Startup ensures + opens `--conversation` (default `default`); banner shows the active conversation.
- Slash-command dispatch: `/list`, `/new [title]`, `/switch <id>`, `/rename <title>`, `/delete [id]` (confirms; re-opens newest remaining or a fresh `default` if the active one is deleted), `/clear`/`clear`, `/help`, `exit`/`quit`. The old `[Y/n]` resume prompt was removed.

## What Changed From the Spec

- No behavioral deviations. The whole change landed as one cohesive green commit (store + engine + host) rather than separate store/host commits, because the `IConversationStore` change is pervasive and every intermediate state must build. Helpers and docs are separate commits.

## What the Next Phase Needs to Know

- Persistence is **text-only** (`StoredTurn`); tool-use/tool-result blocks remain in-call only.
- Switching conversations **recreates the engine** via the factory (engines are cheap, stateless beyond turns + config).
- Conversation ids are slugs and **stable across rename** (only the title changes).
- File migration runs once, keyed on the `conversations/` directory's first creation — deleting `default` does not resurrect legacy history.
- `ChatOptions.ConversationId` is now "the conversation to open at startup" for both backends (was Postgres-only).

## Files Changed

| File | Change | Notes |
| --- | --- | --- |
| `ConversationInfo.cs`, `ConversationSlug.cs`, `ChatCommand.cs`, `ChatCommandParser.cs` | new | Pure helpers (Core) |
| `IConversationStore.cs` | rewrite | Id-scoped multi-conversation contract |
| `FileConversationStore.cs` | rewrite | Document-per-conversation + migration |
| `PostgresConversationStore.cs` | rewrite | Schema upgrade + full op set |
| `IChatService.cs`, `StreamingChatService.cs`, `CompactionChatService.cs` | edit | Bind conversation id; clear keeps conversation |
| `ChatServiceFactory.cs` | edit | `CreateAsync(id)` |
| `src/ChatBot/Program.cs` | rewrite | Slash-command host |
| `FakeConversationStore.cs`, `FileConversationStoreTests.cs`, `PostgresConversationStoreIntegrationTests.cs`, `StreamingChatServiceTests.cs`, `ChatServiceFactoryTests.cs` | edit | New-contract tests |
| `ConversationSlugTests.cs`, `ChatCommandParserTests.cs` | new | Helper tests |
| `README.md`, `docs/feature-plan.md` | edit | Docs |

## Test Coverage

- **71 passing, 1 skipped** (`dotnet test`). Skipped = Postgres integration (`CHATBOT_TEST_POSTGRES` unset).
- Unit: slug normalize/dedup; command parser (all verbs + edges); file store round-trips, list ordering, rename-keeps-id, delete, dedup, idempotent ensure, path-safe ids, legacy migration, no-resurrection, corrupt-file skip; factory seeding/binding; engine save-by-id + clear-keeps-conversation + exposes id.
- Integration (opt-in): Postgres create/save/load/rename/list/delete round-trip.
- Manual smoke: drove `/help /list /new /rename /switch /delete (+confirm) /clear /bogus exit` against a temp store with a dummy key — no API calls, all flows correct.

## Known Issues / Tech Debt

- Listing scans every conversation file (no index). Fine for a console app's handful of conversations; revisit if it grows.
- Postgres `ExistingIds` for `/new` dedup does a `SELECT id` then inserts — a tiny TOCTOU window exists for concurrent `/new`, irrelevant for a single-user console host.
- Rename does not bump `UpdatedAt` (ordering reflects message activity, not metadata edits) — intentional.

## Verification Commands

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
dotnet build
dotnet test                       # 71 passed, 1 skipped
dotnet run --project src/ChatBot  # then: /help, /new trip, /list, /switch default, /rename X, /delete, clear
# Postgres path (optional): docker compose up -d; set CHATBOT_TEST_POSTGRES; dotnet test
```
