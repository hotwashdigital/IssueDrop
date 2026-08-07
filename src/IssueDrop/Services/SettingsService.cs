using IssueDrop.Infrastructure;
using IssueDrop.Models;
using Microsoft.Win32;

namespace IssueDrop.Services;

public sealed class SettingsService
{
    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        AppPaths.EnsureCreated();
        Current = await JsonFile.ReadAsync(AppPaths.SettingsFile, new AppSettings());
        if (UpgradeLegacyDefaults(Current)) await SaveAsync();
    }

    public static bool UpgradeLegacyDefaults(AppSettings settings)
    {
        if (!settings.Hotkey.Equals(AppSettings.LegacyDefaultHotkey, StringComparison.OrdinalIgnoreCase)) return false;
        settings.Hotkey = AppSettings.DefaultHotkey;
        return true;
    }

    public Task SaveAsync() => JsonFile.WriteAsync(AppPaths.SettingsFile, Current);

    public void ApplyStartupPreference()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ISSUEDROP_DATA_DIR"))) return;
#if DEBUG
        return;
#else
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if (Current.LaunchAtStartup)
            key.SetValue("IssueDrop", $"\"{Environment.ProcessPath}\" --background");
        else
            key.DeleteValue("IssueDrop", false);
#endif
    }
}
