using System.Text.Json;

namespace AIChat.Server.Services.TestMode;

/// <summary>
/// Default implementation of instruction chain parser for test mode.
/// </summary>
public sealed class InstructionChainParser : IInstructionChainParser
{
    private const string InstructionStartTag = "<|instruction_start|>";
    private const string InstructionEndTag = "<|instruction_end|>";
    
    private readonly ILogger<InstructionChainParser> _logger;

    public InstructionChainParser(ILogger<InstructionChainParser> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public InstructionPlan[]? ExtractInstructionChain(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogDebug("Content is null or whitespace, no instruction chain to extract");
            return null;
        }

        var start = content.IndexOf(InstructionStartTag, StringComparison.Ordinal);
        var end = content.IndexOf(InstructionEndTag, StringComparison.Ordinal);

        if (start < 0 || end <= start)
        {
            _logger.LogDebug("Instruction tags not found in content");
            return null;
        }

        var json = content.Substring(start + InstructionStartTag.Length, end - start - InstructionStartTag.Length).Trim();
        
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for instruction_chain array format
            if (root.TryGetProperty("instruction_chain", out var chainEl) && chainEl.ValueKind == JsonValueKind.Array)
            {
                var chain = new List<InstructionPlan>();
                foreach (var item in chainEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    
                    var plan = ParseSingleInstruction(item);
                    if (plan != null)
                    {
                        chain.Add(plan);
                    }
                }
                
                if (chain.Count > 0)
                {
                    _logger.LogInformation("Parsed instruction chain with {Count} instructions", chain.Count);
                    return chain.ToArray();
                }
                
                // Empty chain array - return null to indicate no instructions
                _logger.LogDebug("Empty instruction chain array found");
                return null;
            }

            // Backward compatibility: single instruction format
            var singleInstruction = ParseSingleInstruction(root);
            if (singleInstruction != null)
            {
                _logger.LogInformation("Parsed single instruction (backward compatibility mode)");
                return new[] { singleInstruction };
            }

            _logger.LogWarning("No valid instruction format found in JSON");
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse instruction chain JSON");
            throw new InvalidOperationException("Malformed instruction chain", ex);
        }
    }

    /// <inheritdoc />
    public InstructionPlan? ParseSingleInstruction(JsonElement instructionEl)
    {
        if (instructionEl.ValueKind != JsonValueKind.Object)
        {
            _logger.LogDebug("Instruction element is not an object");
            return null;
        }

        // Extract id (optional, but useful for logging)
        var id = instructionEl.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString() ?? string.Empty
            : string.Empty;

        // Extract id_message
        var idMessage = instructionEl.TryGetProperty("id_message", out var idMsgEl) && idMsgEl.ValueKind == JsonValueKind.String
            ? idMsgEl.GetString() ?? string.Empty
            : id; // Fall back to id if id_message not present

        // Extract reasoning length (optional)
        int? reasoningLen = null;
        if (instructionEl.TryGetProperty("reasoning", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.Object)
        {
            if (reasonEl.TryGetProperty("length", out var lenEl) && lenEl.ValueKind == JsonValueKind.Number && lenEl.TryGetInt32(out var len))
            {
                reasoningLen = Math.Max(0, len);
            }
        }

        // Extract messages array
        var messages = ParseInstructionMessages(instructionEl);
        
        if (messages.Count == 0)
        {
            _logger.LogWarning("No valid messages found in instruction");
            return null;
        }

        _logger.LogDebug("Parsed instruction: id={Id}, idMessage={IdMessage}, messageCount={Count}", 
            id, idMessage, messages.Count);
        
        return new InstructionPlan(idMessage, reasoningLen, messages);
    }

    private List<InstructionMessage> ParseInstructionMessages(JsonElement instructionEl)
    {
        var messages = new List<InstructionMessage>();
        
        if (!instructionEl.TryGetProperty("messages", out var msgsEl) || msgsEl.ValueKind != JsonValueKind.Array)
        {
            return messages;
        }

        foreach (var item in msgsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            
            // Check for text message
            if (item.TryGetProperty("text_message", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
            {
                if (textEl.TryGetProperty("length", out var lEl) && lEl.ValueKind == JsonValueKind.Number && lEl.TryGetInt32(out var tlen))
                {
                    messages.Add(InstructionMessage.ForText(Math.Max(0, tlen)));
                    continue;
                }
            }
            
            // Check for tool calls
            if (item.TryGetProperty("tool_call", out var toolEl) && toolEl.ValueKind == JsonValueKind.Array)
            {
                var calls = ParseToolCalls(toolEl);
                if (calls.Count > 0)
                {
                    messages.Add(InstructionMessage.ForToolCalls(calls));
                }
            }
        }

        return messages;
    }

    private List<InstructionToolCall> ParseToolCalls(JsonElement toolCallsElement)
    {
        var calls = new List<InstructionToolCall>();
        
        foreach (var call in toolCallsElement.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object) continue;
            
            var name = call.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String 
                ? (nEl.GetString() ?? string.Empty) 
                : string.Empty;
                
            var argsObj = call.TryGetProperty("args", out var aEl) ? aEl : default;
            var argsJson = argsObj.ValueKind != JsonValueKind.Undefined ? argsObj.GetRawText() : "{}";
            
            calls.Add(new InstructionToolCall(name, argsJson));
        }
        
        return calls;
    }
}