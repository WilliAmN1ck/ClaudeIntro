using Anthropic;
using Anthropic.Models.Messages;

string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Error: ANTHROPIC_API_KEY environment variable is not set.");
    return 1;
}

AnthropicClient client = new() { ApiKey = apiKey };

var history = new List<MessageParam>();

Console.WriteLine("Claude Chatbot — type 'exit' or 'quit' to stop.");
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

    history.Add(new MessageParam { Role = Role.User, Content = input.Trim() });

    Message response = await client.Messages.Create(new MessageCreateParams
    {
        Model = Model.ClaudeOpus4_8,
        MaxTokens = 4096,
        Messages = history,
    });

    string reply = string.Concat(
        response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .Select(t => t.Text));

    history.Add(new MessageParam { Role = Role.Assistant, Content = reply });

    Console.WriteLine($"\nClaude: {reply}");
}

Console.WriteLine("\nGoodbye!");
return 0;
