using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace MailClient.App.Views.Converters;

// Visible when the bound int is greater than zero — used for unread-count badges.
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
