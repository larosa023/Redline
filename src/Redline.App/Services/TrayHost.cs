using System.Runtime.InteropServices;
using Redline.App.ViewModels;

namespace Redline.App.Services;

/// <summary>
/// Notification-area icon with a profile menu, plus global profile shortcuts (RegisterHotKey).
/// Runs on the UI thread's message loop through a hidden window; never shown.
/// </summary>
public sealed class TrayHost : IDisposable
{
    const uint CallbackMsg = 0x8000 + 1;   // WM_APP + 1
    const int IdOpen = 1, IdQuit = 2, IdProfileBase = 1000;

    readonly WndProc _proc;
    readonly nint _hwnd, _icon;
    readonly uint _taskbarCreated;
    readonly Dictionary<int, string> _hotkeyProfiles = new();
    IReadOnlyList<TrayMenuItem> _items = [];
    string _tip = "Redline";
    bool _added, _disposed;

    public event Action? OpenRequested;
    public event Action? QuitRequested;
    public event Action<string>? ProfilePicked;
    public event Action<string>? HotkeyPressed;

    public TrayHost(string iconPath)
    {
        _proc = WindowProc;
        var hInst = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInst, lpszClassName = "RedlineTray",
        };
        RegisterClassExW(ref wc);
        _hwnd = CreateWindowExW(0, "RedlineTray", "Redline tray", 0x80000000 /* WS_POPUP */, 0, 0, 0, 0, 0, 0, hInst, 0);
        _icon = LoadImageW(0, iconPath, 1 /* IMAGE_ICON */, GetSystemMetrics(49), GetSystemMetrics(50), 0x10 /* LR_LOADFROMFILE */);
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        AddIcon();
    }

    public void SetTooltip(string tip)
    {
        _tip = tip.Length > 120 ? tip[..120] : tip;
        if (_added) { var d = Data(NIF_TIP); Shell_NotifyIconW(NIM_MODIFY, ref d); }
    }

    public void SetMenu(IReadOnlyList<TrayMenuItem> items) => _items = items;

    /// <summary>Registers the shortcuts, replacing any from before. Returns the ones Windows refused (another app owns them).</summary>
    public IReadOnlyList<string> SetHotkeys(IReadOnlyList<HotkeyBinding> bindings)
    {
        foreach (var id in _hotkeyProfiles.Keys) UnregisterHotKey(_hwnd, id);
        _hotkeyProfiles.Clear();
        var refused = new List<string>();
        var next = 1;
        foreach (var b in bindings)
        {
            if (!Hotkey.TryParse(b.Gesture, out var chord)) continue;
            var id = next++;
            if (RegisterHotKey(_hwnd, id, chord.Modifiers | 0x4000 /* MOD_NOREPEAT */, chord.Key)) _hotkeyProfiles[id] = b.ProfileId;
            else refused.Add(b.Gesture);
        }
        return refused;
    }

    void AddIcon()
    {
        if (_hwnd == 0) return;
        var d = Data(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        _added = Shell_NotifyIconW(NIM_ADD, ref d);
    }

    NOTIFYICONDATAW Data(uint flags) => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(), hWnd = _hwnd, uID = 1, uFlags = flags,
        uCallbackMessage = CallbackMsg, hIcon = _icon, szTip = _tip, szInfo = "", szInfoTitle = "",
    };

    nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        try
        {
            if (msg == CallbackMsg)
            {
                switch ((uint)(lParam & 0xFFFF))
                {
                    case 0x0202: OpenRequested?.Invoke(); break;          // WM_LBUTTONUP
                    case 0x0205: ShowMenu(); break;                        // WM_RBUTTONUP
                }
                return 0;
            }
            if (msg == 0x0312 && _hotkeyProfiles.TryGetValue((int)wParam, out var profile))   // WM_HOTKEY
            {
                HotkeyPressed?.Invoke(profile);
                return 0;
            }
            if (msg == _taskbarCreated && _taskbarCreated != 0) { AddIcon(); return 0; }   // Explorer restarted
        }
        catch (Exception e) { CrashLog.Write("tray", e, show: false); }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < _items.Count; i++)
                AppendMenuW(menu, _items[i].Checked ? MF_STRING | MF_CHECKED : MF_STRING, (nuint)(IdProfileBase + i), _items[i].Label);
            if (_items.Count > 0) AppendMenuW(menu, MF_SEPARATOR, 0, null);
            AppendMenuW(menu, MF_STRING, IdOpen, "Open Redline");
            AppendMenuW(menu, MF_STRING, IdQuit, "Quit");
            GetCursorPos(out var pt);
            SetForegroundWindow(_hwnd);
            var cmd = TrackPopupMenu(menu, 0x0100 /* TPM_RETURNCMD */ | 0x0002 /* TPM_RIGHTBUTTON */, pt.X, pt.Y, 0, _hwnd, 0);
            PostMessageW(_hwnd, 0, 0, 0);   // WM_NULL: lets the menu close properly when clicking elsewhere
            if (cmd == IdOpen) OpenRequested?.Invoke();
            else if (cmd == IdQuit) QuitRequested?.Invoke();
            else if (cmd >= IdProfileBase && cmd - IdProfileBase < _items.Count) ProfilePicked?.Invoke(_items[cmd - IdProfileBase].ProfileId);
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var id in _hotkeyProfiles.Keys) UnregisterHotKey(_hwnd, id);
        if (_added) { var d = Data(0); Shell_NotifyIconW(NIM_DELETE, ref d); }
        if (_icon != 0) DestroyIcon(_icon);
        if (_hwnd != 0) DestroyWindow(_hwnd);
    }

    const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800;

    delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NOTIFYICONDATAW
    {
        public uint cbSize; public nint hWnd; public uint uID, uFlags, uCallbackMessage; public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags; public Guid guidItem; public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] static extern nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint LoadImageW(nint inst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rect);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] static extern bool PostMessageW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(nint hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandleW(string? name);
}
