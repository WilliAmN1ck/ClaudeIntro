using Anthropic;
using Anthropic.Models.Messages;
using ChatBot;

// CLI flags override env vars, which override built-in defaults.
Dictionary<string, string?> cli = ParseArgs(args);

if (cli.ContainsKey("help") || cli.ContainsKey("h"))
{
    PrintUsage();
    return 0;
}

string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Error: ANTHROPIC_API_KEY environment variable is not set.");
    return 1;
}

AnthropicClient client = new() { ApiKey = apiKey };

const string defaultSystemPrompt = "You are a helpful, concise assistant.";
string systemPrompt = ResolveSystemPrompt(defaultSystemPrompt, cli);

// Configurable model and output cap. Precedence: CLI flag > env var > default.
string modelId = Setting(cli, "model", "ANTHROPIC_MODEL") ?? "claude-opus-4-8";
long maxTokens = SettingLong(cli, "max-tokens", "ANTHROPIC_MAX_TOKENS", 4096, min: 1);

// Context-window management: cap how many recent messages are sent per request.
// 0 means "no cap — send the whole history".
int maxHistoryMessages = (int)SettingLong(cli, "max-history", "ANTHROPIC_MAX_HISTORY_MESSAGES", 40, min: 0);

// Cache the system prompt so repeated requests reuse its prefix (cheaper, faster).
var systemBlocks = new List<TextBlockParam>
{
    new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
};

string historyPath = Setting(cli, "history", "ANTHROPIC_HISTORY_FILE")
                     ?? ConversationStore.DefaultPath;

// `turns` is the persistence source of truth; `history` is the SDK view sent on each request.
var turns = new List<StoredTurn>();

