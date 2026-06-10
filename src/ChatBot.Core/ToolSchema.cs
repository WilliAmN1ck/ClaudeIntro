using System.Text.Json;

namespace ChatBot;

/// <summary>Helpers for building JSON-schema property definitions for <see cref="IChatTool"/>.</summary>
public static class ToolSchema
{
    /// <summary>A string parameter.</summary>
    public static JsonElement String(string description) =>
        JsonSerializer.SerializeToElement(new { type = "string", description });

    /// <summary>An integer parameter.</summary>
    public static JsonElement Integer(string description) =>
        JsonSerializer.SerializeToElement(new { type = "integer", description });

    /// <summary>A number (floating-point) parameter.</summary>
    public static JsonElement Number(string description) =>
        JsonSerializer.SerializeToElement(new { type = "number", description });

    /// <summary>A boolean parameter.</summary>
    public static JsonElement Boolean(string description) =>
        JsonSerializer.SerializeToElement(new { type = "boolean", description });
}
