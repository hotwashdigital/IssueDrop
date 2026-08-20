using System.Windows;
using System.Windows.Threading;
using IssueDrop.Services;
using IssueDrop.Views;
using Forms = System.Windows.Forms;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using IssueDrop.Infrastructure;

namespace IssueDrop;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private SettingsService? _settings;
    private DraftStore? _drafts;
    private ThemeManager? _theme;
    private TrayService? _tray;
    private GlobalHotkeyService? _hotkey;
    private MainWindow? _composer;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppLog.Write($"Starting IssueDrop. Arguments: {string.Join(' ', e.Args)}");
        _singleInstance = new Mutex(true, @"Local\IssueDrop.Singleton", out var isFirst);
        _ownsMutex = isFirst;
        if (!isFirst)
        {
            try { EventWaitHandle.OpenExisting(@"Local\IssueDrop.Show").Set(); }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\IssueDrop.Show");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        _settings = new SettingsService();
        await _settings.LoadAsync();
        _theme = new ThemeManager();
        _theme.Apply(_settings.Current.Theme);
        _settings.ApplyStartupPreference();

        _drafts = new DraftStore();
        await _drafts.LoadAsync(_settings.Current.HistoryRetentionDays);
        var github = new GitHubService();
        var attachments = new AttachmentService();
        _tray = new TrayService();
        _composer = new MainWindow(github, _drafts, _settings, attachments,
            (title, message, error) => _tray.ShowMessage(title, message, error ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info));
        MainWindow = _composer;
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) =>
            Dispatcher.BeginInvoke(_composer.ShowFreshComposer), null, Timeout.Infinite, false);

        _hotkey = new GlobalHotkeyService(_composer);
        _hotkey.Pressed += (_, _) => Dispatcher.Invoke(_composer.ShowFreshComposer);
        RegisterHotkey();

        _tray.OpenRequested += (_, _) => Dispatcher.Invoke(_composer.ShowFreshComposer);
        _tray.HistoryRequested += (_, _) => Dispatcher.Invoke(OpenHistory);
        _tray.SettingsRequested += (_, _) => Dispatcher.Invoke(OpenSettings);
        _tray.AboutRequested += (_, _) => Dispatcher.Invoke(OpenAbout);
        _tray.QuitRequested += (_, _) => Dispatcher.Invoke(Shutdown);

        if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
        {
            AppLog.Write("Manual launch: showing composer.");
            _composer.ShowFreshComposer();
        }
        else AppLog.Write("Background launch: composer remains hidden.");
        await _composer.InitializeAsync();
        AppLog.Write("GitHub initialization completed.");
    }

    private void RegisterHotkey()
    {
        if (_settings is null || _hotkey is null || _tray is null) return;
        if (!HotkeyGesture.TryParse(_settings.Current.Hotkey, out var gesture) || !_hotkey.Register(gesture))
            _tray.ShowMessage("Shortcut unavailable", $"{_settings.Current.Hotkey} is already being used. Choose another shortcut in Settings.", Forms.ToolTipIcon.Warning);
        else AppLog.Write($"Global shortcut {_settings.Current.Hotkey} registered via {_hotkey.RegistrationMode}.");
        _tray.SetHotkeyText(_settings.Current.Hotkey);
    }

    private void OpenHistory()
    {
        if (_drafts is null || _composer is null) return;
        var window = new HistoryWindow(_drafts);
        window.EditRequested += (_, draft) => _composer.EditDraft(draft);
        window.Show(); window.Activate();
    }

    private void OpenSettings()
    {
        if (_settings is null || _theme is null) return;
        var window = new SettingsWindow(_settings);
        window.SettingsSaved += (_, _) => { _theme.Apply(_settings.Current.Theme); RegisterHotkey(); };
        window.ShowDialog();
    }

    private static void OpenAbout()
    {
        var window = new AboutWindow();
        window.ShowDialog();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        AppLog.Write($"Unhandled UI exception: {e.Exception}");
        _tray?.ShowMessage("IssueDrop encountered an error", e.Exception.Message, Forms.ToolTipIcon.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null); _showEvent?.Dispose();
        _hotkey?.Dispose(); _tray?.Dispose(); _theme?.Dispose();
        if (_ownsMutex) _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
