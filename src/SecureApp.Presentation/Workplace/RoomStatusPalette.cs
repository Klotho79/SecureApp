namespace SecureApp.Presentation.Workplace;

/// <summary>
/// Semantic color bucket for a room-status VALUE (2026-10-15, design pass — "chci lepsi, pouzitelnejsi
/// navrh" — status pills replaced by a color-coded grid of tiles, matching the app's own already-chosen
/// visual direction: the same Success/Info/Warn/Danger tokens Colors.xaml ships are reused here, nothing
/// new). The curated status list (<see cref="Domain.Interfaces.Services.IRoomStatusService.GetStatusOptionsAsync"/>)
/// is free-text, admin-edited — there is no fixed enum of values — so this is a best-effort keyword
/// match on the Czech words an admin is likely to use; anything unrecognized just renders neutral
/// (still fully usable, just without the extra at-a-glance color).
/// </summary>
public enum RoomStatusCategory
{
    Neutral,
    Success,
    Info,
    Warn,
    Danger
}

public static class RoomStatusPalette
{
    public static RoomStatusCategory CategoryFor(string statusText)
    {
        var t = statusText.ToLowerInvariant();

        if (t.Contains("volno") || t.Contains("připraven") || t.Contains("pripraven") || t.Contains("volný") || t.Contains("volny"))
            return RoomStatusCategory.Success;

        if (t.Contains("probíhá") || t.Contains("probiha") || t.Contains("výkon") || t.Contains("vykon") || t.Contains("zahájen") || t.Contains("zahajen"))
            return RoomStatusCategory.Info;

        if (t.Contains("uklízí") || t.Contains("uklizi") || t.Contains("úklid") || t.Contains("uklid") || t.Contains("čeká") || t.Contains("ceka"))
            return RoomStatusCategory.Warn;

        if (t.Contains("uzavřeno") || t.Contains("uzavreno") || t.Contains("uzavřen") || t.Contains("mimo provoz") || t.Contains("porucha") || t.Contains("havári") || t.Contains("havari"))
            return RoomStatusCategory.Danger;

        return RoomStatusCategory.Neutral;
    }
}
