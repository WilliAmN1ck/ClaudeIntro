# ClaudeIntro

[![CI](https://github.com/WilliAmN1ck/ClaudeIntro/actions/workflows/ci.yml/badge.svg)](https://github.com/WilliAmN1ck/ClaudeIntro/actions/workflows/ci.yml)

A small multi-turn console chatbot built with **C# / .NET 10** and the official
[Anthropic .NET SDK](https://www.nuget.org/packages/Anthropic). It streams
Claude's replies token-by-token, persists multiple named conversations across runs
(switch between them with slash commands), supports a configurable system prompt,
runs **tools** the model can call, and offers context-window management via either a
message-count cap or beta server-side compaction. The engine lives in a reusable
`ChatBot.Core` library.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- An Anthropic API key (starts with `sk-ant-`) from the
  [Anthropic Console](https://console.anthropic.com) → **API Keys**

## Setup

### 1. Set your API key

The app reads the key from the `ANTHROPIC_API_KEY` environment variable and exits
with an error if it is not set.

```powershell
# PowerShell — current session only
$env:ANTHROPIC_API_KEY = "sk-ant-..."
```

To persist it across new shells (Windows, user scope):

```powershell
[Environment]::SetEnvironmentVariable("ANTHROPIC_API_KEY", "sk-ant-...", "User")
```

### 2. NuGet restore note (path with spaces)

This repo lives under a path that contains spaces, which breaks NuGet's default
package resolution. Point NuGet at a space-free cache directory before any
`dotnet` command:

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
```

## Running

```powershell
dotnet run --project src/ChatBot
```

Type messages at the `You:` prompt. The reply streams in under `Claude:`, and a
`[tokens: … | cost: $… (session $…)]` line reports token usage and the USD cost
after each turn (cost is shown when the model's price is known; see
[Cost reporting](#cost-reporting)). Press **Ctrl-C** while a reply is
streaming to cancel just that reply (the app keeps running); press it at the prompt
to exit. API errors are reported inline (`[error] …`) without crashing.

### In-chat commands

Conversation-management commands use a leading `/` so they never collide with a
message you actually want to send. Type `/help` while running to see them.

| Command            | Effect                                                        |
| ------------------ | ------------------------------------------------------------- |
| `/list` (`/ls`)    | List conversations, newest first (`*` marks the active one)   |
| `/new [title]`     | Create and switch to a new conversation                       |
| `/switch <id>`     | Switch to an existing conversation (alias `/use`)             |
| `/rename <title>`  | Rename the active conversation (its id is unchanged)          |
| `/delete [id]`     | Delete a conversation (defaults to the active one; confirms)  |
| `/clear` / `clear` | Empty the active conversation's history (keeps the conversation) |
| `/help` (`/?`)     | Show the command list                                         |
| `exit` / `quit`    | End the session                                               |
| `Ctrl-C`           | Cancel the streaming reply (or exit at the prompt)            |

Conversation ids are short slugs derived from the title (e.g. `Trip Planning` →
`trip-planning`), so they're easy to type after `/switch`.

## Configuration

Settings are loaded with the standard .NET configuration stack, in increasing
order of precedence:

1. **`appsettings.json`** (the `ChatBot` section) — checked-in defaults.
2. **Environment variables** — the config key with a `__` separator, e.g.
   `ChatBot__Model`.
3. **Command-line flags** — listed below.

> **Note:** `ANTHROPIC_API_KEY` is separate from this stack — it is always read
> directly from the environment (see Setup) and is never put in `appsettings.json`.

Logging is configured from the `Logging` section (standard .NET levels) and writes
to **stderr**, so it never interleaves with the streamed reply on stdout. The default
level is `Warning`, keeping normal runs quiet; raise it (e.g. `ChatBot`/`Default` to
`Information`) to see per-turn request/usage logs.

| Flag              | Argument | Config key / env var          | Default              | Description |
| ----------------- | -------- | ----------------------------- | -------------------- | ----------- |
| `--model`         | `<id>`   | `ChatBot__Model`              | `claude-opus-4-8`    | Model id (e.g. `claude-opus-4-8`, `claude-sonnet-4-6`, `claude-haiku-4-5`). |
| `--max-tokens`    | `<n>`    | `ChatBot__MaxTokens`          | `4096`               | Max output tokens per reply. |
| `--max-history`   | `<n>`    | `ChatBot__MaxHistoryMessages` | `40`                 | Cap on recent messages sent per request. `0` = unlimited. Ignored when compaction is on. |
| `--system`        | `<text>` | `ChatBot__SystemPrompt`       | helpful-assistant    | System prompt text (the bot's persona/instructions). |
| `--system-file`   | `<path>` | `ChatBot__SystemPromptFile`   | —                    | Read the system prompt from a file (useful for long prompts). |
| `--history`       | `<path>` | `ChatBot__HistoryPath`        | `%APPDATA%/ClaudeIntro/history.json` | Base path for the file store; conversations live in a `conversations/` dir beside it. |
| `--store`         | `<name>` | `ChatBot__Store`              | `file`               | Conversation store backend: `file` or `postgres`. |
| `--conversation`  | `<id>`   | `ChatBot__ConversationId`     | `default`            | Conversation to open at startup (both stores). Created if it doesn't exist. |
| *(n/a)*           | —        | `ChatBot__PostgresConnectionString` | —              | Npgsql connection string; required when `--store postgres`. |
| `--compaction`    | *(flag)* | `ChatBot__Compaction`         | `false`              | Use beta server-side compaction (summarizes old turns) instead of the count-based trim. **Non-streaming**; requires a compaction-capable model (Opus 4.6+/Sonnet 4.6). |
| `-h`, `--help`    | *(flag)* | —                             | —                    | Print usage and exit. |

Pass flags after `--` so `dotnet` forwards them to the app. Flags accept either
`--flag value` or `--flag=value`. The system prompt resolves as: `SystemPrompt`
(inline) → `SystemPromptFile` (file contents) → default.

### Examples

```powershell
# Faster, cheaper model with a shorter reply cap
dotnet run --project src/ChatBot -- --model claude-haiku-4-5 --max-tokens 1024

# Give the bot a persona and send the full history every request
dotnet run --project src/ChatBot -- --system "Talk like a pirate." --max-history 0

# Long system prompt from a file, and a custom history location
dotnet run --project src/ChatBot -- --system-file .\prompt.txt --history .\chat.json

# Server-side compaction for very long conversations
dotnet run --project src/ChatBot -- --compaction

# Show all options
dotnet run --project src/ChatBot -- --help
```

### Cost reporting

Each turn prints its USD cost beside the token counts, plus a running session total,
and the session total again on exit. The active model's rates appear in the startup
banner. Costs use a built-in per-model price table (Anthropic list prices, USD per
million tokens; cache **write** at 1.25× and **read** at 0.1× of the input rate, matching
the ephemeral cache the engine uses). Unknown models show tokens only.

Override or extend the table via the `ChatBot:Pricing` config section (keyed by model id),
so rates can be updated without recompiling:

```jsonc
// appsettings.json
"ChatBot": {
  "Pricing": {
    "claude-opus-4-8": {
      "InputPerMillion": 5.00, "OutputPerMillion": 25.00,
      "CacheWritePerMillion": 6.25, "CacheReadPerMillion": 0.50
    }
  }
}
```

Equivalent environment variable: `ChatBot__Pricing__claude-opus-4-8__InputPerMillion=5.00`.

An override replaces **all four** rates for that model, so specify every field — an omitted
rate is treated as `0`.

## Tools

The model can call **tools** (functions) mid-conversation. The default streaming
engine runs an agentic loop: it streams text, and if Claude requests a tool it
executes the tool, returns the result, and continues until done. A `[tool: name]`
line marks each call. (Tool use is **not** available in `--compaction` mode.)

Two sample tools ship in `ChatBot.Core` and are registered by the console host:
`get_current_time` and `roll_dice`.

Add your own by implementing `IChatTool` and registering it before `AddChatBot`:

```csharp
public sealed class GetWeatherTool : IChatTool
{
    public string Name => "get_weather";
    public string Description => "Get the current weather for a city.";
    public IReadOnlyDictionary<string, JsonElement> Properties => new Dictionary<string, JsonElement>
    {
        ["city"] = ToolSchema.String("City name, e.g. Paris"),
    };
    public IReadOnlyList<string> Required => new[] { "city" };

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> args, CancellationToken ct)
        => Task.FromResult($"It's sunny in {args["city"].GetString()}.");
}

// registration
services.AddSingleton<IChatTool, GetWeatherTool>();
```

`ToolSchema` provides `String`/`Integer`/`Number`/`Boolean` helpers for parameter
definitions. Tool failures are returned to the model as an error string rather than
crashing the turn.

## Conversation persistence

Persistence is behind the `IConversationStore` abstraction, which manages **multiple
named conversations** — each with an id, a title, created/updated timestamps, and its
turns. Two backends ship:

- **File (default)** — one JSON document per conversation under
  `%APPDATA%/ClaudeIntro/conversations/<id>.json`. Zero setup.
- **PostgreSQL** — opt-in with `--store postgres` plus `ChatBot__PostgresConnectionString`.
  Each conversation is a `jsonb` row in the `conversations` table, created/upgraded
  automatically on first use.

On startup the app opens the conversation named by `--conversation` (default `default`),
creating it if needed. Use the slash commands above to list, create, switch, rename, and
delete conversations during a session.

**Upgrading from a single history:** the first time the new file store runs, a
pre-existing `%APPDATA%/ClaudeIntro/history.json` is migrated into a conversation named
`default`, so your existing chat is preserved and selectable.

```powershell
# Run against a local Postgres (see docker-compose.yml)
docker compose up -d
$env:ChatBot__PostgresConnectionString = "Host=localhost;Username=chatbot;Password=chatbot;Database=chatbot"
dotnet run --project src/ChatBot -- --store postgres
```

Add another backend (SQLite, a cloud DB, …) by implementing `IConversationStore`
and registering it.

## Project layout

The engine lives in a reusable class library (`ChatBot.Core`); `ChatBot` is a thin
console host that references it.

| Path                                          | Purpose                                        |
| --------------------------------------------- | ---------------------------------------------- |
| `ClaudeIntro.slnx`                            | Solution file                                  |
| `src/ChatBot/` (host)                         | Console app: builds config + DI + logging, drives the engine |
| &nbsp;&nbsp;`Program.cs`                       | Entry point and chat loop                      |
| &nbsp;&nbsp;`appsettings.json`                | Default configuration values                   |
| `src/ChatBot.Core/` (library)                 | The reusable chat engine                       |
| &nbsp;&nbsp;`IChatService.cs`                 | Console-agnostic chat engine interface         |
| &nbsp;&nbsp;`StreamingChatService.cs`         | Streaming engine (token-by-token + count-based trim + tool loop) |
| &nbsp;&nbsp;`CompactionChatService.cs`        | Beta server-side compaction engine             |
| &nbsp;&nbsp;`IChatCompletionClient.cs`        | Seam over the streaming API (makes the loop testable) |
| &nbsp;&nbsp;`AnthropicCompletionClient.cs`    | Real `IChatCompletionClient` (wraps the SDK)   |
| &nbsp;&nbsp;`ChatServiceFactory.cs`           | Picks the engine from options; resolves the system prompt |
| &nbsp;&nbsp;`IChatTool.cs` / `ToolSchema.cs`  | Tool abstraction + schema helpers              |
| &nbsp;&nbsp;`ToolInvoker.cs`                   | Tool dispatch + SDK tool-definition building (unit-tested) |
| &nbsp;&nbsp;`Tools/`                          | Sample tools (`CurrentTimeTool`, `RollDiceTool`) |
| &nbsp;&nbsp;`HistoryTrimmer.cs`               | Pure context-window trim (unit-tested)         |
| &nbsp;&nbsp;`TokenUsage.cs`                    | Per-turn token counts                          |
| &nbsp;&nbsp;`ChatOptions.cs`                  | Strongly-typed settings (the `ChatBot` section) |
| &nbsp;&nbsp;`ServiceCollectionExtensions.cs`  | `AddChatBot` DI registration                   |
| &nbsp;&nbsp;`StoredTurn.cs`                    | One persisted conversation turn (role + text)  |
| &nbsp;&nbsp;`ConversationInfo.cs`             | Per-conversation metadata (id, title, timestamps, turn count) |
| &nbsp;&nbsp;`ConversationSlug.cs`             | Derives stable, typeable ids from titles       |
| &nbsp;&nbsp;`ChatCommand.cs` / `ChatCommandParser.cs` | Parse a console line into a send-or-manage intent (unit-tested) |
| &nbsp;&nbsp;`IConversationStore.cs`           | Multi-conversation persistence abstraction     |
| &nbsp;&nbsp;`FileConversationStore.cs`        | JSON document-per-conversation store (+ legacy migration) |
| &nbsp;&nbsp;`PostgresConversationStore.cs`    | PostgreSQL (`jsonb`) multi-conversation store  |
| `tests/ChatBot.Tests/`                        | xUnit unit tests (reference `ChatBot.Core`)    |
| `docker-compose.yml`                          | Local PostgreSQL for the opt-in store          |
| `docs/feature-plan.md`                        | Feature plan and implementation notes          |

## Tests

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
dotnet test
```

The tests cover the network-independent logic — history trimming, system-prompt
resolution, the file store (round-trips, **legacy migration**, path-safe ids, corrupt
files), conversation-id slugging, **console-command parsing**, sample tools, tool
dispatch, the **agentic tool loop** (via a scripted `IChatCompletionClient` fake), and
engine option clamping, history seeding, and per-conversation persistence.

The PostgreSQL round-trip test is an **integration test** that runs only when a
connection string is provided, and is skipped otherwise:

```powershell
docker compose up -d
$env:CHATBOT_TEST_POSTGRES = "Host=localhost;Username=chatbot;Password=chatbot;Database=chatbot"
dotnet test
```

## Reusing the engine

Another app (web API, GUI, service) can consume `ChatBot.Core` directly. Register
the services and create an engine:

```csharp
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());   // the host supplies logging
services.AddChatBot(configuration);          // binds ChatOptions, registers the client/store/factory
var provider = services.BuildServiceProvider();

// Open (or create) a conversation by id, then chat. List/create/delete via the store.
IChatService chat = await provider.GetRequiredService<IChatServiceFactory>().CreateAsync("default");
await foreach (string delta in chat.SendAsync("Hello"))
    Console.Write(delta);
```

`AddChatBot` deliberately does **not** configure logging — the consumer brings its
own providers. Swap persistence by registering a different `IConversationStore`
before `AddChatBot` (or after, to override); the store is fully async
(`LoadAsync`/`SaveAsync`/…), so a database backend never blocks the calling thread.
