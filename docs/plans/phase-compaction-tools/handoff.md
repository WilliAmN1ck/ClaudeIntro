# Phase: Tool Use in Compaction Mode — Handoff

- **Date:** 2026-06-18
- **Branch:** `feature/compaction-tools` (from `feature/multi-conversation`; sibling to `feature/cost-tracking`)
- **Plan:** `docs/plans/phase-compaction-tools/spec.md`
- **Status:** Complete. Build + 86 tests green (1 Postgres integration skipped); two `/code-review` passes (pass 1 fixed, pass 2 clean).

## What Was Built

- **`IBetaCompletionClient`** (Core) — a seam over the beta `Messages.Create` call, mirroring
  `IChatCompletionClient`. Returns `BetaCompletionResult` (reply text, domain `ToolCall`s, the raw
  assistant `BetaContentBlockParam`s to round-trip, stop-for-tools, `TokenUsage`).
- **`AnthropicBetaCompletionClient`** (Core) — the real implementation; projects the `BetaMessage`
  response into text + tool calls and round-trips text/tool-use/**compaction** blocks (including
  the compaction block's `EncryptedContent`).
- **`CompactionChatService`** (Core) — now depends on `IBetaCompletionClient` + `IEnumerable<IChatTool>`
  and runs the agentic loop (tool_use → tool_result → end_turn), summing usage and preserving
  compaction blocks across iterations; commit-on-success keeps history untouched on failure/cancel.
- **`ToolInvoker.BetaToolUnions`** (Core) — beta tool definitions built alongside the streaming
  `ToolUnions` (one place for both SDK flavors; the beta namespace is aliased to avoid a `Tool` clash).
- **Factory/DI** — `ChatServiceFactory` injects the beta seam and passes it + tools to the compaction
  engine (no more "tools ignored" warning); `AnthropicBetaCompletionClient` registered in `AddChatBot`.
- **Host** — the banner now lists tools in compaction mode too.

## What Changed From the Spec

- No behavioral deviations. Two review-driven additions beyond the spec: round-tripping the
  compaction block's `EncryptedContent` (a latent gap carried from the pre-existing code), and
  centralizing beta tool-definition building in `ToolInvoker`.

## What the Next Phase Needs to Know

- **End-to-end beta tool+compaction behavior is unit-tested via the seam, not verified against the
  live API** (no API key here) — exactly as the streaming loop is tested. Verify a real
  `--compaction` tool call against the API when available.
- The response projection handles text/tool-use/compaction blocks; thinking/server-tool blocks are
  not round-tripped (the app enables neither) — same scope as the streaming engine.
- Persistence stays text-only; tool/compaction blocks live in the in-memory beta history per session.

## Files Changed

| File | Change | Notes |
| --- | --- | --- |
| `IBetaCompletionClient.cs`, `AnthropicBetaCompletionClient.cs` | new | Beta seam + real adapter |
| `CompactionChatService.cs` | rewrite | Tool loop via the seam |
| `ToolInvoker.cs` | edit | `BetaToolUnions` |
| `ChatServiceFactory.cs`, `ServiceCollectionExtensions.cs` | edit | Wire the seam; drop warning |
| `src/ChatBot/Program.cs` | edit | Banner shows tools in compaction |
| `FakeBetaCompletionClient.cs`, `CompactionChatServiceTests.cs` | new | Loop tests |
| `ChatServiceFactoryTests.cs` | edit | New factory ctor |
| `README.md`, `docs/feature-plan.md` | edit | Docs |

## Test Coverage

- **86 passing, 1 skipped**. Compaction loop tests: plain reply, single + multiple tool calls,
  unknown-tool recovery, commit-on-failure (thrown completion leaves history untouched), option
  clamping, no-usage-before-first-turn, seeding, clear.
- The real adapter (`AnthropicBetaCompletionClient`) is fake-bypassed in tests — the same pattern as
  the streaming `AnthropicCompletionClient`.

## Code Review (two passes, max effort)

- **Pass 1:** round-trip `EncryptedContent`; centralize `BetaToolUnions` in `ToolInvoker`; add
  multi-tool / commit-on-failure / clamp / no-usage tests.
- **Pass 2:** clean — fixes verified (types match, alias unambiguous, commit-on-failure proves the
  invariant, DI intact).

## Known Issues / Tech Debt

- Live API verification of compaction+tools is pending (see above).
- `while (true)` loop has no hard iteration cap — identical to the streaming engine; the model ends
  the loop with `end_turn`.

## Verification Commands

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
dotnet build; dotnet test                          # 86 passed, 1 skipped
dotnet run --project src/ChatBot -- --compaction   # tools now run in compaction mode (needs API key + compaction-capable model)
```
