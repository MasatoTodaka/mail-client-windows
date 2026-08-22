using MailClient.ViewModels.Rules;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MailClient.App.Views.Rules;

public sealed partial class RulesWindow : Window
{
    public RulesViewModel ViewModel { get; }

    public RulesWindow(RulesViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        _ = ViewModel.LoadCommand.ExecuteAsync(null);
    }

    private void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: RuleDisplayItem item })
            ViewModel.DeleteRuleCommand.Execute(item);
    }

    private Visibility EmptyRulesVisibility(int ruleCount) =>
        ruleCount == 0 ? Visibility.Visible : Visibility.Collapsed;

    private Visibility TextVisibility(string? text) =>
        string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
