# Phase: Tool Use in Compaction Mode — Spec

**Date:** 2026-06-18
**Branch:** `feature/compaction-tools` (from `feature/multi-conversation`; sibling to `feature/cost-tracking`)
**Status:** Approved (autonomous follow-up task) — in execution

## Problem

The beta compaction engine (`CompactionChatService`, `--compaction`) ignored registered tools
and logged a warning — "Tool use is streaming-mode only" was a documented limitation. The
streaming engine has run an agentic tool loop since the tool-use feature shipped.

## Goal

Make the compaction engine run the **same agentic tool loop** as the streaming engine, and make
that loop unit-testable (matching the streaming path's testability via `IChatCompletionClient`).

## Design

- **`IBetaCompletionClient`** — a seam over the beta `Messages.Create` call, mirroring
  `IChatCompletionClient`. Returns a `BetaCompletionResult` (reply text + domain `ToolCall`s +
  the raw assistant `BetaContentBlockParam`s to round-trip + stop-for-tools + `TokenUsage`).
  `AnthropicBetaCompletionClient` is the real implementation; a scripted fake drives the tests.
- **`CompactionChatService`** depends on `IBetaCompletionClient` + `IEnumerable<IChatTool>`
  (was: `AnthropicClient` directly, no tools). It builds beta tool definitions from the tools and
  runs the loop: send → if `stop_reason == tool_use`, execute each tool (via the existing
  `ToolInvoker`), append `tool_result` blocks, repeat → else commit. Compaction blocks are
  preserved verbatim across iterations; usage is summed; commit-on-success leaves history
  untouched on cancel/failure (same invariants as the streaming engine).
- **Factory/DI** — `ChatServiceFactory` injects the beta seam and passes it + the tools to the
  compaction engine; the "tools ignored in compaction" warning is removed. The console banner
  now shows the Tools line in compaction mode too.

## Acceptance Criteria

1. `CompactionChatService` executes tools and continues the loop; usage is summed; unknown tools
   recover with an error result; clear empties the conversation — all unit-tested via the fake.
2. The factory wires the beta seam; DI registers `AnthropicBetaCompletionClient`; no warning.
3. README + feature-plan updated (tool use works in both modes); handoff written; two reviews.

## Out of Scope / Caveat

- **End-to-end beta behavior is not verified against the live API** (no API key in this
  environment) — the loop orchestration is unit-tested with a scripted seam exactly as the
  streaming loop is. Verify a real `--compaction` tool call against the API when available.
- Persistence stays text-only (tool/compaction blocks are in-memory per session), unchanged.
