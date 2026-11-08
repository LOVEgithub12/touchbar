using System.ComponentModel;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace WindowsTouchBar.Interop;

/// <summary>A desktop brightness indicator; it never activates or takes keyboard focus.</summary>
internal static class BrightnessOsd
{
    private static readonly Lazy<WindowHost> Host = new(() => new WindowHost());

    public static void ShowScreen(int percent) => Show(false, percent, null);
    public static void ShowKeyboard(int percent) => Show(true, percent, null);
    public static void ShowUnavailable(string label) => Show(label.Contains("键盘", StringComparison.Ordinal), 0, label);

    private static void Show(bool keyboard, int percent, string? unavailableLabel)
    {
        // A notification failure must not interrupt brightness control or touch input.
        try { Host.Value.Show(keyboard, Math.Clamp(percent, 0, 100), unavailableLabel); }
        catch (Exception error) { Report(error); }
    }

    public static void Shutdown()
    {
        if (Host.IsValueCreated) Host.Value.Stop();
    }

    private static void Report(Exception error)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "WindowsTouchBar.osd.log"),
                $"{DateTime.Now:O} {error}{Environment.NewLine}");
        }
        catch { }
    }

    private sealed class WindowHost
    {
        private const uint RefreshMessage = 0x8001;
        private const uint HideTimer = 1;
        private const int LogicalWidth = 230;
        private const int LogicalHeight = 88;
        private readonly object gate = new();
        private readonly Thread thread;
        private readonly WindowProcedure procedure;
        private IntPtr window;
        private bool pending, refreshQueued, stopping;
        private bool nextKeyboard, displayedKeyboard;
        private int nextPercent, displayedPercent;
        private string? nextUnavailableLabel, displayedUnavailableLabel;
        private int pixelWidth = LogicalWidth, pixelHeight = LogicalHeight;

        public WindowHost()
        {
            procedure = WindowProc;
            thread = new Thread(Run) { IsBackground = true, Name = "Touch Bar brightness indicator" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }

        public void Show(bool keyboard, int percent, string? unavailableLabel)
        {
            lock (gate)
            {
                if (stopping) return;
                nextKeyboard = keyboard;
                nextPercent = percent;
                nextUnavailableLabel = unavailableLabel;
                pending = true;
                if (window != IntPtr.Zero && !refreshQueued)
                {
                    refreshQueued = true;
                    if (!PostMessage(window, RefreshMessage, IntPtr.Zero, IntPtr.Zero)) refreshQueued = false;
                }
            }
        }

        public void Stop()
        {
            lock (gate)
            {
                stopping = true;
                if (window != IntPtr.Zero) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }
            if (Thread.CurrentThread != thread) thread.Join(1000);
        }

        private void Run()
        {
            string className = $"WindowsTouchBar.BrightnessOsd.{Environment.ProcessId}";
            IntPtr instance = GetModuleHandle(null);
            IntPtr previousDpi = IntPtr.Zero;
            bool registered = false;
            try
            {
                previousDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
                var definition = new WindowClass
                {
                    Size = (uint)Marshal.SizeOf<WindowClass>(),
                    Procedure = procedure,
                    Instance = instance,
                    ClassName = className
                };
                if (RegisterClassEx(ref definition) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                registered = true;
                // TOPMOST | TRANSPARENT | TOOLWINDOW | LAYERED | NOACTIVATE.
                IntPtr created = CreateWindowEx(0x080800a8, className, "Touch Bar brightness", 0x80000000,
                    0, 0, LogicalWidth, LogicalHeight, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
                if (created == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                SetLayeredWindowAttributes(created, 0, 250, 2);
                lock (gate)
                {
                    window = created;
                    if (stopping) PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    else if (pending)
                    {
                        refreshQueued = true;
                        PostMessage(window, RefreshMessage, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                int result;
                while ((result = GetMessage(out var message, IntPtr.Zero, 0, 0)) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
                if (result < 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            }
            catch (Exception error) { Report(error); }
            finally
            {
                IntPtr remaining;
                lock (gate) { stopping = true; remaining = window; window = IntPtr.Zero; }
                if (remaining != IntPtr.Zero) DestroyWindow(remaining);
                if (registered) UnregisterClass(className, instance);
                if (previousDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        private IntPtr WindowProc(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                switch (message)
                {
                    case RefreshMessage:
                        Refresh(handle);
                        return IntPtr.Zero;
                    case 0x000f: // WM_PAINT
                        Paint(handle);
                        return IntPtr.Zero;
                    case 0x0014: // WM_ERASEBKGND: Paint draws the complete surface.
                        return new IntPtr(1);
                    case 0x0021: // WM_MOUSEACTIVATE
                        return new IntPtr(3); // MA_NOACTIVATE
                    case 0x0084: // WM_NCHITTEST
                        return new IntPtr(-1); // HTTRANSPARENT
                    case 0x0113: // WM_TIMER
                        if (wParam.ToInt64() == HideTimer)
                        {
                            KillTimer(handle, new UIntPtr(HideTimer));
                            ShowWindow(handle, 0);
                        }
                        return IntPtr.Zero;
                    case 0x0010: // WM_CLOSE
                        DestroyWindow(handle);
                        return IntPtr.Zero;
                    case 0x0002: // WM_DESTROY
                        KillTimer(handle, new UIntPtr(HideTimer));
                        lock (gate) { window = IntPtr.Zero; }
                        PostQuitMessage(0);
                        return IntPtr.Zero;
                }
            }
            catch (Exception error)
            {
                Report(error);
                // Hide the indicator if rendering fails; the Touch Bar keeps working.
                ShowWindow(handle, 0);
            }
            return DefWindowProc(handle, message, wParam, lParam);
        }

        private void Refresh(IntPtr handle)
        {
            lock (gate)
            {
                refreshQueued = false;
                pending = false;
                if (stopping) return;
                displayedKeyboard = nextKeyboard;
                displayedPercent = nextPercent;
                displayedUnavailableLabel = nextUnavailableLabel;
            }
            float scale = Math.Max(96, GetDpiForWindow(handle)) / 96f;
            pixelWidth = (int)Math.Round(LogicalWidth * scale);
            pixelHeight = (int)Math.Round(LogicalHeight * scale);
            int margin = (int)Math.Round(20 * scale);
            var workArea = new Rect();
            SystemParametersInfo(0x0030, 0, ref workArea, 0); // SPI_GETWORKAREA: primary screen.
            SetWindowText(handle, $"{DisplayLabel} {(displayedUnavailableLabel is null ? $"{displayedPercent}%" : "不可用")}");
            SetWindowPos(handle, new IntPtr(-1), workArea.Left + margin, workArea.Top + margin,
                pixelWidth, pixelHeight, 0x0010 | 0x0040); // NOACTIVATE | SHOWWINDOW.
            InvalidateRect(handle, IntPtr.Zero, false);
            SetTimer(handle, new UIntPtr(HideTimer), 1600, IntPtr.Zero);
        }

        private string DisplayLabel => displayedUnavailableLabel ?? (displayedKeyboard ? "键盘背光" : "屏幕亮度");

        private void Paint(IntPtr handle)
        {
            IntPtr deviceContext = BeginPaint(handle, out var paintState);
            try
            {
                using var bitmap = new SKBitmap(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Opaque);
                using var canvas = new SKCanvas(bitmap);
                canvas.Clear(new SKColor(35, 35, 35));
                canvas.Scale(pixelWidth / (float)LogicalWidth, pixelHeight / (float)LogicalHeight);
                using var ink = new SKPaint { IsAntialias = true, Color = new SKColor(245, 245, 245) };
                DrawIcon(canvas, ink, displayedKeyboard);
                using var typeface = SKTypeface.FromFamilyName("Microsoft YaHei UI");
                ink.Typeface = typeface;
                ink.TextSize = 15;
                canvas.DrawText(DisplayLabel, 55, 36, ink);
                ink.TextAlign = SKTextAlign.Right;
                ink.TextSize = 17;
                canvas.DrawText(displayedUnavailableLabel is null ? $"{displayedPercent}%" : "不可用", LogicalWidth - 18, 36, ink);
                ink.Color = new SKColor(83, 83, 83);
                canvas.DrawRoundRect(SKRect.Create(18, 59, LogicalWidth - 36, 7), 3.5f, 3.5f, ink);
                ink.Color = new SKColor(0, 150, 235);
                if (displayedPercent > 0 && displayedUnavailableLabel is null)
                    canvas.DrawRoundRect(SKRect.Create(18, 59, (LogicalWidth - 36) * displayedPercent / 100f, 7), 3.5f, 3.5f, ink);
                canvas.Flush();
                var info = new BitmapInfo
                {
                    Size = 40, Width = pixelWidth, Height = -pixelHeight,
                    Planes = 1, BitCount = 32, SizeImage = (uint)(pixelWidth * pixelHeight * 4)
                };
                StretchDIBits(deviceContext, 0, 0, pixelWidth, pixelHeight, 0, 0, pixelWidth, pixelHeight,
                    bitmap.GetPixels(), ref info, 0, 0x00cc0020);
            }
            finally { EndPaint(handle, ref paintState); }
        }

        private static void DrawIcon(SKCanvas canvas, SKPaint ink, bool keyboard)
        {
            ink.Style = SKPaintStyle.Stroke;
            ink.StrokeWidth = 1.8f;
            if (keyboard)
            {
                canvas.DrawRoundRect(SKRect.Create(18, 24, 26, 17), 2, 2, ink);
                for (int row = 0; row < 2; row++)
                    for (int column = 0; column < 5; column++)
                        canvas.DrawLine(22 + column * 4.5f, 29 + row * 4, 23 + column * 4.5f, 29 + row * 4, ink);
                canvas.DrawLine(25, 37, 37, 37, ink);
                canvas.DrawLine(24, 18, 22, 15, ink);
                canvas.DrawLine(31, 17, 31, 13, ink);
                canvas.DrawLine(38, 18, 40, 15, ink);
            }
            else
            {
                canvas.DrawCircle(31, 30, 6.5f, ink);
                for (int ray = 0; ray < 8; ray++)
                {
                    double angle = ray * Math.PI / 4;
                    canvas.DrawLine(31 + 10 * (float)Math.Cos(angle), 30 + 10 * (float)Math.Sin(angle),
                        31 + 14 * (float)Math.Cos(angle), 30 + 14 * (float)Math.Sin(angle), ink);
                }
            }
            ink.Style = SKPaintStyle.Fill;
        }
    }

    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public WindowProcedure Procedure;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message
    {
        public IntPtr Window;
        public uint Id;
        public IntPtr WParam, LParam;
        public uint Time;
        public Point Position;
        public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct PaintState
    {
        public IntPtr DeviceContext;
        public int Erase;
        public Rect Bounds;
        public int Restore, Update;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] public byte[] Reserved;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPixelsPerMeter, YPixelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass definition);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool UnregisterClass(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint exStyle, string className, string name, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr window, uint key, byte alpha, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Message message);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr window, string text);
    [DllImport("user32.dll")] private static extern bool InvalidateRect(IntPtr window, IntPtr rectangle, bool erase);
    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint milliseconds, IntPtr callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr window, UIntPtr id);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action, uint parameter, ref Rect value, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintState state);
    [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintState state);
    [DllImport("gdi32.dll")] private static extern int StretchDIBits(IntPtr context, int x, int y, int width, int height, int sourceX, int sourceY, int sourceWidth, int sourceHeight, IntPtr bits, ref BitmapInfo info, uint usage, uint operation);
}
