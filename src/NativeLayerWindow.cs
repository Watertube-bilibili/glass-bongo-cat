using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlassBongoCat
{
    /// <summary>
    /// A non-activating native layered window. Position and premultiplied BGRA
    /// content are submitted together, without WPF HWND render-thread barriers.
    /// All methods, the message hook and GDI lifetime belong to its creating thread.
    /// </summary>
    internal sealed class NativeLayerWindow : IDisposable
    {
        private const uint Layered = 0x00080000, Transparent = 0x20, ExcludeFromCapture = 0x11;
        private static readonly object ClassGate = new object();
        private static readonly Dictionary<IntPtr, NativeLayerWindow> Windows = new Dictionary<IntPtr, NativeLayerWindow>();
        private static readonly WindowProcedure Procedure = WindowProc;
        private static readonly string ClassName = "GlassPet.NativeLayer." + Process.GetCurrentProcess().Id;
        private static bool classRegistered;
        private readonly Dispatcher dispatcher;
        private IntPtr handle, screen, memory, bitmap, previous, pixels;
        private int bufferWidth, bufferHeight;
        private bool closed, clickThrough, affinityApplied;
        private uint previousAffinity;

        internal HwndSourceHook MessageHook;
        internal IntPtr Handle { get { return handle; } }
        internal bool IsVisible { get { return handle != IntPtr.Zero && IsWindowVisible(handle); } }
        internal int LeftPixels { get; private set; }
        internal int TopPixels { get; private set; }
        internal int WidthPixels { get; private set; }
        internal int HeightPixels { get; private set; }
        internal double DpiScale { get; private set; }
        internal string LastError { get; private set; }

        internal NativeLayerWindow(string title)
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            DpiScale = ReadSystemDpi() / 96.0;
            LastError = "";
            RegisterWindowClass();
            handle = CreateWindowEx(0x08000000 | Layered | 0x80 | 0x08, ClassName,
                title ?? "GlassPet", 0x80000000, 0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法创建桌宠窗口。");
            WidthPixels = HeightPixels = 1;
            lock (ClassGate) Windows.Add(handle, this);
        }

        internal void Show()
        {
            VerifyOpen();
            ShowWindow(handle, 4); // SW_SHOWNOACTIVATE
        }
        internal void Hide()
        {
            VerifyAccess();
            if (closed) return;
            SetPointerCapture(false);
            SetCaptureExclusion(false);
            ShowWindow(handle, 0);
        }

        internal bool Present(BitmapSource source, int x, int y)
        {
            VerifyOpen();
            if (source == null) throw new ArgumentNullException("source");
            int width = source.PixelWidth, height = source.PixelHeight;
            if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 8 * 1024 * 1024)
                throw new ArgumentException("桌宠图像尺寸无效或过大。", "source");
            try
            {
                EnsureSurface(width, height);
                BitmapSource premultiplied = source;
                if (source.Format != PixelFormats.Pbgra32)
                    premultiplied = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                // Directly fill the cached DIB. No managed per-frame pixel array.
                premultiplied.CopyPixels(Int32Rect.Empty, pixels, width * height * 4, width * 4);
                NativePoint destination = new NativePoint(x, y), origin = new NativePoint(0, 0);
                NativeSize size = new NativeSize(width, height);
                BlendFunction blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
                if (!UpdateLayeredWindow(handle, IntPtr.Zero, ref destination, ref size, memory, ref origin, 0, ref blend, 2))
                {
                    LastError = "桌宠呈现失败：" + new Win32Exception(Marshal.GetLastWin32Error()).Message;
                    ReleaseSurface();
                    return false;
                }
                LeftPixels = x; TopPixels = y; WidthPixels = width; HeightPixels = height;
                LastError = "";
                return true;
            }
            catch (OutOfMemoryException) { ReleaseSurface(); LastError = "桌宠呈现内存不足。"; return false; }
            catch (Win32Exception error) { ReleaseSurface(); LastError = error.Message; return false; }
        }

        internal void SetClickThrough(bool enabled)
        {
            VerifyOpen();
            long style = GetExtendedStyle(handle).ToInt64();
            long updated = enabled ? style | Transparent : style & ~((long)Transparent);
            if (updated != style && SetExtendedStyle(handle, new IntPtr(updated)) == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法更新桌宠点击穿透状态。");
            clickThrough = enabled;
        }

        internal bool SetCaptureExclusion(bool enabled)
        {
            VerifyAccess();
            if (closed || handle == IntPtr.Zero) return !enabled;
            if (!enabled)
            {
                if (!affinityApplied) return true;
                if (!SetWindowDisplayAffinity(handle, previousAffinity))
                { LastError = "无法恢复桌宠原捕获状态。"; return false; }
                affinityApplied = false;
                return true;
            }
            if (!NativeCaptureSupport.IsSupported)
            { LastError = NativeCaptureSupport.FailureReason; return false; }
            uint current;
            if (!GetWindowDisplayAffinity(handle, out current))
            { LastError = "无法读取桌宠捕获状态。"; return false; }
            if (affinityApplied && current == ExcludeFromCapture) return true;
            if (!affinityApplied) previousAffinity = current;
            if (!SetWindowDisplayAffinity(handle, ExcludeFromCapture))
            { LastError = "系统无法排除桌宠自身，背景折射已停用。"; return false; }
            affinityApplied = true;
            if (!GetWindowDisplayAffinity(handle, out current) || current != ExcludeFromCapture)
            { SetCaptureExclusion(false); LastError = "系统未确认桌宠捕获排除。"; return false; }
            return true;
        }

        internal void Raise()
        {
            VerifyOpen();
            SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        }

        internal void SetPointerCapture(bool enabled)
        {
            VerifyAccess();
            if (enabled) { VerifyOpen(); SetCapture(handle); }
            else if (handle != IntPtr.Zero && GetCapture() == handle) ReleaseCapture();
        }

        internal void Close()
        {
            VerifyAccess();
            if (closed) return;
            SetPointerCapture(false);
            SetCaptureExclusion(false);
            IntPtr current = handle;
            if (current != IntPtr.Zero && !DestroyWindow(current))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法关闭桌宠窗口。");
            NativeDestroyed(current);
        }
        public void Dispose() { Close(); }

        private void VerifyAccess() { dispatcher.VerifyAccess(); }
        private void VerifyOpen() { VerifyAccess(); if (closed) throw new ObjectDisposedException("NativeLayerWindow"); }

        private void EnsureSurface(int width, int height)
        {
            if (bitmap != IntPtr.Zero && width == bufferWidth && height == bufferHeight) return;
            ReleaseSurface();
            try
            {
                screen = GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                memory = CreateCompatibleDC(screen);
                if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                BitmapInfo info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
                bitmap = CreateDIBSection(screen, ref info, 0, out pixels, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || pixels == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                previous = SelectObject(memory, bitmap);
                if (previous == IntPtr.Zero || previous == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
                bufferWidth = width; bufferHeight = height;
            }
            catch { ReleaseSurface(); throw; }
        }

        private void ReleaseSurface()
        {
            if (memory != IntPtr.Zero && previous != IntPtr.Zero && previous != new IntPtr(-1)) SelectObject(memory, previous);
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memory != IntPtr.Zero) DeleteDC(memory);
            if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen);
            screen = memory = bitmap = previous = pixels = IntPtr.Zero;
            bufferWidth = bufferHeight = 0;
        }

        private void NativeDestroyed(IntPtr destroyed)
        {
            ReleaseSurface();
            lock (ClassGate) Windows.Remove(destroyed);
            handle = IntPtr.Zero;
            closed = true;
            affinityApplied = false;
            MessageHook = null;
        }

        private static IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam)
        {
            NativeLayerWindow window;
            lock (ClassGate) Windows.TryGetValue(hwnd, out window);
            if (window != null)
            {
                try
                {
                    HwndSourceHook hook = window.MessageHook;
                    if (hook != null)
                    {
                        bool handled = false;
                        IntPtr result = hook(hwnd, (int)message, wparam, lparam, ref handled);
                        if (handled && message != 0x0082) return result;
                    }
                    if (message == 0x0021) return new IntPtr(3); // MA_NOACTIVATE
                    if (message == 0x0084 && window.clickThrough) return new IntPtr(-1);
                    if (message == 0x0014) return new IntPtr(1); // no GDI background erase
                    if (message == 0x0010) { window.Close(); return IntPtr.Zero; }
                    if (message == 0x0047)
                    {
                        NativeRect bounds;
                        if (GetWindowRect(hwnd, out bounds))
                        {
                            window.LeftPixels = bounds.Left; window.TopPixels = bounds.Top;
                            window.WidthPixels = bounds.Right - bounds.Left; window.HeightPixels = bounds.Bottom - bounds.Top;
                        }
                    }
                    if (message == 0x007e) { window.ReleaseSurface(); window.DpiScale = ReadSystemDpi() / 96.0; }
                    if (message == 0x0082) window.NativeDestroyed(hwnd);
                }
                catch (Exception error)
                {
                    // Never unwind a managed exception across the native WndProc.
                    window.LastError = "桌宠窗口消息处理失败：" + error.Message;
                    Trace.WriteLine(error);
                    if (message == 0x0082) window.NativeDestroyed(hwnd);
                }
            }
            return DefWindowProc(hwnd, message, wparam, lparam);
        }

        private static void RegisterWindowClass()
        {
            lock (ClassGate)
            {
                if (classRegistered) return;
                WindowClass definition = new WindowClass { Size = (uint)Marshal.SizeOf(typeof(WindowClass)), Style = 0x0008,
                    Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), Instance = GetModuleHandle(null),
                    Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)), Name = ClassName };
                if (RegisterClassEx(ref definition) == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法注册桌宠窗口。");
                classRegistered = true;
            }
        }

        private static uint ReadSystemDpi()
        {
            try { uint dpi = GetDpiForSystem(); if (dpi >= 48 && dpi <= 768) return dpi; }
            catch (EntryPointNotFoundException) { }
            IntPtr dc = GetDC(IntPtr.Zero);
            try { int dpi = dc == IntPtr.Zero ? 96 : GetDeviceCaps(dc, 88); return (uint)(dpi >= 48 && dpi <= 768 ? dpi : 96); }
            finally { if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc); }
        }
        private static IntPtr GetExtendedStyle(IntPtr hwnd) { return IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, -20) : new IntPtr(GetWindowLong(hwnd, -20)); }
        private static IntPtr SetExtendedStyle(IntPtr hwnd, IntPtr style) { return IntPtr.Size == 8 ? SetWindowLongPtr(hwnd, -20, style) : new IntPtr(SetWindowLong(hwnd, -20, style.ToInt32())); }

        private delegate IntPtr WindowProcedure(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WindowClass
        {
            internal uint Size, Style;
            internal IntPtr Procedure;
            internal int ClassExtra, WindowExtra;
            internal IntPtr Instance, Icon, Cursor, Background;
            internal string MenuName, Name;
            internal IntPtr SmallIcon;
        }
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X, Y; internal NativePoint(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { internal int Width, Height; internal NativeSize(int width, int height) { Width = width; Height = height; } }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BlendFunction { internal byte Operation, Flags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal uint Size; internal int Width, Height; internal ushort Planes, BitCount;
            internal uint Compression, SizeImage; internal int XPels, YPels; internal uint Used, Important;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WindowClass definition);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateWindowEx(uint extended, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr data);
        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
        [DllImport("user32.dll", EntryPoint = "LoadCursorW", CharSet = CharSet.Unicode)] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursor);
        [DllImport("user32.dll")] private static extern IntPtr SetCapture(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ReleaseCapture();
        [DllImport("user32.dll")] private static extern IntPtr GetCapture();
        [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll", SetLastError = true)] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr destinationDc, ref NativePoint destination, ref NativeSize size, IntPtr sourceDc, ref NativePoint origin, uint colorKey, ref BlendFunction blend, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    }
}
