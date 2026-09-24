namespace SecureApp.Relay;

public sealed record CreateInviteRequest(string? DisplayNameHint, int ValidForMinutes = 60);
public sealed record CreateInviteResponse(string Code, DateTimeOffset ExpiresAtUtc);

public sealed record CreateDeviceRequest(string DisplayName);
public sealed record DeviceCredentialResponse(Guid DeviceId, string Secret);

/// <summary>Self-update manifest (2026-09-23) — written by /admin/upload/android, read by the app's own update check via /download/android/version. VersionCode is the Android versionCode (ApplicationVersion in the .csproj) — the actual number compared to decide "is there something newer", VersionName is just the human-readable display string.</summary>
public sealed record UpdateManifest(int VersionCode, string VersionName, DateTimeOffset ReleasedAtUtc);

/// <summary>New-member WireGuard onboarding (2026-09-23) — see /admin/wireguard/clients' own remarks.</summary>
public sealed record CreateWireGuardClientRequest(string Name);
public sealed record WireGuardClientResponse(string ConfigurationText);

/// <summary>Shape of wg-easy's own GET /api/wireguard/client response — only the fields this relay actually reads (id, to fetch the configuration afterward; createdAt, to identify the just-created client). Not the full wg-easy client shape.</summary>
public sealed record WgEasyClient(string Id, string Name, DateTimeOffset CreatedAt);

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

// --- Device management (2.1, 2026-09-17) — lets an admin see every registered device's staleness
// and pending-outbox depth, and deregister a dead one, instead of only discovering a ghost identity
// mid-incident (SSH + manual SQLite queries, see RelayDatabase.GetAllDevicesWithStatus's own remarks).

/// <summary>One registered device in the admin's device-management list. <c>DirectoryDisplayName</c>/<c>LastActiveAtUtc</c> are null if the device has never published to the directory (or its entry is old enough that <c>RelayDatabase.DirectoryActiveWindow</c> would already hide it from the member picker) — that's the actual "is this a ghost" signal, not <c>CreatedAtUtc</c>.</summary>
public sealed record RegisteredDeviceSummary(Guid Id, string DisplayName, DateTimeOffset CreatedAtUtc, string? DirectoryDisplayName, DateTimeOffset? LastActiveAtUtc, int PendingOutboxCount);

// --- Member directory (2026-09-06) — replaces manual contact-card generate/paste for starting a
// chat with someone already on this relay. Only makes sense BECAUSE activation above already had
// an admin approve every device's real identity; see RelayDatabase's own remarks on
// directory_entries for the trust-model note this relies on.

/// <summary>Upserts the CALLING (device-authenticated) device's own entry — carries no device id, since the relay already knows which device is asking from the X-Device-Id/X-Device-Secret headers, same auth as the /library/files endpoints.</summary>
public sealed record PublishDirectoryEntryRequest(string DisplayName, string PublicKeyBase64);

/// <summary>One other community member, resolvable straight into a chat session with no QR/paste step — <c>PublicKeyBase64</c> is the same chat-identity key a manually-shared contact card would have carried.</summary>
public sealed record DirectoryMemberSummary(Guid DeviceId, string DisplayName, string PublicKeyBase64);

// --- Shared-library-key escrow (2026-09-11) — see the wrapped_library_keys table's own remarks. A
// device that HAS the key uploads it wrapped (ML-KEM to each recipient's public identity key) via
// the POST; a device that LACKS it fetches its own wrapped blob via the GET and unwraps locally.
// The relay only ever holds opaque ciphertext.

/// <summary>Uploads the shared library key wrapped for one specific recipient device. The caller (any key-holding, device-authenticated member) supplies the recipient's device id and the opaque wrapped blob.</summary>
public sealed record PublishWrappedKeyRequest(Guid RecipientDeviceId, string WrappedBlob);

/// <summary>The wrapped shared library key stored for the CALLING device, returned by GET /library/wrapped-key (404 if none has been escrowed for it yet).</summary>
public sealed record WrappedKeyResponse(string WrappedBlob);

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

// --- Shared company phone/extension directory (2026-09-20) — see RelayDatabase's shared_contacts
// table and SharedContact's own remarks. Same device-authenticated, not-admin-gated shape as the
// Logbook catalog sync above.

public sealed record SharedContactDto(Guid Id, string DisplayName, string? Phone, string? Note, int SortOrder, DateTimeOffset CreatedAtUtc);

public sealed record WorkplaceDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAtUtc);

// --- Admin-assigned device policy + notice board (2026-09-24) — see RelayDatabase's device_policy
// and board_posts tables for the full reasoning. Roles and tab visibility used to be decided purely
// on-device (anyone could make themselves Admin), so an admin had no way to govern anyone else.

/// <summary>What the CALLING device is allowed to be. <c>Role</c> null means no admin has managed this device yet — the client then keeps its own local role rather than being demoted. <c>HiddenTabs</c> holds AppShell preference keys (e.g. <c>tab_chaty_visible</c>) the device must not show.</summary>
public sealed record DevicePolicyResponse(int? Role, IReadOnlyList<string> HiddenTabs);

/// <summary>One member as the admin's management screen sees them. <c>LastSeenUtc</c> is the directory's own last-published timestamp (null = never connected since the directory existed).</summary>
public sealed record ManagedDeviceDto(Guid DeviceId, string DisplayName, int? Role, IReadOnlyList<string> HiddenTabs, DateTimeOffset? LastSeenUtc);

/// <summary>Admin sets one member's role and which tabs they may not see. A null <c>Role</c> clears the assignment and hands the role decision back to the device.</summary>
public sealed record SetDevicePolicyRequest(int? Role, IReadOnlyList<string>? HiddenTabs);

/// <summary><c>ContentBlob</c> is opaque to the relay — the client encrypts the message with the shared community library key before posting, so the board is ciphertext at rest exactly like a library file.</summary>
public sealed record CreateBoardPostRequest(string ContentBlob);

/// <summary>One notice-board post. <c>AuthorDisplayName</c> is resolved against the live directory at read time, so a rename applies retroactively.</summary>
public sealed record BoardPostDto(Guid Id, Guid AuthorDeviceId, string AuthorDisplayName, string ContentBlob, DateTimeOffset CreatedAtUtc);
