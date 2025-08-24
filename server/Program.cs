using AIChat.Server.Services;
using AIChat.Server.Hubs;
using AchieveAi.LmDotnetTools.LmConfig.Services;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.OpenAIProvider.Agents;
using AchieveAi.LmDotnetTools.Misc.Storage;
using AchieveAi.LmDotnetTools.Misc.Configuration;
using AchieveAi.LmDotnetTools.Misc.Http;
using Lib.AspNetCore.ServerSentEvents;
using AIChat.Server.Models;
using AIChat.Server.Services.TestMode;
using AIChat.Server.Logging;
using AIChat.Server.Storage.Sqlite;
using AIChat.Server.Storage;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for JSON file logging - ensure logs go to project root
var projectRoot = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? Directory.GetCurrentDirectory();
var logFileName = builder.Environment.EnvironmentName switch
{
    "Development" => Path.Combine(projectRoot, "logs", "server", "app-dev.jsonl"),
    "Test" => Path.Combine(projectRoot, "logs", "server", "app-test.jsonl"),
    _ => Path.Combine(projectRoot, "logs", "server", "app.jsonl")
};

// Ensure log directory exists
Directory.CreateDirectory(Path.GetDirectoryName(logFileName)!);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Verbose()
    .WriteTo.Console()
    .WriteTo.File(
        new CompactJsonFormatter(), 
        logFileName,
        shared: true,
        buffered: false,
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Verbose)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Use the same serialization options as MessageSerializationOptions.Default
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter(System.Text.Json.JsonNamingPolicy.CamelCase));
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure storage (replace EF)
builder.Services.AddSingleton<SqliteConnectionFactory>(sp =>
{
    var env = sp.GetRequiredService<IHostEnvironment>();
    var config = sp.GetRequiredService<IConfiguration>();

    var connStr = config.GetConnectionString("DefaultConnection");
    var keepRootOpen = false;

    if (env.IsEnvironment("Test"))
    {
        // Default to in-memory shared cache if not overridden
        if (string.IsNullOrWhiteSpace(connStr))
        {
            connStr = "Data Source=File:aichat_test?mode=memory&cache=shared";
        }
        keepRootOpen = true;
    }
    else if (string.IsNullOrWhiteSpace(connStr))
    {
        // Fallback default for non-Test when not supplied by config
        connStr = "Data Source=aichat.db";
    }

    return new SqliteConnectionFactory(connStr!, keepRootOpen);
});

builder.Services.AddSingleton<ISqliteConnectionFactory>(sp => sp.GetRequiredService<SqliteConnectionFactory>());

// Register IChatStorage and ITaskStorage
builder.Services.AddScoped<IChatStorage, SqliteChatStorage>();
builder.Services.AddScoped<ITaskStorage, SqliteTaskStorage>();

// Register TaskManagerService (using improved version)
builder.Services.AddScoped<ITaskManagerService, ImprovedTaskManagerService>();

// Add SignalR with increased timeout values
builder.Services.AddSignalR(hubOptions =>
{
    // Set client timeout interval to 5 minutes (time window clients have to send a message)
    hubOptions.ClientTimeoutInterval = TimeSpan.FromMinutes(10);
    // Set keep alive interval to 2 minutes (how often the server sends a ping message)
    hubOptions.KeepAliveInterval = TimeSpan.FromMinutes(4);
});

// Add LmConfig services
builder.Services.AddLmConfig(builder.Configuration.GetSection("LmConfig"));

// Bind AI model selection options
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("AI"));

// Configure MCP servers
builder.Services.Configure<McpConfiguration>(builder.Configuration.GetSection("Mcp"));
builder.Services.AddSingleton<IMcpClientManager, McpClientManager>();

