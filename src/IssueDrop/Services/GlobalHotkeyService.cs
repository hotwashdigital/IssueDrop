using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace IssueDrop.Services;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0, Alt = 0x0001, Ctrl = 0x0002, Shift = 0x0004, Win = 0x0008, NoRepeat = 0x4000
}

public sealed record HotkeyGesture(HotkeyModifiers Modifiers, Key Key)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key == Key.Space ? "Space" : Key.ToString());
        return string.Join('+', parts);
    }

    public static bool TryParse(string text, out HotkeyGesture gesture)
    {
        gesture = new HotkeyGesture(HotkeyModifiers.None, Key.None);
        if (string.IsNullOrWhiteSpace(text)) return false;
        var modifiers = HotkeyModifiers.None;
        Key key = Key.None;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= HotkeyModifiers.Ctrl; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win" or "windows": modifiers |= HotkeyModifiers.Win; break;
                default:
                    if (key != Key.None) return false;
                    try { key = (Key)new KeyConverter().ConvertFromInvariantString(raw)!; }
                    catch { return false; }
                    break;
            }
        }
        if (key == Key.None || modifiers == HotkeyModifiers.None) return false;
        gesture = new HotkeyGesture(modifiers, key);
        return true;
    }
}

public sealed class GlobalHotkeyService : IDisposable
{
    private const int HotkeyId = 0x1D10;
    private const int WmHotkey = 0x0312;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfAltDown = 0x20;
    private const int VkSpace = 0x20;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private readonly Dispatcher _dispatcher;
    private readonly LowLevelKeyboardProc _hookProc;
    private bool _registered;
    private IntPtr _hookHandle;
    private bool _fallbackKeyDown;

    public event EventHandler? Pressed;
    public string RegistrationMode { get; private set; } = "None";

    public GlobalHotkeyService(Window window)
    {
        _handle = new WindowInteropHelper(window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException("Unable to create window source.");
        _dispatcher = window.Dispatcher;
        _hookProc = KeyboardHookCallback;
        _source.AddHook(WindowProc);
    }

    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        var modifiers = gesture.Modifiers | HotkeyModifiers.NoRepeat;
        _registered = RegisterHotKey(_handle, HotkeyId, (uint)modifiers, (uint)KeyInterop.VirtualKeyFromKey(gesture.Key));
        if (_registered)
        {
            RegistrationMode = "RegisterHotKey";
            return true;
        }

        if (!IsAltSpace(gesture)) return false;
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
        if (_hookHandle == IntPtr.Zero) return false;
        RegistrationMode = "AltSpace keyboard hook";
        return true;
    }

    private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    private void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(_handle, HotkeyId);
            _registered = false;
        }
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        _fallbackKeyDown = false;
        RegistrationMode = "None";
    }

    private static bool IsAltSpace(HotkeyGesture gesture) =>
        gesture.Key == Key.Space && gesture.Modifiers == HotkeyModifiers.Alt;

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code < 0) return CallNextHookEx(_hookHandle, code, wParam, lParam);
        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        if (data.VirtualKey != VkSpace) return CallNextHookEx(_hookHandle, code, wParam, lParam);

        var message = wParam.ToInt32();
        var keyDown = message is WmKeyDown or WmSysKeyDown;
        var keyUp = message is WmKeyUp or WmSysKeyUp;
        var altContext = (data.Flags & LlkhfAltDown) != 0;
        var otherModifier = IsKeyDown(VkShift) || IsKeyDown(VkControl) || IsKeyDown(VkLWin) || IsKeyDown(VkRWin);

        if (keyDown && altContext && !otherModifier)
        {
            if (!_fallbackKeyDown)
            {
                _fallbackKeyDown = true;
                _dispatcher.BeginInvoke(() => Pressed?.Invoke(this, EventArgs.Empty));
            }
            return new IntPtr(1);
        }
        if (keyUp && _fallbackKeyDown)
        {
            _fallbackKeyDown = false;
            return new IntPtr(1);
        }
        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WindowProc);
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct KbdLlHookStruct
    {
        public readonly uint VirtualKey;
        public readonly uint ScanCode;
        public readonly uint Flags;
        public readonly uint Time;
        public readonly UIntPtr ExtraInfo;
    }
}
