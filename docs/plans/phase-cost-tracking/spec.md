# Phase: Dollar-Cost Computation — Spec

**Date:** 2026-06-18
**Branch:** `feature/cost-tracking` (stacked on `feature/multi-conversation`)
**Status:** Approved (autonomous follow-up task) — in execution

## Problem

The engine exposes per-turn `TokenUsage` but never the dollar cost. `docs/feature-plan.md`
lists "Dollar-cost computation (needs a maintained price table)" as a future item. Users
want to see what each turn and the session cost.

## Goal

Show per-turn and running-session **USD cost** alongside the existing `[tokens: …]` line,
using a built-in (and config-overridable) per-model price table.

## Decisions

- **Pricing source:** Anthropic list prices, USD per million tokens, as of 2026-06 (from the
  `claude-api` skill reference). Cache rates follow ephemeral (5-min) caching — write = 1.25×
  input, read = 0.1× input — matching the engine's `CacheControlEphemeral`.

  | Model | Input | Output | Cache write | Cache read |
  | --- | --- | --- | --- | --- |
  | claude-opus-4-8 / 4-7 / 4-6 | 5.00 | 25.00 | 6.25 | 0.50 |
  | claude-sonnet-4-6 | 3.00 | 15.00 | 3.75 | 0.30 |
  | claude-haiku-4-5 | 1.00 | 5.00 | 1.25 | 0.10 |
  | claude-fable-5 | 10.00 | 50.00 | 12.50 | 1.00 |

- **Maintainability (the doc's stated concern):** the table is **overridable via config**
  (`ChatBot:Pricing:<model-id>:{InputPerMillion,…}`), so prices can be updated without
  recompiling. Unknown models simply show tokens with no cost (no crash, no guess).
- **Money type:** `decimal` throughout; the four token categories are disjoint (uncached input
  vs cache-write vs cache-read), so a rate-weighted sum is exact with no double-counting.
- **UX:** per-turn cost + running session total on the token line, e.g.
  `[tokens: … | cost: $0.0123 (session $0.0456)]`; a session total on exit; a banner line with
  the active model's rates. Always on when the model's price is known (no toggle — YAGNI).

## Acceptance Criteria

1. `CostEstimator.Estimate(usage, pricing)` returns the exact USD cost (pure, unit-tested).
2. `ModelPrices.For(modelId, overrides?)` returns built-in rates, prefers config overrides
   (case-insensitive), and returns null for unknown models (unit-tested).
3. The console host shows per-turn and session cost when pricing is known, tokens-only otherwise.
4. README + feature-plan updated; handoff written; two `/code-review` passes.

## Out of Scope

- Per-conversation (vs per-process-session) cost breakdown.
- Live pricing fetch from an API.
