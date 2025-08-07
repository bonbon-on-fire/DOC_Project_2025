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

        try
        {
            var streamRequest = new Services.StreamChatRequest
            {
                UserId = request.UserId,
                Message = request.Message,
                SystemPrompt = request.SystemPrompt
            };

            // Prepare chat and get initialization metadata
            var initResult = await _chatService.PrepareStreamChatAsync(streamRequest);

            // Send fully decorated initial event
            var initEvent = new 
            { 
                type = "init",
                chatId = initResult.ChatId,
                userMessageId = initResult.UserMessageId,
                messageId = initResult.AssistantMessageId,  // For backward compatibility
                assistantMessageId = initResult.AssistantMessageId,
                userTimestamp = initResult.UserTimestamp,
                assistantTimestamp = initResult.AssistantTimestamp,
                userSequenceNumber = initResult.UserSequenceNumber,
                assistantSequenceNumber = initResult.AssistantSequenceNumber
            };
            await SendSseEvent("init", initEvent);

            // Stream the response using ChatService
            await foreach (var chunk in _chatService.StreamChatCompletionAsync(streamRequest, cancellationToken))
            {
                var chunkEvent = new { type = "chunk", delta = chunk, done = false };
                await SendSseEvent("chunk", chunkEvent);
            }

            // Send completion event with final content
            var completeEvent = new { type = "complete", done = true };
            await SendSseEvent("complete", completeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chat completion");
            var errorEvent = new { type = "error", message = ex.Message };
            await SendSseEvent("error", errorEvent);
        }
    }

    private async Task SendSseEvent(string eventType, object data)
    {
        // For now, we'll send to all connected clients
        // In a real implementation, you'd want to send to specific clients
        var clients = _serverSentEventsService.GetClients();
        if (clients.Any())
        {
            var client = clients.First();
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            await client.SendEventAsync(new ServerSentEvent { Type = eventType, Data = new List<string> { json } });
        }
        else
        {
            // Fallback to manual SSE if no clients connected
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            await Response.WriteAsync($"event: {eventType}\n");
            await Response.WriteAsync($"data: {json}\n\n");
            await Response.Body.FlushAsync();
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