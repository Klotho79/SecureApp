using SecureApp.Presentation.ViewModels;

namespace SecureApp.Presentation.Controls;

/// <summary>
/// Group-chat counterpart of <see cref="ChatMessageTemplateSelector"/> (2026-09-12): text-only group
/// messages get a lean bubble that never builds the attachment sub-tree, cutting the per-cell UI-thread
/// work that stole frames from the open animation. See ChatMessageTemplateSelector for the full rationale.
/// </summary>
public sealed class GroupMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate TextTemplate { get; set; } = null!;
    public DataTemplate AttachmentTemplate { get; set; } = null!;

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
        => item is GroupMessageItem { HasAttachment: true } ? AttachmentTemplate : TextTemplate;
}
