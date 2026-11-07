using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
using GlassBongoCat;

internal static class CaptureThroughputTests
{
    private static StreamWriter log;
    private static IntPtr ordinaryWindow, layeredWindow, excludedWindow;
    private static bool strictFixture = true;
    [STAThread]
    private static int Main(string[] args)
    {
        Directory.CreateDirectory("artifacts");
        log = new StreamWriter("artifacts/capture-throughput.txt", false); log.AutoFlush = true;
        strictFixture = Array.IndexOf(args,"--timing-only") < 0;
        string sessionReason;
        if(strictFixture&&!DesktopSessionState.QueryActive(out sessionReason))
        { Write("SKIP live capture benchmark: "+sessionReason);log.Dispose();return 0; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        IntPtr foreground = GetForegroundWindow();
        var ordinary = new Window { Left = 100, Top = 100, Width = 700, Height = 520, WindowStyle = WindowStyle.None,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, Background = new SolidColorBrush(Color.FromRgb(30, 180, 70)) };
        try
        {
            ordinary.Show();
            IntPtr ordinaryHandle = new WindowInteropHelper(ordinary).Handle;
            ordinaryWindow = ordinaryHandle;
            ShowWindow(ordinaryHandle, 4); SetWindowPos(ordinaryHandle, new IntPtr(-1), 20, 20, 800, 600, 0x10 | 0x40); Pump(.3);
            NativeRect ordinaryBounds; GetWindowRect(ordinaryHandle, out ordinaryBounds); uint ordinaryAffinity; GetWindowDisplayAffinity(ordinaryHandle, out ordinaryAffinity);
            Write("FIXTURE ordinary visible=" + IsWindowVisible(ordinaryHandle) + "; bounds=" + ordinaryBounds.Left+","+ordinaryBounds.Top+","+ordinaryBounds.Right+","+ordinaryBounds.Bottom + "; affinity=" + ordinaryAffinity);
            Write("DESKTOP thread="+DesktopName(GetThreadDesktop(GetCurrentThreadId()))+"; input="+InputDesktopName());
            using (var layered = new NativeLayerWindow("Capture test ordinary layered background"))
            using (var excluded = new NativeLayerWindow("Capture test excluded pet"))
            {
                Check(layered.Present(Solid(400, 300, 45, 90, 210), 180, 160), "present ordinary layered fixture"); layered.Show();
                Check(excluded.Present(Solid(160, 100, 240, 30, 50), 220, 200), "present excluded pet fixture"); excluded.Show();
                Check(excluded.SetCaptureExclusion(true), "own pet capture exclusion");
                excluded.Raise(); DwmFlush(); Pump(.3);
                layeredWindow = layered.Handle; excludedWindow = excluded.Handle;
                Write("POINT normal=" + Describe(WindowFromPoint(new NativePoint {X=145,Y=140})) + "; underPet=" + Describe(WindowFromPoint(new NativePoint {X=260,Y=230})));
                int ordinaryCloak,layeredCloak,excludedCloak;
                DwmGetWindowAttribute(ordinaryHandle,14,out ordinaryCloak,4);DwmGetWindowAttribute(layered.Handle,14,out layeredCloak,4);DwmGetWindowAttribute(excluded.Handle,14,out excludedCloak,4);
                Write("CLOAK ordinary="+ordinaryCloak+"; layered="+layeredCloak+"; excluded="+excludedCloak+"; foreground="+Describe(foreground));
                if(Array.IndexOf(args,"--inspect")>=0)Pump(18);
                Check(GetForegroundWindow() == foreground, "fixtures never activate");
                Exception failure = null; bool done = false;
                var thread = new Thread(delegate()
                {
                    try { RunRaw(0x40CC0020, "CAPTUREBLT"); RunRaw(0x00CC0020, "SRCCOPY"); }
                    catch (Exception e) { failure = e; }
                    finally { Volatile.Write(ref done, true); }
                });
                thread.IsBackground = true; thread.Start();
                while (!Volatile.Read(ref done)) Pump(.05);
                if (failure != null) throw failure;
                using (var sampler = new AsyncDesktopBackdrop())
                {
                    var timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                    int received = 0; double blit = 0, flush = 0, copy = 0, validate = 0, total = 0, pre = 0, post = 0, create = 0, publish = 0;
                    var elapsed = Stopwatch.StartNew();
                    timer.Tick += delegate
                    {
                        sampler.Request(excluded.Handle, new Rect(220, 200, 300, 220));
                        BackdropFrame frame;
                        if (sampler.TryGetSnapshot(out frame))
                        {
                            received++; blit += sampler.LastBlitMilliseconds; flush += sampler.LastFlushMilliseconds;
                            copy += sampler.LastCopyMilliseconds; validate += sampler.LastValidationMilliseconds; total += sampler.LastCaptureMilliseconds;
                            pre+=sampler.LastPrecheckMilliseconds;post+=sampler.LastPostcheckMilliseconds;create+=sampler.LastBufferMilliseconds;publish+=sampler.LastPublishWaitMilliseconds;
                            int x = 260 - (int)frame.ScreenBounds.Left, y = 230 - (int)frame.ScreenBounds.Top;
                            if(strictFixture)Check(frame.Pixels[(y * frame.Width + x) * 4] > 180, "sampler sees blue background through excluded red pet");
                        }
                    };
                    timer.Start(); Pump(5); timer.Stop();
                    Write("ASYNC captures=" + sampler.CapturedFrames + "; received=" + received + "; seconds=" + elapsed.Elapsed.TotalSeconds.ToString("F3") +
                        "; ms total=" + (total / Math.Max(1, received)).ToString("F3") + "; blit=" + (blit / Math.Max(1, received)).ToString("F3") +
                        "; flush=" + (flush / Math.Max(1, received)).ToString("F3") + "; copy=" + (copy / Math.Max(1, received)).ToString("F3") +
                        "; checks=" + (validate / Math.Max(1, received)).ToString("F3") + "; failure=" + sampler.FailureReason);
                    Write("ASYNC stages pre="+(pre/Math.Max(1,received)).ToString("F3")+"; post="+(post/Math.Max(1,received)).ToString("F3")+"; buffer="+(create/Math.Max(1,received)).ToString("F3")+"; publish="+(publish/Math.Max(1,received)).ToString("F3")+"; buffersCreated="+sampler.NativeBufferCreations);
                    sampler.Stop();
                }
                Check(GetForegroundWindow() == foreground, "capture leaves user focus unchanged");
            }
            Write(strictFixture?"PASS: own ordinary and layered fixtures included; own excluded pet absent; user focus unchanged.":"DIAGNOSTIC ONLY: fixture pixels were not asserted; these timings do not validate live background capture."); return 0;
        }
        catch (Exception error) { Write(error.ToString()); return 1; }
        finally { ordinary.Close(); app.Shutdown(); log.Dispose(); }
    }
    private static void RunRaw(uint operation, string name)
    {
        const int width = 496, height = 412;
        IntPtr screen = GetDC(IntPtr.Zero), memory = IntPtr.Zero, bitmap = IntPtr.Zero, old = IntPtr.Zero, bits;
        try
        {
            memory = CreateCompatibleDC(screen);
            var info = new BitmapInfo { Size = 40, Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(screen, ref info, 0, out bits, IntPtr.Zero, 0); old = SelectObject(memory, bitmap);
            var pixels = new byte[width * height * 4];
            var duration = Stopwatch.StartNew(); int count = 0; double blit = 0, flush = 0, copy = 0;
            while (duration.Elapsed.TotalSeconds < 3 || count < 3)
            {
                double t = Now;
                Check(BitBlt(memory, 0, 0, width, height, screen, 124, 104, operation), "BitBlt " + name);
                double b = Now; Check(GdiFlush(), "flush " + name); double f = Now;
                Marshal.Copy(bits, pixels, 0, pixels.Length); double c = Now;
                blit += b - t; flush += f - b; copy += c - f; count++;
                int normal = ((140 - 104) * width + (145 - 124)) * 4;
                int underPet = ((230 - 104) * width + (260 - 124)) * 4;
                if(count==1)Write(name+" firstRGB normal="+pixels[normal+2]+","+pixels[normal+1]+","+pixels[normal]+"; under="+pixels[underPet+2]+","+pixels[underPet+1]+","+pixels[underPet]+"; blitms="+(b-t).ToString("F3"));
                if(strictFixture)Check(pixels[normal + 1] > 150 && pixels[normal + 2] < 70, name + " includes ordinary green window; RGB=" + pixels[normal+2]+","+pixels[normal+1]+","+pixels[normal]);
                if(strictFixture)Check(pixels[underPet] > 180 && pixels[underPet + 2] < 80, name + " includes ordinary blue layer and excludes red pet; RGB=" + pixels[underPet+2]+","+pixels[underPet+1]+","+pixels[underPet]);
            }
            Write(name + " n=" + count + "; fps=" + (count / duration.Elapsed.TotalSeconds).ToString("F1") +
                "; ms blit=" + (blit / count).ToString("F3") + "; flush=" + (flush / count).ToString("F3") + "; copy=" + (copy / count).ToString("F3"));
        }
        finally { if (old != IntPtr.Zero) SelectObject(memory, old); if (bitmap != IntPtr.Zero) DeleteObject(bitmap); if (memory != IntPtr.Zero) DeleteDC(memory); if (screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screen); }
    }
    private static BitmapSource Solid(int w, int h, byte r, byte g, byte b)
    { var p = new byte[w * h * 4]; for (int i = 0; i < p.Length; i += 4) { p[i] = b; p[i + 1] = g; p[i + 2] = r; p[i + 3] = 255; } return BitmapSource.Create(w, h, 96, 96, PixelFormats.Pbgra32, null, p, w * 4); }
    private static double Now { get { return Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency); } }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL " + message); }
    private static void Write(string value) { Console.WriteLine(value); log.WriteLine(value); }
    private static string Describe(IntPtr hwnd)
    {
        if(hwnd==ordinaryWindow)return "ordinary";if(hwnd==layeredWindow)return "layered";if(hwnd==excludedWindow)return "excluded";
        uint process;GetWindowThreadProcessId(hwnd,out process);
        var cls=new System.Text.StringBuilder(128);GetClassName(hwnd,cls,128);
        try{return "other/"+Process.GetProcessById((int)process).ProcessName+"/"+hwnd.ToInt64()+"/"+cls;}catch{return "other/"+hwnd.ToInt64();}
    }
    private static string DesktopName(IntPtr handle){if(handle==IntPtr.Zero)return "unavailable("+Marshal.GetLastWin32Error()+")";var s=new System.Text.StringBuilder(256);uint needed;return GetUserObjectInformation(handle,2,s,512,out needed)?s.ToString():"error("+Marshal.GetLastWin32Error()+")";}
    private static string InputDesktopName(){IntPtr d=OpenInputDesktop(0,false,1);try{return DesktopName(d);}finally{if(d!=IntPtr.Zero)CloseDesktop(d);}}
    private static void Pump(double seconds)
    { var frame = new DispatcherFrame(); var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(seconds) }; timer.Tick += delegate { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { internal uint Size; internal int Width, Height; internal ushort Planes, BitCount; internal uint Compression, SizeImage; internal int XPels, YPels; internal uint Used, Important; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr order, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { internal int X,Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder name,int max);
    [DllImport("user32.dll",SetLastError=true)]private static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll")]private static extern IntPtr GetThreadDesktop(uint id);
    [DllImport("kernel32.dll")]private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool GetUserObjectInformation(IntPtr handle,int index,System.Text.StringBuilder value,uint size,out uint needed);
    [DllImport("user32.dll")]private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("dwmapi.dll")]private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint use, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr bitmap);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
}
