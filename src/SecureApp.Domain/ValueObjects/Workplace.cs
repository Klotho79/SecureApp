namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One entry in the shared, company-wide list of workplaces (e.g. "Operační sál 7", "Ambulance") —
/// same relay-backed, device-authenticated, unencrypted reference-catalog pattern as
/// <see cref="SharedContact"/>/<c>ILogbookCatalogSyncService</c>: plain reference data everyone in
/// the community should see the same names for when picking a <see cref="Entities.WorkAssignment"/>'s
/// workplace, not a personal/clinical record. Synced via <see cref="Interfaces.Services.IWorkplaceCatalogService"/>.
/// </summary>
public sealed record Workplace(Guid Id, string Name, string? Description, DateTimeOffset CreatedAtUtc);
