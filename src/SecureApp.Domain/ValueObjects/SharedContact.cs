namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One entry in the company-wide, shared phone/extension directory (2026-09-20, user's own
/// correction of an earlier per-device design: "kontakty jsou společné pro všechny... je to
/// pracovní záležitost... kontakty jsou veřejně dostupné i mimo apku" — contacts are shared by
/// everyone, a work matter, and public information even outside the app). Deliberately no chat
/// linking — "u kontaktů určitě neotevírat chaty, to jsou podnikové kontakty telefonní, klapky"
/// (definitely don't open chats from contacts, these are corporate phone contacts — extensions).
/// Synced via <see cref="Interfaces.Services.ISharedContactService"/>, same relay-backed,
/// device-authenticated, unencrypted pattern <c>ILogbookCatalogSyncService</c> already established
/// for the Logbook's own shared reference catalogs — plain reference data, not a clinical/personal
/// document, so it doesn't need the shared library's own encryption key.
/// </summary>
public sealed record SharedContact(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc);
