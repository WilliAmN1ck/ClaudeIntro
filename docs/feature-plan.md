# Feature Implementation Plan

This document plans three enhancements to the ChatBot console app:

1. **Streaming responses** — print Claude's reply token-by-token as it arrives.
2. **System prompt** — give the bot a configurable persona / instructions.
3. **Persisting conversation history** — save and reload conversations across runs.

Each feature is scoped so it can ship as an independent commit/PR.

---

## Status

All three original features below are **implemented**, plus the three follow-up
items (configurable model/tokens, prompt caching, context-window management).

`src/ChatBot/Program.cs` + `src/ChatBot/ConversationStore.cs` now:
- Read `ANTHROPIC_API_KEY` from the environment (exit with error if unset).
- Apply a configurable system prompt, cached for prefix reuse.
- Stream replies token-by-token.
- Persist/resume conversation history as JSON, with a `clear` command.
- Cap the messages sent per request (context-window management).
- Read model and max-tokens from env vars.

Default model: `claude-opus-4-8`.

---

## Feature 1: Streaming Responses

**Goal:** Replace the blocking `Messages.Create` call with `Messages.CreateStreaming` so text appears incrementally, like a real chat UI.

**Steps:**
1. Swap `await client.Messages.Create(params)` for `client.Messages.CreateStreaming(params)`.
2. Iterate the `RawMessageStreamEvent` async stream. On each event, use the `TryPickContentBlockDelta` → `delta.Delta.TryPickText` pattern to extract text deltas.
3. `Console.Write` each delta with no newline; flush as it arrives.
4. Accumulate the deltas into a `StringBuilder` so the full reply can still be appended to `history` for multi-turn context.
5. Write a trailing newline once the stream completes.

**Files:** `src/ChatBot/Program.cs`

**Verification:** Run the app; confirm the reply renders progressively rather than all at once, and that a follow-up question still has context (history captured correctly).

**Notes / risks:**
- Must still build the complete assistant message from deltas — don't lose history.
- Handle the case where a stream yields non-text blocks (ignore them for now).

---

## Feature 2: System Prompt

**Goal:** Let the bot have a persona / standing instructions, configurable without recompiling.

**Steps:**
1. Add a `System` property to `MessageCreateParams`. Start with a sensible default string (e.g. "You are a helpful, concise assistant.").
2. Make it overridable via an `ANTHROPIC_SYSTEM_PROMPT` environment variable, falling back to the default when unset.
3. (Optional) Support loading the prompt from a file path given by `ANTHROPIC_SYSTEM_PROMPT_FILE` for longer prompts.
4. Print the active persona at startup so the user knows the bot's mode.

**Files:** `src/ChatBot/Program.cs`

**Verification:** Set a distinctive system prompt (e.g. "Always answer in rhyming couplets") and confirm the bot's behavior changes.

**Notes / risks:**
- The system prompt is NOT part of the `messages` history — it's a top-level param sent on every request. Keep it stable for prompt caching benefits later.

---

## Feature 3: Persisting Conversation History

**Goal:** Save the conversation to disk and optionally resume it on the next run.

**Steps:**
1. Choose a storage location — a JSON file under the user's app-data dir (e.g. `%APPDATA%/ClaudeIntro/history.json`) or a path-configurable location.
2. Define a serializable record for a stored turn (`role` + `text`). Avoid serializing the SDK's `MessageParam` union directly — map to/from a simple DTO.
3. On startup: if a history file exists, prompt the user to resume or start fresh. If resuming, deserialize and rebuild the `List<MessageParam>`.
4. After each assistant reply (and on exit), serialize the current history to the file via `System.Text.Json`.
5. Add a `clear` command to wipe the stored history.

**Files:** `src/ChatBot/Program.cs` (plus possibly a small `ConversationStore.cs` helper).

**Verification:** Have a multi-turn conversation, exit, restart, resume — confirm the bot remembers earlier turns. Test the `clear` command.

**Notes / risks:**
- Long histories grow the token cost of every request. A future enhancement could trim or summarize old turns (out of scope here).
- Handle corrupt/missing files gracefully (start fresh rather than crash).

---

## Suggested Order

1. **System prompt** — smallest, isolated change; good warm-up.
2. **Streaming** — changes the core request loop.
3. **Persistence** — builds on the stable history representation; largest surface area.

## Follow-up Features (implemented)

### Configurable model / max tokens
- `ANTHROPIC_MODEL` (default `claude-opus-4-8`) and `ANTHROPIC_MAX_TOKENS` (default 4096) env vars.
- The `Model` param accepts the raw string directly (SDK implicit conversion).
- Active config is printed in the startup banner.

### Prompt caching
- The system prompt is sent as a `List<TextBlockParam>` with `CacheControlEphemeral`, so its prefix is cached and reused across requests.
- Caveat: a prefix only caches once it exceeds the model's minimum (~4096 tokens for Opus), so a short persona is a harmless no-op until the prompt grows.

### Context-window management
- `ANTHROPIC_MAX_HISTORY_MESSAGES` (default 40, `0` = unlimited) caps how many recent messages are sent per request.
- Trimming preserves the API rule that the first message must be from the user (leading assistant messages are dropped).
- Full history is still persisted to disk; only the per-request view is trimmed.

