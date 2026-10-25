using SecureApp.Domain.ValueObjects;

namespace SecureApp.Presentation.Workplace;

/// <summary>One workplace in one doctor's statistics: share of their worked days, against the target if one is set.</summary>
public sealed record WorkplaceShareStat(int WorkplaceId, string WorkplaceName, double Days, double ActualPercent, int? TargetPercent);

/// <summary>
/// One doctor over the statistics window. <paramref name="DeviationPoints"/> is how far they are from fair, in
/// percentage points: against their own targets when set (the largest gap), otherwise against the average of their
/// grade (total variation distance — half the sum of the gaps over all workplaces).
/// </summary>
public sealed record PersonStat(
    int PersonId,
    string PersonName,
    StaffGrade Grade,
    double WorkedDays,
    int DutyCount,
    int UnassignedDays,
    IReadOnlyList<WorkplaceShareStat> Shares,
    double DeviationPoints,
    bool HasTargets);

/// <summary>
/// Who stood where how often (2026-10-25, POZICE_NAVRH_PLAN.md phase 2) — computed from
/// <see cref="OpicentrumHistoryData"/> and the admin's <see cref="OpicentrumStaffingConfig"/>. A day on two workplaces
/// counts half to each, so a doctor's shares always add up to 100 %. NEZAŘAZENÍ only counts as an unassigned day when
/// the doctor has no real workplace that day (the portal sometimes leaves a moved person in both).
/// </summary>
public static class OpicentrumStaffingStatistics
{
    private const int UnassignedWorkplaceId = 999;

    /// <summary>Doctors with fewer worked days than this do not shape their grade's average.</summary>
    private const double MinDaysForGroupAverage = 10;

