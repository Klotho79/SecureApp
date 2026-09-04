namespace SecureApp.Domain.Exceptions;

public sealed class DocumentNotFoundException : DomainException
{
    public Guid DocumentId { get; }

    public DocumentNotFoundException(Guid documentId)
        : base($"Document '{documentId}' was not found.")
    {
        DocumentId = documentId;
    }
}
