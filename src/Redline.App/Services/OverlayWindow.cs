using System.Runtime.InteropServices;

namespace Redline.App.Services;

/// <summary>
/// Small always-on-top, click-through window drawn with GDI. It never touches game processes, so it's anti-cheat safe.
/// Shows over borderless/windowed games; exclusive-fullscreen games may hide it.
/// </summary>
public sealed class OverlayWindow : IDisposable
{
    const int W = 380, H = 64, Margin = 16;
    readonly WndProc _proc;
    readonly nint _hwnd;
    string _line1 = "", _line2 = "";
    bool _disposed;

    public OverlayWindow()
    {
        _proc = WindowProc;
        var hInst = GetModuleHandleW(null);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(), lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_proc),
            hInstance = hInst, lpszClassName = "RedlineOverlay", hbrBackground = CreateSolidBrush(0x0E0A0A),
        };
        RegisterClassExW(ref wc);
        _hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
            "RedlineOverlay", "Redline overlay", WS_POPUP, 0, 0, W, H, 0, 0, hInst, 0);
        SetLayeredWindowAttributes(_hwnd, 0, 215, LWA_ALPHA);
    }

    public void Update(bool visible, string line1, string line2, string corner)
    {
        if (_disposed || _hwnd == 0) return;
        _line1 = line1; _line2 = line2;
        if (!visible) { ShowWindow(_hwnd, SW_HIDE); return; }
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        var x = corner.EndsWith("Right", StringComparison.Ordinal) ? sw - W - Margin : Margin;
        var y = corner.StartsWith("Bottom", StringComparison.Ordinal) ? sh - H - Margin - 48 : Margin;
        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, W, H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        InvalidateRect(_hwnd, 0, true);
    }

    nint WindowProc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WM_PAINT)
        {
            var hdc = BeginPaint(hwnd, out var ps);
            var bg = CreateSolidBrush(0x0E0A0A);
            var rc = new RECT { Right = W, Bottom = H };
            FillRect(hdc, ref rc, bg);
            DeleteObject(bg);
            SetBkMode(hdc, 1);
            var big = CreateFontW(-22, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
            var small = CreateFontW(-15, 0, 0, 0, 400, 0, 0, 0, 0, 0, 0, 5, 0, "Segoe UI");
            var old = SelectObject(hdc, big);
            SetTextColor(hdc, 0xFFFFFF);
            var r1 = new RECT { Left = 14, Top = 6, Right = W - 10, Bottom = 34 };
            DrawTextW(hdc, _line1.Length > 0 ? _line1 : "Redline", -1, ref r1, 0x20);
            SelectObject(hdc, small);
            SetTextColor(hdc, 0xC8C8D0);
            var r2 = new RECT { Left = 14, Top = 36, Right = W - 10, Bottom = H - 4 };
            DrawTextW(hdc, _line2, -1, ref r2, 0x20);
            SelectObject(hdc, old);
            DeleteObject(big); DeleteObject(small);
            EndPaint(hwnd, ref ps);
            return 0;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hwnd != 0) DestroyWindow(_hwnd);
    }

    delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    const uint WS_POPUP = 0x80000000, WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80,
        WS_EX_NOACTIVATE = 0x08000000, LWA_ALPHA = 0x2, WM_PAINT = 0x000F, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const int SW_HIDE = 0;
    static readonly nint HWND_TOPMOST = -1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEXW
    {
        public uint cbSize, style; public nint lpfnWndProc; public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName; public string lpszClassName; public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct PAINTSTRUCT
    {
        public nint hdc; public int fErase; public RECT rcPaint; public int fRestore, fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] rgbReserved;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] static extern nint DefWindowProcW(nint hwnd, uint msg, nint w, nint l);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(nint hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool InvalidateRect(nint hwnd, nint rect, bool erase);
    [DllImport("user32.dll")] static extern nint BeginPaint(nint hwnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern bool EndPaint(nint hwnd, ref PAINTSTRUCT ps);
    [DllImport("user32.dll")] static extern int FillRect(nint hdc, ref RECT rc, nint brush);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DrawTextW(nint hdc, string text, int len, ref RECT rc, uint fmt);
    [DllImport("gdi32.dll")] static extern nint CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] static extern int SetBkMode(nint hdc, int mode);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(nint hdc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern nint CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(nint obj);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern nint GetModuleHandleW(string? name);
}
