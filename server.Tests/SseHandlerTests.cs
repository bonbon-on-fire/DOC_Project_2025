// <copyright file="SseHandlerTests.cs" company="AIChat">
// Copyright (c) AIChat. All rights reserved.
// </copyright>

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using AIChat.Server.Services.TestMode;
using FluentAssertions;
using Xunit;

namespace AIChat.Server.Tests;

/// <summary>
/// Tests for the Server-Sent Events (SSE) handler functionality.
/// </summary>
public class SseHandlerTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Builds an HTTP request message for testing SSE endpoints.
    /// </summary>
    /// <param name="userMessage">The user message to include in the request.</param>
    /// <param name="stream">Whether to enable streaming.</param>
    /// <param name="model">The model to use for the request.</param>
    /// <returns>An HTTP request message configured for testing.</returns>
    private static HttpRequestMessage BuildRequest(string userMessage, bool stream = true, string? model = null)
    {
        var payload = new
        {
            model,
            stream,
            messages = new object[]
            {
                new { role = "user", content = userMessage }
            }
        };
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return req;
    }

    [Fact]
    public async Task NonTargetedPath_Returns404()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/completions");
        var res = await invoker.SendAsync(req, default);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MissingStreamTrue_Returns404()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("hello", stream: false);
        var res = await invoker.SendAsync(req, default);
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Response_IsEventStream_WithChunks_AndDone()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 3 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("hello world", stream: true);
        var res = await invoker.SendAsync(req, default);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType!.MediaType.Should().Be("text/event-stream");

        var text = await res.Content.ReadAsStringAsync();
        text.Should().Contain("data:");
        text.Should().Contain("[DONE]");

        var firstJson = GetFirstJsonLine(text);
        using var doc = JsonDocument.Parse(firstJson);
        var root = doc.RootElement;
        root.GetProperty("object").GetString().Should().Be("chat.completion.chunk");
        root.GetProperty("choices")[0].GetProperty("index").GetInt32().Should().Be(0);
        root.TryGetProperty("id", out _).Should().BeTrue();
        root.TryGetProperty("created", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Model_IsEchoed_WhenProvided()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("hello", stream: true, model: "test-model");
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var firstJson = GetFirstJsonLine(text);
        using var doc = JsonDocument.Parse(firstJson);
        doc.RootElement.GetProperty("model").GetString().Should().Be("test-model");
    }

    [Fact]
    public async Task Lorem_Count_WithinRange_And_EchoExcluded()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 50 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("test-echo", stream: true);
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        var allJson = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        var contents = allJson
            .SelectMany(d => d.RootElement.GetProperty("choices")[0].GetProperty("delta").EnumerateObject()
                .Where(p => p.NameEquals("content"))
                .Select(p => p.Value.GetString() ?? string.Empty))
            .ToList();

        contents.Count.Should().BeGreaterThan(0);
        var echo = "test-echo";
        var nonEchoWords = string.Join(" ", contents.Where(c => c != echo)).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        nonEchoWords.Length.Should().BeGreaterThanOrEqualTo(5).And.BeLessThanOrEqualTo(500);
    }

    [Fact]
    public async Task ReasoningFirst_WhenTriggered()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var req = BuildRequest("Hello\nReason: please show thinking", stream: true);
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var jsons = GetAllJsonLines(text).ToList();

        var seenContent = false;
        var seenReasoning = false;
        foreach (var json in jsons.Skip(1))
        {
            using var doc = JsonDocument.Parse(json);
            var delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta");
            if (delta.TryGetProperty("content", out var c) && !string.IsNullOrEmpty(c.GetString()))
            {
                seenContent = true;
            }

            if (delta.TryGetProperty("reasoning", out var r) && !string.IsNullOrEmpty(r.GetString()))
            {
                seenReasoning = true;
                if (seenContent)
                {
                    throw new Xunit.Sdk.XunitException("Reasoning appeared after content");
                }
            }
        }

        seenReasoning.Should().BeTrue();
    }

    [Fact]
    public async Task Request_Parsing_Ignores_Complex_Content_Arrays()
    {
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0 };
        using var invoker = new HttpMessageInvoker(handler);
        var payload = new
        {
            stream = true,
            messages = new object[]
            {
                new { role = "user", content = new object[] { new { type = "text", text = "ignored" } } },
                new { role = "user", content = "plain" }
            }
        };
        var json = JsonSerializer.Serialize(payload);
        var req = new HttpRequestMessage(HttpMethod.Post, "http://localhost/v1/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();
        var allJson = GetAllJsonLines(text).Select(s => JsonDocument.Parse(s)).ToList();
        var contents = allJson
                .SelectMany(d => d.RootElement.GetProperty("choices")[0].GetProperty("delta").EnumerateObject()
                    .Where(p => p.NameEquals("content"))
                    .Select(p => p.Value.GetString() ?? string.Empty))
                .ToList();
        contents.Where(c => !string.IsNullOrEmpty(c)).Last().Should().Be("<|user_post|><|text_message|> plain");
    }

    [Fact]
    public async Task Emits_Multiple_Chunks_Before_Done()
    {
        // Arrange: force small chunks and a long message to produce multiple chunks deterministically
        var handler = new TestSseMessageHandler { ChunkDelayMs = 0, WordsPerChunk = 3 };
        using var invoker = new HttpMessageInvoker(handler);
        var longMessage = string.Join(" ", Enumerable.Repeat("lorem", 30));
        var req = BuildRequest(longMessage, stream: true);

        // Act
        var res = await invoker.SendAsync(req, default);
        var text = await res.Content.ReadAsStringAsync();

        // Assert
        var dataLines = text.Split('\n').Where(l => l.StartsWith("data: ", StringComparison.Ordinal)).ToList();
        dataLines.Count.Should().BeGreaterThan(3, "expected multiple SSE chunks to be emitted");
        text.Should().Contain("[DONE]");
    }

    /// <summary>
    /// Gets the first JSON line from the SSE response.
    /// </summary>
    /// <param name="response">The SSE response content.</param>
    /// <returns>The first JSON line found.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no JSON line is found.</exception>
    private static string GetFirstJsonLine(string response)
    {
        foreach (var line in response.Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && !line.Contains("[DONE]", StringComparison.Ordinal))
            {
                return line.Substring(6).Trim();
            }
        }

        throw new InvalidOperationException("No JSON line found");
    }

    /// <summary>
    /// Gets all JSON lines from the SSE response.
    /// </summary>
    /// <param name="response">The SSE response content.</param>
    /// <returns>An enumerable collection of JSON lines.</returns>
    private static IEnumerable<string> GetAllJsonLines(string response)
    {
        foreach (var line in response.Split('\n'))
        {
            if (line.StartsWith("data: ", StringComparison.Ordinal) && !line.Contains("[DONE]", StringComparison.Ordinal))
            {
                yield return line.Substring(6).Trim();
            }
        }
    }
}