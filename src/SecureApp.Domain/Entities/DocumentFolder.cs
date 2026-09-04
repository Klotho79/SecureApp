using SecureApp.Domain.Common;

namespace SecureApp.Domain.Entities;

/// <summary>A user-defined folder for organizing documents. Folders may be nested.</summary>
public sealed class DocumentFolder : Entity
{
    public string Name { get; private set; }
    public Guid? ParentFolderId { get; private set; }

    private DocumentFolder()
    {
        Name = string.Empty;
    }

    public DocumentFolder(string name, Guid? parentFolderId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Folder name cannot be empty.", nameof(name));

        Name = name;
        ParentFolderId = parentFolderId;
    }

    public void Rename(string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ArgumentException("Folder name cannot be empty.", nameof(newName));

        Name = newName;
        Touch();
    }

    public void MoveTo(Guid? newParentFolderId)
    {
        if (newParentFolderId == Id)
            throw new InvalidOperationException("A folder cannot be its own parent.");

        ParentFolderId = newParentFolderId;
        Touch();
    }
}
