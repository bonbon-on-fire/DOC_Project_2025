using Microsoft.EntityFrameworkCore;
using AIChat.Server.Data;
using AIChat.Server.Services;
using AIChat.Server.Hubs;
using AchieveAi.LmDotnetTools.LmConfig.Services;
using AchieveAi.LmDotnetTools.LmCore.Agents;
using AchieveAi.LmDotnetTools.OpenAIProvider.Agents;
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
    // Get environment variables
    var apiKey = Environment.GetEnvironmentVariable("LLM_API_KEY") ?? "";
    var baseUrl = Environment.GetEnvironmentVariable("LLM_BASE_API_URL") ?? "https://api.openai.com/v1";

    // Create an OpenAI client
    var openClient = new OpenClient(apiKey, baseUrl);
    return new OpenClientAgent("OpenAi", openClient);
});

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSvelteApp", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://localhost:4173", "http://localhost:5174")
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
