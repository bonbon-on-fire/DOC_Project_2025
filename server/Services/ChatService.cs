using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Collections.Immutable;
using System.Text;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using AIChat.Server.Storage;
using System.Text.Json;
using AIChat.Server.Models;

namespace AIChat.Server.Services;

public class ChatService : IChatService
{
    private readonly IChatStorage _storage;
    private readonly IStreamingAgent _streamingAgent;
    private readonly ILogger<ChatService> _logger;
    private readonly AiOptions _aiOptions;

    public ChatService(
        IChatStorage storage,
        IStreamingAgent streamingAgent,
        ILogger<ChatService> logger,
        IOptions<AiOptions> aiOptions)
    {
        _storage = storage;
        _streamingAgent = streamingAgent;
        _logger = logger;
        _aiOptions = aiOptions.Value;
    }

    // Events for real-time notifications
    public event Func<MessageCreatedEvent, Task>? MessageCreated;
    public event Func<StreamChunkEvent, Task>? StreamChunkReceived;
    public event Func<StreamCompleteEvent, Task>? StreamCompleted;
    public event Func<ReasoningStreamEvent, Task>? ReasoningChunkReceived;
    public event Func<StreamChunkEvent, Task>? SideChannelReceived;

    public async Task<ChatResult> CreateChatAsync(CreateChatRequest request)
    {
        try
        {
            var now = DateTime.UtcNow;
            var title = GenerateChatTitle(request.Message);
            var create = await _storage.CreateChatAsync(request.UserId, title, now, now, null);
            if (!create.Success || create.Chat == null)
            {
                return new ChatResult { Success = false, Error = create.Error ?? "Failed to create chat" };
            }

            var chat = create.Chat;

            // Insert initial user message
            var userSeq = await _storage.AllocateSequenceAsync(chat.Id);
            if (!userSeq.Success) return new ChatResult { Success = false, Error = userSeq.Error };
            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = userSeq.NextSequence,
                Text = request.Message
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chat.Id,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(userDto)
            };
            var insUser = await _storage.InsertMessageAsync(userRecord);
            if (!insUser.Success) return new ChatResult { Success = false, Error = insUser.Error };

            // Optional system prompt
            if (!string.IsNullOrEmpty(request.SystemPrompt))
            {
                var sysSeq = await _storage.AllocateSequenceAsync(chat.Id);
                if (!sysSeq.Success) return new ChatResult { Success = false, Error = sysSeq.Error };
                var sysDto = new TextMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat.Id,
                    Role = "system",
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                    SequenceNumber = sysSeq.NextSequence,
                    Text = request.SystemPrompt!
                };
                var sysRecord = new MessageRecord
                {
                    Id = sysDto.Id,
                    ChatId = chat.Id,
                    Role = sysDto.Role,
                    Kind = "text",
                    TimestampUtc = sysDto.Timestamp,
                    SequenceNumber = sysDto.SequenceNumber,
                    MessageJson = JsonSerializer.Serialize<MessageDto>(sysDto)
                };
                var insSys = await _storage.InsertMessageAsync(sysRecord);
                if (!insSys.Success) return new ChatResult { Success = false, Error = insSys.Error };
            }

            // Generate AI response
            var aiResponse = await GenerateAIResponseAsync(chat.Id);

