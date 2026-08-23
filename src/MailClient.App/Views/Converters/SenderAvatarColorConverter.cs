using Microsoft.UI.Xaml.Data;

namespace MailClient.App.Views.Converters;

public sealed class SenderAvatarColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        SenderAvatar.ColorFor(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
