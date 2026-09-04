namespace SecureApp.Domain.Exceptions;

public sealed class ChatSessionNotFoundException : DomainException
{
    public Guid ChatSessionId { get; }

    public ChatSessionNotFoundException(Guid chatSessionId)
        : base($"Chat session '{chatSessionId}' was not found.")
    {
        ChatSessionId = chatSessionId;
    }
}
