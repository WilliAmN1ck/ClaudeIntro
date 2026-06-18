# Phase: Multi-Conversation Management — Implementation Plan

TDD throughout: failing test → implementation → green. Conventional commits per sub-phase.

## Sub-phase 1 — Metadata model + pure helpers (Core)

- `ConversationInfo(string Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int TurnCount)`.
- `ConversationSlug.Slugify(title)` → lowercase `[a-z0-9-]`, collapse/trim dashes; empty → `""`.
  `ConversationSlug.MakeUnique(slug, existingIds)` → append `-2`/`-3`…; empty base → `chat-1`…
- `ChatCommand` record hierarchy + `ChatCommandParser.Parse(input)` (pure).
- **Tests:** `ConversationSlugTests`, `ChatCommandParserTests`.
- **Accept:** helpers covered incl. edge cases (symbols, collisions, blank, unknown `/x`).

## Sub-phase 2 — Store contract + file store + migration (Core)

- Rewrite `IConversationStore` to the id-scoped multi-conversation contract.
- `FileConversationStore`: `conversations/<id>.json` documents (`ConversationDocument` DTO);
  `ListAsync` scans+projects; lazy `EnsureMigratedAsync` (flag + `SemaphoreSlim`) imports a
  legacy `history.json` → `default` once; create/get/rename/delete/load/save.
- Update `FakeConversationStore` to the new contract (in-memory).
- **Tests:** `FileConversationStoreTests` — create/list ordering, save/load round-trip,
  rename keeps id, delete, slug dedup on create, **migration** of legacy `history.json`.
- **Accept:** file store green; migration verified; fake compiles for downstream tests.

## Sub-phase 3 — Postgres store (Core)

- Additive schema: `ADD COLUMN IF NOT EXISTS title text`, `created_at timestamptz NOT NULL DEFAULT now()`.
- Implement list/get/create/rename/delete + id-scoped load/save; preserve existing `default` row.
- **Tests:** extend `PostgresConversationStoreIntegrationTests` (`[SkippableFact]`) for the
  multi-conversation op set + schema migration; keep "missing connection string throws".
- **Accept:** integration test passes against local Docker Postgres (or skips cleanly).

## Sub-phase 4 — Engine + factory rebind (Core)

- `IChatService.ConversationId`; `ClearAsync` empties+persists (keeps the conversation).
- `StreamingChatService` / `CompactionChatService`: take + store `conversationId`; save/clear by id.
- `IChatServiceFactory.CreateAsync(string conversationId, CancellationToken)` loads that seed.
- **Tests:** `StreamingChatServiceTests` (save-by-id, clear-empties), `ChatServiceFactoryTests`
  (seed loads for requested id).
- **Accept:** engine tests green; factory returns an engine seeded from the right conversation.

## Sub-phase 5 — Console host slash commands (host)

- `Program.cs`: ensure+open startup conversation; banner shows active conversation + `/help` hint;
  loop dispatches `ChatCommandParser.Parse`: Send / List / New / Switch / Rename / Delete / Clear /
  Help / Exit / Unknown. Switch+New recreate the engine; Delete-current picks newest remaining or
  recreates `default`. Update `--help` usage text.
- **Accept:** manual run exercises every command; migrated `default` visible in `/list`.

## Sub-phase 6 — Docs + handoff

- Update `README.md` (in-chat commands table, persistence section, store-contract example) and
  `docs/feature-plan.md` (move item out of "future", document the design).
- Write `docs/plans/phase-multi-conversation/handoff.md`.

## Review

- Establish green build/tests, then run `/code-review` **twice**, fixing findings at the root
  between passes (extra targeted passes if warranted). Re-run full test suite after each fix batch.

## Verification

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
dotnet build; dotnet test
dotnet run --project src/ChatBot   # exercise /help /new /list /switch /rename /delete clear
```
