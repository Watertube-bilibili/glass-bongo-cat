using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using GlassBongoCat;

// Manual comparative benchmark, deliberately excluded from build.ps1. It creates
// only its own nearly transparent, click-through, non-activating native window.
// No global input, screen image files, application settings or singleton paths.
internal static class FramePerformanceTests
{
    private const double WarmupSeconds = 2, MeasureSeconds = 6, TestOpacity = .001;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [STAThread]
    private static int Main(string[] args)
    {
        Application app = null;
        try
        {
            if (args.Length != 2) throw new ArgumentException("Expected version label and output CSV path.");
            string reason;
            if (!DesktopSessionState.QueryActive(out reason))
            {
                Console.Error.WriteLine("SKIP: active desktop required; " + reason);
                return 2;
            }
            if (SystemParameters.HighContrast)
            {
                Console.Error.WriteLine("SKIP: high contrast disables real background refraction.");
                return 2;
            }
            app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            List<string> lines = new List<string>();
            lines.Add("version,size_logical,dpi_scale,pixel_width,pixel_height,warmup_seconds,elapsed_seconds,presented_frames,present_fps,full_frame_mean_ms,full_frame_p95_ms,full_frame_max_ms,captured_frames,capture_fps,refraction_checks,refraction_missing,managed_bytes_before,managed_bytes_after,private_bytes_before,private_bytes_after,gc0,gc1,gc2,opacity,status");
            bool valid = true;
            foreach (int size in new int[] { 200, 300 })
            {
                string line;
                bool result = Measure(args[0], size, out line);
                valid &= result;
                lines.Add(line);
                File.WriteAllLines(args[1], lines.ToArray(), new System.Text.UTF8Encoding(false));
                Console.WriteLine(line);
            }
            return valid ? 0 : 3;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (app != null) app.Shutdown(); }
    }

