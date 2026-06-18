using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatBot;

/// <summary>Creates the configured <see cref="IChatService"/> for a conversation.</summary>
public interface IChatServiceFactory
{
    /// <summary>
    /// Creates a chat engine bound to <paramref name="conversationId"/>, seeded with that
    /// conversation's prior turns from the store. Switching conversations means creating anew.
    /// </summary>
    Task<IChatService> CreateAsync(string conversationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Picks the streaming or compaction engine based on <see cref="ChatOptions"/> and
/// resolves the system prompt. The engine loads/saves history via the store.
/// </summary>
public sealed class ChatServiceFactory : IChatServiceFactory
{
    private const string DefaultSystemPrompt = "You are a helpful, concise assistant.";

    private readonly IChatCompletionClient _completion;
    private readonly IBetaCompletionClient _betaCompletion;
    private readonly ChatOptions _options;
    private readonly IConversationStore _store;
    private readonly IReadOnlyList<IChatTool> _tools;
    private readonly ILoggerFactory _loggerFactory;

    public ChatServiceFactory(
        IChatCompletionClient completion,
        IBetaCompletionClient betaCompletion,
        IOptions<ChatOptions> options,
        IConversationStore store,
        IEnumerable<IChatTool> tools,
        ILoggerFactory loggerFactory)
    {
        _completion = completion;
        _betaCompletion = betaCompletion;
        _options = options.Value;
        _store = store;
        _tools = tools.ToList();
        _loggerFactory = loggerFactory;
    }

    public async Task<IChatService> CreateAsync(
        string conversationId, CancellationToken cancellationToken = default)
    {
        string systemPrompt = ResolveSystemPrompt(_options);
        List<StoredTurn> seed = await _store.LoadAsync(conversationId, cancellationToken);

        if (_options.Compaction)
        {
            return new CompactionChatService(_betaCompletion, _options, systemPrompt, conversationId, _store, seed,
                _tools, _loggerFactory.CreateLogger<CompactionChatService>());
        }

        return new StreamingChatService(_completion, _options, systemPrompt, conversationId, _store, seed, _tools,
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
