using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ChatBot;

/// <summary>DI registration for the chatbot's services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers chatbot services: binds <see cref="ChatOptions"/> from the
    /// <c>ChatBot</c> configuration section and registers the <see cref="AnthropicClient"/>
    /// (API key read from the <c>ANTHROPIC_API_KEY</c> environment variable).
    /// </summary>
    public static IServiceCollection AddChatBot(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ChatOptions>(config.GetSection(ChatOptions.SectionName));

        services.AddSingleton(_ =>
        {
            string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "ANTHROPIC_API_KEY environment variable is not set.");

            return new AnthropicClient { ApiKey = apiKey };
        });

        services.AddSingleton<IChatServiceFactory, ChatServiceFactory>();

        return services;
    }
}
