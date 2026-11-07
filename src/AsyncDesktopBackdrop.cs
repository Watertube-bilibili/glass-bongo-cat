// Native desktop sampler built for GlassPet: a latest-request mailbox, worker-
// owned GDI resources, double-buffered publication and an isolated UI snapshot.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace GlassBongoCat
{
    /// <summary>
    /// UI-thread Request/TryGetSnapshot/Stop; one bounded worker owns all GDI work.
    /// Requests overwrite one mailbox, so dragging never builds a capture backlog.
    /// TryGetSnapshot copies a newly published frame into one UI-owned reusable
    /// snapshot. That snapshot remains valid until the next successful call.
    /// Neither the capture worker nor Stop ever changes its pixels.
    /// </summary>
    internal sealed class AsyncDesktopBackdrop : IDisposable
    {
        private const int Padding = 96, MaximumPixels = 2 * 1024 * 1024;
        private const uint ExcludeFromCapture = 0x11, CaptureLayered = 0x40CC0020;
        private readonly object gate = new object();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        private readonly uint creatingThread = GetCurrentThreadId();
        private Thread worker;
        private IntPtr nativeOwner;
        private bool nativeAffinityApplied;
        private uint nativePreviousAffinity;
        private bool disposed, active, hasPending, releasePending;
        private int generation;
        private CaptureRequest pending;
        private BackdropFrame published, snapshot;
        private long publishedSequence, readSequence, capturedFrames;
        private long nativeBufferCreations;
        private double publishedAt, lastCaptureMilliseconds, lastBlitMilliseconds, lastFlushMilliseconds, lastCopyMilliseconds, lastValidationMilliseconds;
        private double lastPrecheckMilliseconds, lastPostcheckMilliseconds, lastBufferMilliseconds, lastPublishWaitMilliseconds;
        private string failureReason = "";
        private double sessionCheckedAt = Double.NegativeInfinity;
        private bool sessionAvailable;
        private string sessionReason = "";
#if GLASSPET_CAPTURE_TESTING
        internal int NativeBuffersActive, NativeBuffersCreated, PixelArraysCreated;
#endif

        internal long CapturedFrames { get { return Interlocked.Read(ref capturedFrames); } }
        internal double LastCaptureMilliseconds { get { lock (gate) return lastCaptureMilliseconds; } }
        internal double LastBlitMilliseconds { get { lock (gate) return lastBlitMilliseconds; } }
        internal double LastFlushMilliseconds { get { lock (gate) return lastFlushMilliseconds; } }
        internal double LastCopyMilliseconds { get { lock (gate) return lastCopyMilliseconds; } }
        internal double LastValidationMilliseconds { get { lock (gate) return lastValidationMilliseconds; } }
        internal double LastPrecheckMilliseconds { get { lock (gate) return lastPrecheckMilliseconds; } }
        internal double LastPostcheckMilliseconds { get { lock (gate) return lastPostcheckMilliseconds; } }
        internal double LastBufferMilliseconds { get { lock (gate) return lastBufferMilliseconds; } }
        internal double LastPublishWaitMilliseconds { get { lock (gate) return lastPublishWaitMilliseconds; } }
        internal long NativeBufferCreations { get { return Interlocked.Read(ref nativeBufferCreations); } }
        internal string FailureReason { get { lock (gate) return failureReason; } }

        private struct CaptureRequest
        {
            internal IntPtr Window;
            internal int Generation, Left, Top, Width, Height;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal uint Size;
            internal int Width, Height;
            internal ushort Planes, BitCount;
            internal uint Compression, SizeImage;
            internal int XPels, YPels;
            internal uint Used, Important;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int metric);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
        [DllImport("dwmapi.dll")] private static extern int DwmIsCompositionEnabled(out bool enabled);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr pixels, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr destination, int x, int y, int width, int height, IntPtr source, int sourceX, int sourceY, uint operation);

        private sealed class NativeBuffer : IDisposable
        {
            internal IntPtr Screen, Memory, Bitmap, Previous, Pixels;
            internal int Width, Height;
            internal BackdropFrame Capture;
            internal bool Counted;
            public void Dispose()
            {
                if (Memory != IntPtr.Zero && Previous != IntPtr.Zero && Previous != new IntPtr(-1)) SelectObject(Memory, Previous);
                if (Bitmap != IntPtr.Zero) DeleteObject(Bitmap);
                if (Memory != IntPtr.Zero) DeleteDC(Memory);
                if (Screen != IntPtr.Zero) ReleaseDC(IntPtr.Zero, Screen);
                Screen = Memory = Bitmap = Previous = Pixels = IntPtr.Zero;
                Capture = null;
            }
        }

        /// <summary>
        /// Native HWND entry point. The HWND must belong to this instance's creating
        /// UI thread. The caller calls Stop before hiding/destroying its HWND.
        /// Existing capture affinity is preserved and restored by Stop.
        /// </summary>
        internal bool Request(IntPtr hwnd, Rect lensScreenBounds)
        {
            if (!dispatcher.CheckAccess()) return Fail("背景采样请求必须来自创建它的 UI 线程。");
            lock (gate) if (disposed) return false;
            if (hwnd == IntPtr.Zero || !IsWindow(hwnd) || !IsWindowVisible(hwnd))
            { Stop(); return Fail("桌宠窗口未显示，背景采样已暂停。"); }
            if (Clock - sessionCheckedAt >= 250)
            {
                sessionAvailable = DesktopSessionState.QueryActive(out sessionReason);
                sessionCheckedAt = Clock;
            }
            if (!sessionAvailable) { Stop(); return Fail(sessionReason); }
            uint process;
            if (GetWindowThreadProcessId(hwnd, out process) != creatingThread)
                return Fail("只能采样由同一 UI 线程创建的桌宠窗口。");
            if (!NativeCaptureSupport.IsSupported) return Fail(NativeCaptureSupport.FailureReason);
            if (lensScreenBounds.IsEmpty || !Finite(lensScreenBounds.X) || !Finite(lensScreenBounds.Y) ||
                !Finite(lensScreenBounds.Width) || !Finite(lensScreenBounds.Height) || !Finite(lensScreenBounds.Right) ||
                !Finite(lensScreenBounds.Bottom) || lensScreenBounds.Width <= 0 || lensScreenBounds.Height <= 0)
                return Fail("背景采样区域无效。");
            if (nativeOwner != hwnd)
            {
                Stop();
                nativeOwner = hwnd;
            }
            if (!ApplyNativeAffinity())
            {
                string error = FailureReason;
                Stop();
                return Fail(error);
            }
            return QueueRequest(hwnd, lensScreenBounds);
        }

        private bool ApplyNativeAffinity()
        {
            bool composed;
            uint current;
            if (DwmIsCompositionEnabled(out composed) != 0 || !composed)
                return Fail("桌面合成不可用，已暂停背景捕获。");
            if (!GetWindowDisplayAffinity(nativeOwner, out current)) return Fail("无法读取桌宠捕获状态。");
            if (nativeAffinityApplied && current == ExcludeFromCapture) return true;
            if (!nativeAffinityApplied) nativePreviousAffinity = current;
            if (!SetWindowDisplayAffinity(nativeOwner, ExcludeFromCapture)) return Fail("系统无法排除桌宠自身。");
            nativeAffinityApplied = true;
            if (!GetWindowDisplayAffinity(nativeOwner, out current) || current != ExcludeFromCapture)
                return Fail("系统未确认桌宠捕获排除，已暂停背景捕获。");
            // One startup/repair barrier, never a steady-state frame operation.
            // Ensure the exclusion reaches DWM before the worker samples pixels.
            if (DwmFlush() != 0) return Fail("桌宠捕获排除尚未完成合成。");
            return true;
        }

        private bool QueueRequest(IntPtr handle, Rect lensScreenBounds)
        {

            int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77), vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
            Rect sample = SamplingBounds(lensScreenBounds, new Rect(vx, vy, Math.Max(0, vw), Math.Max(0, vh)));
            double left = sample.Left, top = sample.Top, right = sample.Right, bottom = sample.Bottom;
            if (vw <= 0 || vh <= 0 || right <= left || bottom <= top || (right - left) * (bottom - top) > MaximumPixels)
                return Fail("背景采样区域超出屏幕或 200 万像素限制。");
            lock (gate)
            {
                if (disposed) return false;
                active = true;
                pending = new CaptureRequest { Window = handle, Generation = generation,
                    Left = (int)left, Top = (int)top, Width = (int)(right - left), Height = (int)(bottom - top) };
                hasPending = true;
                if (worker == null)
                {
                    worker = new Thread(WorkerLoop);
                    worker.IsBackground = true;
                    worker.Name = "Glass Bongo Cat desktop refraction";
                    worker.Start();
                }
                wake.Set();
            }
            return true;
        }

        internal static Rect SamplingBounds(Rect lens, Rect desktop)
        {
            // A constant fractional-sized pet must not alternate its DIB width
            // whenever it crosses a pixel center. Keep the padded extent fixed
            // and only clip it when the desktop edge is actually reached.
            double x = Math.Floor(lens.Left) - Padding, y = Math.Floor(lens.Top) - Padding;
            double width = Math.Ceiling(lens.Width) + 1 + 2 * Padding;
            double height = Math.Ceiling(lens.Height) + 1 + 2 * Padding;
            return Rect.Intersect(new Rect(x, y, width, height), desktop);
        }

        internal bool TryGetSnapshot(out BackdropFrame frame)
        {
            frame = null;
            if (!dispatcher.CheckAccess() || nativeOwner == IntPtr.Zero) return false;
            lock (gate)
            {
                if (disposed || !active || published == null || readSequence == publishedSequence || Clock - publishedAt > 250) return false;
                if (snapshot == null || snapshot.Width != published.Width || snapshot.Height != published.Height)
                    snapshot = CreateFrame(published.Width, published.Height);
                // Worker holds this lock only to exchange two frame references;
                // BitBlt, GdiFlush and Marshal.Copy never hold it.
                Buffer.BlockCopy(published.Pixels, 0, snapshot.Pixels, 0, published.Pixels.Length);
                snapshot.ScreenBounds = published.ScreenBounds;
                readSequence = publishedSequence;
                frame = snapshot;
                return true;
            }
        }

        internal void Stop()
        {
            if (!dispatcher.CheckAccess())
                throw new InvalidOperationException("背景采样必须从窗口 UI 线程停止。");
            lock (gate)
            {
                generation++;
                active = hasPending = false;
                releasePending = true;
                published = null;
                snapshot = null;
                readSequence = publishedSequence;
                if (!disposed) wake.Set();
            }
            // Incrementing the generation first prevents an in-flight capture from
            // publishing after affinity is restored, including during window close.
            if (nativeAffinityApplied)
            {
                if (!IsWindow(nativeOwner) || SetWindowDisplayAffinity(nativeOwner, nativePreviousAffinity)) nativeAffinityApplied = false;
                else Fail("无法恢复桌宠原捕获状态。");
            }
        }

        public void Dispose()
        {
            Stop();
            nativeOwner = IntPtr.Zero;
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                if (worker == null) wake.Dispose();
                else wake.Set();
            }
            // Do not Join here: an in-flight BitBlt must never stall the UI.
            // The background worker releases its own DCs and event in finally.
        }

        private void WorkerLoop()
        {
            NativeBuffer buffer = null;
            try
            {
                while (true)
                {
                    wake.WaitOne();
                    while (true)
                    {
                        CaptureRequest request = new CaptureRequest();
                        bool stop, work;
                        lock (gate)
                        {
                            if (disposed) return;
                            stop = !active || releasePending;
                            releasePending = false;
                            work = active && hasPending;
                            if (work) { request = pending; hasPending = false; }
                        }
                        if (stop) ReleaseBuffer(ref buffer);
                        if (!work) break;
                        try
                        {
                            Capture(request, ref buffer);
                        }
                        catch (OutOfMemoryException) { ReleaseBuffer(ref buffer); WorkerFailure(request, "背景采样内存不足，暂时显示清透玻璃。"); }
                        catch (Exception error) { ReleaseBuffer(ref buffer); WorkerFailure(request, "后台采样失败：" + error.Message); }
                    }
                }
            }
            finally { ReleaseBuffer(ref buffer); wake.Dispose(); }
        }

        private void Capture(CaptureRequest request, ref NativeBuffer buffer)
        {
            double started = Clock;
            if (!CaptureAllowed(request.Window)) { WorkerFailure(request, "浮窗捕获排除不可用，已跳过背景帧。"); return; }
            double firstValidated = Clock;
            if (buffer == null || buffer.Width != request.Width || buffer.Height != request.Height)
            {
                ReleaseBuffer(ref buffer);
                buffer = new NativeBuffer { Width = request.Width, Height = request.Height };
                buffer.Screen = GetDC(IntPtr.Zero);
                if (buffer.Screen == IntPtr.Zero) throw new InvalidOperationException("无法访问桌面");
                buffer.Memory = CreateCompatibleDC(buffer.Screen);
                if (buffer.Memory == IntPtr.Zero) throw new InvalidOperationException("无法创建采样缓冲区");
                BitmapInfo info = new BitmapInfo { Size = 40, Width = request.Width, Height = -request.Height, Planes = 1, BitCount = 32 };
                buffer.Bitmap = CreateDIBSection(buffer.Screen, ref info, 0, out buffer.Pixels, IntPtr.Zero, 0);
                if (buffer.Bitmap == IntPtr.Zero || buffer.Pixels == IntPtr.Zero) throw new InvalidOperationException("无法分配采样像素");
                buffer.Previous = SelectObject(buffer.Memory, buffer.Bitmap);
                if (buffer.Previous == IntPtr.Zero || buffer.Previous == new IntPtr(-1)) throw new InvalidOperationException("无法选择采样缓冲区");
                buffer.Counted = true;
                Interlocked.Increment(ref nativeBufferCreations);
#if GLASSPET_CAPTURE_TESTING
                Interlocked.Increment(ref NativeBuffersCreated); Interlocked.Increment(ref NativeBuffersActive);
#endif
            }
            if (buffer.Capture == null || buffer.Capture.Width != request.Width || buffer.Capture.Height != request.Height)
                buffer.Capture = CreateFrame(request.Width, request.Height);
            double beforeBlit = Clock;
            if (!BitBlt(buffer.Memory, 0, 0, request.Width, request.Height, buffer.Screen, request.Left, request.Top, CaptureLayered))
                throw new InvalidOperationException("桌面像素复制失败");
            double afterBlit = Clock;
            if (!GdiFlush()) throw new InvalidOperationException("桌面像素复制未完成");
            double afterFlush = Clock;
            if (!CaptureAllowed(request.Window)) { WorkerFailure(request, "捕获状态已改变，已丢弃背景帧。"); return; }
            double afterValidation = Clock;
            byte[] pixels = buffer.Capture.Pixels;
            Marshal.Copy(buffer.Pixels, pixels, 0, pixels.Length);
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            double afterCopy = Clock;
            buffer.Capture.ScreenBounds = new Rect(request.Left, request.Top, request.Width, request.Height);
            lock (gate)
            {
                if (disposed || !active || request.Generation != generation) return;
                BackdropFrame old = published;
                published = buffer.Capture;
                buffer.Capture = old;
                publishedSequence++;
                publishedAt = Clock;
                lastCaptureMilliseconds = publishedAt - started;
                lastBlitMilliseconds = afterBlit - beforeBlit;
                lastFlushMilliseconds = afterFlush - afterBlit;
                lastCopyMilliseconds = afterCopy - afterValidation;
                lastValidationMilliseconds = firstValidated - started + afterValidation - afterFlush;
                lastPrecheckMilliseconds = firstValidated - started;
                lastPostcheckMilliseconds = afterValidation - afterFlush;
                lastBufferMilliseconds = beforeBlit - firstValidated;
                lastPublishWaitMilliseconds = publishedAt - afterCopy;
                failureReason = "";
                Interlocked.Increment(ref capturedFrames);
            }
        }

        private BackdropFrame CreateFrame(int width, int height)
        {
#if GLASSPET_CAPTURE_TESTING
            Interlocked.Increment(ref PixelArraysCreated);
#endif
            return new BackdropFrame(width, height);
        }
        private void ReleaseBuffer(ref NativeBuffer buffer)
        {
            if (buffer == null) return;
#if GLASSPET_CAPTURE_TESTING
            if (buffer.Counted) Interlocked.Decrement(ref NativeBuffersActive);
#endif
            buffer.Dispose();
            buffer = null;
        }
        private void WorkerFailure(CaptureRequest request, string reason)
        {
            lock (gate) if (!disposed && active && request.Generation == generation) failureReason = reason;
        }
        private bool Fail(string reason) { lock (gate) failureReason = reason; return false; }
        private static bool CaptureAllowed(IntPtr handle)
        {
            bool composed;
            uint affinity;
            return handle != IntPtr.Zero && IsWindowVisible(handle) &&
                DwmIsCompositionEnabled(out composed) == 0 && composed &&
                GetWindowDisplayAffinity(handle, out affinity) && affinity == ExcludeFromCapture;
        }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Clock { get { return Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency); } }
    }
}
