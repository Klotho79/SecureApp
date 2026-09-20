using SecureApp.Domain.Enums;

namespace SecureApp.Presentation.Workplace;

/// <summary>Czech label + glyph per <see cref="AssignmentType"/> — the only place this mapping lives, so every ViewModel/Picker in the Workplace feature shares it.</summary>
internal static class AssignmentTypeCatalog
{
    public static readonly IReadOnlyList<AssignmentType> All =
    [
        AssignmentType.Work,
        AssignmentType.OnCall,
        AssignmentType.BusinessTrip,
        AssignmentType.Training,
        AssignmentType.Vacation,
        AssignmentType.DayOff,
        AssignmentType.SickLeave,
        AssignmentType.Other
    ];

    public static string Label(AssignmentType type) => type switch
    {
        AssignmentType.Work => "Práce",
        AssignmentType.Vacation => "Dovolená",
        AssignmentType.SickLeave => "Nemocenská",
        AssignmentType.BusinessTrip => "Služební cesta",
        AssignmentType.Training => "Školení",
        AssignmentType.DayOff => "Volno",
        AssignmentType.OnCall => "Pohotovost",
        AssignmentType.Other => "Jiné",
        _ => type.ToString()
    };

    public static string Glyph(AssignmentType type) => type switch
    {
        AssignmentType.Work => "💼",
        AssignmentType.Vacation => "🏖",
        AssignmentType.SickLeave => "🤒",
        AssignmentType.BusinessTrip => "✈",
        AssignmentType.Training => "🎓",
        AssignmentType.DayOff => "🛌",
        AssignmentType.OnCall => "📞",
        AssignmentType.Other => "◾",
        _ => "◾"
    };
}
