using MailClient.Core.Abstractions;
using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MailClient.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    // Tracked so App can decide whether a new-mail event should become a toast (window in the
    // background) or is already visible to the user (window focused).
    public bool IsWindowActive { get; private set; } = true;

    public MainWindow()
    {
        ViewModel = App.Services.GetRequiredService<MainViewModel>();

        InitializeComponent();

        SystemBackdrop = new MicaBackdrop();

        // Packaged MSIX tile assets don't reliably drive a running window's own taskbar/title
        // bar icon (that comes from the window's own icon resource), so set it explicitly.
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        Activated += (_, e) => IsWindowActive = e.WindowActivationState != WindowActivationState.Deactivated;

        Sidebar.ViewModel.FolderSelected += async (_, folder) => await MessageList.ViewModel.LoadAsync(folder);
        MessageList.ViewModel.MessageSelected += async (_, message) => await ReadingPane.ViewModel.LoadAsync(message);
        ReadingPane.ViewModel.MessageStateChanged += async (_, _) => await MessageList.ViewModel.RefreshCurrentAsync();
    }

    // Entry point for toast activation: open the given message in the reading pane.
    public async Task OpenMessageAsync(Guid messageId)
    {
        var messageStore = App.Services.GetRequiredService<IMessageStore>();
        var message = await messageStore.GetByIdAsync(messageId, CancellationToken.None);
        if (message is not null)
            await ReadingPane.ViewModel.LoadAsync(message);
    }
}