            // Insert assistant message
            var asq = await _storage.AllocateSequenceAsync(chat.Id);
            if (!asq.Success) return new ChatResult { Success = false, Error = asq.Error };
            var assistantDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = asq.NextSequence,
                Text = aiResponse
            };
            var assistantRecord = new MessageRecord
            {
                Id = assistantDto.Id,
                ChatId = chat.Id,
                Role = assistantDto.Role,
                Kind = "text",
                TimestampUtc = assistantDto.Timestamp,
                SequenceNumber = assistantDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(assistantDto)
            };
            var insAsst = await _storage.InsertMessageAsync(assistantRecord);
            if (!insAsst.Success) return new ChatResult { Success = false, Error = insAsst.Error };

            await _storage.UpdateChatUpdatedAtAsync(chat.Id, DateTime.UtcNow);

            // Build DTO with ordered messages
            var list = await _storage.ListChatMessagesOrderedAsync(chat.Id);
            var messages = list.Success
                ? list.Messages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!).ToList()
                : new List<MessageDto> { userDto, assistantDto };

            return new ChatResult
            {
                Success = true,
                Chat = new ChatDto
                {
                    Id = chat.Id,
                    UserId = chat.UserId,
                    Title = chat.Title,
                    Messages = messages,
                    CreatedAt = chat.CreatedAtUtc,
                    UpdatedAt = DateTime.UtcNow
                }
            };
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
            var chat = await _storage.GetChatByIdAsync(chatId);
            if (!chat.Success || chat.Chat == null)
            {
                return new ChatResult { Success = false, Error = chat.Error ?? "Chat not found" };
            }
            var list = await _storage.ListChatMessagesOrderedAsync(chatId);
            var messages = list.Success
                ? list.Messages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!).ToList()
                : new List<MessageDto>();

            return new ChatResult
            {
                Success = true,
                Chat = new ChatDto
                {
                    Id = chat.Chat.Id,
                    UserId = chat.Chat.UserId,
                    Title = chat.Chat.Title,
                    Messages = messages,
                    CreatedAt = chat.Chat.CreatedAtUtc,
                    UpdatedAt = chat.Chat.UpdatedAtUtc
                }
            };
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
            var res = await _storage.GetChatHistoryByUserAsync(userId, page, pageSize);
            var chats = res.Success ? res.Chats : Array.Empty<ChatRecord>();

            var chatDtos = new List<ChatDto>(chats.Count);
            foreach (var c in chats)
            {
                var list = await _storage.ListChatMessagesOrderedAsync(c.Id);
                var messages = list.Success
                    ? list.Messages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!).ToList()
                    : new List<MessageDto>();

                chatDtos.Add(new ChatDto
                {
                    Id = c.Id,
                    UserId = c.UserId,
                    Title = c.Title,
                    Messages = messages,
                    CreatedAt = c.CreatedAtUtc,
                    UpdatedAt = c.UpdatedAtUtc
                });
            }

            return new ChatHistoryResult
            {
                Success = true,
                Chats = chatDtos,
                TotalCount = res.TotalCount,
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
            var res = await _storage.DeleteChatAsync(chatId);
            return res.Success;
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
            var userSeq = await _storage.AllocateSequenceAsync(request.ChatId);
            if (!userSeq.Success) return new MessageResult { Success = false, Error = userSeq.Error };

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = userSeq.NextSequence,
                Text = request.Message
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = request.ChatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(userDto)
            };
            var insUser = await _storage.InsertMessageAsync(userRecord);
            if (!insUser.Success) return new MessageResult { Success = false, Error = insUser.Error };

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent(request.ChatId, userDto));

            var aiResponse = await GenerateAIResponseAsync(request.ChatId);

            var asq = await _storage.AllocateSequenceAsync(request.ChatId);
            if (!asq.Success) return new MessageResult { Success = false, Error = asq.Error };

            var assistantDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = request.ChatId,
                Role = "assistant",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = asq.NextSequence,
                Text = aiResponse
            };
            var assistantRecord = new MessageRecord
            {
                Id = assistantDto.Id,
                ChatId = request.ChatId,
                Role = assistantDto.Role,
                Kind = "text",
                TimestampUtc = assistantDto.Timestamp,
                SequenceNumber = assistantDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(assistantDto)
            };
            var insAsst = await _storage.InsertMessageAsync(assistantRecord);
            if (!insAsst.Success) return new MessageResult { Success = false, Error = insAsst.Error };

            await _storage.UpdateChatUpdatedAtAsync(request.ChatId, DateTime.UtcNow);

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent(request.ChatId, assistantDto));

            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = assistantDto
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
        var now = DateTime.UtcNow;
        var createRes = await _storage.CreateChatAsync(request.UserId, GenerateChatTitle(request.Message), now, now, null);
        if (!createRes.Success || createRes.Chat == null) throw new InvalidOperationException(createRes.Error ?? "Create chat failed");
        var chatId = createRes.Chat.Id;

        var userSeq = await _storage.AllocateSequenceAsync(chatId);
        if (!userSeq.Success) throw new InvalidOperationException(userSeq.Error);
        var userDto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "user",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = userSeq.NextSequence,
            Text = request.Message
        };
        await _storage.InsertMessageAsync(new MessageRecord
        {
            Id = userDto.Id,
            ChatId = chatId,
            Role = userDto.Role,
            Kind = "text",
            TimestampUtc = userDto.Timestamp,
            SequenceNumber = userDto.SequenceNumber,
            MessageJson = JsonSerializer.Serialize<MessageDto>(userDto)
        });

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            var sysSeq = await _storage.AllocateSequenceAsync(chatId);
            var sysDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "system",
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                SequenceNumber = sysSeq.NextSequence,
                Text = request.SystemPrompt!
            };
            await _storage.InsertMessageAsync(new MessageRecord
            {
                Id = sysDto.Id,
                ChatId = chatId,
                Role = sysDto.Role,
                Kind = "text",
                TimestampUtc = sysDto.Timestamp,
                SequenceNumber = sysDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(sysDto)
            });
        }

        var asq = await _storage.AllocateSequenceAsync(chatId);
        var assistantDto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "assistant",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = asq.NextSequence,
            Text = ""
        };
        await _storage.InsertMessageAsync(new MessageRecord
        {
            Id = assistantDto.Id,
            ChatId = chatId,
            Role = assistantDto.Role,
            Kind = "text",
            TimestampUtc = assistantDto.Timestamp,
            SequenceNumber = assistantDto.SequenceNumber,
            MessageJson = JsonSerializer.Serialize<MessageDto>(assistantDto)
        });

        return new StreamInitResult
        {
            ChatId = chatId,
            UserMessageId = userDto.Id,
            AssistantMessageId = assistantDto.Id,
            UserTimestamp = userDto.Timestamp,
            AssistantTimestamp = assistantDto.Timestamp,
            UserSequenceNumber = userDto.SequenceNumber,
            AssistantSequenceNumber = assistantDto.SequenceNumber
        };
    }

    public async IAsyncEnumerable<string> StreamChatCompletionAsync(
        StreamChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var init = await PrepareStreamChatAsync(request);
        var chatId = init.ChatId;
        var assistantMessageId = init.AssistantMessageId;

        // Build conversation history from storage (exclude empty contents)
        var convo = await _storage.ListChatMessagesOrderedAsync(chatId);
        var history = convo.Messages
            .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!)
            .Where(d => d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
            .ToList();
        var lmMessages = history.Select(ConvertToLmMessage).ToList();

        _logger.LogInformation("Making API call to LLM for chat {ChatId}", chatId);
        var options = new GenerateReplyOptions { ModelId = GetModelId() };
        var streamingResponse = await _streamingAgent.GenerateReplyStreamingAsync(
            lmMessages,
            options,
            cancellationToken: cancellationToken);
        _logger.LogInformation("Received response from LLM for chat {ChatId}", chatId);

        var responseBuffer = new StringBuilder();
        var hadAnyTextChunk = false;
        var reasoningBuffer = new StringBuilder();
        ReasoningVisibility? latestReasoningVisibility = null;

        await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
        {
            if (message is TextUpdateMessage textMessage)
            {
                var content = textMessage.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                    responseBuffer.Append(content);
                    hadAnyTextChunk = true;
                    if (StreamChunkReceived != null)
                    {
                        await StreamChunkReceived(new TextStreamEvent(chatId, assistantMessageId, content, false));
                    }
                }
            }
            else if (message is ReasoningUpdateMessage reasoningUpdate)
            {
                if (reasoningUpdate.Visibility == ReasoningVisibility.Encrypted) continue;
                var delta = reasoningUpdate.Reasoning;
                if (!string.IsNullOrEmpty(delta))
                {
                    reasoningBuffer.Append(delta);
                    latestReasoningVisibility = reasoningUpdate.Visibility;
                    if (ReasoningChunkReceived != null)
                    {
                        await ReasoningChunkReceived(new ReasoningStreamEvent(chatId, assistantMessageId, delta, reasoningUpdate.Visibility));
                    }
                    if (SideChannelReceived != null)
                    {
                        await SideChannelReceived(new ReasoningStreamEvent(chatId, assistantMessageId, delta, reasoningUpdate.Visibility));
                    }
                }
            }
            else if (message is ReasoningMessage reasoning)
            {
                if (reasoning.Visibility == ReasoningVisibility.Encrypted) continue;
                var delta = reasoning.Reasoning;
                if (!string.IsNullOrEmpty(delta))
                {
                    reasoningBuffer.Append(delta);
                    latestReasoningVisibility = reasoning.Visibility;
                    if (ReasoningChunkReceived != null)
                    {
                        await ReasoningChunkReceived(new ReasoningStreamEvent(chatId, assistantMessageId, delta, reasoning.Visibility));
                    }
                    if (SideChannelReceived != null)
                    {
                        await SideChannelReceived(new ReasoningStreamEvent(chatId, assistantMessageId, delta, reasoning.Visibility));
                    }
                }
            }
        }

        if (!hadAnyTextChunk)
        {
            try
            {
                var fallbackOptions = new GenerateReplyOptions { ModelId = GetModelId() };
                var messages = await _streamingAgent.GenerateReplyAsync(lmMessages, fallbackOptions, cancellationToken);
                var fullText = string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
                if (!string.IsNullOrEmpty(fullText))
                {
                    responseBuffer.Append(fullText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-streaming fallback failed for chat {ChatId}", chatId);
            }
        }

        // Persist accumulated reasoning (if any) as a separate message
        if (reasoningBuffer.Length > 0)
        {
            var seqAlloc = await _storage.AllocateSequenceAsync(chatId);
            if (seqAlloc.Success)
            {
                var reasoningDto = new ReasoningMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chatId,
                    Role = "assistant",
                    Timestamp = init.AssistantTimestamp,
                    SequenceNumber = seqAlloc.NextSequence,
                    Reasoning = reasoningBuffer.ToString(),
                    Visibility = latestReasoningVisibility ?? ReasoningVisibility.Plain
                };
                var reasoningRecord = new MessageRecord
                {
                    Id = reasoningDto.Id,
                    ChatId = chatId,
                    Role = reasoningDto.Role,
                    Kind = "reasoning",
                    TimestampUtc = reasoningDto.Timestamp,
                    SequenceNumber = reasoningDto.SequenceNumber,
                    MessageJson = JsonSerializer.Serialize<MessageDto>(reasoningDto)
                };
                await _storage.InsertMessageAsync(reasoningRecord);
            }
        }

        // Update assistant message JSON with full content
        var finalDto = new TextMessageDto
        {
            Id = assistantMessageId,
            ChatId = chatId,
            Role = "assistant",
            Timestamp = init.AssistantTimestamp,
            SequenceNumber = init.AssistantSequenceNumber,
            Text = responseBuffer.ToString()
        };
        await _storage.UpdateMessageJsonAsync(assistantMessageId, JsonSerializer.Serialize<MessageDto>(finalDto));
        await _storage.UpdateChatUpdatedAtAsync(chatId, DateTime.UtcNow);

        if (StreamCompleted != null)
        {
            await StreamCompleted(new StreamCompleteEvent(chatId, assistantMessageId, finalDto.Text));
        }
    }

    public async IAsyncEnumerable<string> StreamAssistantResponseAsync(
        string chatId,
        string assistantMessageId,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var convo = await _storage.ListChatMessagesOrderedAsync(chatId);
        var history = convo.Messages
            .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!)
            .Where(d => d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
            .ToList();
        var lmMessages = history.Select(ConvertToLmMessage).ToList();

        _logger.LogInformation("Making API call to LLM for existing chat {ChatId}", chatId);
        var options = new GenerateReplyOptions { ModelId = GetModelId() };
        var streamingResponse = await _streamingAgent.GenerateReplyStreamingAsync(
            lmMessages,
            options,
            cancellationToken: cancellationToken);
        _logger.LogInformation("Received response from LLM for existing chat {ChatId}", chatId);

        var textMessageBuffer = new StringBuilder();
        var hadAnyTextChunk = false;
        var reasoningBuffer = new StringBuilder();
        ReasoningVisibility? latestReasoningVisibility = null;

        await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
        {
            if (message is TextUpdateMessage textMessage)
            {
                var content = textMessage.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    yield return content;
                    textMessageBuffer.Append(content);
                    hadAnyTextChunk = true;
                    if (StreamChunkReceived != null)
                    {
                        await StreamChunkReceived(
                            new TextStreamEvent(
                                chatId,
                                message.GenerationId ?? assistantMessageId,
                                content,
                                false));
                    }
                }
            }
            else if (message is ReasoningUpdateMessage reasoningUpdate)
            {
                if (reasoningUpdate.Visibility == ReasoningVisibility.Encrypted) continue;
                var delta = reasoningUpdate.Reasoning;
                if (!string.IsNullOrEmpty(delta))
                {
                    reasoningBuffer.Append(delta);
                    latestReasoningVisibility = reasoningUpdate.Visibility;
                    if (ReasoningChunkReceived != null)
                    {
                        await ReasoningChunkReceived(
                            new ReasoningStreamEvent(chatId,
                                message.GenerationId ?? assistantMessageId,
                                delta,
                                reasoningUpdate.Visibility));
                    }

                    if (SideChannelReceived != null)
                    {
                        await SideChannelReceived(
                            new ReasoningStreamEvent(
                                chatId,
                                message.GenerationId ?? assistantMessageId,
                                delta,
                                reasoningUpdate.Visibility));
                    }
                }
            }
            else if (message is ReasoningMessage reasoning)
            {
                if (reasoning.Visibility == ReasoningVisibility.Encrypted) continue;
                var delta = reasoning.Reasoning;
                if (!string.IsNullOrEmpty(delta))
                {
                    reasoningBuffer.Append(delta);
                    latestReasoningVisibility = reasoning.Visibility;
                    if (ReasoningChunkReceived != null)
                    {
                        await ReasoningChunkReceived(
                            new ReasoningStreamEvent(
                                chatId,
                                message.GenerationId ?? assistantMessageId,
                                delta,
                                reasoning.Visibility));
                    }
                    if (SideChannelReceived != null)
                    {
                        await SideChannelReceived(
                            new ReasoningStreamEvent(
                                chatId,
                                message.GenerationId ?? assistantMessageId,
                                delta,
                                reasoning.Visibility));
                    }
                }
            }
        }

        if (!hadAnyTextChunk)
        {
            try
            {
                var fallbackOptions = new GenerateReplyOptions { ModelId = GetModelId() };
                var messages = await _streamingAgent.GenerateReplyAsync(lmMessages, fallbackOptions, cancellationToken);
                var fullText = string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
                if (!string.IsNullOrEmpty(fullText))
                {
                    textMessageBuffer.Append(fullText);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Non-streaming fallback failed for existing chat {ChatId}", chatId);
            }
        }

        // Persist accumulated reasoning (if any) as a separate message
        if (reasoningBuffer.Length > 0)
        {
            var seqAlloc = await _storage.AllocateSequenceAsync(chatId);
            if (seqAlloc.Success)
            {
                var reasoningDto = new ReasoningMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chatId,
                    Role = "assistant",
                    Timestamp = DateTime.UtcNow,
                    SequenceNumber = seqAlloc.NextSequence,
                    Reasoning = reasoningBuffer.ToString(),
                    Visibility = latestReasoningVisibility ?? ReasoningVisibility.Plain
                };
                var reasoningRecord = new MessageRecord
                {
                    Id = reasoningDto.Id,
                    ChatId = chatId,
                    Role = reasoningDto.Role,
                    Kind = "reasoning",
                    TimestampUtc = reasoningDto.Timestamp,
                    SequenceNumber = reasoningDto.SequenceNumber,
                    MessageJson = JsonSerializer.Serialize<MessageDto>(reasoningDto)
                };
                await _storage.InsertMessageAsync(reasoningRecord);
            }
        }

        // Preserve the originally reserved timestamp and sequence number for the assistant message
        DateTime preservedTimestamp = DateTime.UtcNow;
        int preservedSequence = 0;
        try
        {
            var existing = await _storage.GetMessageByIdAsync(assistantMessageId);
            if (existing.Success && existing.Message != null)
            {
                var dto = JsonSerializer.Deserialize<MessageDto>(existing.Message.MessageJson);
                if (dto != null)
                {
                    preservedTimestamp = dto.Timestamp;
                    preservedSequence = dto.SequenceNumber;
                }
            }
        }
        catch { /* best-effort preserve; fallback to defaults above */ }

        var finalDto = new TextMessageDto
        {
            Id = assistantMessageId,
            ChatId = chatId,
            Role = "assistant",
            Timestamp = preservedTimestamp,
            SequenceNumber = preservedSequence,
            Text = textMessageBuffer.ToString()
        };

        await _storage.UpdateMessageJsonAsync(assistantMessageId, JsonSerializer.Serialize<MessageDto>(finalDto));
        await _storage.UpdateChatUpdatedAtAsync(chatId, DateTime.UtcNow);

        if (StreamCompleted != null)
        {
            await StreamCompleted(new StreamCompleteEvent(chatId, assistantMessageId, finalDto.Text));
        }
    }

    public async Task<MessageResult> AddUserMessageToExistingChatAsync(string chatId, string userId, string message)
    {
        try
        {
            var userSeq = await _storage.AllocateSequenceAsync(chatId);
            if (!userSeq.Success) return new MessageResult { Success = false, Error = userSeq.Error };

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = userSeq.NextSequence,
                Text = message
            };
            var userRecord = new MessageRecord
            {
                Id = userDto.Id,
                ChatId = chatId,
                Role = userDto.Role,
                Kind = "text",
                TimestampUtc = userDto.Timestamp,
                SequenceNumber = userDto.SequenceNumber,
                MessageJson = JsonSerializer.Serialize<MessageDto>(userDto)
            };
            var ins = await _storage.InsertMessageAsync(userRecord);
            if (!ins.Success) return new MessageResult { Success = false, Error = ins.Error };

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent(chatId, userDto));

            return new MessageResult
            {
                Success = true,
                UserMessage = userDto,
                AssistantMessage = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user message to existing chat {ChatId}", chatId);
            return new MessageResult { Success = false, Error = "Failed to add user message" };
        }
    }

    public async Task<int> GetNextSequenceNumberAsync(string chatId)
    {
        var res = await _storage.AllocateSequenceAsync(chatId);
        if (!res.Success) throw new InvalidOperationException(res.Error);
        return res.NextSequence;
    }

    public async Task<string> CreateAssistantMessageForStreamingAsync(string chatId, int sequenceNumber)
    {
        var dto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "assistant",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = sequenceNumber,
            Text = ""
        };
        var record = new MessageRecord
        {
            Id = dto.Id,
            ChatId = chatId,
            Role = dto.Role,
            Kind = "text",
            TimestampUtc = dto.Timestamp,
            SequenceNumber = dto.SequenceNumber,
            MessageJson = JsonSerializer.Serialize<MessageDto>(dto)
        };
        var ins = await _storage.InsertMessageAsync(record);
        if (!ins.Success) throw new InvalidOperationException(ins.Error);
        return dto.Id;
    }

    public async Task<string> GetMessageContentAsync(string messageId)
    {
        var res = await _storage.GetMessageContentAsync(messageId);
        return res.Success ? res.Content ?? string.Empty : string.Empty;
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request)
    {
        try
        {
            if (!string.IsNullOrEmpty(request.ChatId))
            {
                var userMessageResult = await AddUserMessageToExistingChatAsync(request.ChatId, request.UserId, request.Message);
                if (!userMessageResult.Success)
                {
                    throw new InvalidOperationException(userMessageResult.Error ?? "Failed to add user message");
                }

                var assistantSeqNumber = await GetNextSequenceNumberAsync(request.ChatId);
                var assistantMsgId = await CreateAssistantMessageForStreamingAsync(request.ChatId, assistantSeqNumber);
                var assistantTime = DateTime.UtcNow;

                return new StreamInitResult
                {
                    ChatId = request.ChatId,
                    UserMessageId = userMessageResult.UserMessage!.Id,
                    AssistantMessageId = assistantMsgId,
                    UserTimestamp = userMessageResult.UserMessage.Timestamp,
                    AssistantTimestamp = assistantTime,
                    UserSequenceNumber = userMessageResult.UserMessage.SequenceNumber,
                    AssistantSequenceNumber = assistantSeqNumber
                };
            }
            else
            {
                return await PrepareStreamChatAsync(request);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preparing unified stream chat");
            throw;
        }
    }

    public async IAsyncEnumerable<string> StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            var assistantSeqNumber = await GetNextSequenceNumberAsync(request.ChatId) - 1;
            var list = await _storage.ListChatMessagesOrderedAsync(request.ChatId);
            var lastAssistant = list.Messages.FirstOrDefault(m =>
            {
                var dto = JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!;
                return dto.Role == "assistant" && dto.SequenceNumber == assistantSeqNumber;
            });
            if (lastAssistant == null)
            {
                _logger.LogError("Assistant message not found for streaming in chat {ChatId}", request.ChatId);
                throw new InvalidOperationException("Assistant message not found for streaming");
            }
            await foreach (var chunk in StreamAssistantResponseAsync(request.ChatId, lastAssistant.Id, cancellationToken))
            {
                yield return chunk;
            }
        }
        else
        {
            await foreach (var chunk in StreamChatCompletionAsync(request, cancellationToken))
            {
                yield return chunk;
            }
        }
    }

    // Helper methods
    private async Task<string> GenerateAIResponseAsync(string chatId)
    {
        try
        {
            var list = await _storage.ListChatMessagesOrderedAsync(chatId);
            var history = list.Messages
                .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!)
                .Where(d => d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
                .ToList();
            var lmMessages = history.Select(ConvertToLmMessage).ToList();

            var options = new GenerateReplyOptions { ModelId = GetModelId() };
            var messages = await _streamingAgent.GenerateReplyAsync(lmMessages, options);
            return string.Join("", messages.OfType<TextMessage>().Select(m => m.Text));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating AI response");
            return $"Error: Failed to generate AI response. {ex.Message}";
        }
    }

    private string GetModelId() => _aiOptions.ModelId ?? "openrouter/horizon-beta";

    private static IMessage ConvertToLmMessage(MessageDto message)
    {
        var role = message.Role.ToLowerInvariant() switch
        {
            "user" => Role.User,
            "assistant" => Role.Assistant,
            "system" => Role.System,
            _ => Role.User
        };

        if (message is TextMessageDto t)
        {
            return new TextMessage
            {
                Role = role,
                Text = t.Text ?? string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty
            };
        }
        if (message is ReasoningMessageDto r)
        {
            return new TextMessage
            {
                Role = role,
                Text = r.GetText() ?? string.Empty,
                Metadata = ImmutableDictionary<string, object>.Empty
            };
        }

        return new TextMessage
        {
            Role = role,
            Text = string.Empty,
            Metadata = ImmutableDictionary<string, object>.Empty
        };
    }

    private static string GenerateChatTitle(string firstMessage)
    {
        var title = firstMessage.Length > 50
            ? firstMessage[..47] + "..."
            : firstMessage;
        return title;
    }
}
