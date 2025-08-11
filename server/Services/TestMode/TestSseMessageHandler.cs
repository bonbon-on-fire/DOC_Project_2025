using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIChat.Server.Services.TestMode;

public sealed class TestSseMessageHandler : HttpMessageHandler
{
    public int WordsPerChunk { get; set; } = 5;
    public int ChunkDelayMs { get; set; } = 100;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Post || request.RequestUri == null)
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        if (!request.RequestUri.AbsolutePath.EndsWith("/v1/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        string body = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("Missing request body")
            };
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (Exception ex)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent($"Invalid JSON: {ex.Message}")
            };
        }
        using (doc)
        {
            var root = doc.RootElement;

            bool stream = root.TryGetProperty("stream", out var streamProp) && streamProp.ValueKind == JsonValueKind.True;
            if (!stream)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            string? model = root.TryGetProperty("model", out var modelProp) && modelProp.ValueKind == JsonValueKind.String
                ? modelProp.GetString()
                : null;

            // Instruction-driven mode detection
            var latest = ExtractLatestUserMessage(root) ?? string.Empty;
            var (plan, fallbackMessage) = TryParseInstructionPlan(latest);

            HttpContent content;
            if (plan is not null)
            {
                content = new SseStreamHttpContent(
                    instructionPlan: plan,
                    model: model,
                    wordsPerChunk: WordsPerChunk,
                    chunkDelayMs: ChunkDelayMs);
            }
            else
            {
                bool reasoningFirst = fallbackMessage.Contains("\nReason:", StringComparison.Ordinal);
                content = new SseStreamHttpContent(
                    userMessage: fallbackMessage,
                    model: model,
                    reasoningFirst: reasoningFirst,
                    wordsPerChunk: WordsPerChunk,
                    chunkDelayMs: ChunkDelayMs);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            response.Headers.ConnectionClose = false;
            return response;
        }
    }

    private static string? ExtractLatestUserMessage(JsonElement root)
    {
        if (!root.TryGetProperty("messages", out var messages) || messages.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        string? latest = null;
        foreach (var el in messages.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            if (!el.TryGetProperty("role", out var role) || role.ValueKind != JsonValueKind.String) continue;
            if (!string.Equals(role.GetString(), "user", StringComparison.OrdinalIgnoreCase)) continue;

            if (el.TryGetProperty("content", out var content))
            {
                if (content.ValueKind == JsonValueKind.String)
                {
                    latest = content.GetString();
                }
            }
        }
        return latest;
    }

    private static (InstructionPlan? plan, string fallback) TryParseInstructionPlan(string userMessage)
    {
        const string startTag = "<|instruction_start|>";
        const string endTag = "<|instruction_end|>";
        var start = userMessage.IndexOf(startTag, StringComparison.Ordinal);
        var end = userMessage.IndexOf(endTag, StringComparison.Ordinal);
        if (start < 0 || end <= start)
        {
            return (null, userMessage);
        }

        var jsonSpan = userMessage.Substring(start + startTag.Length, end - (start + startTag.Length)).Trim();
        try
        {
            using var doc = JsonDocument.Parse(jsonSpan);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, userMessage);
            }

            var idMessage = root.TryGetProperty("id_message", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? string.Empty
                : string.Empty;
            int? reasoningLen = null;
            if (root.TryGetProperty("reasoning", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.Object)
            {
                if (reasonEl.TryGetProperty("length", out var lenEl) && lenEl.ValueKind == JsonValueKind.Number && lenEl.TryGetInt32(out var len))
                {
                    reasoningLen = Math.Max(0, len);
                }
            }

            var messages = new List<InstructionMessage>();
            if (root.TryGetProperty("messages", out var msgsEl) && msgsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in msgsEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    if (item.TryGetProperty("text_message", out var textEl) && textEl.ValueKind == JsonValueKind.Object)
                    {
                        if (textEl.TryGetProperty("length", out var lEl) && lEl.ValueKind == JsonValueKind.Number && lEl.TryGetInt32(out var tlen))
                        {
                            messages.Add(InstructionMessage.ForText(Math.Max(0, tlen)));
                            continue;
                        }
                    }
                    if (item.TryGetProperty("tool_call", out var toolEl) && toolEl.ValueKind == JsonValueKind.Array)
                    {
                        var calls = new List<InstructionToolCall>();
                        foreach (var call in toolEl.EnumerateArray())
                        {
                            if (call.ValueKind != JsonValueKind.Object) continue;
                            var name = call.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? (nEl.GetString() ?? string.Empty) : string.Empty;
                            var argsObj = call.TryGetProperty("args", out var aEl) ? aEl : default;
                            var argsJson = argsObj.ValueKind != JsonValueKind.Undefined ? argsObj.GetRawText() : "{}";
                            calls.Add(new InstructionToolCall(name, argsJson));
                        }
                        messages.Add(InstructionMessage.ForToolCalls(calls));
                        continue;
                    }
                }
            }

            if (messages.Count == 0)
            {
                return (null, userMessage);
            }

            var plan = new InstructionPlan(idMessage, reasoningLen, messages);
            return (plan, userMessage);
        }
        catch
        {
            return (null, userMessage);
        }
    }
}
