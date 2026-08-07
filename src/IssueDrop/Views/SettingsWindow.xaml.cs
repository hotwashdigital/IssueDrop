using System.Windows;
using System.Windows.Input;
using IssueDrop.Models;
using IssueDrop.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace IssueDrop.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    public event EventHandler? SettingsSaved;

    public SettingsWindow(SettingsService settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemeCombo.ItemsSource = Enum.GetValues<ThemePreference>();
        ThemeCombo.SelectedItem = settings.Current.Theme;
        HotkeyBox.Text = settings.Current.Hotkey;
        StartupCheck.IsChecked = settings.Current.LaunchAtStartup;
        RetentionBox.Text = settings.Current.HistoryRetentionDays.ToString();
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        var modifiers = Keyboard.Modifiers;
        if (modifiers == ModifierKeys.None) { ErrorText.Text = "Include Ctrl, Alt, Shift, or the Windows key."; e.Handled = true; return; }
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key == Key.Space ? "Space" : key.ToString());
        HotkeyBox.Text = string.Join('+', parts);
        ErrorText.Text = string.Empty;
        e.Handled = true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!HotkeyGesture.TryParse(HotkeyBox.Text, out _)) { ErrorText.Text = "Choose a valid keyboard shortcut."; return; }
        if (!int.TryParse(RetentionBox.Text, out var retention) || retention is < 1 or > 3650) { ErrorText.Text = "History retention must be between 1 and 3650 days."; return; }
        _settings.Current.Hotkey = HotkeyBox.Text;
        _settings.Current.Theme = ThemeCombo.SelectedItem is ThemePreference theme ? theme : ThemePreference.System;
        _settings.Current.LaunchAtStartup = StartupCheck.IsChecked == true;
        _settings.Current.HistoryRetentionDays = retention;
        await _settings.SaveAsync();
        _settings.ApplyStartupPreference();
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        DialogResult = true;
        Close();
    }
}
