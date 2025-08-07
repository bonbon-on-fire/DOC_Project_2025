using Microsoft.EntityFrameworkCore;
using AIChat.Server.Data;
using AIChat.Server.Services;
using AIChat.Server.Hubs;
using AchieveAi.LmDotnetTools.LmConfig.Services;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.OpenAIProvider.Agents;
using AchieveAi.LmDotnetTools.Misc.Storage;
using AchieveAi.LmDotnetTools.Misc.Configuration;
using AchieveAi.LmDotnetTools.Misc.Http;
using Lib.AspNetCore.ServerSentEvents;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Entity Framework with SQLite
builder.Services.AddDbContext<AIChatDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

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

// Register IStreamingAgent as scoped service
builder.Services.AddTransient<IStreamingAgent>(
    provider => {
    // Register IStreamingAgent as an OpenAIProvider-based agent with caching
    // Get configuration - prioritize environment variables, then User Secrets/config
    var configuration = provider.GetRequiredService<IConfiguration>();
    var logger = provider.GetRequiredService<ILogger<Program>>();
    
    var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY") 
                 // ?? configuration["OpenAI:ApiKey"] 
                 ?? "";
    var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_API_URL") 
                  // ?? configuration["OpenAI:BaseUrl"] 
                  ?? "https://api.openai.com/v1";

    // Diagnostic logging for API configuration
    logger.LogInformation("[DIAGNOSTIC] API Configuration:");
    logger.LogInformation("[DIAGNOSTIC] Base URL: {BaseUrl}", baseUrl);
    logger.LogInformation("[DIAGNOSTIC] API Key Length: {ApiKeyLength}", apiKey?.Length ?? 0);
    logger.LogInformation("[DIAGNOSTIC] API Key Prefix: {ApiKeyPrefix}", apiKey?.Length > 10 ? apiKey.Substring(0, 10) + "..." : "[EMPTY]");

    // Create an OpenAI client with caching
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new InvalidOperationException("OpenAI API key is required but was not provided.");
    }
    
    // Create cache infrastructure
    var cacheDirectory = configuration["LlmCache:CacheDirectory"] ?? "./llm-cache";
    var cache = new AchieveAi.LmDotnetTools.Misc.Storage.FileKvStore(cacheDirectory);
    
    // Configure cache options
    var cacheOptions = new AchieveAi.LmDotnetTools.Misc.Configuration.LlmCacheOptions
    {
        EnableCaching = configuration.GetValue<bool>("LlmCache:EnableCaching", true),
        CacheExpiration = configuration.GetValue<TimeSpan?>("LlmCache:CacheExpiration", TimeSpan.FromHours(24)),
        MaxCacheItems = configuration.GetValue<int?>("LlmCache:MaxCacheItems", 10000)
    };
    
    // Create HTTP client with caching handler
    var httpClientHandler = new HttpClientHandler();
    var cachingHandler = new AchieveAi.LmDotnetTools.Misc.Http.CachingHttpMessageHandler(cache, cacheOptions, httpClientHandler, logger);
    
    var httpClient = new HttpClient(cachingHandler)
    {
        BaseAddress = new Uri(baseUrl),
        Timeout = TimeSpan.FromMinutes(5)
    };
    
    // Add authentication headers
    httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    
    var openClient = new AchieveAi.LmDotnetTools.OpenAIProvider.Agents.OpenClient(httpClient, baseUrl, null, logger);
    return new AchieveAi.LmDotnetTools.OpenAIProvider.Agents.OpenClientAgent("OpenAi", openClient);
});

// Add CORS for development
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

// Add logging
builder.Services.AddLogging();

// Add Server-Sent Events services
builder.Services.AddServerSentEvents();

// Add custom SSE service
builder.Services.AddScoped<SseService>();

// Add message sequence service
builder.Services.AddScoped<IMessageSequenceService, MessageSequenceService>();

// Add chat service
builder.Services.AddScoped<IChatService, ChatService>();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Apply database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AIChatDbContext>();
    try
    {
        context.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database");
    }
}

app.UseCors("AllowSvelteApp");

app.UseHttpsRedirection();

app.MapControllers();
app.MapHub<ChatHub>("/api/chat-hub");

// Add Server-Sent Events endpoint
app.MapServerSentEvents("/api/chat-sse");

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTime.UtcNow }));

app.Run();
