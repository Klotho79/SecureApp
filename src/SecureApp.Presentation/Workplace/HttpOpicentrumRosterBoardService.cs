using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IOpicentrumRosterBoardService"/>
public sealed class HttpOpicentrumRosterBoardService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : HttpBoardCacheServiceBase<OpicentrumRosterBoardSnapshot>(vault, transportSettingsRepository, "opicentrum-roster-board"), IOpicentrumRosterBoardService;
