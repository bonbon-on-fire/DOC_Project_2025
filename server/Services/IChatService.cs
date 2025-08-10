using AchieveAi.LmDotnetTools.LmCore.Messages;
using AIChat.Server.Models;
using System.Text.Json.Serialization;

namespace AIChat.Server.Services;

public interface IChatService
{
    // Chat Management
    Task<ChatResult> CreateChatAsync(CreateChatRequest request);
    Task<ChatResult> GetChatAsync(string chatId);
    Task<ChatHistoryResult> GetChatHistoryAsync(string userId, int page, int pageSize);
    Task<bool> DeleteChatAsync(string chatId);

    // Message Operations
    Task<MessageResult> SendMessageAsync(SendMessageRequest request);
    Task<MessageResult> AddUserMessageToExistingChatAsync(string chatId, string userId, string message);
    Task<StreamInitResult> PrepareStreamChatAsync(StreamChatRequest request);
    Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request);
    IAsyncEnumerable<string> StreamChatCompletionAsync(StreamChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamUnifiedChatCompletionAsync(StreamChatRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<string> StreamAssistantResponseAsync(string chatId, string assistantMessageId, CancellationToken cancellationToken = default);
    Task<int> GetNextSequenceNumberAsync(string chatId);
    Task<string> CreateAssistantMessageForStreamingAsync(string chatId, int sequenceNumber);
    Task<string> GetMessageContentAsync(string messageId);

    // Events for real-time notifications
    event Func<MessageCreatedEvent, Task>? MessageCreated;
    event Func<StreamChunkEvent, Task>? StreamChunkReceived;
    event Func<StreamCompleteEvent, Task>? StreamCompleted;
    event Func<ReasoningStreamEvent, Task>? ReasoningChunkReceived; // NEW: reasoning streaming side-channel
    event Func<StreamChunkEvent, Task>? SideChannelReceived; // NEW: generic side-channel event for all kinds
}

// Request types
public class CreateChatRequest
{
    public required string UserId { get; set; }
    public required string Message { get; set; }
    public string? SystemPrompt { get; set; }
}

public class SendMessageRequest
{
    public required string ChatId { get; set; }
    public required string UserId { get; set; }
    public required string Message { get; set; }
}

public class StreamChatRequest
{
    public string? ChatId { get; set; } // null for new chats, populated for existing chats
    public required string UserId { get; set; }
    public required string Message { get; set; }
    public string? SystemPrompt { get; set; }
}

// Result types
public class ChatResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public ChatDto? Chat { get; set; }
}

public class MessageResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public MessageDto? UserMessage { get; set; }
    public MessageDto? AssistantMessage { get; set; }
}

public class StreamInitResult
{
    public required string ChatId { get; set; }
    public required string UserMessageId { get; set; }
    public required string AssistantMessageId { get; set; }
    public required DateTime UserTimestamp { get; set; }
    public required DateTime AssistantTimestamp { get; set; }
    public required int UserSequenceNumber { get; set; }
    public required int AssistantSequenceNumber { get; set; }
}

public class ChatHistoryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<ChatDto> Chats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

// Event types for real-time notifications
public record MessageCreatedEvent(string ChatId, MessageDto Message);
public abstract record StreamChunkEvent(string ChatId, string MessageId, string Delta, bool Done, string Kind);
public record StreamCompleteEvent(string ChatId, string MessageId, string FullContent);
public record ReasoningStreamEvent(string ChatId, string MessageId, string Delta, ReasoningVisibility? Visibility)
   : StreamChunkEvent(ChatId, MessageId, Delta, false, "reasoning");
public record TextStreamEvent(string ChatId, string MessageId, string Delta, bool Done)
   : StreamChunkEvent(ChatId, MessageId, Delta, Done, "text");

// DTO types (moved from ChatController for reuse)
public class ChatDto
{
    public required string Id { get; set; }
    public required string UserId { get; set; }
    public required string Title { get; set; }
    public List<MessageDto> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

// Enable polymorphic serialization so derived message content (text/reasoning) is included in JSON
[JsonPolymorphic(TypeDiscriminatorPropertyName = "messageType")]
[JsonDerivedType(typeof(TextMessageDto), typeDiscriminator: "text")]
[JsonDerivedType(typeof(ReasoningMessageDto), typeDiscriminator: "reasoning")]
public class MessageDto
{
    public required string Id { get; set; }
    public required string ChatId { get; set; }
    public required string Role { get; set; }
    public DateTime Timestamp { get; set; }
    public int SequenceNumber { get; set; }
}

public class TextMessageDto : MessageDto
{
    public required string Text { get; set; }
}

public class ReasoningMessageDto : MessageDto
{
    public required string Reasoning { get; set; }

    public ReasoningVisibility Visibility { get; set; }

    public string? GetText() => Visibility == ReasoningVisibility.Encrypted ? null : Reasoning;
}
