namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// A new member's request for access, waiting for an admin (2026-11-18, user: "uživatel vytvoří účet a já ho schválím,
/// vše ostatní udělá appka") — the relay's pending activation request.
/// </summary>
public sealed record AccessRequest(Guid Id, string DisplayName, string Email, DateTimeOffset CreatedAtUtc);
