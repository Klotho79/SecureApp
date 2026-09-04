namespace SecureApp.Domain.Exceptions;

public sealed class ChatSessionClosedException : DomainException
{
    public Guid ChatSessionId { get; }

    public ChatSessionClosedException(Guid chatSessionId)
        : base($"Chat session '{chatSessionId}' is closed and cannot send or receive messages.")
    {
        ChatSessionId = chatSessionId;
    }
}
