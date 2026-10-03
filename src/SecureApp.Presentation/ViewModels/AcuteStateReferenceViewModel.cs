using CommunityToolkit.Mvvm.ComponentModel;
using SecureApp.Presentation.Library;

namespace SecureApp.Presentation.ViewModels;

/// <summary>
/// Shows one entry of <see cref="AcuteStateReferenceData"/> — the built-in fallback content for the
/// "Akutní stavy" row (see that class's own remarks on why/when this is used instead of a real
/// uploaded document). Plain display, no commands beyond navigation — content is immutable static
/// data, nothing to load or save.
/// </summary>
public sealed partial class AcuteStateReferenceViewModel : ObservableObject, IQueryAttributable
{
    [ObservableProperty]
    public partial string Term { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("term", out var termValue)) return;
        Term = Uri.UnescapeDataString(termValue?.ToString() ?? string.Empty);
        Content = AcuteStateReferenceData.Recommendations.TryGetValue(Term, out var content)
            ? content
            : string.Empty;
    }
}
