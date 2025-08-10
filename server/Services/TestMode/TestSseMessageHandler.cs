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

            string userMessage = ExtractLatestUserMessage(root) ?? string.Empty;

            bool reasoningFirst = userMessage.Contains("\nReason:", StringComparison.Ordinal);

            var content = new SseStreamHttpContent(
                userMessage: userMessage,
                model: model,
                reasoningFirst: reasoningFirst,
                wordsPerChunk: WordsPerChunk,
                chunkDelayMs: ChunkDelayMs);

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
}
