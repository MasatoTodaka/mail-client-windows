using MailClient.ViewModels.Shell;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace MailClient.App.Views.Shell;

public sealed partial class MessageListView : UserControl
{
    public MessageListViewModel ViewModel { get; }

    public MessageListView()
    {
        ViewModel = App.Services.GetRequiredService<MessageListViewModel>();
        InitializeComponent();
    }
}
