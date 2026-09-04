using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Data.Identity;

/// <inheritdoc cref="ICurrentUserService"/>
public sealed class CurrentUserService : ICurrentUserService
{
    private const string DefaultDisplayName = "Local User";

    private readonly IUserRepository _userRepository;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private User _current;
    private bool _initialized;

    public event EventHandler? CurrentUserChanged;

    public User Current => _current;

    public CurrentUserService(IUserRepository userRepository)
    {
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        // Sensible in-memory default so Current is never null before InitializeAsync
        // completes — Admin so existing single-user functionality isn't suddenly locked
        // down for anyone upgrading from before RBAC existed.
        _current = new User(DefaultDisplayName, Role.Admin);
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var stored = await _userRepository.GetCurrentUserAsync(ct);
            if (stored is null)
                await _userRepository.SaveCurrentUserAsync(_current, ct);
            else
                _current = stored;

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SetCurrentUserAsync(string displayName, Role role, CancellationToken ct = default)
    {
        _current.Rename(displayName);
        _current.ChangeRole(role);
        await _userRepository.SaveCurrentUserAsync(_current, ct);
        CurrentUserChanged?.Invoke(this, EventArgs.Empty);
    }
}
