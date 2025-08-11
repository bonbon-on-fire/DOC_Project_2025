using System.IO.Pipelines;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIChat.Server.Services.TestMode;

public sealed class SseStreamHttpContent : HttpContent
{
    private static readonly JsonSerializerOptions _jsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions _jsonSerializerOptionsWithReasoning = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly string _userMessage;
    private readonly string? _model;
    private readonly bool _reasoningFirst;
    private readonly int _wordsPerChunk;
    private readonly int _chunkDelayMs;

    public SseStreamHttpContent(
        string userMessage,
        string? model = null,
        bool reasoningFirst = false,
        int wordsPerChunk = 5,
        int chunkDelayMs = 100)
    {
        _userMessage = userMessage;
        _model = model;
        _reasoningFirst = reasoningFirst;
        _wordsPerChunk = Math.Max(1, wordsPerChunk);
        _chunkDelayMs = Math.Max(0, chunkDelayMs);

        Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
    }

    protected override bool TryComputeLength(out long length)
    {
        length = -1;
        return false;
    }

    // Back-compat override signature
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => SerializeCoreAsync(stream, CancellationToken.None);

    protected override Stream CreateContentReadStream(CancellationToken cancellationToken)
    {
        var pipe = new Pipe();

        _ = Task.Run(async () =>
        {
            try
            {
                using var writerStream = pipe.Writer.AsStream(leaveOpen: false);
                await SerializeCoreAsync(writerStream, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Swallow exceptions to avoid crashing background task; reader will observe stream end or failure.
            }
            finally
            {
                await pipe.Writer.CompleteAsync().ConfigureAwait(false);
            }
        }, CancellationToken.None);

        return pipe.Reader.AsStream(leaveOpen: false);
    }

    protected override Task<Stream> CreateContentReadStreamAsync()
        => Task.FromResult(CreateContentReadStream(CancellationToken.None));

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
        => Task.FromResult(CreateContentReadStream(cancellationToken));

    private async Task SerializeCoreAsync(Stream stream, CancellationToken cancellationToken)
    {
        // Important: leave the provided stream open so HttpContent can buffer/read it after serialization
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = false };

        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = $"gen-{created}-{Guid.NewGuid().ToString("N")[..16]}";

        async Task WriteSseAsync(object payload)
        {
            string json = JsonSerializer.Serialize(
                payload,
                _reasoningFirst ? _jsonSerializerOptionsWithReasoning : _jsonSerializerOptions);

            await writer.WriteAsync("data: ");
            await writer.WriteAsync(json);
            await writer.WriteAsync("\n\n");
            await writer.FlushAsync(cancellationToken);
        }

        // Removed initial empty chunk to avoid provider errors on null content

        // New behavior: add user message at the start of the assistant message
        if (!string.IsNullOrEmpty(_userMessage))
        {
            if (_reasoningFirst)
            {
                var preReasoning = $"<|user_pre|><|reasoning|> {_userMessage}";
                await WriteSseAsync(BuildChunk(id, created, content: null, reasoning: preReasoning));
            }
            else
            {
                var preContent = $"<|user_pre|><|text_message|> {_userMessage}";
                await WriteSseAsync(BuildChunk(id, created, content: preContent, reasoning: null));
            }
        }

        if (_reasoningFirst)
        {
            foreach (var token in GenerateReasoningTokens(_userMessage))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await WriteSseAsync(BuildChunk(id, created, content: null, reasoning: token));
                if (_chunkDelayMs > 0)
                {
                    await Task.Delay(_chunkDelayMs, cancellationToken);
                }
            }
        }

        var loremWordCount = CalculateStableWordCount(_userMessage);

