using System;
using System.Windows;
using System.Windows.Media;
using TrackDot.Models;

namespace TrackDot.Services;

/// <summary>
/// WPF-side adapter that listens to an <see cref="IThemeService"/> and
/// paints the application palette (colors, brushes, and system color
/// overrides) into <see cref="Application.Current"/>'s
/// <see cref="Application.Resources"/>.
///
/// This class is the only place in the codebase that touches
/// <see cref="Application.Current"/> in response to a theme change.
/// Keeping it isolated here means <see cref="ThemeService"/> stays a
/// pure state machine with no WPF dependencies, which makes it
/// trivially unit-testable and removes the dispatcher-marshalling
/// deadlock that hangs the test host when no <see cref="Application"/>
/// is present on the test's STA thread.
///
/// Owned by <c>App.xaml.cs</c> as a sibling service to
/// <see cref="IThemeService"/>; constructed after the service, with
/// its lifetime bound to the application's.
/// </summary>
public sealed class WpfThemePaletteApplier : IDisposable
{
    private readonly IThemeService _themeService;
    private bool _disposed;

    public WpfThemePaletteApplier(IThemeService themeService)
    {
        ArgumentNullException.ThrowIfNull(themeService);
        _themeService = themeService;
        _themeService.EffectiveThemeChanged += OnEffectiveThemeChanged;
    }

    /// <summary>
    /// Paints the palette for the service's current effective theme
    /// without waiting for a change event. Call once at startup so the
    /// initial theme takes effect on the first frame.
    /// </summary>
    public void ApplyInitial()
    {
        UpdateApplicationResources(_themeService.IsEffectiveDark);
    }

    private void OnEffectiveThemeChanged(object? sender, bool isDark)
    {
        UpdateApplicationResources(isDark);
    }

    private void UpdateApplicationResources(bool isDark)
    {
        var app = Application.Current;
        if (app == null)
        {
            // No WPF application is alive on this thread (e.g. a unit
            // test). Nothing to paint. The next time a real app
            // subscribes, the next event will do the work.
            return;
        }

        void Action()
        {
            var res = app.Resources;
            if (res == null) return;

            // Palette definitions for Dark and Light mode
            var panelColor = isDark ? ColorFromHex("#1A1B1E") : ColorFromHex("#FFFFFF");
            var panelBorderColor = isDark ? ColorFromHex("#2E3036") : ColorFromHex("#E5E7EB");
            var cardColor = isDark ? ColorFromHex("#202226") : ColorFromHex("#F9FAFB");
            var cardBorderColor = isDark ? ColorFromHex("#2E3036") : ColorFromHex("#E5E7EB");
            var textColor = isDark ? ColorFromHex("#F3F4F6") : ColorFromHex("#111827");
            var mutedColor = isDark ? ColorFromHex("#9CA3AF") : ColorFromHex("#6B7280");
            var accentColor = isDark ? ColorFromHex("#8AB4F8") : ColorFromHex("#1A73E8");
            var accentHoverColor = isDark ? ColorFromHex("#A3C5FF") : ColorFromHex("#1765CC");
            var accentPressedColor = isDark ? ColorFromHex("#719FE4") : ColorFromHex("#1557B0");
            var accentFgColor = isDark ? ColorFromHex("#121316") : ColorFromHex("#FFFFFF");
            var badgeBgColor = isDark ? ColorFromHex("#27282D") : ColorFromHex("#F3F4F6");
            var badgeBorderColor = isDark ? ColorFromHex("#37383E") : ColorFromHex("#E5E7EB");
            var buttonHoverColor = isDark ? ColorFromHex("#2B2D33") : ColorFromHex("#E5E7EB");
            var buttonPressedColor = isDark ? ColorFromHex("#393C44") : ColorFromHex("#D1D5DB");
            var progressTrackColor = isDark ? ColorFromHex("#2D2F35") : ColorFromHex("#E5E7EB");
            var artworkBgColor = isDark ? ColorFromHex("#26282E") : ColorFromHex("#F3F4F6");
            var statusErrorColor = isDark ? ColorFromHex("#F28B82") : ColorFromHex("#D93025");

            SetOrUpdateBrush(res, "PanelBrush", panelColor);
            SetOrUpdateBrush(res, "PanelBorderBrush", panelBorderColor);
            SetOrUpdateBrush(res, "CardBrush", cardColor);
            SetOrUpdateBrush(res, "CardBorderBrush", cardBorderColor);
            SetOrUpdateBrush(res, "TextBrush", textColor);
            SetOrUpdateBrush(res, "MutedBrush", mutedColor);
            SetOrUpdateBrush(res, "AccentBrush", accentColor);
            SetOrUpdateBrush(res, "AccentHoverBrush", accentHoverColor);
            SetOrUpdateBrush(res, "AccentPressedBrush", accentPressedColor);
            SetOrUpdateBrush(res, "AccentForegroundBrush", accentFgColor);
            SetOrUpdateBrush(res, "BadgeBackgroundBrush", badgeBgColor);
            SetOrUpdateBrush(res, "BadgeBorderBrush", badgeBorderColor);
            SetOrUpdateBrush(res, "ButtonHoverBrush", buttonHoverColor);
            SetOrUpdateBrush(res, "ButtonPressedBrush", buttonPressedColor);
            SetOrUpdateBrush(res, "ProgressTrackBrush", progressTrackColor);
            SetOrUpdateBrush(res, "ArtworkBackgroundBrush", artworkBgColor);
            SetOrUpdateBrush(res, "StatusErrorBrush", statusErrorColor);

            // Context Menu SystemColors overrides
            SetOrUpdateBrush(res, SystemColors.HighlightBrushKey, buttonHoverColor);
            SetOrUpdateBrush(res, SystemColors.HighlightTextBrushKey, textColor);
            SetOrUpdateBrush(res, SystemColors.MenuHighlightBrushKey, buttonHoverColor);
            SetOrUpdateBrush(res, SystemColors.MenuBrushKey, panelColor);
            SetOrUpdateBrush(res, SystemColors.ControlBrushKey, panelColor);
        }

        if (app.Dispatcher.CheckAccess())
        {
            Action();
        }
        else
        {
            // Use BeginInvoke rather than Invoke: the applier is a
            // passive subscriber to a state-machine event and must
            // never block the publisher's thread. If the dispatcher
            // isn't pumping right now (e.g. during shutdown) the
            // next pump will run the update; for production this
            // always happens before the next render frame.
            app.Dispatcher.BeginInvoke(Action);
        }
    }

    private static void SetOrUpdateBrush(ResourceDictionary res, object key, Color color)
    {
        if (res.Contains(key) && res[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
        }
        else
        {
            res[key] = new SolidColorBrush(color);
        }
    }

    private static Color ColorFromHex(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _themeService.EffectiveThemeChanged -= OnEffectiveThemeChanged;
    }
}
