using Anthropic;
using Microsoft.Extensions.Options;

namespace ChatBot;

/// <summary>Creates the configured <see cref="IChatService"/> for a conversation.</summary>
public interface IChatServiceFactory
{
    /// <summary>Creates a chat engine seeded with the given prior turns.</summary>
    IChatService Create(IEnumerable<StoredTurn> seed);
}

/// <summary>
/// Picks the streaming or compaction engine based on <see cref="ChatOptions"/> and
/// resolves the system prompt. Hosts own the resume decision and pass in the seed.
/// </summary>
public sealed class ChatServiceFactory : IChatServiceFactory
{
    private const string DefaultSystemPrompt = "You are a helpful, concise assistant.";

    private readonly AnthropicClient _client;
    private readonly ChatOptions _options;

    public ChatServiceFactory(AnthropicClient client, IOptions<ChatOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public IChatService Create(IEnumerable<StoredTurn> seed)
    {
        string systemPrompt = ResolveSystemPrompt(_options);
        return _options.Compaction
            ? new CompactionChatService(_client, _options, systemPrompt, seed)
            : new StreamingChatService(_client, _options, systemPrompt, seed);
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
