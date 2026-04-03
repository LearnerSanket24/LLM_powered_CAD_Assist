using CADMCPServer.Models;

namespace CADMCPServer.Services.Conversation;

public interface IConversationStore
{
    ConversationContext GetOrCreate(string sessionId);
    void Save(ConversationContext context);
}
