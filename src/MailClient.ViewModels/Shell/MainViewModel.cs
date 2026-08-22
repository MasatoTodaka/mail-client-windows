using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// Root shell view model. The sidebar owns its own view model (SidebarViewModel) since M3;
// MessageListViewModel / ReadingPaneViewModel land the same way in M4/M5.
public sealed partial class MainViewModel : ViewModelBase
{
    public string MessageListPlaceholderText => "メッセージ一覧 (M4で実装)";
    public string ReadingPanePlaceholderText => "メッセージ本文 (M5で実装)";
}
