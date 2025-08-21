using System.Text.Json;
using AIChat.Server.Services;
using AIChat.Server.Storage;
using AchieveAi.LmDotnetTools.Misc.Utils;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace AIChat.Server.Tests.Services;

public class ImprovedTaskManagerServiceTests
{
    private readonly Mock<ITaskStorage> _taskStorageMock;
    private readonly Mock<ILogger<ImprovedTaskManagerService>> _loggerMock;
    private readonly ImprovedTaskManagerService _service;

    public ImprovedTaskManagerServiceTests()
    {
        _taskStorageMock = new Mock<ITaskStorage>();
        _loggerMock = new Mock<ILogger<ImprovedTaskManagerService>>();
        _service = new ImprovedTaskManagerService(_taskStorageMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task GetTaskManagerAsync_WhenNoExistingTasks_CreatesNewTaskManager()
    {
        // Arrange
        var chatId = "test-chat-1";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        taskManager.Should().NotBeNull();
        taskManager.Should().BeOfType<TaskManager>();
        var markdown = taskManager.GetMarkdown();
        markdown.Should().Contain("No tasks");
    }

    [Fact]
    public async Task SaveTaskManagerStateAsync_SavesTasksToStorage()
    {
        // Arrange
        var chatId = "test-chat-2";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Get task manager and add some tasks
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        taskManager.AddTask("Test Task 1");
        taskManager.AddTask("Test Task 2");

        var savedTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = new TaskManager(),
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _taskStorageMock.Setup(x => x.SaveTasksAsync(
                chatId,
                It.IsAny<TaskManager>(),
                0,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedTaskState);

        // Act
        await _service.SaveTaskManagerStateAsync(chatId);

        // Assert
        _taskStorageMock.Verify(x => x.SaveTasksAsync(
            chatId,
            It.IsAny<TaskManager>(),
            0,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTaskManagerAsync_WithExistingTasks_RestoresTasksCorrectly()
    {
        // Arrange
        var chatId = "test-chat-3";
        var taskData = new
        {
            tasks = new object[]
            {
                new
                {
                    Id = "1",
                    Title = "Restored Task 1",
                    Status = "NotStarted",
                    Subtasks = Array.Empty<object>(),
                    Notes = Array.Empty<string>()
                },
                new
                {
                    Id = "2",
                    Title = "Restored Task 2",
                    Status = "Completed",
                    Subtasks = new object[]
                    {
                        new
                        {
                            Id = "2.1",
                            Title = "Subtask 1",
                            Status = "NotStarted",
                            Subtasks = Array.Empty<object>(),
                            Notes = Array.Empty<string>()
                        }
                    },
                    Notes = new[] { "Note for task 2" }
                }
            },
            markdown = "test markdown",
            timestamp = DateTime.UtcNow
        };

        var taskJson = JsonSerializer.Serialize(taskData);
        var taskElement = JsonDocument.Parse(taskJson).RootElement;

        var existingTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = taskElement,
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTaskState);

        // Act
        var taskManager = await _service.GetTaskManagerAsync(chatId);

        // Assert
        taskManager.Should().NotBeNull();
        var markdown = taskManager.GetMarkdown();
        
        // The restored TaskManager should have the tasks from bulk-initialize
        markdown.Should().Contain("Restored Task 1");
        markdown.Should().Contain("Restored Task 2");
        markdown.Should().Contain("Subtask 1");
        
        // Verify the task count
        markdown.Should().Contain("2 tasks");
    }

    [Fact]
    public async Task ClearTaskManagerAsync_RemovesFromCacheAndStorage()
    {
        // Arrange
        var chatId = "test-chat-4";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Get task manager to cache it
        await _service.GetTaskManagerAsync(chatId);

        // Act
        await _service.ClearTaskManagerAsync(chatId);

        // Assert
        _taskStorageMock.Verify(x => x.DeleteTasksAsync(chatId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetTaskStateAsync_ReturnsCorrectJsonStructure()
    {
        // Arrange
        var chatId = "test-chat-5";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        var taskManager = await _service.GetTaskManagerAsync(chatId);
        taskManager.AddTask("Task for JSON");

        // Act
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        taskState.Should().NotBeNull();
        taskState.Value.GetProperty("chatId").GetString().Should().Be(chatId);
        taskState.Value.GetProperty("markdown").GetString().Should().Contain("Task for JSON");
        taskState.Value.GetProperty("taskCount").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task TaskManagersAreIsolatedPerChat()
    {
        // Arrange
        var chatId1 = "test-chat-6";
        var chatId2 = "test-chat-7";
        
        _taskStorageMock.Setup(x => x.GetTasksAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Act
        var taskManager1 = await _service.GetTaskManagerAsync(chatId1);
        var taskManager2 = await _service.GetTaskManagerAsync(chatId2);
        
        taskManager1.AddTask("Task for Chat 1");
        taskManager2.AddTask("Task for Chat 2");

        // Assert
        var markdown1 = taskManager1.GetMarkdown();
        var markdown2 = taskManager2.GetMarkdown();
        
        markdown1.Should().Contain("Task for Chat 1");
        markdown1.Should().NotContain("Task for Chat 2");
        
        markdown2.Should().Contain("Task for Chat 2");
        markdown2.Should().NotContain("Task for Chat 1");
    }

    [Fact]
    public async Task ParseTasksFromMarkdown_HandlesInProgressTasksCorrectly()
    {
        // Arrange
        var chatId = "test-chat-inprogress";
        _taskStorageMock.Setup(x => x.GetTasksAsync(chatId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatTaskState?)null);

        // Create a task manager and add tasks with different statuses
        var taskManager = await _service.GetTaskManagerAsync(chatId);
        taskManager.AddTask("Not Started Task");
        taskManager.AddTask("In Progress Task");
        taskManager.AddTask("Completed Task");
        
        // Update task statuses to test all status symbols
        taskManager.UpdateTask(2, null, "in progress");  // Should become [-]
        taskManager.UpdateTask(3, null, "completed");    // Should become [x]

        // Simulate save and reload cycle to test parsing
        var savedTaskState = new ChatTaskState
        {
            ChatId = chatId,
            TaskManager = JsonDocument.Parse("{}").RootElement,
            Version = 1,
            LastUpdatedUtc = DateTime.UtcNow
        };

        _taskStorageMock.Setup(x => x.SaveTasksAsync(
                chatId,
                It.IsAny<JsonElement>(),
                0,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(savedTaskState);

        // Act - Save and then get the task state
        await _service.SaveTaskManagerStateAsync(chatId);
        var taskState = await _service.GetTaskStateAsync(chatId);

        // Assert
        taskState.Should().NotBeNull();
        
        // Check markdown contains all tasks 
        var markdown = taskState.Value.GetProperty("markdown").GetString();
        markdown.Should().Contain("Not Started Task");
        markdown.Should().Contain("In Progress Task");
        markdown.Should().Contain("Completed Task");
        
        // Most importantly, verify that [-] symbol is present (our fix working)
        markdown.Should().Contain("[-]", "InProgress symbol should be present in markdown");
        
        // Check tasks array contains all tasks with correct statuses
        var tasksArray = taskState.Value.GetProperty("tasks");
        tasksArray.GetArrayLength().Should().Be(3);
        
        // Find any task with InProgress status in the parsed array (the core fix)
        var inProgressTaskFound = false;
        var taskStatuses = new List<string>();
        
        for (int i = 0; i < tasksArray.GetArrayLength(); i++)
        {
            var task = tasksArray[i];
            var status = task.GetProperty("Status").GetString();
            var title = task.GetProperty("Title").GetString();
            taskStatuses.Add($"{title}: {status}");
            
            if (status == "InProgress")
            {
                inProgressTaskFound = true;
            }
        }
        
        // Debug info for troubleshooting
        var statusInfo = string.Join("; ", taskStatuses);
        
        inProgressTaskFound.Should().BeTrue($"At least one InProgress task should be correctly parsed and included in tasks array. Found statuses: {statusInfo}");
        
        // Check task count matches
        var taskCount = taskState.Value.GetProperty("taskCount").GetInt32();
        taskCount.Should().Be(3, "All three tasks should be counted");
    }
}