### CLI flags
- `--model`, `--max-tokens`, `--max-history`, `--system`, `--system-file`, `--history`, `--compaction`, and `-h/--help`.
- `--help` prints usage and exits before the API-key check.

### Server-side compaction (beta)
- `--compaction` (or `ChatBot__Compaction=true`) switches from the count-based trim to the API's `compact-2026-01-12` context management, which summarizes old turns server-side.
- Implemented in `src/ChatBot/CompactionChat.cs` against `client.Beta.Messages`; it round-trips full response content (preserving compaction blocks) each turn.
- This mode is non-streaming (the beta path used here returns a complete message); the default streaming mode is unchanged. Requires a compaction-capable model (Opus 4.6+/Sonnet 4.6).

## Toward a reusable library

First step: **dependency injection + configuration** (foundation for packaging the chat engine for reuse).
- `Microsoft.Extensions.Hosting` brings the config/DI/options stack.
- `ChatOptions` (the `ChatBot` config section) is the strongly-typed settings surface.
- `ServiceCollectionExtensions.AddChatBot(IServiceCollection, IConfiguration)` registers options + `AnthropicClient` — the idiomatic consumer entry point.
- Settings load from `appsettings.json` → environment variables (`ChatBot__*`) → CLI flags (each overrides the previous). `ANTHROPIC_API_KEY` is read directly from the environment.

### Console-agnostic engine (foundation)
- `IChatService` is the reusable engine: `SendAsync` returns `IAsyncEnumerable<string>` (streamed text) plus a `CancellationToken`, and exposes `History`/`Model`/`MaxTokens`/`SystemPrompt`/`Clear()`. No `Console` calls inside.
- `StreamingChatService` and `CompactionChatService` implement it; `ChatServiceFactory` (registered via `AddChatBot`) picks one from options and resolves the system prompt.
- `Program.cs` is now a thin host: it owns the resume prompt, I/O, and persistence; the engine owns conversation state.

### Pluggable persistence
- `IConversationStore` abstracts persistence; `FileConversationStore` is the JSON-file implementation, registered via `AddChatBot` (path from `ChatOptions.HistoryPath`).
- The engine owns load/save/clear through the store; the host only drives the resume prompt. Swapping in SQLite/DB is now a new implementation + DI registration.

### Production concerns
- **Logging:** `ILogger` via DI (the host configures providers from the `Logging` section, written to stderr so it never mixes with the reply); the file store logs failures instead of writing to `Console.Error`.
- **Token usage:** `IChatService.LastTurnUsage` (`TokenUsage`) is captured from the stream/response and printed per turn.
- **Cancellation:** `SendAsync` honors a `CancellationToken`; Ctrl-C cancels the in-progress reply (per-turn `CancellationTokenSource`) without exiting the app.
- **Graceful errors:** API exceptions are caught in the host loop and reported inline (`[error] …`) rather than crashing.
- **Tests:** `tests/ChatBot.Tests` (xUnit) covers `HistoryTrimmer`, system-prompt resolution, `FileConversationStore`, and engine option-clamping/seeding. Trimming was extracted to `HistoryTrimmer` and turns are committed only on a successful turn, both of which made the engine testable.

### Library split
- The engine moved to a class library, `src/ChatBot.Core`, with a minimal dependency surface (Anthropic SDK + Microsoft.Extensions DI/Options/Configuration/Logging *abstractions*). `src/ChatBot` is now just the console host (`Program.cs` + `appsettings.json`) referencing it; `tests/ChatBot.Tests` references the library.
- `AddChatBot` no longer configures logging — the consumer supplies logging providers. The console host configures the stderr console logger itself. This makes the library safe to drop into any host (web/GUI/service).

### Tool use / function calling
- `IChatTool` (name, description, JSON-schema `Properties`/`Required`, `ExecuteAsync`) is the consumer-implementable tool contract; `ToolSchema` provides parameter helpers. Tools are registered in DI and injected into the engine.
- `StreamingChatService` runs the agentic loop: stream text → on `stop_reason == tool_use`, execute each tool and send `tool_result` back → repeat until `end_turn`. Tool failures return an error string instead of crashing; usage is summed across loop iterations.
- Tool-use/tool-result turns are in-call only; persistence stays text-only. Tool use is streaming-mode only (compaction logs a warning and ignores tools).
- Sample tools shipped in Core: `CurrentTimeTool`, `RollDiceTool` (registered by the host).

### PostgreSQL store (opt-in)
- `PostgresConversationStore : IConversationStore` (Npgsql, synchronous APIs — no engine change) stores turns as a `jsonb` row keyed by `ChatOptions.ConversationId`; the table is created on first use.
- Selected via `ChatOptions.Store = "postgres"` with `PostgresConnectionString`; `AddChatBot` registers the chosen backend. File store remains the default.
- Config/connection errors surface as a clear startup error (`InvalidOperationException`, caught by the host); transient runtime errors are logged and degrade.
- Verified by a `[SkippableFact]` integration test that runs against `CHATBOT_TEST_POSTGRES` (skipped when unset); `docker-compose.yml` provides a local Postgres.

## Out of Scope (future)

- Dollar-cost computation (needs a maintained price table); token counts are exposed so callers can derive it.
- Tool use within compaction mode.
- Async `IConversationStore` surface (for high-concurrency hosts) and multi-conversation management beyond the single `ConversationId` key.
