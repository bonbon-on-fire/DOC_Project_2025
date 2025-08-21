using System.Collections.Concurrent;
using System.Text.Json;
using AIChat.Server.Storage;
using AchieveAi.LmDotnetTools.Misc.Utils;

namespace AIChat.Server.Services;

/// <summary>
/// Service that manages TaskManager instances per chat with persistence
/// </summary>
public interface ITaskManagerService
{
    /// <summary>
    /// Gets or creates a TaskManager for the specified chat
    /// </summary>
    Task<TaskManager> GetTaskManagerAsync(string chatId, CancellationToken ct = default);
    
    /// <summary>
    /// Saves the current state of a chat's TaskManager
    /// </summary>
    Task SaveTaskManagerStateAsync(string chatId, CancellationToken ct = default);
    
    /// <summary>
    /// Clears the TaskManager for a chat (when chat is deleted)
    /// </summary>
    Task ClearTaskManagerAsync(string chatId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the current task state as JSON for a chat
    /// </summary>
    Task<JsonElement?> GetTaskStateAsync(string chatId, CancellationToken ct = default);
}

public class TaskManagerService : ITaskManagerService
{
    private readonly ITaskStorage _taskStorage;
    private readonly ILogger<TaskManagerService> _logger;
    private readonly ConcurrentDictionary<string, (TaskManager Manager, int Version)> _taskManagers;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public TaskManagerService(ITaskStorage taskStorage, ILogger<TaskManagerService> logger)
    {
        _taskStorage = taskStorage;
        _logger = logger;
        _taskManagers = new ConcurrentDictionary<string, (TaskManager, int)>();
    }

    public async Task<TaskManager> GetTaskManagerAsync(string chatId, CancellationToken ct = default)
    {
        // Check if we already have it in memory
        if (_taskManagers.TryGetValue(chatId, out var cached))
        {
            _logger.LogDebug("Returning cached TaskManager for chat {ChatId}", chatId);
            return cached.Manager;
        }

        // Try to load from storage
        var taskState = await _taskStorage.GetTasksAsync(chatId, ct);
        
        var taskManager = new TaskManager();
        var version = 0;
        
        if (taskState != null)
        {
            _logger.LogInformation("Loading existing tasks for chat {ChatId}, version {Version}", chatId, taskState.Version);
            version = taskState.Version;
            
            // Restore tasks from the saved state
            await RestoreTaskManagerStateAsync(taskManager, taskState.Tasks, ct);
        }
        else
        {
            _logger.LogInformation("Creating new TaskManager for chat {ChatId}", chatId);
        }

        // Cache it
        _taskManagers.TryAdd(chatId, (taskManager, version));
        
        return taskManager;
    }

    public async Task SaveTaskManagerStateAsync(string chatId, CancellationToken ct = default)
    {
        if (!_taskManagers.TryGetValue(chatId, out var cached))
        {
            _logger.LogWarning("Cannot save TaskManager state for chat {ChatId} - not loaded", chatId);
            return;
        }

        await _saveLock.WaitAsync(ct);
        try
        {
            // Get current state from TaskManager
            var taskState = ExtractTaskManagerState(cached.Manager);
            
            // Save to storage with optimistic concurrency
            var newState = await _taskStorage.SaveTasksAsync(chatId, taskState, cached.Version, ct);
            
            // Update cached version
            _taskManagers.TryUpdate(chatId, (cached.Manager, newState.Version), cached);
            
            _logger.LogInformation("Saved TaskManager state for chat {ChatId}, new version {Version}", chatId, newState.Version);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Version conflict"))
        {
            _logger.LogWarning(ex, "Version conflict when saving tasks for chat {ChatId}, will reload", chatId);
            
            // Clear from cache to force reload next time
            _taskManagers.TryRemove(chatId, out _);
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public async Task ClearTaskManagerAsync(string chatId, CancellationToken ct = default)
    {
        _logger.LogInformation("Clearing TaskManager for chat {ChatId}", chatId);
        
        // Remove from cache
        _taskManagers.TryRemove(chatId, out _);
        
        // Delete from storage
        await _taskStorage.DeleteTasksAsync(chatId, ct);
    }

    public async Task<JsonElement?> GetTaskStateAsync(string chatId, CancellationToken ct = default)
    {
        // Get or create the TaskManager
        var taskManager = await GetTaskManagerAsync(chatId, ct);
        
        // Get current state
        var markdown = taskManager.GetMarkdown();
        var json = JsonSerializer.Serialize(new { markdown, chatId });
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private JsonElement ExtractTaskManagerState(TaskManager taskManager)
    {
        // TaskManager doesn't expose internal state directly, so we use the markdown representation
        // and store it in a format we can restore from
        var markdown = taskManager.GetMarkdown();
        
        // For now, store the markdown and reconstruct tasks from it
        // In a real implementation, TaskManager should expose GetTasks() or similar
        var state = new
        {
            markdown,
            timestamp = DateTime.UtcNow,
            // We could parse the markdown to extract structured task data here if needed
            // For now, just store the markdown for display purposes
        };
        
        var json = JsonSerializer.Serialize(state);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task RestoreTaskManagerStateAsync(TaskManager taskManager, JsonElement savedState, CancellationToken ct)
    {
        // Since TaskManager doesn't have a direct way to restore state,
        // we need to use its bulk-initialize function if available
        // For now, this is a placeholder - the actual implementation depends on TaskManager's API
        
        // Check if we have markdown in the saved state
        if (savedState.TryGetProperty("markdown", out var markdownElement))
        {
            var markdown = markdownElement.GetString();
            if (!string.IsNullOrEmpty(markdown))
            {
                _logger.LogDebug("Restored task markdown for display: {MarkdownLength} chars", markdown.Length);
                // Note: TaskManager state is restored but we can't actually recreate the tasks
                // without access to bulk-initialize or similar methods
                // This is a limitation that needs to be addressed with the LmDotNet team
            }
        }
        
        await Task.CompletedTask;
    }
}