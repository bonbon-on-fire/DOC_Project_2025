using AIChat.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace AIChat.Server.Services;

public interface IMessageSequenceService
{
    Task<int> GetNextSequenceNumberAsync(string chatId);
}

public class MessageSequenceService : IMessageSequenceService
{
    private readonly AIChatDbContext _dbContext;

    public MessageSequenceService(AIChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<int> GetNextSequenceNumberAsync(string chatId)
    {
        // InMemory provider does not support transactions; compute without transaction in that case
        if (_dbContext.Database.IsInMemory())
        {
            var chat = await _dbContext.Chats
                .Where(c => c.Id == chatId)
                .FirstOrDefaultAsync();

            if (chat == null)
            {
                throw new InvalidOperationException($"Chat with ID {chatId} not found");
            }

            var maxSequenceNumber = await _dbContext.Messages
                .Where(m => m.ChatId == chatId)
                .MaxAsync(m => (int?)m.SequenceNumber) ?? -1;

            return maxSequenceNumber + 1;
        }

        // Use a transaction to ensure atomicity when calculating the next sequence number (relational providers)
        await using var transaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var chat = await _dbContext.Chats
                .Where(c => c.Id == chatId)
                .FirstOrDefaultAsync();

            if (chat == null)
            {
                throw new InvalidOperationException($"Chat with ID {chatId} not found");
            }

            var maxSequenceNumber = await _dbContext.Messages
                .Where(m => m.ChatId == chatId)
                .MaxAsync(m => (int?)m.SequenceNumber) ?? -1;

            var nextSequenceNumber = maxSequenceNumber + 1;

            await transaction.CommitAsync();
            return nextSequenceNumber;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
