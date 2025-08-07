using Microsoft.EntityFrameworkCore;
using AIChat.Server.Data;
using AIChat.Server.Models;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Collections.Immutable;
using System.Text;
using System.Runtime.CompilerServices;

namespace AIChat.Server.Services;

public class ChatService : IChatService
{
    private readonly AIChatDbContext _dbContext;
    private readonly IStreamingAgent _streamingAgent;
    private readonly IMessageSequenceService _messageSequenceService;
    private readonly ILogger<ChatService> _logger;

    public ChatService(
        AIChatDbContext dbContext,
        IStreamingAgent streamingAgent,
        IMessageSequenceService messageSequenceService,
        ILogger<ChatService> logger)
    {
        _dbContext = dbContext;
        _streamingAgent = streamingAgent;
        _messageSequenceService = messageSequenceService;
        _logger = logger;
    }

    // Events for real-time notifications
    public event Func<MessageCreatedEvent, Task>? MessageCreated;
    public event Func<StreamChunkEvent, Task>? StreamChunkReceived;
    public event Func<StreamCompleteEvent, Task>? StreamCompleted;

    public async Task<ChatResult> CreateChatAsync(CreateChatRequest request)
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

            // Get sequence numbers for messages
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

            // Generate AI response
            var aiResponse = await GenerateAIResponseAsync(chat.Id);
            
            // Get sequence number for assistant message
            var assistantSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(chat.Id);
            
            // Create assistant message
            var assistantMessage = new Message
            {
                ChatId = chat.Id,
                Role = "assistant",
                Content = aiResponse,
                Timestamp = DateTime.UtcNow,
                SequenceNumber = assistantSequenceNumber
            };