    private static bool Measure(string label, int size, out string line)
    {
        CatWindow cat = null;
        Stopwatch watch = Stopwatch.StartNew();
        DispatcherFrame pump = new DispatcherFrame();
        DispatcherTimer timeout = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(13) };
        List<double> costs = new List<double>(420);
        bool started = false, completed = false, activated = false;
        long firstFrames = 0, previousFrames = 0, lastFrames = 0, firstCaptures = 0, captures = 0;
        double firstCost = 0, previousCost = 0, lastCost = 0, startedAt = 0, elapsed = 0;
        int missing = 0, checks = 0, gc0 = 0, gc1 = 0, gc2 = 0;
        long memoryBefore = 0, memoryAfter = 0, privateBefore = 0, privateAfter = 0;
        double dpi = 1;
        string status = "TIMEOUT";
        try
        {
            CatOptions options = new CatOptions { Size = size, InputEnabled = false, ClickThrough = true, FollowCursor = false };
            cat = new CatWindow(options) { DiagnosticMode = true, Opacity = TestOpacity };
            Rect work = SystemParameters.WorkArea;
            cat.Left = work.Left + Math.Max(0, (work.Width - cat.Width) * .35);
            cat.Top = work.Top + Math.Max(0, (work.Height - cat.Height) * .45);
            cat.Show();
            dpi = cat.DpiScale;
            // Use the primary physical work area converted with the actual system DPI.
            System.Drawing.Rectangle physical = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            work = new Rect(physical.Left / dpi, physical.Top / dpi, physical.Width / dpi, physical.Height / dpi);
            double rangeX = Math.Max(0, work.Width - cat.Width - 24);
            double rangeY = Math.Max(0, work.Height - cat.Height - 24);
            double centerX = work.Left + 12 + rangeX * .5;
            double centerY = work.Top + 12 + rangeY * .5;
            double radiusX = Math.Min(220, rangeX * .42), radiusY = Math.Min(120, rangeY * .3);
            watch.Restart();
            cat.BeforeRender = delegate(double ignored)
            {
                double now = watch.Elapsed.TotalSeconds;
                activated |= GetForegroundWindow() == cat.Handle;
                if (!started && now >= WarmupSeconds)
                {
                    started = true; startedAt = now;
                    firstFrames = previousFrames = cat.RenderedFrames;
                    firstCost = previousCost = cat.TotalRenderMilliseconds;
                    firstCaptures = cat.Surface.CapturedFrames;
                    memoryBefore = GC.GetTotalMemory(false);
                    using (Process process = Process.GetCurrentProcess()) privateBefore = process.PrivateMemorySize64;
                    gc0 = GC.CollectionCount(0); gc1 = GC.CollectionCount(1); gc2 = GC.CollectionCount(2);
                }
                else if (started && !completed)
                {
                    long count = cat.RenderedFrames - previousFrames;
                    // BeforeRender observes the immediately preceding completed frame.
                    // Exactly one frame must have completed between adjacent callbacks.
                    if (count == 1) costs.Add(cat.TotalRenderMilliseconds - previousCost);
                    else throw new InvalidOperationException("Unexpected completed-frame delta: " + count);
                    previousFrames = cat.RenderedFrames; previousCost = cat.TotalRenderMilliseconds;
                    checks++; if (!cat.Surface.HasRefraction) missing++;
                    if (now - startedAt >= MeasureSeconds)
                    {
                        completed = true; elapsed = now - startedAt;
                        lastFrames = cat.RenderedFrames; lastCost = cat.TotalRenderMilliseconds;
                        captures = cat.Surface.CapturedFrames - firstCaptures;
                        memoryAfter = GC.GetTotalMemory(false);
                        using (Process process = Process.GetCurrentProcess()) privateAfter = process.PrivateMemorySize64;
                        gc0 = GC.CollectionCount(0) - gc0; gc1 = GC.CollectionCount(1) - gc1; gc2 = GC.CollectionCount(2) - gc2;
                        status = !activated && missing == 0 && captures > 0 ? "VALID" : "INVALID";
                        cat.BeforeRender = null;
                        // Finish after this callback returns; the final in-flight frame is
                        // intentionally excluded from the recorded completed-frame totals.
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, new Action(delegate { pump.Continue = false; }));
                        return;
                    }
                }
                cat.Left = centerX + Math.Sin(now * Math.PI * 2 / 4) * radiusX;
                cat.Top = centerY + Math.Cos(now * Math.PI * 2 / 3) * radiusY;
                int phase = ((int)(now / .125)) & 3;
                cat.Surface.TestLeft = (phase & 1) != 0;
                cat.Surface.TestRight = (phase & 2) != 0;
            };
            timeout.Tick += delegate { timeout.Stop(); pump.Continue = false; };
            timeout.Start(); Dispatcher.PushFrame(pump);
            if (!completed)
            {
                string reason; DesktopSessionState.QueryActive(out reason);
                Console.Error.WriteLine("Incomplete sample; session=" + reason + "; capture=" + cat.Surface.CaptureStatus);
            }
        }
        finally { timeout.Stop(); if (cat != null) { cat.BeforeRender = null; cat.Dispose(); } }
        costs.Sort();
        long frames = lastFrames - firstFrames;
        double mean = frames > 0 ? (lastCost - firstCost) / frames : 0;
        double p95 = costs.Count == 0 ? 0 : costs[Math.Max(0, (int)Math.Ceiling(costs.Count * .95) - 1)];
        double maximum = costs.Count == 0 ? 0 : costs[costs.Count - 1];
        line = String.Join(",", new string[] { Csv(label), N(size), N(dpi), N(Math.Ceiling(size * dpi)),
            N(Math.Ceiling(size * CatSurface.DesignHeight / CatSurface.DesignWidth * dpi)), N(WarmupSeconds), N(elapsed), N(frames),
            N(elapsed > 0 ? frames / elapsed : 0), N(mean), N(p95), N(maximum), N(captures), N(elapsed > 0 ? captures / elapsed : 0),
            N(checks), N(missing), N(memoryBefore), N(memoryAfter), N(privateBefore), N(privateAfter), N(gc0), N(gc1), N(gc2), N(TestOpacity), status });
        return status == "VALID";
    }
    private static string N(double value) { return value.ToString("0.000", Invariant); }
    private static string Csv(string value) { return "\"" + value.Replace("\"", "\"\"") + "\""; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
