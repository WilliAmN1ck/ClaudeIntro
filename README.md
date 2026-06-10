# ClaudeIntro

A small multi-turn console chatbot built with **C# / .NET 10** and the official
[Anthropic .NET SDK](https://www.nuget.org/packages/Anthropic). It streams
Claude's replies token-by-token, persists conversations across runs, supports a
configurable system prompt, and offers context-window management via either a
message-count cap or beta server-side compaction.

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

Type messages at the `You:` prompt. The reply streams in under `Claude:`.

### In-chat commands

| Command         | Effect                                   |
| --------------- | ---------------------------------------- |
| `exit` / `quit` | End the session                          |
| `clear`         | Wipe the saved conversation history      |

## Configuration

Settings are loaded with the standard .NET configuration stack, in increasing
order of precedence:

1. **`appsettings.json`** (the `ChatBot` section) — checked-in defaults.
2. **Environment variables** — the config key with a `__` separator, e.g.
   `ChatBot__Model`.
3. **Command-line flags** — listed below.

> **Note:** `ANTHROPIC_API_KEY` is separate from this stack — it is always read
> directly from the environment (see Setup) and is never put in `appsettings.json`.

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

## Conversation persistence

Each exchange is saved to the history file (default
`%APPDATA%/ClaudeIntro/history.json`) as JSON. On startup, if a saved
conversation exists you are asked whether to resume it; the `clear` command (or
deleting the file) starts fresh.

## Project layout

| Path                                      | Purpose                                        |
| ----------------------------------------- | ---------------------------------------------- |
| `ClaudeIntro.slnx`                        | Solution file                                  |
| `src/ChatBot/Program.cs`                  | Entry point: builds config + DI, runs chat loop |
| `src/ChatBot/ChatOptions.cs`              | Strongly-typed settings (the `ChatBot` section) |
| `src/ChatBot/ServiceCollectionExtensions.cs` | `AddChatBot` DI registration                |
| `src/ChatBot/ConversationStore.cs`        | JSON load/save of conversation history         |
| `src/ChatBot/CompactionChat.cs`           | Beta server-side compaction backend            |
| `src/ChatBot/appsettings.json`            | Default configuration values                   |
| `docs/feature-plan.md`                    | Feature plan and implementation notes          |
