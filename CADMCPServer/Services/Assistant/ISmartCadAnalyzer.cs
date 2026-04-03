using CADMCPServer.Models;

namespace CADMCPServer.Services.Assistant;

public interface ISmartCadAnalyzer
{
    SmartCadAnalysis Analyze(AnalyzeRequest request, ConversationContext context, IReadOnlyCollection<string> orchestrationAssumptions);
    string Format(SmartCadAnalysis analysis);
}
