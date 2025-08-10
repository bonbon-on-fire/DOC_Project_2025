using Microsoft.AspNetCore.SignalR;
using AIChat.Server.Services;
using AIChat.Server.Data;
using AIChat.Server.Models;
using Microsoft.EntityFrameworkCore;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Collections.Immutable;

namespace AIChat.Server.Hubs;

public class ChatHub : Hub
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IChatService chatService,
        ILogger<ChatHub> logger)
    {
        _chatService = chatService;
        _logger = logger;

        // Subscribe to ChatService events for real-time broadcasting
        _chatService.MessageCreated += OnMessageCreated;
        _chatService.StreamChunkReceived += OnStreamChunkReceived;
        _chatService.StreamCompleted += OnStreamCompleted;
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
            var sendRequest = new SendMessageRequest
            {
                ChatId = chatId,
                UserId = userId,
                Message = message
            };

            var result = await _chatService.SendMessageAsync(sendRequest);

            if (!result.Success)
            {
                _logger.LogError("Error sending message for chat {ChatId}: {Error}", chatId, result.Error);

                await Clients.Group($"chat_{chatId}").SendAsync("ReceiveError", new
                {
                    ChatId = chatId,
                    Error = result.Error ?? "Failed to process message. Please try again.",
                    Timestamp = DateTime.UtcNow
                });
                return;
            }

            // Note: Real-time broadcasting is handled by ChatService events
            // The OnMessageCreated event handler will broadcast messages to SignalR clients
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

    // Event handlers for real-time broadcasting
    private async Task OnMessageCreated(MessageCreatedEvent messageEvent)
    {
        await Clients.Group($"chat_{messageEvent.ChatId}").SendAsync("ReceiveMessage", new
        {
            Id = messageEvent.Message.Id,
            ChatId = messageEvent.Message.ChatId,
            Role = messageEvent.Message.Role,
            Content = (messageEvent.Message as TextMessageDto)?.Text ?? string.Empty,
            Timestamp = messageEvent.Message.Timestamp,
            SequenceNumber = messageEvent.Message.SequenceNumber
        });
    }

    private async Task OnStreamChunkReceived(StreamChunkEvent chunkEvent)
    {
        await Clients.Group($"chat_{chunkEvent.ChatId}").SendAsync("ReceiveStreamChunk", new
        {
            MessageId = chunkEvent.MessageId,
            ChatId = chunkEvent.ChatId,
            Delta = chunkEvent.Delta,
            Done = chunkEvent.Done,
            Kind = chunkEvent.Kind
        });
    }

    private async Task OnStreamCompleted(StreamCompleteEvent completeEvent)
    {
        await Clients.Group($"chat_{completeEvent.ChatId}").SendAsync("ReceiveStreamChunk", new
        {
            MessageId = completeEvent.MessageId,
            ChatId = completeEvent.ChatId,
            Delta = "",
            Done = true
        });
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

        // Unsubscribe from events when client disconnects
        _chatService.MessageCreated -= OnMessageCreated;
        _chatService.StreamChunkReceived -= OnStreamChunkReceived;
        _chatService.StreamCompleted -= OnStreamCompleted;
    }
}