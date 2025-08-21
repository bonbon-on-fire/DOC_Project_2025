using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Reflection;
using AIChat.Server.Storage;
using AchieveAi.LmDotnetTools.Misc.Utils;

namespace AIChat.Server.Services;

/// <summary>
/// Improved TaskManagerService that properly handles task state persistence
/// by parsing tasks from markdown and restoring them using the bulk-initialize function
/// </summary>
public class ImprovedTaskManagerService : ITaskManagerService
{
    private readonly ITaskStorage _taskStorage;
    private readonly ILogger<ImprovedTaskManagerService> _logger;
    private readonly ConcurrentDictionary<string, TaskManagerState> _taskManagers;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    private class TaskManagerState
    {
        public TaskManager Manager { get; set; }
        public int Version { get; set; }
        public List<TaskItem> Tasks { get; set; } = new();
        
        public class TaskItem
        {
            public string Id { get; set; } = "";
            public string Title { get; set; } = "";
            public string Status { get; set; } = "NotStarted";
            public List<TaskItem> Subtasks { get; set; } = new();
            public List<string> Notes { get; set; } = new();
        }
    }

    public ImprovedTaskManagerService(ITaskStorage taskStorage, ILogger<ImprovedTaskManagerService> logger)
    {
        _taskStorage = taskStorage;
        _logger = logger;
        _taskManagers = new ConcurrentDictionary<string, TaskManagerState>();
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
        var state = new TaskManagerState 
        { 
            Manager = taskManager,
            Version = 0
        };
        
        if (taskState != null)
        {
            _logger.LogInformation("Loading existing tasks for chat {ChatId}, version {Version}", chatId, taskState.Version);
            state.Version = taskState.Version;
            
            // Restore tasks from the saved state
            await RestoreTaskManagerStateAsync(state, taskState.Tasks, ct);
        }
        else
        {
            _logger.LogInformation("Creating new TaskManager for chat {ChatId}", chatId);
        }

        // Cache it
        _taskManagers.TryAdd(chatId, state);
        
        return taskManager;
    }

