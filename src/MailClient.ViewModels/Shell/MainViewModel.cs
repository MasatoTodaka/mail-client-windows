using MailClient.ViewModels.Common;

namespace MailClient.ViewModels.Shell;

// Root shell view model. Sub-panes get their own view models (SidebarViewModel,
// MessageListViewModel, ReadingPaneViewModel) wired in as real data arrives in later milestones.
public sealed partial class MainViewModel : ViewModelBase
{
    public string SidebarPlaceholderText => "アカウント / フォルダ (M3で実装)";
    public string MessageListPlaceholderText => "メッセージ一覧 (M4で実装)";
    public string ReadingPanePlaceholderText => "メッセージ本文 (M5で実装)";
}
