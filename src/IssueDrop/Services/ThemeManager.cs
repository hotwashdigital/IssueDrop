using System.Windows;
using IssueDrop.Models;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace IssueDrop.Services;

public sealed class ThemeManager : IDisposable
{
    private ThemePreference _preference;

    public ThemeManager() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public void Apply(ThemePreference preference)
    {
        _preference = preference;
        var light = preference == ThemePreference.Light || preference == ThemePreference.System && SystemUsesLightTheme();
        var source = new Uri(light ? "Themes/Light.xaml" : "Themes/Dark.xaml", UriKind.Relative);
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(x =>
            x.Source?.OriginalString.Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
        var replacement = new System.Windows.ResourceDictionary { Source = source };
        if (existing is null) dictionaries.Insert(0, replacement);
        else dictionaries[dictionaries.IndexOf(existing)] = replacement;
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            var value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            return value is int intValue && intValue != 0;
        }
        catch { return false; }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (_preference == ThemePreference.System && Application.Current is not null)
            Application.Current.Dispatcher.Invoke(() => Apply(_preference));
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
}
