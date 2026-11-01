using MailClient.App.Views.Compose;
using MailClient.App.Views.Converters;
using MailClient.ViewModels.Compose;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;

namespace MailClient.App.Views.Shell;

public sealed partial class ReadingPaneView : UserControl
{
    public ReadingPaneViewModel ViewModel { get; }

    public ReadingPaneView()
    {
        ViewModel = App.Services.GetRequiredService<ReadingPaneViewModel>();
        InitializeComponent();

        // async void by nature: anything thrown here would surface on the UI thread's unhandled
        // exception path, so each branch contains its own failures.
        ViewModel.PropertyChanged += async (_, e) =>
        {
            try
            {
                if (e.PropertyName == nameof(ReadingPaneViewModel.HtmlBody) && ViewModel.HtmlBody is not null)
                    await NavigateAsync(ViewModel.HtmlBody);
                if (e.PropertyName == nameof(ReadingPaneViewModel.SelectedMessage))
                    await UpdateSenderLogoAsync();
            }
            catch (Exception ex)
            {
                LogWebViewEvent($"PropertyChanged handler failed ({e.PropertyName}): {ex}");
            }
        };
    }

    private async Task UpdateSenderLogoAsync()
    {
        var message = ViewModel.SelectedMessage;
        if (message is null)
        {
            ApplySenderLogo(null);
            return;
        }

        var path = await ViewModel.GetSenderLogoPathAsync(message.FromAddress);

        // The user may have moved on to another message while the logo was being fetched.
        if (!ReferenceEquals(ViewModel.SelectedMessage, message))
            return;

        ApplySenderLogo(SenderLogoRender.For(path, SenderLogoRender.ReadingPaneAvatarDip));
    }

    private void ApplySenderLogo(SenderLogoRender.Info? info)
    {
        // info is null for "no logo" and for "logo too low-resolution to look good" (see
        // SenderLogoRender); both keep the colored-initial avatar.
        SenderLogoBrush.ImageSource = info?.Source;
        SenderLogoBacking.Background = info is null ? null : new SolidColorBrush(Colors.White);
    }

    // ---- Message body (WebView2) ------------------------------------------------------------
    //
    // The WebView2 is created lazily, in code, and can be replaced: a long-running, often hidden
    // (tray) app outlives WebView2 runtime updates, GPU resets and renderer crashes, any of which
    // used to leave the body pane permanently blank until the app was restarted -- the control
    // never recovered on its own and nothing here noticed. Now a ProcessFailed handler recovers,
    // navigation failures rebuild the control, and the window re-checks the page when it's
    // brought back to the foreground (EnsureBodyRenderedAsync).

    private Task<WebView2>? _webViewReady;
    private string? _currentHtml;
    private int _recoveryCount;
    private DateTimeOffset _recoveryWindowStart;

    private Task<WebView2> GetWebViewAsync() => _webViewReady ??= CreateWebViewAsync();

    private async Task<WebView2> CreateWebViewAsync()
    {
        var webView = new WebView2();
        BodyWebViewHost.Children.Add(webView);
        try
        {
            await webView.EnsureCoreWebView2Async();
        }
        catch
        {
            BodyWebViewHost.Children.Remove(webView);
            _webViewReady = null;
            throw;
        }

        webView.CoreWebView2.ProcessFailed += OnWebViewProcessFailed;
        return webView;
    }

    private async Task DiscardWebViewAsync()
    {
        var pending = _webViewReady;
        _webViewReady = null;
        BodyWebViewHost.Children.Clear();

        if (pending is null)
            return;

        try
        {
            (await pending).Close();
        }
        catch
        {
            // Already dead or never finished starting -- nothing left to release.
        }
    }

