using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// Root shell view model. Sidebar (M3), message list (M4), and reading pane (M5) each own their
// own view model, composed directly by MainWindow's code-behind.
public sealed partial class MainViewModel : ViewModelBase
{
    public string ReadingPanePlaceholderText => "メッセージ本文 (M5で実装)";
}
