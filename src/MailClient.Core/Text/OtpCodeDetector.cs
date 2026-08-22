using System.Text.RegularExpressions;

namespace MailClient.Core.Text;

// Heuristic: look for a 4-8 digit code near a recognizable "this is your code" phrase, rather
// than just grabbing any digit run — order numbers, prices, and dates are common false positives
// otherwise. Checks a window around each keyword match so the code can appear either before it
// ("123456 is your verification code") or after ("Your code: 123456").
public static class OtpCodeDetector
{
    private static readonly string[] Keywords =
    [
        "verification code", "confirmation code", "security code", "access code",
        "one-time password", "one time password", "one-time code", "otp code", "otp:", " otp ",
        "your code", "auth code", "passcode",
        "認証コード", "確認コード", "セキュリティコード", "ワンタイムパスワード",
        "確認番号", "認証番号", "パスコード", "確認用コード", "認証用コード",
    ];

    private static readonly Regex CodePattern = new(@"\d[\d\s-]{2,9}\d", RegexOptions.Compiled);

    private const int WindowRadius = 60;

    public static string? TryExtract(string? subject, string? bodyText)
    {
        var combined = string.Join('\n', new[] { subject, bodyText }.Where(s => !string.IsNullOrEmpty(s)));
        if (combined.Length == 0)
            return null;

        var lower = combined.ToLowerInvariant();

        foreach (var keyword in Keywords)
        {
            var idx = lower.IndexOf(keyword, StringComparison.Ordinal);
            if (idx < 0)
                continue;

            var windowStart = Math.Max(0, idx - WindowRadius);
            var windowEnd = Math.Min(combined.Length, idx + keyword.Length + WindowRadius);
            var window = combined[windowStart..windowEnd];

            foreach (Match match in CodePattern.Matches(window))
            {
                var digitsOnly = new string(match.Value.Where(char.IsDigit).ToArray());
                if (digitsOnly.Length is >= 4 and <= 8)
                    return digitsOnly;
            }
        }

        return null;
    }
}
