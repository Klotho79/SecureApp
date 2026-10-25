namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// A doctor's grade for the position suggestions (POZICE_NAVRH_PLAN.md, user's definitions 2026-10-25).
/// Ordered, so "at least L3" is a plain comparison. Stored as a number — never renumber.
/// </summary>
public enum StaffGrade
{
    /// <summary>Not set yet — the suggestions skip the person until the admin sets one.</summary>
    Unset = 0,

    /// <summary>Newcomer, being trained: never alone on a theatre, only as a second next to a trainer.</summary>
    L1N = 1,

    /// <summary>Without the core anaesthesia exam: theatres alone, detached workplaces only where the workplace allows it (e.g. DIOP).</summary>
    L1 = 2,

    /// <summary>After the core exam — same rules as L1.</summary>
    L2 = 3,

    /// <summary>After attestation: everywhere.</summary>
    L3 = 4,

    /// <summary>Usually a section lead: everywhere, around 50 % on their own section by default.</summary>
    L4 = 5,
}

/// <summary>A target share of one workplace in a doctor's positions, in percent.</summary>
public sealed record StaffWorkplaceShare(int WorkplaceId, int Percent);

/// <summary>
/// One doctor's settings, keyed by the Opicentrum person id ("osoba", the same id the roster and the leave grid
/// carry). <paramref name="PersonName"/> is only a display copy from the last time the list was refreshed.
/// </summary>
public sealed record StaffProfile(
    int PersonId,
    string PersonName,
    StaffGrade Grade,
    int? HomeWorkplaceId,
    IReadOnlyList<string> Specializations,
    IReadOnlyList<StaffWorkplaceShare> TargetShares,
    bool ExcludedFromSuggestions);

/// <summary>
/// One workplace's rules, keyed by Opicentrum's "sal" id. <paramref name="MinGrade"/> expresses the detached
/// workplaces (L3) and their exceptions (e.g. DIOP stays at L1); <paramref name="Headcount"/> null = typical
/// count from history.
/// </summary>
public sealed record WorkplaceProfile(
    int WorkplaceId,
    string WorkplaceName,
    StaffGrade MinGrade,
    bool AllowsTrainee,
    string? RequiredSpecialization,
    int? Headcount,
    bool ExcludedFromSuggestions);

/// <summary>Which workplace a duty slot from sluzby7.php ("ARIM I", …) puts its doctor on that day; null = none.</summary>
public sealed record DutySlotMapping(string SlotLabel, int? WorkplaceId);

/// <summary>
/// The admin-maintained input of the position suggestions (2026-10-25, POZICE_NAVRH_PLAN.md phase 1), shared
/// through the relay's <c>/opicentrum-staffing-config</c> blob — read by Admin/Modifier, written by Admin only.
/// </summary>
public sealed record OpicentrumStaffingConfig(
    DateTimeOffset UpdatedAtUtc,
    IReadOnlyList<StaffProfile> People,
    IReadOnlyList<WorkplaceProfile> Workplaces,
    IReadOnlyList<DutySlotMapping> DutySlots)
{
    public static OpicentrumStaffingConfig Empty { get; } = new(DateTimeOffset.MinValue, [], [], []);
}
