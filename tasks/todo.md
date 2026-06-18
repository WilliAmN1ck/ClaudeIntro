# Tasks — Multi-Conversation Management

Live checklist (updated during execution). See `docs/plans/phase-multi-conversation/`.

## Sub-phase 1 — Metadata + pure helpers
- [x] `ConversationInfo` record
- [x] `ConversationSlug` (Slugify + MakeUnique) + tests
- [x] `ChatCommand` + `ChatCommandParser` + tests

## Sub-phase 2 — Store contract + file store + migration
- [ ] Rewrite `IConversationStore` (id-scoped multi-conversation)
- [ ] `FileConversationStore` (per-conversation docs + lazy migration)
- [ ] Update `FakeConversationStore`
- [ ] `FileConversationStoreTests` (round-trips, rename, delete, dedup, migration)

## Sub-phase 3 — Postgres store
- [ ] Schema migration (title + created_at) + full op set
- [ ] Extend `PostgresConversationStoreIntegrationTests`

## Sub-phase 4 — Engine + factory rebind
- [ ] `IChatService.ConversationId` + `ClearAsync` semantics
- [ ] `StreamingChatService` / `CompactionChatService` bind conversation id
- [ ] `ChatServiceFactory.CreateAsync(id)`
- [ ] Engine + factory tests

## Sub-phase 5 — Console host
- [ ] `Program.cs` startup + banner + slash-command dispatch
- [ ] Update `--help` usage text

## Sub-phase 6 — Docs + handoff
- [ ] README + feature-plan updates
- [ ] handoff.md

## Review
- [ ] Build + full test suite green
- [ ] `/code-review` pass 1 + root-cause fixes
- [ ] `/code-review` pass 2 + root-cause fixes
