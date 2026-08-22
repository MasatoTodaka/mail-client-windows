using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Data;

namespace MailClient.App.Views.Converters;

// Unread (IsRead == false) renders bold, matching macOS Mail's unread convention.
public sealed class BoolToFontWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is true ? FontWeights.Normal : FontWeights.Bold;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
