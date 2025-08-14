using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.LmCore.Messages;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using AIChat.Server.Storage;
using System.Text.Json;
using AIChat.Server.Models;
using AchieveAi.LmDotnetTools.LmCore.Middleware;

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
    public event Func<MessageEvent, Task>? MessageReceived;

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
            var (allocSeqSuccess, allocSeqError, allocSeqNextSequence) = await _storage.AllocateSequenceAsync(chat.Id);
            if (!allocSeqSuccess) return new ChatResult { Success = false, Error = allocSeqError };
            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chat.Id,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = allocSeqNextSequence,
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
            if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
            {
                (allocSeqSuccess, allocSeqError, allocSeqNextSequence) = await _storage.AllocateSequenceAsync(chat.Id);
                if (!allocSeqSuccess) return new ChatResult { Success = false, Error = allocSeqError };
                var sysDto = new TextMessageDto
                {
                    Id = Guid.NewGuid().ToString(),
                    ChatId = chat.Id,
                    Role = "system",
                    Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                    SequenceNumber = allocSeqNextSequence,
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

            await _storage.UpdateChatUpdatedAtAsync(chat.Id, DateTime.UtcNow);

            // Build DTO with ordered messages
            var (Success, Error, Messages) = await _storage.ListChatMessagesOrderedAsync(chat.Id);
            var messages = Success
                ? Messages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!).ToList()
                : [ userDto ];

            return new ChatResult
            {
                Success = true,
                Chat = new ChatDto
                {
                    Id = chat.Id,
                    UserId = chat.UserId,
                    Title = chat.Title,
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
            var (Success, Error, Messages) = await _storage.ListChatMessagesOrderedAsync(chatId);
            var messages = Success
                ? Messages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson, MessageSerializationOptions.Default)!).ToList()
                : [];

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
            var (histSuccess, histError, histChats, histTotalCount) = await _storage.GetChatHistoryByUserAsync(userId, page, pageSize);
            var chats = histSuccess ? histChats : Array.Empty<ChatRecord>();

            var chatDtos = new List<ChatDto>(chats.Count);
            foreach (var c in chats)
            {
                var (msgsSuccess, msgsError, msgsMessages) = await _storage.ListChatMessagesOrderedAsync(c.Id);
                var messages = msgsSuccess
                    ? [.. msgsMessages.Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!)]
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
                TotalCount = histTotalCount,
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

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent {
                ChatId = request.ChatId,
                Message = userDto
            });

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

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent
            {
                ChatId = request.ChatId,
                Message = assistantDto
            });

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
        _logger.LogInformation("[DEBUG] PrepareStreamChatAsync - UserId: {UserId}, Message: {Message}", request.UserId, request.Message);
        var now = DateTime.UtcNow;
        var createRes = await _storage.CreateChatAsync(
            request.UserId,
            GenerateChatTitle(request.Message),
            now,
            now,
            null);

        if (!createRes.Success || createRes.Chat == null)
        {
            throw new InvalidOperationException(createRes.Error ?? "Create chat failed");
        }

        var chatId = createRes.Chat.Id;

        var (seqSuccess, seqError, userMsgSequence) = await _storage.AllocateSequenceAsync(chatId);
        if (!seqSuccess) throw new InvalidOperationException(seqError);

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            var sysDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "system",
                Timestamp = DateTime.UtcNow.AddMilliseconds(-1),
                SequenceNumber = userMsgSequence++,
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

        var userDto = new TextMessageDto
        {
            Id = Guid.NewGuid().ToString(),
            ChatId = chatId,
            Role = "user",
            Timestamp = DateTime.UtcNow,
            SequenceNumber = userMsgSequence,
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

        return new StreamInitResult
        {
            ChatId = chatId,
            UserMessageId = userDto.Id,
            UserTimestamp = userDto.Timestamp,
            UserSequenceNumber = userDto.SequenceNumber,
        };
    }

    public async Task StreamChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var init = await PrepareStreamChatAsync(request);
        var chatId = init.ChatId;

        var convo = await _storage.ListChatMessagesOrderedAsync(chatId);
        var history = convo.Messages
            .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!)
            .Where(d => d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
            .ToList();

        await StreamChatCompletionAsync(chatId, history, cancellationToken);
    }

    public async Task StreamAssistantResponseAsync(
        string chatId,
        CancellationToken cancellationToken = default)
    {
        var (_, _, messages) = await _storage.ListChatMessagesOrderedAsync(chatId, cancellationToken);
        var history = messages
            .Select(m => JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!)
            .Where(d => d is TextMessageDto td && !string.IsNullOrWhiteSpace(td.Text))
            .ToList();

        await StreamChatCompletionAsync(chatId, history, cancellationToken);
    }

    private async Task StreamChatCompletionAsync(
        string chatId,
        List<MessageDto> history,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("[DEBUG] StreamChatCompletionAsync - ChatId: {ChatId}, History count: {Count}", chatId, history.Count);
        foreach (var msg in history)
        {
            _logger.LogInformation("[DEBUG] History message - Role: {Role}, Type: {Type}, Content: {Content}", 
                msg.Role, msg.GetType().Name, 
                msg is TextMessageDto textMsg ? textMsg.Text?.Substring(0, Math.Min(100, textMsg.Text.Length)) + "..." : "N/A");
        }
        
        var lmMessages = history.Select(ConvertToLmMessage).ToList();
        var userMsgSequence = history.Count > 0 ? history.Max(m => m.SequenceNumber) + 1 : 1;

        _logger.LogInformation("[DEBUG] Converted to {Count} LM messages", lmMessages.Count);
        foreach (var lmMsg in lmMessages)
        {
            _logger.LogInformation("[DEBUG] LM message - Role: {Role}, Type: {Type}", lmMsg.Role, lmMsg.GetType().Name);
        }
        
        _logger.LogInformation("Making API call to LLM for chat {ChatId}", chatId);
        var options = new GenerateReplyOptions { ModelId = GetModelId() };

        var streamingResponse = await _streamingAgent
            .WithMiddleware(new JsonFragmentUpdateMiddleware())
            .WithMiddleware(
                (context, agent, cancellationToken) =>
                {
                    return agent.GenerateReplyAsync(
                        context.Messages,
                        context.Options,
                        cancellationToken);
                },
                async (context, agent, cancellationToken) =>
                {
                    var stream = await agent.GenerateReplyStreamingAsync(
                        context.Messages,
                        context.Options,
                        cancellationToken);

                    return ProcessStream(stream, chatId, userMsgSequence, cancellationToken);
                }
            )
            .WithMiddleware(new MessageUpdateJoinerMiddleware())
            .GenerateReplyStreamingAsync(
                lmMessages,
                options,
                cancellationToken: cancellationToken);

        _logger.LogInformation("Received response from LLM for chat {ChatId}", chatId);

        int fullMessageIndex = userMsgSequence;
        Type? lastFullMessageType = null;
        await foreach (var message in streamingResponse.WithCancellation(cancellationToken))
        {
            if (message.GetType() != lastFullMessageType)
            {
                fullMessageIndex++;
            }

            lastFullMessageType = message.GetType();
            var fullMessageId = message.GenerationId + $"-{fullMessageIndex:D3}";
            var sequenceNumber = await PersistFullMessage(chatId, message, fullMessageId);
            if (sequenceNumber != fullMessageIndex)
            {
                _logger.LogError(
                    "Sequence number mismatch: {Expected}, {Actual}",
                    fullMessageIndex,
                    sequenceNumber);
            }

            if (MessageReceived != null)
            {
                MessageEvent? evt = message switch
                {
                    TextMessage textMessage => new TextEvent
                    {
                        ChatId = chatId,
                        MessageId = fullMessageId,
                        Kind = "text",
                        SequenceNumber = sequenceNumber,
                        Text = textMessage.Text
                    },
                    ReasoningMessage reasoningMessage => new ReasoningEvent
                    {
                        ChatId = chatId,
                        MessageId = fullMessageId,
                        Kind = "reasoning",
                        SequenceNumber = sequenceNumber,
                        Reasoning = reasoningMessage.Reasoning,
                        Visibility = reasoningMessage.Visibility
                    },
                    UsageMessage usageMessage => new UsageEvent
                    {
                        ChatId = chatId,
                        MessageId = fullMessageId,
                        Kind = "usage",
                        SequenceNumber = sequenceNumber,
                        Usage = usageMessage.Usage
                    },
                    _ => null
                };

                if (evt != null)
                {
                    await MessageReceived(evt);
                }
            }
        }

        await _storage.UpdateChatUpdatedAtAsync(chatId, DateTime.UtcNow, cancellationToken);
    }

    private async IAsyncEnumerable<IMessage> ProcessStream(
        IAsyncEnumerable<IMessage> stream,
        string chatId,
        int userMsgSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int messageIndex = userMsgSequence;
        int chunkSequenceId = 0;
        Type? lastType = null;

        await foreach (var message in stream.WithCancellation(cancellationToken))
        {
            if (message.GetType() != lastType)
            {
                messageIndex++;
                chunkSequenceId = 0;
            }

            chunkSequenceId++;

            lastType = message.GetType();
            var messageId = message.GenerationId + $"-{messageIndex:D3}";

            if (message is TextUpdateMessage textMessage)
            {
                var content = textMessage.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    _logger.LogTrace(
                        "TextUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Content: {Delta}",
                        chatId,
                        messageId,
                        "text",
                        content);

                    if (StreamChunkReceived != null)
                    {
                        await StreamChunkReceived(
                            new TextStreamEvent
                            {
                                ChatId = chatId,
                                MessageId = messageId,
                                Kind = "text",
                                Done = false,
                                Delta = content,
                                ChunkSequenceId = chunkSequenceId,
                                SequenceNumber = messageIndex,
                            });
                    }
                }
            }
            else if (message is ReasoningUpdateMessage reasoningUpdate)
            {
                if (reasoningUpdate.Visibility == ReasoningVisibility.Encrypted) continue;
                var delta = reasoningUpdate.Reasoning;
                if (!string.IsNullOrEmpty(delta))
                {
                    _logger.LogTrace(
                        "ReasoningUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Content: {Delta}",
                        chatId,
                        messageId,
                        "reasoning",
                        delta);

                    if (StreamChunkReceived != null)
                    {
                        await StreamChunkReceived(
                            new ReasoningStreamEvent
                            {
                                ChatId = chatId,
                                MessageId = messageId,
                                Kind = "reasoning",
                                Done = false,
                                Delta = delta,
                                ChunkSequenceId = chunkSequenceId,
                                SequenceNumber = messageIndex,
                                Visibility = reasoningUpdate.Visibility
                            });
                    }
                }
            }
            else if (message is ToolsCallUpdateMessage toolsCallUpdateMessage)
            {
                foreach (var toolCallUpdate in toolsCallUpdateMessage.ToolCallUpdates)
                {
                    _logger.LogTrace(
                        "ToolsCallUpdateMessage - ChatId: {ChatId}, MessageId: {MessageId}, {Type}, Tool: {Delta}",
                        chatId,
                        messageId,
                        "tools_call_update",
                        JsonSerializer.Serialize(toolCallUpdate));
                    if (StreamChunkReceived != null)
                    {
                        await StreamChunkReceived(
                            new ToolsCallUpdateStreamEvent
                            {
                                ChatId = chatId,
                                MessageId = messageId,
                                Kind = "tools_call_update",
                                Done = false,
                                ChunkSequenceId = chunkSequenceId,
                                SequenceNumber = messageIndex,
                                ToolCallUpdate = toolCallUpdate
                            });
                    }
                }
            }

            yield return message;
        }

        yield break;
    }

    private async Task<int> PersistFullMessage(
        string chatId,
        IMessage message,
        string fullMessageId)
    {
        var (_, _, nextSequence) = await _storage.AllocateSequenceAsync(chatId);
        var timestamp = DateTime.UtcNow;
        var messageRecord = message switch
        {
            ReasoningMessage reasoning => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = message.Role.ToString(),
                Kind = "reasoning",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new ReasoningMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = message.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        Reasoning = reasoning.Reasoning,
                        Visibility = reasoning.Visibility
                    })
            },
            TextMessage text => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = message.Role.ToString(),
                Kind = "text",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(new TextMessageDto
                {
                    Id = fullMessageId,
                    ChatId = chatId,
                    Role = message.Role.ToString(),
                    Timestamp = timestamp,
                    SequenceNumber = nextSequence,
                    Text = text.Text
                })
            },
            UsageMessage usage => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = usage.Role.ToString(),
                Kind = "usage",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new UsageMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = usage.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        Usage = usage.Usage
                    })
            },
            ToolsCallMessage toolsCall => new MessageRecord
            {
                Id = fullMessageId,
                ChatId = chatId,
                Role = toolsCall.Role.ToString(),
                Kind = "tools_call",
                TimestampUtc = timestamp,
                SequenceNumber = nextSequence,
                MessageJson = JsonSerializer.Serialize<MessageDto>(
                    new ToolCallMessageDto
                    {
                        Id = fullMessageId,
                        ChatId = chatId,
                        Role = toolsCall.Role.ToString(),
                        Timestamp = timestamp,
                        SequenceNumber = nextSequence,
                        ToolCalls = [.. toolsCall.ToolCalls]
                    })
            },
            _ => null
        };

        if (messageRecord != null)
        {
            await _storage.InsertMessageAsync(messageRecord);
        }

        return nextSequence;
    }

    public async Task<MessageResult> AddUserMessageToExistingChatAsync(string chatId, string userId, string message)
    {
        try
        {
            var (seqSuccess, seqError, nextSequence) = await _storage.AllocateSequenceAsync(chatId);
            if (!seqSuccess) return new MessageResult { Success = false, Error = seqError };

            var userDto = new TextMessageDto
            {
                Id = Guid.NewGuid().ToString(),
                ChatId = chatId,
                Role = "user",
                Timestamp = DateTime.UtcNow,
                SequenceNumber = nextSequence,
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

            if (MessageCreated != null) await MessageCreated(new MessageCreatedEvent
            {
                ChatId = chatId,
                Message = userDto
            });

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
        var (success, error, nextSequence) = await _storage.AllocateSequenceAsync(chatId);
        if (!success) throw new InvalidOperationException(error);
        return nextSequence;
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
        var (resSuccess, _, resContent) = await _storage.GetMessageContentAsync(messageId);
        return resSuccess ? resContent ?? string.Empty : string.Empty;
    }

    public async Task<StreamInitResult> PrepareUnifiedStreamChatAsync(StreamChatRequest request)
    {
        try
        {
            if (!string.IsNullOrEmpty(request.ChatId))
            {
                var userMessageResult = await AddUserMessageToExistingChatAsync(
                    request.ChatId,
                    request.UserId,
                    request.Message);

                if (!userMessageResult.Success)
                {
                    throw new InvalidOperationException(userMessageResult.Error ?? "Failed to add user message");
                }

                return new StreamInitResult
                {
                    ChatId = request.ChatId,
                    UserMessageId = userMessageResult.UserMessage!.Id,
                    UserTimestamp = userMessageResult.UserMessage.Timestamp,
                    UserSequenceNumber = userMessageResult.UserMessage.SequenceNumber,
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

    public async Task StreamUnifiedChatCompletionAsync(
        StreamChatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.ChatId))
        {
            var assistantSeqNumber = await GetNextSequenceNumberAsync(request.ChatId) - 1;
            var (_, _, listMessages) = await _storage.ListChatMessagesOrderedAsync(request.ChatId, cancellationToken);
            var lastAssistant = listMessages.FirstOrDefault(m =>
            {
                var dto = JsonSerializer.Deserialize<MessageDto>(m.MessageJson)!;
                return dto.Role == "assistant" && dto.SequenceNumber == assistantSeqNumber;
            });
            if (lastAssistant == null)
            {
                _logger.LogError("Assistant message not found for streaming in chat {ChatId}", request.ChatId);
                throw new InvalidOperationException("Assistant message not found for streaming");
            }

            await StreamAssistantResponseAsync(request.ChatId, cancellationToken);
        }
        else
        {
            await StreamChatCompletionAsync(request, cancellationToken);
        }
    }

    // Helper methods
    private async Task<string> GenerateAIResponseAsync(string chatId)
    {
        try
        {
            var (_, _, listMessages) = await _storage.ListChatMessagesOrderedAsync(chatId);
            var history = listMessages
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
