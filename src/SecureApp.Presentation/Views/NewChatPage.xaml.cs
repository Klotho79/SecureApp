using SecureApp.Presentation.Chat;
using SecureApp.Presentation.ViewModels;
using ZXing.Net.Maui;

namespace SecureApp.Presentation.Views;

public partial class NewChatPage : ContentPage
{
    private readonly NewChatViewModel _viewModel;

    public NewChatPage(NewChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // BarcodeReaderOptions' properties are init-only — the compiled-XAML source-gen can only
        // assign settable properties after construction, so this has to happen here instead of as
        // a <CameraBarcodeReaderView.Options> element in the XAML (that failed to build with
        // CS8852 on every target). Must match QrBlobCodec.CharacterSet exactly on both ends.
        var readerOptions = new BarcodeReaderOptions
        {
            CharacterSet = QrBlobCodec.CharacterSet,
            Formats = BarcodeFormat.QrCode,
            TryHarder = true
        };
        PeerCardScanner.Options = readerOptions;
        InviteScanner.Options = readerOptions;
    }

    // BarcodesDetected fires off the camera-processing thread (same reasoning as
    // ChatViewModel's EnvelopeReceived — see StartListening's own remarks), so every UI/ViewModel
    // touch here is marshalled back via MainThread. Only the first detected code is used; a QR
    // pairing blob is never expected to appear alongside another barcode in frame.
    private void OnPeerCardBarcodeDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrEmpty(value))
            return;

        MainThread.BeginInvokeOnMainThread(() => _viewModel.OnPeerCardScanned(value));
    }

    private void OnInviteBarcodeDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        var value = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrEmpty(value))
            return;

        MainThread.BeginInvokeOnMainThread(() => _viewModel.OnInviteScanned(value));
    }
}
