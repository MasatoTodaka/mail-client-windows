using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// Root shell view model. Sidebar (M3), message list (M4), and reading pane (M5) each own their
// own view model, composed directly by MainWindow's code-behind — this one currently has no
// state of its own, but stays as the natural place for window-level state later (e.g. M11).
public sealed partial class MainViewModel : ViewModelBase;
