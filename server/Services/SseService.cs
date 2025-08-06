using Lib.AspNetCore.ServerSentEvents;
using System.Text.Json;
using AIChat.Server.Models;
using AIChat.Server.Data;
using AIChat.Server.Controllers;
using Microsoft.EntityFrameworkCore;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Collections.Immutable;

namespace AIChat.Server.Services;

public class SseService
{
    private readonly AIChatDbContext _dbContext;
    private readonly IStreamingAgent _streamingAgent;
    private readonly ILogger<SseService> _logger;

    public SseService(
        AIChatDbContext dbContext,
        IStreamingAgent streamingAgent,
        ILogger<SseService> logger)
    {
        _dbContext = dbContext;
        _streamingAgent = streamingAgent;
        _logger = logger;
    }

    public async Task SendEventAsync(IServerSentEventsClient client, string eventType, object data)
    {
        var json = JsonSerializer.Serialize(data);
        await client.SendEventAsync(new ServerSentEvent { Type = eventType, Data = new List<string> { json } });
    }

    public async IAsyncEnumerable<string> StreamChatCompletionImpl(
        CreateChatRequest request,
        string chatId,
        string userId)
    {
        _logger.LogInformation("Starting streaming chat completion for chat {ChatId}", chatId);

        // Create user message
        var userMessage = new Message
        {
            ChatId = chatId,
            Role = "user",
            Content = request.Message,
            Timestamp = DateTime.UtcNow
        };

        _dbContext.Messages.Add(userMessage);
        await _dbContext.SaveChangesAsync();

        // Get chat history
        var messages = await _dbContext.Messages
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        var lmMessages = messages.Select(m => new TextMessage
        {
            Text = m.Content,
            Role = m.Role.ToLowerInvariant() switch
            {
                "user" => Role.User,
                "assistant" => Role.Assistant,
                "system" => Role.System,
                _ => Role.User
            },
            Metadata = ImmutableDictionary<string, object>.Empty
        }).ToList();

        // Create system message if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            lmMessages.Insert(0, new TextMessage
            {
                Text = request.SystemPrompt,
                Role = Role.System,
                Metadata = ImmutableDictionary<string, object>.Empty
            });
        }

        // Stream AI response
        var aiMessage = new Message
        {
            ChatId = chatId,
            Role = "assistant",
            Content = "",
            Timestamp = DateTime.UtcNow
        };

        _dbContext.Messages.Add(aiMessage);
        await _dbContext.SaveChangesAsync();

        var fullResponse = "";
        var lmMessages2 = messages.Select(m => new TextMessage
        {
            Text = m.Content,
            Role = m.Role.ToLowerInvariant() switch
            {
                "user" => Role.User,
                "assistant" => Role.Assistant,
                "system" => Role.System,
                _ => Role.User
            },
            Metadata = ImmutableDictionary<string, object>.Empty
        }).ToList();
        var options = new GenerateReplyOptions { ModelId = "moonshotai/kimi-k2" };
        var streamingResponse = await _streamingAgent.GenerateReplyAsync(lmMessages, options);
        foreach (var message in streamingResponse.OfType<TextMessage>())
        {
            fullResponse += message.Text;
            yield return message.Text;
        }

        // Update AI message with full response
        aiMessage.Content = fullResponse;
        await _dbContext.SaveChangesAsync();
    }
}
