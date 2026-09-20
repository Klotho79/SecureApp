using SecureApp.Domain.Entities;

namespace SecureApp.Domain.Interfaces.Repositories;

public interface IContactRepository
{
    Task<IReadOnlyList<Contact>> GetAllOrderedAsync(CancellationToken ct = default);
    Task<Contact?> GetByLinkedPublicKeyAsync(byte[] publicKey, CancellationToken ct = default);
    Task AddAsync(Contact contact, CancellationToken ct = default);
    Task UpdateAsync(Contact contact, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