    private async Task NavigateAsync(string html)
    {
        _currentHtml = html;

        // Second attempt runs against a freshly built WebView2.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var webView = await GetWebViewAsync();
                webView.CoreWebView2.NavigateToString(html);
                return;
            }
            catch (Exception ex)
            {
                LogWebViewEvent($"NavigateToString failed (attempt {attempt + 1}, {html.Length} chars): {ex}");
                await DiscardWebViewAsync();
            }
        }
    }

    private void OnWebViewProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
    {
        LogWebViewEvent($"ProcessFailed: kind={args.ProcessFailedKind} reason={args.Reason} exitCode={args.ExitCode}");

        switch (args.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                // The whole control is dead (typically a runtime update replaced the binaries).
                _ = RecoverAsync(rebuild: true);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
            case CoreWebView2ProcessFailedKind.FrameRenderProcessExited:
                _ = RecoverAsync(rebuild: false);
                break;
            default:
                // GPU/utility/helper processes are restarted by the runtime itself.
                break;
        }
    }

    private async Task RecoverAsync(bool rebuild)
    {
        // A page that crashes its renderer every time must not turn into a reload loop.
        var now = DateTimeOffset.UtcNow;
        if (now - _recoveryWindowStart > TimeSpan.FromSeconds(30))
        {
            _recoveryWindowStart = now;
            _recoveryCount = 0;
        }

        if (++_recoveryCount > 3)
            return;

        try
        {
            if (rebuild)
                await DiscardWebViewAsync();

            if (_currentHtml is not null && ViewModel.IsHtml)
                await NavigateAsync(_currentHtml);
        }
        catch (Exception ex)
        {
            LogWebViewEvent($"Recovery failed: {ex}");
        }
    }

    // Called when the main window is activated (including when it's restored from the tray after
    // sitting hidden for a long time). Cheap when healthy: a single script round trip.
    public async Task EnsureBodyRenderedAsync()
    {
        if (_webViewReady is null || _currentHtml is null || !ViewModel.IsHtml)
            return;

        try
        {
            var webView = await GetWebViewAsync();
            var probe = webView.CoreWebView2.ExecuteScriptAsync(
                "document.documentElement ? document.documentElement.outerHTML.length : 0").AsTask();

            if (await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(3))) != probe)
            {
                LogWebViewEvent("Health check timed out; rebuilding the WebView2");
                await DiscardWebViewAsync();
                await NavigateAsync(_currentHtml);
                return;
            }

            // An empty/default document is ~39 chars ("<html><head></head><body></body></html>").
            if (!int.TryParse(await probe, out var length) || length < 60)
            {
                LogWebViewEvent($"Health check found a blank page (length={length}); re-rendering");
                await NavigateAsync(_currentHtml);
            }
        }
        catch (Exception ex)
        {
            LogWebViewEvent($"Health check failed: {ex}");
            await DiscardWebViewAsync();
            await NavigateAsync(_currentHtml);
        }
    }

    // Diagnostic trail for "the body went blank after a while" reports: the underlying cause
    // (runtime update vs. renderer crash vs. something else) isn't knowable from here, so record it.
    private static void LogWebViewEvent(string message)
    {
        try
        {
            var path = Path.Combine(App.Services.GetRequiredService<MailClient.Core.AppDataPaths>().RootDirectory, "webview2.log");
            if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                File.Delete(path);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch
        {
            // Diagnostics only.
        }
    }

    private Visibility IsPlainTextVisible(bool isHtml) => isHtml ? Visibility.Collapsed : Visibility.Visible;

    private string SenderInitial(string? fromDisplay) => SenderAvatar.Initial(fromDisplay);

    private Brush SenderAvatarColor(string? fromDisplay) => SenderAvatar.ColorFor(fromDisplay);

    private void OnReplyClick(object sender, RoutedEventArgs e) => OpenCompose(ComposeMode.Reply);

    private void OnReplyAllClick(object sender, RoutedEventArgs e) => OpenCompose(ComposeMode.ReplyAll);

    private void OnForwardClick(object sender, RoutedEventArgs e) => OpenCompose(ComposeMode.Forward);

    private void OpenCompose(ComposeMode mode)
    {
        var sourceMessage = ViewModel.SelectedMessage;
        if (sourceMessage is null)
            return;

        var quotedBody = ViewModel.IsHtml ? null : ViewModel.PlainTextBody;

        var composeViewModel = App.Services.GetRequiredService<ComposeViewModel>();
        composeViewModel.Prepare(mode, sourceMessage, quotedBody);

        var composeWindow = new ComposeWindow(composeViewModel);
        composeWindow.Activate();
    }
}
