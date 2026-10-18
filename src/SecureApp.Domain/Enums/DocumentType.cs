namespace SecureApp.Domain.Enums;

public enum DocumentType
{
    Unknown = 0,
    Pdf,
    Image,
    Spreadsheet,
    PlainText,
    Other,
    Video,
    /// <summary>CMS document (CMS_PLAN.md): JSON block tree, extension .cmsdoc, rendered natively — not rasterized.</summary>
    Cms
}
