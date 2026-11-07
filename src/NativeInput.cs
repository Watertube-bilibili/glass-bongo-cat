using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace GlassBongoCat
{
    /// <summary>
    /// UI-thread-only, transient input for the pet animation. This class never
    /// converts keys to text, writes input to disk, or keeps an event history.
    /// Cursor is in physical desktop pixels; MouseSpeed is pixels per second.
    /// </summary>
    internal sealed class NativeInput : IDisposable
    {
        private const int WmInput = 0x00FF, WmHotkey = 0x0312;
        private const uint RidInput = 0x10000003, RidevInputSink = 0x100, RidevRemove = 1;
        private const int FirstHotkey = 0x6B10;
        private static readonly uint[] HotkeyKeys = { 0x47, 0x53, 0x51, 0x50 };
        private readonly bool[] down = new bool[256];
        private readonly double[] heat = new double[256];
        private readonly bool[] hotkeys = new bool[4];
        private Window owner;
        private Dispatcher dispatcher;
        private HwndSource source;
        private IntPtr handle, rawBuffer;
        private DispatcherTimer timer;
        private bool enabled = true, disposed, rawRegistered, haveCursor;
        private double lastTick = Clock, lastActivity = Clock;
        private Point cursor;
        private double mouseSpeed;

        internal Action ToggleRequested { get; set; }
        internal Action SettingsRequested { get; set; }
        internal Action ExitRequested { get; set; }
        internal Action PauseRequested { get; set; }

        // Registration errors can be shown by settings without preventing startup.
        internal string Status { get; private set; }
        internal bool RawInputAvailable { get { return rawRegistered; } }
        internal bool AllHotkeysAvailable
        {
            get { return hotkeys[0] && hotkeys[1] && hotkeys[2] && hotkeys[3]; }
        }
        internal bool Enabled
        {
            get { return enabled; }
            set
            {
                VerifyAccess();
                if (enabled == value) return;
                enabled = value;
                ClearInput();
                lastTick = Clock;
                if (enabled) { lastActivity = Clock; UpdateCursor(0); }
            }
        }
        internal Point Cursor { get { return cursor; } }
        internal bool LeftButton { get { return IsDown(1); } }
        internal bool RightButton { get { return IsDown(2); } }
        internal double LastActivitySeconds { get { return Math.Max(0, Clock - lastActivity); } }
        internal double MouseSpeed { get { return enabled ? mouseSpeed : 0; } }

        internal bool IsDown(int vk)
        {
            return enabled && vk >= 0 && vk < down.Length && down[vk];
        }

        internal double Heat(int vk)
        {
            if (!enabled || vk < 0 || vk >= heat.Length) return 0;
            return down[vk] ? 1 : heat[vk];
        }

        internal void Attach(Window window)
        {
            if (window == null) throw new ArgumentNullException("window");
            if (disposed) throw new ObjectDisposedException("NativeInput");
            window.Dispatcher.VerifyAccess();
            if (owner == window) return;
            if (dispatcher != null) throw new InvalidOperationException("Input is already attached to a window.");
            owner = window;
            window.SourceInitialized += OnSourceInitialized;
            window.Closed += OnClosed;
            StartTimer(window.Dispatcher);
            Connect();
        }

        /// <summary>
        /// Attach to a caller-owned native window on this dispatcher thread.
        /// The host forwards its WndProc to ProcessMessage and disposes input
        /// before destroying the HWND. No HwndSource hook is installed here.
        /// </summary>
        internal void Attach(IntPtr hwnd, Dispatcher dispatcher)
        {
            if (dispatcher == null) throw new ArgumentNullException("dispatcher");
            if (disposed) throw new ObjectDisposedException("NativeInput");
            dispatcher.VerifyAccess();
            uint process;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) ||
                GetWindowThreadProcessId(hwnd, out process) != GetCurrentThreadId() || process != GetCurrentProcessId())
                throw new ArgumentException("Input requires a window owned by the current dispatcher thread.", "hwnd");
            if (owner == null && handle == hwnd && this.dispatcher == dispatcher) return;
            if (this.dispatcher != null) throw new InvalidOperationException("Input is already attached to a window.");
            handle = hwnd;
            StartTimer(dispatcher);
            RegisterInput();
        }

        private void StartTimer(Dispatcher target)
        {
            dispatcher = target;
            Status = "";
            timer = new DispatcherTimer(DispatcherPriority.Input, dispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += OnTick;
            lastTick = lastActivity = Clock;
            UpdateCursor(0);
            timer.Start();
        }

        private void VerifyAccess()
        {
            if (dispatcher != null) dispatcher.VerifyAccess();
        }

        private void OnSourceInitialized(object sender, EventArgs e) { Connect(); }
        private void OnClosed(object sender, EventArgs e) { Dispose(); }

        private void Connect()
        {
            if (disposed || owner == null || source != null) return;
            handle = new WindowInteropHelper(owner).Handle;
            if (handle == IntPtr.Zero) return;
            source = HwndSource.FromHwnd(handle);
            if (source == null) return;
            source.AddHook(WndProc);
            RegisterInput();
        }

        private void RegisterInput()
        {
            rawBuffer = Marshal.AllocHGlobal(256);
            RawInputDevice[] devices = {
                new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevInputSink, Target = handle },
                new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RidevInputSink, Target = handle }
            };
            rawRegistered = RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
            if (!rawRegistered) Status = "后台输入不可用，已切换到按键状态采样（Windows " + Marshal.GetLastWin32Error() + "）。";
            for (int i = 0; i < hotkeys.Length; i++)
            {
                hotkeys[i] = RegisterHotKey(handle, FirstHotkey + i, 0x4003, HotkeyKeys[i]);
                if (!hotkeys[i])
                    Status += " Ctrl+Alt+" + (char)HotkeyKeys[i] + " 已被占用或无法注册。";
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        { return ProcessMessage(hwnd, message, wParam, lParam, ref handled); }

        internal IntPtr ProcessMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (disposed || handle == IntPtr.Zero || hwnd != handle) return IntPtr.Zero;
            VerifyAccess();
            if (message == WmHotkey)
            {
                int id = wParam.ToInt32() - FirstHotkey;
                Action action = id == 0 ? ToggleRequested : id == 1 ? SettingsRequested : id == 2 ? ExitRequested : id == 3 ? PauseRequested : null;
                if (id >= 0 && id < 4)
                {
                    handled = true;
                    // Leave the native message before user callbacks hide/close a window.
                    if (action != null && dispatcher != null && !dispatcher.HasShutdownStarted)
                        dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(delegate { if (!disposed) action(); }));
                }
            }
            else if (message == WmInput && enabled && rawRegistered)
            {
                ReadRawInput(lParam);
                // DefWindowProc must receive foreground WM_INPUT for OS cleanup.
                // Leaving handled false also allows WPF to finish its processing.
            }
            return IntPtr.Zero;
        }

        private void ReadRawInput(IntPtr input)
        {
            uint headerSize = (uint)(8 + IntPtr.Size * 2), size = 256;
            uint read = GetRawInputData(input, RidInput, rawBuffer, ref size, headerSize);
            if (read == UInt32.MaxValue || read < headerSize || read > 256) return;
            int type = Marshal.ReadInt32(rawBuffer);
            IntPtr payload = IntPtr.Add(rawBuffer, (int)headerSize);
            if (type == 1 && read >= headerSize + 16)
            {
                int scan = (ushort)Marshal.ReadInt16(payload, 0);
                int flags = (ushort)Marshal.ReadInt16(payload, 2);
                int vk = (ushort)Marshal.ReadInt16(payload, 6);
                if (vk >= 255 || scan == 255) return;
                // Preserve left/right modifier identity; generic queries remain supported.
                if (vk == 16) vk = (int)MapVirtualKey((uint)scan, 3);
                else if (vk == 17) vk = (flags & 2) != 0 ? 163 : 162;
                else if (vk == 18) vk = (flags & 2) != 0 ? 165 : 164;
                SetKey(vk, (flags & 1) == 0);
                if (vk == 160 || vk == 161) UpdateModifier(16, 160, 161);
                else if (vk == 162 || vk == 163) UpdateModifier(17, 162, 163);
                else if (vk == 164 || vk == 165) UpdateModifier(18, 164, 165);
                lastActivity = Clock;
            }
            else if (type == 0 && read >= headerSize + 24)
            {
                // RAWMOUSE's ULONG union is aligned at byte 4, not byte 2.
                int buttons = (ushort)Marshal.ReadInt16(payload, 4);
                MouseButton(buttons, 0x0001, 0x0002, 1);
                MouseButton(buttons, 0x0004, 0x0008, 2);
                MouseButton(buttons, 0x0010, 0x0020, 4);
                MouseButton(buttons, 0x0040, 0x0080, 5);
                MouseButton(buttons, 0x0100, 0x0200, 6);
                if (buttons != 0 || Marshal.ReadInt32(payload, 12) != 0 || Marshal.ReadInt32(payload, 16) != 0)
                    lastActivity = Clock;
            }
        }

        private void MouseButton(int flags, int pressed, int released, int vk)
        {
            if ((flags & pressed) != 0) SetKey(vk, true);
            if ((flags & released) != 0) SetKey(vk, false);
        }

        private void UpdateModifier(int generic, int left, int right)
        {
            SetKey(generic, down[left] || down[right]);
        }

        private void SetKey(int vk, bool pressed)
        {
            if (vk <= 0 || vk >= down.Length) return;
            if (pressed) heat[vk] = 1;
            if (pressed != down[vk]) lastActivity = Clock;
            down[vk] = pressed;
        }

        private void OnTick(object sender, EventArgs e)
        {
            double now = Clock, elapsed = Math.Max(0.001, now - lastTick);
            lastTick = now;
            if (!enabled || disposed) return;
            double fade = Math.Exp(-elapsed / 0.24);
            for (int vk = 1; vk < down.Length; vk++)
            {
                // Polling only repairs releases with Raw Input enabled. It cannot
                // erase a brief press's heat, and clears keys after desktop switches.
                if (!rawRegistered || down[vk] || vk == 1 || vk == 2)
                {
                    bool pressed = GetAsyncKeyState(vk) < 0;
                    if (!rawRegistered || !pressed || vk <= 2) SetKey(vk, pressed);
                }
                if (!down[vk]) heat[vk] *= fade;
                if (heat[vk] < 0.002) heat[vk] = 0;
            }
            UpdateCursor(elapsed);
        }

        private void UpdateCursor(double elapsed)
        {
            NativePoint native;
            if (!GetCursorPos(out native)) { mouseSpeed = 0; return; }
            Point next = new Point(native.X, native.Y);
            double dx = next.X - cursor.X, dy = next.Y - cursor.Y;
            if (haveCursor && elapsed > 0)
            {
                double distance = Math.Sqrt(dx * dx + dy * dy);
                double target = Math.Min(10000, distance / elapsed);
                mouseSpeed += (target - mouseSpeed) * (1 - Math.Exp(-elapsed / 0.10));
                if (distance > 0) lastActivity = Clock;
                if (mouseSpeed < 0.05) mouseSpeed = 0;
            }
            cursor = next;
            haveCursor = true;
        }

        private void ClearInput()
        {
            Array.Clear(down, 0, down.Length);
            Array.Clear(heat, 0, heat.Length);
            mouseSpeed = 0;
            haveCursor = false;
        }

        public void Dispose()
        {
            if (disposed) return;
            VerifyAccess();
            disposed = true;
            if (timer != null) { timer.Stop(); timer.Tick -= OnTick; timer = null; }
            if (owner != null) { owner.SourceInitialized -= OnSourceInitialized; owner.Closed -= OnClosed; }
            if (handle != IntPtr.Zero)
            {
                for (int i = 0; i < hotkeys.Length; i++)
                {
                    if (hotkeys[i]) UnregisterHotKey(handle, FirstHotkey + i);
                    hotkeys[i] = false;
                }
                if (rawRegistered)
                {
                    RawInputDevice[] devices = {
                        new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevRemove, Target = IntPtr.Zero },
                        new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RidevRemove, Target = IntPtr.Zero }
                    };
                    RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf(typeof(RawInputDevice)));
                }
            }
            if (source != null) { source.RemoveHook(WndProc); source = null; }
            if (rawBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(rawBuffer); rawBuffer = IntPtr.Zero; }
            rawRegistered = false;
            handle = IntPtr.Zero;
            owner = null;
            dispatcher = null;
            ClearInput();
        }

        private static double Clock { get { return Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency; } }
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            internal ushort UsagePage, Usage;
            internal uint Flags;
            internal IntPtr Target;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint MapVirtualKey(uint code, uint mapType);
    }

    /// <summary>Ordinary-desktop overlay behavior; never attempts to enter the secure desktop.</summary>
    internal static class NativeWindow
    {
        private const int GwlExStyle = -20;
        private const long WsExToolWindow = 0x80, WsExNoActivate = 0x08000000,
            WsExTransparent = 0x20, WsExAppWindow = 0x40000;
        private static readonly ConditionalWeakTable<Window, OverlayState> States = new ConditionalWeakTable<Window, OverlayState>();

        private sealed class OverlayState
        {
            internal Window Window;
            internal bool ClickThrough;
            internal HwndSource Source;
            internal void Initialized(object sender, EventArgs e) { Apply(this); }
            internal void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
            {
                if (Window.IsVisible) Raise(Window);
            }
            internal void Closed(object sender, EventArgs e)
            {
                Window.SourceInitialized -= Initialized;
                Window.IsVisibleChanged -= VisibilityChanged;
                Window.Closed -= Closed;
                if (Source != null) Source.RemoveHook(Hook);
                States.Remove(Window);
            }
            internal IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE, preserve click
                if (ClickThrough && message == 0x84) { handled = true; return new IntPtr(-1); }
                return IntPtr.Zero;
            }
        }

        internal static void MakeOverlay(Window window)
        {
            if (window == null) throw new ArgumentNullException("window");
            window.Dispatcher.VerifyAccess();
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Topmost = true;
            OverlayState state;
            if (!States.TryGetValue(window, out state))
            {
                state = new OverlayState { Window = window };
                States.Add(window, state);
                window.SourceInitialized += state.Initialized;
                window.IsVisibleChanged += state.VisibilityChanged;
                window.Closed += state.Closed;
            }
            Apply(state);
        }

        internal static void Raise(Window window)
        {
            if (window == null) return;
            window.Dispatcher.VerifyAccess();
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0013); // NOMOVE | NOSIZE | NOACTIVATE
        }

        internal static void MoveTo(Window window, double left, double top)
        {
            window.Dispatcher.VerifyAccess();
            var hwnd = new WindowInteropHelper(window).Handle;
            var source = PresentationSource.FromVisual(window);
            if(hwnd == IntPtr.Zero || source == null) { window.Left=left;window.Top=top;return; }
            Point pixel=source.CompositionTarget.TransformToDevice.Transform(new Point(left,top));
            // One native move updates both coordinates. Two WPF property writes
            // cause two layered-window position transactions on every pointer move.
            SetWindowPos(hwnd,IntPtr.Zero,(int)Math.Round(pixel.X),(int)Math.Round(pixel.Y),0,0,0x0415);
        }

        internal static void SetClickThrough(Window window, bool enabled)
        {
            MakeOverlay(window);
            OverlayState state;
            if (States.TryGetValue(window, out state)) { state.ClickThrough = enabled; Apply(state); }
        }

        private static void Apply(OverlayState state)
        {
            IntPtr hwnd = new WindowInteropHelper(state.Window).Handle;
            if (hwnd == IntPtr.Zero) return;
            long style = GetStyle(hwnd).ToInt64();
            style = (style | WsExToolWindow | WsExNoActivate) & ~WsExAppWindow;
            style = state.ClickThrough ? style | WsExTransparent : style & ~WsExTransparent;
            SetStyle(hwnd, new IntPtr(style));
            if (state.Source == null)
            {
                state.Source = HwndSource.FromHwnd(hwnd);
                if (state.Source != null) state.Source.AddHook(state.Hook);
            }
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0033); // apply frame without activation
        }

        private static IntPtr GetStyle(IntPtr hwnd)
        {
            return IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, GwlExStyle) : new IntPtr(GetWindowLong(hwnd, GwlExStyle));
        }
        private static void SetStyle(IntPtr hwnd, IntPtr style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr(hwnd, GwlExStyle, style);
            else SetWindowLong(hwnd, GwlExStyle, style.ToInt32());
        }
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
