using Microsoft.UI.Xaml.Data;

namespace MailClient.App.Views.Converters;

public sealed class DateTimeOffsetToShortStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is DateTimeOffset date ? date.LocalDateTime.ToString("yyyy/MM/dd HH:mm") : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