        foreach (var piece in GenerateLoremChunks(loremWordCount, _wordsPerChunk))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await WriteSseAsync(BuildChunk(id, created, content: piece, reasoning: null));
            if (_chunkDelayMs > 0)
            {
                await Task.Delay(_chunkDelayMs, cancellationToken);
            }
        }

        // Add user message at the end of the assistant message (as content)
        if (!string.IsNullOrEmpty(_userMessage))
        {
            var postContent = $"<|user_post|><|text_message|> {_userMessage}";
            await WriteSseAsync(BuildChunk(id, created, content: postContent, reasoning: null));
        }

        await WriteSseAsync(BuildFinishChunk(id, created));
        await writer.WriteAsync("data: [DONE]\n\n");
        await writer.FlushAsync(cancellationToken);
    }

    private object BuildChunk(string id, long created, string? content, string? reasoning)
    {
        var delta = new Dictionary<string, object?>
        {
            ["role"] = "assistant",
            ["reasoning_details"] = Array.Empty<object>()
        };

        // Always include a content field. Some upstream parsers throw if it is missing/null.
        delta["content"] = content ?? string.Empty;

        if (reasoning != null)
        {
            delta["reasoning"] = reasoning;
        }

        var choice = new Dictionary<string, object?>
        {
            ["index"] = 0,
            ["delta"] = delta,
            ["finishReason"] = null,
            ["nativeFinishReason"] = null,
            ["logprobs"] = null
        };

        var root = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["created"] = created,
            ["choices"] = new[] { choice }
        };

        root["model"] = !string.IsNullOrWhiteSpace(_model) ? _model : "test-model";
        return root;
    }

    private object BuildFinishChunk(string id, long created)
    {
        var choice = new Dictionary<string, object?>
        {
            ["index"] = 0,
            ["delta"] = new Dictionary<string, object>
            {
                ["role"] = "assistant",
                ["reasoning_details"] = Array.Empty<object>(),
                ["content"] = string.Empty
            },
            ["finishReason"] = "stop",
            ["nativeFinishReason"] = null,
            ["logprobs"] = null
        };

        var root = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["object"] = "chat.completion.chunk",
            ["created"] = created,
            ["choices"] = new[] { choice }
        };

        root["model"] = !string.IsNullOrWhiteSpace(_model) ? _model : "test-model";
        return root;
    }

    private static int CalculateStableWordCount(string seed)
    {
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(seed ?? string.Empty));
        uint val = BitConverter.ToUInt32(bytes, 0);
        return 5 + (int)(val % 100u);
    }

    private static IEnumerable<string> GenerateLoremChunks(int totalWords, int wordsPerChunk)
    {
        if (totalWords <= 0)
        {
            yield break;
        }

        var lorem = ("lorem ipsum dolor sit amet consectetur adipiscing elit sed do eiusmod tempor " +
                     "incididunt ut labore et dolore magna aliqua ut enim ad minim veniam quis nostrud " +
                     "exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat duis aute irure " +
                     "dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur " +
                     "excepteur sint occaecat cupidatat non proident sunt in culpa qui officia deserunt mollit anim id est laborum")
            .Split(' ');

        var words = new List<string>(totalWords);
        for (int i = 0; i < totalWords; i++)
        {
            words.Add(lorem[i % lorem.Length]);
        }

        for (int i = 0; i < words.Count; i += wordsPerChunk)
        {
            var chunkWords = words.Skip(i).Take(Math.Min(wordsPerChunk, words.Count - i));
            yield return string.Join(' ', chunkWords);
        }
    }

    private static IEnumerable<string> GenerateReasoningTokens(string userMessage)
    {
        var basis = new[]
        {
            "The", " user", " says", ":", " \"", "Setup", " message", " ", "1", " for", " existing",
            " conversation", " test", "\"", " then", " \"", "Setup", " message", " ", "2", " for",
            " existing", " conversation", "\"", ".", " They", " likely", " want", " me", " to", " the",
            " existing", " conversation", " after", " refresh", " \".", " They", " likely", " want",
            " me", " to", " respond", " to", " a", " previous", " conversation", ".", " But", " we",
            " don't", " have", " the", " previous", " conversation", " context", "."
        };

        foreach (var token in basis)
        {
            yield return token;
        }
    }
}


