namespace SecureApp.Domain.Exceptions;

public sealed class UnsupportedFileFormatException : DomainException
{
    public string FileName { get; }

    public UnsupportedFileFormatException(string fileName)
        : base($"'{fileName}' has an unsupported or unrecognized file format.")
    {
        FileName = fileName;
    }
}
