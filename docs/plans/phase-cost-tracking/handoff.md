# Phase: Dollar-Cost Computation — Handoff

- **Date:** 2026-06-18
- **Branch:** `feature/cost-tracking` (stacked on `feature/multi-conversation`)
- **Plan:** `docs/plans/phase-cost-tracking/spec.md`
- **Status:** Complete. Build + 91 tests green (1 Postgres integration skipped); banner + override smoke-tested; two `/code-review` passes done (pass 1 fixed, pass 2 clean).

## What Was Built

- `ModelPricing` (Core) — per-model rates (USD per million tokens), `init`-only so the built-in table is immutable while still binding from config.
- `ModelPrices` (Core) — built-in price table for Opus 4.8/4.7/4.6, Sonnet 4.6, Haiku 4.5, Fable 5 (Anthropic list prices as of 2026-06; cache write/read at 1.25×/0.1× of input). `For(modelId, overrides?)` prefers config overrides (case-insensitive), returns null for unknown models.
- `CostEstimator.Estimate(usage, pricing)` (Core) — pure, rate-weighted sum over the four disjoint token categories.
- `ChatOptions.Pricing` — `Dictionary<string, ModelPricing>?` bound from the `ChatBot:Pricing` config section (overrides the built-in table).
- `Program.cs` — prints the active model's rates in the banner, each turn's cost + a running session total on the token line, and the session total on exit. USD amounts use `InvariantCulture` (`Money()`/`Rate()` helpers). Unknown models degrade to tokens-only.

## What Changed From the Spec

- No behavioral deviations. The price table is overridable (the spec's answer to the "maintained price table" concern); an override is a **full replacement** of all four rates for a model (decimal can't distinguish unset from 0) — documented in the README.

## What the Next Phase Needs to Know

- Cost is process-session scoped (not per-conversation); it accumulates across `/switch`. Per-conversation cost would need the engine to attribute usage per conversation.
- The price table is static (config-overridable). A live-pricing fetch is out of scope.
- The four `TokenUsage` categories are disjoint (uncached input, output, cache write, cache read) per Anthropic's caching model, so the rate-weighted sum is exact.

## Files Changed

| File | Change | Notes |
| --- | --- | --- |
| `ModelPricing.cs`, `ModelPrices.cs`, `CostEstimator.cs` | new | Pure pricing/cost (Core) |
| `ChatOptions.cs` | edit | `Pricing` override dictionary |
| `src/ChatBot/Program.cs` | edit | Banner rates + per-turn/session cost (invariant-culture) |
| `CostEstimatorTests.cs`, `ModelPricesTests.cs` | new | 14 unit tests |
| `README.md`, `docs/feature-plan.md` | edit | Docs |

## Test Coverage

- **91 passing, 1 skipped** (`dotnet test`). 14 cost tests: estimate math (all categories, scaling, cache write/read distinct, zero), price lookup (known/unknown/blank/case-insensitive), override precedence + override-doesn't-mutate-builtin.
- Manual smoke: banner pricing line (default and `ChatBot__Pricing__…` override) verified live with a dummy key — no API calls.

## Code Review (two passes, max effort)

- **Pass 1 fixes:** USD amounts formatted with `InvariantCulture` (a comma-decimal locale previously rendered `$0,0512`); `ModelPricing` made `init`-only to prevent mutation of the shared built-in table; named cache args in the estimator test; regression test for override-not-mutating-builtin; documented full-replacement override semantics.
- **Pass 2:** clean — fixes confirmed complete, no new defects.

## Known Issues / Tech Debt

- A price override must specify all four rates (an omitted rate is `0`). Documented; merging-with-builtin is intentionally avoided because `0` is a valid rate.
- Pricing data is a point-in-time snapshot (2026-06); update the built-in table or use the config override when Anthropic prices change.

## Verification Commands

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
dotnet build; dotnet test          # 91 passed, 1 skipped
dotnet run --project src/ChatBot    # banner shows the model's rates; each turn prints cost + session total
```
