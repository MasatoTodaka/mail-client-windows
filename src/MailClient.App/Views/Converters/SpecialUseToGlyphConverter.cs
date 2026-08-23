using MailClient.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace MailClient.App.Views.Converters;

public sealed class SpecialUseToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is MailFolderSpecialUse specialUse
            ? specialUse switch
            {
                MailFolderSpecialUse.Inbox => "",
                MailFolderSpecialUse.Sent => "",
                MailFolderSpecialUse.Drafts => "",
                MailFolderSpecialUse.Trash => "",
                MailFolderSpecialUse.Junk => "",
                MailFolderSpecialUse.Archive => "",
                MailFolderSpecialUse.Flagged => "",
                MailFolderSpecialUse.Today => "",
                _ => "",
            }
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