    public static IReadOnlyList<PersonStat> Compute(OpicentrumHistoryData history, OpicentrumStaffingConfig config, DateOnly from, DateOnly to)
    {
        var workplaceNames = new Dictionary<int, string>();
        foreach (var w in config.Workplaces) workplaceNames[w.WorkplaceId] = w.WorkplaceName;
        var personNames = new Dictionary<int, string>();
        foreach (var p in config.People) personNames[p.PersonId] = p.PersonName;

        // (person, date) → workplaces that day
        var placements = new Dictionary<(int PersonId, DateOnly Date), HashSet<int>>();
        foreach (var week in history.Weeks.Values)
        {
            foreach (var day in week.Days)
            {
                if (day.Date < from || day.Date > to) continue;
                foreach (var workplace in day.Workplaces)
                {
                    if (workplace.WorkplaceId <= 0) continue;
                    if (workplace.WorkplaceId != UnassignedWorkplaceId) workplaceNames.TryAdd(workplace.WorkplaceId, workplace.WorkplaceName.Trim());
                    foreach (var person in workplace.People)
                    {
                        if (person.PersonId <= 0) continue;
                        personNames.TryAdd(person.PersonId, person.PersonName.Trim());
                        if (!placements.TryGetValue((person.PersonId, day.Date), out var set))
                            placements[(person.PersonId, day.Date)] = set = [];
                        set.Add(workplace.WorkplaceId);
                    }
                }
            }
        }
        foreach (var volno in history.VolnoMonths.Values)
            foreach (var person in volno.People)
                personNames.TryAdd(person.PersonId, person.PersonName.Trim());

        var days = new Dictionary<int, Dictionary<int, double>>();
        var worked = new Dictionary<int, double>();
        var unassigned = new Dictionary<int, int>();
        foreach (var ((personId, _), set) in placements)
        {
            var real = set.Where(id => id != UnassignedWorkplaceId).ToList();
            if (real.Count == 0)
            {
                unassigned[personId] = unassigned.GetValueOrDefault(personId) + 1;
                continue;
            }
            worked[personId] = worked.GetValueOrDefault(personId) + 1;
            if (!days.TryGetValue(personId, out var perWorkplace)) days[personId] = perWorkplace = [];
            foreach (var id in real)
                perWorkplace[id] = perWorkplace.GetValueOrDefault(id) + 1.0 / real.Count;
        }

        var duties = CountDuties(history, personNames, from, to);
        var profiles = config.People.ToDictionary(p => p.PersonId);

        var ids = personNames.Keys.Where(id => worked.ContainsKey(id) || duties.ContainsKey(id) || profiles.ContainsKey(id));
        var shares = ids.ToDictionary(id => id, id => Percentages(days.GetValueOrDefault(id), worked.GetValueOrDefault(id)));
        var gradeOf = ids.ToDictionary(id => id, id => profiles.TryGetValue(id, out var p) ? p.Grade : StaffGrade.Unset);

        // Average share per workplace within each grade, from the doctors with enough days to say something.
        var groupAverage = gradeOf.Values.Distinct().ToDictionary(grade => grade, grade =>
        {
            var members = gradeOf.Where(g => g.Value == grade && worked.GetValueOrDefault(g.Key) >= MinDaysForGroupAverage).Select(g => shares[g.Key]).ToList();
            return members.Count == 0
                ? new Dictionary<int, double>()
                : members.SelectMany(s => s.Keys).Distinct().ToDictionary(w => w, w => members.Average(s => s.GetValueOrDefault(w)));
        });

        var result = new List<PersonStat>();
        foreach (var id in shares.Keys)
        {
            var actual = shares[id];
            var targets = profiles.TryGetValue(id, out var profile) ? profile.TargetShares.ToDictionary(t => t.WorkplaceId, t => t.Percent) : [];
            var hasTargets = targets.Count > 0;

            double deviation;
            if (worked.GetValueOrDefault(id) == 0) deviation = 0;
            else if (hasTargets) deviation = targets.Max(t => Math.Abs(actual.GetValueOrDefault(t.Key) - t.Value));
            else
            {
                var average = groupAverage[gradeOf[id]];
                deviation = average.Count == 0 ? 0 : 0.5 * actual.Keys.Union(average.Keys).Sum(w => Math.Abs(actual.GetValueOrDefault(w) - average.GetValueOrDefault(w)));
            }

            var workplaceStats = actual.Keys.Union(targets.Keys)
                .Select(w => new WorkplaceShareStat(
                    w,
                    workplaceNames.GetValueOrDefault(w, $"#{w}"),
                    days.GetValueOrDefault(id)?.GetValueOrDefault(w) ?? 0,
                    actual.GetValueOrDefault(w),
                    targets.TryGetValue(w, out var t) ? t : null))
                .OrderByDescending(s => s.ActualPercent).ThenByDescending(s => s.TargetPercent ?? 0)
                .ToList();

            result.Add(new PersonStat(id, personNames[id], gradeOf[id], worked.GetValueOrDefault(id), duties.GetValueOrDefault(id), unassigned.GetValueOrDefault(id), workplaceStats, deviation, hasTargets));
        }
        return result;
    }

    private static Dictionary<int, double> Percentages(Dictionary<int, double>? perWorkplace, double workedDays) =>
        perWorkplace is null || workedDays == 0 ? [] : perWorkplace.ToDictionary(kv => kv.Key, kv => kv.Value / workedDays * 100);

    /// <summary>sluzby7.php names people instead of ids — matched to the known names the same way the sync matches the user's own.</summary>
    private static Dictionary<int, int> CountDuties(OpicentrumHistoryData history, Dictionary<int, string> personNames, DateOnly from, DateOnly to)
    {
        var byNormalizedName = new Dictionary<string, int>();
        foreach (var (id, name) in personNames)
            byNormalizedName.TryAdd(OpicentrumParsing.NormalizeName(name), id);

        var resolved = new Dictionary<string, int?>();
        int? Resolve(string name)
        {
            if (resolved.TryGetValue(name, out var cached)) return cached;
            int? id = byNormalizedName.TryGetValue(OpicentrumParsing.NormalizeName(name), out var exact)
                ? exact
                : personNames.Where(p => OpicentrumParsing.NamesMatch(p.Value, name)).Select(p => (int?)p.Key).FirstOrDefault();
            resolved[name] = id;
            return id;
        }

        var counts = new Dictionary<int, int>();
        foreach (var entry in history.DutyMonths.Values.SelectMany(m => m.Entries))
        {
            if (entry.Date < from || entry.Date > to) continue;
            if (Resolve(entry.PersonName) is { } id)
                counts[id] = counts.GetValueOrDefault(id) + 1;
        }
        return counts;
    }
}
