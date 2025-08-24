using System.Text.Json;

namespace AIChat.Server.Services.TestMode;

/// <summary>
/// Default implementation of conversation analyzer for test mode.
/// </summary>
public sealed class ConversationAnalyzer : IConversationAnalyzer
{
    private readonly ILogger<ConversationAnalyzer> _logger;
    private readonly IInstructionChainParser _chainParser;

    public ConversationAnalyzer(
        ILogger<ConversationAnalyzer> logger,
        IInstructionChainParser chainParser)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _chainParser = chainParser ?? throw new ArgumentNullException(nameof(chainParser));
    }

    /// <inheritdoc />
    public (InstructionPlan? plan, int assistantResponseCount) AnalyzeConversation(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            _logger.LogDebug("No messages array found in conversation");
            return (null, 0);
        }

        var (chain, chainMessageIndex) = FindLatestInstructionChain(messages);
        
        if (chain == null || chainMessageIndex < 0)
        {
            _logger.LogDebug("No instruction chain found in conversation");
            return (null, 0);
        }

        var assistantCount = CountAssistantResponsesAfterChain(messages, chainMessageIndex);
        
        _logger.LogDebug("Found {AssistantCount} assistant responses after instruction chain at index {Index}", 
            assistantCount, chainMessageIndex);

        // Return instruction at index or null if out of bounds
        var instruction = (assistantCount < chain.Length)
            ? chain[assistantCount]
            : null;

        if (instruction != null)
        {
            _logger.LogInformation("Executing instruction {Index}: {Id}", 
                assistantCount + 1, instruction.IdMessage);
        }
        else
        {
            _logger.LogInformation("Chain exhausted after {Count} executions", assistantCount);
        }

        return (instruction, assistantCount);
    }

    /// <inheritdoc />
    public string? ExtractLatestUserMessage(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            _logger.LogDebug("No messages array found for user message extraction");
            return null;
        }

        string? latest = null;
        foreach (var el in messages.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            if (!el.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase)) continue;

            if (el.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                latest = content.GetString();
            }
        }

        _logger.LogDebug("Extracted latest user message: {HasMessage}", latest != null);
        return latest;
    }

    private (InstructionPlan[]? chain, int messageIndex) FindLatestInstructionChain(JsonElement messages)
    {
        InstructionPlan[]? chain = null;
        int chainMessageIndex = -1;

        // Find last instruction chain by scanning messages from newest to oldest
        var messageArray = messages.EnumerateArray().ToList();
        
        for (int i = messageArray.Count - 1; i >= 0; i--)
        {
            var message = messageArray[i];
            if (message.ValueKind != JsonValueKind.Object) continue;
            
            // Check if this is a user message
            if (!message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase)) continue;

            // Check if it contains instruction tags
            if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var contentStr = content.GetString() ?? string.Empty;
                var extractedChain = _chainParser.ExtractInstructionChain(contentStr);
                
                if (extractedChain != null)
                {
                    chain = extractedChain;
                    chainMessageIndex = i;
                    _logger.LogInformation("Found instruction chain with {Count} instructions at message index {Index}", 
                        chain.Length, chainMessageIndex);
                    break; // Use the last (most recent) chain found
                }
            }
        }

        return (chain, chainMessageIndex);
    }

    private int CountAssistantResponsesAfterChain(JsonElement messages, int chainMessageIndex)
    {
        var messageArray = messages.EnumerateArray().ToList();
        int assistantCount = 0;

        for (int i = chainMessageIndex + 1; i < messageArray.Count; i++)
        {
            var message = messageArray[i];
            if (message.ValueKind != JsonValueKind.Object) continue;
            
            if (!message.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String) continue;
            
            // Count only assistant messages (not user or tool messages)
            if (string.Equals(role.GetString(), "assistant", StringComparison.OrdinalIgnoreCase))
            {
                assistantCount++;
            }
        }

        return assistantCount;
    }
}