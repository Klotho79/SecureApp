using SecureApp.Presentation.Controls;

namespace SecureApp.Presentation.Views;

/// <summary>Full-screen pinch-zoom viewer for a CMS inline image, reached from <c>CmsDocumentView.ImageTapped</c>.</summary>
public partial class CmsImageViewerPage : ContentPage, IQueryAttributable
{
    public CmsImageViewerPage()
    {
        InitializeComponent();
        _ = new PinchZoomController(FullImage);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("path", out var value) && value?.ToString() is { Length: > 0 } path)
            FullImage.Source = ImageSource.FromFile(Uri.UnescapeDataString(path));
    }

    private async void OnCloseClicked(object? sender, EventArgs e) => await Infrastructure.ShellNavigation.GoToAsync("..");
}
