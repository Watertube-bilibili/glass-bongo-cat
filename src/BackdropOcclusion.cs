using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace GlassBongoCat
{
    /// <summary>
    /// Read-only detection of capture-protected windows below the pet. These
    /// rectangles describe unavailable pixels, never a way to capture them.
    /// UI-thread use only. Window geometry is cached for 50 ms, independently of
    /// the lens position; moving the lens recomputes intersections every call.
    /// </summary>
    internal sealed class BackdropOcclusion
    {
        private const double RefreshMilliseconds = 50;
        private const int MaximumPieces = 256;
        private readonly List<WindowInfo> windows = new List<WindowInfo>(32);
        private readonly List<Rect> protectedRegions = new List<Rect>(16);
        private readonly List<Rect> opaqueRegions = new List<Rect>(16);
        private readonly List<Rect> pieces = new List<Rect>(16);
        private readonly List<Rect> nextPieces = new List<Rect>(16);
        private readonly List<Rect> intersections = new List<Rect>(16);
        private readonly ReadOnlyCollection<Rect> result;
        private readonly Func<IntPtr, List<WindowInfo>, bool> readWindows;
        private readonly Func<double> clock;
        private IntPtr owner;
        private double refreshedAt = Double.NegativeInfinity;

        // The injectable reader keeps geometry/order tests independent of the
        // user's desktop. Production uses only the query APIs below.
        internal struct WindowInfo
        {
            internal IntPtr Handle;
            internal Rect Bounds;
            internal bool Visible, Minimized, Cloaked, AffinityKnown, Opaque;
            internal uint Affinity;
        }

        internal BackdropOcclusion() : this(ReadNativeWindows, Now) { }
        internal BackdropOcclusion(Func<IntPtr, List<WindowInfo>, bool> reader, Func<double> time)
        {
            if (reader == null || time == null) throw new ArgumentNullException();
            readWindows = reader; clock = time; result = intersections.AsReadOnly();
        }

        /// <summary>
        /// Physical screen-pixel intersections, clipped to the current lens.
        /// Borrowed read-only list: consume before the next GetRegions call.
        /// No native resources are retained; no Dispose is required.
        /// </summary>
        internal IList<Rect> GetRegions(IntPtr petHwnd, Rect lensScreenBounds)
        {
            intersections.Clear();
            if (petHwnd == IntPtr.Zero || !Valid(lensScreenBounds)) return result;
            double now = clock();
            if (owner != petHwnd || now - refreshedAt >= RefreshMilliseconds || now < refreshedAt)
            {
                owner = petHwnd; refreshedAt = now;
                windows.Clear(); protectedRegions.Clear(); opaqueRegions.Clear();
                if (readWindows(petHwnd, windows)) BuildRegions(petHwnd);
            }
            for (int i = 0; i < protectedRegions.Count; i++)
            {
                Rect overlap = Rect.Intersect(protectedRegions[i], lensScreenBounds);
                if (Valid(overlap)) intersections.Add(overlap);
            }
            return result;
        }

        internal void Invalidate() { refreshedAt = Double.NegativeInfinity; }

        private void BuildRegions(IntPtr petHwnd)
        {
            bool below = false;
            for (int i = 0; i < windows.Count; i++)
            {
                WindowInfo window = windows[i];
                if (window.Handle == petHwnd) { below = true; continue; }
                if (!below || window.Handle == IntPtr.Zero || !window.Visible || window.Minimized || window.Cloaked || !Valid(window.Bounds)) continue;
                if (window.AffinityKnown && window.Affinity != 0)
                {
                    pieces.Clear(); pieces.Add(window.Bounds);
                    bool tooComplex = false;
                    for (int blocker = 0; blocker < opaqueRegions.Count && pieces.Count != 0; blocker++)
                    {
                        nextPieces.Clear();
                        for (int piece = 0; piece < pieces.Count; piece++)
                        {
                            Subtract(pieces[piece], opaqueRegions[blocker], nextPieces);
                            if (nextPieces.Count > MaximumPieces) { tooComplex = true; break; }
                        }
                        if (tooComplex) break;
                        pieces.Clear(); pieces.AddRange(nextPieces);
                    }
                    // Extremely fragmented geometry falls back conservatively:
                    // preserve the protected area rather than show wrong pixels.
                    if (tooComplex) protectedRegions.Add(window.Bounds);
                    else protectedRegions.AddRange(pieces);
                }
                else if (window.Opaque) opaqueRegions.Add(window.Bounds);
            }
        }

        private static void Subtract(Rect source, Rect cover, List<Rect> output)
        {
            Rect cut = Rect.Intersect(source, cover);
            if (!Valid(cut)) { output.Add(source); return; }
            Add(output, source.Left, source.Top, source.Width, cut.Top - source.Top);
            Add(output, source.Left, cut.Bottom, source.Width, source.Bottom - cut.Bottom);
            Add(output, source.Left, cut.Top, cut.Left - source.Left, cut.Height);
            Add(output, cut.Right, cut.Top, source.Right - cut.Right, cut.Height);
        }
        private static void Add(List<Rect> output, double x, double y, double width, double height)
        { if (width > 0 && height > 0) output.Add(new Rect(x, y, width, height)); }
        private static bool Valid(Rect bounds)
        { return !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 && Finite(bounds.Left) && Finite(bounds.Top) && Finite(bounds.Right) && Finite(bounds.Bottom); }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Now() { return Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency); }

        private static bool ReadNativeWindows(IntPtr petHwnd, List<WindowInfo> output)
        {
            bool found = false;
            bool complete = EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
            {
                // EnumWindows traverses top-level windows in z order. Above-pet
                // windows must never make its own material become transparent.
                if (hwnd == petHwnd) { found = true; output.Add(new WindowInfo { Handle = hwnd }); return true; }
                if (!found || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return true;
                int cloaked;
                if (DwmGetWindowAttribute(hwnd, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                NativeRect raw;
                if (!GetWindowRect(hwnd, out raw) || raw.Right <= raw.Left || raw.Bottom <= raw.Top) return true;
                Rect bounds = new Rect(raw.Left, raw.Top, (double)raw.Right - raw.Left, (double)raw.Bottom - raw.Top);
                uint affinity;
                bool affinityKnown = GetWindowDisplayAffinity(hwnd, out affinity);
                long style = IntPtr.Size == 8 ? GetWindowLongPtr(hwnd, -20).ToInt64() : GetWindowLong(hwnd, -20);
                // Layered / transparent / non-redirection windows have unknown
                // per-pixel coverage and cannot safely hide a lower protected one.
                bool opaque = (style & (0x00080000L | 0x20L | 0x00200000L)) == 0;
                output.Add(new WindowInfo { Handle = hwnd, Bounds = bounds, Visible = true, Minimized = false, Cloaked = false,
                    AffinityKnown = affinityKnown, Affinity = affinity, Opaque = opaque });
                return true;
            }, IntPtr.Zero);
            return complete && found;
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { internal int Left, Top, Right, Bottom; }
        private delegate bool EnumWindowCallback(IntPtr hwnd, IntPtr data);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect bounds);
        [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    }
}
