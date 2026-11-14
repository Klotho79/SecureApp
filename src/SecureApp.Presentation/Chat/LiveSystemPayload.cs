using System.Collections.ObjectModel;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.Interfaces.Services;
using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Chat;

/// <summary>What a system payload that arrived while a chat thread is open asks that thread to do.</summary>
internal enum LiveSystemPayloadKind
{
    /// <summary>Something the open thread does not show (a key offer/request, ...).</summary>
    Other,
    /// <summary>A delete-for-everyone command; the local copy is already removed from the database.</summary>
    Delete,
    /// <summary>The peer's app confirmed receipt of one of our messages.</summary>
    DeliveryAck,
}

/// <summary>
/// The part of handling a live system payload that the 1:1 and the group thread share (2026-11-14 — it used to be copied
/// in both view models): store and decrypt it, recognise a delete command or a delivery ack, and apply a delete to the local
/// database. Each thread then updates its own visible list, since a group marks an ack per member and shows the aggregate.
/// Everything here is idempotent; the app-wide handler does the same database work.
/// </summary>
internal static class LiveSystemPayload
{
    public static async Task<(LiveSystemPayloadKind Kind, Guid CorrelationId, Message Message)> ReceiveAsync(
        IMessagingService messagingService, IMessageRepository messageRepository, MessageEnvelope envelope)
    {
        var message = await messagingService.ReceiveMessageAsync(envelope); // idempotent — safe alongside the app-wide handler
        var plaintext = await messagingService.DecryptMessageAsync(message.Id);

        if (MessageDeletionSync.TryParseDeleteCommand(plaintext, out var correlationId))
        {
            await messageRepository.DeleteByCorrelationAsync(correlationId);
            return (LiveSystemPayloadKind.Delete, correlationId, message);
        }

        return DeliveryAckSync.TryParseAck(plaintext, out var ackCorrelationId)
            ? (LiveSystemPayloadKind.DeliveryAck, ackCorrelationId, message)
            : (LiveSystemPayloadKind.Other, Guid.Empty, message);
    }

    /// <summary>Removes every visible item that belongs to <paramref name="correlationId"/>. True when something was removed.</summary>
    public static bool RemoveByCorrelation<T>(ObservableCollection<T> items, Func<T, Guid?> correlationOf, Guid correlationId)
    {
        var doomed = items.Where(item => correlationOf(item) == correlationId).ToList();
        foreach (var item in doomed)
            items.Remove(item);
        return doomed.Count > 0;
    }
}
