using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Controls;

/// <summary>
/// Picks a lean text-only bubble template for the common case, and the fuller attachment template only
/// when a message actually carries a file (2026-09-12). Diagnosed root cause of the chat-open jank: in
/// MAUI an <c>IsVisible="false"</c> subtree is still fully instantiated, measured and bound, so the old
/// single template built the whole attachment sub-tree (~7 extra views) into EVERY bubble — most of
/// which are plain text. That per-cell waste, multiplied across the initial page and realized on the
/// single UI thread during the Shell slide, is what stole animation frames. Splitting the template so
/// text messages never build the attachment views cuts that UI-thread burst directly. See the
/// diagnose-before-fixing / visual-smoothness memories for why this was done this way.
/// </summary>
public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate TextTemplate { get; set; } = null!;
    public DataTemplate AttachmentTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item is ChatMessageItem { HasAttachment: true } ? AttachmentTemplate : TextTemplate;
}
