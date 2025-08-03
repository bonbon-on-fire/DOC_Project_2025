using AIChat.Server.Models;

namespace AIChat.Server.Services;

public interface IOpenAIService
{
    Task<string> GenerateResponseAsync(List<Message> conversationHistory, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> GenerateStreamingResponseAsync(List<Message> conversationHistory, CancellationToken cancellationToken = default);
}

public class OpenAIRequest
{
    public List<OpenAIMessage> Messages { get; set; } = new();
    public string Model { get; set; } = "gpt-3.5-turbo";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 1000;
    public bool Stream { get; set; } = false;
}

public class OpenAIMessage
{
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class OpenAIResponse
{
    public List<OpenAIChoice> Choices { get; set; } = new();
    public OpenAIUsage Usage { get; set; } = new();
}

public class OpenAIChoice
{
    public OpenAIMessage Message { get; set; } = new();
    public string FinishReason { get; set; } = string.Empty;
}

public class OpenAIUsage
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}