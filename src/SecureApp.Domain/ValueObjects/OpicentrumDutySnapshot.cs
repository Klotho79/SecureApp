namespace SecureApp.Domain.ValueObjects;

/// <summary>One duty slot ("ARIM I", …) on one day and the name of whoever serves it, as sluzby7.php lists it.</summary>
public sealed record OpicentrumDutyEntry(DateOnly Date, string SlotLabel, string PersonName);

/// <summary>A month of everyone's duties (2026-10-25, the position-suggestion history — POZICE_NAVRH_PLAN.md).</summary>
public sealed record OpicentrumDutySnapshot(int Year, int Month, DateTimeOffset UpdatedAtUtc, IReadOnlyList<OpicentrumDutyEntry> Entries);
