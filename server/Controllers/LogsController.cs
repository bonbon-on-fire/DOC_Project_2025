using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Serilog;

namespace AIChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LogsController(ILogger<LogsController> logger) : ControllerBase
{
    private readonly static JsonSerializerOptions S_JsonSerializerOptions = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<LogsController> _logger = logger;
    private static readonly string ClientLogFile = Path.Combine(
        Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? Directory.GetCurrentDirectory(),
        "logs", "client", "app.jsonl");

    // Accept POST /api/logs
    [HttpPost]
    public async Task<IActionResult> LogClientEntry([FromBody] JsonElement logEntry)
    {
        try
        {
            // Ensure the client logs directory exists
            var directory = Path.GetDirectoryName(ClientLogFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Serialize the log entry as JSONL (one JSON object per line)
            var jsonString = JsonSerializer.Serialize(logEntry, S_JsonSerializerOptions);

            // Append to the JSONL file
            await System.IO.File.AppendAllTextAsync(ClientLogFile, jsonString + Environment.NewLine);

            // Also log to server's structured logging system for correlation
            _logger.LogInformation("Client log entry received: {ClientLog}", jsonString);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write client log entry");
            return StatusCode(500, "Failed to write log entry");
        }
    }
}
