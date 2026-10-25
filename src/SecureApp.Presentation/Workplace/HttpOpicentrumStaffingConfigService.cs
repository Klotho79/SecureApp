using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <inheritdoc cref="IOpicentrumStaffingConfigService"/>
public sealed class HttpOpicentrumStaffingConfigService(ISecureVaultKeyStore vault, ITransportSettingsRepository transportSettingsRepository)
    : HttpBoardCacheServiceBase<OpicentrumStaffingConfig>(vault, transportSettingsRepository, "opicentrum-staffing-config"), IOpicentrumStaffingConfigService;
