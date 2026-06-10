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

// Resolving the factory constructs the AnthropicClient, which throws if the key is unset.
IChatServiceFactory factory;
try
{
    factory = provider.GetRequiredService<IChatServiceFactory>();
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

string historyPath = string.IsNullOrWhiteSpace(options.HistoryPath)
    ? ConversationStore.DefaultPath
    : options.HistoryPath;

// Resume decision is a host (console) concern; the engine just gets the seed turns.
var seed = new List<StoredTurn>();
if (ConversationStore.Exists(historyPath))
{
    Console.Write($"Found a saved conversation at {historyPath}. Resume it? [Y/n] ");
    string? answer = Console.ReadLine();
    if (answer is null || !answer.Trim().Equals("n", StringComparison.OrdinalIgnoreCase))
    {
        seed = ConversationStore.Load(historyPath);
        Console.WriteLine($"Resumed {seed.Count} turn(s).");
    }
    else
    {
        ConversationStore.Clear(historyPath);
        Console.WriteLine("Started a fresh conversation.");
    }
}

IChatService chat = factory.Create(seed);

Console.WriteLine("Claude Chatbot — type 'exit'/'quit' to stop, 'clear' to wipe saved history.");
Console.WriteLine($"Model: {chat.Model}  |  MaxTokens: {chat.MaxTokens}  |  Context: " +
                  (options.Compaction
                      ? "server-side compaction"
                      : options.MaxHistoryMessages > 0 ? $"last {options.MaxHistoryMessages} msgs" : "unlimited"));
Console.WriteLine($"Persona: {chat.SystemPrompt}");
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
        chat.Clear();
        Console.WriteLine("Conversation history cleared.");
        continue;
    }

    Console.Write("\nClaude: ");
    await foreach (string delta in chat.SendAsync(input.Trim()))
        Console.Write(delta);
    Console.WriteLine();

    ConversationStore.Save(historyPath, chat.History);
}

Console.WriteLine("\nGoodbye!");
return 0;

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
