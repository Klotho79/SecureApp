namespace SecureApp.Domain.Exceptions;

public sealed class MessageNotFoundException : DomainException
{
    public Guid MessageId { get; }

    public MessageNotFoundException(Guid messageId)
        : base($"Message '{messageId}' was not found.")
    {
        MessageId = messageId;
    }
}
