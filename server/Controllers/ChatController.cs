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

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly AIChatDbContext _dbContext;
    private readonly ILogger<ChatController> _logger;
    private readonly IStreamingAgent _streamingAgent;
    private readonly IServerSentEventsService _serverSentEventsService;
    private readonly IMessageSequenceService _messageSequenceService;

    public ChatController(
        AIChatDbContext dbContext, 
        ILogger<ChatController> logger, 
        IStreamingAgent streamingAgent, 
        IServerSentEventsService serverSentEventsService,
        IMessageSequenceService messageSequenceService)
    {
        _dbContext = dbContext;
        _logger = logger;
        _streamingAgent = streamingAgent;
        _serverSentEventsService = serverSentEventsService;
        _messageSequenceService = messageSequenceService;
    }

    // GET: api/chat/history?userId={userId}&page={page}&pageSize={pageSize}
    [HttpGet("history")]
    public async Task<ActionResult<ChatHistoryResponse>> GetChatHistory(
        [FromQuery] string userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var totalCount = await _dbContext.Chats
                .Where(c => c.UserId == userId)
                .CountAsync();

            var chats = await _dbContext.Chats
                .Where(c => c.UserId == userId)
                .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
                .OrderByDescending(c => c.UpdatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var response = new ChatHistoryResponse
            {
                Chats = chats.Select(c => new ChatDto
                {
                    Id = c.Id,
                    UserId = c.UserId,
                    Title = c.Title,
                    Messages = c.Messages.Select(m => new MessageDto
                    {
                        Id = m.Id,
                        ChatId = m.ChatId,
                        Role = m.Role,
                        Content = m.Content,
                        Timestamp = m.Timestamp,
                        SequenceNumber = m.SequenceNumber
                    }).ToList(),
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt
                }).ToList(),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat history for user {UserId}", userId);
            return StatusCode(500, new { Error = "Failed to retrieve chat history" });
        }
    }

    // GET: api/chat/{id}
    [HttpGet("{id}")]
    public async Task<ActionResult<ChatDto>> GetChat(string id)
    {
        try
        {
            var chat = await _dbContext.Chats
                .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
                .FirstOrDefaultAsync(c => c.Id == id);

            if (chat == null)
            {
                return NotFound(new { Error = "Chat not found" });
            }

            var chatDto = new ChatDto
            {
                Id = chat.Id,
                UserId = chat.UserId,
                Title = chat.Title,
                Messages = chat.Messages.Select(m => new MessageDto
                {
                    Id = m.Id,
                    ChatId = m.ChatId,
                    Role = m.Role,
                    Content = m.Content,
                    Timestamp = m.Timestamp,
                    SequenceNumber = m.SequenceNumber
                }).ToList(),
                CreatedAt = chat.CreatedAt,
                UpdatedAt = chat.UpdatedAt
            };

            return Ok(chatDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat {ChatId}", id);
            return StatusCode(500, new { Error = "Failed to retrieve chat" });
        }
    }

    // POST: api/chat
    [HttpPost]
    public async Task<ActionResult<ChatDto>> CreateChat([FromBody] CreateChatRequest request)
    {
        try
        {
            // Create new chat
            var chat = new Chat
            {
                UserId = request.UserId,
                Title = GenerateChatTitle(request.Message),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Chats.Add(chat);

            // Add initial user message
            var userMessage = new Message
            {
                ChatId = chat.Id,
                Role = "user",
                Content = request.Message,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.Messages.Add(userMessage);

            // Add system prompt if provided
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                var systemMessage = new Message
                {
                    ChatId = chat.Id,
                    Role = "system",
                    Content = request.SystemPrompt,
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-1) // Ensure system message comes first
                };

                _dbContext.Messages.Add(systemMessage);
            }

            await _dbContext.SaveChangesAsync();

            string aiResponse;
            if (_streamingAgent != null)
            {
                // Use IStreamingAgent for AI response
                try
                {
                    // Get conversation history for context
                    var conversationHistory = await _dbContext.Messages
                        .Where(m => m.ChatId == chat.Id)
                        .OrderBy(m => m.Timestamp)
                        .ToListAsync();

                    var lmMessages = conversationHistory.Select(ConvertToLmMessage).ToList();
                    
                    // Generate response using IStreamingAgent (non-streaming)
                    var options = new GenerateReplyOptions { ModelId = "moonshotai/kimi-k2" };
                    var messages = await _streamingAgent.GenerateReplyAsync(lmMessages, options);
                    aiResponse = string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating AI response with IStreamingAgent");
                    aiResponse = $"Error: Failed to generate AI response. {ex.Message}";
                }
            }
            else
            {
                // Fallback to existing OpenAIService
                try
                {
                    // Get conversation history for context
                    var conversationHistory = await _dbContext.Messages
                        .Where(m => m.ChatId == chat.Id)
                        .OrderBy(m => m.Timestamp)
                        .ToListAsync();
                    aiResponse = "Error: No AI service available.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error generating AI response with OpenAIService");
                    aiResponse = $"Error: Failed to generate AI response. {ex.Message}";
                }
            }
            
            var assistantMessage = new Message
            {
                ChatId = chat.Id,
                Role = "assistant",
                Content = aiResponse,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.Messages.Add(assistantMessage);
            chat.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Return complete chat
            var chatDto = new ChatDto
            {
                Id = chat.Id,
                UserId = chat.UserId,
                Title = chat.Title,
                Messages = new List<MessageDto>
                {
                    new()
                    {
                        Id = userMessage.Id,
                        ChatId = chat.Id,
                        Role = "user",
                        Content = request.Message,
                        Timestamp = userMessage.Timestamp
                    },
                    new()
                    {
                        Id = assistantMessage.Id,
                        ChatId = chat.Id,
                        Role = "assistant",
                        Content = aiResponse,
                        Timestamp = assistantMessage.Timestamp
                    }
                },
                CreatedAt = chat.CreatedAt,
                UpdatedAt = chat.UpdatedAt
            };

            return CreatedAtAction(nameof(GetChat), new { id = chat.Id }, chatDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chat");
            return StatusCode(500, new { Error = "Failed to create chat" });
        }
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
        // var userId = "user123"; // Placeholder
        
        try
        {
            // Save user message to database
            var userMessage = new Message
            {
                ChatId = chatId,
                Role = "user",
                Content = request.Message,
                Timestamp = DateTime.UtcNow
            };

            _dbContext.Messages.Add(userMessage);
            await _dbContext.SaveChangesAsync();

            var messageDto = new MessageDto
            {
                Id = userMessage.Id,
                ChatId = userMessage.ChatId,
                Role = userMessage.Role,
                Content = userMessage.Content,
                Timestamp = userMessage.Timestamp
            };

            return Ok(messageDto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to chat {ChatId}", chatId);
            return StatusCode(500, new { Error = "Failed to send message" });
        }
    }

    // POST: api/chat/stream
    [HttpPost("stream")]
    public async IAsyncEnumerable<string> StreamChatCompletion(
        [FromBody] CreateChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (_streamingAgent == null)
        {
            _logger.LogWarning("Streaming agent not configured");
            yield return "Error: Streaming agent not configured";
            yield break;
        }
        await foreach (var content in StreamChatCompletionImpl(request, cancellationToken))
        {
            yield return content;
        }
    }

    private async Task WriteSseEvent(object data)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        await Response.WriteAsync($"data: {json}\n\n");
        await Response.Body.FlushAsync();
    }

    private async IAsyncEnumerable<string> StreamChatCompletionImpl(
        CreateChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Chat chat;
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            chat = await _dbContext.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
            if (chat == null)
            {
                yield return "Error: Chat not found";
                yield break;
            }
        }
        else
        {
            chat = new Chat
            {
                UserId = request.UserId,
                Title = GenerateChatTitle(request.Message),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Chats.Add(chat);
        }

        // Add initial user message
        var userMessage = new Message
        {
            ChatId = chat.Id,
            Role = "user",
            Content = request.Message,
            Timestamp = DateTime.UtcNow
        };

        _dbContext.Messages.Add(userMessage);

        // Add system prompt if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            var systemMessage = new Message
            {
                ChatId = chat.Id,
                Role = "system",
                Content = request.SystemPrompt,
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1) // Ensure system message comes first
            };

            _dbContext.Messages.Add(systemMessage);
        }

        await _dbContext.SaveChangesAsync();

        // Convert project messages to LmDotnetTools messages
        var conversationHistory = await _dbContext.Messages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.Timestamp)
            .ToListAsync(cancellationToken);

        var lmMessages = conversationHistory.Select(ConvertToLmMessage).ToList();

        // Generate streaming response using IStreamingAgent
        var options = new GenerateReplyOptions { ModelId = "moonshotai/kimi-k2" };
        var streamingResponse = await _streamingAgent!.GenerateReplyStreamingAsync(
            lmMessages,
            options,
            cancellationToken: cancellationToken);

        // Buffer to accumulate partial responses for saving to database
        var responseBuffer = new StringBuilder();
        var assistantMessage = new Message
        {
            ChatId = chat.Id,
            Role = "assistant",
            Content = "",
            Timestamp = DateTime.UtcNow
        };

        await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
        {
            if (message is TextMessage textMessage)
            {
                var content = textMessage.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    // Yield the content for streaming to client
                    yield return content;
                    
                    // Accumulate content for saving
                    responseBuffer.Append(content);
                }
            }
        }

        // Save the complete assistant response
        assistantMessage.Content = responseBuffer.ToString();
        _dbContext.Messages.Add(assistantMessage);
        chat.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();
    }

    // Helper method to convert project Message to LmDotnetTools IMessage
    private static IMessage ConvertToLmMessage(Message message)
    {
        var role = message.Role.ToLowerInvariant() switch
        {
            "user" => Role.User,
            "assistant" => Role.Assistant,
            "system" => Role.System,
            _ => Role.User
        };

        return new TextMessage
        {
            Role = role,
            Text = message.Content,
            FromAgent = null,
            GenerationId = null,
            Metadata = null,
            IsThinking = false
        };
    }

    // DELETE: api/chat/{id}
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteChat(string id)
    {
        try
        {
            var chat = await _dbContext.Chats.FindAsync(id);
            
            if (chat == null)
            {
                return NotFound(new { Error = "Chat not found" });
            }

            _dbContext.Chats.Remove(chat);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting chat {ChatId}", id);
            return StatusCode(500, new { Error = "Failed to delete chat" });
        }
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

        Chat chat;
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            chat = await _dbContext.Chats.FirstOrDefaultAsync(c => c.Id == request.ChatId, cancellationToken);
            if (chat == null)
            {
                var errorEvent = new { type = "error", message = "Chat not found" };
                await SendSseEvent("error", errorEvent);
                return;
            }
        }
        else
        {
            chat = new Chat
            {
                UserId = request.UserId,
                Title = GenerateChatTitle(request.Message),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _dbContext.Chats.Add(chat);
            await _dbContext.SaveChangesAsync();
        }

        // Get sequence number for user message
        var userSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(chat.Id);
        
        // Add initial user message
        var userMessage = new Message
        {
            ChatId = chat.Id,
            Role = "user",
            Content = request.Message,
            Timestamp = DateTime.UtcNow,
            SequenceNumber = userSequenceNumber
        };

        _dbContext.Messages.Add(userMessage);

        // Add system prompt if provided
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            var systemSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(chat.Id);
            var systemMessage = new Message
            {
                ChatId = chat.Id,
                Role = "system",
                Content = request.SystemPrompt,
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1), // Ensure system message comes first
                SequenceNumber = systemSequenceNumber
            };

            _dbContext.Messages.Add(systemMessage);
        }

        await _dbContext.SaveChangesAsync();

        // Get sequence number for assistant message
        var assistantSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(chat.Id);
        
        // Create assistant message placeholder
        var assistantMessage = new Message
        {
            ChatId = chat.Id,
            Role = "assistant",
            Content = "",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = assistantSequenceNumber
        };

        _dbContext.Messages.Add(assistantMessage);
        await _dbContext.SaveChangesAsync();

        // Send initial event with chat and message IDs and timestamps
        var initEvent = new { 
            type = "init", 
            chatId = chat.Id, 
            messageId = assistantMessage.Id,
            userMessageId = userMessage.Id,
            userTimestamp = userMessage.Timestamp,
            assistantTimestamp = assistantMessage.Timestamp,
            userSequenceNumber = userMessage.SequenceNumber,
            assistantSequenceNumber = assistantMessage.SequenceNumber
        };
        await SendSseEvent("init", initEvent);

        // Stream the response
        try
        {
            // Get conversation history for context
            var conversationHistory = await _dbContext.Messages
                .Where(m => m.ChatId == chat.Id)
                .OrderBy(m => m.SequenceNumber)
                .ToListAsync(cancellationToken);

            var lmMessages = conversationHistory.Select(ConvertToLmMessage).ToList();
            
            // Generate response using IStreamingAgent (streaming)
            var options = new GenerateReplyOptions { ModelId = "moonshotai/kimi-k2" };
            
            var fullResponse = "";
            var responseStream = await _streamingAgent.GenerateReplyStreamingAsync(
                lmMessages,
                new GenerateReplyOptions
                {
                    ModelId = "moonshotai/kimi-k2"
                },
                cancellationToken);

            await foreach (var message in responseStream.WithCancellation(cancellationToken))
            {
                if (message is TextUpdateMessage textMessage)
                {
                    var chunkEvent = new { type = "chunk", delta = textMessage.Text, done = false };
                    await SendSseEvent("chunk", chunkEvent);
                    fullResponse += textMessage.Text;
                }
            }

            // Update the assistant message with the full response
            assistantMessage.Content = fullResponse;
            await _dbContext.SaveChangesAsync();

            // Send completion event
            var completeEvent = new { type = "complete", content = fullResponse, done = true };
            await SendSseEvent("complete", completeEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming chat completion for chat {ChatId}", chat.Id);
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

// DTOs for API responses
public class ChatDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<MessageDto> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class MessageDto
{
    public string Id { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public int SequenceNumber { get; set; }
}

public record CreateChatRequest(string? ChatId, string UserId, string Message, string? SystemPrompt);

public class SendMessageRequest
{
    public string Message { get; set; } = string.Empty;
}

public class ChatHistoryResponse
{
    public List<ChatDto> Chats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}