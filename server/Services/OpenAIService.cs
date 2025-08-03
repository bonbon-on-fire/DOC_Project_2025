using OpenAI;
using OpenAI.Chat;
using AIChat.Server.Models;
using AIChat.Server.Services;

namespace AIChat.Server.Services;

public class OpenAIService : IOpenAIService
{
    private readonly OpenAIClient _openAIClient;
    private readonly ILogger<OpenAIService> _logger;
    private readonly string _model;

    public OpenAIService(IConfiguration configuration, ILogger<OpenAIService> logger)
    {
        var apiKey = configuration["OpenAI:ApiKey"] 
                     ?? throw new InvalidOperationException("OpenAI API key not configured");
        
        _openAIClient = new OpenAIClient(apiKey);
        _logger = logger;
        _model = configuration["OpenAI:Model"] ?? "gpt-3.5-turbo";
    }

    public async Task<string> GenerateResponseAsync(List<Message> conversationHistory, CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = ConvertToOpenAIMessages(conversationHistory);
            
            var response = await _openAIClient.GetChatClient(_model)
                .CompleteChatAsync(messages, new ChatCompletionOptions(), cancellationToken);

            var content = response.Value.Content[0].Text;
            _logger.LogInformation("Generated AI response");
            
            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI response");
            throw new InvalidOperationException("Failed to generate AI response", ex);
        }
    }

    public async IAsyncEnumerable<string> GenerateStreamingResponseAsync(
        List<Message> conversationHistory, 
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var messages = ConvertToOpenAIMessages(conversationHistory);
        
        var streamingResponse = _openAIClient.GetChatClient(_model)
            .CompleteChatStreamingAsync(messages, new ChatCompletionOptions(), cancellationToken);

        await foreach (var update in streamingResponse)
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            if (update.ContentUpdate.Count > 0)
            {
                var content = string.Join("", update.ContentUpdate.Select(c => c.Text));
                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                }
            }
        }
    }

    private static List<ChatMessage> ConvertToOpenAIMessages(List<Message> messages)
    {
        var openAIMessages = new List<ChatMessage>();

        foreach (var message in messages.OrderBy(m => m.Timestamp))
        {
            ChatMessage chatMessage = message.Role.ToLowerInvariant() switch
            {
                "user" => ChatMessage.CreateUserMessage(message.Content),
                "assistant" => ChatMessage.CreateAssistantMessage(message.Content),
                "system" => ChatMessage.CreateSystemMessage(message.Content),
                _ => ChatMessage.CreateUserMessage(message.Content)
            };

            openAIMessages.Add(chatMessage);
        }

        return openAIMessages;
    }
}