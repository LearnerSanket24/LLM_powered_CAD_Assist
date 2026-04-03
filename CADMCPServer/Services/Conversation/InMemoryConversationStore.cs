using System.Collections.Concurrent;
using CADMCPServer.Models;

namespace CADMCPServer.Services.Conversation;

public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, ConversationContext> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public ConversationContext GetOrCreate(string sessionId)
    {
        var key = string.IsNullOrWhiteSpace(sessionId) ? "default" : sessionId.Trim();
        return _sessions.GetOrAdd(key, value => new ConversationContext
        {
            SessionId = value,
            UpdatedAt = DateTimeOffset.UtcNow
        });
    }

    public void Save(ConversationContext context)
    {
        context.UpdatedAt = DateTimeOffset.UtcNow;
        _sessions.AddOrUpdate(context.SessionId, context, (_, _) => context);
    }
}
