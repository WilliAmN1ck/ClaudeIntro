using ChatBot;
using ChatBot.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
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
    ["--store"] = "ChatBot:Store",
    ["--conversation"] = "ChatBot:ConversationId",
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
    .AddLogging(builder =>
    {
        // Console logging is a host concern. Default level (Warning) keeps chat clean;
        // output goes to stderr so it never interleaves with the reply on stdout.
        builder.AddConfiguration(config.GetSection("Logging"));
        builder.AddSimpleConsole(o => o.SingleLine = true);
        builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    })
    // Tools the model may call. Register your own IChatTool implementations here.
    .AddSingleton<IChatTool, CurrentTimeTool>()
    .AddSingleton<IChatTool, RollDiceTool>()
    .AddChatBot(config)
    .BuildServiceProvider();

ChatOptions options = provider.GetRequiredService<IOptions<ChatOptions>>().Value;

// Resolving the factory constructs the AnthropicClient (throws if the key is unset); the first
// store call may surface a misconfigured/unreachable database. Both are reported as a clean
// startup error.
IConversationStore store;
IChatServiceFactory factory;
IChatService chat;
try
{
    factory = provider.GetRequiredService<IChatServiceFactory>();
    store = provider.GetRequiredService<IConversationStore>();

    // Ensure the requested (or default) conversation exists, then open it. For the file store
    // this is also where a legacy single-file history is migrated into 'default' on first run.
    string requestedId = string.IsNullOrWhiteSpace(options.ConversationId) ? "default" : options.ConversationId;
    ConversationInfo startup = await store.CreateAsync(requestedId, null);
    chat = await factory.CreateAsync(startup.Id);
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

Console.WriteLine("Claude Chatbot — type /help for commands, Ctrl-C to cancel a reply.");
Console.WriteLine($"Model: {chat.Model}  |  MaxTokens: {chat.MaxTokens}  |  Store: {options.Store}  |  Context: " +
                  (options.Compaction
                      ? "server-side compaction"
                      : options.MaxHistoryMessages > 0 ? $"last {options.MaxHistoryMessages} msgs" : "unlimited"));
Console.WriteLine($"Persona: {chat.SystemPrompt}");
var toolNames = provider.GetServices<IChatTool>().Select(t => t.Name).ToList();
if (toolNames.Count > 0 && !options.Compaction)
    Console.WriteLine($"Tools: {string.Join(", ", toolNames)}");
await PrintActiveConversationAsync();
Console.WriteLine(new string('-', 50));

// Ctrl-C cancels the in-progress reply without killing the app; at the prompt it exits normally.
CancellationTokenSource? turnCts = null;
Console.CancelKeyPress += (_, e) =>
{
    if (turnCts is { IsCancellationRequested: false })
    {
        e.Cancel = true;
        turnCts.Cancel();
    }
};

while (true)
{
    Console.Write("\nYou: ");
    string? input = Console.ReadLine();

    // Null input (EOF / closed stdin) ends the session, like 'exit'.
    if (input is null)
        break;

    ChatCommand command = ChatCommandParser.Parse(input);

    if (command is ChatCommand.Exit)
        break;
    if (command is ChatCommand.Empty)
        continue;

    // Conversation-management commands run at the prompt (never mid-stream).
    if (command is not ChatCommand.Send send)
    {
        await HandleCommandAsync(command);
        continue;
    }

    turnCts = new CancellationTokenSource();
    try
    {
        Console.Write("\nClaude: ");
        await foreach (string delta in chat.SendAsync(send.Text, turnCts.Token))
            Console.Write(delta);
        Console.WriteLine();

        if (chat.LastTurnUsage is { } usage)
            Console.WriteLine($"[tokens: {usage}]");
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("\n[cancelled]");
    }
    catch (Anthropic.Exceptions.AnthropicException ex)
    {
        Console.WriteLine();
        Console.Error.WriteLine($"[error] {ex.Message}");
    }
    finally
    {
        turnCts.Dispose();
        turnCts = null;
    }
}

Console.WriteLine("\nGoodbye!");
return 0;

// Dispatches a parsed management command, mutating the active engine (`chat`) when switching.
async Task HandleCommandAsync(ChatCommand command)
{
    switch (command)
    {
        case ChatCommand.Help:
            PrintCommands();
            break;

        case ChatCommand.List:
            await PrintConversationsAsync();
            break;

        case ChatCommand.New newCmd:
        {
            ConversationInfo info = await store.CreateAsync(null, newCmd.Title);
            chat = await factory.CreateAsync(info.Id);
            Console.WriteLine($"Started conversation '{info.Title}' [{info.Id}].");
            break;
        }

        case ChatCommand.Switch sw:
        {
            ConversationInfo? info = await store.GetAsync(sw.Id);
            if (info is null)
            {
                Console.WriteLine($"No conversation '{sw.Id}'. Type /list to see them.");
                break;
            }

            chat = await factory.CreateAsync(info.Id);
            Console.WriteLine($"Switched to '{info.Title}' [{info.Id}] ({chat.History.Count} turn(s)).");
            break;
        }

        case ChatCommand.Rename rn:
            await store.RenameAsync(chat.ConversationId, rn.Title);
            Console.WriteLine($"Renamed conversation [{chat.ConversationId}] to '{rn.Title}'.");
            break;

        case ChatCommand.Delete del:
            await HandleDeleteAsync(del.Id);
            break;

        case ChatCommand.ClearCurrent:
            await chat.ClearAsync();
            Console.WriteLine("Conversation history cleared.");
            break;

        case ChatCommand.Unknown:
            Console.WriteLine("Unknown command. Type /help for the list.");
            break;
    }
}

async Task HandleDeleteAsync(string? id)
{
    string targetId = id ?? chat.ConversationId;
    ConversationInfo? target = await store.GetAsync(targetId);
    if (target is null)
    {
        Console.WriteLine($"No conversation '{targetId}'.");
        return;
    }

    Console.Write($"Delete conversation '{target.Title}' [{target.Id}] and its history? [y/N] ");
    string? confirm = Console.ReadLine();
    if (confirm is null || !confirm.Trim().Equals("y", StringComparison.OrdinalIgnoreCase))
    {
        Console.WriteLine("Cancelled.");
        return;
    }

    await store.DeleteAsync(target.Id);
    Console.WriteLine($"Deleted '{target.Title}' [{target.Id}].");

    // If the active conversation was deleted, open the newest remaining one, or a fresh 'default'.
    if (string.Equals(target.Id, chat.ConversationId, StringComparison.OrdinalIgnoreCase))
    {
        IReadOnlyList<ConversationInfo> remaining = await store.ListAsync();
        string nextId = remaining.Count > 0 ? remaining[0].Id : (await store.CreateAsync("default", null)).Id;
        chat = await factory.CreateAsync(nextId);
        Console.WriteLine($"Now in [{chat.ConversationId}] ({chat.History.Count} turn(s)).");
    }
}

async Task PrintConversationsAsync()
{
    IReadOnlyList<ConversationInfo> conversations = await store.ListAsync();
    if (conversations.Count == 0)
    {
        Console.WriteLine("(no conversations)");
        return;
    }

    Console.WriteLine("Conversations (newest first):");
    foreach (ConversationInfo c in conversations)
    {
        string marker = string.Equals(c.Id, chat.ConversationId, StringComparison.OrdinalIgnoreCase) ? "*" : " ";
        Console.WriteLine($" {marker} [{c.Id}] {c.Title} — {c.TurnCount} turn(s), updated {c.UpdatedAt.LocalDateTime:g}");
    }
}

async Task PrintActiveConversationAsync()
{
    ConversationInfo? active = await store.GetAsync(chat.ConversationId);
    string title = active?.Title ?? chat.ConversationId;
    Console.WriteLine($"Conversation: {title} [{chat.ConversationId}]  ({chat.History.Count} turn(s))");
}

static void PrintCommands()
{
    Console.WriteLine("""
        Commands:
          /list, /ls            List conversations (newest first; * = active)
          /new [title]          Create and switch to a new conversation
          /switch <id>, /use    Switch to an existing conversation
          /rename <title>       Rename the active conversation
          /delete [id]          Delete a conversation (defaults to the active one)
          /clear, clear         Empty the active conversation's history
          /help, /?             Show this help
          exit / quit           End the session  (Ctrl-C cancels a streaming reply)
        """);
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
          --history <path>       History base path   (ChatBot__HistoryPath; file store)
          --store <backend>      Conversation store: file (default) or postgres (ChatBot__Store)
          --conversation <id>    Conversation to open at startup (ChatBot__ConversationId, default 'default')
          --compaction           Use server-side compaction instead of message-count trim
                                 (beta; non-streaming; ChatBot__Compaction=true)
          -h, --help             Show this help and exit

        Postgres store also needs ChatBot__PostgresConnectionString.

        Requires the ANTHROPIC_API_KEY environment variable.
        In-chat commands (type /help once running): /list, /new, /switch, /rename, /delete, /clear.
        """);
}