    public async Task SaveTaskManagerStateAsync(string chatId, CancellationToken ct = default)
    {
        if (!_taskManagers.TryGetValue(chatId, out var state))
        {
            _logger.LogWarning("Cannot save TaskManager state for chat {ChatId} - not loaded", chatId);
            return;
        }

        await _saveLock.WaitAsync(ct);
        try
        {
            // Parse the current markdown to extract structured task data
            var markdown = state.Manager.GetMarkdown();
            state.Tasks = ParseTasksFromMarkdown(markdown);
            
            // Create JSON representation of tasks
            var taskStateJson = JsonSerializer.Serialize(new
            {
                tasks = state.Tasks,
                markdown = markdown,
                timestamp = DateTime.UtcNow
            });
            
            var taskElement = JsonDocument.Parse(taskStateJson).RootElement.Clone();
            
            // Save to storage with optimistic concurrency
            var newState = await _taskStorage.SaveTasksAsync(chatId, taskElement, state.Version, ct);
            
            // Update cached version
            state.Version = newState.Version;
            
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
        
        // Also include structured task data if available
        var tasks = _taskManagers.TryGetValue(chatId, out var state) ? state.Tasks : ParseTasksFromMarkdown(markdown);
        
        var json = JsonSerializer.Serialize(new 
        { 
            markdown,
            chatId,
            tasks,
            taskCount = CountTasks(tasks)
        });
        
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private async Task RestoreTaskManagerStateAsync(TaskManagerState state, JsonElement savedState, CancellationToken ct)
    {
        try
        {
            // Try to get structured task data from saved state
            if (savedState.TryGetProperty("tasks", out var tasksElement))
            {
                var tasksJson = tasksElement.GetRawText();
                state.Tasks = JsonSerializer.Deserialize<List<TaskManagerState.TaskItem>>(tasksJson) ?? new List<TaskManagerState.TaskItem>();
                
                _logger.LogInformation("Restored {TaskCount} tasks from saved state", CountTasks(state.Tasks));
                
                // Now restore tasks into the TaskManager using bulk-initialize
                if (state.Tasks.Count > 0)
                {
                    var bulkTasks = ConvertToBulkTaskItems(state.Tasks);
                    var result = CallBulkInitialize(state.Manager, bulkTasks, clearExisting: true);
                    
                    if (result.StartsWith("Error"))
                    {
                        _logger.LogError("Failed to restore tasks via bulk-initialize: {Error}", result);
                    }
                    else
                    {
                        _logger.LogInformation("Successfully restored tasks via bulk-initialize: {Result}", result);
                        
                        // After bulk initialize, restore task statuses
                        RestoreTaskStatuses(state.Manager, state.Tasks);
                    }
                }
            }
            else if (savedState.TryGetProperty("markdown", out var markdownElement))
            {
                var markdown = markdownElement.GetString();
                if (!string.IsNullOrEmpty(markdown))
                {
                    state.Tasks = ParseTasksFromMarkdown(markdown);
                    _logger.LogDebug("Parsed {TaskCount} tasks from markdown", CountTasks(state.Tasks));
                    
                    // Restore parsed tasks into TaskManager
                    if (state.Tasks.Count > 0)
                    {
                        var bulkTasks = ConvertToBulkTaskItems(state.Tasks);
                        var result = CallBulkInitialize(state.Manager, bulkTasks, clearExisting: true);
                        
                        if (result.StartsWith("Error"))
                        {
                            _logger.LogError("Failed to restore tasks from markdown via bulk-initialize: {Error}", result);
                        }
                        else
                        {
                            _logger.LogInformation("Successfully restored tasks from markdown via bulk-initialize: {Result}", result);
                            
                            // After bulk initialize, restore task statuses
                            RestoreTaskStatuses(state.Manager, state.Tasks);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring TaskManager state");
        }
        
        await Task.CompletedTask;
    }

    private List<TaskManagerState.TaskItem> ParseTasksFromMarkdown(string markdown)
    {
        var tasks = new List<TaskManagerState.TaskItem>();
        
        if (string.IsNullOrWhiteSpace(markdown))
            return tasks;
        
        // Parse the markdown to extract tasks
        // TaskManager uses format like:
        // ## 📋 Task List
        // ### Status: ✓ 1/3 completed
        // - [ ] 1. Task title
        // - [✓] 2. Completed task
        // - [→] 3. In progress task
        //   - [ ] 3.1. Subtask
        
        var lines = markdown.Split('\n');
        TaskManagerState.TaskItem? currentTask = null;
        var taskStack = new Stack<TaskManagerState.TaskItem>();
        
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            
            // Match task lines
            var taskMatch = Regex.Match(trimmed, @"^- \[([ ✓→x])\] (\d+(?:\.\d+)*)\. (.+)$");
            if (taskMatch.Success)
            {
                var status = taskMatch.Groups[1].Value switch
                {
                    "✓" => "Completed",
                    "→" => "InProgress",
                    "x" => "Removed",
                    _ => "NotStarted"
                };
                
                var id = taskMatch.Groups[2].Value;
                var title = taskMatch.Groups[3].Value;
                
                var task = new TaskManagerState.TaskItem
                {
                    Id = id,
                    Title = title,
                    Status = status
                };
                
                // Determine nesting level by counting dots in ID
                var level = id.Count(c => c == '.');
                
                // Pop stack to appropriate level
                while (taskStack.Count > level)
                {
                    taskStack.Pop();
                }
                
                if (taskStack.Count == 0)
                {
                    // Root level task
                    tasks.Add(task);
                }
                else
                {
                    // Subtask
                    var parent = taskStack.Peek();
                    parent.Subtasks.Add(task);
                }
                
                currentTask = task;
                taskStack.Push(task);
            }
            // Match notes (indented lines after a task that start with "Note:")
            else if (currentTask != null && trimmed.StartsWith("Note:"))
            {
                var note = trimmed.Substring(5).Trim();
                currentTask.Notes.Add(note);
            }
        }
        
        return tasks;
    }

    private int CountTasks(List<TaskManagerState.TaskItem> tasks)
    {
        int count = tasks.Count;
        foreach (var task in tasks)
        {
            count += CountTasks(task.Subtasks);
        }
        return count;
    }
    
    /// <summary>
    /// Converts our internal TaskItem representation to TaskManager's BulkTaskItem format
    /// </summary>
    private List<TaskManager.BulkTaskItem> ConvertToBulkTaskItems(List<TaskManagerState.TaskItem> tasks)
    {
        var bulkItems = new List<TaskManager.BulkTaskItem>();
        
        foreach (var task in tasks)
        {
            var bulkItem = new TaskManager.BulkTaskItem
            {
                Task = task.Title,
                SubTasks = task.Subtasks.Select(st => st.Title).ToList(),
                Notes = task.Notes.ToList()
            };
            
            bulkItems.Add(bulkItem);
        }
        
        return bulkItems;
    }
    
    /// <summary>
    /// Calls the bulk-initialize function on TaskManager using reflection if needed
    /// </summary>
    private string CallBulkInitialize(TaskManager taskManager, List<TaskManager.BulkTaskItem> tasks, bool clearExisting)
    {
        try
        {
            // The BulkInitialize method is public, so we can call it directly
            var result = taskManager.BulkInitialize(tasks, clearExisting);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling BulkInitialize on TaskManager");
            return $"Error: Failed to initialize tasks - {ex.Message}";
        }
    }
    
    /// <summary>
    /// Calls the update-task function to restore task status
    /// </summary>
    private void RestoreTaskStatuses(TaskManager taskManager, List<TaskManagerState.TaskItem> tasks)
    {
        try
        {
            // After bulk-initialize, we need to update statuses of tasks that are not "NotStarted"
            var taskIdMap = new Dictionary<string, int>();
            var currentId = 1;
            
            // Build ID mapping (assuming tasks are added in order)
            foreach (var task in tasks)
            {
                taskIdMap[task.Id] = currentId++;
                foreach (var subtask in task.Subtasks)
                {
                    // Subtasks get their own IDs in sequence
                    taskIdMap[subtask.Id] = currentId++;
                }
            }
            
            // Update statuses for main tasks and subtasks
            foreach (var task in tasks)
            {
                if (task.Status != "NotStarted" && taskIdMap.TryGetValue(task.Id, out var taskId))
                {
                    var status = task.Status.ToLowerInvariant() switch
                    {
                        "completed" => "completed",
                        "inprogress" => "in progress",
                        "removed" => "removed",
                        _ => "not started"
                    };
                    
                    var updateResult = taskManager.UpdateTask(taskId, null, status);
                    _logger.LogDebug("Restored task {TaskId} status to {Status}: {Result}", taskId, status, updateResult);
                }
                
                // Update statuses for subtasks (they are updated by their own ID, not as subtasks of parent)
                foreach (var subtask in task.Subtasks)
                {
                    if (subtask.Status != "NotStarted" && taskIdMap.TryGetValue(subtask.Id, out var subtaskId))
                    {
                        var status = subtask.Status.ToLowerInvariant() switch
                        {
                            "completed" => "completed",
                            "inprogress" => "in progress",
                            "removed" => "removed",
                            _ => "not started"
                        };
                        
                        // For subtasks, we need to use the parent task ID and the subtask's relative position
                        // Since TaskManager tracks subtasks under their parent
                        if (taskIdMap.TryGetValue(task.Id, out var parentId))
                        {
                            // The subtask ID in the update call should be its ID, not position
                            var updateResult = taskManager.UpdateTask(parentId, subtaskId, status);
                            _logger.LogDebug("Restored subtask {SubtaskId} of task {ParentId} status to {Status}: {Result}", 
                                subtaskId, parentId, status, updateResult);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error restoring task statuses");
        }
    }
}