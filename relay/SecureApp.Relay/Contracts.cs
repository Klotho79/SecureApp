namespace SecureApp.Relay;

public sealed record CreateInviteRequest(string? DisplayNameHint, int ValidForMinutes = 60);
public sealed record CreateInviteResponse(string Code, DateTimeOffset ExpiresAtUtc);

public sealed record CreateDeviceRequest(string DisplayName);
public sealed record DeviceCredentialResponse(Guid DeviceId, string Secret);

public sealed record RegisterRequest(string InviteCode, string DisplayName);

// --- Activation requests (2026-09-06) — replaces the invite-code hand-off above for a new device
// joining the community. Instead of an admin minting a code that then has to travel out-of-band
// (SMS/WhatsApp/in person) to the new device, the new device sends the admin a request directly
// (name + email + a short fingerprint of its own identity key) and the admin approves it in-app —
// see IRelayAdminService/IMessageTransport's own remarks for the full flow. The old invite-code
// endpoints above are left in place, unused by the app now, rather than removed — no harm in a
// server-side path nothing calls, and it stays available as a manual curl fallback per
// relay/ops/README.md if this flow is ever unreachable.

public sealed record CreateActivationRequestRequest(string DisplayName, string Email, string KeyFingerprint);
public sealed record CreateActivationRequestResponse(Guid RequestId);

/// <summary>Polled by the still-unregistered device. Once <c>Status</c> is "Approved", <c>DeviceId</c>/<c>Secret</c> are populated — the same credential a successful <c>/register</c> call would have returned, just delivered asynchronously once a human approves rather than synchronously against a pre-shared code.</summary>
public sealed record ActivationStatusResponse(string Status, Guid? DeviceId, string? Secret);

/// <summary>One row in the admin's pending-activations list — deliberately carries no secret, only what the admin needs to decide whether to approve.</summary>
public sealed record ActivationRequestSummary(Guid Id, string DisplayName, string Email, string KeyFingerprint, DateTimeOffset CreatedAtUtc);

// --- Member directory (2026-09-06) — replaces manual contact-card generate/paste for starting a
// chat with someone already on this relay. Only makes sense BECAUSE activation above already had
// an admin approve every device's real identity; see RelayDatabase's own remarks on
// directory_entries for the trust-model note this relies on.

/// <summary>Upserts the CALLING (device-authenticated) device's own entry — carries no device id, since the relay already knows which device is asking from the X-Device-Id/X-Device-Secret headers, same auth as the /library/files endpoints.</summary>
public sealed record PublishDirectoryEntryRequest(string DisplayName, string PublicKeyBase64);

/// <summary>One other community member, resolvable straight into a chat session with no QR/paste step — <c>PublicKeyBase64</c> is the same chat-identity key a manually-shared contact card would have carried.</summary>
public sealed record DirectoryMemberSummary(Guid DeviceId, string DisplayName, string PublicKeyBase64);

// --- Shared diagnostics log (2026-09-10) — see IDiagnosticsReporter's own remarks for why this
// exists: a place any device can report a problem to, and any device (or an operator with SSH into
// the relay) can read from, so debugging a cross-device issue doesn't need physical access to every
// device involved. Device-authenticated (X-Device-Id/X-Device-Secret), same as /directory/*  and
// /library/files — not admin-gated, since any already-activated device is exactly who this should
// be visible to.

public sealed record ReportDiagnosticLogRequest(string Level, string Message, string? Context, string? ExceptionDetails);

/// <summary><c>DeviceDisplayName</c> is resolved server-side against the CURRENT member directory, not stored at report time — see <c>RelayDatabase.GetRecentDiagnosticLogs</c>'s own remarks.</summary>
public sealed record DiagnosticLogEntryDto(Guid Id, string DeviceDisplayName, string Level, string Message, string? Context, string? ExceptionDetails, DateTimeOffset CreatedAtUtc);

// --- Logbook catalog sync (2026-09-10) — see ILogbookCatalogSyncService's own remarks. Same
// device-authenticated (X-Device-Id/X-Device-Secret), not-admin-gated shape as everything above.

public sealed record LogbookChecklistDto(Guid Id, string Name, IReadOnlyList<string> Items, DateTimeOffset CreatedAtUtc);

public sealed record LogbookProcedureTypeDto(Guid Id, string Name, string Abbreviation, string Category, DateTimeOffset CreatedAtUtc);
