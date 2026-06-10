using Anthropic;
using Microsoft.Extensions.Options;

namespace ChatBot;

/// <summary>Creates the configured <see cref="IChatService"/> for a conversation.</summary>
public interface IChatServiceFactory
{
    /// <summary>Creates a chat engine (history is loaded from the conversation store).</summary>
    IChatService Create();
}

/// <summary>
/// Picks the streaming or compaction engine based on <see cref="ChatOptions"/> and
/// resolves the system prompt. The engine loads/saves history via the store.
/// </summary>
public sealed class ChatServiceFactory : IChatServiceFactory
{
    private const string DefaultSystemPrompt = "You are a helpful, concise assistant.";

    private readonly AnthropicClient _client;
    private readonly ChatOptions _options;
    private readonly IConversationStore _store;

    public ChatServiceFactory(AnthropicClient client, IOptions<ChatOptions> options, IConversationStore store)
    {
        _client = client;
        _options = options.Value;
        _store = store;
    }

    public IChatService Create()
    {
        string systemPrompt = ResolveSystemPrompt(_options);
        return _options.Compaction
            ? new CompactionChatService(_client, _options, systemPrompt, _store)
            : new StreamingChatService(_client, _options, systemPrompt, _store);
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
