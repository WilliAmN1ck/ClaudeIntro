using Anthropic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatBot;

/// <summary>Creates the configured <see cref="IChatService"/> for a conversation.</summary>
public interface IChatServiceFactory
{
    /// <summary>Creates a chat engine, loading prior history from the conversation store.</summary>
    Task<IChatService> CreateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Picks the streaming or compaction engine based on <see cref="ChatOptions"/> and
/// resolves the system prompt. The engine loads/saves history via the store.
/// </summary>
public sealed class ChatServiceFactory : IChatServiceFactory
{
    private const string DefaultSystemPrompt = "You are a helpful, concise assistant.";

    private readonly AnthropicClient _client;
    private readonly IChatCompletionClient _completion;
    private readonly ChatOptions _options;
    private readonly IConversationStore _store;
    private readonly IReadOnlyList<IChatTool> _tools;
    private readonly ILoggerFactory _loggerFactory;

    public ChatServiceFactory(
        AnthropicClient client,
        IChatCompletionClient completion,
        IOptions<ChatOptions> options,
        IConversationStore store,
        IEnumerable<IChatTool> tools,
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _completion = completion;
        _options = options.Value;
        _store = store;
        _tools = tools.ToList();
        _loggerFactory = loggerFactory;
    }

    public async Task<IChatService> CreateAsync(CancellationToken cancellationToken = default)
    {
        string systemPrompt = ResolveSystemPrompt(_options);
        List<StoredTurn> seed = await _store.LoadAsync(cancellationToken);

        if (_options.Compaction)
        {
            if (_tools.Count > 0)
            {
                _loggerFactory.CreateLogger<ChatServiceFactory>()
                    .LogWarning("Tools are not used in compaction mode; {Count} tool(s) ignored.", _tools.Count);
            }

            return new CompactionChatService(_client, _options, systemPrompt, _store, seed,
                _loggerFactory.CreateLogger<CompactionChatService>());
        }

        return new StreamingChatService(_completion, _options, systemPrompt, _store, seed, _tools,
            _loggerFactory.CreateLogger<StreamingChatService>());
    }

    /// <summary>
    /// Resolves the system prompt: inline value, then file contents, then the default.
    /// </summary>
    public static string ResolveSystemPrompt(ChatOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
            return options.SystemPrompt.Trim();

        if (!string.IsNullOrWhiteSpace(options.SystemPromptFile) && File.Exists(options.SystemPromptFile))
        {
            string fromFile = File.ReadAllText(options.SystemPromptFile).Trim();
            if (!string.IsNullOrWhiteSpace(fromFile))
                return fromFile;
        }

        return DefaultSystemPrompt;
    }
}
