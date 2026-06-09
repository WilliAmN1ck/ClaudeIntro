using Anthropic;
using Anthropic.Models.Messages;
using ChatBot;

string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Error: ANTHROPIC_API_KEY environment variable is not set.");
    return 1;
}

AnthropicClient client = new() { ApiKey = apiKey };

const string defaultSystemPrompt = "You are a helpful, concise assistant.";
string systemPrompt = ResolveSystemPrompt(defaultSystemPrompt);

// Configurable model and output cap (env-overridable, with sensible defaults).
string modelId = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")?.Trim() is { Length: > 0 } envModel
    ? envModel
    : "claude-opus-4-8";
long maxTokens = ParseLongEnv("ANTHROPIC_MAX_TOKENS", 4096);

// Context-window management: cap how many recent messages are sent per request.
// 0 or negative means "no cap — send the whole history".
int maxHistoryMessages = (int)ParseLongEnv("ANTHROPIC_MAX_HISTORY_MESSAGES", 40);

// Cache the system prompt so repeated requests reuse its prefix (cheaper, faster).
var systemBlocks = new List<TextBlockParam>
{
    new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
};

string historyPath = Environment.GetEnvironmentVariable("ANTHROPIC_HISTORY_FILE")
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
//   1. ANTHROPIC_SYSTEM_PROMPT_FILE — path to a file holding the prompt
//   2. ANTHROPIC_SYSTEM_PROMPT      — the prompt text directly
//   3. the supplied default
static string ResolveSystemPrompt(string fallback)
{
    string? file = Environment.GetEnvironmentVariable("ANTHROPIC_SYSTEM_PROMPT_FILE");
    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
    {
        string fromFile = File.ReadAllText(file).Trim();
        if (!string.IsNullOrWhiteSpace(fromFile))
            return fromFile;
    }

    string? inline = Environment.GetEnvironmentVariable("ANTHROPIC_SYSTEM_PROMPT");
    if (!string.IsNullOrWhiteSpace(inline))
        return inline.Trim();

    return fallback;
}

// Parses a positive long from an env var, falling back when unset or invalid.
static long ParseLongEnv(string name, long fallback)
{
    string? raw = Environment.GetEnvironmentVariable(name);
    return long.TryParse(raw, out long value) && value > 0 ? value : fallback;
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
