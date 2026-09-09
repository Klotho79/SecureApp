namespace SecureApp.Domain.Enums;

/// <summary>
/// The reference logbook's own three-tier competency scale — each of its "Kompetence dle..." tables
/// has one column per tier ("viděl" / "pod dohledem" / "samostatně", or "probráno" for situations —
/// the same three-step progression under a different first label). A paper logbook records this as
/// a single checkbox per tier per row, signed by a supervisor; here it's a per-entry tag on every
/// <see cref="Entities.LogbookProcedureEntry"/> instead, so the same procedure logged multiple times
/// at different tiers over training accumulates into real statistics rather than overwriting one box.
/// </summary>
public enum LogbookCompetenceLevel
{
    /// <summary>Viděl / probráno — observed or discussed, not yet performed.</summary>
    Seen,

    /// <summary>Pod dohledem — performed under supervision.</summary>
    Supervised,

    /// <summary>Samostatně — performed independently.</summary>
    Independent
}
