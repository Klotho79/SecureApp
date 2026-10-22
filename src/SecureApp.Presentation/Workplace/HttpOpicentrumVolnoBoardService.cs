using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IOpicentrumVolnoBoardService"/>
public sealed class HttpOpicentrumVolnoBoardService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : HttpBoardCacheServiceBase<OpicentrumVolnoSnapshot>(vault, transportSettingsRepository, "opicentrum-volno-board"), IOpicentrumVolnoBoardService;
