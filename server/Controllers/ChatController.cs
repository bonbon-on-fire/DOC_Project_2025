using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIChat.Server.Data;
using AIChat.Server.Models;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Runtime.CompilerServices;
using System.Text;
using Lib.AspNetCore.ServerSentEvents;
using AIChat.Server.Services;
using ChatDto = AIChat.Server.Services.ChatDto;
using MessageDto = AIChat.Server.Services.MessageDto;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IChatService _chatService;
    private readonly ILogger<ChatController> _logger;
    private readonly IServerSentEventsService _serverSentEventsService;

    public ChatController(
        IChatService chatService,
        ILogger<ChatController> logger,
        IServerSentEventsService serverSentEventsService)
    {
        _chatService = chatService;
        _logger = logger;
        _serverSentEventsService = serverSentEventsService;
    }

    // GET: api/chat/history?userId={userId}&page={page}&pageSize={pageSize}
    [HttpGet("history")]
    public async Task<ActionResult<ChatHistoryResponse>> GetChatHistory(
        [FromQuery] string userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await _chatService.GetChatHistoryAsync(userId, page, pageSize);

        if (!result.Success)
        {
            _logger.LogError("Error retrieving chat history for user {UserId}: {Error}", userId, result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to retrieve chat history" });
        }

        var response = new ChatHistoryResponse
        {
            Chats = result.Chats,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize
        };

        return Ok(response);
    }

    // GET: api/chat/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<ChatDto>> GetChat(string id)
    {
        var result = await _chatService.GetChatAsync(id);

        if (!result.Success)
        {
            if (result.Error == "Chat not found")
            {
                return NotFound(new { Error = "Chat not found" });
            }

            _logger.LogError("Error retrieving chat {ChatId}: {Error}", id, result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to retrieve chat" });
        }

        return Ok(result.Chat);
    }

    // POST: api/chat
    [HttpPost]
    public async Task<ActionResult<ChatDto>> CreateChat([FromBody] CreateChatRequest request)
    {
        var createRequest = new Services.CreateChatRequest
        {
            UserId = request.UserId,
            Message = request.Message,
            SystemPrompt = request.SystemPrompt
        };

        var result = await _chatService.CreateChatAsync(createRequest);

        if (!result.Success)
        {
            _logger.LogError("Error creating chat: {Error}", result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to create chat" });
        }

        return CreatedAtAction(nameof(GetChat), new { id = result.Chat!.Id }, result.Chat);
    }

    // POST: api/chat/{chatId}/messages
    [HttpPost("{chatId}/messages")]
    public async Task<ActionResult<MessageDto>> SendMessage(
        string chatId,
        [FromBody] SendMessageRequest request)
    {
        // This endpoint provides an HTTP alternative to SignalR for sending messages
        // Implementation mirrors ChatHub.SendMessage but returns HTTP responses
        // For real-time updates, clients should use SignalR

        // TODO: Get userId from authentication context
        var userId = "user123"; // Placeholder

        var sendRequest = new Services.SendMessageRequest
        {
            ChatId = chatId,
            UserId = userId,
            Message = request.Message
        };

        var result = await _chatService.SendMessageAsync(sendRequest);

        if (!result.Success)
        {
            _logger.LogError("Error sending message to chat {ChatId}: {Error}", chatId, result.Error);
            return StatusCode(500, new { Error = result.Error ?? "Failed to send message" });
        }

        return Ok(result.UserMessage);
    }

    private async Task WriteSseEvent(object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }

    // DELETE: api/chat/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChat(string id)
    {
        var success = await _chatService.DeleteChatAsync(id);

        if (!success)
        {
            return NotFound(new { Error = "Chat not found" });
        }

        return NoContent();
    }

    private static string GenerateChatTitle(string firstMessage)
    {
        // Generate a simple title from the first message
        var title = firstMessage.Length > 50
            ? firstMessage[..47] + "..."
            : firstMessage;

        return title;
    }

    // POST: api/chat/stream-sse
    [HttpPost("stream-sse")]
    public async Task StreamChatCompletionSse(
        [FromBody] CreateChatRequest request,
        CancellationToken cancellationToken = default)
    {
        // Set response headers for SSE
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("Connection", "keep-alive");

        string? currentChatId = null;
        string? currentAssistantMessageId = null;
        int currentAssistantSequenceNumber = 0;

        // Generic side-channel forwarder
        async Task ForwardSideChannel(StreamChunkEvent ev)
        {
            var chatId = ev.ChatId;
            var messageId = ev.MessageId;
            var kind = ev.Kind;
            object payload = ev switch
            {
                ReasoningStreamEvent r => new { delta = r.Delta, visibility = r.Visibility?.ToString().ToLowerInvariant() },
                TextStreamEvent t => new { delta = t.Delta, done = t.Done },
                _ => new { delta = ev.Delta, done = ev.Done }
            };

            var envelope = new
            {
                chatId,
                messageId,
                sequenceId = currentAssistantSequenceNumber,
                version = 1,
                ts = DateTime.UtcNow,
                kind,
                payload
            };
            var sid = $"{chatId}:{messageId}:{currentAssistantSequenceNumber}";
            await SendSseEvent("messageupdate", envelope, sid);
        }

        Func<StreamChunkEvent, Task>? sideHandler = null;

        try
        {
            // Use unified service method for both new and existing chats
            var streamRequest = new Services.StreamChatRequest
            {
                ChatId = request.ChatId, // null for new chats, populated for existing
                UserId = request.UserId,
                Message = request.Message,
                SystemPrompt = request.SystemPrompt
            };

            // Get initialization metadata from service
            var initResult = await _chatService.PrepareUnifiedStreamChatAsync(streamRequest);
            currentChatId = initResult.ChatId;
            currentAssistantMessageId = initResult.AssistantMessageId;
            currentAssistantSequenceNumber = initResult.AssistantSequenceNumber;

            // Subscribe to side-channel after IDs are known
            sideHandler = ForwardSideChannel;
            _chatService.SideChannelReceived += sideHandler;

            // Send INIT event (envelope optional kind: 'meta')
            var initEnvelope = new
            {
                chatId = initResult.ChatId,
                messageId = initResult.AssistantMessageId,
                sequenceId = initResult.AssistantSequenceNumber,
                version = 1,
                ts = DateTime.UtcNow,
                kind = "meta",
                payload = new
                {
                    userMessageId = initResult.UserMessageId,
                    assistantMessageId = initResult.AssistantMessageId,
                    userTimestamp = initResult.UserTimestamp,
                    assistantTimestamp = initResult.AssistantTimestamp,
                    userSequenceNumber = initResult.UserSequenceNumber,
                    assistantSequenceNumber = initResult.AssistantSequenceNumber
                }
            };
            var initId = $"{initResult.ChatId}:{initResult.AssistantMessageId}:{initResult.AssistantSequenceNumber}";
            await SendSseEvent("init", initEnvelope, initId);

            // Stream the assistant response using the assistant message created during initialization
            await foreach (var chunk in _chatService.StreamAssistantResponseAsync(
                initResult.ChatId,
                initResult.AssistantMessageId,
                cancellationToken))
            {
                // Text update message
                var updateEnvelope = new
                {
                    chatId = initResult.ChatId,
                    messageId = initResult.AssistantMessageId,
                    sequenceId = initResult.AssistantSequenceNumber,
                    version = 1,
                    ts = DateTime.UtcNow,
                    kind = "text",
                    payload = new { delta = chunk }
                };
                var upId = $"{initResult.ChatId}:{initResult.AssistantMessageId}:{initResult.AssistantSequenceNumber}";
                await SendSseEvent("messageupdate", updateEnvelope, upId);
            }

            // Get final content of assistant message for complete event
            var finalContent = await _chatService.GetMessageContentAsync(initResult.AssistantMessageId);

            // Send completion event with final content
            var completeEnvelope = new
            {
                chatId = initResult.ChatId,
                messageId = initResult.AssistantMessageId,
                sequenceId = initResult.AssistantSequenceNumber,
                version = 1,
                ts = DateTime.UtcNow,
                kind = "text",
                payload = new { content = finalContent }
            };
            var cId = $"{initResult.ChatId}:{initResult.AssistantMessageId}:{initResult.AssistantSequenceNumber}";
            await SendSseEvent("complete", completeEnvelope, cId);
        }
        catch (Exception ex)
        {
            // Avoid passing exception object to logger in watch/Test to prevent formatter crashes
            _logger.LogError("Error streaming chat completion: {Type}: {Message}", ex.GetType().Name, ex.Message);
            var errorEnvelope = new
            {
                chatId = currentChatId,
                messageId = currentAssistantMessageId,
                sequenceId = currentAssistantSequenceNumber,
                version = 1,
                ts = DateTime.UtcNow,
                kind = "error",
                payload = new { message = ex.Message }
            };
            await SendSseEvent("message", errorEnvelope, currentChatId != null && currentAssistantMessageId != null ? $"{currentChatId}:{currentAssistantMessageId}:{currentAssistantSequenceNumber}" : null);
        }
        finally
        {
            if (sideHandler != null)
            {
                _chatService.SideChannelReceived -= sideHandler;
            }
        }
    }

    private async Task SendSseEvent(string eventType, object data, string? id = null)
    {
        // Always stream to the current HTTP response (client fetch())
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        if (!string.IsNullOrEmpty(id))
        {
            await Response.WriteAsync($"id: {id}\n");
        }
        await Response.WriteAsync($"event: {eventType}\n");
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();

        // Additionally, broadcast via IServerSentEventsService if any listeners are connected
        var clients = _serverSentEventsService.GetClients();
        if (clients.Any())
        {
            var client = clients.First();
            var sse = new ServerSentEvent { Type = eventType, Data = new List<string> { json } };
            if (!string.IsNullOrEmpty(id))
            {
                sse.Id = id;
            }
            await client.SendEventAsync(sse);
        }
    }
}

// Request DTOs for API endpoints
public record CreateChatRequest(string? ChatId, string UserId, string Message, string? SystemPrompt);

public class SendMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class ChatHistoryResponse
{
    public List<Services.ChatDto> Chats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}