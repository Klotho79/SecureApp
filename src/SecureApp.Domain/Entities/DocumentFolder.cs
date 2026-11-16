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

    /// <summary>Moves the folder under another one (null = the root).</summary>
    public void MoveTo(Guid? parentFolderId)
    {
        if (parentFolderId == Id)
            throw new ArgumentException("A folder cannot contain itself.", nameof(parentFolderId));

        ParentFolderId = parentFolderId;
        Touch();
    }
}
