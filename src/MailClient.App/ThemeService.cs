using MailClient.Core.Abstractions;
using MailClient.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace MailClient.App;

// Applies the persisted light/dark/system choice to every window the app opens. Per-element
// RequestedTheme (not Application.RequestedTheme, which can't change after launch) is what lets
// the switch happen live; each window registers itself once its content exists.
public sealed class ThemeService : IThemeService
{
    // Title bar colors have to be set by hand: the caption buttons otherwise follow the *system*
    // theme, which is the wrong one whenever the app's choice differs from it.
    private static readonly Color LightBackground = Color.FromArgb(255, 0xF6, 0xF3, 0xEE);
    private static readonly Color LightForeground = Color.FromArgb(255, 0x24, 0x21, 0x1C);
    private static readonly Color LightInactive = Color.FromArgb(255, 0x6B, 0x64, 0x5A);
    private static readonly Color DarkBackground = Color.FromArgb(255, 0x15, 0x14, 0x12);
    private static readonly Color DarkForeground = Color.FromArgb(255, 0xEC, 0xE8, 0xE1);
    private static readonly Color DarkInactive = Color.FromArgb(255, 0xA3, 0x9B, 0x8E);

    private readonly ISettingsStore _settingsStore;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly UISettings _uiSettings = new();
    private readonly List<Window> _windows = [];

    public ThemeService(ISettingsStore settingsStore, IUiDispatcher uiDispatcher)
    {
        _settingsStore = settingsStore;
        _uiDispatcher = uiDispatcher;

        // The OS theme changing while we follow it (e.g. the evening dark-mode schedule). Raised on
        // a background thread.
        _uiSettings.ColorValuesChanged += (_, _) => _uiDispatcher.Post(() =>
        {
            if (Mode == ThemeMode.System)
                ApplyToAll();
        });
    }

    public ThemeMode Mode { get; private set; } = ThemeMode.System;

    public event EventHandler? ModeChanged;

    // What's actually showing right now, with "system" resolved.
    public bool IsDark => Mode switch
    {
        ThemeMode.Dark => true,
        ThemeMode.Light => false,
        _ => _uiSettings.GetColorValue(UIColorType.Background).R < 128,
    };

    public async Task InitializeAsync()
    {
        try
        {
            Mode = await _settingsStore.GetThemeModeAsync(CancellationToken.None);
        }
        catch
        {
            Mode = ThemeMode.System;
        }
    }

    public async Task SetModeAsync(ThemeMode mode, CancellationToken ct)
    {
        Mode = mode;
        ApplyToAll();
        ModeChanged?.Invoke(this, EventArgs.Empty);
        await _settingsStore.SetThemeModeAsync(mode, ct);
    }

    // The sidebar's quick toggle: flips whatever is currently showing.
    public Task ToggleAsync() => SetModeAsync(IsDark ? ThemeMode.Light : ThemeMode.Dark, CancellationToken.None);

    // Call once the window's Content has been created (after InitializeComponent).
    public void Register(Window window)
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        Apply(window);
    }

    private void ApplyToAll()
    {
        foreach (var window in _windows.ToList())
            Apply(window);
    }

    private void Apply(Window window)
    {
        try
        {
            if (window.Content is FrameworkElement root)
            {
                root.RequestedTheme = Mode switch
                {
                    ThemeMode.Light => ElementTheme.Light,
                    ThemeMode.Dark => ElementTheme.Dark,
                    _ => ElementTheme.Default,
                };
            }

            ApplyTitleBar(window);
        }
        catch
        {
            // A window that's mid-close can throw here; theming it no longer matters.
        }
    }

    private void ApplyTitleBar(Window window)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported())
            return;

        var dark = IsDark;
        var background = dark ? DarkBackground : LightBackground;
        var foreground = dark ? DarkForeground : LightForeground;
        var inactive = dark ? DarkInactive : LightInactive;
        var hover = dark ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(24, 0, 0, 0);
        var pressed = dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(48, 0, 0, 0);

        var titleBar = window.AppWindow.TitleBar;
        titleBar.BackgroundColor = background;
        titleBar.ForegroundColor = foreground;
        titleBar.InactiveBackgroundColor = background;
        titleBar.InactiveForegroundColor = inactive;
        titleBar.ButtonBackgroundColor = background;
        titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = inactive;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
    }
}
