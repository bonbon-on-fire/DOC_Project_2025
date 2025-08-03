using Microsoft.EntityFrameworkCore;
using AIChat.Server.Models;

namespace AIChat.Server.Data;

public class AIChatDbContext : DbContext
{
    public AIChatDbContext(DbContextOptions<AIChatDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Chat> Chats { get; set; }
    public DbSet<Message> Messages { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure User entity
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.Name).IsRequired();
            entity.Property(e => e.Provider).IsRequired();
        });

        // Configure Chat entity
        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            
            // Configure relationship with User
            entity.HasOne(e => e.User)
                  .WithMany(u => u.Chats)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure Message entity
        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Content).IsRequired();
            entity.Property(e => e.Role).IsRequired();
            
            // Configure relationship with Chat
            entity.HasOne(e => e.Chat)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ChatId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Create index for better query performance
            entity.HasIndex(e => new { e.ChatId, e.Timestamp });
        });

        // Seed data (optional)
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Create a default system user for AI responses
        var systemUserId = "system-user-id";
        var demoUserId = "user-123";
        
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = systemUserId,
                Email = "system@aichat.com",
                Name = "System",
                Provider = "internal",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = demoUserId,
                Email = "demo@aichat.com",
                Name = "Demo User",
                Provider = "demo",
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}