// Register IStreamingAgent as scoped service
builder.Services.AddTransient<IStreamingAgent>(
    provider =>
    {
        // Register IStreamingAgent as an OpenAIProvider-based agent with caching
        // Get configuration - prioritize environment variables, then User Secrets/config
        var configuration = provider.GetRequiredService<IConfiguration>();
        var logger = provider.GetRequiredService<ILogger<Program>>();
        var hostEnv = provider.GetRequiredService<IHostEnvironment>();

        var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY")
                     ?? configuration["OpenAI:ApiKey"]
                     ?? "";
        var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_API_URL")
                      ?? configuration["OpenAI:BaseUrl"]
                      ?? "https://api.openai.com/v1";

        // Diagnostic logging for API configuration
        logger.LogInformation("[DIAGNOSTIC] API Configuration:");
        logger.LogInformation("[DIAGNOSTIC] Base URL: {BaseUrl}", baseUrl);
        logger.LogInformation("[DIAGNOSTIC] API Key Length: {ApiKeyLength}", apiKey?.Length ?? 0);
        logger.LogInformation("[DIAGNOSTIC] API Key Prefix: {ApiKeyPrefix}", apiKey?.Length > 10 ? apiKey.Substring(0, 10) + "..." : "[EMPTY]");

        if (hostEnv.IsEnvironment("Test"))
        {
            // In Test environment, synthesize streaming via TestSseMessageHandler and bypass API key
            var testHandler = new TestSseMessageHandler();
            var testHttpClient = new HttpClient(testHandler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromMinutes(5)
            };
            var openClientTest = new OpenClient(testHttpClient, baseUrl, null, logger);
            return new OpenClientAgent("OpenAi", openClientTest);
        }

        // Create an OpenAI client with caching (non-Test environments)
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("OpenAI API key is required but was not provided.");
        }

        // Create cache infrastructure
        var cacheDirectory = configuration["LlmCache:CacheDirectory"] ?? "./llm-cache";
        var cache = new FileKvStore(cacheDirectory);

        // Configure cache options
        var cacheOptions = new LlmCacheOptions
        {
            EnableCaching = configuration.GetValue<bool>("LlmCache:EnableCaching", true),
            CacheExpiration = configuration.GetValue<TimeSpan?>("LlmCache:CacheExpiration", TimeSpan.FromHours(24)),
            MaxCacheItems = configuration.GetValue<int?>("LlmCache:MaxCacheItems", 10000)
        };

        // Create HTTP client with caching handler
        var httpClientHandler = new HttpClientHandler();
        var cachingHandler = new CachingHttpMessageHandler(cache, cacheOptions, httpClientHandler, logger);

        var httpClient = new HttpClient(cachingHandler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(5)
        };

        // Add authentication headers
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        var openClient = new OpenClient(httpClient, baseUrl, null, logger);
        return new OpenClientAgent("OpenAi", openClient);
    });

// Add CORS for development and test
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSvelteApp", policy =>
    {
        policy.WithOrigins(
            "http://localhost:5173",
            "http://localhost:5174",
            "http://localhost:5175",
            "http://localhost:5176",
            "http://localhost:5177",
            "http://localhost:5178",
            "http://localhost:5179",
            "http://localhost:5180",
            "http://localhost:5182",
            "http://localhost:5183",
            "http://localhost:5183",
            "http://localhost:4173",
            "http://localhost:5174")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// Add timestamped Debug logger for Dev/Test so VS Output shows timestamps
builder.Services.AddLogging(logging =>
{
    // Add our timestamped Debug provider so VS Immediate/Output shows timestamps
    if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Test"))
    {
        logging.AddProvider(new TimestampedDebugLoggerProvider());
    }
});

// Add Server-Sent Events services
builder.Services.AddServerSentEvents();

// Add task management services
// Removed ChatTaskManager - using TaskManager from LmDotNet directly

// Add chat service
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Initialize database schema
using (var scope = app.Services.CreateScope())
{
    var env = app.Environment;
    var factory = scope.ServiceProvider.GetRequiredService<SqliteConnectionFactory>();
    if (env.IsEnvironment("Test"))
    {
        await TestDatabaseInitializer.InitializeAsync(factory);
    }
    else
    {
        // Idempotent ensure schema and seed users
        await using var conn = await factory.CreateOpenConnectionAsync();
        await SchemaHelper.EnsureSchemaAsync(conn);
        await SchemaHelper.SeedUsersAsync(conn);
    }
}

app.UseCors("AllowSvelteApp");

// Skip HTTPS redirection in Test (HTTP-only)
if (!app.Environment.IsEnvironment("Test"))
{
    app.UseHttpsRedirection();
}

app.MapControllers();
app.MapHub<ChatHub>("/api/chat-hub");

// Add Server-Sent Events endpoint
app.MapServerSentEvents("/api/chat-sse");

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.Run();

public partial class Program { }
