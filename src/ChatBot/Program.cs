using Anthropic;
using Anthropic.Models.Messages;
using ChatBot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

if (args.Contains("-h") || args.Contains("--help"))
{
    PrintUsage();
    return 0;
}

// Configuration precedence (low → high): appsettings.json → env vars → CLI flags.
var switchMappings = new Dictionary<string, string>
{
    ["--model"] = "ChatBot:Model",
    ["--max-tokens"] = "ChatBot:MaxTokens",
    ["--max-history"] = "ChatBot:MaxHistoryMessages",
    ["--system"] = "ChatBot:SystemPrompt",
    ["--system-file"] = "ChatBot:SystemPromptFile",
    ["--history"] = "ChatBot:HistoryPath",
};

// The command-line config provider can't express bare flags, so map --compaction manually.
var flagOverrides = new Dictionary<string, string?>();
if (args.Contains("--compaction"))
    flagOverrides["ChatBot:Compaction"] = "true";

IConfiguration config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args, switchMappings)
    .AddInMemoryCollection(flagOverrides)
    .Build();

using ServiceProvider provider = new ServiceCollection()
    .AddChatBot(config)
    .BuildServiceProvider();

ChatOptions options = provider.GetRequiredService<IOptions<ChatOptions>>().Value;

AnthropicClient client;
try
{
    client = provider.GetRequiredService<AnthropicClient>();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

const string defaultSystemPrompt = "You are a helpful, concise assistant.";
string systemPrompt = ResolveSystemPrompt(defaultSystemPrompt, options);

string modelId = string.IsNullOrWhiteSpace(options.Model) ? "claude-opus-4-8" : options.Model.Trim();
long maxTokens = options.MaxTokens >= 1 ? options.MaxTokens : 4096;
int maxHistoryMessages = options.MaxHistoryMessages >= 0 ? options.MaxHistoryMessages : 40;

// Cache the system prompt so repeated requests reuse its prefix (cheaper, faster).
var systemBlocks = new List<TextBlockParam>
{
    new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
};

string historyPath = string.IsNullOrWhiteSpace(options.HistoryPath)
    ? ConversationStore.DefaultPath
    : options.HistoryPath;

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

// Compaction mode (beta) lets the API summarize old turns server-side instead of
// the simple count-based trim. It is non-streaming and uses the beta endpoint.
bool useCompaction = options.Compaction;

// Each backend owns its own conversation history and prints its own reply text;
// the outer loop just handles input, commands, and text persistence.
Func<string, Task<string>> sendTurn;
Action clearChat;

if (useCompaction)
{
    var compactionChat = new CompactionChat(client, modelId, maxTokens, systemPrompt, turns);
    sendTurn = compactionChat.SendAsync;
    clearChat = compactionChat.Clear;
}
else
{
    sendTurn = async userText =>
    {
        history.Add(new MessageParam { Role = Role.User, Content = userText });
        var parameters = new MessageCreateParams
        {
            Model = modelId,
            MaxTokens = maxTokens,
            System = systemBlocks,
            Messages = TrimHistory(history, maxHistoryMessages),
        };

        var sb = new System.Text.StringBuilder();
        await foreach (RawMessageStreamEvent streamEvent in client.Messages.CreateStreaming(parameters))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                delta.Delta.TryPickText(out var text))
            {
                Console.Write(text.Text);
                sb.Append(text.Text);
            }
        }

        string reply = sb.ToString();
        history.Add(new MessageParam { Role = Role.Assistant, Content = reply });
        return reply;
    };
    clearChat = () => history.Clear();
}

Console.WriteLine("Claude Chatbot — type 'exit'/'quit' to stop, 'clear' to wipe saved history.");
Console.WriteLine($"Model: {modelId}  |  MaxTokens: {maxTokens}  |  Context: " +
                  (useCompaction
                      ? "server-side compaction"
                      : maxHistoryMessages > 0 ? $"last {maxHistoryMessages} msgs" : "unlimited"));
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
        clearChat();
        turns.Clear();
        Console.WriteLine("Conversation history cleared.");
        continue;
    }

    string userText = input.Trim();
    turns.Add(new StoredTurn("user", userText));

    Console.Write("\nClaude: ");
    string reply = await sendTurn(userText);
    Console.WriteLine();

    turns.Add(new StoredTurn("assistant", reply));
    ConversationStore.Save(historyPath, turns);
}

Console.WriteLine("\nGoodbye!");
return 0;

// Resolves the system prompt: inline value, then file contents, then the default.
static string ResolveSystemPrompt(string fallback, ChatOptions options)
{
    if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
        return options.SystemPrompt.Trim();

    if (!string.IsNullOrWhiteSpace(options.SystemPromptFile) && File.Exists(options.SystemPromptFile))
    {
        string fromFile = File.ReadAllText(options.SystemPromptFile).Trim();
        if (!string.IsNullOrWhiteSpace(fromFile))
            return fromFile;
    }

    return fallback;
}

static void PrintUsage()
{
    Console.WriteLine("""
        ChatBot — a Claude console chatbot.

        Usage: dotnet run --project src/ChatBot -- [options]

        Settings load from appsettings.json, then environment variables, then the
        CLI flags below (each source overrides the previous). Env-var form is the
        config key with '__', e.g. ChatBot__Model.

          --model <id>           Model id            (ChatBot__Model, default claude-opus-4-8)
          --max-tokens <n>       Max output tokens   (ChatBot__MaxTokens, default 4096)
          --max-history <n>      Recent-message cap  (ChatBot__MaxHistoryMessages, default 40; 0 = unlimited)
          --system <text>        System prompt text  (ChatBot__SystemPrompt)
          --system-file <path>   System prompt file  (ChatBot__SystemPromptFile)
          --history <path>       History file path   (ChatBot__HistoryPath)
          --compaction           Use server-side compaction instead of message-count trim
                                 (beta; non-streaming; ChatBot__Compaction=true)
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
