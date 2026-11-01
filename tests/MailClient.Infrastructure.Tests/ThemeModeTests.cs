using MailClient.Core.Models;
using Xunit;

namespace MailClient.Infrastructure.Tests;

public sealed class ThemeModeTests
{
    [Theory]
    [InlineData(ThemeMode.System, "system")]
    [InlineData(ThemeMode.Light, "light")]
    [InlineData(ThemeMode.Dark, "dark")]
    public void StorageValue_RoundTrips(ThemeMode mode, string stored)
    {
        Assert.Equal(stored, mode.ToStorageValue());
        Assert.Equal(mode, ThemeModeExtensions.ParseThemeMode(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sepia")]
    [InlineData("DARK")]
    public void UnknownOrMissingValue_FollowsTheSystem(string? stored)
    {
        Assert.Equal(ThemeMode.System, ThemeModeExtensions.ParseThemeMode(stored));
    }

    [Fact]
    public void PickerOrder_MatchesTheEnumNumbers()
    {
        // The settings RadioButtons index is cast straight to ThemeMode: 0 system, 1 light, 2 dark.
        Assert.Equal(0, (int)ThemeMode.System);
        Assert.Equal(1, (int)ThemeMode.Light);
        Assert.Equal(2, (int)ThemeMode.Dark);
    }
}
