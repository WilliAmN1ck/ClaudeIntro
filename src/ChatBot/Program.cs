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
        Model = Model.ClaudeOpus4_8,
        MaxTokens = 4096,
        System = systemPrompt,
        Messages = history,
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
