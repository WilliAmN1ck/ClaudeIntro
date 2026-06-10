using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Logging is configured from the "Logging" section; default level keeps the
        // console clean during chat (warnings/errors still surface). Writes to stderr
        // so log lines don't interleave with the streamed reply on stdout.
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(config.GetSection("Logging"));
            builder.AddSimpleConsole(options => options.SingleLine = true);
            // Route log output to stderr so it never interleaves with the reply on stdout.
            builder.Services.Configure<Microsoft.Extensions.Logging.Console.ConsoleLoggerOptions>(
                options => options.LogToStandardErrorThreshold = LogLevel.Trace);
        });

        services.AddSingleton(_ =>
        {
            string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException(
                    "ANTHROPIC_API_KEY environment variable is not set.");

            return new AnthropicClient { ApiKey = apiKey };
        });

        services.AddSingleton<IConversationStore, FileConversationStore>();
        services.AddSingleton<IChatServiceFactory, ChatServiceFactory>();

        return services;
    }
}
