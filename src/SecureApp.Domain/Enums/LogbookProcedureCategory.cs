namespace SecureApp.Domain.Enums;

/// <summary>
/// The three groupings the reference logbook (KNTB Zlín ARIM, "Příručka a logbook začínajícího
/// anesteziologa") organizes its competency tables by — "Kompetence dle pracovišť" / "dle výkonů" /
/// "dle situací". A <see cref="Entities.LogbookProcedureType"/> belongs to exactly one, purely for
/// grouping the statistics view; nothing behavioral depends on which.
/// </summary>
public enum LogbookProcedureCategory
{
    /// <summary>Pracoviště — e.g. Traumatologie, Ortopedie, ORL.</summary>
    Workplace,

    /// <summary>Výkon — e.g. OTI, CŽK, spinální anestezie.</summary>
    Procedure,

    /// <summary>Situace — e.g. laryngospasmus, anafylaxe, KPR.</summary>
    Situation
}
