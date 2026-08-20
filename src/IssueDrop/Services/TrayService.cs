using System.Drawing;
using System.Drawing.Drawing2D;
using IssueDrop.Models;
using Forms = System.Windows.Forms;

namespace IssueDrop.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _ownedIcon;

    public event EventHandler? OpenRequested;
    public event EventHandler? HistoryRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? AboutRequested;
    public event EventHandler? QuitRequested;

    public TrayService()
    {
        _ownedIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty) ?? CreateIcon();
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("New issue", null, (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Drafts & history", null, (_, _) => HistoryRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("About IssueDrop", null, (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit IssueDrop", null, (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty));

        _icon = new Forms.NotifyIcon
        {
            Text = $"IssueDrop — {AppSettings.DefaultHotkey}",
            Icon = _ownedIcon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowMessage(string title, string message, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.BalloonTipIcon = icon;
        _icon.ShowBalloonTip(3500);
    }

    public void SetHotkeyText(string hotkey) => _icon.Text = $"IssueDrop — {hotkey}";

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.FromArgb(78, 143, 217));
        graphics.FillEllipse(background, 1, 1, 30, 30);
        using var pen = new Pen(Color.White, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(pen, 16, 7, 16, 21);
        graphics.DrawLine(pen, 10, 16, 16, 22);
        graphics.DrawLine(pen, 22, 16, 16, 22);
        var handle = bitmap.GetHicon();
        try { return Icon.FromHandle(handle).Clone() as Icon ?? (Icon)SystemIcons.Application.Clone(); }
        finally { DestroyIcon(handle); }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _ownedIcon.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
