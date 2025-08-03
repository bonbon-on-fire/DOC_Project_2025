using Microsoft.AspNetCore.SignalR;
using AIChat.Server.Services;
using AIChat.Server.Data;
using AIChat.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace AIChat.Server.Hubs;

public class ChatHub : Hub
{
    private readonly IOpenAIService _openAIService;
    private readonly AIChatDbContext _dbContext;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(IOpenAIService openAIService, AIChatDbContext dbContext, ILogger<ChatHub> logger)
    {
        _openAIService = openAIService;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task JoinChatGroup(string chatId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        _logger.LogInformation("User {ConnectionId} joined chat group {ChatId}", Context.ConnectionId, chatId);
    }

    public async Task LeaveChatGroup(string chatId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_{chatId}");
        _logger.LogInformation("User {ConnectionId} left chat group {ChatId}", Context.ConnectionId, chatId);
    }

    public async Task SendMessage(string chatId, string userId, string message)
    {
        try
        {
            // Save user message to database
            var userMessage = new Message
            {
                ChatId = chatId,
                Role = "user",
                Content = message,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.Messages.Add(userMessage);
            await _dbContext.SaveChangesAsync();

            // Broadcast user message to chat group
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveMessage", new
            {
                Id = userMessage.Id,
                ChatId = chatId,
                Role = "user",
                Content = message,
                Timestamp = userMessage.Timestamp
            });

            // Get conversation history for AI context
            var conversationHistory = await _dbContext.Messages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.Timestamp)
                .ToListAsync();

            // Create assistant message placeholder
            var assistantMessage = new Message
            {
                ChatId = chatId,
                Role = "assistant",
                Content = "", // Will be filled as we stream
                Timestamp = DateTime.UtcNow
            };

            _dbContext.Messages.Add(assistantMessage);
            await _dbContext.SaveChangesAsync();

            // Notify clients that AI is starting to respond
            await Clients.Group($"chat_{chatId}").SendAsync("AIResponseStarted", new
            {
                Id = assistantMessage.Id,
                ChatId = chatId,
                Role = "assistant",
                Timestamp = assistantMessage.Timestamp
            });

            // Stream AI response
            var fullResponse = "";
            await foreach (var chunk in _openAIService.GenerateStreamingResponseAsync(conversationHistory))
            {
                fullResponse += chunk;
                
                // Send chunk to clients
                await Clients.Group($"chat_{chatId}").SendAsync("ReceiveStreamChunk", new
                {
                    MessageId = assistantMessage.Id,
                    ChatId = chatId,
                    Delta = chunk,
                    Done = false
                });
            }

            // Update the assistant message with full content
            assistantMessage.Content = fullResponse;
            _dbContext.Messages.Update(assistantMessage);
            await _dbContext.SaveChangesAsync();

            // Notify clients that streaming is complete
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveStreamChunk", new
            {
                MessageId = assistantMessage.Id,
                ChatId = chatId,
                Delta = "",
                Done = true
            });

            // Update chat's UpdatedAt timestamp
            var chat = await _dbContext.Chats.FindAsync(chatId);
            if (chat != null)
            {
                chat.UpdatedAt = DateTime.UtcNow;
                _dbContext.Chats.Update(chat);
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message for chat {ChatId}", chatId);
            
            await Clients.Group($"chat_{chatId}").SendAsync("ReceiveError", new
            {
                ChatId = chatId,
                Error = "Failed to process message. Please try again.",
                Timestamp = DateTime.UtcNow
            });
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client {ConnectionId} connected to ChatHub", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected from ChatHub", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}