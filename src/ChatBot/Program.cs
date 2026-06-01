using Anthropic;
using Anthropic.Models.Messages;

string? apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("Error: ANTHROPIC_API_KEY environment variable is not set.");
    return 1;
}

AnthropicClient client = new() { ApiKey = apiKey };

const string defaultSystemPrompt = "You are a helpful, concise assistant.";
string systemPrompt = ResolveSystemPrompt(defaultSystemPrompt);

var history = new List<MessageParam>();

Console.WriteLine("Claude Chatbot — type 'exit' or 'quit' to stop.");
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

    history.Add(new MessageParam { Role = Role.User, Content = input.Trim() });

    Message response = await client.Messages.Create(new MessageCreateParams
    {
        Model = Model.ClaudeOpus4_8,
        MaxTokens = 4096,
        System = systemPrompt,
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

// Resolves the system prompt with the following precedence:
//   1. ANTHROPIC_SYSTEM_PROMPT_FILE — path to a file holding the prompt
//   2. ANTHROPIC_SYSTEM_PROMPT      — the prompt text directly
//   3. the supplied default
static string ResolveSystemPrompt(string fallback)
{
    string? file = Environment.GetEnvironmentVariable("ANTHROPIC_SYSTEM_PROMPT_FILE");
    if (!string.IsNullOrWhiteSpace(file) && File.Exists(file))
    {
        string fromFile = File.ReadAllText(file).Trim();
        if (!string.IsNullOrWhiteSpace(fromFile))
            return fromFile;
    }

    string? inline = Environment.GetEnvironmentVariable("ANTHROPIC_SYSTEM_PROMPT");
    if (!string.IsNullOrWhiteSpace(inline))
        return inline.Trim();

    return fallback;
}