            _dbContext.Messages.Add(assistantMessage);
            chat.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            // Build result
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
                        Timestamp = userMessage.Timestamp,
                        SequenceNumber = userMessage.SequenceNumber
                    },
                    new()
                    {
                        Id = assistantMessage.Id,
                        ChatId = chat.Id,
                        Role = "assistant",
                        Content = aiResponse,
                        Timestamp = assistantMessage.Timestamp,
                        SequenceNumber = assistantMessage.SequenceNumber
                    }
                },
                CreatedAt = chat.CreatedAt,
                UpdatedAt = chat.UpdatedAt
            };

            return new ChatResult { Success = true, Chat = chatDto };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating chat");
            return new ChatResult { Success = false, Error = "Failed to create chat" };
        }
    }

    public async Task<ChatResult> GetChatAsync(string chatId)
    {
        try
        {
            var chat = await _dbContext.Chats
                .Include(c => c.Messages.OrderBy(m => m.SequenceNumber))
                .FirstOrDefaultAsync(c => c.Id == chatId);

            if (chat == null)
            {
                return new ChatResult { Success = false, Error = "Chat not found" };
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

            return new ChatResult { Success = true, Chat = chatDto };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat {ChatId}", chatId);
            return new ChatResult { Success = false, Error = "Failed to retrieve chat" };
        }
    }

    public async Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize)
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

            var chatDtos = chats.Select(c => new ChatDto
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
            }).ToList();

            return new ChatHistoryResult
            {
                Success = true,
                Chats = chatDtos,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving chat history for user {UserId}", userId);
            return new ChatHistoryResult { Success = false, Error = "Failed to retrieve chat history" };
        }
    }

    public async Task<bool> DeleteChatAsync(string chatId)
    {
        try
        {
            var chat = await _dbContext.Chats.FindAsync(chatId);
            
            if (chat == null)
            {
                return false;
            }

            _dbContext.Chats.Remove(chat);
            await _dbContext.SaveChangesAsync();

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting chat {ChatId}", chatId);
            return false;
        }
    }

    public async Task<MessageResult> SendMessageAsync(SendMessageRequest request)
    {
        try
        {
            // Get next sequence number for user message
            var userSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(request.ChatId);
            
            // Save user message to database
            var userMessage = new Message
            {
                ChatId = request.ChatId,
                Role = "user",
                Content = request.Message,
                Timestamp = DateTime.UtcNow,
                SequenceNumber = userSequenceNumber
            };

            _dbContext.Messages.Add(userMessage);
            await _dbContext.SaveChangesAsync();

            // Create user message DTO
            var userMessageDto = new MessageDto
            {
                Id = userMessage.Id,
                ChatId = request.ChatId,
                Role = "user",
                Content = request.Message,
                Timestamp = userMessage.Timestamp,
                SequenceNumber = userMessage.SequenceNumber
            };

            // Publish user message created event
            if (MessageCreated != null)
            {
                await MessageCreated(new MessageCreatedEvent(request.ChatId, userMessageDto));
            }

            // Generate AI response
            var aiResponse = await GenerateAIResponseAsync(request.ChatId);
            
            // Get next sequence number for assistant message
            var assistantSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(request.ChatId);
            
            // Create assistant message
            var assistantMessage = new Message
            {
                ChatId = request.ChatId,
                Role = "assistant",
                Content = aiResponse,
                Timestamp = DateTime.UtcNow,
                SequenceNumber = assistantSequenceNumber
            };

            _dbContext.Messages.Add(assistantMessage);

            // Update chat timestamp
            var chat = await _dbContext.Chats.FindAsync(request.ChatId);
            if (chat != null)
            {
                chat.UpdatedAt = DateTime.UtcNow;
                _dbContext.Chats.Update(chat);
            }

            await _dbContext.SaveChangesAsync();

            // Create assistant message DTO
            var assistantMessageDto = new MessageDto
            {
                Id = assistantMessage.Id,
                ChatId = request.ChatId,
                Role = "assistant",
                Content = aiResponse,
                Timestamp = assistantMessage.Timestamp,
                SequenceNumber = assistantMessage.SequenceNumber
            };

            // Publish assistant message created event
            if (MessageCreated != null)
            {
                await MessageCreated(new MessageCreatedEvent(request.ChatId, assistantMessageDto));
            }

            return new MessageResult
            {
                Success = true,
                UserMessage = userMessageDto,
                AssistantMessage = assistantMessageDto
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message for chat {ChatId}", request.ChatId);
            return new MessageResult { Success = false, Error = "Failed to send message" };
        }
    }

    public async Task<StreamInitResult> PrepareStreamChatAsync(StreamChatRequest request)
    {
        // Create new chat for streaming
        var chat = new Chat
        {
            UserId = request.UserId,
            Title = GenerateChatTitle(request.Message),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Chats.Add(chat);
        
        // IMPORTANT: Save chat to database FIRST before getting sequence numbers
        await _dbContext.SaveChangesAsync();

        // Get sequence numbers and create user message
        var userSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(chat.Id);
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
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                SequenceNumber = systemSequenceNumber
            };

            _dbContext.Messages.Add(systemMessage);
        }

        // Save user message and system prompt
        await _dbContext.SaveChangesAsync();

        // Get sequence number for assistant message
        var assistantSequenceNumber = await _messageSequenceService.GetNextSequenceNumberAsync(chat.Id);
        
        // Create assistant message
        var assistantMessage = new Message
        {
            ChatId = chat.Id,
            Role = "assistant",
            Content = "",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = assistantSequenceNumber
        };

        _dbContext.Messages.Add(assistantMessage);

        // Save all messages including assistant message
        await _dbContext.SaveChangesAsync();

        // Return the initialization metadata
        return new StreamInitResult
        {
            ChatId = chat.Id,
            UserMessageId = userMessage.Id,
            AssistantMessageId = assistantMessage.Id,
            UserTimestamp = userMessage.Timestamp,
            AssistantTimestamp = assistantMessage.Timestamp,
            UserSequenceNumber = userMessage.SequenceNumber,
            AssistantSequenceNumber = assistantMessage.SequenceNumber
        };
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        StreamChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Prepare the chat first
        var initResult = await PrepareStreamChatAsync(request);
        
        // Retrieve the chat and messages from database
        var chat = await _dbContext.Chats.FindAsync(initResult.ChatId);
        if (chat == null)
        {
            throw new InvalidOperationException($"Chat with ID {initResult.ChatId} not found.");
        }
        
        var userMessage = await _dbContext.Messages.FindAsync(initResult.UserMessageId);
        if (userMessage == null)
        {
            throw new InvalidOperationException($"User message with ID {initResult.UserMessageId} not found.");
        }
        
        // Get conversation history for AI context
        var conversationHistory = await _dbContext.Messages
            .Where(m => m.ChatId == chat.Id)
            .OrderBy(m => m.SequenceNumber)
            .ToListAsync(cancellationToken);

        // Convert to LM messages
        var lmMessages = conversationHistory.Select(ConvertToLmMessage).ToList();

        // Generate streaming response
        _logger.LogInformation("Making API call to LLM for chat {ChatId}", chat.Id);
        var options = new GenerateReplyOptions { ModelId = "openrouter/horizon-beta" };
        var streamingResponse = await _streamingAgent.GenerateReplyStreamingAsync(
            lmMessages,
            options,
            cancellationToken: cancellationToken);
        _logger.LogInformation("Received response from LLM for chat {ChatId}", chat.Id);

        // Retrieve the existing assistant message using the ID from initialization
        var assistantMessage = await _dbContext.Messages
            .FirstOrDefaultAsync(m => m.Id == initResult.AssistantMessageId, cancellationToken);

        // If the assistant message doesn't exist, create it
        if (assistantMessage == null)
        {
            assistantMessage = new Message
            {
                Id = initResult.AssistantMessageId,
                ChatId = chat.Id,
                Role = "assistant",
                Content = "",
                Timestamp = initResult.AssistantTimestamp,
                SequenceNumber = initResult.AssistantSequenceNumber
            };
            _dbContext.Messages.Add(assistantMessage);
        }
        await _dbContext.SaveChangesAsync();

        // Stream response and accumulate content
        var responseBuffer = new StringBuilder();
        
        await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
        {
            if (message is TextUpdateMessage textMessage)
            {
                var content = textMessage.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    // Yield content for streaming
                    yield return content;
                    
                    // Accumulate for final save
                    responseBuffer.Append(content);

                    // Publish stream chunk event
                    if (StreamChunkReceived != null)
                    {
                        await StreamChunkReceived(new StreamChunkEvent(chat.Id, assistantMessage.Id, content, false));
                    }
                }
            }
        }

        // Save the complete assistant response
        assistantMessage.Content = responseBuffer.ToString();
        _dbContext.Messages.Update(assistantMessage);
        chat.UpdatedAt = DateTime.UtcNow;
        await _dbContext.SaveChangesAsync();

        // Publish stream complete event
        if (StreamCompleted != null)
        {
            await StreamCompleted(new StreamCompleteEvent(chat.Id, assistantMessage.Id, assistantMessage.Content));
        }
    }

    // Helper methods
    private async Task<string> GenerateAIResponseAsync(string chatId)
    {
        try
        {
            // Get conversation history for context
            var conversationHistory = await _dbContext.Messages
                .Where(m => m.ChatId == chatId)
                .OrderBy(m => m.SequenceNumber)
                .ToListAsync();

            var lmMessages = conversationHistory.Select(ConvertToLmMessage).ToList();
            
            // Generate response using IStreamingAgent (non-streaming)
            var options = new GenerateReplyOptions { ModelId = "moonshotai/kimi-k2" };
            var messages = await _streamingAgent.GenerateReplyAsync(lmMessages, options);
            return string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI response");
            return $"Error: Failed to generate AI response. {ex.Message}";
        }
    }

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
            Metadata = ImmutableDictionary<string, object>.Empty
        };
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
