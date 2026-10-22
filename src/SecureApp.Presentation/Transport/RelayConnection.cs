using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;

namespace SecureApp.Presentation.Transport;

/// <summary>Shared "make sure the relay WebSocket is up before a screen needs it" step — the connection supervisor (App.xaml.cs) is the primary owner of the connection, this just covers a screen opened in the gap between supervisor ticks.</summary>
internal static class RelayConnection
{
    /// <summary>Connects using the stored configuration if not already connected. Best-effort by design: callers tolerate staying disconnected (messages stay Pending, lists show their own error), so nothing is thrown.</summary>
    public static async Task EnsureConnectedAsync(IMessageTransport transport, ITransportSettingsRepository transportSettingsRepository)
    {
        if (transport.IsConnected) return;

        try
        {
            var configuration = await transportSettingsRepository.GetAsync();
            if (configuration is { AssignedDeviceId: not null, IsAutoConnectEnabled: true, EndpointUri: { } endpoint })
                await transport.ConnectAsync(endpoint);
        }
        catch
        {
            // Best-effort — see summary.
        }
    }
}
