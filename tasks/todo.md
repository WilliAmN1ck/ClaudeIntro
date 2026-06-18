# Tasks — Multi-Conversation Management

Live checklist (updated during execution). See `docs/plans/phase-multi-conversation/`.

## Sub-phase 1 — Metadata + pure helpers
- [x] `ConversationInfo` record
- [x] `ConversationSlug` (Slugify + MakeUnique) + tests
- [x] `ChatCommand` + `ChatCommandParser` + tests

## Sub-phase 2 — Store contract + file store + migration
- [x] Rewrite `IConversationStore` (id-scoped multi-conversation)
- [x] `FileConversationStore` (per-conversation docs + lazy migration)
- [x] Update `FakeConversationStore`
- [x] `FileConversationStoreTests` (round-trips, rename, delete, dedup, migration)

## Sub-phase 3 — Postgres store
- [x] Schema migration (title + created_at) + full op set
- [x] Extend `PostgresConversationStoreIntegrationTests`

## Sub-phase 4 — Engine + factory rebind
- [x] `IChatService.ConversationId` + `ClearAsync` semantics
- [x] `StreamingChatService` / `CompactionChatService` bind conversation id
- [x] `ChatServiceFactory.CreateAsync(id)`
- [x] Engine + factory tests

## Sub-phase 5 — Console host
- [x] `Program.cs` startup + banner + slash-command dispatch
- [x] Update `--help` usage text

## Sub-phase 6 — Docs + handoff
- [x] README + feature-plan updates
- [x] handoff.md

## Review
- [x] Build + full test suite green (71 passed, 1 skipped) + host smoke test
- [ ] `/code-review` pass 1 + root-cause fixes
- [ ] `/code-review` pass 2 + root-cause fixes