if (ConversationStore.Exists(historyPath))
{
    Console.Write($"Found a saved conversation at {historyPath}. Resume it? [Y/n] ");
    string? answer = Console.ReadLine();
    if (answer is null || !answer.Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
    {
        turns = ConversationStore.Load(historyPath);
        Console.WriteLine($"Resumed {turns.Count} turn(s).");
    }
    else
    {
        ConversationStore.Clear(historyPath);
        Console.WriteLine("Started a fresh conversation.");
    }
}

var history = turns.Select(t => t.ToMessage()).ToList();

Console.WriteLine("Claude Chatbot — type 'exit'/'quit' to stop, 'clear' to wipe saved history.");
Console.WriteLine($"Model: {modelId}  |  MaxTokens: {maxTokens}  |  History cap: " +
                  (maxHistoryMessages > 0 ? $"{maxHistoryMessages} msgs" : "unlimited"));
Console.WriteLine($"Persona: {systemPrompt}");
Console.WriteLine(new string('-', 50));

while (true)
{
    Console.Write("\nYou: ");
    string? input = Console.ReadLine();

    if (input is null || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase)
                      || input.Trim().Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    if (string.IsNullOrWhiteSpace(input))
        continue;

    if (input.Trim().Equals("clear", StringComparison.OrdinalIgnoreCase))
    {
        ConversationStore.Clear(historyPath);
        history.Clear();
        turns.Clear();
        Console.WriteLine("Conversation history cleared.");
        continue;
    }

    string userText = input.Trim();
    history.Add(new MessageParam { Role = Role.User, Content = userText });
    turns.Add(new StoredTurn("user", userText));

    var parameters = new MessageCreateParams
    {
        Model = modelId,
        MaxTokens = maxTokens,
        System = systemBlocks,
        Messages = TrimHistory(history, maxHistoryMessages),
    };

    Console.Write("\nClaude: ");

    var reply = new System.Text.StringBuilder();
    await foreach (RawMessageStreamEvent streamEvent in client.Messages.CreateStreaming(parameters))
    {
        if (streamEvent.TryPickContentBlockDelta(out var delta) &&
            delta.Delta.TryPickText(out var text))
        {
            Console.Write(text.Text);
            reply.Append(text.Text);
        }
    }
    Console.WriteLine();

    history.Add(new MessageParam { Role = Role.Assistant, Content = reply.ToString() });
    turns.Add(new StoredTurn("assistant", reply.ToString()));

    ConversationStore.Save(historyPath, turns);
}

Console.WriteLine("\nGoodbye!");
return 0;

// Resolves the system prompt with the following precedence:
//   1. --system <text>             — inline prompt on the command line
//   2. --system-file <path>        — file path on the command line
//   3. ANTHROPIC_SYSTEM_PROMPT_FILE — path to a file holding the prompt
//   4. ANTHROPIC_SYSTEM_PROMPT      — the prompt text directly
//   5. the supplied default
static string ResolveSystemPrompt(string fallback, Dictionary<string, string?> cli)
{
    if (cli.TryGetValue("system", out string? cliInline) && !string.IsNullOrWhiteSpace(cliInline))
        return cliInline.Trim();

    string? cliFile = cli.TryGetValue("system-file", out string? cf) ? cf : null;
    string? envFile = Environment.GetEnvironmentVariable("ANTHROPIC_SYSTEM_PROMPT_FILE");
    foreach (string? file in new[] { cliFile, envFile })
    {
        if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
        {
            string fromFile = File.ReadAllText(file).Trim();
            if (!string.IsNullOrWhiteSpace(fromFile))
                return fromFile;
        }
    }

    string? inline = Environment.GetEnvironmentVariable("ANTHROPIC_SYSTEM_PROMPT");
    if (!string.IsNullOrWhiteSpace(inline))
        return inline.Trim();

    return fallback;
}

// Returns a string setting by CLI flag, then env var, else null (trimmed, non-empty).
static string? Setting(Dictionary<string, string?> cli, string flag, string envVar)
{
    if (cli.TryGetValue(flag, out string? v) && !string.IsNullOrWhiteSpace(v))
        return v.Trim();
    string? env = Environment.GetEnvironmentVariable(envVar);
    return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
}

// Returns a long setting (>= min) by CLI flag, then env var, else the fallback.
static long SettingLong(Dictionary<string, string?> cli, string flag, string envVar, long fallback, long min)
{
    string? raw = Setting(cli, flag, envVar);
    return long.TryParse(raw, out long value) && value >= min ? value : fallback;
}

// Parses "--key value", "--key=value", and bare "--flag" (value null) into a map.
// Keys are lowercased and stripped of leading dashes.
static Dictionary<string, string?> ParseArgs(string[] args)
{
    var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < args.Length; i++)
    {
        string arg = args[i];
        if (!arg.StartsWith('-'))
            continue;

        string key = arg.TrimStart('-');
        int eq = key.IndexOf('=');
        if (eq >= 0)
        {
            map[key[..eq]] = key[(eq + 1)..];
        }
        else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
        {
            map[key] = args[++i];
        }
        else
        {
            map[key] = null; // bare flag
        }
    }
    return map;
}

static void PrintUsage()
{
    Console.WriteLine("""
        ChatBot — a Claude console chatbot.

        Usage: dotnet run --project src/ChatBot -- [options]

        Options (CLI flags override env vars, which override defaults):
          --model <id>           Model id            (env ANTHROPIC_MODEL, default claude-opus-4-8)
          --max-tokens <n>       Max output tokens   (env ANTHROPIC_MAX_TOKENS, default 4096)
          --max-history <n>      Recent-message cap  (env ANTHROPIC_MAX_HISTORY_MESSAGES, default 40; 0 = unlimited)
          --system <text>        System prompt text  (env ANTHROPIC_SYSTEM_PROMPT)
          --system-file <path>   System prompt file  (env ANTHROPIC_SYSTEM_PROMPT_FILE)
          --history <path>       History file path   (env ANTHROPIC_HISTORY_FILE)
          -h, --help             Show this help and exit

        Requires the ANTHROPIC_API_KEY environment variable.
        In-chat commands: 'exit'/'quit' to stop, 'clear' to wipe saved history.
        """);
}

// Returns the most recent `max` messages while ensuring the result still starts
// with a user message (the API requires the first message to be from the user).
// `max <= 0` means no trimming. The full history/persistence is left untouched.
static List<MessageParam> TrimHistory(List<MessageParam> history, int max)
{
    if (max <= 0 || history.Count <= max)
        return history;

    int start = history.Count - max;
    while (start < history.Count && history[start].Role == Role.Assistant)
        start++;

    return history.GetRange(start, history.Count - start);
}
