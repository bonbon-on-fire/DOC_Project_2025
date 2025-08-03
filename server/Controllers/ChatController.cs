using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AIChat.Server.Data;
using AIChat.Server.Models;
using AIChat.Server.Services;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly AIChatDbContext _dbContext;
    private readonly IOpenAIService _openAIService;
    private readonly ILogger<ChatController> _logger;

    public ChatController(AIChatDbContext dbContext, IOpenAIService openAIService, ILogger<ChatController> logger)
    {
        _dbContext = dbContext;
        _openAIService = openAIService;
        _logger = logger;
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
                .Include(c => c.Messages.OrderBy(m => m.Timestamp))
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
                        Timestamp = m.Timestamp
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
                .Include(c => c.Messages.OrderBy(m => m.Timestamp))
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
                    Timestamp = m.Timestamp
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

            // Mock AI response for testing (replace with real OpenAI integration later)
            var mockAiResponse = $"Hello! I'm an AI assistant. You said: \"{request.Message}\". This is a mock response for testing purposes. The chat functionality is working perfectly!";
            
            var assistantMessage = new Message
            {
                ChatId = chat.Id,
                Role = "assistant",
                Content = mockAiResponse,
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
                        Content = mockAiResponse,
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
}

public class CreateChatRequest
{
    public string UserId { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? SystemPrompt { get; set; }
}

public class ChatHistoryResponse
{
    public List<ChatDto> Chats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}