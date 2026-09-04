using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IEncryptionKeyMetadataRepository
{
    Task<EncryptionKeyMetadata?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<EncryptionKeyMetadata?> GetActiveKeyAsync(KeyPurpose purpose, CancellationToken ct = default);
    Task AddAsync(EncryptionKeyMetadata keyMetadata, CancellationToken ct = default);
    Task UpdateAsync(EncryptionKeyMetadata keyMetadata, CancellationToken ct = default);
}
