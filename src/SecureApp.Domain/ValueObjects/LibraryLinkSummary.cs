namespace SecureApp.Domain.ValueObjects;

/// <summary>One external link (e.g. PubMed, ČSARIM) filed under a <see cref="LibrarySubcategorySummary"/> (2026-10-02). No ciphertext — the relay stores title/URL as plain text, same as the shared company directory.</summary>
public sealed record LibraryLinkSummary(
    Guid Id,
    Guid SubcategoryId,
    string Title,
    string Url,
    string CreatedByDeviceId,
    DateTimeOffset CreatedAtUtc);
