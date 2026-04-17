using BodyCam.Models;

namespace BodyCam.Agents;

/// <summary>
/// Local history tracker. Realtime API handles reasoning — this just records transcripts.
/// </summary>
public class ConversationAgent
{
    public void AddUserMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "user", Content = transcript });
    }

    public void AddAssistantMessage(string transcript, SessionContext session)
    {
        session.Messages.Add(new ChatMessage { Role = "assistant", Content = transcript });
    }
}
