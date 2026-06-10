# ClaudeIntro

A small multi-turn console chatbot built with **C# / .NET 10** and the official
[Anthropic .NET SDK](https://www.nuget.org/packages/Anthropic). It streams
Claude's replies token-by-token, persists conversations across runs, supports a
configurable system prompt, runs **tools** the model can call, and offers
context-window management via either a message-count cap or beta server-side
compaction. The engine lives in a reusable `ChatBot.Core` library.

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
`[tokens: …]` line reports usage after each turn. Press **Ctrl-C** while a reply is
streaming to cancel just that reply (the app keeps running); press it at the prompt
to exit. API errors are reported inline (`[error] …`) without crashing.

### In-chat commands

| Command         | Effect                                   |
| --------------- | ---------------------------------------- |
| `exit` / `quit` | End the session                          |
| `clear`         | Wipe the saved conversation history      |
| `Ctrl-C`        | Cancel the streaming reply (or exit at the prompt) |

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
| `--history`       | `<path>` | `ChatBot__HistoryPath`        | `%APPDATA%/ClaudeIntro/history.json` | Where the conversation is saved/loaded. |
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

Each exchange is saved to the history file (default
`%APPDATA%/ClaudeIntro/history.json`) as JSON. On startup, if a saved
conversation exists you are asked whether to resume it; the `clear` command (or
deleting the file) starts fresh.

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
| &nbsp;&nbsp;`StreamingChatService.cs`         | Streaming engine (token-by-token + count-based trim) |
| &nbsp;&nbsp;`CompactionChatService.cs`        | Beta server-side compaction engine             |
| &nbsp;&nbsp;`ChatServiceFactory.cs`           | Picks the engine from options; resolves the system prompt |
| &nbsp;&nbsp;`IChatTool.cs` / `ToolSchema.cs`  | Tool abstraction + schema helpers              |
| &nbsp;&nbsp;`Tools/`                          | Sample tools (`CurrentTimeTool`, `RollDiceTool`) |
| &nbsp;&nbsp;`HistoryTrimmer.cs`               | Pure context-window trim (unit-tested)         |
| &nbsp;&nbsp;`TokenUsage.cs`                    | Per-turn token counts                          |
| &nbsp;&nbsp;`ChatOptions.cs`                  | Strongly-typed settings (the `ChatBot` section) |
| &nbsp;&nbsp;`ServiceCollectionExtensions.cs`  | `AddChatBot` DI registration                   |
| &nbsp;&nbsp;`StoredTurn.cs`                    | One persisted conversation turn (role + text)  |
| &nbsp;&nbsp;`IConversationStore.cs`           | Persistence abstraction (file/SQLite/DB)       |
| &nbsp;&nbsp;`FileConversationStore.cs`        | JSON-file implementation of the store          |
| `tests/ChatBot.Tests/`                        | xUnit unit tests (reference `ChatBot.Core`)    |
| `docs/feature-plan.md`                        | Feature plan and implementation notes          |

## Tests

```powershell
$env:NUGET_PACKAGES = "C:\NuGetPackages"
dotnet test
```

The tests cover the network-independent logic — history trimming, system-prompt
resolution, the file store (round-trip/corrupt/clear), and engine option clamping
and history seeding.

## Reusing the engine

Another app (web API, GUI, service) can consume `ChatBot.Core` directly. Register
the services and create an engine:

```csharp
var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole());   // the host supplies logging
services.AddChatBot(configuration);          // binds ChatOptions, registers the client/store/factory
var provider = services.BuildServiceProvider();

IChatService chat = provider.GetRequiredService<IChatServiceFactory>().Create();
await foreach (string delta in chat.SendAsync("Hello"))
    Console.Write(delta);
```

`AddChatBot` deliberately does **not** configure logging — the consumer brings its
own providers. Swap persistence by registering a different `IConversationStore`
before `AddChatBot` (or after, to override).
