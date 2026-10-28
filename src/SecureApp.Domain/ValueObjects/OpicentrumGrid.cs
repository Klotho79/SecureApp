namespace SecureApp.Domain.ValueObjects;

/// <summary>
/// One day cell of a pracoviste.php roster (2026-10-28, the unstaffed-position alerts): who is listed, or
/// <see cref="Closed"/> when the page shows "--" (a holiday / a day the workplace does not run). Unlike the
/// per-person entries the sync works with, an EMPTY cell is kept — an unstaffed position is exactly what the
/// alerts look for.
/// </summary>
public sealed record OpicentrumGridCell(IReadOnlyList<string> Names, bool Closed);

/// <summary>One workplace row of the roster grid, keyed by day. Only the working days the page has a column for are present (no weekend).</summary>
public sealed record OpicentrumGridRow(string Label, IReadOnlyDictionary<DateOnly, OpicentrumGridCell> Cells);

/// <summary>The whole roster grid of the week starting <paramref name="Monday"/>, from the doctors' (ARO) or the nurses' (AROS) portal.</summary>
public sealed record OpicentrumGridWeek(DateOnly Monday, IReadOnlyList<OpicentrumGridRow> Rows);
